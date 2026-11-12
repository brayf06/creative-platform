using Microsoft.EntityFrameworkCore;

namespace CreativeIntelligence.Web.Services.Strategy;

/// <summary>
/// Briefs de campaña de la organización activa. Un brief pertenece a una marca y no puede contradecirla: su
/// producto tiene que ser de esa marca y ninguno de sus textos puede contener un mensaje que la marca prohíbe.
/// </summary>
/// <param name="db">Contexto de datos.</param>
/// <param name="brands">Marcas, para validar los mensajes.</param>
/// <param name="audit">Auditoría.</param>
/// <param name="current">Usuario y organización que operan.</param>
public sealed class BriefService(AppDbContext db, BrandService brands, AuditService audit, CurrentContext current)
{
    /// <summary>Lista los briefs de la organización activa, del más nuevo al más viejo. No escribe.</summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Hasta 200 briefs.</returns>
    public async Task<List<BriefListItem>> ListAsync(CancellationToken cancellationToken)
    {
        return await db.CampaignBriefs.AsNoTracking()
            .OrderByDescending(c => c.Id)
            .Take(200)
            .Select(c => new BriefListItem(c.Id, c.Name, c.Brand!.Name, c.Occasion, c.VideoCount, c.Platform, c.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Devuelve un brief con todos sus campos. No escribe.</summary>
    /// <param name="id">Brief.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>El brief.</returns>
    /// <exception cref="BusinessException">404 <c>BRIEF_NOT_FOUND</c> si no existe en la organización activa.</exception>
    public async Task<BriefDetail> GetAsync(long id, CancellationToken cancellationToken)
    {
        return await db.CampaignBriefs.AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new BriefDetail
            {
                Id = c.Id,
                BrandId = c.BrandId,
                BrandName = c.Brand!.Name,
                Name = c.Name,
                OriginalText = c.OriginalText,
                ProductId = c.ProductId,
                ProductName = db.BrandItems.Where(i => i.Id == c.ProductId).Select(i => i.Text).FirstOrDefault(),
                Occasion = c.Occasion,
                Audience = c.Audience,
                Objective = c.Objective,
                Message = c.Message,
                Approaches = c.Approaches,
                DurationSeconds = c.DurationSeconds,
                VideoCount = c.VideoCount,
                Platform = c.Platform,
                UseExternalResearch = c.UseExternalResearch,
                MaxGenerationBudgetUsd = c.MaxGenerationBudgetUsd,
                Autonomy = c.Autonomy,
                CreatedAt = c.CreatedAt,
            })
            .FirstOrDefaultAsync(cancellationToken) ?? throw NotFound();
    }

    /// <summary>Interpreta el texto libre de un brief para proponer sus campos. No lee ni escribe en la base.</summary>
    /// <param name="text">Lo que escribió la persona.</param>
    /// <returns>Lo que se pudo deducir.</returns>
    public BriefInterpretation Interpret(string? text)
    {
        return BriefTextParser.Parse(text);
    }

    /// <summary>Crea un brief. Escribe el brief y la auditoría.</summary>
    /// <param name="body">Campos del brief.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Identificador del brief.</returns>
    /// <exception cref="BusinessException">404 <c>BRAND_NOT_FOUND</c>; 400 <c>PRODUCT_INVALID</c> si el producto no es
    /// de esa marca; 409 <c>FORBIDDEN_MESSAGE</c> si algún texto contiene un mensaje que la marca prohíbe.</exception>
    public async Task<long> CreateAsync(BriefBody body, CancellationToken cancellationToken)
    {
        await ValidateAsync(body, cancellationToken);
        CampaignBrief brief = new() { CreatedByUserId = current.UserId };
        Apply(brief, body);
        return await db.InTransactionAsync(
            async () =>
            {
                db.CampaignBriefs.Add(brief);
                await db.SaveChangesAsync(cancellationToken);
                audit.Record("brief.create", nameof(CampaignBrief), brief.Id.ToString(), $"Creó el brief \"{brief.Name}\".");
                await db.SaveChangesAsync(cancellationToken);
                return brief.Id;
            },
            cancellationToken);
    }

    /// <summary>Cambia un brief. Escribe el brief y la auditoría.</summary>
    /// <param name="id">Brief.</param>
    /// <param name="body">Campos del brief.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <exception cref="BusinessException">404 <c>BRIEF_NOT_FOUND</c>, y los mismos que al crearlo.</exception>
    public async Task UpdateAsync(long id, BriefBody body, CancellationToken cancellationToken)
    {
        CampaignBrief brief = await db.CampaignBriefs.FirstOrDefaultAsync(c => c.Id == id, cancellationToken) ?? throw NotFound();
        await ValidateAsync(body, cancellationToken);
        Apply(brief, body);
        audit.Record("brief.update", nameof(CampaignBrief), id.ToString(), $"Cambió el brief \"{brief.Name}\".");
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task ValidateAsync(BriefBody body, CancellationToken cancellationToken)
    {
        if (!await db.Brands.AnyAsync(b => b.Id == body.BrandId, cancellationToken))
        {
            throw new BusinessException(404, "La marca no existe.", "BRAND_NOT_FOUND");
        }

        if (body.ProductId is not null
            && !await db.BrandItems.AnyAsync(
                i => i.Id == body.ProductId && i.BrandId == body.BrandId && i.Kind == BrandItemKind.Product, cancellationToken))
        {
            throw new BusinessException(400, "El producto elegido no es de esa marca.", "PRODUCT_INVALID");
        }

        List<string> forbidden = await brands.ForbiddenInAsync(
            body.BrandId, [body.OriginalText, body.Message, body.Approaches, body.Objective, body.Name], cancellationToken);
        if (forbidden.Count > 0)
        {
            throw new BusinessException(
                409, $"El brief contiene mensajes que la marca prohíbe: {string.Join("; ", forbidden.Select(f => $"\"{f}\""))}.", "FORBIDDEN_MESSAGE");
        }
    }

    private static void Apply(CampaignBrief brief, BriefBody body)
    {
        brief.BrandId = body.BrandId;
        brief.Name = body.Name.Trim();
        brief.OriginalText = Clean(body.OriginalText);
        brief.ProductId = body.ProductId;
        brief.Occasion = Clean(body.Occasion);
        brief.Audience = Clean(body.Audience);
        brief.Objective = Clean(body.Objective);
        brief.Message = Clean(body.Message);
        brief.Approaches = Clean(body.Approaches);
        brief.DurationSeconds = body.DurationSeconds;
        brief.VideoCount = body.VideoCount;
        brief.Platform = body.Platform;
        brief.UseExternalResearch = body.UseExternalResearch;
        brief.MaxGenerationBudgetUsd = body.MaxGenerationBudgetUsd;
        brief.Autonomy = body.Autonomy;
    }

    private static string? Clean(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static BusinessException NotFound()
    {
        return new BusinessException(404, "El brief no existe.", "BRIEF_NOT_FOUND");
    }
}

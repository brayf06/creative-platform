using Microsoft.EntityFrameworkCore;

namespace CreativeIntelligence.Web.Services.Strategy;

/// <summary>
/// Perfiles de marca de la organización activa: sus datos generales y sus listas de productos, beneficios,
/// públicos, objeciones y mensajes permitidos y prohibidos. Es el conocimiento que se reutiliza en cada campaña.
/// </summary>
/// <param name="db">Contexto de datos.</param>
/// <param name="audit">Auditoría.</param>
/// <param name="current">Usuario y organización que operan.</param>
public sealed class BrandService(AppDbContext db, AuditService audit, CurrentContext current)
{
    /// <summary>Lista las marcas de la organización activa. No escribe.</summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Marcas por nombre.</returns>
    public async Task<List<BrandListItem>> ListAsync(CancellationToken cancellationToken)
    {
        return await db.Brands.AsNoTracking()
            .OrderBy(b => b.Name)
            .Select(b => new BrandListItem(
                b.Id,
                b.Name,
                b.Items.Count(i => i.Kind == BrandItemKind.Product),
                b.Items.Count(i => i.Kind == BrandItemKind.ForbiddenMessage),
                db.CampaignBriefs.Count(c => c.BrandId == b.Id)))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Lista las marcas con sus productos, para el formulario de un brief. No escribe.</summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Marcas por nombre.</returns>
    public async Task<List<BrandOption>> OptionsAsync(CancellationToken cancellationToken)
    {
        return await db.Brands.AsNoTracking()
            .OrderBy(b => b.Name)
            .Select(b => new BrandOption(
                b.Id,
                b.Name,
                b.Items.Where(i => i.Kind == BrandItemKind.Product)
                    .OrderBy(i => i.Text)
                    .Select(i => new BrandItemView(i.Id, i.Kind, i.Text, i.Detail, i.MarginPercent))
                    .ToList()))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Devuelve el perfil completo de una marca. No escribe.</summary>
    /// <param name="id">Marca.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>El perfil.</returns>
    /// <exception cref="BusinessException">404 <c>BRAND_NOT_FOUND</c> si no existe en la organización activa.</exception>
    public async Task<BrandDetail> GetAsync(int id, CancellationToken cancellationToken)
    {
        return await db.Brands.AsNoTracking()
            .Where(b => b.Id == id)
            .Select(b => new BrandDetail(
                b.Id,
                b.Name,
                b.ValueProposition,
                b.CommunicationStyle,
                b.VisualIdentity,
                b.Objectives,
                b.CommercialRestrictions,
                b.MonthlyBudgetUsd,
                b.Items.OrderBy(i => i.Text).Select(i => new BrandItemView(i.Id, i.Kind, i.Text, i.Detail, i.MarginPercent)).ToList()))
            .FirstOrDefaultAsync(cancellationToken) ?? throw NotFound();
    }

    /// <summary>Crea una marca. Escribe la marca y la auditoría.</summary>
    /// <param name="body">Datos del perfil.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Identificador de la marca.</returns>
    /// <exception cref="BusinessException">409 <c>BRAND_EXISTS</c> si la organización ya tiene una con ese nombre.</exception>
    public async Task<int> CreateAsync(BrandBody body, CancellationToken cancellationToken)
    {
        string name = body.Name.Trim();
        await EnsureNameFreeAsync(name, null, cancellationToken);
        Brand brand = new() { CreatedByUserId = current.UserId };
        Apply(brand, body, name);
        return await db.InTransactionAsync(
            async () =>
            {
                db.Brands.Add(brand);
                await db.SaveChangesAsync(cancellationToken);
                audit.Record("brand.create", nameof(Brand), brand.Id.ToString(), $"Creó la marca \"{name}\".");
                await db.SaveChangesAsync(cancellationToken);
                return brand.Id;
            },
            cancellationToken);
    }

    /// <summary>Cambia los datos generales de una marca. Escribe la marca y la auditoría.</summary>
    /// <param name="id">Marca.</param>
    /// <param name="body">Datos del perfil.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <exception cref="BusinessException">404 <c>BRAND_NOT_FOUND</c>; 409 <c>BRAND_EXISTS</c> si el nombre ya lo usa
    /// otra marca.</exception>
    public async Task UpdateAsync(int id, BrandBody body, CancellationToken cancellationToken)
    {
        Brand brand = await db.Brands.FirstOrDefaultAsync(b => b.Id == id, cancellationToken) ?? throw NotFound();
        string name = body.Name.Trim();
        await EnsureNameFreeAsync(name, id, cancellationToken);
        Apply(brand, body, name);
        audit.Record("brand.update", nameof(Brand), id.ToString(), $"Cambió el perfil de la marca \"{name}\".");
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Agrega un elemento a una lista del perfil. Escribe el elemento y la auditoría.</summary>
    /// <param name="brandId">Marca.</param>
    /// <param name="body">Lista, texto, detalle y margen.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Identificador del elemento.</returns>
    /// <exception cref="BusinessException">404 <c>BRAND_NOT_FOUND</c>; 409 <c>BRAND_ITEM_EXISTS</c> si ese texto ya
    /// está en esa lista.</exception>
    public async Task<int> AddItemAsync(int brandId, BrandItemBody body, CancellationToken cancellationToken)
    {
        if (!await db.Brands.AnyAsync(b => b.Id == brandId, cancellationToken))
        {
            throw NotFound();
        }

        string text = body.Text.Trim();
        if (await db.BrandItems.AnyAsync(i => i.BrandId == brandId && i.Kind == body.Kind && i.Text == text, cancellationToken))
        {
            throw new BusinessException(409, $"\"{text}\" ya está en esa lista.", "BRAND_ITEM_EXISTS");
        }

        BrandItem item = new()
        {
            BrandId = brandId,
            Kind = body.Kind,
            Text = text,
            Detail = string.IsNullOrWhiteSpace(body.Detail) ? null : body.Detail.Trim(),
            MarginPercent = body.Kind == BrandItemKind.Product ? body.MarginPercent : null,
        };
        db.BrandItems.Add(item);
        audit.Record("brand.item.add", nameof(Brand), brandId.ToString(), $"Agregó \"{text}\" ({body.Kind}) al perfil.");
        await db.SaveChangesAsync(cancellationToken);
        return item.Id;
    }

    /// <summary>Quita un elemento de una lista del perfil. Escribe el borrado y la auditoría.</summary>
    /// <param name="itemId">Elemento.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>La marca a la que pertenecía.</returns>
    /// <exception cref="BusinessException">404 <c>BRAND_ITEM_NOT_FOUND</c>; 409 <c>PRODUCT_IN_USE</c> si es un
    /// producto que algún brief promociona.</exception>
    public async Task<int> RemoveItemAsync(int itemId, CancellationToken cancellationToken)
    {
        BrandItem item = await db.BrandItems.FirstOrDefaultAsync(i => i.Id == itemId, cancellationToken)
            ?? throw new BusinessException(404, "El elemento no existe.", "BRAND_ITEM_NOT_FOUND");
        if (await db.CampaignBriefs.AnyAsync(c => c.ProductId == itemId, cancellationToken))
        {
            throw new BusinessException(409, "Ese producto está en uso en un brief: no se puede quitar.", "PRODUCT_IN_USE");
        }

        db.BrandItems.Remove(item);
        audit.Record("brand.item.remove", nameof(Brand), item.BrandId.ToString(), $"Quitó \"{item.Text}\" ({item.Kind}) del perfil.");
        await db.SaveChangesAsync(cancellationToken);
        return item.BrandId;
    }

    /// <summary>Devuelve los mensajes prohibidos de una marca que aparecen en alguno de los textos. No escribe.</summary>
    /// <param name="brandId">Marca.</param>
    /// <param name="texts">Textos a revisar; los nulos se ignoran.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Frases prohibidas encontradas, sin repetir.</returns>
    public async Task<List<string>> ForbiddenInAsync(int brandId, IEnumerable<string?> texts, CancellationToken cancellationToken)
    {
        List<string> forbidden = await db.BrandItems.AsNoTracking()
            .Where(i => i.BrandId == brandId && i.Kind == BrandItemKind.ForbiddenMessage)
            .Select(i => i.Text)
            .ToListAsync(cancellationToken);
        return MessageRules.Violations(string.Join('\n', texts), forbidden);
    }

    private static void Apply(Brand brand, BrandBody body, string name)
    {
        brand.Name = name;
        brand.ValueProposition = Clean(body.ValueProposition);
        brand.CommunicationStyle = Clean(body.CommunicationStyle);
        brand.VisualIdentity = Clean(body.VisualIdentity);
        brand.Objectives = Clean(body.Objectives);
        brand.CommercialRestrictions = Clean(body.CommercialRestrictions);
        brand.MonthlyBudgetUsd = body.MonthlyBudgetUsd;
    }

    private static string? Clean(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private async Task EnsureNameFreeAsync(string name, int? exceptId, CancellationToken cancellationToken)
    {
        if (await db.Brands.AnyAsync(b => b.Name == name && b.Id != exceptId, cancellationToken))
        {
            throw new BusinessException(409, $"Ya existe una marca llamada \"{name}\".", "BRAND_EXISTS");
        }
    }

    private static BusinessException NotFound()
    {
        return new BusinessException(404, "La marca no existe.", "BRAND_NOT_FOUND");
    }
}

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CreativeIntelligence.Web.Services.Strategy;

/// <summary>Reglas de la investigación externa que no dependen de la base.</summary>
public static class ResearchRules
{
    /// <summary>
    /// Convierte lo que escribió una persona (un dominio o una dirección completa) en un dominio: sin protocolo,
    /// sin <c>www.</c>, sin ruta y en minúsculas.
    /// </summary>
    /// <param name="input">Dominio o dirección.</param>
    /// <returns>El dominio, o nulo si no parece uno.</returns>
    public static string? NormalizeDomain(string? input)
    {
        string text = (input ?? string.Empty).Trim().ToLowerInvariant();
        if (!text.Contains("://", StringComparison.Ordinal))
        {
            text = "https://" + text;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) || uri.HostNameType != UriHostNameType.Dns || !uri.Host.Contains('.'))
        {
            return null;
        }

        return uri.Host.StartsWith("www.", StringComparison.Ordinal) ? uri.Host[4..] : uri.Host;
    }

    /// <summary>
    /// Dice a qué dominio autorizado pertenece una dirección: el propio dominio o un subdominio suyo. Una dirección
    /// que no es <c>http</c> o <c>https</c>, o cuyo sitio sólo se parece a uno autorizado (<c>ejemplo.com.otro.net</c>,
    /// <c>miejemplo.com</c>), no pertenece a ninguno.
    /// </summary>
    /// <param name="url">Dirección de la referencia.</param>
    /// <param name="domains">Dominios autorizados.</param>
    /// <returns>El dominio autorizado, o nulo.</returns>
    public static string? MatchDomain(string? url, IEnumerable<string> domains)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out Uri? uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        string host = uri.Host.ToLowerInvariant();
        return domains.FirstOrDefault(d => host == d || host.EndsWith("." + d, StringComparison.Ordinal));
    }
}

/// <summary>
/// Investigación externa de la organización activa: las fuentes autorizadas, las referencias guardadas y las
/// corridas del agente investigador. Una referencia sólo se guarda si su dirección pertenece a una fuente
/// autorizada y activa, y sólo se guarda su dirección y un resumen: el material ajeno no se descarga ni se
/// reproduce. Una referencia es utilizable sólo si su fuente lo permite; si no, es sólo inspiración.
/// </summary>
/// <param name="db">Contexto de datos.</param>
/// <param name="briefs">Briefs.</param>
/// <param name="brands">Marcas.</param>
/// <param name="agent">Agente investigador.</param>
/// <param name="jobs">Cola de trabajos.</param>
/// <param name="audit">Auditoría.</param>
/// <param name="current">Usuario y organización que operan.</param>
/// <param name="options">Topes de costo.</param>
public sealed class ResearchService(
    AppDbContext db, BriefService briefs, BrandService brands, IResearchAgent agent, JobService jobs, AuditService audit, CurrentContext current,
    IOptions<AiOptions> options)
{
    /// <summary>Devuelve las fuentes autorizadas y las referencias guardadas. No escribe.</summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Fuentes por nombre y hasta 300 referencias, de la más nueva a la más vieja.</returns>
    public async Task<ResearchOverview> OverviewAsync(CancellationToken cancellationToken)
    {
        List<ResearchSourceItem> sources = await db.ResearchSources.AsNoTracking()
            .OrderBy(s => s.Name)
            .Select(s => new ResearchSourceItem(
                s.Id, s.Name, s.Domain, s.ConditionsOfUse, s.DefaultUsage, s.IsActive, db.ExternalReferences.Count(r => r.ResearchSourceId == s.Id)))
            .ToListAsync(cancellationToken);
        return new ResearchOverview(sources, await ReferencesAsync(null, cancellationToken));
    }

    /// <summary>Devuelve las corridas y las referencias de un brief. No escribe.</summary>
    /// <param name="briefId">Brief.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Corridas y referencias.</returns>
    public async Task<BriefResearch> ForBriefAsync(long briefId, CancellationToken cancellationToken)
    {
        List<ResearchRunItem> runs = await db.ResearchRuns.AsNoTracking()
            .Where(r => r.CampaignBriefId == briefId)
            .OrderByDescending(r => r.Id)
            .Select(r => new ResearchRunItem(r.Id, r.Status, r.Error, r.SearchQueries, r.Found, r.Kept, r.CostUsd, r.CreatedAt))
            .ToListAsync(cancellationToken);
        return new BriefResearch(runs, await ReferencesAsync(briefId, cancellationToken));
    }

    /// <summary>Autoriza una fuente externa. Escribe la fuente y la auditoría.</summary>
    /// <param name="body">Nombre, dominio, condiciones y permiso por defecto.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Identificador de la fuente.</returns>
    /// <exception cref="BusinessException">400 <c>DOMAIN_INVALID</c> si lo escrito no es un dominio; 409
    /// <c>SOURCE_EXISTS</c> si ese dominio ya está autorizado.</exception>
    public async Task<int> AddSourceAsync(ResearchSourceBody body, CancellationToken cancellationToken)
    {
        string domain = ResearchRules.NormalizeDomain(body.Domain)
            ?? throw new BusinessException(400, "Escribí el dominio del sitio, por ejemplo ejemplo.com.", "DOMAIN_INVALID");
        if (await db.ResearchSources.AnyAsync(s => s.Domain == domain, cancellationToken))
        {
            throw new BusinessException(409, $"El dominio {domain} ya está autorizado.", "SOURCE_EXISTS");
        }

        ResearchSource source = new()
        {
            Name = body.Name.Trim(),
            Domain = domain,
            ConditionsOfUse = string.IsNullOrWhiteSpace(body.ConditionsOfUse) ? null : body.ConditionsOfUse.Trim(),
            DefaultUsage = body.DefaultUsage,
            IsActive = true,
            CreatedByUserId = current.UserId,
        };
        db.ResearchSources.Add(source);
        audit.Record("research.source.add", nameof(ResearchSource), domain, $"Autorizó la fuente {source.Name} ({domain}, {body.DefaultUsage}).");
        await db.SaveChangesAsync(cancellationToken);
        return source.Id;
    }

    /// <summary>Activa o desactiva una fuente. Desactivada, no acepta referencias nuevas; las que ya tiene quedan.
    /// Escribe la fuente y la auditoría.</summary>
    /// <param name="id">Fuente.</param>
    /// <param name="active">Verdadero para activarla.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <exception cref="BusinessException">404 <c>SOURCE_NOT_FOUND</c> si no existe en la organización activa.</exception>
    public async Task SetSourceActiveAsync(int id, bool active, CancellationToken cancellationToken)
    {
        ResearchSource source = await db.ResearchSources.FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
            ?? throw new BusinessException(404, "La fuente no existe.", "SOURCE_NOT_FOUND");
        source.IsActive = active;
        audit.Record("research.source.active", nameof(ResearchSource), source.Domain, active ? "Activó la fuente." : "Desactivó la fuente.");
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Carga una referencia a mano. Su permiso es el que la fuente da por defecto. Escribe la referencia y
    /// la auditoría.</summary>
    /// <param name="body">Dirección, título, clase, resumen y brief opcional.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Identificador de la referencia.</returns>
    /// <exception cref="BusinessException">400 <c>SOURCE_NOT_AUTHORIZED</c> si la dirección no pertenece a una fuente
    /// autorizada y activa; 404 <c>BRIEF_NOT_FOUND</c>; 409 <c>REFERENCE_EXISTS</c> si esa dirección ya está guardada.</exception>
    public async Task<long> AddReferenceAsync(ReferenceBody body, CancellationToken cancellationToken)
    {
        string url = body.Url.Trim();
        List<ResearchSource> sources = await ActiveSourcesAsync(cancellationToken);
        string? domain = ResearchRules.MatchDomain(url, sources.Select(s => s.Domain));
        if (domain is null)
        {
            throw new BusinessException(
                400, "Esa dirección no pertenece a una fuente autorizada y activa. Primero autorizá el sitio como fuente.", "SOURCE_NOT_AUTHORIZED");
        }

        if (body.BriefId is not null && !await db.CampaignBriefs.AnyAsync(c => c.Id == body.BriefId, cancellationToken))
        {
            throw new BusinessException(404, "El brief no existe.", "BRIEF_NOT_FOUND");
        }

        if (await db.ExternalReferences.AnyAsync(r => r.Url == url, cancellationToken))
        {
            throw new BusinessException(409, "Esa referencia ya está guardada.", "REFERENCE_EXISTS");
        }

        ResearchSource source = sources.First(s => s.Domain == domain);
        ExternalReference reference = new()
        {
            ResearchSourceId = source.Id,
            CampaignBriefId = body.BriefId,
            Kind = body.Kind,
            Url = url,
            Title = body.Title.Trim(),
            Summary = string.IsNullOrWhiteSpace(body.Summary) ? null : body.Summary.Trim(),
            Usage = source.DefaultUsage,
            Origin = ReferenceOrigin.Manual,
            CreatedByUserId = current.UserId,
        };
        db.ExternalReferences.Add(reference);
        audit.Record("research.reference.add", nameof(ExternalReference), url, $"Guardó una referencia de {source.Name}.");
        await db.SaveChangesAsync(cancellationToken);
        return reference.Id;
    }

    /// <summary>Cambia el permiso de una referencia. Sólo se puede marcar utilizable si su fuente lo permite. Escribe
    /// la referencia y la auditoría.</summary>
    /// <param name="id">Referencia.</param>
    /// <param name="usage">Permiso nuevo.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <exception cref="BusinessException">404 <c>REFERENCE_NOT_FOUND</c>; 409 <c>SOURCE_FORBIDS_USE</c> si se quiere
    /// marcar utilizable y la fuente sólo permite usarla como referencia.</exception>
    public async Task SetUsageAsync(long id, ReferenceUsage usage, CancellationToken cancellationToken)
    {
        ExternalReference reference = await db.ExternalReferences.Include(r => r.Source).FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
            ?? throw NotFound();
        if (usage == ReferenceUsage.Usable && reference.Source!.DefaultUsage != ReferenceUsage.Usable)
        {
            throw new BusinessException(
                409, $"La fuente {reference.Source.Name} sólo permite usar su material como referencia.", "SOURCE_FORBIDS_USE");
        }

        reference.Usage = usage;
        audit.Record("research.reference.usage", nameof(ExternalReference), reference.Url, $"Cambió el permiso a {usage}.");
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Quita una referencia. Escribe el borrado y la auditoría.</summary>
    /// <param name="id">Referencia.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <exception cref="BusinessException">404 <c>REFERENCE_NOT_FOUND</c> si no existe en la organización activa.</exception>
    public async Task RemoveReferenceAsync(long id, CancellationToken cancellationToken)
    {
        ExternalReference reference = await db.ExternalReferences.FirstOrDefaultAsync(r => r.Id == id, cancellationToken) ?? throw NotFound();
        db.ExternalReferences.Remove(reference);
        audit.Record("research.reference.remove", nameof(ExternalReference), reference.Url, "Quitó la referencia.");
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Estima cuánto costaría investigar para un brief, sin llamar al proveedor. No escribe.</summary>
    /// <param name="briefId">Brief.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Fuentes, cantidad máxima de referencias, costo y tope.</returns>
    /// <exception cref="BusinessException">404 <c>BRIEF_NOT_FOUND</c>.</exception>
    public async Task<ResearchEstimate> EstimateAsync(long briefId, CancellationToken cancellationToken)
    {
        ResearchRequest request = await BuildRequestAsync(briefId, cancellationToken);
        AnalysisCost cost = agent.Estimate(ResearchPrompt.BuildInput(request).Length, request.MaxReferences);
        return new ResearchEstimate(
            agent.IsConfigured, agent.Model, request.Domains.Count, request.MaxReferences, cost.CostUsd,
            Math.Min(options.Value.MaxCostPerResearchUsd, request.Brief.MaxGenerationBudgetUsd ?? decimal.MaxValue));
    }

    /// <summary>Pide una corrida de investigación para un brief. Escribe la corrida en estado pendiente, la
    /// auditoría y el trabajo, en una transacción.</summary>
    /// <param name="briefId">Brief.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Identificador de la corrida.</returns>
    /// <exception cref="BusinessException">404 <c>BRIEF_NOT_FOUND</c>; 409 <c>NO_SOURCES</c> si no hay fuentes
    /// autorizadas y activas; 409 <c>AI_NOT_CONFIGURED</c>; 409 <c>BUDGET_EXCEEDED</c>; 409
    /// <c>RESEARCH_IN_PROGRESS</c> si el brief ya tiene una corrida en curso.</exception>
    public async Task<long> RequestAsync(long briefId, CancellationToken cancellationToken)
    {
        ResearchEstimate estimate = await EstimateAsync(briefId, cancellationToken);
        if (estimate.Sources == 0)
        {
            throw new BusinessException(409, "No hay fuentes autorizadas y activas en las que investigar.", "NO_SOURCES");
        }

        if (!estimate.Configured)
        {
            throw new BusinessException(409, "Falta configurar la clave del proveedor de IA (Ai:Gemini:ApiKey).", "AI_NOT_CONFIGURED");
        }

        if (estimate.CostUsd > estimate.MaxCostUsd)
        {
            throw new BusinessException(
                409, $"La investigación costaría unos USD {estimate.CostUsd:0.0000} y el tope es USD {estimate.MaxCostUsd:0.0000}.", "BUDGET_EXCEEDED");
        }

        if (await db.ResearchRuns.AnyAsync(r => r.CampaignBriefId == briefId && r.Status == MediaProcessingStatus.Pending, cancellationToken))
        {
            throw new BusinessException(409, "Ya hay una investigación en curso para este brief.", "RESEARCH_IN_PROGRESS");
        }

        ResearchRun run = new() { CampaignBriefId = briefId, Status = MediaProcessingStatus.Pending, CreatedByUserId = current.UserId };
        return await db.InTransactionAsync(
            async () =>
            {
                db.ResearchRuns.Add(run);
                await db.SaveChangesAsync(cancellationToken);
                audit.Record(
                    "research.request", nameof(ResearchRun), run.Id.ToString(),
                    $"Pidió investigar en {estimate.Sources} fuentes ({estimate.Model}, estimado USD {estimate.CostUsd:0.0000}).");
                await db.SaveChangesAsync(cancellationToken);
                await jobs.EnqueueAsync(
                    ResearchJobHandler.JobType,
                    $"{ResearchJobHandler.JobType}:{run.Id}",
                    JsonSerializer.Serialize(new ResearchPayload(run.Id)),
                    cancellationToken);
                return run.Id;
            },
            cancellationToken);
    }

    /// <summary>Arma lo que se le envía al agente: marca, brief, dominios autorizados y cantidad máxima. No escribe.</summary>
    /// <param name="briefId">Brief.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>El pedido.</returns>
    /// <exception cref="BusinessException">404 <c>BRIEF_NOT_FOUND</c>.</exception>
    public async Task<ResearchRequest> BuildRequestAsync(long briefId, CancellationToken cancellationToken)
    {
        BriefDetail brief = await briefs.GetAsync(briefId, cancellationToken);
        BrandDetail brand = await brands.GetAsync(brief.BrandId, cancellationToken);
        List<ResearchSource> sources = await ActiveSourcesAsync(cancellationToken);
        return new ResearchRequest(brand, brief, sources.Select(s => s.Domain).ToList(), options.Value.MaxReferencesPerResearch);
    }

    /// <summary>Fuentes autorizadas y activas. No escribe.</summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Las fuentes.</returns>
    public async Task<List<ResearchSource>> ActiveSourcesAsync(CancellationToken cancellationToken)
    {
        return await db.ResearchSources.AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.Domain).ToListAsync(cancellationToken);
    }

    /// <summary>Referencias de un brief tal como se le describen al planificador. No escribe.</summary>
    /// <param name="briefId">Brief.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Hasta 30 referencias.</returns>
    public async Task<List<PlanReference>> ForPlanAsync(long briefId, CancellationToken cancellationToken)
    {
        return await db.ExternalReferences.AsNoTracking()
            .Where(r => r.CampaignBriefId == briefId)
            .OrderBy(r => r.Id)
            .Take(30)
            .Select(r => new PlanReference(r.Kind, r.Title, r.Summary, r.Usage))
            .ToListAsync(cancellationToken);
    }

    private async Task<List<ReferenceItem>> ReferencesAsync(long? briefId, CancellationToken cancellationToken)
    {
        return await db.ExternalReferences.AsNoTracking()
            .Where(r => briefId == null || r.CampaignBriefId == briefId)
            .OrderByDescending(r => r.Id)
            .Take(300)
            .Select(r => new ReferenceItem(
                r.Id, r.Kind, r.Url, r.Title, r.Summary, r.Usage, r.Origin, r.Source!.Name, r.Source.DefaultUsage == ReferenceUsage.Usable,
                r.CampaignBriefId, db.CampaignBriefs.Where(c => c.Id == r.CampaignBriefId).Select(c => c.Name).FirstOrDefault(), r.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    private static BusinessException NotFound()
    {
        return new BusinessException(404, "La referencia no existe.", "REFERENCE_NOT_FOUND");
    }
}

/// <summary>
/// Corrida del agente investigador. Llama al agente y guarda sólo las referencias cuya dirección pertenece a una
/// fuente autorizada y activa, sin repetir y con el permiso que esa fuente da por defecto. Registra el costo. Un
/// fallo transitorio se reintenta; uno definitivo deja la corrida fallida con el motivo.
/// </summary>
/// <param name="db">Contexto de datos, con la organización del trabajo activa.</param>
/// <param name="research">Investigación, para armar el pedido.</param>
/// <param name="agent">Agente investigador.</param>
/// <param name="usage">Registro de costos de IA.</param>
public sealed class ResearchJobHandler(AppDbContext db, ResearchService research, IResearchAgent agent, AiUsageService usage) : IJobHandler
{
    /// <summary>Tipo de trabajo que atiende.</summary>
    public const string JobType = "strategy.research";

    /// <inheritdoc />
    public string Type => JobType;

    /// <inheritdoc />
    /// <exception cref="PermanentJobException">Si la corrida ya no existe o el proveedor la rechaza de forma definitiva.</exception>
    /// <exception cref="AiProviderException">Si el proveedor falla de forma transitoria: el trabajo se reintenta.</exception>
    public async Task ExecuteAsync(JobExecution execution, CancellationToken cancellationToken)
    {
        ResearchPayload? payload = JsonSerializer.Deserialize<ResearchPayload>(execution.Job.PayloadJson);
        long runId = payload?.RunId ?? 0;
        ResearchRun run = await db.ResearchRuns.FirstOrDefaultAsync(r => r.Id == runId, cancellationToken)
            ?? throw new PermanentJobException("La corrida de investigación ya no existe.");
        try
        {
            ResearchRequest request = await research.BuildRequestAsync(run.CampaignBriefId, cancellationToken);
            List<ResearchSource> sources = await research.ActiveSourcesAsync(cancellationToken);
            await execution.ReportAsync(10, "Investigando", cancellationToken);
            ResearchOutcome outcome = await agent.ResearchAsync(request, cancellationToken);
            await execution.ReportAsync(80, "Guardando las referencias", cancellationToken);

            List<string> known = await db.ExternalReferences.AsNoTracking().Select(r => r.Url).ToListAsync(cancellationToken);
            List<ExternalReference> kept = Keep(outcome.References, sources, known, run);
            await db.InTransactionAsync(
                async () =>
                {
                    await usage.RecordAsync(
                        new AiUsageEntry(
                            "strategy.research", outcome.Provider, outcome.Model, outcome.PromptVersion, outcome.InputTokens,
                            outcome.OutputTokens, outcome.CostUsd, outcome.DurationMs, execution.Job.Id),
                        cancellationToken);
                    db.ExternalReferences.AddRange(kept);
                    run.Status = MediaProcessingStatus.Ready;
                    run.Error = null;
                    run.SearchQueries = outcome.SearchQueries;
                    run.Found = outcome.References.Count;
                    run.Kept = kept.Count;
                    run.Model = outcome.Model;
                    run.PromptVersion = outcome.PromptVersion;
                    run.CostUsd = outcome.CostUsd;
                    await db.SaveChangesAsync(cancellationToken);
                    return true;
                },
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException
            && (IsPermanent(exception) || execution.Job.Attempts >= execution.Job.MaxAttempts))
        {
            string message = exception.Message.Length > 1000 ? exception.Message[..1000] : exception.Message;
            await db.ResearchRuns.Where(r => r.Id == runId).ExecuteUpdateAsync(
                s => s.SetProperty(r => r.Status, MediaProcessingStatus.Failed).SetProperty(r => r.Error, message),
                cancellationToken);
            throw new PermanentJobException(message);
        }
    }

    /// <summary>
    /// Decide qué propuestas del agente se guardan: las que tienen título, cuya dirección pertenece a una fuente
    /// autorizada y activa, y que no están ya guardadas ni repetidas en la misma corrida. Cada una recibe el permiso
    /// por defecto de su fuente. Función pura.
    /// </summary>
    /// <param name="proposals">Lo que devolvió el agente.</param>
    /// <param name="sources">Fuentes autorizadas y activas.</param>
    /// <param name="known">Direcciones ya guardadas.</param>
    /// <param name="run">Corrida que las encuentra.</param>
    /// <returns>Las referencias a guardar.</returns>
    public static List<ExternalReference> Keep(
        IReadOnlyList<ReferenceProposal> proposals, IReadOnlyList<ResearchSource> sources, IReadOnlyCollection<string> known, ResearchRun run)
    {
        HashSet<string> seen = new(known, StringComparer.OrdinalIgnoreCase);
        List<ExternalReference> kept = [];
        foreach (ReferenceProposal proposal in proposals)
        {
            string url = proposal.Url.Trim();
            string? domain = ResearchRules.MatchDomain(url, sources.Select(s => s.Domain));
            if (domain is null || url.Length > 450 || string.IsNullOrWhiteSpace(proposal.Title) || !seen.Add(url))
            {
                continue;
            }

            ResearchSource source = sources.First(s => s.Domain == domain);
            kept.Add(new ExternalReference
            {
                ResearchSourceId = source.Id,
                CampaignBriefId = run.CampaignBriefId,
                ResearchRunId = run.Id,
                Kind = proposal.Kind,
                Url = url,
                Title = proposal.Title.Trim().Length > 300 ? proposal.Title.Trim()[..300] : proposal.Title.Trim(),
                Summary = proposal.Summary.Trim().Length > 2000 ? proposal.Summary.Trim()[..2000] : proposal.Summary.Trim(),
                Usage = source.DefaultUsage,
                Origin = ReferenceOrigin.Researched,
            });
        }

        return kept;
    }

    private static bool IsPermanent(Exception exception)
    {
        return exception is BusinessException or JsonException || (exception is AiProviderException provider && !provider.Retryable);
    }
}

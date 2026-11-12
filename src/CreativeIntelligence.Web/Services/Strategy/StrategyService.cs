using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CreativeIntelligence.Web.Services.Strategy;

/// <summary>
/// Motor estratégico de la organización activa: a partir de un brief, del perfil de su marca y de los fragmentos
/// disponibles pide un plan de conceptos al planificador, y convierte los conceptos realizables en recetas del
/// motor de composición. Generar un plan cuesta: se estima antes, se confirma y respeta el tope por plan y el
/// presupuesto de generación del brief. La llamada al proveedor corre en un trabajo de la cola.
/// </summary>
/// <param name="db">Contexto de datos.</param>
/// <param name="briefs">Briefs.</param>
/// <param name="brands">Marcas.</param>
/// <param name="composition">Recetas y renders.</param>
/// <param name="planner">Planificador de conceptos.</param>
/// <param name="jobs">Cola de trabajos.</param>
/// <param name="audit">Auditoría.</param>
/// <param name="current">Usuario y organización que operan.</param>
/// <param name="options">Topes de costo.</param>
/// <param name="research">Investigación, para las referencias del brief.</param>
/// <param name="learnings">Aprendizajes vigentes, para pasárselos al planificador.</param>
public sealed class StrategyService(
    AppDbContext db, BriefService briefs, BrandService brands, CompositionService composition, IConceptPlanner planner, JobService jobs,
    AuditService audit, CurrentContext current, IOptions<AiOptions> options, ResearchService research, LearningService learnings)
{
    /// <summary>Conceptos que se piden cuando el brief no dice cuántos videos quiere.</summary>
    public const int DefaultConcepts = 10;

    /// <summary>Estima cuánto costaría generar el plan de un brief, sin llamar al proveedor. No escribe.</summary>
    /// <param name="briefId">Brief.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Conceptos, fragmentos, tokens, costo y tope.</returns>
    /// <exception cref="BusinessException">404 <c>BRIEF_NOT_FOUND</c> si no existe en la organización activa.</exception>
    public async Task<PlanEstimate> EstimateAsync(long briefId, CancellationToken cancellationToken)
    {
        PlanRequest request = await BuildRequestAsync(briefId, cancellationToken);
        AnalysisCost cost = planner.Estimate(ConceptPlanPrompt.BuildInput(request).Length, request.Count);
        return new PlanEstimate(
            planner.IsConfigured, planner.Model, request.Count, request.Fragments.Count, cost.InputTokens, cost.OutputTokens, cost.CostUsd,
            Math.Min(options.Value.MaxCostPerPlanUsd, request.Brief.MaxGenerationBudgetUsd ?? decimal.MaxValue));
    }

    /// <summary>
    /// Pide generar el plan de un brief. Escribe el plan en estado pendiente, la auditoría y el trabajo, en una
    /// transacción.
    /// </summary>
    /// <param name="briefId">Brief.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Identificador del plan.</returns>
    /// <exception cref="BusinessException">404 <c>BRIEF_NOT_FOUND</c>; 409 <c>AI_NOT_CONFIGURED</c> si falta la clave
    /// del proveedor; 409 <c>BUDGET_EXCEEDED</c> si la estimación supera el tope; 409 <c>PLAN_IN_PROGRESS</c> si el
    /// brief ya tiene un plan generándose.</exception>
    public async Task<long> RequestAsync(long briefId, CancellationToken cancellationToken)
    {
        PlanEstimate estimate = await EstimateAsync(briefId, cancellationToken);
        if (!estimate.Configured)
        {
            throw new BusinessException(409, "Falta configurar la clave del proveedor de IA (Ai:Gemini:ApiKey).", "AI_NOT_CONFIGURED");
        }

        if (estimate.CostUsd > estimate.MaxCostUsd)
        {
            throw new BusinessException(
                409, $"El plan costaría unos USD {estimate.CostUsd:0.0000} y el tope es USD {estimate.MaxCostUsd:0.0000}.", "BUDGET_EXCEEDED");
        }

        if (await db.CreativePlans.AnyAsync(p => p.CampaignBriefId == briefId && p.Status == MediaProcessingStatus.Pending, cancellationToken))
        {
            throw new BusinessException(409, "Ya hay un plan generándose para este brief.", "PLAN_IN_PROGRESS");
        }

        CreativePlan plan = new()
        {
            CampaignBriefId = briefId,
            Status = MediaProcessingStatus.Pending,
            RequestedCount = estimate.Concepts,
            CreatedByUserId = current.UserId,
        };
        return await db.InTransactionAsync(
            async () =>
            {
                db.CreativePlans.Add(plan);
                await db.SaveChangesAsync(cancellationToken);
                audit.Record(
                    "plan.request", nameof(CreativePlan), plan.Id.ToString(),
                    $"Pidió un plan de {estimate.Concepts} conceptos ({estimate.Model}, estimado USD {estimate.CostUsd:0.0000}).");
                await db.SaveChangesAsync(cancellationToken);
                await jobs.EnqueueAsync(
                    StrategyPlanJobHandler.JobType,
                    $"{StrategyPlanJobHandler.JobType}:{plan.Id}",
                    JsonSerializer.Serialize(new PlanPayload(plan.Id)),
                    cancellationToken);
                return plan.Id;
            },
            cancellationToken);
    }

    /// <summary>Lista los planes de un brief, del más nuevo al más viejo. No escribe.</summary>
    /// <param name="briefId">Brief.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Los planes con su cantidad de conceptos y de recetas.</returns>
    public async Task<List<PlanListItem>> ListAsync(long briefId, CancellationToken cancellationToken)
    {
        return await db.CreativePlans.AsNoTracking()
            .Where(p => p.CampaignBriefId == briefId)
            .OrderByDescending(p => p.Id)
            .Select(p => new PlanListItem(
                p.Id, p.Status, p.Error, p.Concepts.Count, p.Concepts.Count(c => c.CreativeRecipeId != null), p.CostUsd, p.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Devuelve un plan con sus conceptos y el material de cada uno. No escribe.</summary>
    /// <param name="planId">Plan.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>El plan.</returns>
    /// <exception cref="BusinessException">404 <c>PLAN_NOT_FOUND</c> si no existe en la organización activa.</exception>
    public async Task<PlanDetail> GetAsync(long planId, CancellationToken cancellationToken)
    {
        CreativePlan plan = await db.CreativePlans.AsNoTracking().Include(p => p.Brief).FirstOrDefaultAsync(p => p.Id == planId, cancellationToken)
            ?? throw new BusinessException(404, "El plan no existe.", "PLAN_NOT_FOUND");
        List<ConceptView> concepts = await db.CreativeConcepts.AsNoTracking()
            .Where(c => c.CreativePlanId == planId)
            .OrderBy(c => c.Position)
            .Select(c => new ConceptView
            {
                Id = c.Id,
                Position = c.Position,
                Title = c.Title,
                Hypothesis = c.Hypothesis,
                Approach = c.Approach,
                Hook = c.Hook,
                Script = c.Script,
                Development = c.Development,
                Cta = c.Cta,
                Audience = c.Audience,
                RequiredMaterial = c.RequiredMaterial,
                Reason = c.Reason,
                Risks = c.Risks,
                Novelty = c.Novelty,
                MissingText = c.MissingText,
                ViolationsText = c.ViolationsText,
                RecipeId = c.CreativeRecipeId,
            })
            .ToListAsync(cancellationToken);
        List<ConceptFragmentRow> fragments = await db.ConceptFragments.AsNoTracking()
            .Where(f => db.CreativeConcepts.Any(c => c.Id == f.CreativeConceptId && c.CreativePlanId == planId))
            .OrderBy(f => f.Position)
            .Select(f => new ConceptFragmentRow
            {
                ConceptId = f.CreativeConceptId,
                Fragment = new ConceptFragmentView(f.MediaSegmentId, f.MediaAssetId, f.MediaAsset!.OriginalFileName, f.StartMs, f.EndMs),
            })
            .ToListAsync(cancellationToken);
        ILookup<long, ConceptFragmentView> byConcept = fragments.ToLookup(f => f.ConceptId, f => f.Fragment);
        foreach (ConceptView concept in concepts)
        {
            concept.Fragments = byConcept[concept.Id].ToList();
        }

        return new PlanDetail(
            plan.Id, plan.CampaignBriefId, plan.Brief!.Name, plan.Status, plan.Error, plan.RequestedCount, plan.Model, plan.PromptVersion,
            plan.CostUsd, plan.CreatedAt, concepts);
    }

    /// <summary>
    /// Arma la receta de un concepto con sus fragmentos, en el formato de la plataforma del brief. La receta lleva
    /// el título, el gancho y la hipótesis del concepto. Escribe la receta, el vínculo y la auditoría.
    /// </summary>
    /// <param name="conceptId">Concepto.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Identificador de la receta.</returns>
    /// <exception cref="BusinessException">404 <c>CONCEPT_NOT_FOUND</c>; 409 <c>CONCEPT_HAS_RECIPE</c> si ya la tiene;
    /// 409 <c>CONCEPT_NOT_REALIZABLE</c> si no tiene fragmentos, alguno ya no existe o usa un mensaje prohibido; y los
    /// de crear una receta (por ejemplo, un video que perdió la autorización).</exception>
    public async Task<long> CreateRecipeAsync(long conceptId, CancellationToken cancellationToken)
    {
        CreativeConcept concept = await db.CreativeConcepts.Include(c => c.Fragments).FirstOrDefaultAsync(c => c.Id == conceptId, cancellationToken)
            ?? throw new BusinessException(404, "El concepto no existe.", "CONCEPT_NOT_FOUND");
        if (concept.CreativeRecipeId is not null)
        {
            throw new BusinessException(409, "Ese concepto ya tiene su receta.", "CONCEPT_HAS_RECIPE");
        }

        if (concept.Fragments.Count == 0 || concept.Fragments.Any(f => f.MediaSegmentId is null) || !string.IsNullOrEmpty(concept.ViolationsText))
        {
            throw new BusinessException(
                409, "El concepto no se puede armar: le falta material existente, alguno de sus fragmentos ya no existe o usa un mensaje prohibido.",
                "CONCEPT_NOT_REALIZABLE");
        }

        BriefPlatform platform = await db.CreativePlans.AsNoTracking()
            .Where(p => p.Id == concept.CreativePlanId)
            .Select(p => p.Brief!.Platform)
            .FirstAsync(cancellationToken);
        CreateRecipeBody body = new()
        {
            Name = concept.Title,
            Concept = concept.Hook,
            Hypothesis = concept.Hypothesis,
            Format = platform == BriefPlatform.MetaFeed ? "4:5" : "9:16",
            Items = concept.Fragments.OrderBy(f => f.Position).Select(f => new RecipeItemBody { SegmentId = f.MediaSegmentId!.Value }).ToList(),
        };
        return await db.InTransactionAsync(
            async () =>
            {
                long recipeId = await composition.CreateAsync(body, cancellationToken);
                concept.CreativeRecipeId = recipeId;
                audit.Record("concept.recipe", nameof(CreativeConcept), conceptId.ToString(), $"Armó la receta #{recipeId} del concepto \"{concept.Title}\".");
                await db.SaveChangesAsync(cancellationToken);
                return recipeId;
            },
            cancellationToken);
    }

    /// <summary>Arma las recetas de todos los conceptos realizables de un plan que todavía no la tienen. Escribe las
    /// recetas, los vínculos y la auditoría.</summary>
    /// <param name="planId">Plan.</param>
    /// <param name="render">Verdadero para pedir además el render de cada receta.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Cuántas recetas se armaron.</returns>
    /// <exception cref="BusinessException">404 <c>PLAN_NOT_FOUND</c>, y los de armar una receta.</exception>
    public async Task<int> CreateRecipesAsync(long planId, bool render, CancellationToken cancellationToken)
    {
        PlanDetail plan = await GetAsync(planId, cancellationToken);
        List<long> pending = plan.Concepts.Where(c => c.Realizable && c.RecipeId is null).Select(c => c.Id).ToList();
        return await db.InTransactionAsync(
            async () =>
            {
                foreach (long conceptId in pending)
                {
                    long recipeId = await CreateRecipeAsync(conceptId, cancellationToken);
                    if (render)
                    {
                        await composition.RenderAsync(recipeId, cancellationToken);
                    }
                }

                return pending.Count;
            },
            cancellationToken);
    }

    /// <summary>
    /// Arma lo que se le envía al planificador: el perfil de la marca, el brief, los fragmentos disponibles (los
    /// elegidos para producción de videos con autorización vigente, con lo que su análisis dice de cada uno) y la
    /// cantidad de conceptos. No escribe. Lo usan la estimación y el trabajo.
    /// </summary>
    /// <param name="briefId">Brief.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>El pedido.</returns>
    /// <exception cref="BusinessException">404 <c>BRIEF_NOT_FOUND</c>.</exception>
    public async Task<PlanRequest> BuildRequestAsync(long briefId, CancellationToken cancellationToken)
    {
        BriefDetail brief = await briefs.GetAsync(briefId, cancellationToken);
        BrandDetail brand = await brands.GetAsync(brief.BrandId, cancellationToken);
        List<PlanSource> sources = await SourcesAsync(cancellationToken);
        List<PlanFragment> fragments = sources
            .Select((s, index) => new PlanFragment(
                index + 1,
                s.EndMs - s.StartMs,
                s.Functions,
                string.Join(". ", new[] { s.Description, s.Message }.Where(t => !string.IsNullOrWhiteSpace(t)).DefaultIfEmpty($"Sin analizar ({s.FileName})"))))
            .ToList();
        return new PlanRequest(brand, brief, fragments, brief.VideoCount ?? DefaultConcepts)
        {
            // Las referencias sólo entran si el brief pide investigación externa.
            References = brief.UseExternalResearch ? await research.ForPlanAsync(briefId, cancellationToken) : [],
            // Sólo los vigentes: uno corregido o invalidado deja de influir en el plan siguiente.
            Learnings = await learnings.ForPlanAsync(cancellationToken),
        };
    }

    /// <summary>Fragmentos disponibles para un plan, en el orden en que se le numeran al planificador. No escribe.</summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Los fragmentos.</returns>
    public async Task<List<PlanSource>> SourcesAsync(CancellationToken cancellationToken)
    {
        return await db.MediaSegments.AsNoTracking()
            .Where(s => s.SelectedForProduction
                && s.MediaAsset!.Kind == MediaAssetKind.Video
                && s.MediaAsset.ReuseAuthorized
                && (s.MediaAsset.RightsExpireOn == null || s.MediaAsset.RightsExpireOn >= DateOnly.FromDateTime(AppDbContext.UtcNow())))
            .OrderBy(s => s.MediaAssetId)
            .ThenBy(s => s.StartMs)
            .Take(options.Value.MaxFragmentsPerPlan)
            .Select(s => new PlanSource
            {
                SegmentId = s.Id,
                AssetId = s.MediaAssetId,
                FileName = s.MediaAsset!.OriginalFileName,
                StartMs = s.StartMs,
                EndMs = s.EndMs,
                Description = db.SegmentAnalyses.Where(a => a.MediaSegmentId == s.Id && a.IsCurrent).Select(a => a.VisualDescription).FirstOrDefault(),
                Message = db.SegmentAnalyses.Where(a => a.MediaSegmentId == s.Id && a.IsCurrent).Select(a => a.MainMessage).FirstOrDefault(),
                Functions = db.SegmentAnalyses.Where(a => a.MediaSegmentId == s.Id && a.IsCurrent)
                    .SelectMany(a => a.Functions)
                    .Select(f => f.Function)
                    .ToList(),
            })
            .ToListAsync(cancellationToken);
    }
}

/// <summary>
/// Generación de un plan de conceptos. Llama al planificador, guarda los conceptos que valen (descarta los de gancho
/// repetido y los números de fragmento que no existen), marca los que usan un mensaje prohibido de la marca,
/// registra el costo y, según la autonomía del brief, arma las recetas y pide los renders. Un fallo transitorio del
/// proveedor se reintenta; uno definitivo deja el plan fallido con el motivo.
/// </summary>
/// <param name="db">Contexto de datos, con la organización del trabajo activa.</param>
/// <param name="strategy">Estrategia, para armar el pedido y las recetas.</param>
/// <param name="planner">Planificador de conceptos.</param>
/// <param name="usage">Registro de costos de IA.</param>
public sealed class StrategyPlanJobHandler(AppDbContext db, StrategyService strategy, IConceptPlanner planner, AiUsageService usage) : IJobHandler
{
    /// <summary>Tipo de trabajo que atiende.</summary>
    public const string JobType = "strategy.plan";

    /// <inheritdoc />
    public string Type => JobType;

    /// <inheritdoc />
    /// <exception cref="PermanentJobException">Si el plan ya no existe o el proveedor lo rechaza de forma definitiva.</exception>
    /// <exception cref="AiProviderException">Si el proveedor falla de forma transitoria: el trabajo se reintenta.</exception>
    public async Task ExecuteAsync(JobExecution execution, CancellationToken cancellationToken)
    {
        PlanPayload? payload = JsonSerializer.Deserialize<PlanPayload>(execution.Job.PayloadJson);
        long planId = payload?.PlanId ?? 0;
        CreativePlan plan = await db.CreativePlans.Include(p => p.Brief).FirstOrDefaultAsync(p => p.Id == planId, cancellationToken)
            ?? throw new PermanentJobException("El plan ya no existe.");
        try
        {
            PlanRequest request = await strategy.BuildRequestAsync(plan.CampaignBriefId, cancellationToken);
            List<PlanSource> sources = await strategy.SourcesAsync(cancellationToken);
            await execution.ReportAsync(10, "Consultando al planificador", cancellationToken);
            PlanOutcome outcome = await planner.PlanAsync(request, cancellationToken);
            await execution.ReportAsync(80, "Guardando los conceptos", cancellationToken);

            List<string> forbidden = request.Brand.Of(BrandItemKind.ForbiddenMessage).Select(i => i.Text).ToList();
            await db.InTransactionAsync(
                async () =>
                {
                    await usage.RecordAsync(
                        new AiUsageEntry(
                            "strategy.plan", outcome.Provider, outcome.Model, outcome.PromptVersion, outcome.InputTokens, outcome.OutputTokens,
                            outcome.CostUsd, outcome.DurationMs, execution.Job.Id),
                        cancellationToken);
                    await db.CreativeConcepts.Where(c => c.CreativePlanId == planId).ExecuteDeleteAsync(cancellationToken);
                    plan.Concepts = BuildConcepts(outcome.Concepts, sources, forbidden, plan.RequestedCount);
                    plan.Status = MediaProcessingStatus.Ready;
                    plan.Error = null;
                    plan.Provider = outcome.Provider;
                    plan.Model = outcome.Model;
                    plan.PromptVersion = outcome.PromptVersion;
                    plan.CostUsd = outcome.CostUsd;
                    await db.SaveChangesAsync(cancellationToken);

                    // Según la autonomía del brief: sólo proponer, armar las recetas, o armarlas y renderizarlas.
                    if (plan.Brief!.Autonomy != AutonomyLevel.Propose)
                    {
                        await strategy.CreateRecipesAsync(planId, plan.Brief.Autonomy == AutonomyLevel.Automatic, cancellationToken);
                    }

                    return true;
                },
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException
            && (IsPermanent(exception) || execution.Job.Attempts >= execution.Job.MaxAttempts))
        {
            // Queda fallido con el motivo cuando reintentar no sirve o ya no quedan intentos; si no, sigue pendiente.
            string message = exception.Message.Length > 1000 ? exception.Message[..1000] : exception.Message;
            await db.CreativePlans.Where(p => p.Id == planId).ExecuteUpdateAsync(
                s => s.SetProperty(p => p.Status, MediaProcessingStatus.Failed).SetProperty(p => p.Error, message),
                cancellationToken);
            throw new PermanentJobException(message);
        }
    }

    /// <summary>
    /// Convierte las propuestas del planificador en conceptos: toma hasta la cantidad pedida, descarta los que
    /// repiten el gancho de uno anterior, conserva de cada uno sólo los números de fragmento que existen (sin
    /// repetir) y anota qué mensajes prohibidos usa. Función pura.
    /// </summary>
    /// <param name="proposals">Lo que devolvió el planificador.</param>
    /// <param name="sources">Fragmentos disponibles, en el orden en que se le numeraron.</param>
    /// <param name="forbidden">Mensajes prohibidos de la marca.</param>
    /// <param name="count">Cantidad pedida.</param>
    /// <returns>Los conceptos a guardar.</returns>
    public static List<CreativeConcept> BuildConcepts(
        IReadOnlyList<ConceptProposal> proposals, IReadOnlyList<PlanSource> sources, IReadOnlyList<string> forbidden, int count)
    {
        List<CreativeConcept> concepts = [];
        HashSet<string> hooks = [];
        foreach (ConceptProposal proposal in proposals)
        {
            if (concepts.Count == count || string.IsNullOrWhiteSpace(proposal.Title) || !hooks.Add(MessageRules.Normalize(proposal.Hook)))
            {
                continue;
            }

            List<string> violations = MessageRules.Violations(
                string.Join('\n', proposal.Title, proposal.Hook, proposal.Script, proposal.Development, proposal.Cta), forbidden);
            concepts.Add(new CreativeConcept
            {
                Position = concepts.Count + 1,
                Title = Limit(proposal.Title, 180),
                Hypothesis = Limit(proposal.Hypothesis, 1000),
                Approach = Limit(proposal.Approach, 500),
                Hook = Limit(proposal.Hook, 1000),
                Script = Limit(proposal.Script, 4000),
                Development = Limit(proposal.Development, 2000),
                Cta = Limit(proposal.Cta, 500),
                Audience = Limit(proposal.Audience, 500),
                RequiredMaterial = Limit(proposal.RequiredMaterial, 1000),
                Reason = Limit(proposal.Reason, 1000),
                Risks = Limit(proposal.Risks, 1000),
                Novelty = Math.Clamp(proposal.Novelty, 1, 5),
                MissingText = Join(proposal.Missing.Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m.Trim().Replace('\n', ' ')), 2000),
                ViolationsText = Join(violations, 1000),
                Fragments = proposal.Fragments
                    .Where(i => i >= 1 && i <= sources.Count)
                    .Distinct()
                    .Select((i, position) => new ConceptFragment
                    {
                        Position = position + 1,
                        MediaSegmentId = sources[i - 1].SegmentId,
                        MediaAssetId = sources[i - 1].AssetId,
                        StartMs = sources[i - 1].StartMs,
                        EndMs = sources[i - 1].EndMs,
                    })
                    .ToList(),
            });
        }

        return concepts;
    }

    private static bool IsPermanent(Exception exception)
    {
        return exception is BusinessException or JsonException || (exception is AiProviderException provider && !provider.Retryable);
    }

    private static string Limit(string? value, int max)
    {
        string text = value?.Trim() ?? string.Empty;
        return text.Length > max ? text[..max] : text;
    }

    private static string? Join(IEnumerable<string> lines, int max)
    {
        string text = string.Join('\n', lines);
        return text.Length == 0 ? null : (text.Length > max ? text[..max] : text);
    }
}

using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace CreativeIntelligence.Web.Services.Automation;

/// <summary>
/// Guardas de la automatización que no dependen de la base. Deciden si una acción se bloquea, si la tiene que
/// decidir una persona o si puede ejecutarse sola. El orden importa: primero lo que frena todo (kill switch,
/// límites sin definir), después los límites, después los derechos y recién al final el modo.
/// </summary>
public static class AutomationRules
{
    /// <summary>Evalúa una acción contra la política. Función pura.</summary>
    /// <param name="limits">Kill switch, modo y límites.</param>
    /// <param name="budget">Presupuesto diario de la acción.</param>
    /// <param name="today">Lo ya comprometido hoy por las demás acciones.</param>
    /// <param name="evidence">Confianza del mejor aprendizaje vigente que la respalda; nulo si no tiene.</param>
    /// <param name="unauthorizedSource">Nombre de un original o una pista del video sin autorización vigente; nulo si todos la tienen.</param>
    /// <param name="openAnomalies">Anomalías de las métricas que nadie revisó.</param>
    /// <returns>El veredicto, con su motivo.</returns>
    public static GuardVerdict Evaluate(
        AutomationLimits limits, decimal budget, DailyUsage today, LearningConfidence? evidence, string? unauthorizedSource, int openAnomalies = 0)
    {
        if (limits.KillSwitchActive)
        {
            return new GuardVerdict(true, true, false, "El kill switch está activo: no se acepta ni se ejecuta ninguna acción.");
        }

        if (limits.MaxBudgetPerAction <= 0 || limits.MaxDailyBudget <= 0 || limits.MaxActionsPerDay <= 0)
        {
            return Block("Faltan límites: hay que definir el presupuesto máximo por acción, el presupuesto diario y las acciones por día antes de pedir acciones.");
        }

        if (budget > limits.MaxBudgetPerAction)
        {
            return Block($"El presupuesto diario de {Money(budget)} supera el máximo por acción ({Money(limits.MaxBudgetPerAction)}).");
        }

        if (today.Budget + budget > limits.MaxDailyBudget)
        {
            return Block(
                $"Con esta acción el presupuesto comprometido hoy sería {Money(today.Budget + budget)} y el máximo diario es {Money(limits.MaxDailyBudget)}.");
        }

        if (today.Actions >= limits.MaxActionsPerDay)
        {
            return Block($"Ya hay {today.Actions} acciones hoy y el límite diario es {limits.MaxActionsPerDay}.");
        }

        if (unauthorizedSource is not null)
        {
            return Block($"\"{unauthorizedSource}\" ya no tiene autorización de reutilización vigente: el video no se puede publicar.");
        }

        return limits.Mode switch
        {
            AutomationMode.Manual => new GuardVerdict(false, false, true, "Modo manual: toda acción la decide una persona."),
            AutomationMode.SemiAutomatic => new GuardVerdict(
                false, false, true, "Modo semiautomático: publicar es una acción crítica y necesita la aprobación de una persona."),
            // Con anomalías sin revisar el sistema pierde la autonomía: algo raro pasa en la cuenta y lo mira una persona.
            _ when openAnomalies > 0 => new GuardVerdict(
                false, false, true,
                $"Hay {openAnomalies} anomalía(s) sin revisar en las métricas: hasta revisarlas, toda acción la decide una persona."),
            _ => evidence is LearningConfidence.High or LearningConfidence.Medium
                ? new GuardVerdict(false, false, false, string.Empty)
                : new GuardVerdict(
                    false, false, true,
                    "Datos insuficientes: no la respalda un aprendizaje vigente de confianza media o alta, así que la decide una persona."),
        };
    }

    /// <summary>Si una receta usa un fragmento en el lugar del que habla una hipótesis: como apertura, como cierre
    /// o en cualquier parte. Función pura.</summary>
    /// <param name="recipe">Componentes de la receta.</param>
    /// <param name="variable">Sobre qué parte del anuncio trata la hipótesis.</param>
    /// <param name="assetId">Original del fragmento.</param>
    /// <param name="startMs">Inicio del fragmento.</param>
    /// <param name="endMs">Fin del fragmento.</param>
    /// <returns>Verdadero si la receta lo aplica.</returns>
    public static bool Uses(AdComponents recipe, ExperimentVariable variable, long assetId, int startMs, int endMs)
    {
        bool Same(ComponentRef item)
        {
            return item.AssetId == assetId && item.StartMs == startMs && item.EndMs == endMs;
        }

        return variable switch
        {
            ExperimentVariable.Opening => recipe.Items.Count > 0 && Same(recipe.Items[0]),
            ExperimentVariable.Closing => recipe.Items.Count > 1 && Same(recipe.Items[^1]),
            ExperimentVariable.Development => recipe.Items.Any(Same),
            _ => false,
        };
    }

    private static GuardVerdict Block(string reason)
    {
        return new GuardVerdict(true, false, false, reason);
    }

    private static string Money(decimal value)
    {
        return value.ToString("0.00", CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// Automatización publicitaria de la organización activa. Una acción (publicar un video generado como anuncio) pasa
/// por las guardas al pedirla, al aprobarla y otra vez al ejecutarla. <b>La ejecución real está desactivada por
/// defecto</b>: mientras lo esté, toda acción es una simulación que registra lo que se habría hecho y nunca llama
/// al publicador. Habilitarla exige conexión con Meta, límites definidos y el kill switch apagado.
/// </summary>
/// <param name="db">Contexto de datos.</param>
/// <param name="publisher">Publicador de Meta.</param>
/// <param name="jobs">Cola de trabajos.</param>
/// <param name="anomalies">Anomalías de las métricas.</param>
/// <param name="performance">Componentes de las recetas.</param>
/// <param name="audit">Auditoría.</param>
/// <param name="current">Usuario y organización que operan.</param>
public sealed class AutomationService(
    AppDbContext db, IMetaPublisher publisher, JobService jobs, AnomalyService anomalies, PerformanceService performance, AuditService audit,
    CurrentContext current)
{
    // Estados que cuentan como comprometidos para los límites del día y para detectar un duplicado.
    private static readonly AutomationActionStatus[] Live =
    [
        AutomationActionStatus.PendingApproval, AutomationActionStatus.Approved, AutomationActionStatus.Executed, AutomationActionStatus.Simulated,
    ];

    /// <summary>Devuelve la política, lo comprometido hoy, las últimas acciones y lo que hace falta para pedir una. No escribe.</summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Lo que muestra la pantalla.</returns>
    public async Task<AutomationOverview> OverviewAsync(CancellationToken cancellationToken)
    {
        AutomationPolicy policy = await PolicyAsync(cancellationToken);
        List<AutomationActionItem> actions = await db.AutomationActions.AsNoTracking()
            .OrderByDescending(a => a.Id)
            .Take(100)
            .Select(a => new AutomationActionItem(
                a.Id,
                a.CreativeVariantId,
                db.CreativeVariants.Where(v => v.Id == a.CreativeVariantId)
                    .Select(v => db.CreativeRecipes.Where(r => r.Id == v.CreativeRecipeId).Select(r => r.Name).First())
                    .First(),
                db.MetaAdSets.Where(s => s.Id == a.MetaAdSetId).Select(s => s.Campaign!.Name + " · " + s.Name).First(),
                a.AdName, a.DailyBudget, a.IsSimulation, a.Status, a.ApprovalReason, a.Result, a.DecisionNote, a.CreatedAt, a.ProductionPlanId))
            .ToListAsync(cancellationToken);
        List<VariantOption> variants = await db.CreativeVariants.AsNoTracking()
            .Where(v => v.Status == MediaProcessingStatus.Ready)
            .OrderByDescending(v => v.Id)
            .Take(200)
            .Select(v => new VariantOption(v.Id, db.CreativeRecipes.Where(r => r.Id == v.CreativeRecipeId).Select(r => r.Name).First(), v.CreatedAt))
            .ToListAsync(cancellationToken);
        List<AdSetOption> adSets = await db.MetaAdSets.AsNoTracking()
            .OrderBy(s => s.Campaign!.Name)
            .ThenBy(s => s.Name)
            .Take(500)
            .Select(s => new AdSetOption(s.Id, s.Campaign!.Name + " · " + s.Name))
            .ToListAsync(cancellationToken);
        List<HypothesisOption> hypotheses = await db.Hypotheses.AsNoTracking()
            .Where(h => h.DiscardedAt == null)
            .OrderByDescending(h => h.Id)
            .Take(200)
            .Select(h => new HypothesisOption(h.Id, h.Statement))
            .ToListAsync(cancellationToken);
        List<KillSwitchItem> events = await db.KillSwitchEvents.AsNoTracking()
            .OrderByDescending(e => e.Id)
            .Take(10)
            .Select(e => new KillSwitchItem(e.Activated, e.Reason, e.CancelledActions, e.CreatedAt))
            .ToListAsync(cancellationToken);
        List<PlanOption> plans = await db.ProductionPlans.AsNoTracking()
            .OrderByDescending(p => p.Id)
            .Take(50)
            .Select(p => new PlanOption(p.Id, p.Name))
            .ToListAsync(cancellationToken);
        return new AutomationOverview(
            new AutomationPolicyView(
                policy.Mode, policy.RealExecutionEnabled, policy.KillSwitchActive, policy.MaxBudgetPerAction, policy.MaxDailyBudget, policy.MaxActionsPerDay,
                policy.MaxBatchBudget),
            publisher.IsConfigured, await UsageAsync(!policy.RealExecutionEnabled, null, cancellationToken), actions, variants, adSets, hypotheses, events,
            await anomalies.ListAsync(cancellationToken), plans);
    }

    /// <summary>Guarda el modo y los límites. Escribe la política y la auditoría.</summary>
    /// <param name="body">Modo y límites.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    public async Task SavePolicyAsync(AutomationPolicyBody body, CancellationToken cancellationToken)
    {
        await db.InTransactionAsync(
            async () =>
            {
                AutomationPolicy policy = await TrackedPolicyAsync(cancellationToken);
                policy.Mode = body.Mode;
                policy.MaxBudgetPerAction = body.MaxBudgetPerAction;
                policy.MaxDailyBudget = body.MaxDailyBudget;
                policy.MaxActionsPerDay = body.MaxActionsPerDay;
                policy.MaxBatchBudget = body.MaxBatchBudget;
                await db.SaveChangesAsync(cancellationToken);
                audit.Record(
                    "automation.policy", nameof(AutomationPolicy), policy.Id.ToString(),
                    $"Fijó el modo {policy.Mode} y los límites: {policy.MaxBudgetPerAction.ToString("0.00", CultureInfo.InvariantCulture)} por acción, "
                    + $"{policy.MaxDailyBudget.ToString("0.00", CultureInfo.InvariantCulture)} por día, {policy.MaxActionsPerDay} acciones por día y "
                    + $"{policy.MaxBatchBudget.ToString("0.00", CultureInfo.InvariantCulture)} por lote.");
                await db.SaveChangesAsync(cancellationToken);
                return true;
            },
            cancellationToken);
    }

    /// <summary>
    /// Habilita o deshabilita la ejecución real. Habilitarla exige conexión con Meta, límites definidos y el kill
    /// switch apagado. Escribe la política y la auditoría.
    /// </summary>
    /// <param name="enabled">Verdadero para que las acciones nuevas escriban en Meta.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <exception cref="BusinessException">409 <c>META_NOT_CONNECTED</c>, <c>LIMITS_REQUIRED</c> o
    /// <c>KILL_SWITCH_ACTIVE</c> si no se cumple lo necesario para habilitarla.</exception>
    public async Task SetRealExecutionAsync(bool enabled, CancellationToken cancellationToken)
    {
        await db.InTransactionAsync(
            async () =>
            {
                AutomationPolicy policy = await TrackedPolicyAsync(cancellationToken);
                if (enabled && !publisher.IsConfigured)
                {
                    throw new BusinessException(409, "No hay conexión con Meta: la ejecución real no se puede habilitar. Todo sigue en simulación.", "META_NOT_CONNECTED");
                }

                if (enabled && (policy.MaxBudgetPerAction <= 0 || policy.MaxDailyBudget <= 0 || policy.MaxActionsPerDay <= 0))
                {
                    throw new BusinessException(409, "Antes de habilitar la ejecución real hay que definir los tres límites.", "LIMITS_REQUIRED");
                }

                if (enabled && policy.KillSwitchActive)
                {
                    throw new BusinessException(409, "El kill switch está activo: primero hay que desactivarlo.", "KILL_SWITCH_ACTIVE");
                }

                policy.RealExecutionEnabled = enabled;
                await db.SaveChangesAsync(cancellationToken);
                audit.Record(
                    "automation.real-execution", nameof(AutomationPolicy), policy.Id.ToString(),
                    enabled ? "Habilitó la ejecución real en Meta." : "Deshabilitó la ejecución real: las acciones nuevas son simulaciones.");
                await db.SaveChangesAsync(cancellationToken);
                return true;
            },
            cancellationToken);
    }

    /// <summary>
    /// Pide publicar un video generado como anuncio. Pasa por las guardas: queda bloqueada con su motivo, a la
    /// espera de aprobación, o aprobada y en cola si el modo y los datos lo permiten. Con la ejecución real
    /// desactivada es una simulación. Escribe la acción, la auditoría y, si corresponde, el trabajo.
    /// </summary>
    /// <param name="body">Video, conjunto, nombre, presupuesto diario e hipótesis que la respalda.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Identificador de la acción.</returns>
    /// <exception cref="BusinessException">404 <c>VARIANT_NOT_FOUND</c>, <c>ADSET_NOT_FOUND</c> o
    /// <c>HYPOTHESIS_NOT_FOUND</c>; 409 <c>VARIANT_NOT_READY</c> si el video no terminó de renderizarse; 409
    /// <c>DUPLICATE_ACTION</c> si ya hay una acción en pie para ese video en ese conjunto.</exception>
    public async Task<long> RequestAsync(PublishRequestBody body, CancellationToken cancellationToken)
    {
        long variantId = body.VariantId!.Value;
        long adSetId = body.AdSetId!.Value;
        MediaProcessingStatus? status = await db.CreativeVariants.AsNoTracking()
            .Where(v => v.Id == variantId)
            .Select(v => (MediaProcessingStatus?)v.Status)
            .FirstOrDefaultAsync(cancellationToken);
        if (status is null)
        {
            throw new BusinessException(404, "El video no existe.", "VARIANT_NOT_FOUND");
        }

        if (status != MediaProcessingStatus.Ready)
        {
            throw new BusinessException(409, "El video todavía no está listo.", "VARIANT_NOT_READY");
        }

        if (!await db.MetaAdSets.AnyAsync(s => s.Id == adSetId, cancellationToken))
        {
            throw new BusinessException(404, "El conjunto de anuncios no existe.", "ADSET_NOT_FOUND");
        }

        if (body.HypothesisId is not null && !await db.Hypotheses.AnyAsync(h => h.Id == body.HypothesisId, cancellationToken))
        {
            throw new BusinessException(404, "La hipótesis no existe.", "HYPOTHESIS_NOT_FOUND");
        }

        return await db.InTransactionAsync(
            async () =>
            {
                AutomationPolicy policy = await PolicyAsync(cancellationToken);
                bool simulation = !policy.RealExecutionEnabled;
                if (await DuplicateAsync(variantId, adSetId, simulation, cancellationToken))
                {
                    throw new BusinessException(
                        409, "Ya hay una acción en pie para publicar ese video en ese conjunto: no se publica dos veces.", "DUPLICATE_ACTION");
                }

                return await AddAsync(
                    policy,
                    new AutomationAction
                    {
                        CreativeVariantId = variantId,
                        MetaAdSetId = adSetId,
                        AdName = body.AdName.Trim(),
                        DailyBudget = body.DailyBudget,
                        HypothesisId = body.HypothesisId,
                        IsSimulation = simulation,
                        RequestedByUserId = current.UserId,
                    },
                    cancellationToken);
            },
            cancellationToken);
    }

    /// <summary>
    /// Propone las publicaciones de un plan de producción: para cada hipótesis que el plan dice aplicar y que trata
    /// de un fragmento, busca el video listo más nuevo, sin publicar, que lo use en ese lugar, y pide publicarlo.
    /// Cada acción pasa por las mismas guardas que una pedida a mano. El lote entero se rechaza si su presupuesto
    /// supera el máximo por lote. Escribe las acciones, la auditoría y los trabajos que correspondan.
    /// </summary>
    /// <param name="body">Plan, conjunto de anuncios y presupuesto diario de cada publicación.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Las acciones creadas y los renglones del plan que no dieron ninguna, con su motivo.</returns>
    /// <exception cref="BusinessException">404 <c>PRODUCTION_PLAN_NOT_FOUND</c> o <c>ADSET_NOT_FOUND</c>; 409
    /// <c>BATCH_LIMIT_REQUIRED</c> si no se definió el presupuesto por lote; 409 <c>BATCH_BUDGET_EXCEEDED</c> si el
    /// lote lo supera.</exception>
    public async Task<ProposalResult> ProposeFromPlanAsync(ProposeFromPlanBody body, CancellationToken cancellationToken)
    {
        long planId = body.PlanId!.Value;
        long adSetId = body.AdSetId!.Value;
        string planName = await db.ProductionPlans.AsNoTracking().Where(p => p.Id == planId).Select(p => p.Name).FirstOrDefaultAsync(cancellationToken)
            ?? throw new BusinessException(404, "El plan de producción no existe.", "PRODUCTION_PLAN_NOT_FOUND");
        if (!await db.MetaAdSets.AnyAsync(s => s.Id == adSetId, cancellationToken))
        {
            throw new BusinessException(404, "El conjunto de anuncios no existe.", "ADSET_NOT_FOUND");
        }

        AutomationPolicy policy = await PolicyAsync(cancellationToken);
        if (policy.MaxBatchBudget <= 0)
        {
            throw new BusinessException(409, "Antes de proponer un lote hay que definir el presupuesto máximo por lote.", "BATCH_LIMIT_REQUIRED");
        }

        bool simulation = !policy.RealExecutionEnabled;
        List<ProductionPlanItem> items = await db.ProductionPlanItems.AsNoTracking()
            .Where(i => i.ProductionPlanId == planId && i.Action == PlanAction.Apply)
            .OrderBy(i => i.Position)
            .ToListAsync(cancellationToken);
        Dictionary<long, AdComponents> recipes = (await performance.RecipeComponentsAsync(cancellationToken)).ToDictionary(r => r.AdId);
        List<ReadyVariantRow> ready = await db.CreativeVariants.AsNoTracking()
            .Where(v => v.Status == MediaProcessingStatus.Ready && !db.MetaAds.Any(a => a.CreativeVariantId == v.Id))
            .OrderByDescending(v => v.Id)
            .Take(500)
            .Select(v => new ReadyVariantRow(v.Id, v.CreativeRecipeId))
            .ToListAsync(cancellationToken);

        List<string> skipped = [];
        List<AutomationAction> chosen = [];
        foreach (ProductionPlanItem item in items)
        {
            if (item.MediaAssetId is null || item.StartMs is null || item.EndMs is null)
            {
                skipped.Add($"«{item.Statement}»: no trata de un fragmento, así que el sistema no sabe qué video la aplica.");
                continue;
            }

            long? variantId = null;
            foreach (ReadyVariantRow variant in ready)
            {
                if (chosen.All(c => c.CreativeVariantId != variant.VariantId)
                    && recipes.TryGetValue(variant.RecipeId, out AdComponents? recipe)
                    && AutomationRules.Uses(recipe, item.Variable, item.MediaAssetId.Value, item.StartMs.Value, item.EndMs.Value)
                    && !await DuplicateAsync(variant.VariantId, adSetId, simulation, cancellationToken))
                {
                    variantId = variant.VariantId;
                    break;
                }
            }

            if (variantId is null)
            {
                skipped.Add($"«{item.Statement}»: no hay un video listo y sin publicar que lo use, o ya tiene una acción en pie en ese conjunto.");
                continue;
            }

            string name = $"{planName} · {item.Position}";
            chosen.Add(new AutomationAction
            {
                CreativeVariantId = variantId.Value,
                MetaAdSetId = adSetId,
                AdName = name.Length > 300 ? name[..300] : name,
                DailyBudget = body.DailyBudget,
                HypothesisId = item.HypothesisId,
                ProductionPlanId = planId,
                IsSimulation = simulation,
                RequestedByUserId = current.UserId,
            });
        }

        if (items.Count == 0)
        {
            skipped.Add("El plan no propone aplicar ninguna hipótesis: las que tiene son para probar o explorar.");
        }

        decimal total = chosen.Count * body.DailyBudget;
        if (total > policy.MaxBatchBudget)
        {
            throw new BusinessException(
                409,
                $"El lote comprometería {total.ToString("0.00", CultureInfo.InvariantCulture)} por día y el máximo por lote es "
                + $"{policy.MaxBatchBudget.ToString("0.00", CultureInfo.InvariantCulture)}. No se creó ninguna acción.",
                "BATCH_BUDGET_EXCEEDED");
        }

        return await db.InTransactionAsync(
            async () =>
            {
                List<long> ids = [];
                foreach (AutomationAction action in chosen)
                {
                    ids.Add(await AddAsync(policy, action, cancellationToken));
                }

                audit.Record(
                    "automation.propose", nameof(ProductionPlan), planId.ToString(),
                    $"Propuso {ids.Count} publicaciones del plan \"{planName}\" y dejó {skipped.Count} renglones sin acción.");
                await db.SaveChangesAsync(cancellationToken);
                return new ProposalResult(ids, skipped);
            },
            cancellationToken);
    }

    private async Task<bool> DuplicateAsync(long variantId, long adSetId, bool simulation, CancellationToken cancellationToken)
    {
        return await db.AutomationActions.AnyAsync(
            a => a.CreativeVariantId == variantId && a.MetaAdSetId == adSetId && a.IsSimulation == simulation && Live.Contains(a.Status),
            cancellationToken);
    }

    // Da de alta una acción ya armada: la pasa por las guardas, la guarda con su estado, la audita y, si quedó
    // aprobada, la encola. Se llama dentro de una transacción.
    private async Task<long> AddAsync(AutomationPolicy policy, AutomationAction action, CancellationToken cancellationToken)
    {
        long variantId = action.CreativeVariantId;
        bool simulation = action.IsSimulation;
        GuardVerdict verdict = await EvaluateAsync(policy, action, cancellationToken);
        action.Status = verdict.Blocked ? AutomationActionStatus.Blocked
            : verdict.NeedsApproval ? AutomationActionStatus.PendingApproval
            : AutomationActionStatus.Approved;
        action.Result = verdict.Blocked ? verdict.Reason : null;
        action.ApprovalReason = verdict.NeedsApproval ? verdict.Reason : null;
        db.AutomationActions.Add(action);
        await db.SaveChangesAsync(cancellationToken);
        if (verdict.Blocked)
        {
            await db.AutomationActions.Where(a => a.Id == action.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.FinishedAt, a => (DateTime?)AppDbContext.UtcNow()), cancellationToken);
        }

        audit.Record(
            "automation.request", nameof(AutomationAction), action.Id.ToString(),
            $"Pidió publicar el video {variantId} como \"{action.AdName}\" ({(simulation ? "simulación" : "ejecución real")}): {action.Status}.");
        await db.SaveChangesAsync(cancellationToken);
        if (action.Status == AutomationActionStatus.Approved)
        {
            await EnqueueAsync(action.Id, cancellationToken);
        }

        return action.Id;
    }

    /// <summary>
    /// Aprueba una acción que esperaba decisión. Vuelve a pasar por las guardas: si mientras tanto dejó de entrar
    /// en los límites, queda bloqueada. Escribe la acción, la auditoría y el trabajo.
    /// </summary>
    /// <param name="id">Acción.</param>
    /// <param name="note">Nota de quien aprueba.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <exception cref="BusinessException">404 <c>ACTION_NOT_FOUND</c>; 409 <c>ACTION_NOT_PENDING</c> si ya no espera decisión.</exception>
    public async Task ApproveAsync(long id, string? note, CancellationToken cancellationToken)
    {
        await db.InTransactionAsync(
            async () =>
            {
                AutomationAction action = await PendingAsync(id, cancellationToken);
                GuardVerdict verdict = await EvaluateAsync(await PolicyAsync(cancellationToken), action, cancellationToken);
                AutomationActionStatus status = verdict.CancelledByKillSwitch ? AutomationActionStatus.Cancelled
                    : verdict.Blocked ? AutomationActionStatus.Blocked
                    : AutomationActionStatus.Approved;
                await DecideAsync(id, status, note, verdict.Blocked ? verdict.Reason : null, cancellationToken);
                audit.Record("automation.approve", nameof(AutomationAction), id.ToString(), $"Aprobó la acción: {status}.");
                await db.SaveChangesAsync(cancellationToken);
                if (status == AutomationActionStatus.Approved)
                {
                    await EnqueueAsync(id, cancellationToken);
                }

                return true;
            },
            cancellationToken);
    }

    /// <summary>Rechaza una acción que esperaba decisión. Escribe la acción y la auditoría.</summary>
    /// <param name="id">Acción.</param>
    /// <param name="note">Nota de quien rechaza.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <exception cref="BusinessException">404 <c>ACTION_NOT_FOUND</c>; 409 <c>ACTION_NOT_PENDING</c>.</exception>
    public async Task RejectAsync(long id, string? note, CancellationToken cancellationToken)
    {
        await db.InTransactionAsync(
            async () =>
            {
                await PendingAsync(id, cancellationToken);
                await DecideAsync(id, AutomationActionStatus.Rejected, note, "Rechazada por una persona.", cancellationToken);
                audit.Record("automation.reject", nameof(AutomationAction), id.ToString(), "Rechazó la acción.");
                await db.SaveChangesAsync(cancellationToken);
                return true;
            },
            cancellationToken);
    }

    /// <summary>
    /// Activa o desactiva el kill switch. Al activarlo cancela todas las acciones que esperaban aprobación o
    /// estaban en cola; las que ya están ejecutándose se frenan solas al comprobarlo. Desactivarlo no revive
    /// ninguna. Escribe la política, las acciones, el registro del kill switch y la auditoría.
    /// </summary>
    /// <param name="activate">Verdadero para activarlo.</param>
    /// <param name="reason">Motivo.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Cantidad de acciones canceladas.</returns>
    public async Task<int> KillSwitchAsync(bool activate, string reason, CancellationToken cancellationToken)
    {
        string text = reason.Trim();
        return await db.InTransactionAsync(
            async () =>
            {
                AutomationPolicy policy = await TrackedPolicyAsync(cancellationToken);
                policy.KillSwitchActive = activate;
                await db.SaveChangesAsync(cancellationToken);
                int cancelled = !activate
                    ? 0
                    : await db.AutomationActions
                        .Where(a => a.Status == AutomationActionStatus.PendingApproval || a.Status == AutomationActionStatus.Approved)
                        .ExecuteUpdateAsync(
                            s => s.SetProperty(a => a.Status, AutomationActionStatus.Cancelled)
                                .SetProperty(a => a.Result, "Cancelada por el kill switch.")
                                .SetProperty(a => a.FinishedAt, a => (DateTime?)AppDbContext.UtcNow()),
                            cancellationToken);
                db.KillSwitchEvents.Add(new KillSwitchEvent { Activated = activate, Reason = text, CancelledActions = cancelled, UserId = current.UserId });
                audit.Record(
                    "automation.kill-switch", nameof(AutomationPolicy), policy.Id.ToString(),
                    activate ? $"Activó el kill switch y canceló {cancelled} acciones: {text}" : $"Desactivó el kill switch: {text}");
                await db.SaveChangesAsync(cancellationToken);
                return cancelled;
            },
            cancellationToken);
    }

    /// <summary>Devuelve la política vigente; si la organización no guardó ninguna, la más restrictiva. No escribe.</summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>La política.</returns>
    public async Task<AutomationPolicy> PolicyAsync(CancellationToken cancellationToken)
    {
        return await db.AutomationPolicies.AsNoTracking().FirstOrDefaultAsync(cancellationToken) ?? new AutomationPolicy();
    }

    /// <summary>
    /// Pasa una acción por las guardas con los datos de hoy: kill switch, límites, lo ya comprometido por las demás
    /// acciones del día, derechos del video y respaldo de su hipótesis. No escribe. La usan el pedido, la
    /// aprobación y la ejecución.
    /// </summary>
    /// <param name="policy">Política vigente.</param>
    /// <param name="action">Acción a evaluar.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>El veredicto.</returns>
    public async Task<GuardVerdict> EvaluateAsync(AutomationPolicy policy, AutomationAction action, CancellationToken cancellationToken)
    {
        long recipeId = await db.CreativeVariants.AsNoTracking()
            .Where(v => v.Id == action.CreativeVariantId)
            .Select(v => v.CreativeRecipeId)
            .FirstAsync(cancellationToken);
        List<LearningConfidence> confidences = action.HypothesisId is null
            ? []
            : await db.Learnings.AsNoTracking()
                .Where(l => l.HypothesisId == action.HypothesisId && l.Status == LearningStatus.Active)
                .Select(l => l.Confidence)
                .ToListAsync(cancellationToken);
        return AutomationRules.Evaluate(
            new AutomationLimits(policy.KillSwitchActive, policy.Mode, policy.MaxBudgetPerAction, policy.MaxDailyBudget, policy.MaxActionsPerDay),
            action.DailyBudget,
            await UsageAsync(action.IsSimulation, action.Id, cancellationToken),
            confidences.Count == 0 ? null : confidences.Max(),
            await CompositionService.UnauthorizedSourceAsync(db, recipeId, cancellationToken),
            await anomalies.OpenCountAsync(cancellationToken));
    }

    // Lo comprometido hoy (fecha de la base) por las acciones en pie del mismo tipo, sin contar la que se evalúa.
    private async Task<DailyUsage> UsageAsync(bool simulation, long? exceptId, CancellationToken cancellationToken)
    {
        List<decimal> budgets = await db.AutomationActions.AsNoTracking()
            .Where(a => a.IsSimulation == simulation && Live.Contains(a.Status) && a.Id != exceptId
                && DateOnly.FromDateTime(a.CreatedAt) == DateOnly.FromDateTime(AppDbContext.UtcNow()))
            .Select(a => a.DailyBudget)
            .ToListAsync(cancellationToken);
        return new DailyUsage(budgets.Count, budgets.Sum());
    }

    private async Task<AutomationPolicy> TrackedPolicyAsync(CancellationToken cancellationToken)
    {
        AutomationPolicy? policy = await db.AutomationPolicies.FirstOrDefaultAsync(cancellationToken);
        if (policy is null)
        {
            policy = new AutomationPolicy();
            db.AutomationPolicies.Add(policy);
        }

        return policy;
    }

    private async Task<AutomationAction> PendingAsync(long id, CancellationToken cancellationToken)
    {
        AutomationAction action = await db.AutomationActions.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
            ?? throw new BusinessException(404, "La acción no existe.", "ACTION_NOT_FOUND");
        return action.Status == AutomationActionStatus.PendingApproval
            ? action
            : throw new BusinessException(409, "La acción ya no espera una decisión.", "ACTION_NOT_PENDING");
    }

    // Registra la decisión. La condición va en el UPDATE: de dos decisiones simultáneas, gana una.
    private async Task DecideAsync(long id, AutomationActionStatus status, string? note, string? result, CancellationToken cancellationToken)
    {
        string? text = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        bool finished = status != AutomationActionStatus.Approved;
        int rows = await db.AutomationActions.Where(a => a.Id == id && a.Status == AutomationActionStatus.PendingApproval).ExecuteUpdateAsync(
            s => s.SetProperty(a => a.Status, status)
                .SetProperty(a => a.DecisionNote, text)
                .SetProperty(a => a.Result, result)
                .SetProperty(a => a.DecidedByUserId, current.UserId)
                .SetProperty(a => a.DecidedAt, a => (DateTime?)AppDbContext.UtcNow())
                .SetProperty(a => a.FinishedAt, a => finished ? (DateTime?)AppDbContext.UtcNow() : null),
            cancellationToken);
        if (rows == 0)
        {
            throw new BusinessException(409, "La acción ya no espera una decisión.", "ACTION_NOT_PENDING");
        }
    }

    private async Task EnqueueAsync(long actionId, CancellationToken cancellationToken)
    {
        await jobs.EnqueueAsync(
            AutomationExecuteJobHandler.JobType,
            $"{AutomationExecuteJobHandler.JobType}:{actionId}",
            JsonSerializer.Serialize(new AutomationExecutePayload(actionId)),
            cancellationToken);
    }
}

/// <summary>
/// Trabajo que ejecuta una acción aprobada (<c>automation.execute</c>). Vuelve a pasar por las guardas justo antes
/// de actuar. Una simulación registra lo que se habría hecho y <b>nunca llama al publicador</b>. Una ejecución real
/// sólo llama al publicador si la ejecución real sigue habilitada, y con una clave de idempotencia propia de la
/// acción: repetir el trabajo no publica dos veces.
/// </summary>
/// <param name="db">Contexto de datos, con la organización del trabajo activa.</param>
/// <param name="automation">Guardas y política.</param>
/// <param name="publisher">Publicador de Meta.</param>
/// <param name="audit">Auditoría.</param>
public sealed class AutomationExecuteJobHandler(AppDbContext db, AutomationService automation, IMetaPublisher publisher, AuditService audit) : IJobHandler
{
    /// <summary>Tipo de trabajo que atiende.</summary>
    public const string JobType = "automation.execute";

    /// <inheritdoc />
    public string Type => JobType;

    /// <inheritdoc />
    /// <exception cref="PermanentJobException">Si la acción ya no existe o Meta la rechaza sin remedio.</exception>
    public async Task ExecuteAsync(JobExecution execution, CancellationToken cancellationToken)
    {
        AutomationExecutePayload? payload = JsonSerializer.Deserialize<AutomationExecutePayload>(execution.Job.PayloadJson);
        long actionId = payload?.ActionId ?? 0;
        AutomationAction action = await db.AutomationActions.FirstOrDefaultAsync(a => a.Id == actionId, cancellationToken)
            ?? throw new PermanentJobException("La acción ya no existe.");
        if (action.Status != AutomationActionStatus.Approved)
        {
            // Ya se ejecutó, o el kill switch la canceló mientras esperaba en la cola: no hay nada que hacer.
            return;
        }

        AutomationPolicy policy = await automation.PolicyAsync(cancellationToken);
        GuardVerdict verdict = await automation.EvaluateAsync(policy, action, cancellationToken);
        if (verdict.Blocked)
        {
            await FinishAsync(action, verdict.CancelledByKillSwitch ? AutomationActionStatus.Cancelled : AutomationActionStatus.Blocked, verdict.Reason, cancellationToken);
            return;
        }

        MetaAdSet adSet = await db.MetaAdSets.AsNoTracking().Include(s => s.Campaign).FirstAsync(s => s.Id == action.MetaAdSetId, cancellationToken);
        string budget = action.DailyBudget.ToString("0.00", CultureInfo.InvariantCulture);
        if (action.IsSimulation)
        {
            await FinishAsync(
                action, AutomationActionStatus.Simulated,
                $"Simulación: se habría creado el anuncio \"{action.AdName}\" en el conjunto \"{adSet.Campaign!.Name} · {adSet.Name}\" con un presupuesto "
                + $"diario de {budget}. No se escribió nada en Meta.",
                cancellationToken);
            return;
        }

        if (!policy.RealExecutionEnabled || !publisher.IsConfigured)
        {
            await FinishAsync(action, AutomationActionStatus.Blocked, "La ejecución real se deshabilitó después de aprobarla: no se publicó.", cancellationToken);
            return;
        }

        string videoPath = await db.CreativeVariants.AsNoTracking()
            .Where(v => v.Id == action.CreativeVariantId)
            .Select(v => v.StoragePath ?? string.Empty)
            .FirstAsync(cancellationToken);
        try
        {
            action.ExternalId = await publisher.PublishAsync(
                new MetaPublishRequest($"ci-action-{action.Id}", adSet.ExternalId, action.AdName, videoPath, action.DailyBudget), cancellationToken);
        }
        catch (MetaPublishException exception) when (!exception.Retryable)
        {
            await FinishAsync(action, AutomationActionStatus.Failed, Limit($"Meta rechazó la publicación: {exception.Message}"), cancellationToken);
            throw new PermanentJobException(exception.Message);
        }

        // El anuncio queda registrado y vinculado a su video: el linaje no depende de que alguien lo vincule a mano.
        if (!await db.MetaAds.AnyAsync(a => a.ExternalId == action.ExternalId, cancellationToken))
        {
            db.MetaAds.Add(new MetaAd
            {
                MetaAdSetId = action.MetaAdSetId,
                ExternalId = action.ExternalId,
                Name = action.AdName,
                CreativeVariantId = action.CreativeVariantId,
                LinkOrigin = AdLinkOrigin.Automatic,
            });
        }

        await FinishAsync(
            action, AutomationActionStatus.Executed,
            $"Se creó el anuncio \"{action.AdName}\" en Meta (identificador {action.ExternalId}) con un presupuesto diario de {budget}.", cancellationToken);
    }

    private async Task FinishAsync(AutomationAction action, AutomationActionStatus status, string result, CancellationToken cancellationToken)
    {
        action.Status = status;
        action.Result = result;
        audit.Record("automation.execute", nameof(AutomationAction), action.Id.ToString(), Limit($"{status}: {result}"));
        await db.SaveChangesAsync(cancellationToken);
        await db.AutomationActions.Where(a => a.Id == action.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.FinishedAt, a => (DateTime?)AppDbContext.UtcNow()), cancellationToken);
    }

    private static string Limit(string value)
    {
        return value.Length > 1000 ? value[..1000] : value;
    }
}

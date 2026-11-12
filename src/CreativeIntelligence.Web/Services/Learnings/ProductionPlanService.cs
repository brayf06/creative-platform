using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace CreativeIntelligence.Web.Services.Learnings;

/// <summary>
/// Reglas del plan de producción que no dependen de la base: qué se propone hacer con una hipótesis según la
/// evidencia que tiene, y cómo se explica. Producir en cantidad sólo se propone con un aprendizaje de confianza
/// media o alta; con menos, lo que se propone es probar.
/// </summary>
public static class ProductionPlanRules
{
    /// <summary>Qué se propone hacer con una hipótesis. Función pura.</summary>
    /// <param name="confidence">Confianza de su mejor aprendizaje vigente; nulo si no tiene.</param>
    /// <param name="hasPattern">Si hoy tiene detrás un patrón con evidencia.</param>
    /// <returns>La propuesta.</returns>
    public static PlanAction Action(LearningConfidence? confidence, bool hasPattern)
    {
        return confidence switch
        {
            LearningConfidence.High or LearningConfidence.Medium => PlanAction.Apply,
            LearningConfidence.Low => PlanAction.Test,
            _ => hasPattern ? PlanAction.Test : PlanAction.Explore,
        };
    }

    /// <summary>Qué papel cumple en el anuncio aquello de lo que trata la hipótesis.</summary>
    /// <param name="variable">Sobre qué trata.</param>
    /// <returns>El nombre del papel.</returns>
    public static string Role(ExperimentVariable variable)
    {
        return variable switch
        {
            ExperimentVariable.Opening => "Apertura (hook)",
            ExperimentVariable.Closing => "Cierre",
            ExperimentVariable.Development => "Desarrollo",
            ExperimentVariable.Subtitles or ExperimentVariable.Music or ExperimentVariable.Transition => "Terminación",
            _ => "Enfoque",
        };
    }

    /// <summary>
    /// Explica por qué una hipótesis entró en el plan y por qué se propone eso: qué parte del anuncio toca, qué
    /// evidencia la respalda, y qué factores sumaron más al puntaje y cuáles no tenían dato. Función pura.
    /// </summary>
    /// <param name="opportunity">La hipótesis puntuada.</param>
    /// <param name="learning">Su mejor aprendizaje vigente; nulo si no tiene.</param>
    /// <param name="weights">Ponderaciones con que se puntuó.</param>
    /// <returns>La explicación.</returns>
    public static string Justify(Opportunity opportunity, SupportingLearning? learning, PriorityWeights weights)
    {
        string subject = Role(opportunity.Variable) + (opportunity.Component is null ? ". " : $": «{opportunity.Component}». ");
        string proposal = Action(learning?.Confidence, opportunity.HasPattern) switch
        {
            PlanAction.Apply =>
                $"Se propone producir aplicándolo: lo respalda un aprendizaje vigente de confianza {Confidence(learning!.Confidence)} "
                + $"(«{learning.Statement}»). Vale para lo que ese aprendizaje dice que vale, no como regla general. ",
            PlanAction.Test when learning is not null =>
                $"Se propone probarlo con un experimento antes de producir en cantidad: el aprendizaje vigente es de confianza baja («{learning.Statement}»). ",
            PlanAction.Test =>
                "Se propone probarlo con un experimento antes de producir en cantidad: sólo hay un patrón, que es una correlación y no una causa. ",
            _ => "Se propone explorarlo en chico: es una conjetura, todavía sin datos que la respalden. ",
        };
        List<FactorValue> top = opportunity.Factors
            .Where(f => f.Value is > 0 && weights.Of(f.Factor) > 0)
            .OrderByDescending(f => weights.Of(f.Factor) * f.Value!.Value)
            .ThenBy(f => f.Factor)
            .Take(3)
            .ToList();
        string score = $"Puntaje {opportunity.Score.ToString("0.0", CultureInfo.InvariantCulture)} de 100 con las ponderaciones versión {weights.Version}.";
        string reasons = top.Count == 0
            ? string.Empty
            : " Lo que más sumó: " + string.Join(" ", top.Select(f => $"{Formatting.Factor(f.Factor).ToLowerInvariant()} — {f.Explanation}"));
        List<string> missing = opportunity.Factors.Where(f => f.Value is null).Select(f => Formatting.Factor(f.Factor).ToLowerInvariant()).ToList();
        string gaps = missing.Count == 0 ? string.Empty : $" Sin dato, y por eso sin sumar: {string.Join(", ", missing)}.";
        return subject + proposal + score + reasons + gaps;
    }

    private static string Confidence(LearningConfidence confidence)
    {
        return Formatting.Confidence(confidence).ToLowerInvariant();
    }
}

/// <summary>
/// Planes de producción de la organización activa. Un plan toma las hipótesis de mayor prioridad en ese momento y
/// dice, para cada una, qué se propone hacer y por qué, con la evidencia usada. No interviene ninguna IA y no
/// promete resultados. Es una foto: no se edita, y si los datos cambian se arma otro. Sólo cuentan los aprendizajes
/// vigentes: uno corregido o invalidado deja de respaldar a su hipótesis en el plan siguiente.
/// </summary>
/// <param name="db">Contexto de datos.</param>
/// <param name="priorities">Motor de priorización.</param>
/// <param name="audit">Auditoría.</param>
/// <param name="current">Usuario y organización que operan.</param>
public sealed class ProductionPlanService(AppDbContext db, PriorityService priorities, AuditService audit, CurrentContext current)
{
    /// <summary>Lista los planes, del más nuevo al más viejo. No escribe.</summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Hasta 100 planes.</returns>
    public async Task<List<ProductionPlanListItem>> ListAsync(CancellationToken cancellationToken)
    {
        return await db.ProductionPlans.AsNoTracking()
            .OrderByDescending(p => p.Id)
            .Take(100)
            .Select(p => new ProductionPlanListItem(p.Id, p.Name, p.Items.Count, p.WeightsVersion, p.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Arma un plan con las hipótesis de mayor puntaje de hoy. Las que no suman nada (puntaje cero) no entran.
    /// Escribe el plan, sus hipótesis y la auditoría.
    /// </summary>
    /// <param name="body">Nombre y cantidad de hipótesis.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Identificador del plan.</returns>
    /// <exception cref="BusinessException">409 <c>PLAN_NO_CANDIDATES</c> si no hay ninguna hipótesis en pie con puntaje.</exception>
    public async Task<long> CreateAsync(CreateProductionPlanBody body, CancellationToken cancellationToken)
    {
        PriorityBoard board = await priorities.BoardAsync(cancellationToken);
        List<Opportunity> chosen = board.Items.Where(i => i.Score > 0).Take(body.Count).ToList();
        if (chosen.Count == 0)
        {
            throw new BusinessException(
                409, "No hay hipótesis en pie con puntaje. Guardá hipótesis desde el análisis o escribilas, y valoralas.", "PLAN_NO_CANDIDATES");
        }

        List<long> ids = chosen.Select(c => c.HypothesisId).ToList();

        // De cada hipótesis, su aprendizaje vigente de mayor confianza; a igual confianza, el más nuevo.
        Dictionary<long, SupportingLearning> support = (await db.Learnings.AsNoTracking()
                .Where(l => l.HypothesisId != null && ids.Contains(l.HypothesisId.Value) && l.Status == LearningStatus.Active)
                .Select(l => new SupportRow { HypothesisId = l.HypothesisId!.Value, Learning = new SupportingLearning(l.Id, l.Statement, l.Recommendation, l.Confidence) })
                .ToListAsync(cancellationToken))
            .GroupBy(r => r.HypothesisId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.Learning).OrderByDescending(l => l.Confidence).ThenByDescending(l => l.Id).First());

        ProductionPlan plan = new()
        {
            Name = body.Name.Trim(),
            RequestedItems = body.Count,
            WeightsVersion = board.Weights.Version,
            CreatedByUserId = current.UserId,
            Items = chosen
                .Select((opportunity, index) =>
                {
                    SupportingLearning? learning = support.GetValueOrDefault(opportunity.HypothesisId);
                    return new ProductionPlanItem
                    {
                        Position = index + 1,
                        HypothesisId = opportunity.HypothesisId,
                        Statement = opportunity.Statement,
                        Variable = opportunity.Variable,
                        Component = opportunity.Component,
                        MediaAssetId = opportunity.Fragment?.AssetId,
                        StartMs = opportunity.Fragment?.StartMs,
                        EndMs = opportunity.Fragment?.EndMs,
                        Action = ProductionPlanRules.Action(learning?.Confidence, opportunity.HasPattern),
                        Score = opportunity.Score,
                        Justification = ProductionPlanRules.Justify(opportunity, learning, board.Weights),
                        FactorsJson = JsonSerializer.Serialize(opportunity.Factors),
                        LearningId = learning?.Id,
                        Recommendation = learning?.Recommendation,
                    };
                })
                .ToList(),
        };
        return await db.InTransactionAsync(
            async () =>
            {
                db.ProductionPlans.Add(plan);
                await db.SaveChangesAsync(cancellationToken);
                audit.Record(
                    "production-plan.create", nameof(ProductionPlan), plan.Id.ToString(),
                    $"Armó el plan de producción \"{plan.Name}\" con {plan.Items.Count} hipótesis y las ponderaciones versión {plan.WeightsVersion}.");
                await db.SaveChangesAsync(cancellationToken);
                return plan.Id;
            },
            cancellationToken);
    }

    /// <summary>Devuelve un plan con sus hipótesis y sus explicaciones tal como se guardaron, y cómo está hoy el
    /// aprendizaje que respaldaba a cada una. No escribe.</summary>
    /// <param name="id">Plan.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>El plan.</returns>
    /// <exception cref="BusinessException">404 <c>PRODUCTION_PLAN_NOT_FOUND</c> si no existe en la organización activa.</exception>
    public async Task<ProductionPlanDetail> GetAsync(long id, CancellationToken cancellationToken)
    {
        ProductionPlan plan = await db.ProductionPlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new BusinessException(404, "El plan de producción no existe.", "PRODUCTION_PLAN_NOT_FOUND");
        List<ProductionPlanItemRow> rows = await db.ProductionPlanItems.AsNoTracking()
            .Where(i => i.ProductionPlanId == id)
            .OrderBy(i => i.Position)
            .Select(i => new ProductionPlanItemRow
            {
                Item = i,
                LearningStatus = db.Learnings.Where(l => l.Id == i.LearningId).Select(l => (LearningStatus?)l.Status).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);
        int currentVersion = (await priorities.WeightsAsync(cancellationToken)).Current.Version;
        return new ProductionPlanDetail(
            plan.Id, plan.Name, plan.RequestedItems, plan.WeightsVersion, currentVersion, plan.CreatedAt,
            rows.Select(r => new ProductionPlanItemView(
                    r.Item.Position, r.Item.HypothesisId, r.Item.Statement, r.Item.Variable, r.Item.Component, r.Item.MediaAssetId, r.Item.StartMs,
                    r.Item.EndMs, r.Item.Action, r.Item.Score, r.Item.Justification,
                    JsonSerializer.Deserialize<List<FactorValue>>(r.Item.FactorsJson) ?? [], r.Item.LearningId, r.Item.Recommendation, r.LearningStatus))
                .ToList());
    }
}

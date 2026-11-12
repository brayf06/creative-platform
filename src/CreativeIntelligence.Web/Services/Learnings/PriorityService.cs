using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace CreativeIntelligence.Web.Services.Learnings;

/// <summary>
/// Reglas del motor de priorización que no dependen de la base: qué vale cada factor para una hipótesis y cómo se
/// combinan en un puntaje. Todo sale de datos del sistema o de una valoración explícita de una persona; no hay
/// ningún puntaje de IA. El puntaje ordena qué conviene producir primero: <b>no predice ROAS ni ningún resultado</b>.
/// Un factor sin dato no suma: lo que no se sabe no da prioridad. Las escalas de cada factor son puntos de partida
/// elegidos al construir el motor, sin calibrar con datos reales.
/// </summary>
public static class PriorityRules
{
    /// <summary>Ponderaciones de fábrica (versión 0): rigen hasta que la organización guarde las suyas. Pesan más la
    /// evidencia y el rendimiento observado que lo que valora una persona.</summary>
    public static readonly PriorityWeights DefaultWeights = new(0, 6, 8, 3, 3, 5, 6, 3, 5, 4, 4);

    /// <summary>Los factores, en el orden en que se muestran.</summary>
    public static readonly PriorityFactor[] All = Enum.GetValues<PriorityFactor>();

    /// <summary>Calcula el valor de cada factor para una hipótesis. Función pura.</summary>
    /// <param name="input">Lo que se sabe de la hipótesis.</param>
    /// <returns>Los diez factores, cada uno con su valor (o sin dato) y de dónde sale.</returns>
    public static List<FactorValue> Factors(OpportunityInput input)
    {
        return
        [
            Historical(input.Pattern),
            Evidence(input),
            Similarity(input),
            Novelty(input),
            Assessed(PriorityFactor.AudienceRelevance, input.Assessment.AudienceRelevance, false, "Relevancia para el público"),
            Material(input),
            Assessed(PriorityFactor.ProductionCost, input.Assessment.ProductionCost, true, "Costo de producción"),
            Assessed(PriorityFactor.AdRisk, input.Assessment.AdRisk, true, "Riesgo publicitario"),
            Fatigue(input),
            Assessed(PriorityFactor.PotentialMargin, input.Assessment.PotentialMargin, false, "Margen potencial"),
        ];
    }

    /// <summary>
    /// Combina los factores en un puntaje de 0 a 100: la suma de peso por valor sobre la suma de todos los pesos.
    /// Un factor sin dato cuenta como cero. Para unos mismos factores y unas mismas ponderaciones da siempre lo
    /// mismo. Función pura.
    /// </summary>
    /// <param name="weights">Ponderaciones.</param>
    /// <param name="factors">Valor de cada factor.</param>
    /// <returns>El puntaje, con un decimal; 0 si todos los pesos son cero.</returns>
    public static decimal Score(PriorityWeights weights, IReadOnlyList<FactorValue> factors)
    {
        int total = All.Sum(weights.Of);
        return total == 0 ? 0 : Math.Round(factors.Sum(f => weights.Of(f.Factor) * (f.Value ?? 0)) / total * 100, 1);
    }

    private static FactorValue Historical(ComponentGroup? pattern)
    {
        if (pattern is null || pattern.Evidence == EvidenceLevel.Insufficient || pattern.CtrSignal == SignalStrength.None
            || pattern.Metrics.Ctr is null || pattern.Rest.Ctr is null or 0)
        {
            return new FactorValue(PriorityFactor.HistoricalPerformance, null, "Sin dato: no hay un patrón con evidencia que compare su CTR con el del resto.");
        }

        // La diferencia relativa de CTR, acotada a ±100 %, llevada a la escala de 0 a 1 (igual que el resto = 0,5).
        decimal lift = Math.Clamp((pattern.Metrics.Ctr.Value - pattern.Rest.Ctr.Value) / pattern.Rest.Ctr.Value, -1, 1);
        return new FactorValue(
            PriorityFactor.HistoricalPerformance, (lift + 1) / 2,
            $"CTR {Percent(pattern.Metrics.Ctr.Value)} en {pattern.Ads} anuncios que lo usan frente a {Percent(pattern.Rest.Ctr.Value)} del resto. Es una correlación.");
    }

    private static FactorValue Evidence(OpportunityInput input)
    {
        return input.BestConfidence switch
        {
            LearningConfidence.High => new FactorValue(PriorityFactor.EvidenceQuality, 1, "Tiene un aprendizaje vigente de confianza alta."),
            LearningConfidence.Medium => new FactorValue(PriorityFactor.EvidenceQuality, 0.66m, "Tiene un aprendizaje vigente de confianza media."),
            LearningConfidence.Low => new FactorValue(PriorityFactor.EvidenceQuality, 0.33m, "Tiene un aprendizaje vigente de confianza baja."),
            _ => input.Pattern?.Evidence switch
            {
                EvidenceLevel.Moderate => new FactorValue(PriorityFactor.EvidenceQuality, 0.25m, "Sin aprendizaje vigente; el patrón tiene evidencia moderada."),
                EvidenceLevel.Preliminary => new FactorValue(PriorityFactor.EvidenceQuality, 0.15m, "Sin aprendizaje vigente; el patrón tiene evidencia preliminar."),
                _ => new FactorValue(PriorityFactor.EvidenceQuality, 0, "Sin aprendizaje vigente ni patrón con evidencia: es una conjetura."),
            },
        };
    }

    private static FactorValue Similarity(OpportunityInput input)
    {
        if (input.RecipesUsing is null || input.Recipes == 0)
        {
            return new FactorValue(PriorityFactor.Similarity, null, "Sin dato: no se puede medir contra las recetas existentes.");
        }

        return new FactorValue(
            PriorityFactor.Similarity, 1 - ((decimal)input.RecipesUsing.Value / input.Recipes),
            $"{input.RecipesUsing} de {input.Recipes} recetas ya lo usan; cuanto más repetido, menos suma.");
    }

    private static FactorValue Novelty(OpportunityInput input)
    {
        int ads = input.Pattern?.Ads ?? 0;
        return new FactorValue(
            PriorityFactor.Novelty, 1m / (1 + ads + input.Experiments),
            $"{ads} anuncio(s) publicado(s) con esto y {input.Experiments} experimento(s); cuanto menos probado, más suma.");
    }

    private static FactorValue Material(OpportunityInput input)
    {
        return input.MaterialAvailable switch
        {
            true => new FactorValue(PriorityFactor.MaterialAvailability, 1, "El material que necesita está y tiene derechos vigentes."),
            false => new FactorValue(PriorityFactor.MaterialAvailability, 0, "El material que necesita no tiene autorización de reutilización vigente."),
            _ => new FactorValue(PriorityFactor.MaterialAvailability, null, "Sin dato: el sistema no sabe qué material necesita."),
        };
    }

    private static FactorValue Fatigue(OpportunityInput input)
    {
        if (input.Pattern is null || input.Pattern.Ads == 0)
        {
            return new FactorValue(PriorityFactor.Fatigue, null, "Sin dato: no hay anuncios publicados con esto.");
        }

        return new FactorValue(
            PriorityFactor.Fatigue, 1 - ((decimal)input.FatiguedAds / input.Pattern.Ads),
            $"{input.FatiguedAds} de {input.Pattern.Ads} anuncios que lo usan muestran posible fatiga.");
    }

    // Un factor que valora una persona. En costo y riesgo, alto resta.
    private static FactorValue Assessed(PriorityFactor factor, AssessmentLevel? level, bool inverse, string name)
    {
        if (level is null)
        {
            return new FactorValue(factor, null, "Sin dato: nadie lo valoró en la hipótesis.");
        }

        decimal value = level switch
        {
            AssessmentLevel.High => 1,
            AssessmentLevel.Medium => 0.5m,
            _ => 0,
        };
        string text = level switch
        {
            AssessmentLevel.High => "alto",
            AssessmentLevel.Medium => "medio",
            _ => "bajo",
        };
        return new FactorValue(factor, inverse ? 1 - value : value, $"{name} {text}, según la valoración de una persona.");
    }

    private static string Percent(decimal value)
    {
        return (value * 100).ToString("0.00", CultureInfo.InvariantCulture) + " %";
    }
}

/// <summary>
/// Motor de priorización de la organización activa: ordena las hipótesis en pie por un puntaje que combina diez
/// factores con ponderaciones configurables. Las ponderaciones se versionan (cambiarlas crea una versión, nunca
/// pisa la anterior) y se auditan. El puntaje no se guarda: se calcula con los datos de hoy.
/// </summary>
/// <param name="db">Contexto de datos.</param>
/// <param name="performance">Análisis por componente y recetas.</param>
/// <param name="audit">Auditoría.</param>
/// <param name="current">Usuario y organización que operan.</param>
public sealed class PriorityService(AppDbContext db, PerformanceService performance, AuditService audit, CurrentContext current)
{
    /// <summary>Ordena por prioridad las hipótesis que no se descartaron. No escribe.</summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Hasta 200 hipótesis, del puntaje más alto al más bajo, con su desglose.</returns>
    public async Task<PriorityBoard> BoardAsync(CancellationToken cancellationToken)
    {
        PriorityWeights weights = await CurrentAsync(cancellationToken);
        List<Hypothesis> hypotheses = await db.Hypotheses.AsNoTracking()
            .Where(h => h.DiscardedAt == null)
            .OrderByDescending(h => h.Id)
            .Take(200)
            .ToListAsync(cancellationToken);
        List<long> ids = hypotheses.Select(h => h.Id).ToList();
        ILookup<long, bool> experiments = (await db.Experiments.AsNoTracking()
                .Where(e => e.HypothesisId != null && ids.Contains(e.HypothesisId.Value))
                .Select(e => new HypothesisExperimentRow(e.HypothesisId!.Value, e.ConcludedAt != null))
                .ToListAsync(cancellationToken))
            .ToLookup(e => e.HypothesisId, e => e.Concluded);
        ILookup<long, LearningConfidence> learnings = (await db.Learnings.AsNoTracking()
                .Where(l => l.HypothesisId != null && ids.Contains(l.HypothesisId.Value) && l.Status == LearningStatus.Active)
                .Select(l => new HypothesisLearningRow(l.HypothesisId!.Value, l.Confidence))
                .ToListAsync(cancellationToken))
            .ToLookup(l => l.HypothesisId, l => l.Confidence);

        // Un análisis por cada combinación distinta de campaña y período, no uno por hipótesis.
        Dictionary<(long?, DateOnly?, DateOnly?), PerformanceAnalysis> analyses = [];
        foreach (Hypothesis hypothesis in hypotheses.Where(h => h.PatternDimension is not null))
        {
            (long?, DateOnly?, DateOnly?) key = (hypothesis.PatternCampaignId, hypothesis.PatternFrom, hypothesis.PatternTo);
            if (!analyses.ContainsKey(key))
            {
                analyses[key] = await performance.AnalyzeAsync(
                    new PerformanceFilter { CampaignId = key.Item1, From = key.Item2, To = key.Item3 }, cancellationToken);
            }
        }

        List<AdComponents> recipes = await performance.RecipeComponentsAsync(cancellationToken);
        List<ComponentDimension> usage = PerformanceService.Dimensions(recipes);
        List<long> assetIds = analyses.Values
            .SelectMany(a => a.Dimensions)
            .SelectMany(d => d.Groups)
            .Where(g => g.Fragment is not null)
            .Select(g => g.Fragment!.AssetId)
            .Distinct()
            .ToList();
        Dictionary<long, bool> rights = (await db.MediaAssets.AsNoTracking()
                .Where(m => assetIds.Contains(m.Id))
                .Select(m => new AssetRightsRow(
                    m.Id, m.ReuseAuthorized && (m.RightsExpireOn == null || m.RightsExpireOn >= DateOnly.FromDateTime(AppDbContext.UtcNow()))))
                .ToListAsync(cancellationToken))
            .ToDictionary(r => r.AssetId, r => r.Authorized);
        bool music = await db.MusicTracks.AsNoTracking().AnyAsync(
            m => m.Status == MediaProcessingStatus.Ready && m.ReuseAuthorized
                && (m.RightsExpireOn == null || m.RightsExpireOn >= DateOnly.FromDateTime(AppDbContext.UtcNow())),
            cancellationToken);

        List<Opportunity> items = [];
        foreach (Hypothesis hypothesis in hypotheses)
        {
            PerformanceAnalysis? analysis = hypothesis.PatternDimension is null
                ? null
                : analyses[(hypothesis.PatternCampaignId, hypothesis.PatternFrom, hypothesis.PatternTo)];
            ComponentGroup? pattern = Find(analysis?.Dimensions, hypothesis);
            HashSet<long> fatigued = analysis?.Fatigue.Select(f => f.AdId).ToHashSet() ?? [];
            List<LearningConfidence> confidences = learnings[hypothesis.Id].ToList();
            List<bool> concluded = experiments[hypothesis.Id].ToList();
            OpportunityInput input = new()
            {
                Pattern = pattern,
                BestConfidence = confidences.Count == 0 ? null : confidences.Max(),
                Experiments = concluded.Count,
                RecipesUsing = pattern is null ? null : Find(usage, hypothesis)?.Ads ?? 0,
                Recipes = recipes.Count,
                MaterialAvailable = Available(hypothesis, pattern, rights, music),
                FatiguedAds = pattern?.AdIds.Count(fatigued.Contains) ?? 0,
                Assessment = new HypothesisAssessment(
                    hypothesis.AudienceRelevance, hypothesis.ProductionCost, hypothesis.AdRisk, hypothesis.PotentialMargin),
            };
            List<FactorValue> factors = PriorityRules.Factors(input);
            items.Add(new Opportunity(
                hypothesis.Id, hypothesis.Statement, hypothesis.Origin,
                LearningRules.State(false, concluded.Count, concluded.Count(c => !c), confidences.Count),
                PriorityRules.Score(weights, factors), factors.Count(f => f.Value is not null), factors,
                hypothesis.Variable, hypothesis.PatternLabel, pattern?.Fragment, pattern is not null && LearningRules.FromPattern(pattern) is not null));
        }

        return new PriorityBoard(weights, items.OrderByDescending(i => i.Score).ThenBy(i => i.HypothesisId).ToList());
    }

    /// <summary>Devuelve las ponderaciones vigentes y las versiones guardadas. No escribe.</summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Las vigentes y hasta 50 versiones, de la más nueva a la más vieja.</returns>
    public async Task<PriorityWeightsView> WeightsAsync(CancellationToken cancellationToken)
    {
        List<WeightSetItem> history = await db.PriorityWeightSets.AsNoTracking()
            .OrderByDescending(w => w.Version)
            .Take(50)
            .Select(w => new WeightSetItem(
                new PriorityWeights(
                    w.Version, w.HistoricalPerformance, w.EvidenceQuality, w.Similarity, w.Novelty, w.AudienceRelevance, w.MaterialAvailability,
                    w.ProductionCost, w.AdRisk, w.Fatigue, w.PotentialMargin),
                w.Note, w.CreatedAt))
            .ToListAsync(cancellationToken);
        return new PriorityWeightsView(history.Count > 0 ? history[0].Weights : PriorityRules.DefaultWeights, history);
    }

    /// <summary>Guarda ponderaciones nuevas como una versión más; las anteriores quedan intactas. Escribe la
    /// versión y la auditoría.</summary>
    /// <param name="body">Pesos y motivo del cambio.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>El número de la versión nueva.</returns>
    /// <exception cref="BusinessException">400 <c>WEIGHTS_INVALID</c> si todos los pesos son cero.</exception>
    public async Task<int> SaveWeightsAsync(PriorityWeightsBody body, CancellationToken cancellationToken)
    {
        PriorityWeightSet set = new()
        {
            HistoricalPerformance = body.HistoricalPerformance,
            EvidenceQuality = body.EvidenceQuality,
            Similarity = body.Similarity,
            Novelty = body.Novelty,
            AudienceRelevance = body.AudienceRelevance,
            MaterialAvailability = body.MaterialAvailability,
            ProductionCost = body.ProductionCost,
            AdRisk = body.AdRisk,
            Fatigue = body.Fatigue,
            PotentialMargin = body.PotentialMargin,
            Note = body.Note.Trim(),
            CreatedByUserId = current.UserId,
        };
        int[] values =
        [
            set.HistoricalPerformance, set.EvidenceQuality, set.Similarity, set.Novelty, set.AudienceRelevance, set.MaterialAvailability,
            set.ProductionCost, set.AdRisk, set.Fatigue, set.PotentialMargin,
        ];
        if (values.All(v => v == 0))
        {
            throw new BusinessException(400, "Al menos un factor tiene que pesar algo.", "WEIGHTS_INVALID");
        }

        return await db.InTransactionAsync(
            async () =>
            {
                // El índice único de organización y versión hace fallar a la segunda de dos escrituras simultáneas.
                set.Version = (await db.PriorityWeightSets.MaxAsync(w => (int?)w.Version, cancellationToken) ?? 0) + 1;
                db.PriorityWeightSets.Add(set);
                await db.SaveChangesAsync(cancellationToken);
                audit.Record(
                    "priority.weights", nameof(PriorityWeightSet), set.Id.ToString(),
                    $"Guardó la versión {set.Version} de las ponderaciones ({string.Join(", ", values)}): {set.Note}");
                await db.SaveChangesAsync(cancellationToken);
                return set.Version;
            },
            cancellationToken);
    }

    private async Task<PriorityWeights> CurrentAsync(CancellationToken cancellationToken)
    {
        return (await WeightsAsync(cancellationToken)).Current;
    }

    private static ComponentGroup? Find(List<ComponentDimension>? dimensions, Hypothesis hypothesis)
    {
        return dimensions?.FirstOrDefault(d => d.Name == hypothesis.PatternDimension)?.Groups.FirstOrDefault(g => g.Key == hypothesis.PatternKey);
    }

    // Si el material que la hipótesis necesita está: un fragmento, por los derechos de su original; la música, si
    // hay alguna pista autorizada; subtítulos y transición no necesitan material. Del resto no se sabe.
    private static bool? Available(Hypothesis hypothesis, ComponentGroup? pattern, Dictionary<long, bool> rights, bool music)
    {
        if (pattern?.Fragment is not null)
        {
            return rights.GetValueOrDefault(pattern.Fragment.AssetId);
        }

        return hypothesis.PatternDimension switch
        {
            "music" => hypothesis.PatternKey != "yes" || music,
            "subtitles" or "transition" => true,
            _ => null,
        };
    }
}

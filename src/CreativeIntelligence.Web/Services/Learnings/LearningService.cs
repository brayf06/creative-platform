using Microsoft.EntityFrameworkCore;

namespace CreativeIntelligence.Web.Services.Learnings;

/// <summary>
/// Reglas de la memoria de aprendizaje que no dependen de la base: qué confianza merecen unos datos, cómo se
/// enuncia una hipótesis a partir de un patrón y en qué punto está una hipótesis. La confianza la decide el sistema
/// y nunca una persona: una correlación no pasa de media, y sólo un experimento limpio y terminado llega a alta.
/// </summary>
public static class LearningRules
{
    /// <summary>Versión del criterio de confianza. Cambiar las reglas de <see cref="FromExperiment"/> o
    /// <see cref="FromPattern"/> obliga a cambiarla: queda guardada en cada aprendizaje.</summary>
    public const string CriteriaVersion = "learning-v1";

    /// <summary>La confianza que merece el resultado de un experimento. Función pura.</summary>
    /// <param name="result">Resultado calculado.</param>
    /// <returns>La confianza y su porqué, o nulo si los datos no alcanzan para afirmar nada.</returns>
    public static LearningBasis? FromExperiment(ExperimentResult result)
    {
        if (result.Verdict == ExperimentVerdict.NotEnoughData)
        {
            return null;
        }

        if (result.Provisional)
        {
            return new LearningBasis(
                LearningConfidence.Low, "Baja: el experimento no terminó, y mirar antes de tiempo aumenta el riesgo de ver una diferencia que es azar.");
        }

        if (result.Warnings.Count > 0)
        {
            return new LearningBasis(
                LearningConfidence.Low, "Baja: el experimento tiene problemas de diseño y lo observado puede deberse a ellos.");
        }

        return result.Verdict switch
        {
            ExperimentVerdict.ClearDifference => new LearningBasis(
                LearningConfidence.High,
                "Alta: experimento terminado, sin problemas de diseño visibles y con una diferencia clara. Vale para esa audiencia y ese período."),
            ExperimentVerdict.ProbableDifference => new LearningBasis(
                LearningConfidence.Medium,
                "Media: experimento terminado y sin problemas de diseño visibles, con una diferencia probable. Conviene repetirlo."),
            _ => new LearningBasis(LearningConfidence.Low, "Baja: no se vio una diferencia, y eso no prueba que no la haya: puede faltar volumen."),
        };
    }

    /// <summary>La confianza que merece un patrón del análisis por componente. Es una correlación: nunca llega a
    /// alta. Función pura.</summary>
    /// <param name="group">El grupo de anuncios que comparten el componente.</param>
    /// <returns>La confianza y su porqué, o nulo si no hay evidencia o no hay con qué comparar.</returns>
    public static LearningBasis? FromPattern(ComponentGroup group)
    {
        if (group.Evidence == EvidenceLevel.Insufficient || group.CtrSignal == SignalStrength.None)
        {
            return null;
        }

        return group.Evidence == EvidenceLevel.Moderate && group.CtrSignal == SignalStrength.Clear
            ? new LearningBasis(
                LearningConfidence.Medium,
                "Media: correlación clara con evidencia moderada (varios anuncios de más de una campaña). No prueba una causa: falta un experimento.")
            : new LearningBasis(
                LearningConfidence.Low,
                "Baja: es una correlación, con evidencia "
                + (group.Evidence == EvidenceLevel.Moderate ? "moderada" : "preliminar")
                + (group.CtrSignal == SignalStrength.NotDistinguishable ? " y una diferencia que no se distingue del azar" : string.Empty)
                + ". No prueba una causa: falta un experimento.");
    }

    /// <summary>Sobre qué parte de la creatividad trata una dimensión del análisis.</summary>
    /// <param name="dimension">Clave de la dimensión.</param>
    /// <returns>La variable; lo que el sistema no sabe comparar entre creatividades es <see cref="ExperimentVariable.Other"/>.</returns>
    public static ExperimentVariable VariableOf(string dimension)
    {
        return dimension switch
        {
            "opening" => ExperimentVariable.Opening,
            "closing" => ExperimentVariable.Closing,
            "fragment" => ExperimentVariable.Development,
            "subtitles" => ExperimentVariable.Subtitles,
            "music" => ExperimentVariable.Music,
            "transition" => ExperimentVariable.Transition,
            _ => ExperimentVariable.Other,
        };
    }

    /// <summary>Enuncia como conjetura lo que sugiere un patrón. Función pura.</summary>
    /// <param name="dimension">Clave de la dimensión.</param>
    /// <param name="label">Nombre del componente.</param>
    /// <param name="higher">Si el CTR del grupo fue mayor que el del resto.</param>
    /// <returns>El enunciado.</returns>
    public static string PatternStatement(string dimension, string label, bool higher)
    {
        string effect = higher ? "mejora" : "empeora";
        return dimension switch
        {
            "opening" => $"Abrir con «{label}» {effect} el CTR frente a otras aperturas.",
            "closing" => $"Cerrar con «{label}» {effect} el CTR frente a otros cierres.",
            "fragment" => $"Incluir «{label}» {effect} el CTR.",
            "structure" => $"La estructura «{label}» {effect} el CTR frente a otras estructuras.",
            _ => $"«{label}» {effect} el CTR frente al resto.",
        };
    }

    /// <summary>En qué punto está una hipótesis. Función pura.</summary>
    /// <param name="discarded">Si una persona la descartó.</param>
    /// <param name="experiments">Experimentos que la prueban.</param>
    /// <param name="openExperiments">De esos, los que no se cerraron.</param>
    /// <param name="activeLearnings">Aprendizajes vigentes que dejó.</param>
    /// <returns>El estado.</returns>
    public static HypothesisState State(bool discarded, int experiments, int openExperiments, int activeLearnings)
    {
        return discarded ? HypothesisState.Discarded
            : activeLearnings > 0 ? HypothesisState.Learned
            : openExperiments > 0 ? HypothesisState.Testing
            : experiments > 0 ? HypothesisState.Tested
            : HypothesisState.Open;
    }
}

/// <summary>
/// Hipótesis de la organización activa: conjeturas sobre qué hace rendir mejor a una creatividad. Salen de un
/// patrón del análisis por componente o las escribe una persona, y se prueban con experimentos.
/// </summary>
/// <param name="db">Contexto de datos.</param>
/// <param name="performance">Análisis por componente.</param>
/// <param name="audit">Auditoría.</param>
/// <param name="current">Usuario y organización que operan.</param>
public sealed class HypothesisService(AppDbContext db, PerformanceService performance, AuditService audit, CurrentContext current)
{
    /// <summary>Lista las hipótesis, de la más nueva a la más vieja, con su estado. No escribe.</summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Hasta 200 hipótesis.</returns>
    public async Task<List<HypothesisListItem>> ListAsync(CancellationToken cancellationToken)
    {
        List<HypothesisRow> rows = await Rows(db.Hypotheses.AsNoTracking().OrderByDescending(h => h.Id).Take(200)).ToListAsync(cancellationToken);
        return rows
            .Select(r => new HypothesisListItem(r.Id, r.Statement, r.Origin, r.Variable, StateOf(r), r.Experiments, r.ActiveLearnings, r.CreatedAt))
            .ToList();
    }

    /// <summary>Guarda una hipótesis escrita por una persona. Escribe la hipótesis y la auditoría.</summary>
    /// <param name="body">Enunciado y sobre qué trata.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Identificador de la hipótesis.</returns>
    public async Task<long> CreateAsync(CreateHypothesisBody body, CancellationToken cancellationToken)
    {
        Hypothesis hypothesis = new()
        {
            Statement = body.Statement.Trim(),
            Origin = HypothesisOrigin.Manual,
            Variable = body.Variable,
            CreatedByUserId = current.UserId,
        };
        return await SaveAsync(hypothesis, "Escribió una hipótesis.", cancellationToken);
    }

    /// <summary>
    /// Guarda como hipótesis lo que sugiere un patrón del análisis por componente. El patrón se vuelve a calcular
    /// acá: no se confía en lo que mande el navegador. Si ya hay una hipótesis en pie sobre el mismo componente,
    /// devuelve ésa. Escribe la hipótesis y la auditoría.
    /// </summary>
    /// <param name="body">Qué grupo, de qué análisis.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Identificador de la hipótesis.</returns>
    /// <exception cref="BusinessException">409 <c>PATTERN_NO_EVIDENCE</c> si el componente no aparece en el análisis
    /// o no tiene evidencia suficiente para sugerir nada.</exception>
    public async Task<long> FromPatternAsync(PatternBody body, CancellationToken cancellationToken)
    {
        ComponentGroup? group = await PatternAsync(body.Dimension, body.Key, body.CampaignId, body.From, body.To, cancellationToken);
        if (group is null || LearningRules.FromPattern(group) is null)
        {
            throw new BusinessException(
                409, "Ese componente no tiene evidencia suficiente para sugerir una hipótesis.", "PATTERN_NO_EVIDENCE");
        }

        long? existing = await db.Hypotheses.AsNoTracking()
            .Where(h => h.PatternDimension == body.Dimension && h.PatternKey == body.Key && h.DiscardedAt == null)
            .Select(h => (long?)h.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (existing is not null)
        {
            return existing.Value;
        }

        string label = Label(group);
        Hypothesis hypothesis = new()
        {
            Statement = LearningRules.PatternStatement(body.Dimension, label, group.CtrHigher),
            Origin = HypothesisOrigin.Pattern,
            Variable = LearningRules.VariableOf(body.Dimension),
            PatternDimension = body.Dimension,
            PatternKey = body.Key,
            PatternLabel = label,
            PatternReading = group.Reading,
            PatternCampaignId = body.CampaignId,
            PatternFrom = body.From,
            PatternTo = body.To,
            CreatedByUserId = current.UserId,
        };
        return await SaveAsync(hypothesis, $"Guardó como hipótesis el patrón de «{label}».", cancellationToken);
    }

    /// <summary>Devuelve una hipótesis con su patrón recalculado, sus experimentos y sus aprendizajes. No escribe.</summary>
    /// <param name="id">Hipótesis.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>La hipótesis.</returns>
    /// <exception cref="BusinessException">404 <c>HYPOTHESIS_NOT_FOUND</c> si no existe en la organización activa.</exception>
    public async Task<HypothesisDetail> GetAsync(long id, CancellationToken cancellationToken)
    {
        Hypothesis hypothesis = await FindAsync(id, cancellationToken);
        HypothesisRow row = await Rows(db.Hypotheses.AsNoTracking().Where(h => h.Id == id)).FirstAsync(cancellationToken);
        HypothesisPattern? pattern = null;
        bool canLearn = false;
        if (hypothesis.Origin == HypothesisOrigin.Pattern)
        {
            ComponentGroup? group = await PatternAsync(hypothesis, cancellationToken);
            string? campaign = await db.MetaCampaigns.AsNoTracking()
                .Where(c => c.Id == hypothesis.PatternCampaignId)
                .Select(c => c.Name)
                .FirstOrDefaultAsync(cancellationToken);
            pattern = new HypothesisPattern(
                hypothesis.PatternLabel ?? string.Empty, campaign, hypothesis.PatternFrom, hypothesis.PatternTo,
                hypothesis.PatternReading ?? string.Empty, group?.Reading, group?.Evidence);
            canLearn = group is not null && LearningRules.FromPattern(group) is not null;
        }

        List<ExperimentListItem> experiments = await db.Experiments.AsNoTracking()
            .Where(e => e.HypothesisId == id)
            .OrderByDescending(e => e.Id)
            .Select(e => new ExperimentListItem(e.Id, e.Name, e.Variable, e.Metric, e.StartDate, e.EndDate, e.Arms.Count, e.ConcludedAt != null))
            .ToListAsync(cancellationToken);
        List<LearningListItem> learnings = await db.Learnings.AsNoTracking()
            .Where(l => l.HypothesisId == id)
            .OrderByDescending(l => l.Id)
            .Select(l => new LearningListItem(
                l.Id, l.Statement, l.Recommendation, l.Confidence, l.Status, l.ExperimentId != null, l.PeriodStart, l.PeriodEnd, l.CreatedAt))
            .ToListAsync(cancellationToken);
        return new HypothesisDetail(
            hypothesis.Id, hypothesis.Statement, hypothesis.Origin, hypothesis.Variable, StateOf(row), hypothesis.DiscardReason, hypothesis.CreatedAt,
            pattern, canLearn, experiments, learnings,
            new HypothesisAssessment(hypothesis.AudienceRelevance, hypothesis.ProductionCost, hypothesis.AdRisk, hypothesis.PotentialMargin));
    }

    /// <summary>Guarda lo que una persona valora de una hipótesis para priorizarla (público, costo, riesgo y
    /// margen): lo que el sistema no puede medir. Un campo vacío queda sin valorar. Escribe la hipótesis y la
    /// auditoría.</summary>
    /// <param name="id">Hipótesis.</param>
    /// <param name="body">Valoraciones.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <exception cref="BusinessException">404 <c>HYPOTHESIS_NOT_FOUND</c>.</exception>
    public async Task AssessAsync(long id, AssessHypothesisBody body, CancellationToken cancellationToken)
    {
        await FindAsync(id, cancellationToken);
        await db.InTransactionAsync(
            async () =>
            {
                await db.Hypotheses.Where(h => h.Id == id).ExecuteUpdateAsync(
                    s => s.SetProperty(h => h.AudienceRelevance, body.AudienceRelevance)
                        .SetProperty(h => h.ProductionCost, body.ProductionCost)
                        .SetProperty(h => h.AdRisk, body.AdRisk)
                        .SetProperty(h => h.PotentialMargin, body.PotentialMargin),
                    cancellationToken);
                audit.Record(
                    "hypothesis.assess", nameof(Hypothesis), id.ToString(),
                    $"Valoró la hipótesis: público {Level(body.AudienceRelevance)}, costo {Level(body.ProductionCost)}, "
                    + $"riesgo {Level(body.AdRisk)}, margen {Level(body.PotentialMargin)}.");
                await db.SaveChangesAsync(cancellationToken);
                return true;
            },
            cancellationToken);
    }

    /// <summary>Descarta una hipótesis: deja de proponerse para probar. No toca sus experimentos ni sus
    /// aprendizajes. Escribe la hipótesis y la auditoría.</summary>
    /// <param name="id">Hipótesis.</param>
    /// <param name="reason">Por qué se descarta.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <exception cref="BusinessException">404 <c>HYPOTHESIS_NOT_FOUND</c>.</exception>
    public async Task DiscardAsync(long id, string reason, CancellationToken cancellationToken)
    {
        await FindAsync(id, cancellationToken);
        string text = reason.Trim();
        await db.InTransactionAsync(
            async () =>
            {
                await db.Hypotheses.Where(h => h.Id == id && h.DiscardedAt == null).ExecuteUpdateAsync(
                    s => s.SetProperty(h => h.DiscardedAt, h => (DateTime?)AppDbContext.UtcNow()).SetProperty(h => h.DiscardReason, text),
                    cancellationToken);
                audit.Record("hypothesis.discard", nameof(Hypothesis), id.ToString(), "Descartó la hipótesis.");
                await db.SaveChangesAsync(cancellationToken);
                return true;
            },
            cancellationToken);
    }

    /// <summary>Busca una hipótesis de la organización activa. No escribe.</summary>
    /// <param name="id">Hipótesis.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>La hipótesis.</returns>
    /// <exception cref="BusinessException">404 <c>HYPOTHESIS_NOT_FOUND</c>.</exception>
    public async Task<Hypothesis> FindAsync(long id, CancellationToken cancellationToken)
    {
        return await db.Hypotheses.AsNoTracking().FirstOrDefaultAsync(h => h.Id == id, cancellationToken)
            ?? throw new BusinessException(404, "La hipótesis no existe.", "HYPOTHESIS_NOT_FOUND");
    }

    /// <summary>Vuelve a calcular, con los datos de hoy, el patrón del que surgió una hipótesis. No escribe.</summary>
    /// <param name="hypothesis">Hipótesis surgida de un patrón.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>El grupo, o nulo si el componente ya no aparece en el análisis.</returns>
    public async Task<ComponentGroup?> PatternAsync(Hypothesis hypothesis, CancellationToken cancellationToken)
    {
        return hypothesis.PatternDimension is null || hypothesis.PatternKey is null
            ? null
            : await PatternAsync(
                hypothesis.PatternDimension, hypothesis.PatternKey, hypothesis.PatternCampaignId, hypothesis.PatternFrom, hypothesis.PatternTo,
                cancellationToken);
    }

    /// <summary>Nombre para mostrar del componente de un grupo: un fragmento lleva además su tramo.</summary>
    /// <param name="group">Grupo.</param>
    /// <returns>El nombre.</returns>
    public static string Label(ComponentGroup group)
    {
        return group.Fragment is null
            ? group.Label
            : $"{group.Fragment.FileName} {Formatting.Duration(group.Fragment.StartMs)}–{Formatting.Duration(group.Fragment.EndMs)}";
    }

    private async Task<ComponentGroup?> PatternAsync(
        string dimension, string key, long? campaignId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        PerformanceAnalysis analysis = await performance.AnalyzeAsync(
            new PerformanceFilter { CampaignId = campaignId, From = from, To = to }, cancellationToken);
        return analysis.Dimensions.FirstOrDefault(d => d.Name == dimension)?.Groups.FirstOrDefault(g => g.Key == key);
    }

    private async Task<long> SaveAsync(Hypothesis hypothesis, string summary, CancellationToken cancellationToken)
    {
        return await db.InTransactionAsync(
            async () =>
            {
                db.Hypotheses.Add(hypothesis);
                await db.SaveChangesAsync(cancellationToken);
                audit.Record("hypothesis.create", nameof(Hypothesis), hypothesis.Id.ToString(), summary);
                await db.SaveChangesAsync(cancellationToken);
                return hypothesis.Id;
            },
            cancellationToken);
    }

    // Cada hipótesis con los conteos de los que se deduce su estado, en una sola consulta.
    private IQueryable<HypothesisRow> Rows(IQueryable<Hypothesis> source)
    {
        return source.Select(h => new HypothesisRow
        {
            Id = h.Id,
            Statement = h.Statement,
            Origin = h.Origin,
            Variable = h.Variable,
            Discarded = h.DiscardedAt != null,
            Experiments = db.Experiments.Count(e => e.HypothesisId == h.Id),
            OpenExperiments = db.Experiments.Count(e => e.HypothesisId == h.Id && e.ConcludedAt == null),
            ActiveLearnings = db.Learnings.Count(l => l.HypothesisId == h.Id && l.Status == LearningStatus.Active),
            CreatedAt = h.CreatedAt,
        });
    }

    private static string Level(AssessmentLevel? level)
    {
        return level is null ? "sin valorar" : Formatting.Level(level.Value).ToLowerInvariant();
    }

    private static HypothesisState StateOf(HypothesisRow row)
    {
        return LearningRules.State(row.Discarded, row.Experiments, row.OpenExperiments, row.ActiveLearnings);
    }
}

/// <summary>
/// Memoria de aprendizaje de la organización activa. Un aprendizaje siempre tiene datos detrás (un experimento o un
/// patrón): el sistema describe los datos, el resultado y la confianza, y una persona escribe qué se aprendió, qué
/// conviene hacer y para qué casos vale. No se edita ni se borra: se corrige con una versión nueva o se invalida, y
/// sólo los vigentes cuentan para lo que se planifique después.
/// </summary>
/// <param name="db">Contexto de datos.</param>
/// <param name="hypotheses">Hipótesis y sus patrones.</param>
/// <param name="experiments">Experimentos y sus resultados.</param>
/// <param name="audit">Auditoría.</param>
/// <param name="current">Usuario y organización que operan.</param>
public sealed class LearningService(
    AppDbContext db, HypothesisService hypotheses, ExperimentService experiments, AuditService audit, CurrentContext current)
{
    /// <summary>Lista los aprendizajes, del más nuevo al más viejo. No escribe.</summary>
    /// <param name="status">Sólo los de este estado; nulo para todos.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Hasta 200 aprendizajes.</returns>
    public async Task<List<LearningListItem>> ListAsync(LearningStatus? status, CancellationToken cancellationToken)
    {
        return await db.Learnings.AsNoTracking()
            .Where(l => status == null || l.Status == status)
            .OrderByDescending(l => l.Id)
            .Take(200)
            .Select(l => new LearningListItem(
                l.Id, l.Statement, l.Recommendation, l.Confidence, l.Status, l.ExperimentId != null, l.PeriodStart, l.PeriodEnd, l.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Devuelve los aprendizajes vigentes para pasárselos al planificador de conceptos, del más nuevo al
    /// más viejo. Los corregidos y los invalidados no entran. No escribe.</summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Hasta 30 aprendizajes.</returns>
    public async Task<List<PlanLearning>> ForPlanAsync(CancellationToken cancellationToken)
    {
        return await db.Learnings.AsNoTracking()
            .Where(l => l.Status == LearningStatus.Active)
            .OrderByDescending(l => l.Id)
            .Take(30)
            .Select(l => new PlanLearning(l.Statement, l.Recommendation, l.Applicability, l.Confidence))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Calcula lo que el sistema sabe de una fuente antes de registrar un aprendizaje. No escribe.</summary>
    /// <param name="experimentId">Experimento que lo sustenta.</param>
    /// <param name="hypothesisId">Hipótesis surgida de un patrón, si no hay experimento.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Datos, resultado, confianza y período.</returns>
    /// <exception cref="BusinessException">400 <c>LEARNING_SOURCE</c> si no se indica una fuente; 404 si no existe;
    /// 409 <c>LEARNING_NO_EVIDENCE</c> si los datos no alcanzan para afirmar nada.</exception>
    public async Task<LearningDraft> DraftAsync(long? experimentId, long? hypothesisId, CancellationToken cancellationToken)
    {
        LearningSource source = await SourceAsync(experimentId, hypothesisId, cancellationToken);
        LearningBasis basis = source.Basis ?? throw NoEvidence();
        return new LearningDraft(
            source.ExperimentId, source.HypothesisId, source.Name, source.SuggestedStatement, source.DataUsed, source.Result, basis.Confidence,
            basis.Reason, source.PeriodStart, source.PeriodEnd);
    }

    /// <summary>Registra un aprendizaje. Los datos, el resultado y la confianza se calculan acá, no los manda el
    /// navegador. Escribe el aprendizaje, su evidencia y la auditoría.</summary>
    /// <param name="body">Fuente y lo que escribe la persona.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Identificador del aprendizaje.</returns>
    /// <exception cref="BusinessException">Los de <see cref="DraftAsync"/>.</exception>
    public async Task<long> CreateAsync(CreateLearningBody body, CancellationToken cancellationToken)
    {
        LearningSource source = await SourceAsync(body.ExperimentId, body.HypothesisId, cancellationToken);
        Learning learning = await BuildAsync(source, body, null, cancellationToken);
        return await db.InTransactionAsync(
            async () =>
            {
                db.Learnings.Add(learning);
                await db.SaveChangesAsync(cancellationToken);
                audit.Record("learning.create", nameof(Learning), learning.Id.ToString(), $"Registró un aprendizaje con confianza {learning.Confidence}.");
                await db.SaveChangesAsync(cancellationToken);
                return learning.Id;
            },
            cancellationToken);
    }

    /// <summary>Devuelve un aprendizaje con su evidencia y, si está vigente, lo que dicen hoy sus datos. No escribe.</summary>
    /// <param name="id">Aprendizaje.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>El aprendizaje.</returns>
    /// <exception cref="BusinessException">404 <c>LEARNING_NOT_FOUND</c> si no existe en la organización activa.</exception>
    public async Task<LearningDetail> GetAsync(long id, CancellationToken cancellationToken)
    {
        Learning learning = await FindAsync(id, cancellationToken);
        List<LearningEvidenceView> evidence = await db.LearningEvidence.AsNoTracking()
            .Where(e => e.LearningId == id)
            .OrderBy(e => e.Id)
            .Select(e => new LearningEvidenceView(
                e.MetaAdId,
                db.MetaAds.Where(a => a.Id == e.MetaAdId).Select(a => a.Name).FirstOrDefault(),
                e.CreativeVariantId,
                e.MediaAssetId,
                db.MediaAssets.Where(m => m.Id == e.MediaAssetId).Select(m => m.OriginalFileName).FirstOrDefault(),
                e.StartMs,
                e.EndMs))
            .ToListAsync(cancellationToken);
        string? hypothesis = await db.Hypotheses.AsNoTracking()
            .Where(h => h.Id == learning.HypothesisId)
            .Select(h => h.Statement)
            .FirstOrDefaultAsync(cancellationToken);
        string? experiment = await db.Experiments.AsNoTracking()
            .Where(e => e.Id == learning.ExperimentId)
            .Select(e => e.Name)
            .FirstOrDefaultAsync(cancellationToken);
        long? replacedBy = await db.Learnings.AsNoTracking()
            .Where(l => l.ReplacesLearningId == id)
            .Select(l => (long?)l.Id)
            .FirstOrDefaultAsync(cancellationToken);

        LearningReview? review = null;
        if (learning.Status == LearningStatus.Active)
        {
            LearningSource source = await SourceOfAsync(learning, cancellationToken);
            review = new LearningReview(
                source.Basis?.Confidence,
                source.Basis is null ? null : source.Result,
                source.Basis is null || source.Basis.Confidence != learning.Confidence || source.Result != learning.Result);
        }

        return new LearningDetail(
            learning.Id, learning.Statement, learning.DataUsed, learning.Result, learning.Confidence, learning.ConfidenceReason, learning.PeriodStart,
            learning.PeriodEnd, learning.Recommendation, learning.Applicability, learning.CriteriaVersion, learning.Status, learning.StatusReason,
            learning.StatusChangedAt, learning.CreatedAt, learning.HypothesisId, hypothesis, learning.ExperimentId, experiment,
            learning.ReplacesLearningId, replacedBy, evidence, review);
    }

    /// <summary>
    /// Corrige un aprendizaje vigente: crea una versión nueva, calculada con los datos de hoy, y deja la anterior
    /// como corregida. Escribe los dos aprendizajes, la evidencia y la auditoría.
    /// </summary>
    /// <param name="id">Aprendizaje a corregir.</param>
    /// <param name="body">Texto nuevo y motivo.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Identificador de la versión nueva.</returns>
    /// <exception cref="BusinessException">404 <c>LEARNING_NOT_FOUND</c>; 409 <c>LEARNING_NOT_ACTIVE</c> si ya no
    /// está vigente; 409 <c>LEARNING_NO_EVIDENCE</c> si hoy los datos no alcanzan (corresponde invalidarlo).</exception>
    public async Task<long> CorrectAsync(long id, CorrectLearningBody body, CancellationToken cancellationToken)
    {
        Learning previous = await FindAsync(id, cancellationToken);
        if (previous.Status != LearningStatus.Active)
        {
            throw new BusinessException(409, "El aprendizaje ya no está vigente.", "LEARNING_NOT_ACTIVE");
        }

        LearningSource source = await SourceOfAsync(previous, cancellationToken);
        Learning learning = await BuildAsync(source, body, id, cancellationToken);
        return await db.InTransactionAsync(
            async () =>
            {
                await CloseAsync(id, LearningStatus.Corrected, body.Reason, cancellationToken);
                db.Learnings.Add(learning);
                await db.SaveChangesAsync(cancellationToken);
                audit.Record("learning.correct", nameof(Learning), learning.Id.ToString(), $"Corrigió el aprendizaje {id} con una versión nueva.");
                await db.SaveChangesAsync(cancellationToken);
                return learning.Id;
            },
            cancellationToken);
    }

    /// <summary>Invalida un aprendizaje vigente: deja de contar. Escribe el aprendizaje y la auditoría.</summary>
    /// <param name="id">Aprendizaje.</param>
    /// <param name="reason">Por qué deja de valer.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <exception cref="BusinessException">404 <c>LEARNING_NOT_FOUND</c>; 409 <c>LEARNING_NOT_ACTIVE</c> si ya no
    /// está vigente.</exception>
    public async Task InvalidateAsync(long id, string reason, CancellationToken cancellationToken)
    {
        await FindAsync(id, cancellationToken);
        await db.InTransactionAsync(
            async () =>
            {
                await CloseAsync(id, LearningStatus.Invalidated, reason, cancellationToken);
                audit.Record("learning.invalidate", nameof(Learning), id.ToString(), "Invalidó el aprendizaje.");
                await db.SaveChangesAsync(cancellationToken);
                return true;
            },
            cancellationToken);
    }

    // Saca un aprendizaje de vigencia. La condición va en el UPDATE: de dos decisiones simultáneas, gana una.
    private async Task CloseAsync(long id, LearningStatus status, string reason, CancellationToken cancellationToken)
    {
        string text = reason.Trim();
        int rows = await db.Learnings.Where(l => l.Id == id && l.Status == LearningStatus.Active).ExecuteUpdateAsync(
            s => s.SetProperty(l => l.Status, status)
                .SetProperty(l => l.StatusReason, text)
                .SetProperty(l => l.StatusChangedAt, l => (DateTime?)AppDbContext.UtcNow()),
            cancellationToken);
        if (rows == 0)
        {
            throw new BusinessException(409, "El aprendizaje ya no está vigente.", "LEARNING_NOT_ACTIVE");
        }
    }

    private async Task<Learning> BuildAsync(LearningSource source, LearningTextBody text, long? replaces, CancellationToken cancellationToken)
    {
        LearningBasis basis = source.Basis ?? throw NoEvidence();
        Dictionary<long, long?> variants = await db.MetaAds.AsNoTracking()
            .Where(a => source.AdIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.CreativeVariantId, cancellationToken);
        List<LearningEvidence> evidence = source.AdIds
            .Select(adId => new LearningEvidence { MetaAdId = adId, CreativeVariantId = variants.GetValueOrDefault(adId) })
            .ToList();
        if (source.Fragment is not null)
        {
            evidence.Add(new LearningEvidence { MediaAssetId = source.Fragment.AssetId, StartMs = source.Fragment.StartMs, EndMs = source.Fragment.EndMs });
        }

        return new Learning
        {
            HypothesisId = source.HypothesisId,
            ExperimentId = source.ExperimentId,
            Statement = text.Statement.Trim(),
            DataUsed = source.DataUsed,
            Result = source.Result,
            Confidence = basis.Confidence,
            ConfidenceReason = basis.Reason,
            PeriodStart = source.PeriodStart,
            PeriodEnd = source.PeriodEnd,
            Recommendation = text.Recommendation.Trim(),
            Applicability = text.Applicability.Trim(),
            CriteriaVersion = LearningRules.CriteriaVersion,
            Status = LearningStatus.Active,
            ReplacesLearningId = replaces,
            CreatedByUserId = current.UserId,
            Evidence = evidence,
        };
    }

    // La fuente de un aprendizaje ya registrado: su experimento si lo tiene; si no, el patrón de su hipótesis.
    private async Task<LearningSource> SourceOfAsync(Learning learning, CancellationToken cancellationToken)
    {
        return await SourceAsync(learning.ExperimentId, learning.ExperimentId is null ? learning.HypothesisId : null, cancellationToken);
    }

    private async Task<LearningSource> SourceAsync(long? experimentId, long? hypothesisId, CancellationToken cancellationToken)
    {
        if (experimentId is not null)
        {
            return ExperimentSource(await experiments.GetAsync(experimentId.Value, cancellationToken));
        }

        if (hypothesisId is null)
        {
            throw new BusinessException(400, "Un aprendizaje sale de un experimento o de un patrón: indicá cuál.", "LEARNING_SOURCE");
        }

        Hypothesis hypothesis = await hypotheses.FindAsync(hypothesisId.Value, cancellationToken);
        ComponentGroup? group = await hypotheses.PatternAsync(hypothesis, cancellationToken);
        if (group is null)
        {
            // Una hipótesis escrita a mano, o un componente que ya no aparece: no hay datos que la sostengan.
            return new LearningSource { HypothesisId = hypothesis.Id, Name = hypothesis.PatternLabel ?? hypothesis.Statement, SuggestedStatement = hypothesis.Statement };
        }

        string campaign = hypothesis.PatternCampaignId is null
            ? "todas las campañas"
            : "la campaña «" + await db.MetaCampaigns.AsNoTracking()
                .Where(c => c.Id == hypothesis.PatternCampaignId)
                .Select(c => c.Name)
                .FirstAsync(cancellationToken) + "»";
        return new LearningSource
        {
            HypothesisId = hypothesis.Id,
            Name = HypothesisService.Label(group),
            SuggestedStatement = hypothesis.Statement,
            DataUsed = $"Patrón del análisis por componente sobre {campaign}: {group.Ads} anuncios de {group.Campaigns} campaña(s) con "
                + $"«{HypothesisService.Label(group)}» ({group.Metrics.Impressions} impresiones), comparados con el resto ({group.Rest.Impressions} impresiones).",
            Result = group.Reading,
            Basis = LearningRules.FromPattern(group),
            PeriodStart = hypothesis.PatternFrom,
            PeriodEnd = hypothesis.PatternTo,
            AdIds = group.AdIds,
            Fragment = group.Fragment,
        };
    }

    private static LearningSource ExperimentSource(ExperimentDetail experiment)
    {
        return new LearningSource
        {
            ExperimentId = experiment.Id,
            HypothesisId = experiment.HypothesisId,
            Name = experiment.Name,
            SuggestedStatement = experiment.Hypothesis,
            DataUsed = $"Experimento «{experiment.Name}»: {experiment.Result.Arms.Count} brazos ({string.Join(", ", experiment.Result.Arms.Select(a => a.Name))}), "
                + $"{experiment.Result.Arms.Sum(a => a.Metrics.Impressions)} impresiones en total, con un mínimo de {experiment.MinImpressionsPerArm} por brazo.",
            Result = experiment.Result.Summary,
            Basis = LearningRules.FromExperiment(experiment.Result),
            PeriodStart = experiment.StartDate,
            PeriodEnd = experiment.EndDate,
            AdIds = experiment.Result.Arms.Select(a => a.AdId).ToList(),
        };
    }

    private async Task<Learning> FindAsync(long id, CancellationToken cancellationToken)
    {
        return await db.Learnings.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, cancellationToken)
            ?? throw new BusinessException(404, "El aprendizaje no existe.", "LEARNING_NOT_FOUND");
    }

    private static BusinessException NoEvidence()
    {
        return new BusinessException(
            409, "Los datos no alcanzan para afirmar nada: falta volumen, o la hipótesis no tiene un experimento ni un patrón detrás.", "LEARNING_NO_EVIDENCE");
    }
}

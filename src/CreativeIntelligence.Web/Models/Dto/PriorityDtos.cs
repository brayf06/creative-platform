using System.ComponentModel.DataAnnotations;

namespace CreativeIntelligence.Web.Models.Dto;

/// <summary>Los diez factores que pondera el motor de priorización.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<PriorityFactor>))]
public enum PriorityFactor
{
    /// <summary>Cómo rindieron los anuncios que ya usan el componente.</summary>
    HistoricalPerformance,

    /// <summary>Qué tan firme es lo que se sabe.</summary>
    EvidenceQuality,

    /// <summary>Cuánto se parece a lo que ya se produjo (más parecido, menos prioridad).</summary>
    Similarity,

    /// <summary>Qué tan poco se probó.</summary>
    Novelty,

    /// <summary>Relevancia para el público, valorada por una persona.</summary>
    AudienceRelevance,

    /// <summary>Si el material necesario está y tiene derechos.</summary>
    MaterialAvailability,

    /// <summary>Costo de producción, valorado por una persona (más costo, menos prioridad).</summary>
    ProductionCost,

    /// <summary>Riesgo publicitario, valorado por una persona (más riesgo, menos prioridad).</summary>
    AdRisk,

    /// <summary>Fatiga de los anuncios que ya lo usan (más fatiga, menos prioridad).</summary>
    Fatigue,

    /// <summary>Margen potencial, valorado por una persona.</summary>
    PotentialMargin,
}

/// <summary>Ponderaciones nuevas del motor de priorización. Guardarlas crea una versión.</summary>
public sealed class PriorityWeightsBody
{
    /// <summary>Peso del rendimiento histórico.</summary>
    [Range(0, 10, ErrorMessage = "Cada peso va de 0 a 10.")]
    public int HistoricalPerformance { get; set; }

    /// <summary>Peso de la calidad de la evidencia.</summary>
    [Range(0, 10, ErrorMessage = "Cada peso va de 0 a 10.")]
    public int EvidenceQuality { get; set; }

    /// <summary>Peso de la similitud con creatividades existentes.</summary>
    [Range(0, 10, ErrorMessage = "Cada peso va de 0 a 10.")]
    public int Similarity { get; set; }

    /// <summary>Peso de la novedad.</summary>
    [Range(0, 10, ErrorMessage = "Cada peso va de 0 a 10.")]
    public int Novelty { get; set; }

    /// <summary>Peso de la relevancia para el público.</summary>
    [Range(0, 10, ErrorMessage = "Cada peso va de 0 a 10.")]
    public int AudienceRelevance { get; set; }

    /// <summary>Peso de la disponibilidad de material.</summary>
    [Range(0, 10, ErrorMessage = "Cada peso va de 0 a 10.")]
    public int MaterialAvailability { get; set; }

    /// <summary>Peso del costo de producción.</summary>
    [Range(0, 10, ErrorMessage = "Cada peso va de 0 a 10.")]
    public int ProductionCost { get; set; }

    /// <summary>Peso del riesgo publicitario.</summary>
    [Range(0, 10, ErrorMessage = "Cada peso va de 0 a 10.")]
    public int AdRisk { get; set; }

    /// <summary>Peso de la fatiga.</summary>
    [Range(0, 10, ErrorMessage = "Cada peso va de 0 a 10.")]
    public int Fatigue { get; set; }

    /// <summary>Peso del margen potencial.</summary>
    [Range(0, 10, ErrorMessage = "Cada peso va de 0 a 10.")]
    public int PotentialMargin { get; set; }

    /// <summary>Por qué se cambian.</summary>
    [Required(ErrorMessage = "Escribí por qué se cambian las ponderaciones.")]
    [StringLength(500, ErrorMessage = "El motivo admite hasta 500 caracteres.")]
    public string Note { get; set; } = string.Empty;
}

/// <summary>Lo que una persona valora de una hipótesis porque el sistema no tiene de dónde medirlo. Un campo vacío
/// es "sin valorar".</summary>
public sealed class AssessHypothesisBody
{
    /// <summary>Relevancia para el público.</summary>
    public AssessmentLevel? AudienceRelevance { get; set; }

    /// <summary>Costo de producción.</summary>
    public AssessmentLevel? ProductionCost { get; set; }

    /// <summary>Riesgo publicitario.</summary>
    public AssessmentLevel? AdRisk { get; set; }

    /// <summary>Margen potencial.</summary>
    public AssessmentLevel? PotentialMargin { get; set; }
}

/// <summary>Las valoraciones de una persona sobre una hipótesis.</summary>
/// <param name="AudienceRelevance">Relevancia para el público.</param>
/// <param name="ProductionCost">Costo de producción.</param>
/// <param name="AdRisk">Riesgo publicitario.</param>
/// <param name="PotentialMargin">Margen potencial.</param>
public sealed record HypothesisAssessment(
    AssessmentLevel? AudienceRelevance, AssessmentLevel? ProductionCost, AssessmentLevel? AdRisk, AssessmentLevel? PotentialMargin);

/// <summary>Una versión de las ponderaciones: el peso de cada factor, de 0 a 10.</summary>
/// <param name="Version">Número de versión; 0 son las de fábrica.</param>
/// <param name="HistoricalPerformance">Rendimiento histórico.</param>
/// <param name="EvidenceQuality">Calidad de la evidencia.</param>
/// <param name="Similarity">Similitud con lo existente.</param>
/// <param name="Novelty">Novedad.</param>
/// <param name="AudienceRelevance">Relevancia para el público.</param>
/// <param name="MaterialAvailability">Disponibilidad de material.</param>
/// <param name="ProductionCost">Costo de producción.</param>
/// <param name="AdRisk">Riesgo publicitario.</param>
/// <param name="Fatigue">Fatiga.</param>
/// <param name="PotentialMargin">Margen potencial.</param>
public sealed record PriorityWeights(
    int Version, int HistoricalPerformance, int EvidenceQuality, int Similarity, int Novelty, int AudienceRelevance, int MaterialAvailability,
    int ProductionCost, int AdRisk, int Fatigue, int PotentialMargin)
{
    /// <summary>El peso de un factor.</summary>
    /// <param name="factor">Factor.</param>
    /// <returns>El peso, de 0 a 10.</returns>
    public int Of(PriorityFactor factor)
    {
        return factor switch
        {
            PriorityFactor.HistoricalPerformance => HistoricalPerformance,
            PriorityFactor.EvidenceQuality => EvidenceQuality,
            PriorityFactor.Similarity => Similarity,
            PriorityFactor.Novelty => Novelty,
            PriorityFactor.AudienceRelevance => AudienceRelevance,
            PriorityFactor.MaterialAvailability => MaterialAvailability,
            PriorityFactor.ProductionCost => ProductionCost,
            PriorityFactor.AdRisk => AdRisk,
            PriorityFactor.Fatigue => Fatigue,
            _ => PotentialMargin,
        };
    }
}

/// <summary>Una versión guardada de las ponderaciones.</summary>
/// <param name="Weights">Los pesos.</param>
/// <param name="Note">Por qué se cambiaron.</param>
/// <param name="CreatedAt">Cuándo, en UTC.</param>
public sealed record WeightSetItem(PriorityWeights Weights, string Note, DateTime CreatedAt);

/// <summary>Las ponderaciones vigentes y su historia.</summary>
/// <param name="Current">Las vigentes.</param>
/// <param name="History">Las versiones guardadas, de la más nueva a la más vieja.</param>
public sealed record PriorityWeightsView(PriorityWeights Current, List<WeightSetItem> History);

/// <summary>El valor de un factor para una oportunidad.</summary>
/// <param name="Factor">Factor.</param>
/// <param name="Value">De 0 (resta prioridad) a 1 (suma prioridad); nulo si no hay dato.</param>
/// <param name="Explanation">De qué dato sale.</param>
public sealed record FactorValue(PriorityFactor Factor, decimal? Value, string Explanation);

/// <summary>Una hipótesis puntuada.</summary>
/// <param name="HypothesisId">Hipótesis.</param>
/// <param name="Statement">Enunciado.</param>
/// <param name="Origin">De dónde salió.</param>
/// <param name="State">En qué punto está.</param>
/// <param name="Score">Puntaje de prioridad, de 0 a 100. Es un orden para decidir, no una predicción de resultados.</param>
/// <param name="FactorsWithData">Cuántos de los diez factores tienen dato; los que no, no suman.</param>
/// <param name="Factors">El desglose.</param>
/// <param name="Variable">Sobre qué parte de la creatividad trata.</param>
/// <param name="Component">Componente concreto, si salió de un patrón.</param>
/// <param name="Fragment">El tramo, si el componente es un fragmento y sigue apareciendo en el análisis.</param>
/// <param name="HasPattern">Si hoy hay un patrón con evidencia detrás.</param>
public sealed record Opportunity(
    long HypothesisId, string Statement, HypothesisOrigin Origin, HypothesisState State, decimal Score, int FactorsWithData, List<FactorValue> Factors,
    ExperimentVariable Variable, string? Component, ComponentRef? Fragment, bool HasPattern);

/// <summary>Las hipótesis en pie ordenadas por prioridad.</summary>
/// <param name="Weights">Ponderaciones con que se calculó.</param>
/// <param name="Items">Del puntaje más alto al más bajo.</param>
public sealed record PriorityBoard(PriorityWeights Weights, List<Opportunity> Items);

/// <summary>Lo que se sabe de una hipótesis para puntuarla. Lo arma el servicio; las reglas no tocan la base.</summary>
public sealed class OpportunityInput
{

    /// <summary>El patrón con los datos de hoy; nulo si es manual o el componente ya no aparece.</summary>
    public ComponentGroup? Pattern { get; init; }

    /// <summary>La mayor confianza entre sus aprendizajes vigentes; nulo si no tiene.</summary>
    public LearningConfidence? BestConfidence { get; init; }

    /// <summary>Experimentos que la prueban.</summary>
    public int Experiments { get; init; }

    /// <summary>Recetas que ya usan el componente; nulo si no se puede medir.</summary>
    public int? RecipesUsing { get; init; }

    /// <summary>Recetas de la organización contra las que se midió.</summary>
    public int Recipes { get; init; }

    /// <summary>Si el material que necesita está y tiene derechos vigentes; nulo si no se puede saber.</summary>
    public bool? MaterialAvailable { get; init; }

    /// <summary>De los anuncios del patrón, cuántos muestran posible fatiga.</summary>
    public int FatiguedAds { get; init; }

    /// <summary>Valoraciones de una persona.</summary>
    public HypothesisAssessment Assessment { get; init; } = new(null, null, null, null);
}

/// <summary>Proyección intermedia: un experimento de una hipótesis.</summary>
/// <param name="HypothesisId">Hipótesis.</param>
/// <param name="Concluded">Si está cerrado.</param>
public sealed record HypothesisExperimentRow(long HypothesisId, bool Concluded);

/// <summary>Proyección intermedia: un aprendizaje vigente de una hipótesis.</summary>
/// <param name="HypothesisId">Hipótesis.</param>
/// <param name="Confidence">Confianza.</param>
public sealed record HypothesisLearningRow(long HypothesisId, LearningConfidence Confidence);

/// <summary>Proyección intermedia: si un original tiene autorización de reutilización vigente.</summary>
/// <param name="AssetId">Original.</param>
/// <param name="Authorized">Si la tiene.</param>
public sealed record AssetRightsRow(long AssetId, bool Authorized);

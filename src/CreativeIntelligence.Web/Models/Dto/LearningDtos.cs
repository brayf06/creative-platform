using System.ComponentModel.DataAnnotations;

namespace CreativeIntelligence.Web.Models.Dto;

/// <summary>En qué punto está una hipótesis. No se guarda: se deduce de sus experimentos y aprendizajes.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<HypothesisState>))]
public enum HypothesisState
{
    /// <summary>Todavía no se probó.</summary>
    Open,

    /// <summary>Tiene un experimento sin cerrar.</summary>
    Testing,

    /// <summary>Sus experimentos están cerrados y no dejó ningún aprendizaje vigente.</summary>
    Tested,

    /// <summary>Tiene al menos un aprendizaje vigente.</summary>
    Learned,

    /// <summary>Una persona la descartó.</summary>
    Discarded,
}

/// <summary>Datos para escribir una hipótesis a mano.</summary>
public sealed class CreateHypothesisBody
{
    /// <summary>Enunciado.</summary>
    [Required(ErrorMessage = "Escribí la hipótesis.")]
    [StringLength(1000, ErrorMessage = "La hipótesis admite hasta 1000 caracteres.")]
    public string Statement { get; set; } = string.Empty;

    /// <summary>Sobre qué parte de la creatividad trata.</summary>
    public ExperimentVariable Variable { get; set; }
}

/// <summary>Un patrón del análisis por componente: qué grupo, de qué análisis.</summary>
public sealed class PatternBody
{
    /// <summary>Dimensión del análisis (<c>opening</c>, <c>music</c>…).</summary>
    [Required(ErrorMessage = "Falta la dimensión del patrón.")]
    [StringLength(20)]
    public string Dimension { get; set; } = string.Empty;

    /// <summary>Clave del componente dentro de la dimensión.</summary>
    [Required(ErrorMessage = "Falta el componente del patrón.")]
    [StringLength(300)]
    public string Key { get; set; } = string.Empty;

    /// <summary>Campaña a la que se limitó el análisis; nulo si eran todas.</summary>
    public long? CampaignId { get; set; }

    /// <summary>Primer día del análisis.</summary>
    public DateOnly? From { get; set; }

    /// <summary>Último día del análisis.</summary>
    public DateOnly? To { get; set; }
}

/// <summary>Motivo de una decisión sobre una hipótesis o un aprendizaje.</summary>
public sealed class ReasonBody
{
    /// <summary>Por qué.</summary>
    [Required(ErrorMessage = "Escribí el motivo.")]
    [StringLength(500, ErrorMessage = "El motivo admite hasta 500 caracteres.")]
    public string Reason { get; set; } = string.Empty;
}

/// <summary>Lo que escribe una persona al registrar un aprendizaje. El resto lo calcula el sistema.</summary>
public class LearningTextBody
{
    /// <summary>Qué se aprendió.</summary>
    [Required(ErrorMessage = "Escribí qué se aprendió.")]
    [StringLength(1000, ErrorMessage = "El enunciado admite hasta 1000 caracteres.")]
    public string Statement { get; set; } = string.Empty;

    /// <summary>Qué conviene hacer.</summary>
    [Required(ErrorMessage = "Escribí la recomendación.")]
    [StringLength(1000, ErrorMessage = "La recomendación admite hasta 1000 caracteres.")]
    public string Recommendation { get; set; } = string.Empty;

    /// <summary>Para qué casos vale y para cuáles no.</summary>
    [Required(ErrorMessage = "Escribí para qué casos vale.")]
    [StringLength(1000, ErrorMessage = "La aplicabilidad admite hasta 1000 caracteres.")]
    public string Applicability { get; set; } = string.Empty;
}

/// <summary>Datos para registrar un aprendizaje: de dónde salen los datos y lo que escribe la persona.</summary>
public sealed class CreateLearningBody : LearningTextBody
{
    /// <summary>Experimento que lo sustenta.</summary>
    public long? ExperimentId { get; set; }

    /// <summary>Hipótesis surgida de un patrón que lo sustenta, si no hay experimento.</summary>
    public long? HypothesisId { get; set; }
}

/// <summary>Datos para corregir un aprendizaje: el texto nuevo y por qué se corrige.</summary>
public sealed class CorrectLearningBody : LearningTextBody
{
    /// <summary>Por qué se corrige.</summary>
    [Required(ErrorMessage = "Escribí por qué se corrige.")]
    [StringLength(500, ErrorMessage = "El motivo admite hasta 500 caracteres.")]
    public string Reason { get; set; } = string.Empty;
}

/// <summary>La confianza que merecen unos datos y por qué.</summary>
/// <param name="Confidence">Confianza.</param>
/// <param name="Reason">Por qué.</param>
public sealed record LearningBasis(LearningConfidence Confidence, string Reason);

/// <summary>Una hipótesis en el listado.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Statement">Enunciado.</param>
/// <param name="Origin">De dónde salió.</param>
/// <param name="Variable">Sobre qué trata.</param>
/// <param name="State">En qué punto está.</param>
/// <param name="Experiments">Experimentos que la prueban.</param>
/// <param name="ActiveLearnings">Aprendizajes vigentes que dejó.</param>
/// <param name="CreatedAt">Alta, en UTC.</param>
public sealed record HypothesisListItem(
    long Id, string Statement, HypothesisOrigin Origin, ExperimentVariable Variable, HypothesisState State, int Experiments, int ActiveLearnings,
    DateTime CreatedAt);

/// <summary>El patrón del que surgió una hipótesis.</summary>
/// <param name="Label">Componente.</param>
/// <param name="CampaignName">Campaña a la que se limitó el análisis; nulo si eran todas.</param>
/// <param name="From">Primer día.</param>
/// <param name="To">Último día.</param>
/// <param name="ReadingThen">La lectura cuando se guardó la hipótesis.</param>
/// <param name="ReadingNow">La lectura con los datos de hoy; nulo si el componente ya no aparece en el análisis.</param>
/// <param name="EvidenceNow">Nivel de evidencia con los datos de hoy.</param>
public sealed record HypothesisPattern(
    string Label, string? CampaignName, DateOnly? From, DateOnly? To, string ReadingThen, string? ReadingNow, EvidenceLevel? EvidenceNow);

/// <summary>Un aprendizaje en un listado.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Statement">Qué se aprendió.</param>
/// <param name="Recommendation">Qué conviene hacer.</param>
/// <param name="Confidence">Confianza.</param>
/// <param name="Status">Estado.</param>
/// <param name="FromExperiment">Si lo sustenta un experimento; si no, un patrón.</param>
/// <param name="PeriodStart">Primer día de los datos.</param>
/// <param name="PeriodEnd">Último día de los datos.</param>
/// <param name="CreatedAt">Alta, en UTC.</param>
public sealed record LearningListItem(
    long Id, string Statement, string Recommendation, LearningConfidence Confidence, LearningStatus Status, bool FromExperiment,
    DateOnly? PeriodStart, DateOnly? PeriodEnd, DateTime CreatedAt);

/// <summary>Una hipótesis con lo que se hizo con ella.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Statement">Enunciado.</param>
/// <param name="Origin">De dónde salió.</param>
/// <param name="Variable">Sobre qué trata.</param>
/// <param name="State">En qué punto está.</param>
/// <param name="DiscardReason">Por qué se descartó.</param>
/// <param name="CreatedAt">Alta, en UTC.</param>
/// <param name="Pattern">El patrón del que surgió; nulo si es manual.</param>
/// <param name="CanLearnFromPattern">Si el patrón, con los datos de hoy, alcanza para registrar un aprendizaje.</param>
/// <param name="Experiments">Experimentos que la prueban.</param>
/// <param name="Learnings">Aprendizajes que dejó, vigentes o no.</param>
/// <param name="Assessment">Lo que una persona valoró para priorizarla.</param>
public sealed record HypothesisDetail(
    long Id, string Statement, HypothesisOrigin Origin, ExperimentVariable Variable, HypothesisState State, string? DiscardReason, DateTime CreatedAt,
    HypothesisPattern? Pattern, bool CanLearnFromPattern, List<ExperimentListItem> Experiments, List<LearningListItem> Learnings,
    HypothesisAssessment Assessment);

/// <summary>Lo que el sistema calcula antes de registrar un aprendizaje, para que la persona lo vea y escriba el resto.</summary>
/// <param name="ExperimentId">Experimento que lo sustenta.</param>
/// <param name="HypothesisId">Hipótesis de la que sale.</param>
/// <param name="SourceName">Nombre del experimento o del componente del patrón.</param>
/// <param name="SuggestedStatement">Enunciado propuesto: el de la hipótesis.</param>
/// <param name="DataUsed">Qué datos se usan.</param>
/// <param name="Result">El resultado en palabras.</param>
/// <param name="Confidence">Confianza calculada.</param>
/// <param name="ConfidenceReason">Por qué.</param>
/// <param name="PeriodStart">Primer día de los datos.</param>
/// <param name="PeriodEnd">Último día de los datos.</param>
public sealed record LearningDraft(
    long? ExperimentId, long? HypothesisId, string SourceName, string SuggestedStatement, string DataUsed, string Result,
    LearningConfidence Confidence, string ConfidenceReason, DateOnly? PeriodStart, DateOnly? PeriodEnd);

/// <summary>Una pieza de la evidencia de un aprendizaje.</summary>
/// <param name="AdId">Anuncio.</param>
/// <param name="AdName">Nombre del anuncio.</param>
/// <param name="VariantId">Video generado que publicó.</param>
/// <param name="AssetId">Original del tramo.</param>
/// <param name="FileName">Nombre del original.</param>
/// <param name="StartMs">Inicio del tramo.</param>
/// <param name="EndMs">Fin del tramo.</param>
public sealed record LearningEvidenceView(long? AdId, string? AdName, long? VariantId, long? AssetId, string? FileName, int? StartMs, int? EndMs);

/// <summary>Lo que dicen hoy los datos que sustentaban un aprendizaje.</summary>
/// <param name="Confidence">Confianza que tendría hoy; nulo si los datos ya no alcanzan.</param>
/// <param name="Result">El resultado calculado hoy; nulo si los datos ya no alcanzan.</param>
/// <param name="Changed">Si difiere de lo que se registró.</param>
public sealed record LearningReview(LearningConfidence? Confidence, string? Result, bool Changed);

/// <summary>Un aprendizaje completo.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Statement">Qué se aprendió.</param>
/// <param name="DataUsed">Qué datos se usaron.</param>
/// <param name="Result">El resultado en palabras, como estaba al registrarlo.</param>
/// <param name="Confidence">Confianza.</param>
/// <param name="ConfidenceReason">Por qué.</param>
/// <param name="PeriodStart">Primer día de los datos.</param>
/// <param name="PeriodEnd">Último día de los datos.</param>
/// <param name="Recommendation">Qué conviene hacer.</param>
/// <param name="Applicability">Para qué casos vale.</param>
/// <param name="CriteriaVersion">Versión del criterio de confianza.</param>
/// <param name="Status">Estado.</param>
/// <param name="StatusReason">Por qué se corrigió o se invalidó.</param>
/// <param name="StatusChangedAt">Cuándo.</param>
/// <param name="CreatedAt">Alta, en UTC.</param>
/// <param name="HypothesisId">Hipótesis de la que sale.</param>
/// <param name="HypothesisStatement">Su enunciado.</param>
/// <param name="ExperimentId">Experimento que lo sustenta.</param>
/// <param name="ExperimentName">Su nombre.</param>
/// <param name="ReplacesId">Aprendizaje al que corrige.</param>
/// <param name="ReplacedById">Aprendizaje que lo corrigió.</param>
/// <param name="Evidence">Anuncios, videos y tramos que lo sustentan.</param>
/// <param name="Review">Lo que dicen hoy los datos; nulo si ya no está vigente.</param>
public sealed record LearningDetail(
    long Id, string Statement, string DataUsed, string Result, LearningConfidence Confidence, string ConfidenceReason, DateOnly? PeriodStart,
    DateOnly? PeriodEnd, string Recommendation, string Applicability, string CriteriaVersion, LearningStatus Status, string? StatusReason,
    DateTime? StatusChangedAt, DateTime CreatedAt, long? HypothesisId, string? HypothesisStatement, long? ExperimentId, string? ExperimentName,
    long? ReplacesId, long? ReplacedById, List<LearningEvidenceView> Evidence, LearningReview? Review);

/// <summary>Proyección intermedia: una hipótesis con los conteos de los que se deduce su estado.</summary>
public sealed class HypothesisRow
{
    /// <summary>Identificador.</summary>
    public long Id { get; init; }

    /// <summary>Enunciado.</summary>
    public string Statement { get; init; } = string.Empty;

    /// <summary>Origen.</summary>
    public HypothesisOrigin Origin { get; init; }

    /// <summary>Sobre qué trata.</summary>
    public ExperimentVariable Variable { get; init; }

    /// <summary>Si se descartó.</summary>
    public bool Discarded { get; init; }

    /// <summary>Experimentos.</summary>
    public int Experiments { get; init; }

    /// <summary>Experimentos sin cerrar.</summary>
    public int OpenExperiments { get; init; }

    /// <summary>Aprendizajes vigentes.</summary>
    public int ActiveLearnings { get; init; }

    /// <summary>Alta.</summary>
    public DateTime CreatedAt { get; init; }
}

/// <summary>Lo que el sistema sabe de la fuente de un aprendizaje con los datos de hoy.</summary>
public sealed class LearningSource
{
    /// <summary>Experimento.</summary>
    public long? ExperimentId { get; init; }

    /// <summary>Hipótesis.</summary>
    public long? HypothesisId { get; init; }

    /// <summary>Nombre del experimento o del componente.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Enunciado propuesto.</summary>
    public string SuggestedStatement { get; init; } = string.Empty;

    /// <summary>Qué datos se usan.</summary>
    public string DataUsed { get; init; } = string.Empty;

    /// <summary>El resultado en palabras.</summary>
    public string Result { get; init; } = string.Empty;

    /// <summary>Confianza; nulo si los datos no alcanzan para afirmar nada.</summary>
    public LearningBasis? Basis { get; init; }

    /// <summary>Primer día de los datos.</summary>
    public DateOnly? PeriodStart { get; init; }

    /// <summary>Último día de los datos.</summary>
    public DateOnly? PeriodEnd { get; init; }

    /// <summary>Anuncios cuyas métricas se usan.</summary>
    public List<long> AdIds { get; init; } = [];

    /// <summary>El tramo sobre el que trata, si es un fragmento.</summary>
    public ComponentRef? Fragment { get; init; }
}

/// <summary>Lo que necesita el formulario de un experimento nuevo.</summary>
/// <param name="Ads">Anuncios para elegir como brazo.</param>
/// <param name="HypothesisId">Hipótesis que se va a probar; nulo si el experimento es suelto.</param>
/// <param name="Hypothesis">Su enunciado, para proponerlo.</param>
/// <param name="Variable">Sobre qué trata, para proponerlo.</param>
public sealed record ExperimentForm(List<AdOption> Ads, long? HypothesisId, string? Hypothesis, ExperimentVariable Variable);

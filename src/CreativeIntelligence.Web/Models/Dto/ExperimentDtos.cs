using System.ComponentModel.DataAnnotations;

namespace CreativeIntelligence.Web.Models.Dto;

/// <summary>Un brazo al crear un experimento.</summary>
public sealed class ExperimentArmBody
{
    /// <summary>Nombre del brazo; si falta se usa el del anuncio.</summary>
    [StringLength(100, ErrorMessage = "El nombre de un brazo admite hasta 100 caracteres.")]
    public string? Name { get; set; }

    /// <summary>Anuncio; un renglón sin anuncio se ignora.</summary>
    public long? AdId { get; set; }
}

/// <summary>Datos para crear un experimento. Todo se define antes de mirar resultados.</summary>
public sealed class CreateExperimentBody
{
    /// <summary>Nombre.</summary>
    [Required(ErrorMessage = "El nombre es obligatorio.")]
    [StringLength(200, ErrorMessage = "El nombre admite hasta 200 caracteres.")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Hipótesis.</summary>
    [Required(ErrorMessage = "Escribí la hipótesis: qué creés que va a pasar y por qué.")]
    [StringLength(1000, ErrorMessage = "La hipótesis admite hasta 1000 caracteres.")]
    public string Hypothesis { get; set; } = string.Empty;

    /// <summary>Qué se cambia entre los brazos.</summary>
    public ExperimentVariable Variable { get; set; }

    /// <summary>Con qué se mide.</summary>
    public ExperimentMetric Metric { get; set; }

    /// <summary>Primer día.</summary>
    [Required(ErrorMessage = "Indicá desde qué día se mide.")]
    public DateOnly? StartDate { get; set; }

    /// <summary>Último día; opcional.</summary>
    public DateOnly? EndDate { get; set; }

    /// <summary>Impresiones mínimas por brazo.</summary>
    [Range(1000, 10_000_000, ErrorMessage = "El mínimo de impresiones por brazo va de 1.000 a 10.000.000.")]
    public int MinImpressionsPerArm { get; set; } = ExperimentRules.DefaultMinImpressions;

    /// <summary>Hipótesis de la memoria de aprendizaje que se pone a prueba; opcional.</summary>
    public long? HypothesisId { get; set; }

    /// <summary>Brazos, el primero es el control.</summary>
    public List<ExperimentArmBody> Arms { get; set; } = [];
}

/// <summary>Conclusión de un experimento.</summary>
public sealed class ConcludeExperimentBody
{
    /// <summary>Qué se concluye y qué se decide.</summary>
    [Required(ErrorMessage = "Escribí la conclusión.")]
    [StringLength(2000, ErrorMessage = "La conclusión admite hasta 2000 caracteres.")]
    public string Conclusion { get; set; } = string.Empty;
}

/// <summary>Veredicto de un experimento.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<ExperimentVerdict>))]
public enum ExperimentVerdict
{
    /// <summary>Algún brazo no llegó al mínimo de impresiones o no tiene el dato de la métrica.</summary>
    NotEnoughData,

    /// <summary>Ningún brazo se distingue del control.</summary>
    NoDifference,

    /// <summary>Algún brazo difiere del control y difícilmente sea azar.</summary>
    ProbableDifference,

    /// <summary>Algún brazo difiere del control y muy difícilmente sea azar.</summary>
    ClearDifference,
}

/// <summary>Lo que se sabe de un brazo para evaluarlo.</summary>
/// <param name="Name">Nombre del brazo.</param>
/// <param name="AdId">Anuncio.</param>
/// <param name="AdName">Nombre del anuncio.</param>
/// <param name="CampaignId">Campaña del anuncio.</param>
/// <param name="Metrics">Métricas del período.</param>
/// <param name="Components">Componentes de su creatividad; nulo si el anuncio no está vinculado a un video.</param>
public sealed record ArmData(string Name, long AdId, string AdName, long CampaignId, AdMetrics Metrics, AdComponents? Components);

/// <summary>El resultado de un brazo.</summary>
/// <param name="Name">Nombre del brazo.</param>
/// <param name="AdId">Anuncio.</param>
/// <param name="AdName">Nombre del anuncio.</param>
/// <param name="IsControl">Si es el control.</param>
/// <param name="Metrics">Métricas del período.</param>
/// <param name="Rate">Valor de la métrica del experimento, de 0 a 1; nulo si no hay dato.</param>
/// <param name="Signal">Qué tan distinguible del azar es su diferencia con el control.</param>
/// <param name="Higher">Si su métrica es mayor que la del control.</param>
/// <param name="Lift">Diferencia relativa con el control (0,25 = 25 % más); nulo para el control o sin dato.</param>
public sealed record ArmResult(
    string Name, long AdId, string AdName, bool IsControl, AdMetrics Metrics, decimal? Rate, SignalStrength Signal, bool Higher, decimal? Lift);

/// <summary>El resultado calculado de un experimento.</summary>
/// <param name="Verdict">Veredicto.</param>
/// <param name="Provisional">Verdadero si el período no terminó: mirar antes de tiempo aumenta el riesgo de ver una
/// diferencia que no es.</param>
/// <param name="Summary">El resultado en palabras.</param>
/// <param name="Warnings">Problemas de diseño que debilitan la conclusión.</param>
/// <param name="Arms">Resultado por brazo.</param>
public sealed record ExperimentResult(ExperimentVerdict Verdict, bool Provisional, string Summary, List<string> Warnings, List<ArmResult> Arms);

/// <summary>Un experimento en el listado.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Name">Nombre.</param>
/// <param name="Variable">Qué se cambia.</param>
/// <param name="Metric">Con qué se mide.</param>
/// <param name="StartDate">Primer día.</param>
/// <param name="EndDate">Último día.</param>
/// <param name="Arms">Cantidad de brazos.</param>
/// <param name="Concluded">Si ya tiene conclusión.</param>
public sealed record ExperimentListItem(
    long Id, string Name, ExperimentVariable Variable, ExperimentMetric Metric, DateOnly StartDate, DateOnly? EndDate, int Arms, bool Concluded);

/// <summary>Un experimento con su resultado.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Name">Nombre.</param>
/// <param name="Hypothesis">Hipótesis.</param>
/// <param name="Variable">Qué se cambia.</param>
/// <param name="Metric">Con qué se mide.</param>
/// <param name="StartDate">Primer día.</param>
/// <param name="EndDate">Último día.</param>
/// <param name="MinImpressionsPerArm">Impresiones mínimas por brazo.</param>
/// <param name="Conclusion">Conclusión escrita por una persona.</param>
/// <param name="ConcludedAt">Cuándo se cerró.</param>
/// <param name="Result">Resultado calculado sobre las métricas del período.</param>
/// <param name="HypothesisId">Hipótesis de la memoria de aprendizaje que pone a prueba; nulo si es suelto.</param>
public sealed record ExperimentDetail(
    long Id, string Name, string Hypothesis, ExperimentVariable Variable, ExperimentMetric Metric, DateOnly StartDate, DateOnly? EndDate,
    int MinImpressionsPerArm, string? Conclusion, DateTime? ConcludedAt, ExperimentResult Result, long? HypothesisId);

/// <summary>Un anuncio para elegir como brazo.</summary>
/// <param name="Id">Anuncio.</param>
/// <param name="Label">Nombre con su campaña.</param>
public sealed record AdOption(long Id, string Label);

/// <summary>Un experimento en el que participa un anuncio.</summary>
/// <param name="Id">Experimento.</param>
/// <param name="Name">Nombre.</param>
/// <param name="ArmName">Brazo que ocupa el anuncio.</param>
public sealed record AdExperiment(long Id, string Name, string ArmName);

/// <summary>Proyección intermedia: un brazo con su anuncio y su campaña.</summary>
public sealed class ArmRow
{
    /// <summary>Nombre del brazo.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Anuncio.</summary>
    public long AdId { get; init; }

    /// <summary>Nombre del anuncio.</summary>
    public string AdName { get; init; } = string.Empty;

    /// <summary>Campaña.</summary>
    public long CampaignId { get; init; }
}

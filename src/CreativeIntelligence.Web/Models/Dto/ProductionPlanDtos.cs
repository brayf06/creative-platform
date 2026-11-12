using System.ComponentModel.DataAnnotations;

namespace CreativeIntelligence.Web.Models.Dto;

/// <summary>Datos para armar un plan de producción.</summary>
public sealed class CreateProductionPlanBody
{
    /// <summary>Nombre.</summary>
    [Required(ErrorMessage = "El nombre es obligatorio.")]
    [StringLength(200, ErrorMessage = "El nombre admite hasta 200 caracteres.")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Cuántas hipótesis incluir, de las de mayor prioridad.</summary>
    [Range(1, 20, ErrorMessage = "Un plan lleva de 1 a 20 hipótesis.")]
    public int Count { get; set; } = 5;
}

/// <summary>El aprendizaje vigente que mejor respalda una hipótesis.</summary>
/// <param name="Id">Aprendizaje.</param>
/// <param name="Statement">Qué se aprendió.</param>
/// <param name="Recommendation">Qué conviene hacer.</param>
/// <param name="Confidence">Confianza.</param>
public sealed record SupportingLearning(long Id, string Statement, string Recommendation, LearningConfidence Confidence);

/// <summary>Un plan de producción en el listado.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Name">Nombre.</param>
/// <param name="Items">Hipótesis que incluye.</param>
/// <param name="WeightsVersion">Versión de las ponderaciones con que se armó.</param>
/// <param name="CreatedAt">Alta, en UTC.</param>
public sealed record ProductionPlanListItem(long Id, string Name, int Items, int WeightsVersion, DateTime CreatedAt);

/// <summary>Una hipótesis de un plan, con todo lo que justificó elegirla.</summary>
/// <param name="Position">Orden de prioridad.</param>
/// <param name="HypothesisId">Hipótesis.</param>
/// <param name="Statement">Enunciado al armar el plan.</param>
/// <param name="Variable">Sobre qué parte de la creatividad trata.</param>
/// <param name="Component">Componente concreto.</param>
/// <param name="AssetId">Original del fragmento.</param>
/// <param name="StartMs">Inicio del fragmento.</param>
/// <param name="EndMs">Fin del fragmento.</param>
/// <param name="Action">Qué se propone hacer.</param>
/// <param name="Score">Puntaje al armar el plan.</param>
/// <param name="Justification">Por qué.</param>
/// <param name="Factors">Desglose del puntaje.</param>
/// <param name="LearningId">Aprendizaje que la respaldaba.</param>
/// <param name="Recommendation">Su recomendación, como estaba.</param>
/// <param name="LearningStatus">Cómo está hoy ese aprendizaje: si ya no está vigente, el plan quedó desactualizado.</param>
public sealed record ProductionPlanItemView(
    int Position, long HypothesisId, string Statement, ExperimentVariable Variable, string? Component, long? AssetId, int? StartMs, int? EndMs,
    PlanAction Action, decimal Score, string Justification, List<FactorValue> Factors, long? LearningId, string? Recommendation,
    LearningStatus? LearningStatus);

/// <summary>Un plan de producción completo.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Name">Nombre.</param>
/// <param name="RequestedItems">Hipótesis pedidas.</param>
/// <param name="WeightsVersion">Versión de las ponderaciones con que se armó.</param>
/// <param name="CurrentWeightsVersion">Versión vigente hoy.</param>
/// <param name="CreatedAt">Alta, en UTC.</param>
/// <param name="Items">Hipótesis, por orden de prioridad.</param>
public sealed record ProductionPlanDetail(
    long Id, string Name, int RequestedItems, int WeightsVersion, int CurrentWeightsVersion, DateTime CreatedAt, List<ProductionPlanItemView> Items);

/// <summary>Proyección intermedia: una hipótesis de un plan tal como está guardada, con el estado de hoy de su aprendizaje.</summary>
public sealed class ProductionPlanItemRow
{
    /// <summary>La hipótesis del plan.</summary>
    public ProductionPlanItem Item { get; init; } = null!;

    /// <summary>Estado de hoy del aprendizaje que la respaldaba.</summary>
    public LearningStatus? LearningStatus { get; init; }
}

/// <summary>Proyección intermedia: un aprendizaje vigente con la hipótesis a la que respalda.</summary>
public sealed class SupportRow
{
    /// <summary>Hipótesis.</summary>
    public long HypothesisId { get; init; }

    /// <summary>El aprendizaje.</summary>
    public SupportingLearning Learning { get; init; } = null!;
}

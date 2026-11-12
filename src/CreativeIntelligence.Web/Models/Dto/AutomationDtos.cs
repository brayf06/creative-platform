using System.ComponentModel.DataAnnotations;

namespace CreativeIntelligence.Web.Models.Dto;

/// <summary>Modo y límites de la automatización.</summary>
public sealed class AutomationPolicyBody
{
    /// <summary>Modo de autonomía.</summary>
    public AutomationMode Mode { get; set; }

    /// <summary>Presupuesto diario máximo de una acción.</summary>
    [Range(0, 1_000_000, ErrorMessage = "El presupuesto máximo por acción va de 0 a 1.000.000.")]
    public decimal MaxBudgetPerAction { get; set; }

    /// <summary>Presupuesto diario máximo que pueden comprometer todas las acciones de un día.</summary>
    [Range(0, 10_000_000, ErrorMessage = "El presupuesto máximo diario va de 0 a 10.000.000.")]
    public decimal MaxDailyBudget { get; set; }

    /// <summary>Cantidad máxima de acciones por día.</summary>
    [Range(0, 1000, ErrorMessage = "La cantidad de acciones por día va de 0 a 1000.")]
    public int MaxActionsPerDay { get; set; }

    /// <summary>Presupuesto diario máximo que puede comprometer un lote de acciones propuestas de una vez.</summary>
    [Range(0, 10_000_000, ErrorMessage = "El presupuesto máximo por lote va de 0 a 10.000.000.")]
    public decimal MaxBatchBudget { get; set; }
}

/// <summary>Pedido de que el sistema proponga las publicaciones de un plan de producción.</summary>
public sealed class ProposeFromPlanBody
{
    /// <summary>Plan de producción.</summary>
    [Required(ErrorMessage = "Elegí el plan de producción.")]
    public long? PlanId { get; set; }

    /// <summary>Conjunto de anuncios donde se publicarían.</summary>
    [Required(ErrorMessage = "Elegí el conjunto de anuncios.")]
    public long? AdSetId { get; set; }

    /// <summary>Presupuesto diario de cada publicación.</summary>
    [Range(0.01, 1_000_000, ErrorMessage = "El presupuesto diario tiene que ser mayor que cero.")]
    public decimal DailyBudget { get; set; }
}

/// <summary>Qué propuso el sistema a partir de un plan.</summary>
/// <param name="ActionIds">Acciones creadas.</param>
/// <param name="Skipped">Renglones del plan que no dieron una acción, cada uno con su motivo.</param>
public sealed record ProposalResult(List<long> ActionIds, List<string> Skipped);

/// <summary>Las métricas de un anuncio en un día, para buscar anomalías.</summary>
/// <param name="Date">Día.</param>
/// <param name="Spend">Gasto.</param>
/// <param name="Impressions">Impresiones.</param>
/// <param name="Clicks">Clics.</param>
public sealed record AnomalyDay(DateOnly Date, decimal Spend, long Impressions, long Clicks);

/// <summary>Una anomalía encontrada por las reglas.</summary>
/// <param name="Kind">Qué se detectó.</param>
/// <param name="Date">Día en que se ve.</param>
/// <param name="Description">Lo observado, con sus cifras.</param>
public sealed record AnomalyFinding(AnomalyKind Kind, DateOnly Date, string Description);

/// <summary>Una anomalía para mostrar.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="AdId">Anuncio.</param>
/// <param name="AdName">Nombre del anuncio.</param>
/// <param name="Kind">Qué se detectó.</param>
/// <param name="Date">Día.</param>
/// <param name="Description">Lo observado.</param>
/// <param name="Reviewed">Si ya la revisó una persona.</param>
/// <param name="ReviewNote">Qué concluyó.</param>
public sealed record AnomalyItem(long Id, long AdId, string AdName, AnomalyKind Kind, DateOnly Date, string Description, bool Reviewed, string? ReviewNote);

/// <summary>Un plan de producción para elegir.</summary>
/// <param name="Id">Plan.</param>
/// <param name="Name">Nombre.</param>
public sealed record PlanOption(long Id, string Name);

/// <summary>Proyección intermedia: un día de métricas de un anuncio.</summary>
public sealed class AnomalyDayRow
{
    /// <summary>Anuncio.</summary>
    public long AdId { get; init; }

    /// <summary>El día.</summary>
    public AnomalyDay Day { get; init; } = null!;
}

/// <summary>Proyección intermedia: un video generado listo, con su receta.</summary>
/// <param name="VariantId">Video.</param>
/// <param name="RecipeId">Receta.</param>
public sealed record ReadyVariantRow(long VariantId, long RecipeId);

/// <summary>Pedido de publicar un video generado como anuncio.</summary>
public sealed class PublishRequestBody
{
    /// <summary>Video generado.</summary>
    [Required(ErrorMessage = "Elegí el video a publicar.")]
    public long? VariantId { get; set; }

    /// <summary>Conjunto de anuncios donde se publica.</summary>
    [Required(ErrorMessage = "Elegí el conjunto de anuncios.")]
    public long? AdSetId { get; set; }

    /// <summary>Nombre del anuncio.</summary>
    [Required(ErrorMessage = "El nombre del anuncio es obligatorio.")]
    [StringLength(300, ErrorMessage = "El nombre del anuncio admite hasta 300 caracteres.")]
    public string AdName { get; set; } = string.Empty;

    /// <summary>Presupuesto diario que compromete.</summary>
    [Range(0.01, 1_000_000, ErrorMessage = "El presupuesto diario tiene que ser mayor que cero.")]
    public decimal DailyBudget { get; set; }

    /// <summary>Hipótesis que la respalda; opcional.</summary>
    public long? HypothesisId { get; set; }
}

/// <summary>Nota de quien aprueba o rechaza una acción.</summary>
public sealed class DecisionBody
{
    /// <summary>Nota; opcional.</summary>
    [StringLength(500, ErrorMessage = "La nota admite hasta 500 caracteres.")]
    public string? Note { get; set; }
}

/// <summary>Lo que las guardas necesitan saber de la política.</summary>
/// <param name="KillSwitchActive">Si el kill switch está activo.</param>
/// <param name="Mode">Modo de autonomía.</param>
/// <param name="MaxBudgetPerAction">Presupuesto diario máximo de una acción.</param>
/// <param name="MaxDailyBudget">Presupuesto diario máximo de todas las acciones del día.</param>
/// <param name="MaxActionsPerDay">Cantidad máxima de acciones por día.</param>
public sealed record AutomationLimits(
    bool KillSwitchActive, AutomationMode Mode, decimal MaxBudgetPerAction, decimal MaxDailyBudget, int MaxActionsPerDay);

/// <summary>Lo ya comprometido hoy por las demás acciones.</summary>
/// <param name="Actions">Cantidad de acciones.</param>
/// <param name="Budget">Presupuesto diario que suman.</param>
public sealed record DailyUsage(int Actions, decimal Budget);

/// <summary>Qué deciden las guardas sobre una acción.</summary>
/// <param name="Blocked">Si no puede seguir.</param>
/// <param name="CancelledByKillSwitch">Si lo que la frena es el kill switch.</param>
/// <param name="NeedsApproval">Si la tiene que decidir una persona.</param>
/// <param name="Reason">Por qué se bloquea o por qué necesita aprobación; vacío si puede ejecutarse sola.</param>
public sealed record GuardVerdict(bool Blocked, bool CancelledByKillSwitch, bool NeedsApproval, string Reason);

/// <summary>La política de automatización para mostrar.</summary>
/// <param name="Mode">Modo.</param>
/// <param name="RealExecutionEnabled">Si las acciones escriben de verdad en Meta.</param>
/// <param name="KillSwitchActive">Si el kill switch está activo.</param>
/// <param name="MaxBudgetPerAction">Presupuesto diario máximo de una acción.</param>
/// <param name="MaxDailyBudget">Presupuesto diario máximo del día.</param>
/// <param name="MaxActionsPerDay">Acciones máximas por día.</param>
/// <param name="MaxBatchBudget">Presupuesto diario máximo de un lote propuesto de una vez.</param>
public sealed record AutomationPolicyView(
    AutomationMode Mode, bool RealExecutionEnabled, bool KillSwitchActive, decimal MaxBudgetPerAction, decimal MaxDailyBudget, int MaxActionsPerDay,
    decimal MaxBatchBudget);

/// <summary>Una acción en el listado.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="VariantId">Video.</param>
/// <param name="RecipeName">Receta del video.</param>
/// <param name="AdSetName">Conjunto, con su campaña.</param>
/// <param name="AdName">Nombre del anuncio.</param>
/// <param name="DailyBudget">Presupuesto diario.</param>
/// <param name="IsSimulation">Si es una simulación.</param>
/// <param name="Status">Estado.</param>
/// <param name="ApprovalReason">Por qué necesitó aprobación.</param>
/// <param name="Result">Qué pasó.</param>
/// <param name="DecisionNote">Nota de quien decidió.</param>
/// <param name="CreatedAt">Alta, en UTC.</param>
/// <param name="PlanId">Plan de producción del que el sistema la propuso.</param>
public sealed record AutomationActionItem(
    long Id, long VariantId, string RecipeName, string AdSetName, string AdName, decimal DailyBudget, bool IsSimulation,
    AutomationActionStatus Status, string? ApprovalReason, string? Result, string? DecisionNote, DateTime CreatedAt, long? PlanId);

/// <summary>Un conjunto de anuncios para elegir.</summary>
/// <param name="Id">Conjunto.</param>
/// <param name="Label">Campaña y conjunto.</param>
public sealed record AdSetOption(long Id, string Label);

/// <summary>Una hipótesis para elegir como respaldo.</summary>
/// <param name="Id">Hipótesis.</param>
/// <param name="Statement">Enunciado.</param>
public sealed record HypothesisOption(long Id, string Statement);

/// <summary>Una activación o desactivación del kill switch.</summary>
/// <param name="Activated">Si se activó.</param>
/// <param name="Reason">Motivo.</param>
/// <param name="CancelledActions">Acciones que canceló.</param>
/// <param name="CreatedAt">Cuándo, en UTC.</param>
public sealed record KillSwitchItem(bool Activated, string Reason, int CancelledActions, DateTime CreatedAt);

/// <summary>Todo lo que muestra la pantalla de automatización.</summary>
/// <param name="Policy">Política vigente.</param>
/// <param name="MetaConnected">Si hay conexión con Meta para publicar de verdad.</param>
/// <param name="Today">Lo comprometido hoy.</param>
/// <param name="Actions">Últimas acciones.</param>
/// <param name="Variants">Videos listos para publicar.</param>
/// <param name="AdSets">Conjuntos de anuncios conocidos.</param>
/// <param name="Hypotheses">Hipótesis en pie.</param>
/// <param name="KillSwitch">Historia reciente del kill switch.</param>
/// <param name="Anomalies">Anomalías, las abiertas primero.</param>
/// <param name="Plans">Planes de producción recientes.</param>
public sealed record AutomationOverview(
    AutomationPolicyView Policy, bool MetaConnected, DailyUsage Today, List<AutomationActionItem> Actions, List<VariantOption> Variants,
    List<AdSetOption> AdSets, List<HypothesisOption> Hypotheses, List<KillSwitchItem> KillSwitch, List<AnomalyItem> Anomalies, List<PlanOption> Plans);

/// <summary>Lo que se le pide al publicador de Meta.</summary>
/// <param name="IdempotencyKey">Clave que identifica la acción: repetir el pedido con la misma clave no publica dos veces.</param>
/// <param name="AdSetExternalId">Conjunto de anuncios, por su identificador en Meta.</param>
/// <param name="AdName">Nombre del anuncio.</param>
/// <param name="VideoPath">Ruta del video dentro del almacenamiento.</param>
/// <param name="DailyBudget">Presupuesto diario.</param>
public sealed record MetaPublishRequest(string IdempotencyKey, string AdSetExternalId, string AdName, string VideoPath, decimal DailyBudget);

/// <summary>Parámetros del trabajo que ejecuta una acción.</summary>
/// <param name="ActionId">Acción.</param>
public sealed record AutomationExecutePayload(long ActionId);

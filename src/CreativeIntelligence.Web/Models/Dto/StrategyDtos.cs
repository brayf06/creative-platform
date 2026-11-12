using System.ComponentModel.DataAnnotations;

namespace CreativeIntelligence.Web.Models.Dto;

/// <summary>Datos del perfil de una marca.</summary>
public sealed class BrandBody
{
    /// <summary>Nombre.</summary>
    [Required(ErrorMessage = "El nombre de la marca es obligatorio.")]
    [StringLength(150, ErrorMessage = "El nombre admite hasta 150 caracteres.")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Propuesta de valor.</summary>
    [StringLength(1000, ErrorMessage = "La propuesta de valor admite hasta 1000 caracteres.")]
    public string? ValueProposition { get; set; }

    /// <summary>Estilo de comunicación.</summary>
    [StringLength(1000, ErrorMessage = "El estilo de comunicación admite hasta 1000 caracteres.")]
    public string? CommunicationStyle { get; set; }

    /// <summary>Identidad visual.</summary>
    [StringLength(1000, ErrorMessage = "La identidad visual admite hasta 1000 caracteres.")]
    public string? VisualIdentity { get; set; }

    /// <summary>Objetivos comerciales.</summary>
    [StringLength(1000, ErrorMessage = "Los objetivos admiten hasta 1000 caracteres.")]
    public string? Objectives { get; set; }

    /// <summary>Restricciones comerciales.</summary>
    [StringLength(1000, ErrorMessage = "Las restricciones admiten hasta 1000 caracteres.")]
    public string? CommercialRestrictions { get; set; }

    /// <summary>Presupuesto publicitario mensual, en dólares.</summary>
    [Range(0, 10_000_000, ErrorMessage = "El presupuesto mensual va de 0 a 10.000.000.")]
    public decimal? MonthlyBudgetUsd { get; set; }
}

/// <summary>Un elemento para agregar a una lista del perfil de una marca.</summary>
public sealed class BrandItemBody
{
    /// <summary>A qué lista va.</summary>
    public BrandItemKind Kind { get; set; }

    /// <summary>Nombre o frase.</summary>
    [Required(ErrorMessage = "El texto es obligatorio.")]
    [StringLength(300, ErrorMessage = "El texto admite hasta 300 caracteres.")]
    public string Text { get; set; } = string.Empty;

    /// <summary>Detalle.</summary>
    [StringLength(1000, ErrorMessage = "El detalle admite hasta 1000 caracteres.")]
    public string? Detail { get; set; }

    /// <summary>Margen, sólo para productos.</summary>
    [Range(0, 100, ErrorMessage = "El margen va de 0 a 100 %.")]
    public decimal? MarginPercent { get; set; }
}

/// <summary>Una marca en el listado.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Name">Nombre.</param>
/// <param name="Products">Cantidad de productos.</param>
/// <param name="ForbiddenMessages">Cantidad de mensajes prohibidos.</param>
/// <param name="Briefs">Cantidad de briefs.</param>
public sealed record BrandListItem(int Id, string Name, int Products, int ForbiddenMessages, int Briefs);

/// <summary>Un elemento de una lista del perfil.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Kind">A qué lista pertenece.</param>
/// <param name="Text">Nombre o frase.</param>
/// <param name="Detail">Detalle.</param>
/// <param name="MarginPercent">Margen, si es un producto.</param>
public sealed record BrandItemView(int Id, BrandItemKind Kind, string Text, string? Detail, decimal? MarginPercent);

/// <summary>El perfil completo de una marca.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Name">Nombre.</param>
/// <param name="ValueProposition">Propuesta de valor.</param>
/// <param name="CommunicationStyle">Estilo de comunicación.</param>
/// <param name="VisualIdentity">Identidad visual.</param>
/// <param name="Objectives">Objetivos.</param>
/// <param name="CommercialRestrictions">Restricciones comerciales.</param>
/// <param name="MonthlyBudgetUsd">Presupuesto mensual.</param>
/// <param name="Items">Productos, beneficios, públicos, objeciones y mensajes.</param>
public sealed record BrandDetail(
    int Id, string Name, string? ValueProposition, string? CommunicationStyle, string? VisualIdentity, string? Objectives,
    string? CommercialRestrictions, decimal? MonthlyBudgetUsd, List<BrandItemView> Items)
{
    /// <summary>Los elementos de una lista, por orden alfabético.</summary>
    /// <param name="kind">Lista.</param>
    /// <returns>Sus elementos.</returns>
    public List<BrandItemView> Of(BrandItemKind kind)
    {
        return Items.Where(i => i.Kind == kind).ToList();
    }
}

/// <summary>Datos de un brief de campaña.</summary>
public sealed class BriefBody
{
    /// <summary>Marca.</summary>
    [Range(1, int.MaxValue, ErrorMessage = "Elegí una marca.")]
    public int BrandId { get; set; }

    /// <summary>Nombre del brief.</summary>
    [Required(ErrorMessage = "El nombre del brief es obligatorio.")]
    [StringLength(200, ErrorMessage = "El nombre admite hasta 200 caracteres.")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Texto libre original.</summary>
    [StringLength(4000, ErrorMessage = "El texto del brief admite hasta 4000 caracteres.")]
    public string? OriginalText { get; set; }

    /// <summary>Producto de la marca; opcional.</summary>
    public int? ProductId { get; set; }

    /// <summary>Ocasión.</summary>
    [StringLength(200, ErrorMessage = "La ocasión admite hasta 200 caracteres.")]
    public string? Occasion { get; set; }

    /// <summary>Público.</summary>
    [StringLength(500, ErrorMessage = "El público admite hasta 500 caracteres.")]
    public string? Audience { get; set; }

    /// <summary>Objetivo.</summary>
    [StringLength(500, ErrorMessage = "El objetivo admite hasta 500 caracteres.")]
    public string? Objective { get; set; }

    /// <summary>Mensaje principal.</summary>
    [StringLength(1000, ErrorMessage = "El mensaje admite hasta 1000 caracteres.")]
    public string? Message { get; set; }

    /// <summary>Enfoques a priorizar.</summary>
    [StringLength(1000, ErrorMessage = "Los enfoques admiten hasta 1000 caracteres.")]
    public string? Approaches { get; set; }

    /// <summary>Duración de cada video, en segundos.</summary>
    [Range(3, 180, ErrorMessage = "La duración va de 3 a 180 segundos.")]
    public int? DurationSeconds { get; set; }

    /// <summary>Cantidad de videos.</summary>
    [Range(1, 50, ErrorMessage = "La cantidad de videos va de 1 a 50.")]
    public int? VideoCount { get; set; }

    /// <summary>Plataforma de destino.</summary>
    public BriefPlatform Platform { get; set; }

    /// <summary>Si se pide investigación externa.</summary>
    public bool UseExternalResearch { get; set; }

    /// <summary>Presupuesto máximo de generación, en dólares.</summary>
    [Range(0, 100_000, ErrorMessage = "El presupuesto de generación va de 0 a 100.000.")]
    public decimal? MaxGenerationBudgetUsd { get; set; }

    /// <summary>Nivel de autonomía.</summary>
    public AutonomyLevel Autonomy { get; set; }
}

/// <summary>Texto libre de un brief para interpretar.</summary>
public sealed class InterpretBriefBody
{
    /// <summary>Lo que escribió la persona.</summary>
    [StringLength(4000, ErrorMessage = "El texto del brief admite hasta 4000 caracteres.")]
    public string? Text { get; set; }
}

/// <summary>Lo que se pudo deducir del texto libre de un brief. Un campo nulo significa que el texto no lo dice.</summary>
/// <param name="VideoCount">Cantidad de videos.</param>
/// <param name="DurationSeconds">Duración de cada video.</param>
/// <param name="Platform">Plataforma.</param>
/// <param name="Occasion">Ocasión o temporada.</param>
/// <param name="UseExternalResearch">Verdadero si el texto pide referencias del mercado; nulo si no las menciona.</param>
/// <param name="Understood">Lo deducido, descrito para mostrarlo.</param>
public sealed record BriefInterpretation(
    int? VideoCount, int? DurationSeconds, BriefPlatform? Platform, string? Occasion, bool? UseExternalResearch, List<string> Understood);

/// <summary>Un brief en el listado.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Name">Nombre.</param>
/// <param name="BrandName">Marca.</param>
/// <param name="Occasion">Ocasión.</param>
/// <param name="VideoCount">Videos pedidos.</param>
/// <param name="Platform">Plataforma.</param>
/// <param name="CreatedAt">Alta, en UTC.</param>
public sealed record BriefListItem(long Id, string Name, string BrandName, string? Occasion, int? VideoCount, BriefPlatform Platform, DateTime CreatedAt);

/// <summary>Un brief con todos sus campos.</summary>
public sealed class BriefDetail
{
    /// <summary>Identificador.</summary>
    public long Id { get; init; }

    /// <summary>Marca.</summary>
    public int BrandId { get; init; }

    /// <summary>Nombre de la marca.</summary>
    public string BrandName { get; init; } = string.Empty;

    /// <summary>Nombre del brief.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Texto libre original.</summary>
    public string? OriginalText { get; init; }

    /// <summary>Producto; nulo si es la marca en general.</summary>
    public int? ProductId { get; init; }

    /// <summary>Nombre del producto.</summary>
    public string? ProductName { get; init; }

    /// <summary>Ocasión.</summary>
    public string? Occasion { get; init; }

    /// <summary>Público.</summary>
    public string? Audience { get; init; }

    /// <summary>Objetivo.</summary>
    public string? Objective { get; init; }

    /// <summary>Mensaje principal.</summary>
    public string? Message { get; init; }

    /// <summary>Enfoques.</summary>
    public string? Approaches { get; init; }

    /// <summary>Duración de cada video, en segundos.</summary>
    public int? DurationSeconds { get; init; }

    /// <summary>Cantidad de videos.</summary>
    public int? VideoCount { get; init; }

    /// <summary>Plataforma.</summary>
    public BriefPlatform Platform { get; init; }

    /// <summary>Si se pide investigación externa.</summary>
    public bool UseExternalResearch { get; init; }

    /// <summary>Presupuesto máximo de generación.</summary>
    public decimal? MaxGenerationBudgetUsd { get; init; }

    /// <summary>Nivel de autonomía.</summary>
    public AutonomyLevel Autonomy { get; init; }

    /// <summary>Alta, en UTC.</summary>
    public DateTime CreatedAt { get; init; }
}

/// <summary>Una marca con sus productos, para elegir en el formulario de un brief.</summary>
/// <param name="Id">Marca.</param>
/// <param name="Name">Nombre.</param>
/// <param name="Products">Sus productos.</param>
public sealed record BrandOption(int Id, string Name, List<BrandItemView> Products);

/// <summary>Un fragmento disponible, tal como se le describe al planificador.</summary>
/// <param name="Index">Número con el que el planificador lo nombra, desde 1.</param>
/// <param name="DurationMs">Duración.</param>
/// <param name="Functions">Funciones narrativas de su análisis; vacío si no está analizado.</param>
/// <param name="Description">Qué se ve y qué mensaje lleva.</param>
public sealed record PlanFragment(int Index, int DurationMs, List<CreativeFunction> Functions, string Description);

/// <summary>Lo que se le pide al planificador.</summary>
/// <param name="Brand">Perfil de la marca.</param>
/// <param name="Brief">Brief de la campaña.</param>
/// <param name="Fragments">Fragmentos disponibles.</param>
/// <param name="Count">Cantidad de conceptos.</param>
public sealed record PlanRequest(BrandDetail Brand, BriefDetail Brief, List<PlanFragment> Fragments, int Count)
{
    /// <summary>Referencias externas del brief; vacío si el brief no pide investigación o no tiene ninguna.</summary>
    public List<PlanReference> References { get; init; } = [];

    /// <summary>Aprendizajes vigentes de la organización, para que el plan los tenga en cuenta; vacío si no hay.</summary>
    public List<PlanLearning> Learnings { get; init; } = [];
}

/// <summary>Un aprendizaje vigente tal como se le pasa al planificador.</summary>
/// <param name="Statement">Qué se aprendió.</param>
/// <param name="Recommendation">Qué conviene hacer.</param>
/// <param name="Applicability">Para qué casos vale.</param>
/// <param name="Confidence">Confianza calculada por el sistema.</param>
public sealed record PlanLearning(string Statement, string Recommendation, string Applicability, LearningConfidence Confidence);

/// <summary>Un concepto tal como lo propone el planificador.</summary>
/// <param name="Title">Nombre corto.</param>
/// <param name="Hypothesis">Qué se cree que va a pasar y por qué.</param>
/// <param name="Approach">Enfoque.</param>
/// <param name="Hook">Gancho de apertura.</param>
/// <param name="Script">Guion.</param>
/// <param name="Development">Desarrollo.</param>
/// <param name="Cta">Llamada a la acción.</param>
/// <param name="Audience">Público previsto.</param>
/// <param name="RequiredMaterial">Material que requiere.</param>
/// <param name="Reason">Por qué se eligió.</param>
/// <param name="Risks">Riesgos.</param>
/// <param name="Novelty">Novedad, de 1 a 5.</param>
/// <param name="Fragments">Números de los fragmentos existentes que usaría, en orden.</param>
/// <param name="Missing">Material que falta.</param>
public sealed record ConceptProposal(
    string Title, string Hypothesis, string Approach, string Hook, string Script, string Development, string Cta, string Audience,
    string RequiredMaterial, string Reason, string Risks, int Novelty, List<int> Fragments, List<string> Missing);

/// <summary>Lo que devuelve el planificador.</summary>
/// <param name="Concepts">Conceptos propuestos.</param>
/// <param name="Provider">Proveedor.</param>
/// <param name="Model">Modelo.</param>
/// <param name="PromptVersion">Versión de la consigna.</param>
/// <param name="InputTokens">Tokens de entrada.</param>
/// <param name="OutputTokens">Tokens de salida.</param>
/// <param name="CostUsd">Costo en dólares.</param>
/// <param name="DurationMs">Cuánto tardó.</param>
public sealed record PlanOutcome(
    List<ConceptProposal> Concepts, string Provider, string Model, string PromptVersion, int InputTokens, int OutputTokens,
    decimal CostUsd, int DurationMs);

/// <summary>Estimación de un plan antes de pedirlo.</summary>
/// <param name="Configured">Falso si falta la clave del proveedor.</param>
/// <param name="Model">Modelo que se usaría.</param>
/// <param name="Concepts">Conceptos que se pedirían.</param>
/// <param name="Fragments">Fragmentos disponibles que se le describirían.</param>
/// <param name="InputTokens">Tokens de entrada estimados.</param>
/// <param name="OutputTokens">Tokens de salida estimados.</param>
/// <param name="CostUsd">Costo estimado.</param>
/// <param name="MaxCostUsd">Tope: el menor entre el tope por plan y el presupuesto de generación del brief.</param>
public sealed record PlanEstimate(
    bool Configured, string Model, int Concepts, int Fragments, int InputTokens, int OutputTokens, decimal CostUsd, decimal MaxCostUsd);

/// <summary>Un plan en el listado de un brief.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Status">Estado.</param>
/// <param name="Error">Por qué falló.</param>
/// <param name="Concepts">Conceptos que tiene.</param>
/// <param name="Recipes">Conceptos que ya tienen receta.</param>
/// <param name="CostUsd">Lo que costó.</param>
/// <param name="CreatedAt">Pedido, en UTC.</param>
public sealed record PlanListItem(long Id, MediaProcessingStatus Status, string? Error, int Concepts, int Recipes, decimal CostUsd, DateTime CreatedAt);

/// <summary>Un fragmento existente que un concepto usaría.</summary>
/// <param name="SegmentId">Fragmento de la biblioteca; nulo si ya no existe.</param>
/// <param name="AssetId">Video original.</param>
/// <param name="FileName">Nombre del video.</param>
/// <param name="StartMs">Inicio del tramo.</param>
/// <param name="EndMs">Fin del tramo.</param>
public sealed record ConceptFragmentView(long? SegmentId, long AssetId, string FileName, int StartMs, int EndMs);

/// <summary>Un concepto de un plan.</summary>
public sealed class ConceptView
{
    /// <summary>Identificador.</summary>
    public long Id { get; init; }

    /// <summary>Orden dentro del plan, desde 1.</summary>
    public int Position { get; init; }

    /// <summary>Nombre corto.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Hipótesis.</summary>
    public string Hypothesis { get; init; } = string.Empty;

    /// <summary>Enfoque.</summary>
    public string Approach { get; init; } = string.Empty;

    /// <summary>Gancho.</summary>
    public string Hook { get; init; } = string.Empty;

    /// <summary>Guion.</summary>
    public string Script { get; init; } = string.Empty;

    /// <summary>Desarrollo.</summary>
    public string Development { get; init; } = string.Empty;

    /// <summary>Llamada a la acción.</summary>
    public string Cta { get; init; } = string.Empty;

    /// <summary>Público previsto.</summary>
    public string Audience { get; init; } = string.Empty;

    /// <summary>Material que requiere.</summary>
    public string RequiredMaterial { get; init; } = string.Empty;

    /// <summary>Por qué se eligió.</summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>Riesgos.</summary>
    public string Risks { get; init; } = string.Empty;

    /// <summary>Novedad, de 1 a 5.</summary>
    public int Novelty { get; init; }

    /// <summary>Material que falta, un renglón por recurso.</summary>
    public string? MissingText { get; init; }

    /// <summary>Mensajes prohibidos de la marca que aparecen en el concepto, un renglón por frase.</summary>
    public string? ViolationsText { get; init; }

    /// <summary>Receta armada a partir del concepto; nulo si todavía no se armó.</summary>
    public long? RecipeId { get; init; }

    /// <summary>Fragmentos existentes que usaría, en orden.</summary>
    public List<ConceptFragmentView> Fragments { get; set; } = [];

    /// <summary>Material que falta.</summary>
    public List<string> Missing => Lines(MissingText);

    /// <summary>Mensajes prohibidos encontrados.</summary>
    public List<string> Violations => Lines(ViolationsText);

    /// <summary>Si se puede armar su receta: tiene fragmentos, todos siguen existiendo y no usa un mensaje prohibido.</summary>
    public bool Realizable => Fragments.Count > 0 && Fragments.All(f => f.SegmentId is not null) && Violations.Count == 0;

    private static List<string> Lines(string? text)
    {
        return string.IsNullOrEmpty(text) ? [] : text.Split('\n').ToList();
    }
}

/// <summary>Un plan con sus conceptos.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="BriefId">Brief del que sale.</param>
/// <param name="BriefName">Nombre del brief.</param>
/// <param name="Status">Estado.</param>
/// <param name="Error">Por qué falló.</param>
/// <param name="RequestedCount">Conceptos pedidos.</param>
/// <param name="Model">Modelo que lo generó.</param>
/// <param name="PromptVersion">Versión de la consigna.</param>
/// <param name="CostUsd">Lo que costó.</param>
/// <param name="CreatedAt">Pedido, en UTC.</param>
/// <param name="Concepts">Conceptos en orden.</param>
public sealed record PlanDetail(
    long Id, long BriefId, string BriefName, MediaProcessingStatus Status, string? Error, int RequestedCount, string? Model,
    string? PromptVersion, decimal CostUsd, DateTime CreatedAt, List<ConceptView> Concepts);

/// <summary>Parámetros del trabajo que genera un plan.</summary>
/// <param name="PlanId">Plan.</param>
public sealed record PlanPayload(long PlanId);

/// <summary>Proyección intermedia: un fragmento disponible para un plan, con lo que se sabe de él.</summary>
public sealed class PlanSource
{
    /// <summary>Fragmento.</summary>
    public long SegmentId { get; init; }

    /// <summary>Video original.</summary>
    public long AssetId { get; init; }

    /// <summary>Nombre del video.</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>Inicio del tramo.</summary>
    public int StartMs { get; init; }

    /// <summary>Fin del tramo.</summary>
    public int EndMs { get; init; }

    /// <summary>Qué se ve, según su análisis.</summary>
    public string? Description { get; init; }

    /// <summary>Mensaje principal, según su análisis.</summary>
    public string? Message { get; init; }

    /// <summary>Funciones narrativas de su análisis.</summary>
    public List<CreativeFunction> Functions { get; init; } = [];
}

/// <summary>Proyección intermedia: un fragmento de un concepto con el concepto al que pertenece.</summary>
public sealed class ConceptFragmentRow
{
    /// <summary>Concepto.</summary>
    public long ConceptId { get; init; }

    /// <summary>El fragmento.</summary>
    public ConceptFragmentView Fragment { get; init; } = null!;
}
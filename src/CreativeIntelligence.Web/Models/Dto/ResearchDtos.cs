using System.ComponentModel.DataAnnotations;

namespace CreativeIntelligence.Web.Models.Dto;

/// <summary>Datos para autorizar una fuente externa.</summary>
public sealed class ResearchSourceBody
{
    /// <summary>Nombre.</summary>
    [Required(ErrorMessage = "El nombre de la fuente es obligatorio.")]
    [StringLength(150, ErrorMessage = "El nombre admite hasta 150 caracteres.")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Dominio o dirección del sitio.</summary>
    [Required(ErrorMessage = "El dominio es obligatorio.")]
    [StringLength(200, ErrorMessage = "El dominio admite hasta 200 caracteres.")]
    public string Domain { get; set; } = string.Empty;

    /// <summary>Condiciones de uso.</summary>
    [StringLength(1000, ErrorMessage = "Las condiciones de uso admiten hasta 1000 caracteres.")]
    public string? ConditionsOfUse { get; set; }

    /// <summary>Permiso que reciben por defecto sus referencias.</summary>
    public ReferenceUsage DefaultUsage { get; set; }
}

/// <summary>Una fuente autorizada en el listado.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Name">Nombre.</param>
/// <param name="Domain">Dominio.</param>
/// <param name="ConditionsOfUse">Condiciones de uso.</param>
/// <param name="DefaultUsage">Permiso por defecto de sus referencias.</param>
/// <param name="IsActive">Si se la puede consultar.</param>
/// <param name="References">Referencias guardadas de esta fuente.</param>
public sealed record ResearchSourceItem(
    int Id, string Name, string Domain, string? ConditionsOfUse, ReferenceUsage DefaultUsage, bool IsActive, int References);

/// <summary>Datos para cargar una referencia a mano.</summary>
public sealed class ReferenceBody
{
    /// <summary>Dirección; debe pertenecer al dominio de una fuente autorizada y activa.</summary>
    [Required(ErrorMessage = "La dirección es obligatoria.")]
    [StringLength(450, ErrorMessage = "La dirección admite hasta 450 caracteres.")]
    public string Url { get; set; } = string.Empty;

    /// <summary>Título.</summary>
    [Required(ErrorMessage = "El título es obligatorio.")]
    [StringLength(300, ErrorMessage = "El título admite hasta 300 caracteres.")]
    public string Title { get; set; } = string.Empty;

    /// <summary>Qué aporta.</summary>
    public ReferenceKind Kind { get; set; }

    /// <summary>Resumen en palabras propias.</summary>
    [StringLength(2000, ErrorMessage = "El resumen admite hasta 2000 caracteres.")]
    public string? Summary { get; set; }

    /// <summary>Brief al que se asocia; opcional.</summary>
    public long? BriefId { get; set; }
}

/// <summary>Una referencia externa con su procedencia y su permiso.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Kind">Qué aporta.</param>
/// <param name="Url">Dirección.</param>
/// <param name="Title">Título.</param>
/// <param name="Summary">Resumen.</param>
/// <param name="Usage">Cómo se puede usar.</param>
/// <param name="Origin">De dónde salió.</param>
/// <param name="SourceName">Fuente.</param>
/// <param name="SourceAllowsUse">Si la fuente permite marcar sus referencias como utilizables.</param>
/// <param name="BriefId">Brief al que está asociada.</param>
/// <param name="BriefName">Nombre del brief.</param>
/// <param name="CreatedAt">Alta, en UTC.</param>
public sealed record ReferenceItem(
    long Id, ReferenceKind Kind, string Url, string Title, string? Summary, ReferenceUsage Usage, ReferenceOrigin Origin, string SourceName,
    bool SourceAllowsUse, long? BriefId, string? BriefName, DateTime CreatedAt);

/// <summary>Lo que muestra la pantalla de investigación.</summary>
/// <param name="Sources">Fuentes autorizadas.</param>
/// <param name="References">Referencias guardadas, de la más nueva a la más vieja.</param>
public sealed record ResearchOverview(List<ResearchSourceItem> Sources, List<ReferenceItem> References);

/// <summary>Lo que se le pide al agente investigador.</summary>
/// <param name="Brand">Perfil de la marca.</param>
/// <param name="Brief">Brief.</param>
/// <param name="Domains">Dominios de las fuentes autorizadas.</param>
/// <param name="MaxReferences">Cantidad máxima de referencias.</param>
public sealed record ResearchRequest(BrandDetail Brand, BriefDetail Brief, List<string> Domains, int MaxReferences);

/// <summary>Una referencia tal como la propone el agente.</summary>
/// <param name="Url">Dirección.</param>
/// <param name="Title">Título.</param>
/// <param name="Kind">Qué aporta.</param>
/// <param name="Summary">Qué enseña, en palabras propias.</param>
public sealed record ReferenceProposal(string Url, string Title, ReferenceKind Kind, string Summary);

/// <summary>Lo que devuelve el agente investigador.</summary>
/// <param name="References">Referencias propuestas.</param>
/// <param name="SearchQueries">Consultas de búsqueda que hizo.</param>
/// <param name="Provider">Proveedor.</param>
/// <param name="Model">Modelo.</param>
/// <param name="PromptVersion">Versión de la consigna.</param>
/// <param name="InputTokens">Tokens de entrada.</param>
/// <param name="OutputTokens">Tokens de salida.</param>
/// <param name="CostUsd">Costo en dólares, búsquedas incluidas.</param>
/// <param name="DurationMs">Cuánto tardó.</param>
public sealed record ResearchOutcome(
    List<ReferenceProposal> References, int SearchQueries, string Provider, string Model, string PromptVersion, int InputTokens,
    int OutputTokens, decimal CostUsd, int DurationMs);

/// <summary>Estimación de una corrida de investigación antes de pedirla.</summary>
/// <param name="Configured">Falso si falta la clave del proveedor.</param>
/// <param name="Model">Modelo que se usaría.</param>
/// <param name="Sources">Fuentes autorizadas y activas que se le indicarían.</param>
/// <param name="MaxReferences">Referencias que se pedirían como máximo.</param>
/// <param name="CostUsd">Costo estimado, búsquedas incluidas.</param>
/// <param name="MaxCostUsd">Tope: el menor entre el tope por corrida y el presupuesto de generación del brief.</param>
public sealed record ResearchEstimate(bool Configured, string Model, int Sources, int MaxReferences, decimal CostUsd, decimal MaxCostUsd);

/// <summary>Una corrida de investigación de un brief.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Status">Estado.</param>
/// <param name="Error">Por qué falló.</param>
/// <param name="SearchQueries">Consultas de búsqueda hechas.</param>
/// <param name="Found">Referencias propuestas.</param>
/// <param name="Kept">Referencias guardadas.</param>
/// <param name="CostUsd">Lo que costó.</param>
/// <param name="CreatedAt">Pedido, en UTC.</param>
public sealed record ResearchRunItem(
    long Id, MediaProcessingStatus Status, string? Error, int SearchQueries, int Found, int Kept, decimal CostUsd, DateTime CreatedAt);

/// <summary>Lo que muestra la tarjeta de investigación de un brief.</summary>
/// <param name="Runs">Corridas, de la más nueva a la más vieja.</param>
/// <param name="References">Referencias asociadas al brief.</param>
public sealed record BriefResearch(List<ResearchRunItem> Runs, List<ReferenceItem> References);

/// <summary>Una referencia tal como se le describe al planificador.</summary>
/// <param name="Kind">Qué aporta.</param>
/// <param name="Title">Título.</param>
/// <param name="Summary">Resumen.</param>
/// <param name="Usage">Cómo se puede usar.</param>
public sealed record PlanReference(ReferenceKind Kind, string Title, string? Summary, ReferenceUsage Usage);

/// <summary>Parámetros del trabajo de investigación.</summary>
/// <param name="RunId">Corrida.</param>
public sealed record ResearchPayload(long RunId);

/// <summary>Cambio del permiso de una referencia.</summary>
public sealed class ReferenceUsageBody
{
    /// <summary>Permiso nuevo.</summary>
    public ReferenceUsage Usage { get; set; }
}

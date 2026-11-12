using System.ComponentModel.DataAnnotations;

namespace CreativeIntelligence.Web.Models.Dto;

/// <summary>Una fila de métricas leída de un archivo: un anuncio en un día.</summary>
public sealed class MetaCsvRow
{
    /// <summary>Identificador de la cuenta; nulo si el archivo no lo trae.</summary>
    public string? AccountId { get; init; }

    /// <summary>Nombre de la cuenta.</summary>
    public string? AccountName { get; init; }

    /// <summary>Identificador de la campaña; nulo si el archivo no lo trae.</summary>
    public string? CampaignId { get; init; }

    /// <summary>Nombre de la campaña.</summary>
    public string? CampaignName { get; init; }

    /// <summary>Identificador del conjunto; nulo si el archivo no lo trae.</summary>
    public string? AdSetId { get; init; }

    /// <summary>Nombre del conjunto.</summary>
    public string? AdSetName { get; init; }

    /// <summary>Identificador del anuncio.</summary>
    public string AdId { get; init; } = string.Empty;

    /// <summary>Nombre del anuncio.</summary>
    public string AdName { get; init; } = string.Empty;

    /// <summary>Día.</summary>
    public DateOnly Date { get; init; }

    /// <summary>Gasto.</summary>
    public decimal Spend { get; init; }

    /// <summary>Impresiones.</summary>
    public long Impressions { get; init; }

    /// <summary>Clics en el enlace.</summary>
    public long Clicks { get; init; }

    /// <summary>Reproducciones de 3 segundos.</summary>
    public long? VideoPlays { get; init; }

    /// <summary>ThruPlays.</summary>
    public long? ThruPlays { get; init; }

    /// <summary>Compras.</summary>
    public decimal? Purchases { get; init; }

    /// <summary>Valor de las compras.</summary>
    public decimal? PurchaseValue { get; init; }
}

/// <summary>Resultado de leer un archivo de métricas.</summary>
/// <param name="Rows">Filas válidas.</param>
/// <param name="MissingColumns">Columnas obligatorias que el archivo no tiene; si hay alguna, no se leyó ninguna fila.</param>
/// <param name="Problems">Por qué se descartaron filas (las primeras diez).</param>
/// <param name="Skipped">Filas descartadas.</param>
/// <param name="Currency">Moneda que dice el encabezado del gasto; nulo si no la dice.</param>
public sealed record MetaCsvResult(List<MetaCsvRow> Rows, List<string> MissingColumns, List<string> Problems, int Skipped, string? Currency);

/// <summary>
/// Métricas de un anuncio (o de un grupo) en un período. Las cantidades se suman; las tasas se calculan acá, sobre
/// las sumas, y son nulas cuando no hay con qué dividir. Son <b>observaciones</b>: lo que pasó, no por qué pasó.
/// </summary>
/// <param name="Spend">Gasto.</param>
/// <param name="Impressions">Impresiones.</param>
/// <param name="Clicks">Clics en el enlace.</param>
/// <param name="VideoPlays">Reproducciones de 3 segundos; nulo si no hay dato.</param>
/// <param name="ThruPlays">ThruPlays; nulo si no hay dato.</param>
/// <param name="Purchases">Compras; nulo si no hay dato.</param>
/// <param name="PurchaseValue">Valor de las compras; nulo si no hay dato.</param>
/// <param name="Days">Días con datos.</param>
public sealed record AdMetrics(
    decimal Spend, long Impressions, long Clicks, long? VideoPlays, long? ThruPlays, decimal? Purchases, decimal? PurchaseValue, int Days)
{
    /// <summary>Clics sobre impresiones, de 0 a 1.</summary>
    public decimal? Ctr => Impressions == 0 ? null : (decimal)Clicks / Impressions;

    /// <summary>Gasto por clic.</summary>
    public decimal? Cpc => Clicks == 0 ? null : Spend / Clicks;

    /// <summary>Gasto por mil impresiones.</summary>
    public decimal? Cpm => Impressions == 0 ? null : Spend * 1000 / Impressions;

    /// <summary>Gasto por compra.</summary>
    public decimal? Cpa => Purchases is null or 0 ? null : Spend / Purchases.Value;

    /// <summary>Valor de las compras sobre el gasto.</summary>
    public decimal? Roas => PurchaseValue is null || Spend == 0 ? null : PurchaseValue.Value / Spend;

    /// <summary>Reproducciones de 3 segundos sobre impresiones, de 0 a 1: cuánta gente se queda al empezar.</summary>
    public decimal? HookRate => VideoPlays is null || Impressions == 0 ? null : (decimal)VideoPlays.Value / Impressions;

    /// <summary>ThruPlays sobre reproducciones de 3 segundos, de 0 a 1: cuánta gente sigue mirando.</summary>
    public decimal? HoldRate => ThruPlays is null || VideoPlays is null or 0 ? null : (decimal)ThruPlays.Value / VideoPlays.Value;
}

/// <summary>Período a consultar.</summary>
public sealed class MetaFilter
{
    /// <summary>Desde este día, inclusive; nulo para no limitar.</summary>
    public DateOnly? From { get; set; }

    /// <summary>Hasta este día, inclusive; nulo para no limitar.</summary>
    public DateOnly? To { get; set; }
}

/// <summary>Un anuncio en el listado, con sus métricas del período.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="ExternalId">Identificador en Meta.</param>
/// <param name="Name">Nombre.</param>
/// <param name="CampaignName">Campaña.</param>
/// <param name="AdSetName">Conjunto.</param>
/// <param name="Currency">Moneda de la cuenta.</param>
/// <param name="VariantId">Video generado vinculado; nulo si no está vinculado.</param>
/// <param name="Metrics">Métricas del período.</param>
public sealed record AdListItem(
    long Id, string ExternalId, string Name, string CampaignName, string AdSetName, string Currency, long? VariantId, AdMetrics Metrics);

/// <summary>Una importación en el listado.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="FileName">Archivo.</param>
/// <param name="Status">Estado.</param>
/// <param name="Notes">Por qué falló o qué se descartó.</param>
/// <param name="Rows">Filas guardadas.</param>
/// <param name="Skipped">Filas descartadas.</param>
/// <param name="Ads">Anuncios distintos.</param>
/// <param name="FirstDate">Primer día.</param>
/// <param name="LastDate">Último día.</param>
/// <param name="CreatedAt">Carga, en UTC.</param>
public sealed record MetaImportItem(
    long Id, string FileName, MediaProcessingStatus Status, string? Notes, int Rows, int Skipped, int Ads, DateOnly? FirstDate,
    DateOnly? LastDate, DateTime CreatedAt);

/// <summary>Lo que muestra la pantalla de rendimiento.</summary>
/// <param name="Filter">Período consultado.</param>
/// <param name="Imports">Últimas importaciones.</param>
/// <param name="Ads">Anuncios con sus métricas del período, del que más gastó al que menos.</param>
public sealed record MetaOverview(MetaFilter Filter, List<MetaImportItem> Imports, List<AdListItem> Ads);

/// <summary>Las métricas de un anuncio en un día.</summary>
/// <param name="Date">Día.</param>
/// <param name="Metrics">Métricas.</param>
public sealed record AdDay(DateOnly Date, AdMetrics Metrics);

/// <summary>Un video generado que se puede vincular a un anuncio.</summary>
/// <param name="VariantId">Video.</param>
/// <param name="RecipeName">Receta de la que sale.</param>
/// <param name="CreatedAt">Cuándo se generó, en UTC.</param>
public sealed record VariantOption(long VariantId, string RecipeName, DateTime CreatedAt);

/// <summary>
/// Linaje de un anuncio: cómo se generó la creatividad que se publicó en él. Va del video a su receta, a los
/// tramos de los originales, y hacia atrás al concepto, al plan, al brief, a la marca y al lote, cuando existen.
/// </summary>
public sealed class AdLineage
{
    /// <summary>Video generado.</summary>
    public long VariantId { get; init; }

    /// <summary>Receta.</summary>
    public long RecipeId { get; init; }

    /// <summary>Nombre de la receta.</summary>
    public string RecipeName { get; init; } = string.Empty;

    /// <summary>Concepto comercial o gancho de la receta.</summary>
    public string? Hook { get; init; }

    /// <summary>Hipótesis de la receta.</summary>
    public string? Hypothesis { get; init; }

    /// <summary>Lote que generó la receta; nulo si no salió de un lote.</summary>
    public long? BatchId { get; init; }

    /// <summary>Nombre del lote.</summary>
    public string? BatchName { get; init; }

    /// <summary>Concepto del que salió la receta; nulo si se armó a mano.</summary>
    public long? ConceptId { get; init; }

    /// <summary>Título del concepto.</summary>
    public string? ConceptTitle { get; init; }

    /// <summary>Plan del concepto.</summary>
    public long? PlanId { get; init; }

    /// <summary>Brief del plan.</summary>
    public long? BriefId { get; init; }

    /// <summary>Nombre del brief.</summary>
    public string? BriefName { get; init; }

    /// <summary>Marca del brief.</summary>
    public string? BrandName { get; init; }

    /// <summary>Tramos del montaje, con el original de cada uno.</summary>
    public List<RecipeItemView> Items { get; set; } = [];
}

/// <summary>Un anuncio con sus métricas por día, su vínculo y su linaje.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="ExternalId">Identificador en Meta.</param>
/// <param name="Name">Nombre.</param>
/// <param name="AccountName">Cuenta.</param>
/// <param name="CampaignName">Campaña.</param>
/// <param name="AdSetName">Conjunto.</param>
/// <param name="Currency">Moneda de la cuenta.</param>
/// <param name="LinkOrigin">Cómo se vinculó; nulo si no está vinculado.</param>
/// <param name="Totals">Métricas de todo el período con datos.</param>
/// <param name="Days">Métricas por día, en orden.</param>
/// <param name="Lineage">Linaje; nulo si el anuncio no está vinculado a un video generado.</param>
/// <param name="Candidates">Videos generados que se le pueden vincular.</param>
public sealed record AdDetail(
    long Id, string ExternalId, string Name, string AccountName, string CampaignName, string AdSetName, string Currency,
    AdLinkOrigin? LinkOrigin, AdMetrics Totals, List<AdDay> Days, AdLineage? Lineage, List<VariantOption> Candidates)
{
    /// <summary>Experimentos en los que participa el anuncio.</summary>
    public List<AdExperiment> Experiments { get; init; } = [];
}

/// <summary>Formulario de carga de un archivo de métricas.</summary>
public sealed class UploadMetaCsvForm
{
    /// <summary>Archivo CSV exportado de Ads Manager.</summary>
    [Required(ErrorMessage = "Elegí un archivo CSV.")]
    public IFormFile? File { get; set; }

    /// <summary>Moneda que se asume si el archivo no la dice.</summary>
    [RegularExpression("^[A-Za-z]{3}$", ErrorMessage = "La moneda es un código de tres letras, por ejemplo USD.")]
    public string Currency { get; set; } = "USD";
}

/// <summary>Vínculo de un anuncio con un video generado.</summary>
public sealed class LinkAdBody
{
    /// <summary>Video generado; nulo para quitar el vínculo.</summary>
    public long? VariantId { get; set; }
}

/// <summary>Parámetros del trabajo de importación.</summary>
/// <param name="ImportId">Importación.</param>
public sealed record MetaImportPayload(long ImportId);

/// <summary>Proyección intermedia: las sumas de un anuncio en un período.</summary>
public sealed class AdTotalsRow
{
    /// <summary>Anuncio.</summary>
    public long AdId { get; init; }

    /// <summary>Gasto.</summary>
    public decimal Spend { get; init; }

    /// <summary>Impresiones.</summary>
    public long Impressions { get; init; }

    /// <summary>Clics.</summary>
    public long Clicks { get; init; }

    /// <summary>Reproducciones de 3 segundos.</summary>
    public long? VideoPlays { get; init; }

    /// <summary>ThruPlays.</summary>
    public long? ThruPlays { get; init; }

    /// <summary>Compras.</summary>
    public decimal? Purchases { get; init; }

    /// <summary>Valor de las compras.</summary>
    public decimal? PurchaseValue { get; init; }

    /// <summary>Días con datos.</summary>
    public int Days { get; init; }
}

/// <summary>Proyección intermedia: un anuncio con los nombres de su campaña, su conjunto y su cuenta.</summary>
public sealed class AdRow
{
    /// <summary>Identificador.</summary>
    public long Id { get; init; }

    /// <summary>Identificador en Meta.</summary>
    public string ExternalId { get; init; } = string.Empty;

    /// <summary>Nombre.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Campaña.</summary>
    public string CampaignName { get; init; } = string.Empty;

    /// <summary>Conjunto.</summary>
    public string AdSetName { get; init; } = string.Empty;

    /// <summary>Cuenta.</summary>
    public string AccountName { get; init; } = string.Empty;

    /// <summary>Moneda de la cuenta.</summary>
    public string Currency { get; init; } = string.Empty;

    /// <summary>Video vinculado.</summary>
    public long? VariantId { get; init; }

    /// <summary>Cómo se vinculó.</summary>
    public AdLinkOrigin? LinkOrigin { get; init; }
}

/// <summary>Cuánta evidencia hay detrás de una lectura del rendimiento.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<EvidenceLevel>))]
public enum EvidenceLevel
{
    /// <summary>Muy pocos anuncios o muy pocas impresiones: no se puede decir nada.</summary>
    Insufficient,

    /// <summary>Alcanza para una primera lectura, que puede cambiar con más datos.</summary>
    Preliminary,

    /// <summary>Varios anuncios, de más de una campaña y con volumen: la lectura es más estable. Sigue siendo una
    /// correlación.</summary>
    Moderate,
}

/// <summary>Qué tan distinguible del azar es una diferencia entre dos tasas.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<SignalStrength>))]
public enum SignalStrength
{
    /// <summary>No se calculó: faltan datos o no hay con qué comparar.</summary>
    None,

    /// <summary>La diferencia es compatible con el azar.</summary>
    NotDistinguishable,

    /// <summary>La diferencia difícilmente sea azar (aproximadamente 95 %).</summary>
    Probable,

    /// <summary>La diferencia muy difícilmente sea azar (aproximadamente 99 %).</summary>
    Clear,
}

/// <summary>Período y campaña a analizar.</summary>
public sealed class PerformanceFilter
{
    /// <summary>Desde este día, inclusive.</summary>
    public DateOnly? From { get; set; }

    /// <summary>Hasta este día, inclusive.</summary>
    public DateOnly? To { get; set; }

    /// <summary>Sólo los anuncios de esta campaña; nulo para todas. Comparar dentro de una misma campaña quita de
    /// en medio su objetivo, su presupuesto y su audiencia.</summary>
    public long? CampaignId { get; set; }
}

/// <summary>Un tramo de un original usado en una creatividad.</summary>
/// <param name="AssetId">Video original.</param>
/// <param name="FileName">Nombre del video.</param>
/// <param name="StartMs">Inicio del tramo.</param>
/// <param name="EndMs">Fin del tramo.</param>
/// <param name="SegmentId">Fragmento de la biblioteca; nulo si ya no existe.</param>
/// <param name="Role">Función que cumple en el anuncio.</param>
public sealed record ComponentRef(long AssetId, string FileName, int StartMs, int EndMs, long? SegmentId, CreativeFunction? Role);

/// <summary>Un anuncio vinculado, con sus métricas del período y los componentes de su creatividad.</summary>
public sealed class AdComponents
{
    /// <summary>Anuncio.</summary>
    public long AdId { get; init; }

    /// <summary>Nombre del anuncio.</summary>
    public string AdName { get; init; } = string.Empty;

    /// <summary>Campaña.</summary>
    public long CampaignId { get; init; }

    /// <summary>Métricas del período.</summary>
    public AdMetrics Metrics { get; init; } = new(0, 0, 0, null, null, null, null, 0);

    /// <summary>Tramos del montaje, en orden.</summary>
    public List<ComponentRef> Items { get; init; } = [];

    /// <summary>Si lleva subtítulos.</summary>
    public bool Subtitles { get; init; }

    /// <summary>Transición entre tramos.</summary>
    public RecipeTransition Transition { get; init; }

    /// <summary>Si lleva música.</summary>
    public bool Music { get; init; }
}

/// <summary>Un valor de una dimensión por el que se agrupan los anuncios.</summary>
/// <param name="Key">Clave estable del valor.</param>
/// <param name="Label">Texto para mostrar.</param>
/// <param name="Fragment">El tramo, si el valor es un fragmento.</param>
public sealed record ComponentKey(string Key, string Label, ComponentRef? Fragment = null);

/// <summary>
/// Los anuncios que comparten un componente, comparados con el resto. Es una <b>correlación</b>: dice que esos
/// anuncios rindieron así, no que el componente sea la causa.
/// </summary>
/// <param name="Key">Clave del componente.</param>
/// <param name="Label">Texto para mostrar.</param>
/// <param name="Fragment">El tramo, si el componente es un fragmento.</param>
/// <param name="Ads">Anuncios que lo usan.</param>
/// <param name="Campaigns">Campañas distintas de esos anuncios.</param>
/// <param name="Metrics">Métricas sumadas de esos anuncios.</param>
/// <param name="Rest">Métricas sumadas de los demás anuncios analizados.</param>
/// <param name="Evidence">Cuánta evidencia hay.</param>
/// <param name="CtrSignal">Qué tan distinguible del azar es la diferencia de CTR con el resto.</param>
/// <param name="CtrHigher">Si el CTR del grupo es mayor que el del resto.</param>
/// <param name="Reading">Lectura en palabras, con sus limitaciones.</param>
/// <param name="AdIds">Los anuncios del grupo.</param>
public sealed record ComponentGroup(
    string Key, string Label, ComponentRef? Fragment, int Ads, int Campaigns, AdMetrics Metrics, AdMetrics Rest, EvidenceLevel Evidence,
    SignalStrength CtrSignal, bool CtrHigher, string Reading, List<long> AdIds);

/// <summary>Una forma de agrupar los anuncios, con sus grupos.</summary>
/// <param name="Name">Clave de la dimensión.</param>
/// <param name="Title">Título.</param>
/// <param name="Explanation">Qué agrupa.</param>
/// <param name="Groups">Grupos, del que más impresiones tiene al que menos.</param>
public sealed record ComponentDimension(string Name, string Title, string Explanation, List<ComponentGroup> Groups);

/// <summary>Las impresiones y los clics de un anuncio en un día.</summary>
/// <param name="Date">Día.</param>
/// <param name="Impressions">Impresiones.</param>
/// <param name="Clicks">Clics.</param>
public sealed record DayPoint(DateOnly Date, long Impressions, long Clicks);

/// <summary>Una caída del CTR de un anuncio entre la primera y la segunda mitad de sus días.</summary>
/// <param name="FirstCtr">CTR de la primera mitad, de 0 a 1.</param>
/// <param name="LastCtr">CTR de la segunda mitad, de 0 a 1.</param>
/// <param name="Drop">Caída relativa, de 0 a 1.</param>
/// <param name="Days">Días con datos.</param>
public sealed record FatigueSignal(decimal FirstCtr, decimal LastCtr, decimal Drop, int Days);

/// <summary>Un anuncio con señal de posible fatiga.</summary>
/// <param name="AdId">Anuncio.</param>
/// <param name="Name">Nombre.</param>
/// <param name="CampaignName">Campaña.</param>
/// <param name="Signal">La caída observada.</param>
public sealed record FatigueItem(long AdId, string Name, string CampaignName, FatigueSignal Signal);

/// <summary>Una campaña para elegir en el filtro.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Name">Nombre.</param>
public sealed record CampaignOption(long Id, string Name);

/// <summary>El análisis por componente de un período.</summary>
/// <param name="Filter">Período y campaña analizados.</param>
/// <param name="Campaigns">Campañas disponibles para filtrar.</param>
/// <param name="AdsWithData">Anuncios con métricas en el período.</param>
/// <param name="LinkedAds">De esos, los vinculados a una creatividad: los únicos que el análisis puede leer.</param>
/// <param name="Dimensions">Agrupaciones.</param>
/// <param name="Fatigue">Anuncios con señal de posible fatiga.</param>
public sealed record PerformanceAnalysis(
    PerformanceFilter Filter, List<CampaignOption> Campaigns, int AdsWithData, int LinkedAds, List<ComponentDimension> Dimensions,
    List<FatigueItem> Fatigue);

/// <summary>Proyección intermedia: un anuncio vinculado con su campaña y la receta de su video.</summary>
public sealed class LinkedAdRow
{
    /// <summary>Anuncio.</summary>
    public long AdId { get; init; }

    /// <summary>Nombre.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Campaña.</summary>
    public long CampaignId { get; init; }

    /// <summary>Nombre de la campaña.</summary>
    public string CampaignName { get; init; } = string.Empty;

    /// <summary>Receta del video vinculado; nulo si no está vinculado.</summary>
    public long? RecipeId { get; init; }
}

/// <summary>Proyección intermedia: la terminación de una receta.</summary>
public sealed class RecipeFinishRow
{
    /// <summary>Receta.</summary>
    public long RecipeId { get; init; }

    /// <summary>Si lleva subtítulos.</summary>
    public bool Subtitles { get; init; }

    /// <summary>Transición.</summary>
    public RecipeTransition Transition { get; init; }

    /// <summary>Si lleva música.</summary>
    public bool Music { get; init; }
}

/// <summary>Proyección intermedia: un tramo de una receta.</summary>
public sealed class RecipeComponentRow
{
    /// <summary>Receta.</summary>
    public long RecipeId { get; init; }

    /// <summary>El tramo.</summary>
    public ComponentRef Component { get; init; } = null!;
}

/// <summary>Proyección intermedia: un día de un anuncio.</summary>
public sealed class AdDayRow
{
    /// <summary>Anuncio.</summary>
    public long AdId { get; init; }

    /// <summary>El día.</summary>
    public DayPoint Point { get; init; } = null!;
}
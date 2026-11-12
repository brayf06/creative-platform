using System.ComponentModel.DataAnnotations;

namespace CreativeIntelligence.Web.Models.Dto;

/// <summary>Un formato de salida disponible para las creatividades.</summary>
/// <param name="Name">Relación de aspecto; es también su clave.</param>
/// <param name="Width">Ancho en píxeles.</param>
/// <param name="Height">Alto en píxeles.</param>
/// <param name="Label">Descripción para mostrar.</param>
public sealed record OutputFormat(string Name, int Width, int Height, string Label);

/// <summary>Un texto para mostrar sobre el video.</summary>
public sealed class OverlayBody
{
    /// <summary>Texto.</summary>
    [StringLength(120, ErrorMessage = "Un texto sobre el video admite hasta 120 caracteres.")]
    public string? Text { get; set; }

    /// <summary>Dónde va.</summary>
    public OverlayPlacement Placement { get; set; }

    /// <summary>Cuándo se muestra.</summary>
    public OverlayMoment Moment { get; set; }
}

/// <summary>Terminación de un video: subtítulos, transición, música y textos. La comparten la receta y el lote.</summary>
public abstract class FinishingBody
{
    /// <summary>Si se queman subtítulos con lo que se dice en cada tramo.</summary>
    public bool Subtitles { get; set; }

    /// <summary>Cómo se pasa de un tramo al siguiente.</summary>
    public RecipeTransition Transition { get; set; }

    /// <summary>Música de fondo; nulo si no lleva.</summary>
    public long? MusicTrackId { get; set; }

    /// <summary>Volumen de la música, de 5 a 100.</summary>
    [Range(5, 100, ErrorMessage = "El volumen de la música va de 5 a 100.")]
    public int MusicVolumePercent { get; set; } = 25;

    /// <summary>Textos sobre el video; los vacíos se ignoran.</summary>
    [MaxLength(CompositionRules.MaxOverlays, ErrorMessage = "Se admiten hasta 5 textos sobre el video.")]
    public List<OverlayBody> Overlays { get; set; } = [];
}

/// <summary>Datos para crear una receta.</summary>
public sealed class CreateRecipeBody : FinishingBody
{
    /// <summary>Nombre de la receta.</summary>
    [Required(ErrorMessage = "El nombre es obligatorio.")]
    [StringLength(200, ErrorMessage = "El nombre admite hasta 200 caracteres.")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Concepto comercial.</summary>
    [StringLength(1000, ErrorMessage = "El concepto admite hasta 1000 caracteres.")]
    public string? Concept { get; set; }

    /// <summary>Hipótesis a probar.</summary>
    [StringLength(1000, ErrorMessage = "La hipótesis admite hasta 1000 caracteres.")]
    public string? Hypothesis { get; set; }

    /// <summary>Formato de salida: el nombre de uno de <c>CompositionRules.Formats</c>.</summary>
    [Required(ErrorMessage = "El formato es obligatorio.")]
    public string Format { get; set; } = "9:16";

    /// <summary>Fragmentos en el orden del montaje.</summary>
    [MinLength(1, ErrorMessage = "La receta necesita al menos un fragmento.")]
    [MaxLength(CompositionRules.MaxItems, ErrorMessage = "La receta admite hasta 30 fragmentos.")]
    public List<RecipeItemBody> Items { get; set; } = [];
}

/// <summary>Un fragmento elegido para una receta.</summary>
public sealed class RecipeItemBody
{
    /// <summary>Fragmento de la biblioteca.</summary>
    public long SegmentId { get; set; }

    /// <summary>Función que cumple en el anuncio; opcional.</summary>
    public CreativeFunction? Role { get; set; }

    /// <summary>Desde dónde se usa, en milisegundos del original; nulo para usarlo desde su inicio. Debe caer
    /// dentro del fragmento.</summary>
    public int? StartMs { get; set; }

    /// <summary>Hasta dónde se usa, en milisegundos del original; nulo para usarlo hasta su fin. Debe caer dentro
    /// del fragmento.</summary>
    public int? EndMs { get; set; }
}

/// <summary>Un fragmento que se puede usar en una receta: elegido para producción y con autorización vigente.</summary>
/// <param name="SegmentId">Fragmento.</param>
/// <param name="AssetId">Video al que pertenece.</param>
/// <param name="FileName">Nombre del video.</param>
/// <param name="StartMs">Inicio en milisegundos.</param>
/// <param name="EndMs">Fin en milisegundos.</param>
/// <param name="HasThumbnail">Si tiene miniatura.</param>
/// <param name="Functions">Funciones narrativas de su análisis vigente; vacío si no está analizado.</param>
public sealed record AvailableSegment(
    long SegmentId, long AssetId, string FileName, int StartMs, int EndMs, bool HasThumbnail, List<CreativeFunction> Functions);

/// <summary>Una receta en el listado.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Name">Nombre.</param>
/// <param name="Width">Ancho de salida.</param>
/// <param name="Height">Alto de salida.</param>
/// <param name="Items">Cantidad de tramos.</param>
/// <param name="DurationMs">Duración del montaje, en milisegundos.</param>
/// <param name="Variants">Videos generados.</param>
/// <param name="ReadyVariants">Videos generados que terminaron bien.</param>
/// <param name="CreatedAt">Alta, en UTC.</param>
public sealed record RecipeListItem(
    long Id, string Name, int Width, int Height, int Items, int DurationMs, int Variants, int ReadyVariants, DateTime CreatedAt);

/// <summary>Un tramo de una receta, con de dónde sale.</summary>
/// <param name="Position">Lugar en el montaje, desde 1.</param>
/// <param name="Role">Función que cumple; nulo si no se indicó.</param>
/// <param name="AssetId">Video original.</param>
/// <param name="FileName">Nombre del video original.</param>
/// <param name="StartMs">Inicio del tramo en el original.</param>
/// <param name="EndMs">Fin del tramo en el original.</param>
/// <param name="SegmentId">Fragmento de la biblioteca del que se tomó; nulo si ya no existe.</param>
/// <param name="ReuseAuthorized">Si el original tiene autorización vigente a la fecha de la base.</param>
public sealed record RecipeItemView(
    int Position, CreativeFunction? Role, long AssetId, string FileName, int StartMs, int EndMs, long? SegmentId, bool ReuseAuthorized);

/// <summary>Resultado de un control de calidad.</summary>
/// <param name="Rule">Regla.</param>
/// <param name="Passed">Si la cumple.</param>
/// <param name="Detail">Qué se esperaba y qué se encontró.</param>
public sealed record QualityResult(string Rule, bool Passed, string Detail);

/// <summary>Un video generado de una receta.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Status">Estado del render.</param>
/// <param name="Error">Por qué falló.</param>
/// <param name="DurationMs">Duración del archivo.</param>
/// <param name="Width">Ancho del archivo.</param>
/// <param name="Height">Alto del archivo.</param>
/// <param name="SizeBytes">Tamaño del archivo.</param>
/// <param name="CreatedAt">Pedido, en UTC.</param>
/// <param name="Checks">Controles de calidad.</param>
/// <param name="RenderMs">Cuánto tardó el render, en milisegundos.</param>
public sealed record VariantView(
    long Id, MediaProcessingStatus Status, string? Error, int? DurationMs, int? Width, int? Height, long? SizeBytes,
    DateTime CreatedAt, List<QualityResult> Checks, int? RenderMs)
{
    /// <summary>Si el archivo pasó todos los controles; falso mientras no haya terminado.</summary>
    public bool PassedQuality => Status == MediaProcessingStatus.Ready && Checks.Count > 0 && Checks.All(c => c.Passed);
}

/// <summary>Una receta con sus tramos y sus videos generados.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Name">Nombre.</param>
/// <param name="Concept">Concepto comercial.</param>
/// <param name="Hypothesis">Hipótesis.</param>
/// <param name="Width">Ancho de salida.</param>
/// <param name="Height">Alto de salida.</param>
/// <param name="DurationMs">Duración del montaje.</param>
/// <param name="CreatedAt">Alta, en UTC.</param>
/// <param name="Items">Tramos en orden.</param>
/// <param name="Variants">Videos generados, del más nuevo al más viejo.</param>
public sealed record RecipeDetail(
    long Id, string Name, string? Concept, string? Hypothesis, int Width, int Height, int DurationMs, DateTime CreatedAt,
    List<RecipeItemView> Items, List<VariantView> Variants)
{
    /// <summary>Si lleva subtítulos.</summary>
    public bool Subtitles { get; init; }

    /// <summary>Transición entre tramos.</summary>
    public RecipeTransition Transition { get; init; }

    /// <summary>Nombre de la música de fondo; nulo si no lleva.</summary>
    public string? MusicName { get; init; }

    /// <summary>Volumen de la música, de 5 a 100.</summary>
    public int MusicVolumePercent { get; init; }

    /// <summary>Textos sobre el video, en orden.</summary>
    public List<OverlayView> Overlays { get; init; } = [];
}

/// <summary>Un texto sobre el video de una receta.</summary>
/// <param name="Text">Texto.</param>
/// <param name="Placement">Dónde va.</param>
/// <param name="Moment">Cuándo se muestra.</param>
public sealed record OverlayView(string Text, OverlayPlacement Placement, OverlayMoment Moment);

/// <summary>Un texto con sus tiempos dentro del montaje, listo para quemar en el video.</summary>
/// <param name="StartMs">Desde cuándo se muestra.</param>
/// <param name="EndMs">Hasta cuándo se muestra.</param>
/// <param name="Text">Texto.</param>
/// <param name="Placement">Dónde va; nulo si es un subtítulo.</param>
public sealed record TimedText(int StartMs, int EndMs, string Text, OverlayPlacement? Placement);

/// <summary>Un tramo del montaje con las frases transcriptas de su original.</summary>
/// <param name="StartMs">Inicio del tramo en el original.</param>
/// <param name="EndMs">Fin del tramo en el original.</param>
/// <param name="Lines">Frases del original, con tiempos del original.</param>
public sealed record ClipSpeech(int StartMs, int EndMs, List<TranscriptLineItem> Lines);

/// <summary>Una pista de música en el listado.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Name">Nombre.</param>
/// <param name="FileName">Archivo cargado.</param>
/// <param name="DurationMs">Duración; nulo si no se analizó.</param>
/// <param name="Status">Estado del análisis.</param>
/// <param name="Error">Por qué no se pudo leer.</param>
/// <param name="RightsHolder">Titular de los derechos.</param>
/// <param name="LicenseNotes">Licencia y restricciones.</param>
/// <param name="ReuseAuthorized">Si tiene autorización vigente a la fecha de la base.</param>
/// <param name="RightsExpireOn">Vigencia de la autorización.</param>
/// <param name="CreatedAt">Alta, en UTC.</param>
public sealed record MusicTrackItem(
    long Id, string Name, string FileName, int? DurationMs, MediaProcessingStatus Status, string? Error, string? RightsHolder,
    string? LicenseNotes, bool ReuseAuthorized, DateOnly? RightsExpireOn, DateTime CreatedAt);

/// <summary>Formulario de carga de una pista de música: el archivo, su nombre y sus derechos.</summary>
public sealed class UploadMusicForm : MediaRightsBody
{
    /// <summary>Nombre de la pista; si falta se usa el del archivo.</summary>
    [StringLength(200, ErrorMessage = "El nombre admite hasta 200 caracteres.")]
    public string? Name { get; set; }

    /// <summary>Archivo de audio.</summary>
    [Required(ErrorMessage = "Elegí un archivo de audio.")]
    public IFormFile? File { get; set; }
}

/// <summary>Parámetros del trabajo que analiza una pista de música.</summary>
/// <param name="TrackId">Pista.</param>
public sealed record MusicProbePayload(long TrackId);

/// <summary>Respuesta al crear una receta o pedir un render.</summary>
/// <param name="Id">Identificador de lo creado.</param>
public sealed record CreatedResponse(long Id);

/// <summary>Parámetros del trabajo de render.</summary>
/// <param name="VariantId">Video a generar.</param>
public sealed record RenderPayload(long VariantId);

/// <summary>Un tramo a renderizar.</summary>
/// <param name="SourcePath">Ruta local del original.</param>
/// <param name="StartMs">Inicio del tramo.</param>
/// <param name="EndMs">Fin del tramo.</param>
/// <param name="HasAudio">Si el original tiene pista de audio.</param>
public sealed record RenderClip(string SourcePath, int StartMs, int EndMs, bool HasAudio);

/// <summary>Lo que se le pide al render: formato de salida y tramos en orden.</summary>
/// <param name="Width">Ancho de salida.</param>
/// <param name="Height">Alto de salida.</param>
/// <param name="FrameRate">Cuadros por segundo de salida.</param>
/// <param name="Clips">Tramos en orden.</param>
public sealed record RenderPlan(int Width, int Height, int FrameRate, List<RenderClip> Clips)
{
    /// <summary>Milisegundos de fundido a negro entre tramos; 0 para corte directo.</summary>
    public int FadeMs { get; init; }

    /// <summary>Ruta local del archivo ASS con subtítulos y textos a quemar; nulo si no hay.</summary>
    public string? TextFilePath { get; init; }

    /// <summary>Ruta local de la música de fondo; nulo si no lleva.</summary>
    public string? MusicPath { get; init; }

    /// <summary>Volumen de la música, de 0 a 1.</summary>
    public decimal MusicVolume { get; init; }
}

/// <summary>Proyección intermedia: un fragmento candidato a entrar en una receta.</summary>
public sealed class RecipeSource
{
    /// <summary>Fragmento.</summary>
    public long SegmentId { get; init; }

    /// <summary>Video al que pertenece.</summary>
    public long AssetId { get; init; }

    /// <summary>Nombre del video.</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>Inicio en milisegundos.</summary>
    public int StartMs { get; init; }

    /// <summary>Fin en milisegundos.</summary>
    public int EndMs { get; init; }

    /// <summary>Si el archivo es un video.</summary>
    public bool IsVideo { get; init; }

    /// <summary>Si el video tiene autorización de reutilización vigente.</summary>
    public bool Authorized { get; init; }
}

/// <summary>Proyección intermedia: un tramo de receta con lo que el render necesita de su original.</summary>
public sealed class RenderSource
{
    /// <summary>Video original.</summary>
    public long AssetId { get; init; }

    /// <summary>Ruta del original en el almacenamiento.</summary>
    public string StoragePath { get; init; } = string.Empty;

    /// <summary>Inicio del tramo.</summary>
    public int StartMs { get; init; }

    /// <summary>Fin del tramo.</summary>
    public int EndMs { get; init; }

    /// <summary>Si el original tiene pista de audio.</summary>
    public bool HasAudio { get; init; }
}

/// <summary>Proyección intermedia: una frase transcripta con el video al que pertenece.</summary>
public sealed class AssetSpeechLine
{
    /// <summary>Video.</summary>
    public long AssetId { get; init; }

    /// <summary>Inicio en milisegundos.</summary>
    public int StartMs { get; init; }

    /// <summary>Fin en milisegundos.</summary>
    public int EndMs { get; init; }

    /// <summary>Texto.</summary>
    public string Text { get; init; } = string.Empty;
}

/// <summary>Un fragmento del conjunto con el que el generador arma variantes.</summary>
/// <param name="SegmentId">Fragmento.</param>
/// <param name="DurationMs">Duración en milisegundos.</param>
/// <param name="Role">Función que cumple; decide en qué lugar del montaje puede ir.</param>
public sealed record PoolFragment(long SegmentId, int DurationMs, CreativeFunction? Role);

/// <summary>Lo que devuelve el generador de variantes.</summary>
/// <param name="Variants">Montajes aceptados, cada uno con sus fragmentos en orden.</param>
/// <param name="Discarded">Candidatos descartados por parecerse a uno aceptado.</param>
public sealed record GeneratedPlan(List<List<PoolFragment>> Variants, int Discarded);

/// <summary>Datos para previsualizar o crear un lote.</summary>
public sealed class CreateBatchBody : FinishingBody
{
    /// <summary>Nombre del lote.</summary>
    [Required(ErrorMessage = "El nombre es obligatorio.")]
    [StringLength(180, ErrorMessage = "El nombre admite hasta 180 caracteres.")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Brief del lote.</summary>
    [StringLength(2000, ErrorMessage = "El brief admite hasta 2000 caracteres.")]
    public string? Brief { get; set; }

    /// <summary>Formato de salida.</summary>
    [Required(ErrorMessage = "El formato es obligatorio.")]
    public string Format { get; set; } = "9:16";

    /// <summary>Cantidad de variantes.</summary>
    [Range(BatchService.MinCount, BatchService.MaxCount, ErrorMessage = "Un lote lleva entre 5 y 50 variantes.")]
    public int Count { get; set; } = 10;

    /// <summary>Duración máxima de cada variante, en segundos.</summary>
    [Range(2, 180, ErrorMessage = "La duración máxima va de 2 a 180 segundos.")]
    public int MaxDurationSeconds { get; set; } = 30;

    /// <summary>Similitud, en porcentaje, a partir de la cual una variante se descarta por parecida.</summary>
    [Range(50, 95, ErrorMessage = "El umbral de similitud va de 50 a 95 %.")]
    public int SimilarityPercent { get; set; } = 80;

    /// <summary>Semilla del generador; cambiarla da otra combinación.</summary>
    [Range(1, 1_000_000, ErrorMessage = "La semilla va de 1 a 1.000.000.")]
    public int Seed { get; set; } = 1;

    /// <summary>Fragmentos con los que armar las variantes, cada uno con su función.</summary>
    [MinLength(2, ErrorMessage = "El lote necesita al menos dos fragmentos.")]
    [MaxLength(200, ErrorMessage = "El lote admite hasta 200 fragmentos.")]
    public List<RecipeItemBody> Items { get; set; } = [];
}

/// <summary>Vista previa de un lote: qué variantes saldrían, sin crear nada.</summary>
/// <param name="Requested">Variantes pedidas.</param>
/// <param name="Discarded">Candidatos descartados por parecidos.</param>
/// <param name="Variants">Variantes que saldrían.</param>
public sealed record BatchPreview(int Requested, int Discarded, List<PreviewVariant> Variants);

/// <summary>Una variante de la vista previa.</summary>
/// <param name="SegmentIds">Fragmentos en orden.</param>
/// <param name="DurationMs">Duración del montaje.</param>
public sealed record PreviewVariant(List<long> SegmentIds, int DurationMs);

/// <summary>Un lote en el listado, con su avance.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Name">Nombre.</param>
/// <param name="RequestedCount">Variantes pedidas.</param>
/// <param name="Total">Variantes generadas.</param>
/// <param name="Ready">Videos terminados.</param>
/// <param name="Failed">Videos que fallaron.</param>
/// <param name="CreatedAt">Alta, en UTC.</param>
public sealed record BatchListItem(long Id, string Name, int RequestedCount, int Total, int Ready, int Failed, DateTime CreatedAt);

/// <summary>Una variante de un lote: su receta y su video más reciente.</summary>
/// <param name="RecipeId">Receta.</param>
/// <param name="RecipeName">Nombre de la receta.</param>
/// <param name="Items">Cantidad de tramos.</param>
/// <param name="Video">Video más reciente de la receta; nulo si no tiene.</param>
public sealed record BatchVariantView(long RecipeId, string RecipeName, int Items, VariantView? Video);

/// <summary>Un lote con sus parámetros, su avance y sus variantes.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Name">Nombre.</param>
/// <param name="Brief">Brief.</param>
/// <param name="Width">Ancho de salida.</param>
/// <param name="Height">Alto de salida.</param>
/// <param name="RequestedCount">Variantes pedidas.</param>
/// <param name="MaxDurationMs">Duración máxima de cada variante.</param>
/// <param name="SimilarityThreshold">Umbral de similitud, de 0 a 1.</param>
/// <param name="Seed">Semilla del generador.</param>
/// <param name="DiscardedBySimilarity">Candidatos descartados por parecidos.</param>
/// <param name="CreatedAt">Alta, en UTC.</param>
/// <param name="Variants">Variantes del lote.</param>
public sealed record BatchDetail(
    long Id, string Name, string? Brief, int Width, int Height, int RequestedCount, int MaxDurationMs, decimal SimilarityThreshold,
    int Seed, int DiscardedBySimilarity, DateTime CreatedAt, List<BatchVariantView> Variants)
{
    /// <summary>Videos terminados.</summary>
    public int Ready => Variants.Count(v => v.Video?.Status == MediaProcessingStatus.Ready);

    /// <summary>Videos que fallaron.</summary>
    public int Failed => Variants.Count(v => v.Video?.Status == MediaProcessingStatus.Failed);

    /// <summary>Videos que pasaron el control de calidad.</summary>
    public int PassedQuality => Variants.Count(v => v.Video?.PassedQuality == true);

    /// <summary>Avance del lote, de 0 a 100: videos terminados o fallidos sobre el total.</summary>
    public int Percent => Variants.Count == 0 ? 0 : (Ready + Failed) * 100 / Variants.Count;

    /// <summary>Tiempo total de render de los videos terminados, en milisegundos: el costo de cómputo del lote.</summary>
    public long RenderMs => Variants.Sum(v => (long)(v.Video?.RenderMs ?? 0));
}

/// <summary>Proyección intermedia: una receta de un lote.</summary>
public sealed class BatchRecipeRow
{
    /// <summary>Receta.</summary>
    public long RecipeId { get; init; }

    /// <summary>Nombre de la receta.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Cantidad de tramos.</summary>
    public int Items { get; init; }
}

/// <summary>Proyección intermedia: un video de una receta de un lote.</summary>
public sealed class BatchVideoRow
{
    /// <summary>Receta de la que sale.</summary>
    public long RecipeId { get; init; }

    /// <summary>El video.</summary>
    public VariantView Video { get; init; } = null!;
}

/// <summary>Un tramo del borrador con el que se arma una receta nueva a partir de otra.</summary>
public sealed class DraftItem
{
    /// <summary>Fragmento de la biblioteca; nulo si ya no existe.</summary>
    public long? SegmentId { get; init; }

    /// <summary>Video original.</summary>
    public long AssetId { get; init; }

    /// <summary>Nombre del video original.</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>Desde dónde se usa, en milisegundos del original.</summary>
    public int StartMs { get; init; }

    /// <summary>Hasta dónde se usa, en milisegundos del original.</summary>
    public int EndMs { get; init; }

    /// <summary>Inicio actual del fragmento; nulo si ya no existe.</summary>
    public int? SegmentStartMs { get; init; }

    /// <summary>Fin actual del fragmento; nulo si ya no existe.</summary>
    public int? SegmentEndMs { get; init; }

    /// <summary>Si el fragmento tiene miniatura.</summary>
    public bool HasThumbnail { get; init; }

    /// <summary>Función que cumple.</summary>
    public CreativeFunction? Role { get; init; }
}

/// <summary>Borrador de una receta nueva armado a partir de una existente: los mismos datos, listos para cambiar
/// y guardar como otra receta.</summary>
/// <param name="Name">Nombre propuesto.</param>
/// <param name="Concept">Concepto comercial.</param>
/// <param name="Hypothesis">Hipótesis.</param>
/// <param name="Format">Formato de salida.</param>
/// <param name="Subtitles">Si lleva subtítulos.</param>
/// <param name="Transition">Transición entre tramos.</param>
/// <param name="MusicTrackId">Música de fondo.</param>
/// <param name="MusicVolumePercent">Volumen de la música.</param>
/// <param name="Overlays">Textos sobre el video.</param>
/// <param name="Items">Tramos que se pudieron conservar, con sus tiempos ajustados al fragmento actual.</param>
/// <param name="Dropped">Tramos que no se pudieron conservar porque su fragmento ya no existe.</param>
public sealed record RecipeDraft(
    string Name, string? Concept, string? Hypothesis, string Format, bool Subtitles, RecipeTransition Transition, long? MusicTrackId,
    int MusicVolumePercent, List<OverlayView> Overlays, List<DraftItem> Items, int Dropped);

/// <summary>Respuesta de una operación que afecta a varios elementos.</summary>
/// <param name="Count">Cuántos se afectaron.</param>
public sealed record CountResponse(int Count);

/// <summary>Un video generado en el listado general.</summary>
/// <param name="VariantId">Video.</param>
/// <param name="RecipeId">Receta de la que sale.</param>
/// <param name="RecipeName">Nombre de la receta.</param>
/// <param name="BatchId">Lote al que pertenece la receta; nulo si es suelta.</param>
/// <param name="BatchName">Nombre del lote.</param>
/// <param name="Status">Estado del render.</param>
/// <param name="Error">Por qué falló.</param>
/// <param name="DurationMs">Duración.</param>
/// <param name="Width">Ancho.</param>
/// <param name="Height">Alto.</param>
/// <param name="CreatedAt">Pedido, en UTC.</param>
/// <param name="Ads">Anuncios de Meta en los que se publicó.</param>
public sealed record GeneratedVideoItem(
    long VariantId, long RecipeId, string RecipeName, long? BatchId, string? BatchName, MediaProcessingStatus Status, string? Error, int? DurationMs,
    int? Width, int? Height, DateTime CreatedAt, int Ads);

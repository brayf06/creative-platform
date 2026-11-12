using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CreativeIntelligence.Web.Models.Dto;

/// <summary>El análisis vigente de un fragmento.</summary>
/// <param name="SegmentId">Fragmento.</param>
/// <param name="IsStale">Verdadero si el fragmento cambió de tramo después de analizarse.</param>
/// <param name="VisualDescription">Qué se ve.</param>
/// <param name="OnScreenText">Texto sobreimpreso.</param>
/// <param name="Product">Producto que aparece.</param>
/// <param name="Actions">Acciones observadas.</param>
/// <param name="CommercialAngle">Enfoque comercial.</param>
/// <param name="MainMessage">Mensaje principal.</param>
/// <param name="Audience">Público previsto.</param>
/// <param name="VisualQuality">Calidad visual de 1 a 5.</param>
/// <param name="Confidence">Confianza del modelo, de 0 a 1.</param>
/// <param name="Functions">Funciones narrativas.</param>
/// <param name="Model">Modelo que lo produjo.</param>
/// <param name="PromptVersion">Versión del prompt.</param>
/// <param name="CreatedAt">Cuándo se analizó, UTC.</param>
/// <param name="Inherited">Verdadero si se copió del análisis del original del que este video es duplicado visual.</param>
public sealed record SegmentAnalysisItem(
    long SegmentId,
    bool IsStale,
    string VisualDescription,
    string OnScreenText,
    string Product,
    string Actions,
    string CommercialAngle,
    string MainMessage,
    string Audience,
    int VisualQuality,
    decimal Confidence,
    List<CreativeFunction> Functions,
    string Model,
    string PromptVersion,
    DateTime CreatedAt,
    bool Inherited);

/// <summary>El análisis con IA de un video, para la pantalla.</summary>
/// <param name="AssetId">Video.</param>
/// <param name="Status">Estado; nulo si nunca se pidió.</param>
/// <param name="Error">Por qué falló; nulo si no falló.</param>
/// <param name="Items">Análisis vigente de cada escena que lo tiene.</param>
/// <param name="Narratives">Tramos narrativos del video, en orden temporal.</param>
public sealed record AnalysisView(
    long AssetId, MediaProcessingStatus? Status, string? Error, List<SegmentAnalysisItem> Items, List<NarrativeItem> Narratives);

/// <summary>Estimación previa de un análisis, para decidir antes de gastar.</summary>
/// <param name="Configured">Falso si falta la clave del proveedor.</param>
/// <param name="Model">Modelo que se usaría.</param>
/// <param name="Scenes">Escenas a analizar.</param>
/// <param name="Frames">Cuadros que se enviarían.</param>
/// <param name="InputTokens">Tokens de entrada estimados.</param>
/// <param name="OutputTokens">Tokens de salida estimados.</param>
/// <param name="CostUsd">Costo estimado en dólares; 0 si ya está en cache.</param>
/// <param name="MaxCostUsd">Tope configurado por video.</param>
/// <param name="Cached">Verdadero si este mismo análisis ya se hizo y no se volvería a pagar.</param>
/// <param name="InheritsFromAssetId">Si el video es un duplicado visual de otro ya analizado, ese otro: el análisis
/// se copia de él sin llamar al proveedor. Nulo si no aplica.</param>
public sealed record AnalysisEstimate(
    bool Configured, string Model, int Scenes, int Frames, int InputTokens, int OutputTokens, decimal CostUsd,
    decimal MaxCostUsd, bool Cached, long? InheritsFromAssetId);

/// <summary>Un tramo de un video, con el dato que lo identifica: un fragmento o un análisis.</summary>
/// <param name="Id">Identificador del fragmento o del análisis.</param>
/// <param name="StartMs">Inicio en milisegundos.</param>
/// <param name="EndMs">Fin en milisegundos.</param>
public sealed record TimeRange(long Id, int StartMs, int EndMs);

/// <summary>Lo que hace falta para analizar un video: sus escenas ya armadas y la clave de cache.</summary>
/// <param name="FileName">Nombre del archivo.</param>
/// <param name="Sha256">Hash del original.</param>
/// <param name="StoragePath">Ruta del original.</param>
/// <param name="Scenes">Escenas, sin cuadros todavía.</param>
/// <param name="SegmentIds">Fragmento de cada escena, en el mismo orden.</param>
/// <param name="FrameTimes">Instantes de los cuadros de cada escena, en el mismo orden.</param>
/// <param name="CacheKey">Clave de cache del análisis.</param>
public sealed record AnalysisPlan(
    string FileName,
    string Sha256,
    string StoragePath,
    List<SceneInput> Scenes,
    List<long> SegmentIds,
    List<List<int>> FrameTimes,
    string CacheKey);

/// <summary>Proyección intermedia del video para armar el plan de análisis.</summary>
public sealed class AnalysisAssetInfo
{
    /// <summary>Nombre del archivo.</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>Hash del original.</summary>
    public string Sha256 { get; init; } = string.Empty;

    /// <summary>Ruta del original.</summary>
    public string StoragePath { get; init; } = string.Empty;

    /// <summary>Si es un video.</summary>
    public bool IsVideo { get; init; }

    /// <summary>Estado de la detección de escenas.</summary>
    public MediaProcessingStatus? SceneStatus { get; init; }

    /// <summary>Estado de la transcripción.</summary>
    public MediaProcessingStatus? TranscriptStatus { get; init; }
}

/// <summary>Resultado de guardar un original en el almacenamiento.</summary>
/// <param name="Sha256">SHA-256 del contenido, 64 hexadecimales en minúscula.</param>
/// <param name="SizeBytes">Tamaño en bytes; 0 si el contenido estaba vacío (y entonces no se guardó nada).</param>
/// <param name="RelativePath">Ruta relativa a la raíz del almacenamiento; vacía si no se guardó.</param>
public sealed record StoredOriginal(string Sha256, long SizeBytes, string RelativePath);

/// <summary>Volumen del audio de un tramo, en dB respecto del máximo digital (0 dB).</summary>
/// <param name="MeanDb">Volumen medio.</param>
/// <param name="PeakDb">Pico.</param>
public sealed record AudioLevels(decimal MeanDb, decimal PeakDb);

/// <summary>Calidad técnica del audio de un fragmento, deducida de su volumen. En JSON viaja como texto.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AudioQuality>))]
public enum AudioQuality
{
    /// <summary>Prácticamente sin sonido.</summary>
    Silent,

    /// <summary>Se oye, pero muy bajo.</summary>
    Low,

    /// <summary>Volumen dentro de lo normal.</summary>
    Good,

    /// <summary>El pico llega al máximo digital: probablemente distorsiona.</summary>
    Clipped,
}

/// <summary>Un tramo entre dos cortes detectados.</summary>
/// <param name="StartMs">Inicio en milisegundos, inclusive.</param>
/// <param name="EndMs">Fin en milisegundos, exclusive.</param>
public sealed record SceneBoundary(int StartMs, int EndMs);

/// <summary>Propiedades técnicas de un archivo, leídas por ffprobe.</summary>
/// <param name="DurationMs">Duración en milisegundos; nulo si el contenedor no la informa.</param>
/// <param name="Width">Ancho en píxeles tal como se ve.</param>
/// <param name="Height">Alto en píxeles tal como se ve.</param>
/// <param name="FrameRate">Cuadros por segundo; nulo si no se pudo determinar.</param>
/// <param name="VideoCodec">Códec de video o de imagen.</param>
/// <param name="AudioCodec">Códec de audio; nulo si no tiene audio.</param>
/// <param name="Container">Contenedor informado por ffprobe.</param>
public sealed record MediaProbe(
    int? DurationMs, int Width, int Height, decimal? FrameRate, string? VideoCodec, string? AudioCodec, string? Container);

/// <summary>Una frase transcripta con sus tiempos.</summary>
/// <param name="StartMs">Inicio en milisegundos.</param>
/// <param name="EndMs">Fin en milisegundos.</param>
/// <param name="Text">Texto de la frase.</param>
public sealed record TranscriptLineItem(int StartMs, int EndMs, string Text);

/// <summary>Resultado de transcribir un video.</summary>
/// <param name="Language">Idioma detectado o indicado; nulo si no había voz.</param>
/// <param name="Model">Modelo usado, por ejemplo <c>faster-whisper:small</c>.</param>
/// <param name="Lines">Frases en orden temporal.</param>
public sealed record Transcription(string? Language, string Model, List<TranscriptLineItem> Lines);

/// <summary>La transcripción de un video y su estado, para la pantalla.</summary>
/// <param name="AssetId">Video.</param>
/// <param name="HasAudio">Si el video tiene pista de audio; sin audio no se transcribe.</param>
/// <param name="Status">Estado de la transcripción; nulo si no corresponde o no se pidió.</param>
/// <param name="Error">Por qué falló; nulo si no falló.</param>
/// <param name="Language">Idioma; nulo si no se transcribió.</param>
/// <param name="Model">Modelo que produjo la transcripción vigente; nulo si no se transcribió.</param>
/// <param name="Lines">Frases en orden temporal.</param>
public sealed record TranscriptView(
    long AssetId,
    bool HasAudio,
    MediaProcessingStatus? Status,
    string? Error,
    string? Language,
    string? Model,
    List<TranscriptLineItem> Lines);

/// <summary>Proyección intermedia de la cabecera de <see cref="TranscriptView"/>.</summary>
public sealed class TranscriptHeader
{
    /// <summary>Si el video tiene pista de audio.</summary>
    public bool HasAudio { get; init; }

    /// <summary>Estado de la transcripción.</summary>
    public MediaProcessingStatus? Status { get; init; }

    /// <summary>Error de la transcripción.</summary>
    public string? Error { get; init; }

    /// <summary>Idioma.</summary>
    public string? Language { get; init; }

    /// <summary>Modelo.</summary>
    public string? Model { get; init; }
}

/// <summary>Un fragmento de un video, para la línea de escenas.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Kind">Visual o narrativo.</param>
/// <param name="Origin">Detectado o manual.</param>
/// <param name="StartMs">Inicio en milisegundos, inclusive.</param>
/// <param name="EndMs">Fin en milisegundos, exclusive.</param>
/// <param name="HasThumbnail">Si tiene miniatura; falso mientras se regenera tras una edición.</param>
/// <param name="SelectedForProduction">Si está elegido para nuevas creatividades.</param>
/// <param name="Tags">Etiquetas, en orden alfabético.</param>
/// <param name="Transcript">Lo que se dice durante el fragmento: las frases de la transcripción cuyo punto medio
/// cae dentro del tramo. Vacío si no hay voz o no hay transcripción.</param>
/// <param name="AudioMeanDb">Volumen medio del audio en dB; nulo si el video no tiene audio o falta medirlo.</param>
/// <param name="AudioPeakDb">Pico del audio en dB; nulo en los mismos casos.</param>
public sealed record SegmentItem(
    long Id,
    MediaSegmentKind Kind,
    MediaSegmentOrigin Origin,
    int StartMs,
    int EndMs,
    bool HasThumbnail,
    bool SelectedForProduction,
    List<string> Tags,
    string Transcript,
    decimal? AudioMeanDb = null,
    decimal? AudioPeakDb = null)
{
    /// <summary>Calidad técnica del audio según su volumen; nulo si no hay medición.</summary>
    public AudioQuality? AudioQuality => MediaRules.ClassifyAudio(AudioMeanDb, AudioPeakDb);
}

/// <summary>Parámetros del trabajo de detección de escenas.</summary>
/// <param name="AssetId">Video.</param>
/// <param name="ReplaceManual">Si la detección debe descartar también los fragmentos corregidos a mano.</param>
public sealed record MediaScenesPayload(long AssetId, bool ReplaceManual);

/// <summary>Mover el corte entre un fragmento y el siguiente.</summary>
public sealed class MoveBoundaryBody
{
    /// <summary>Nuevo fin del fragmento (e inicio del siguiente), en milisegundos desde el comienzo del video.</summary>
    [Range(0, int.MaxValue, ErrorMessage = "El corte debe ser un tiempo válido.")]
    public int EndMs { get; set; }
}

/// <summary>Dividir un fragmento en dos.</summary>
public sealed class SplitSegmentBody
{
    /// <summary>Punto de división, en milisegundos desde el comienzo del video.</summary>
    [Range(0, int.MaxValue, ErrorMessage = "El punto de división debe ser un tiempo válido.")]
    public int AtMs { get; set; }
}

/// <summary>Etiquetas de un fragmento.</summary>
public sealed class SegmentTagsBody
{
    /// <summary>Etiquetas separadas por coma; reemplazan a las anteriores. Vacío para quitarlas todas.</summary>
    [StringLength(1200, ErrorMessage = "Las etiquetas admiten hasta 1200 caracteres en total.")]
    public string? Tags { get; set; }
}

/// <summary>Elegir o quitar un fragmento para producción.</summary>
public sealed class SelectSegmentBody
{
    /// <summary>Verdadero para elegirlo.</summary>
    public bool Selected { get; set; }
}

/// <summary>Pedido de detección de escenas.</summary>
public sealed class DetectScenesBody
{
    /// <summary>Verdadero para descartar los fragmentos corregidos a mano y sus etiquetas.</summary>
    public bool DiscardManualEdits { get; set; }
}

/// <summary>Las escenas de un video y el estado de su detección.</summary>
/// <param name="AssetId">Video.</param>
/// <param name="DurationMs">Duración del video; nulo si aún no se analizó.</param>
/// <param name="Status">Estado de la detección; nulo si no corresponde o no se pidió.</param>
/// <param name="Error">Por qué falló la detección; nulo si no falló.</param>
/// <param name="CanSelect">Si el video tiene autorización de reutilización vigente y por lo tanto sus fragmentos
/// pueden elegirse para producción.</param>
/// <param name="HasAudio">Si el video tiene pista de audio; sin ella no hay niveles que medir.</param>
/// <param name="Segments">Fragmentos visuales en orden temporal.</param>
public sealed record SceneList(
    long AssetId, int? DurationMs, MediaProcessingStatus? Status, string? Error, bool CanSelect, bool HasAudio,
    List<SegmentItem> Segments);

/// <summary>Proyección intermedia de la cabecera de <see cref="SceneList"/>.</summary>
public sealed class SceneHeader
{
    /// <summary>Duración del video.</summary>
    public int? DurationMs { get; init; }

    /// <summary>Estado de la detección.</summary>
    public MediaProcessingStatus? Status { get; init; }

    /// <summary>Error de la detección.</summary>
    public string? Error { get; init; }

    /// <summary>Si la autorización de reutilización está vigente hoy (fecha de la base).</summary>
    public bool CanSelect { get; init; }

    /// <summary>Si el video tiene pista de audio.</summary>
    public bool HasAudio { get; init; }
}

/// <summary>Parámetros del trabajo de análisis técnico de un archivo.</summary>
/// <param name="AssetId">Archivo a analizar.</param>
public sealed record MediaProbePayload(long AssetId);

/// <summary>Formato de archivo admitido.</summary>
/// <param name="Kind">Video o imagen.</param>
/// <param name="ContentType">Tipo MIME con el que se sirve.</param>
public sealed record MediaFormat(MediaAssetKind Kind, string ContentType);

/// <summary>Datos que acompañan a un archivo al ingresarlo a la biblioteca.</summary>
public class MediaRightsBody
{
    /// <summary>Titular de los derechos. Hasta 200 caracteres.</summary>
    [StringLength(200, ErrorMessage = "El titular admite hasta 200 caracteres.")]
    public string? RightsHolder { get; set; }

    /// <summary>Licencia, alcance y restricciones. Hasta 1000 caracteres.</summary>
    [StringLength(1000, ErrorMessage = "Las notas de licencia admiten hasta 1000 caracteres.")]
    public string? LicenseNotes { get; set; }

    /// <summary>Si el material puede reutilizarse en nuevas creatividades.</summary>
    public bool ReuseAuthorized { get; set; }

    /// <summary>Último día de vigencia de la autorización; nulo si no vence.</summary>
    public DateOnly? RightsExpireOn { get; set; }
}

/// <summary>Qué pasó con un archivo al ingresarlo. En JSON viaja como texto.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<IngestOutcome>))]
public enum IngestOutcome
{
    /// <summary>Se creó un archivo nuevo en la biblioteca.</summary>
    Created,

    /// <summary>La organización ya tenía un archivo con el mismo contenido; no se creó otro.</summary>
    Duplicate,
}

/// <summary>Resultado de ingresar un archivo.</summary>
/// <param name="AssetId">Archivo de la biblioteca: el nuevo, o el existente si era un duplicado.</param>
/// <param name="FileName">Nombre del archivo cargado.</param>
/// <param name="Outcome">Si se creó o era un duplicado exacto.</param>
public sealed record IngestResult(long AssetId, string FileName, IngestOutcome Outcome);

/// <summary>Filtros del listado de la biblioteca. Todos opcionales.</summary>
public sealed class MediaFilter
{
    /// <summary>Sólo videos o sólo imágenes.</summary>
    public MediaAssetKind? Kind { get; set; }

    /// <summary>Sólo con (o sin) autorización de reutilización.</summary>
    public bool? ReuseAuthorized { get; set; }

    /// <summary>Texto contenido en el nombre del archivo. Hasta 100 caracteres.</summary>
    [StringLength(100, ErrorMessage = "La búsqueda admite hasta 100 caracteres.")]
    public string? Search { get; set; }
}

/// <summary>Fila del listado de la biblioteca.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="FileName">Nombre original del archivo.</param>
/// <param name="Kind">Video o imagen.</param>
/// <param name="SizeBytes">Tamaño en bytes.</param>
/// <param name="ReuseAuthorized">Si tiene autorización de reutilización.</param>
/// <param name="RightsHolder">Titular de los derechos; nulo si no se informó.</param>
/// <param name="RightsExpireOn">Último día de vigencia; nulo si no vence.</param>
/// <param name="CreatedAt">Alta, UTC.</param>
/// <param name="ProcessingStatus">Estado del análisis técnico.</param>
/// <param name="DurationMs">Duración en milisegundos; nulo para imágenes o sin analizar.</param>
/// <param name="Width">Ancho en píxeles; nulo sin analizar.</param>
/// <param name="Height">Alto en píxeles; nulo sin analizar.</param>
/// <param name="HasThumbnail">Si ya tiene miniatura.</param>
/// <param name="DuplicateOfAssetId">Archivo anterior del que éste es un duplicado visual; nulo si no lo es.</param>
public sealed record MediaAssetItem(
    long Id,
    string FileName,
    MediaAssetKind Kind,
    long SizeBytes,
    bool ReuseAuthorized,
    string? RightsHolder,
    DateOnly? RightsExpireOn,
    DateTime CreatedAt,
    MediaProcessingStatus ProcessingStatus,
    int? DurationMs,
    int? Width,
    int? Height,
    bool HasThumbnail,
    long? DuplicateOfAssetId);

/// <summary>Detalle de un archivo de la biblioteca.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="FileName">Nombre original del archivo.</param>
/// <param name="Kind">Video o imagen.</param>
/// <param name="ContentType">Tipo MIME.</param>
/// <param name="SizeBytes">Tamaño en bytes.</param>
/// <param name="Sha256">SHA-256 del contenido.</param>
/// <param name="SourceKind">Procedencia.</param>
/// <param name="SourceReference">Referencia de la procedencia; nulo para una carga manual.</param>
/// <param name="RightsHolder">Titular de los derechos; nulo si no se informó.</param>
/// <param name="LicenseNotes">Licencia, alcance y restricciones; nulo si no se informó.</param>
/// <param name="ReuseAuthorized">Si tiene autorización de reutilización.</param>
/// <param name="RightsExpireOn">Último día de vigencia; nulo si no vence.</param>
/// <param name="CreatedAt">Alta, UTC.</param>
/// <param name="ProcessingStatus">Estado del análisis técnico.</param>
/// <param name="ProcessingError">Por qué falló el análisis; nulo si no falló.</param>
/// <param name="DurationMs">Duración en milisegundos; nulo para imágenes o sin analizar.</param>
/// <param name="Width">Ancho en píxeles; nulo sin analizar.</param>
/// <param name="Height">Alto en píxeles; nulo sin analizar.</param>
/// <param name="FrameRate">Cuadros por segundo; nulo para imágenes o sin determinar.</param>
/// <param name="VideoCodec">Códec de video o de imagen; nulo sin analizar.</param>
/// <param name="AudioCodec">Códec de audio; nulo si no tiene audio o sin analizar.</param>
/// <param name="Container">Contenedor; nulo sin analizar.</param>
/// <param name="DuplicateOfAssetId">Archivo anterior del que éste es un duplicado visual; nulo si no lo es.</param>
public sealed record MediaAssetDetail(
    long Id,
    string FileName,
    MediaAssetKind Kind,
    string ContentType,
    long SizeBytes,
    string Sha256,
    MediaSourceKind SourceKind,
    string? SourceReference,
    string? RightsHolder,
    string? LicenseNotes,
    bool ReuseAuthorized,
    DateOnly? RightsExpireOn,
    DateTime CreatedAt,
    MediaProcessingStatus ProcessingStatus,
    string? ProcessingError,
    int? DurationMs,
    int? Width,
    int? Height,
    decimal? FrameRate,
    string? VideoCodec,
    string? AudioCodec,
    string? Container,
    long? DuplicateOfAssetId);

/// <summary>Contenido de un original listo para servir.</summary>
/// <param name="Content">Flujo de lectura con posicionamiento; quien lo recibe lo cierra.</param>
/// <param name="ContentType">Tipo MIME.</param>
/// <param name="FileName">Nombre original del archivo.</param>
public sealed record MediaContent(Stream Content, string ContentType, string FileName);

/// <summary>Proyección intermedia: un archivo candidato a ser el original de un duplicado visual.</summary>
public sealed class HashCandidate
{
    /// <summary>Archivo.</summary>
    public long Id { get; init; }

    /// <summary>Su huella visual.</summary>
    public string Hash { get; init; } = string.Empty;
}

/// <summary>Proyección intermedia para abrir un original: sólo lo necesario para ubicarlo.</summary>
public sealed class MediaLocation
{
    /// <summary>Ruta relativa en el almacenamiento.</summary>
    public string StoragePath { get; init; } = string.Empty;

    /// <summary>Tipo MIME.</summary>
    public string ContentType { get; init; } = string.Empty;

    /// <summary>Nombre original del archivo.</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>Hash del original; nombra sus derivados.</summary>
    public string Sha256 { get; init; } = string.Empty;

    /// <summary>Si el archivo tiene pista de audio.</summary>
    public bool HasAudio { get; init; }
}

/// <summary>Filtros y consulta del buscador de fragmentos. Todo es opcional.</summary>
public sealed class SegmentSearchFilter
{
    /// <summary>Qué se busca, en lenguaje natural. Hasta 300 caracteres.</summary>
    [StringLength(300, ErrorMessage = "La consulta admite hasta 300 caracteres.")]
    public string? Query { get; set; }

    /// <summary>Sólo fragmentos con esta función narrativa según su análisis vigente.</summary>
    public CreativeFunction? Function { get; set; }

    /// <summary>Duración máxima del fragmento en segundos.</summary>
    [Range(0.1, 600, ErrorMessage = "La duración máxima debe estar entre 0,1 y 600 segundos.")]
    public decimal? MaxDurationSeconds { get; set; }

    /// <summary>Sólo escenas o sólo tramos narrativos; sin indicar, los dos.</summary>
    public MediaSegmentKind? Kind { get; set; }

    /// <summary>Sólo fragmentos con esta calidad de audio.</summary>
    public AudioQuality? Audio { get; set; }

    /// <summary>Sólo fragmentos de videos con autorización de reutilización vigente.</summary>
    public bool OnlyAuthorized { get; set; }

    /// <summary>Sólo fragmentos elegidos para producción.</summary>
    public bool OnlySelected { get; set; }

    /// <summary>Cantidad máxima de resultados, de 1 a 50.</summary>
    [Range(1, 50, ErrorMessage = "Se pueden pedir de 1 a 50 resultados.")]
    public int Take { get; set; } = 20;
}

/// <summary>Cómo se resolvió una búsqueda. En JSON viaja como texto.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SearchMode>))]
public enum SearchMode
{
    /// <summary>Sin texto: sólo filtros, del más nuevo al más viejo.</summary>
    Filters,

    /// <summary>Por significado: similitud entre la consulta y la documentación de cada fragmento.</summary>
    Semantic,

    /// <summary>Por palabras: todas las palabras de la consulta deben aparecer. Se usa si no hay proveedor de
    /// embeddings configurado.</summary>
    Keyword,
}

/// <summary>Un fragmento encontrado.</summary>
/// <param name="SegmentId">Fragmento.</param>
/// <param name="AssetId">Video al que pertenece.</param>
/// <param name="FileName">Nombre del video.</param>
/// <param name="StartMs">Inicio en milisegundos.</param>
/// <param name="EndMs">Fin en milisegundos.</param>
/// <param name="Score">Similitud con la consulta, de 0 a 1; nulo si la búsqueda no fue semántica.</param>
/// <param name="HasThumbnail">Si tiene miniatura.</param>
/// <param name="ReuseAuthorized">Si el video tiene autorización de reutilización vigente.</param>
/// <param name="SelectedForProduction">Si está elegido para producción.</param>
/// <param name="Functions">Funciones narrativas de su análisis vigente.</param>
/// <param name="VisualDescription">Qué se ve; vacío si no fue analizado.</param>
/// <param name="MainMessage">Mensaje principal; vacío si no fue analizado.</param>
/// <param name="Transcript">Lo que se dice en el fragmento.</param>
/// <param name="Tags">Etiquetas.</param>
/// <param name="Kind">Si es una escena o un tramo narrativo.</param>
/// <param name="AudioMeanDb">Volumen medio del audio en dB; nulo si no hay medición.</param>
/// <param name="AudioPeakDb">Pico del audio en dB; nulo si no hay medición.</param>
public sealed record SearchHit(
    long SegmentId,
    long AssetId,
    string FileName,
    int StartMs,
    int EndMs,
    double? Score,
    bool HasThumbnail,
    bool ReuseAuthorized,
    bool SelectedForProduction,
    List<CreativeFunction> Functions,
    string VisualDescription,
    string MainMessage,
    string Transcript,
    List<string> Tags,
    MediaSegmentKind Kind,
    decimal? AudioMeanDb,
    decimal? AudioPeakDb)
{
    /// <summary>Calidad técnica del audio según su volumen; nulo si no hay medición.</summary>
    public AudioQuality? AudioQuality => MediaRules.ClassifyAudio(AudioMeanDb, AudioPeakDb);
}

/// <summary>Resultado de una búsqueda.</summary>
/// <param name="Mode">Cómo se resolvió.</param>
/// <param name="SemanticAvailable">Si hay proveedor de embeddings configurado.</param>
/// <param name="Candidates">Cuántos fragmentos pasaron los filtros antes de ordenar y cortar.</param>
/// <param name="Hits">Fragmentos encontrados, del más al menos pertinente.</param>
/// <param name="Interpreted">Filtros que el sistema dedujo del texto de la consulta, descritos para mostrarlos;
/// vacío si no dedujo ninguno.</param>
public sealed record SearchResult(SearchMode Mode, bool SemanticAvailable, int Candidates, List<SearchHit> Hits, List<string> Interpreted);

/// <summary>Una consulta ya interpretada: el texto a buscar y los filtros que estaban escritos en ella.</summary>
/// <param name="Text">Lo que queda para buscar por significado o por palabras; puede quedar vacío.</param>
/// <param name="Function">Función narrativa deducida; nulo si no se dedujo.</param>
/// <param name="MaxDurationSeconds">Duración máxima deducida; nulo si no se dedujo.</param>
/// <param name="OnlyAuthorized">Si la consulta pedía sólo material autorizado.</param>
/// <param name="OnlySelected">Si la consulta pedía sólo lo elegido para producción.</param>
public sealed record ParsedQuery(string Text, CreativeFunction? Function, decimal? MaxDurationSeconds, bool OnlyAuthorized, bool OnlySelected);

/// <summary>El texto que representa a un fragmento en el índice.</summary>
/// <param name="SegmentId">Fragmento.</param>
/// <param name="Text">Documentación del fragmento en una sola cadena; vacía si no hay nada que indexar.</param>
public sealed record SegmentText(long SegmentId, string Text);

/// <summary>Proyección intermedia: un fragmento candidato con su vector.</summary>
public sealed class SegmentVector
{
    /// <summary>Fragmento.</summary>
    public long SegmentId { get; init; }

    /// <summary>Vector serializado.</summary>
    public byte[] Vector { get; init; } = [];
}

/// <summary>Proyección intermedia: datos de un fragmento para armar un resultado o su texto de índice.</summary>
public sealed class SegmentRow
{
    /// <summary>Fragmento.</summary>
    public long SegmentId { get; init; }

    /// <summary>Video.</summary>
    public long AssetId { get; init; }

    /// <summary>Nombre del video.</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>Inicio en milisegundos.</summary>
    public int StartMs { get; init; }

    /// <summary>Fin en milisegundos.</summary>
    public int EndMs { get; init; }

    /// <summary>Si tiene miniatura.</summary>
    public bool HasThumbnail { get; init; }

    /// <summary>Si el video tiene autorización vigente.</summary>
    public bool ReuseAuthorized { get; init; }

    /// <summary>Si está elegido para producción.</summary>
    public bool SelectedForProduction { get; init; }

    /// <summary>Etiquetas.</summary>
    public List<string> Tags { get; init; } = [];

    /// <summary>Escena o tramo narrativo.</summary>
    public MediaSegmentKind Kind { get; init; }

    /// <summary>Volumen medio del audio en dB.</summary>
    public decimal? AudioMeanDb { get; init; }

    /// <summary>Pico del audio en dB.</summary>
    public decimal? AudioPeakDb { get; init; }
}

/// <summary>Proyección intermedia: análisis vigente de un fragmento.</summary>
public sealed class AnalysisRow
{
    /// <summary>Fragmento.</summary>
    public long SegmentId { get; init; }

    /// <summary>Qué se ve.</summary>
    public string VisualDescription { get; init; } = string.Empty;

    /// <summary>Texto en pantalla.</summary>
    public string OnScreenText { get; init; } = string.Empty;

    /// <summary>Producto.</summary>
    public string Product { get; init; } = string.Empty;

    /// <summary>Acciones.</summary>
    public string Actions { get; init; } = string.Empty;

    /// <summary>Enfoque comercial.</summary>
    public string CommercialAngle { get; init; } = string.Empty;

    /// <summary>Mensaje principal.</summary>
    public string MainMessage { get; init; } = string.Empty;

    /// <summary>Público.</summary>
    public string Audience { get; init; } = string.Empty;

    /// <summary>Funciones narrativas.</summary>
    public List<CreativeFunction> Functions { get; init; } = [];
}

/// <summary>Proyección intermedia: una frase transcripta con su video.</summary>
public sealed class TranscriptRow
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

/// <summary>Proyección intermedia: un fragmento al que le falta algún derivado.</summary>
public sealed class PendingSegment
{
    /// <summary>Inicio en milisegundos.</summary>
    public int StartMs { get; init; }

    /// <summary>Fin en milisegundos.</summary>
    public int EndMs { get; init; }

    /// <summary>Si le falta la miniatura.</summary>
    public bool NeedsThumbnail { get; init; }

    /// <summary>Si le falta la medición de audio.</summary>
    public bool NeedsAudio { get; init; }
}

/// <summary>Alta de una carpeta vigilada: la ruta y los derechos que reciben los archivos que entren por ella.</summary>
public sealed class CreateWatchedFolderBody : MediaRightsBody
{
    /// <summary>Ruta de la carpeta en el servidor. Debe existir y estar dentro de una raíz permitida.</summary>
    [Required(ErrorMessage = "La ruta es obligatoria.")]
    [StringLength(500, ErrorMessage = "La ruta admite hasta 500 caracteres.")]
    public string Path { get; set; } = string.Empty;
}

/// <summary>Fila del listado de carpetas vigiladas.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Path">Ruta completa en el servidor.</param>
/// <param name="IsActive">Si el Worker la revisa periódicamente.</param>
/// <param name="ReuseAuthorized">Si los archivos que entran por ella quedan autorizados para reutilizar.</param>
/// <param name="RightsHolder">Titular que se les asigna; nulo si no se indicó.</param>
/// <param name="LastScanAt">Última revisión, UTC; nulo si nunca se revisó.</param>
/// <param name="LastScanSummary">Qué encontró la última revisión; nulo si nunca se revisó.</param>
/// <param name="FileCount">Archivos de la carpeta ya vistos.</param>
public sealed record WatchedFolderItem(
    int Id, string Path, bool IsActive, bool ReuseAuthorized, string? RightsHolder, DateTime? LastScanAt,
    string? LastScanSummary, int FileCount);

/// <summary>Las carpetas vigiladas de la organización y desde dónde se puede importar.</summary>
/// <param name="AllowedRoots">Raíces permitidas, como rutas completas.</param>
/// <param name="Folders">Carpetas vigiladas, por ruta.</param>
public sealed record WatchedFolderList(List<string> AllowedRoots, List<WatchedFolderItem> Folders);

/// <summary>Una carpeta activa de cualquier organización, para el programador del Worker.</summary>
/// <param name="FolderId">Carpeta.</param>
/// <param name="OrganizationId">Organización dueña.</param>
public sealed record ActiveFolder(int FolderId, int OrganizationId);

/// <summary>Parámetros del trabajo de revisión de una carpeta.</summary>
/// <param name="FolderId">Carpeta a revisar.</param>
public sealed record FolderScanPayload(int FolderId);

/// <summary>Estimación de una tanda de análisis en lote, antes de ejecutarla.</summary>
/// <param name="Configured">Falso si falta la clave del proveedor.</param>
/// <param name="Model">Modelo que se usaría.</param>
/// <param name="Pending">Videos con escenas detectadas que nunca se analizaron.</param>
/// <param name="Videos">De esos, cuántos entran en esta tanda.</param>
/// <param name="Scenes">Escenas de los videos que entran.</param>
/// <param name="Inheriting">De los que entran, cuántos copian el análisis de su original sin costo.</param>
/// <param name="CostUsd">Costo estimado de la tanda en dólares.</param>
/// <param name="LimitUsd">Tope de la tanda: el menor entre el tope por lote y lo que queda del presupuesto mensual.</param>
/// <param name="OverVideoCap">Videos que quedan afuera porque superan el tope por video.</param>
/// <param name="OverBudget">Videos que quedan para otra tanda porque ésta ya llegó al tope.</param>
/// <param name="NotConfigured">Videos que no se pueden analizar porque falta la clave del proveedor.</param>
/// <param name="AutoEnabled">Si la organización tiene activado el análisis automático de videos nuevos.</param>
public sealed record BatchEstimate(
    bool Configured, string Model, int Pending, int Videos, int Scenes, int Inheriting, decimal CostUsd, decimal LimitUsd,
    int OverVideoCap, int OverBudget, int NotConfigured, bool AutoEnabled);

/// <summary>Resultado de pedir una tanda de análisis en lote.</summary>
/// <param name="Enqueued">Videos cuyo análisis quedó en la cola.</param>
/// <param name="EstimatedCostUsd">Costo estimado de lo encolado.</param>
/// <param name="Skipped">Videos pendientes que no entraron en esta tanda.</param>
public sealed record BatchResult(int Enqueued, decimal EstimatedCostUsd, int Skipped);

/// <summary>Activar o desactivar el análisis automático de videos nuevos.</summary>
public sealed class AutoAnalyzeBody
{
    /// <summary>Verdadero para activarlo.</summary>
    public bool Enabled { get; set; }
}

/// <summary>Un tramo narrativo: una función del anuncio que puede abarcar varias escenas.</summary>
/// <param name="SegmentId">Fragmento narrativo.</param>
/// <param name="StartMs">Inicio en milisegundos.</param>
/// <param name="EndMs">Fin en milisegundos.</param>
/// <param name="Function">Función que cumple el tramo.</param>
/// <param name="Summary">Qué pasa en el tramo.</param>
public sealed record NarrativeItem(long SegmentId, int StartMs, int EndMs, CreativeFunction? Function, string Summary);
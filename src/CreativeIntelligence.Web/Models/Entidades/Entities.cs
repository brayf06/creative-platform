using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Identity;

namespace CreativeIntelligence.Web.Models.Entidades;

/// <summary>
/// Marca una entidad que pertenece a una organización. El <c>AppDbContext</c> le aplica el filtro global
/// multiempresa y completa <see cref="OrganizationId"/> al insertarla.
/// </summary>
public interface IOrganizationScoped
{
    /// <summary>Organización dueña del registro. Nunca 0 en una fila persistida.</summary>
    int OrganizationId { get; set; }
}

/// <summary>Empresa cliente de la plataforma. Tabla <c>Organizations</c>. Es la raíz del aislamiento de datos.</summary>
public class Organization
{
    /// <summary>Identificador.</summary>
    public int Id { get; set; }

    /// <summary>Nombre visible, único en toda la plataforma. Hasta 150 caracteres.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Si es falso, sus miembros no pueden operar en ella.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Si es verdadero, los videos nuevos se analizan con IA solos, dentro de los topes de costo. Falso por
    /// defecto: el análisis cuesta dinero y lo activa un administrador.</summary>
    public bool AutoAnalyzeNewVideos { get; set; }

    /// <summary>Alta, en UTC. La fija la base.</summary>
    public DateTime CreatedAt { get; set; }
}

/// <summary>Rol de un usuario dentro de una organización. Se persiste como texto.</summary>
public enum OrganizationRole
{
    /// <summary>Sólo lectura.</summary>
    Viewer,

    /// <summary>Puede crear y modificar contenido y lanzar trabajos.</summary>
    Editor,

    /// <summary>Además administra miembros y ve la auditoría.</summary>
    Admin,
}

/// <summary>Usuario de la aplicación. Tabla <c>AspNetUsers</c>. No pertenece a una organización: se vincula por
/// <see cref="OrganizationMember"/>.</summary>
public class ApplicationUser : IdentityUser
{
    /// <summary>Nombre y apellido para mostrar. Hasta 150 caracteres.</summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>Última organización en la que trabajó; es la que se activa al iniciar sesión. Nulo si nunca eligió.</summary>
    public int? LastOrganizationId { get; set; }
}

/// <summary>Pertenencia de un usuario a una organización. Tabla <c>OrganizationMembers</c>. Único por
/// organización y usuario.</summary>
public class OrganizationMember : IOrganizationScoped
{
    /// <summary>Identificador.</summary>
    public int Id { get; set; }

    /// <inheritdoc />
    public int OrganizationId { get; set; }

    /// <summary>Identificador del usuario de Identity.</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>Rol dentro de la organización.</summary>
    public OrganizationRole Role { get; set; }

    /// <summary>Si es falso, el usuario no puede entrar a la organización.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Alta, en UTC. La fija la base.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Organización a la que pertenece.</summary>
    public Organization? Organization { get; set; }

    /// <summary>Usuario miembro.</summary>
    public ApplicationUser? User { get; set; }
}

/// <summary>Estado de un trabajo en segundo plano. Se persiste como texto y así viaja en JSON.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<BackgroundJobStatus>))]
public enum BackgroundJobStatus
{
    /// <summary>En cola, esperando un Worker (o la fecha de reintento).</summary>
    Pending,

    /// <summary>Tomado por un Worker con arriendo vigente.</summary>
    Running,

    /// <summary>Terminó bien.</summary>
    Succeeded,

    /// <summary>Agotó los intentos o no tiene manejador.</summary>
    Failed,

    /// <summary>Cancelado por un usuario.</summary>
    Cancelled,
}

/// <summary>
/// Trabajo en segundo plano. Tabla <c>BackgroundJobs</c>. La cola es esta tabla: un Worker lo toma cambiando
/// <see cref="Status"/> de <c>Pending</c> a <c>Running</c> con un arriendo; si el Worker muere, el arriendo vence
/// y otro lo retoma.
/// </summary>
public class BackgroundJob : IOrganizationScoped
{
    /// <summary>Identificador.</summary>
    public long Id { get; set; }

    /// <inheritdoc />
    public int OrganizationId { get; set; }

    /// <summary>Tipo de trabajo; selecciona el manejador. Hasta 100 caracteres.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Clave de idempotencia, única por organización: encolar dos veces la misma no duplica. Hasta 200.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>Estado actual.</summary>
    public BackgroundJobStatus Status { get; set; }

    /// <summary>Avance de 0 a 100.</summary>
    public int ProgressPercent { get; set; }

    /// <summary>Descripción del paso en curso. Hasta 300 caracteres. Nulo si aún no informó.</summary>
    public string? ProgressStep { get; set; }

    /// <summary>Veces que un Worker lo tomó.</summary>
    public int Attempts { get; set; }

    /// <summary>Tope de intentos antes de quedar <c>Failed</c>.</summary>
    public int MaxAttempts { get; set; }

    /// <summary>Worker que lo tiene tomado. Nulo si no está en curso.</summary>
    public string? LeaseOwner { get; set; }

    /// <summary>Vencimiento del arriendo, en UTC. Pasada esa hora otro Worker puede retomarlo.</summary>
    public DateTime? LeaseExpiresAt { get; set; }

    /// <summary>No se toma antes de esta hora (UTC). Se usa para la espera entre reintentos.</summary>
    public DateTime AvailableAt { get; set; }

    /// <summary>Un usuario pidió cancelarlo mientras corría; el Worker lo detiene en el próximo latido.</summary>
    public bool CancelRequested { get; set; }

    /// <summary>Mensaje del último error. Nulo si no falló.</summary>
    public string? Error { get; set; }

    /// <summary>Parámetros del trabajo en JSON; cada manejador define su forma.</summary>
    public string PayloadJson { get; set; } = "{}";

    /// <summary>Usuario que lo encoló. Nulo si lo creó el sistema.</summary>
    public string? CreatedByUserId { get; set; }

    /// <summary>Alta, en UTC. La fija la base.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Último cambio, en UTC, con hora de la base. Lo usa el notificador de progreso como marca de agua.</summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>Primera vez que un Worker lo tomó, en UTC.</summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>Cuándo llegó a un estado final, en UTC.</summary>
    public DateTime? FinishedAt { get; set; }
}

/// <summary>Registro de auditoría. Tabla <c>AuditEntries</c>. Sólo se inserta; nunca se modifica ni se borra.</summary>
public class AuditEntry : IOrganizationScoped
{
    /// <summary>Identificador.</summary>
    public long Id { get; set; }

    /// <inheritdoc />
    public int OrganizationId { get; set; }

    /// <summary>Usuario que hizo la acción. Nulo si fue el sistema.</summary>
    public string? UserId { get; set; }

    /// <summary>Nombre del usuario al momento de la acción. Hasta 256 caracteres.</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>Código estable de la acción, por ejemplo <c>job.cancel</c>. Hasta 100 caracteres.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Tipo de entidad afectada. Hasta 100 caracteres.</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>Identificador de la entidad afectada, como texto. Hasta 100 caracteres.</summary>
    public string EntityId { get; set; } = string.Empty;

    /// <summary>Descripción legible del cambio. Hasta 1000 caracteres.</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>Momento de la acción, en UTC. Lo fija la base.</summary>
    public DateTime CreatedAt { get; set; }
}

/// <summary>Una llamada a un proveedor de IA y lo que costó. Tabla <c>AiUsageRecords</c>. Sólo se inserta.</summary>
public class AiUsageRecord : IOrganizationScoped
{
    /// <summary>Identificador.</summary>
    public long Id { get; set; }

    /// <inheritdoc />
    public int OrganizationId { get; set; }

    /// <summary>Tarea para la que se usó el modelo, por ejemplo <c>segment.analysis</c>. Hasta 100 caracteres.</summary>
    public string Task { get; set; } = string.Empty;

    /// <summary>Proveedor. Hasta 100 caracteres.</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>Identificador del modelo. Hasta 100 caracteres.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Versión del prompt usado. Hasta 50 caracteres.</summary>
    public string PromptVersion { get; set; } = string.Empty;

    /// <summary>Tokens de entrada.</summary>
    public int InputTokens { get; set; }

    /// <summary>Tokens de salida.</summary>
    public int OutputTokens { get; set; }

    /// <summary>Costo en dólares, con 6 decimales.</summary>
    public decimal CostUsd { get; set; }

    /// <summary>Duración de la llamada en milisegundos.</summary>
    public int DurationMs { get; set; }

    /// <summary>Trabajo en segundo plano que hizo la llamada. Nulo si fue interactiva.</summary>
    public long? BackgroundJobId { get; set; }

    /// <summary>Momento de la llamada, en UTC. Lo fija la base.</summary>
    public DateTime CreatedAt { get; set; }
}

/// <summary>Tipo de archivo de la biblioteca. Se persiste como texto y así viaja en JSON.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MediaAssetKind>))]
public enum MediaAssetKind
{
    /// <summary>Video.</summary>
    Video,

    /// <summary>Imagen fija.</summary>
    Image,
}

/// <summary>Estado del análisis técnico de un archivo. Se persiste como texto y así viaja en JSON.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MediaProcessingStatus>))]
public enum MediaProcessingStatus
{
    /// <summary>Ingresado; falta leer sus propiedades y generar la miniatura.</summary>
    Pending,

    /// <summary>Propiedades leídas y miniatura generada.</summary>
    Ready,

    /// <summary>FFmpeg no pudo leerlo (archivo dañado o que no es lo que su extensión dice).</summary>
    Failed,
}

/// <summary>De dónde llegó un archivo a la biblioteca. Se persiste como texto.</summary>
public enum MediaSourceKind
{
    /// <summary>Carga manual de un usuario desde la pantalla de biblioteca.</summary>
    Upload,

    /// <summary>Ingreso automático desde una carpeta vigilada; la referencia es la ruta del archivo.</summary>
    WatchedFolder,
}

/// <summary>
/// Archivo original de la biblioteca (video o imagen). Tabla <c>MediaAssets</c>. El contenido se guarda una sola
/// vez por organización, direccionado por su SHA-256, y nunca se modifica: único por organización y hash.
/// </summary>
public class MediaAsset : IOrganizationScoped
{
    /// <summary>Identificador.</summary>
    public long Id { get; set; }

    /// <inheritdoc />
    public int OrganizationId { get; set; }

    /// <summary>Video o imagen; se deduce de la extensión.</summary>
    public MediaAssetKind Kind { get; set; }

    /// <summary>Nombre del archivo tal como se cargó, sin ruta. Hasta 260 caracteres.</summary>
    public string OriginalFileName { get; set; } = string.Empty;

    /// <summary>Tipo MIME, deducido de la extensión (no se confía en el que envía el navegador).</summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>Tamaño en bytes.</summary>
    public long SizeBytes { get; set; }

    /// <summary>SHA-256 del contenido, 64 caracteres hexadecimales en minúscula.</summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>Ruta del original relativa a la raíz del almacenamiento.</summary>
    public string StoragePath { get; set; } = string.Empty;

    /// <summary>Procedencia: cómo llegó el archivo.</summary>
    public MediaSourceKind SourceKind { get; set; }

    /// <summary>Referencia de la procedencia (carpeta, URL autorizada…). Nulo para una carga manual.</summary>
    public string? SourceReference { get; set; }

    /// <summary>Titular de los derechos. Hasta 200 caracteres. Nulo si no se informó.</summary>
    public string? RightsHolder { get; set; }

    /// <summary>Licencia, alcance y restricciones (personas, voces, música). Hasta 1000 caracteres.</summary>
    public string? LicenseNotes { get; set; }

    /// <summary>Si el material puede reutilizarse en nuevas creatividades. Falso por defecto: sin autorización
    /// explícita un archivo se puede ver y buscar, pero no entra en una receta.</summary>
    public bool ReuseAuthorized { get; set; }

    /// <summary>Último día de vigencia de la autorización. Nulo si no vence.</summary>
    public DateOnly? RightsExpireOn { get; set; }

    /// <summary>Estado del análisis técnico (propiedades y miniatura).</summary>
    public MediaProcessingStatus ProcessingStatus { get; set; }

    /// <summary>Por qué falló el análisis técnico. Nulo si no falló. Hasta 1000 caracteres.</summary>
    public string? ProcessingError { get; set; }

    /// <summary>Duración en milisegundos. Nulo para imágenes o si aún no se analizó.</summary>
    public int? DurationMs { get; set; }

    /// <summary>Ancho en píxeles tal como se ve (ya aplicada la rotación). Nulo si aún no se analizó.</summary>
    public int? Width { get; set; }

    /// <summary>Alto en píxeles tal como se ve (ya aplicada la rotación). Nulo si aún no se analizó.</summary>
    public int? Height { get; set; }

    /// <summary>Cuadros por segundo, con 3 decimales. Nulo para imágenes o si no se pudo determinar.</summary>
    public decimal? FrameRate { get; set; }

    /// <summary>Códec de video o de imagen (h264, vp8, png…). Nulo si aún no se analizó.</summary>
    public string? VideoCodec { get; set; }

    /// <summary>Códec de audio. Nulo si no tiene audio o aún no se analizó.</summary>
    public string? AudioCodec { get; set; }

    /// <summary>Contenedor informado por ffprobe (por ejemplo <c>mov,mp4,m4a,3gp,3g2,mj2</c>).</summary>
    public string? Container { get; set; }

    /// <summary>Ruta de la miniatura relativa a la raíz del almacenamiento. Nulo si aún no se generó.</summary>
    public string? ThumbnailPath { get; set; }

    /// <summary>Huella visual: 16 hexadecimales por cuadro muestreado (cinco para un video, uno para una imagen).
    /// Dos archivos con la misma imagen dan huellas casi iguales aunque sus bytes sean distintos. Nulo si aún no
    /// se analizó.</summary>
    public string? PerceptualHash { get; set; }

    /// <summary>Archivo anterior del que éste es un duplicado visual (misma imagen, otra codificación). Nulo si
    /// no se encontró ninguno.</summary>
    public long? DuplicateOfAssetId { get; set; }

    /// <summary>Estado de la detección de escenas. Nulo si no corresponde (imágenes) o todavía no se pidió.</summary>
    public MediaProcessingStatus? SceneStatus { get; set; }

    /// <summary>Por qué falló la detección de escenas. Nulo si no falló. Hasta 1000 caracteres.</summary>
    public string? SceneError { get; set; }

    /// <summary>Estado del análisis con IA de las escenas. Nulo si nunca se pidió: no es automático porque cuesta.</summary>
    public MediaProcessingStatus? AnalysisStatus { get; set; }

    /// <summary>Por qué falló el análisis con IA. Nulo si no falló. Hasta 1000 caracteres.</summary>
    public string? AnalysisError { get; set; }

    /// <summary>Estado de la transcripción. Nulo si no corresponde (imágenes, videos sin audio) o no se pidió.</summary>
    public MediaProcessingStatus? TranscriptStatus { get; set; }

    /// <summary>Por qué falló la transcripción. Nulo si no falló. Hasta 1000 caracteres.</summary>
    public string? TranscriptError { get; set; }

    /// <summary>Idioma detectado o indicado (código de dos letras). Nulo si no se transcribió.</summary>
    public string? TranscriptLanguage { get; set; }

    /// <summary>Modelo que produjo la transcripción vigente, por ejemplo <c>faster-whisper:small</c>.</summary>
    public string? TranscriptModel { get; set; }

    /// <summary>Se incrementa cada vez que cambian los límites de las escenas del video. Es la guarda de
    /// concurrencia de la edición: dos cambios simultáneos sobre el mismo video no pueden aplicarse los dos.</summary>
    public int SegmentsVersion { get; set; }

    /// <summary>Usuario que lo cargó. Nulo si lo ingresó el sistema.</summary>
    public string? CreatedByUserId { get; set; }

    /// <summary>Alta, en UTC. La fija la base.</summary>
    public DateTime CreatedAt { get; set; }
}

/// <summary>Clase de fragmento de un video. Se persiste como texto y así viaja en JSON.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MediaSegmentKind>))]
public enum MediaSegmentKind
{
    /// <summary>Tramo entre dos cortes de imagen (un plano).</summary>
    Visual,

    /// <summary>Tramo con una función narrativa (hook, demostración, CTA…); puede abarcar varios visuales.</summary>
    Narrative,
}

/// <summary>Cómo se creó un fragmento. Se persiste como texto y así viaja en JSON.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MediaSegmentOrigin>))]
public enum MediaSegmentOrigin
{
    /// <summary>Lo detectó el sistema.</summary>
    Detected,

    /// <summary>Lo creó o corrigió una persona; un reanálisis no lo pisa.</summary>
    Manual,
}

/// <summary>
/// Fragmento de un video: un tramo de tiempo dentro de un <see cref="MediaAsset"/>. Tabla <c>MediaSegments</c>.
/// Los fragmentos visuales de un video son contiguos y no se superponen; los tiempos van en milisegundos enteros.
/// </summary>
public class MediaSegment : IOrganizationScoped
{
    /// <summary>Identificador.</summary>
    public long Id { get; set; }

    /// <inheritdoc />
    public int OrganizationId { get; set; }

    /// <summary>Video al que pertenece.</summary>
    public long MediaAssetId { get; set; }

    /// <summary>Visual o narrativo.</summary>
    public MediaSegmentKind Kind { get; set; }

    /// <summary>Cómo se creó.</summary>
    public MediaSegmentOrigin Origin { get; set; }

    /// <summary>Inicio en milisegundos desde el comienzo del video, inclusive.</summary>
    public int StartMs { get; set; }

    /// <summary>Fin en milisegundos desde el comienzo del video, exclusive. Siempre mayor que el inicio.</summary>
    public int EndMs { get; set; }

    /// <summary>Ruta de la miniatura (cuadro del medio del tramo) relativa al almacenamiento. Nulo si no se generó
    /// o si el tramo cambió y hay que regenerarla.</summary>
    public string? ThumbnailPath { get; set; }

    /// <summary>Volumen medio del audio del tramo, en dB respecto del máximo digital (siempre negativo o cero).
    /// Nulo si el video no tiene audio, o si el tramo cambió y hay que volver a medirlo.</summary>
    public decimal? AudioMeanDb { get; set; }

    /// <summary>Pico de volumen del tramo, en dB respecto del máximo digital. Cerca de 0 indica saturación.
    /// Nulo en los mismos casos que <see cref="AudioMeanDb"/>.</summary>
    public decimal? AudioPeakDb { get; set; }

    /// <summary>Si el fragmento está elegido para usarse en nuevas creatividades. Sólo puede ser verdadero si el
    /// video tiene autorización de reutilización vigente.</summary>
    public bool SelectedForProduction { get; set; }

    /// <summary>Alta, en UTC. La fija la base.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Video al que pertenece.</summary>
    public MediaAsset? MediaAsset { get; set; }

    /// <summary>Etiquetas del fragmento.</summary>
    public List<MediaSegmentTag> Tags { get; set; } = [];
}

/// <summary>Etiqueta de un fragmento. Tabla <c>MediaSegmentTags</c>. Única por fragmento y texto.</summary>
public class MediaSegmentTag : IOrganizationScoped
{
    /// <summary>Identificador.</summary>
    public long Id { get; set; }

    /// <inheritdoc />
    public int OrganizationId { get; set; }

    /// <summary>Fragmento etiquetado.</summary>
    public long MediaSegmentId { get; set; }

    /// <summary>Texto de la etiqueta, sin espacios al borde. Hasta 50 caracteres.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Si la puso una persona o el análisis automático.</summary>
    public MediaSegmentOrigin Origin { get; set; }
}

/// <summary>
/// Frase de la transcripción de un video, con sus tiempos. Tabla <c>MediaTranscriptLines</c>. Pertenece al video,
/// no a una escena: el texto de cada escena se deriva por tiempo, así sigue siendo correcto si los cortes cambian.
/// </summary>
public class MediaTranscriptLine : IOrganizationScoped
{
    /// <summary>Identificador.</summary>
    public long Id { get; set; }

    /// <inheritdoc />
    public int OrganizationId { get; set; }

    /// <summary>Video transcripto.</summary>
    public long MediaAssetId { get; set; }

    /// <summary>Inicio de la frase en milisegundos desde el comienzo del video.</summary>
    public int StartMs { get; set; }

    /// <summary>Fin de la frase en milisegundos desde el comienzo del video.</summary>
    public int EndMs { get; set; }

    /// <summary>Texto de la frase. Hasta 1000 caracteres.</summary>
    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// Función que cumple un fragmento dentro de un anuncio: la taxonomía de componentes creativos. Un fragmento puede
/// cumplir varias. Se persiste como texto y así viaja en JSON.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<CreativeFunction>))]
public enum CreativeFunction
{
    /// <summary>Gancho inicial que detiene el scroll.</summary>
    Hook,

    /// <summary>Plantea el problema o la necesidad.</summary>
    Problem,

    /// <summary>Presenta el producto o la marca.</summary>
    Presentation,

    /// <summary>Muestra el producto en uso.</summary>
    Demonstration,

    /// <summary>Muestra o enuncia un beneficio o resultado.</summary>
    Benefit,

    /// <summary>Testimonio, reseña u otra prueba social.</summary>
    SocialProof,

    /// <summary>Responde una objeción.</summary>
    Objection,

    /// <summary>Compara con una alternativa o con el antes.</summary>
    Comparison,

    /// <summary>Precio, promoción u oferta.</summary>
    Offer,

    /// <summary>Llamado a la acción.</summary>
    Cta,

    /// <summary>Imagen de apoyo sin mensaje propio.</summary>
    Broll,

    /// <summary>Cierre: logo, firma o placa final.</summary>
    Closing,
}

/// <summary>
/// Documentación de un fragmento producida por un modelo de IA. Tabla <c>SegmentAnalyses</c>. Se versiona: un
/// análisis nuevo no pisa al anterior, lo deja con <see cref="IsCurrent"/> en falso. Guarda el tramo que se
/// analizó: si el fragmento cambió después, el análisis está desactualizado.
/// </summary>
public class SegmentAnalysis : IOrganizationScoped
{
    /// <summary>Identificador.</summary>
    public long Id { get; set; }

    /// <inheritdoc />
    public int OrganizationId { get; set; }

    /// <summary>Fragmento analizado.</summary>
    public long MediaSegmentId { get; set; }

    /// <summary>Inicio del tramo que se analizó, en milisegundos.</summary>
    public int StartMs { get; set; }

    /// <summary>Fin del tramo que se analizó, en milisegundos.</summary>
    public int EndMs { get; set; }

    /// <summary>Qué se ve. Hasta 1000 caracteres.</summary>
    public string VisualDescription { get; set; } = string.Empty;

    /// <summary>Texto sobreimpreso en pantalla; vacío si no hay. Hasta 500 caracteres.</summary>
    public string OnScreenText { get; set; } = string.Empty;

    /// <summary>Producto que aparece; vacío si no se identifica. Hasta 200 caracteres.</summary>
    public string Product { get; set; } = string.Empty;

    /// <summary>Acciones que se observan. Hasta 500 caracteres.</summary>
    public string Actions { get; set; } = string.Empty;

    /// <summary>Enfoque comercial (practicidad, ahorro, emoción…). Hasta 300 caracteres.</summary>
    public string CommercialAngle { get; set; } = string.Empty;

    /// <summary>Mensaje principal del fragmento. Hasta 500 caracteres.</summary>
    public string MainMessage { get; set; } = string.Empty;

    /// <summary>Público al que parece dirigirse. Hasta 300 caracteres.</summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>Calidad visual de 1 (mala) a 5 (excelente), según el modelo.</summary>
    public int VisualQuality { get; set; }

    /// <summary>Confianza del modelo en su clasificación, de 0 a 1. No es una medida de rendimiento.</summary>
    public decimal Confidence { get; set; }

    /// <summary>Proveedor de IA. Hasta 50 caracteres.</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>Modelo usado. Hasta 100 caracteres.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Versión del prompt usado. Hasta 50 caracteres.</summary>
    public string PromptVersion { get; set; } = string.Empty;

    /// <summary>Verdadero para el análisis vigente del fragmento; hay a lo sumo uno.</summary>
    public bool IsCurrent { get; set; }

    /// <summary>Si este análisis no salió de un modelo sino que se copió del de un duplicado visual, el análisis
    /// de origen. Es sólo una referencia para la trazabilidad (sin clave foránea): el análisis de origen puede
    /// dejar de existir y éste sigue siendo válido. Nulo si lo produjo un modelo.</summary>
    public long? InheritedFromAnalysisId { get; set; }

    /// <summary>Alta, en UTC. La fija la base.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Fragmento analizado.</summary>
    public MediaSegment? MediaSegment { get; set; }

    /// <summary>Funciones narrativas asignadas.</summary>
    public List<SegmentAnalysisFunction> Functions { get; set; } = [];
}

/// <summary>Una función narrativa asignada por un análisis. Tabla <c>SegmentAnalysisFunctions</c>.</summary>
public class SegmentAnalysisFunction : IOrganizationScoped
{
    /// <summary>Identificador.</summary>
    public long Id { get; set; }

    /// <inheritdoc />
    public int OrganizationId { get; set; }

    /// <summary>Análisis al que pertenece.</summary>
    public long SegmentAnalysisId { get; set; }

    /// <summary>Función narrativa.</summary>
    public CreativeFunction Function { get; set; }
}

/// <summary>
/// Resultado guardado de una llamada a un modelo de IA. Tabla <c>AiResultCaches</c>. La clave resume la entrada,
/// el modelo y la versión del prompt: la misma clave nunca se vuelve a pagar.
/// </summary>
public class AiResultCache : IOrganizationScoped
{
    /// <summary>Identificador.</summary>
    public long Id { get; set; }

    /// <inheritdoc />
    public int OrganizationId { get; set; }

    /// <summary>SHA-256 de la entrada, el modelo y la versión del prompt. 64 hexadecimales. Única por organización.</summary>
    public string CacheKey { get; set; } = string.Empty;

    /// <summary>Resultado en JSON, con la forma que define quien lo guardó.</summary>
    public string ResultJson { get; set; } = string.Empty;

    /// <summary>Alta, en UTC. La fija la base.</summary>
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Vector de un fragmento para la búsqueda por significado. Tabla <c>SegmentEmbeddings</c>. Uno por fragmento y
/// modelo. Guarda el hash del texto que se indexó: si el texto del fragmento cambia, hay que volver a indexarlo.
/// </summary>
public class SegmentEmbedding : IOrganizationScoped
{
    /// <summary>Identificador.</summary>
    public long Id { get; set; }

    /// <inheritdoc />
    public int OrganizationId { get; set; }

    /// <summary>Fragmento indexado.</summary>
    public long MediaSegmentId { get; set; }

    /// <summary>Modelo de embeddings. Los vectores de modelos distintos no son comparables. Hasta 100 caracteres.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Cantidad de componentes del vector.</summary>
    public int Dimensions { get; set; }

    /// <summary>Vector normalizado, como float de 32 bits (cuatro bytes por componente).</summary>
    public byte[] Vector { get; set; } = [];

    /// <summary>SHA-256 del texto indexado, 64 hexadecimales.</summary>
    public string TextHash { get; set; } = string.Empty;

    /// <summary>Alta, en UTC. La fija la base.</summary>
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Carpeta del servidor que se revisa periódicamente para ingresar sola los archivos nuevos. Tabla
/// <c>WatchedFolders</c>. Única por organización y ruta. Lleva los derechos que reciben los archivos que entran
/// por ella.
/// </summary>
public class WatchedFolder : IOrganizationScoped
{
    /// <summary>Identificador.</summary>
    public int Id { get; set; }

    /// <inheritdoc />
    public int OrganizationId { get; set; }

    /// <summary>Ruta completa en el servidor, dentro de una raíz permitida. Hasta 500 caracteres.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Si el Worker la revisa periódicamente. Pausada, sólo se revisa a pedido.</summary>
    public bool IsActive { get; set; }

    /// <summary>Titular de los derechos que se asigna a sus archivos. Hasta 200 caracteres.</summary>
    public string? RightsHolder { get; set; }

    /// <summary>Licencia y restricciones que se asignan a sus archivos. Hasta 1000 caracteres.</summary>
    public string? LicenseNotes { get; set; }

    /// <summary>Si sus archivos quedan autorizados para reutilizar.</summary>
    public bool ReuseAuthorized { get; set; }

    /// <summary>Vigencia de la autorización que se asigna a sus archivos. Nulo si no vence.</summary>
    public DateOnly? RightsExpireOn { get; set; }

    /// <summary>Última revisión, en UTC con hora de la base. Nulo si nunca se revisó.</summary>
    public DateTime? LastScanAt { get; set; }

    /// <summary>Qué encontró la última revisión. Hasta 500 caracteres.</summary>
    public string? LastScanSummary { get; set; }

    /// <summary>Usuario que la agregó.</summary>
    public string? CreatedByUserId { get; set; }

    /// <summary>Alta, en UTC. La fija la base.</summary>
    public DateTime CreatedAt { get; set; }
}

/// <summary>Qué pasó con un archivo de una carpeta vigilada. Se persiste como texto.</summary>
public enum WatchedFileOutcome
{
    /// <summary>Se ingresó como archivo nuevo de la biblioteca.</summary>
    Ingested,

    /// <summary>Su contenido ya estaba en la biblioteca.</summary>
    Duplicate,

    /// <summary>La biblioteca lo rechazó (por ejemplo, vacío).</summary>
    Rejected,
}

/// <summary>
/// Archivo ya visto en una carpeta vigilada. Tabla <c>WatchedFiles</c>. Único por carpeta y ruta relativa. Guarda
/// tamaño y fecha de modificación: si no cambiaron, la próxima revisión no lo vuelve a leer.
/// </summary>
public class WatchedFile : IOrganizationScoped
{
    /// <summary>Identificador.</summary>
    public long Id { get; set; }

    /// <inheritdoc />
    public int OrganizationId { get; set; }

    /// <summary>Carpeta vigilada.</summary>
    public int WatchedFolderId { get; set; }

    /// <summary>Ruta relativa a la carpeta vigilada. Hasta 400 caracteres.</summary>
    public string RelativePath { get; set; } = string.Empty;

    /// <summary>Tamaño en bytes cuando se lo vio.</summary>
    public long SizeBytes { get; set; }

    /// <summary>Fecha de modificación cuando se lo vio, UTC.</summary>
    public DateTime LastWriteUtc { get; set; }

    /// <summary>Qué pasó con él.</summary>
    public WatchedFileOutcome Outcome { get; set; }

    /// <summary>Archivo de la biblioteca al que corresponde. Nulo si fue rechazado.</summary>
    public long? MediaAssetId { get; set; }

    /// <summary>Motivo del rechazo. Nulo si no fue rechazado. Hasta 500 caracteres.</summary>
    public string? Message { get; set; }
}

/// <summary>
/// Receta de una creatividad: qué tramos de qué videos se unen, en qué orden y con qué formato de salida. Tabla
/// <c>CreativeRecipes</c>. Es inmutable una vez creada: para cambiarla se crea otra, así cada video generado
/// conserva exactamente el montaje del que salió y volver a renderizarla produce lo mismo.
/// </summary>
public class CreativeRecipe : IOrganizationScoped
{
    /// <summary>Identificador.</summary>
    public long Id { get; set; }

    /// <inheritdoc />
    public int OrganizationId { get; set; }

    /// <summary>Nombre con el que se la reconoce. Hasta 200 caracteres.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Concepto comercial que desarrolla. Hasta 1000 caracteres.</summary>
    public string? Concept { get; set; }

    /// <summary>Hipótesis que se quiere probar con esta creatividad. Hasta 1000 caracteres.</summary>
    public string? Hypothesis { get; set; }

    /// <summary>Ancho del video de salida, en píxeles.</summary>
    public int Width { get; set; }

    /// <summary>Alto del video de salida, en píxeles.</summary>
    public int Height { get; set; }

    /// <summary>Lote que la generó; nulo si la armó una persona.</summary>
    public long? CreativeBatchId { get; set; }

    /// <summary>Si el video lleva quemado lo que se dice en cada tramo, tomado de la transcripción del original.</summary>
    public bool Subtitles { get; set; }

    /// <summary>Cómo se pasa de un tramo al siguiente.</summary>
    public RecipeTransition Transition { get; set; }

    /// <summary>Música de fondo; nulo si no lleva.</summary>
    public long? MusicTrackId { get; set; }

    /// <summary>Volumen de la música respecto de su nivel original, de 5 a 100.</summary>
    public int MusicVolumePercent { get; set; }

    /// <summary>Textos que se muestran sobre el video.</summary>
    public List<RecipeOverlay> Overlays { get; set; } = [];

    /// <summary>Usuario que la creó.</summary>
    public string? CreatedByUserId { get; set; }

    /// <summary>Alta, en UTC. La fija la base.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Tramos que la componen, en orden.</summary>
    public List<RecipeItem> Items { get; set; } = [];
}

/// <summary>
/// Un tramo dentro de una receta. Tabla <c>RecipeItems</c>. Es el corazón del linaje: une cada video generado con
/// el video original y el tramo exacto que usó. Guarda el tramo copiado (no sólo el fragmento) porque los
/// fragmentos se editan y se vuelven a detectar, y la receta tiene que seguir diciendo lo mismo.
/// </summary>
public class RecipeItem : IOrganizationScoped
{
    /// <summary>Identificador.</summary>
    public long Id { get; set; }

    /// <inheritdoc />
    public int OrganizationId { get; set; }

    /// <summary>Receta a la que pertenece.</summary>
    public long CreativeRecipeId { get; set; }

    /// <summary>Lugar en el montaje, desde 1.</summary>
    public int Position { get; set; }

    /// <summary>Video original del que sale el tramo.</summary>
    public long MediaAssetId { get; set; }

    /// <summary>Video original.</summary>
    public MediaAsset? MediaAsset { get; set; }

    /// <summary>Fragmento de la biblioteca del que se tomó; nulo si ese fragmento ya no existe (se editó o se
    /// volvieron a detectar las escenas).</summary>
    public long? MediaSegmentId { get; set; }

    /// <summary>Inicio del tramo en el original, en milisegundos.</summary>
    public int StartMs { get; set; }

    /// <summary>Fin del tramo en el original, en milisegundos.</summary>
    public int EndMs { get; set; }

    /// <summary>Función que cumple en el anuncio; nulo si no se indicó.</summary>
    public CreativeFunction? Role { get; set; }
}

/// <summary>
/// Un video generado a partir de una receta. Tabla <c>CreativeVariants</c>. Cada pedido de render crea uno:
/// renderizar dos veces la misma receta da dos variantes con el mismo montaje.
/// </summary>
public class CreativeVariant : IOrganizationScoped
{
    /// <summary>Identificador.</summary>
    public long Id { get; set; }

    /// <inheritdoc />
    public int OrganizationId { get; set; }

    /// <summary>Receta de la que sale.</summary>
    public long CreativeRecipeId { get; set; }

    /// <summary>Estado del render.</summary>
    public MediaProcessingStatus Status { get; set; }

    /// <summary>Por qué falló. Hasta 1000 caracteres.</summary>
    public string? Error { get; set; }

    /// <summary>Ruta del archivo generado, relativa al almacenamiento; nulo hasta que termina.</summary>
    public string? StoragePath { get; set; }

    /// <summary>Duración del archivo generado, en milisegundos.</summary>
    public int? DurationMs { get; set; }

    /// <summary>Ancho del archivo generado.</summary>
    public int? Width { get; set; }

    /// <summary>Alto del archivo generado.</summary>
    public int? Height { get; set; }

    /// <summary>Tamaño del archivo generado, en bytes.</summary>
    public long? SizeBytes { get; set; }

    /// <summary>Cuánto tardó el render, en milisegundos. Es el costo de cómputo del video: el render es local.</summary>
    public int? RenderMs { get; set; }

    /// <summary>Usuario que pidió el render.</summary>
    public string? CreatedByUserId { get; set; }

    /// <summary>Pedido, en UTC. Lo fija la base.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Fin del render, en UTC con hora de la base.</summary>
    public DateTime? FinishedAt { get; set; }

    /// <summary>Controles de calidad del archivo.</summary>
    public List<QualityCheck> Checks { get; set; } = [];
}

/// <summary>Resultado de un control de calidad automático sobre un video generado. Tabla <c>QualityChecks</c>.</summary>
public class QualityCheck : IOrganizationScoped
{
    /// <summary>Identificador.</summary>
    public long Id { get; set; }

    /// <inheritdoc />
    public int OrganizationId { get; set; }

    /// <summary>Video controlado.</summary>
    public long CreativeVariantId { get; set; }

    /// <summary>Regla aplicada: <c>duration</c>, <c>resolution</c> o <c>audio</c>. Hasta 50 caracteres.</summary>
    public string Rule { get; set; } = string.Empty;

    /// <summary>Si la cumple.</summary>
    public bool Passed { get; set; }

    /// <summary>Qué se esperaba y qué se encontró. Hasta 500 caracteres.</summary>
    public string Detail { get; set; } = string.Empty;
}

/// <summary>
/// Lote de creatividades: un pedido de varias variantes generadas a partir de un mismo conjunto de fragmentos.
/// Tabla <c>CreativeBatches</c>. Guarda con qué parámetros se generó (cantidad, duración máxima, umbral de
/// similitud y semilla); sus recetas y videos cuelgan de <see cref="CreativeRecipe.CreativeBatchId"/>. El estado y
/// el avance no se guardan: se deducen de sus videos.
/// </summary>
public class CreativeBatch : IOrganizationScoped
{
    /// <summary>Identificador.</summary>
    public long Id { get; set; }

    /// <inheritdoc />
    public int OrganizationId { get; set; }

    /// <summary>Nombre del lote. Hasta 200 caracteres.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Brief: qué se quiere lograr con el lote. Hasta 2000 caracteres.</summary>
    public string? Brief { get; set; }

    /// <summary>Ancho de salida de sus videos.</summary>
    public int Width { get; set; }

    /// <summary>Alto de salida de sus videos.</summary>
    public int Height { get; set; }

    /// <summary>Cantidad de variantes pedida, de 5 a 50.</summary>
    public int RequestedCount { get; set; }

    /// <summary>Duración máxima de cada variante, en milisegundos.</summary>
    public int MaxDurationMs { get; set; }

    /// <summary>Similitud (0 a 1) a partir de la cual una variante se descartó por parecerse a otra.</summary>
    public decimal SimilarityThreshold { get; set; }

    /// <summary>Semilla del generador: con los mismos fragmentos y parámetros reproduce las mismas variantes.</summary>
    public int Seed { get; set; }

    /// <summary>Candidatos que el generador descartó por parecidos.</summary>
    public int DiscardedBySimilarity { get; set; }

    /// <summary>Usuario que lo pidió.</summary>
    public string? CreatedByUserId { get; set; }

    /// <summary>Alta, en UTC. La fija la base.</summary>
    public DateTime CreatedAt { get; set; }
}

/// <summary>Cómo se pasa de un tramo al siguiente en un montaje. Se persiste como texto.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RecipeTransition>))]
public enum RecipeTransition
{
    /// <summary>Corte directo.</summary>
    Cut,

    /// <summary>Fundido a negro: cada tramo termina oscureciéndose y el siguiente empieza aclarándose. No cambia
    /// la duración del montaje.</summary>
    FadeBlack,
}

/// <summary>Dónde va un texto sobre el video. Se persiste como texto.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<OverlayPlacement>))]
public enum OverlayPlacement
{
    /// <summary>Arriba.</summary>
    Top,

    /// <summary>En el centro.</summary>
    Center,

    /// <summary>Abajo, por encima de los subtítulos.</summary>
    Bottom,
}

/// <summary>Cuándo se muestra un texto sobre el video. Se persiste como texto.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<OverlayMoment>))]
public enum OverlayMoment
{
    /// <summary>Durante todo el video.</summary>
    Whole,

    /// <summary>En los primeros segundos.</summary>
    Opening,

    /// <summary>En los últimos segundos.</summary>
    Closing,
}

/// <summary>Un texto que se muestra sobre el video de una receta. Tabla <c>RecipeOverlays</c>. El momento es
/// relativo (todo, apertura o cierre) para que sirva igual en variantes de distinta duración.</summary>
public class RecipeOverlay : IOrganizationScoped
{
    /// <summary>Identificador.</summary>
    public long Id { get; set; }

    /// <inheritdoc />
    public int OrganizationId { get; set; }

    /// <summary>Receta a la que pertenece.</summary>
    public long CreativeRecipeId { get; set; }

    /// <summary>Orden dentro de la receta, desde 1.</summary>
    public int Position { get; set; }

    /// <summary>Texto. Hasta 120 caracteres.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Dónde va.</summary>
    public OverlayPlacement Placement { get; set; }

    /// <summary>Cuándo se muestra.</summary>
    public OverlayMoment Moment { get; set; }
}

/// <summary>
/// Una pista de música de la organización. Tabla <c>MusicTracks</c>. Lleva sus propios derechos: sólo una pista con
/// autorización vigente puede entrar en una receta. El archivo se guarda igual que un original: por el hash de su
/// contenido y sin modificarlo.
/// </summary>
public class MusicTrack : IOrganizationScoped
{
    /// <summary>Identificador.</summary>
    public long Id { get; set; }

    /// <inheritdoc />
    public int OrganizationId { get; set; }

    /// <summary>Nombre con el que se la reconoce. Hasta 200 caracteres.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Nombre del archivo cargado. Hasta 260 caracteres.</summary>
    public string OriginalFileName { get; set; } = string.Empty;

    /// <summary>SHA-256 del contenido, en hexadecimal. Único por organización.</summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>Ruta relativa al almacenamiento.</summary>
    public string StoragePath { get; set; } = string.Empty;

    /// <summary>Tamaño en bytes.</summary>
    public long SizeBytes { get; set; }

    /// <summary>Duración en milisegundos; nulo hasta que se analiza.</summary>
    public int? DurationMs { get; set; }

    /// <summary>Estado del análisis del archivo.</summary>
    public MediaProcessingStatus Status { get; set; }

    /// <summary>Por qué no se pudo leer. Hasta 500 caracteres.</summary>
    public string? Error { get; set; }

    /// <summary>Titular de los derechos. Hasta 200 caracteres.</summary>
    public string? RightsHolder { get; set; }

    /// <summary>Licencia y restricciones. Hasta 1000 caracteres.</summary>
    public string? LicenseNotes { get; set; }

    /// <summary>Si está autorizada para usarse en creatividades.</summary>
    public bool ReuseAuthorized { get; set; }

    /// <summary>Vigencia de la autorización; nulo si no vence.</summary>
    public DateOnly? RightsExpireOn { get; set; }

    /// <summary>Usuario que la cargó.</summary>
    public string? CreatedByUserId { get; set; }

    /// <summary>Alta, en UTC. La fija la base.</summary>
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Perfil de una marca de la organización. Tabla <c>Brands</c>. Reúne lo que se reutiliza en todas sus campañas:
/// propuesta de valor, estilo, identidad, objetivos, restricciones y presupuesto. Sus listas (productos,
/// beneficios, públicos, objeciones y mensajes permitidos y prohibidos) están en <see cref="BrandItem"/>.
/// </summary>
public class Brand : IOrganizationScoped
{
    /// <summary>Identificador.</summary>
    public int Id { get; set; }

    /// <inheritdoc />
    public int OrganizationId { get; set; }

    /// <summary>Nombre, único en la organización. Hasta 150 caracteres.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Propuesta de valor. Hasta 1000 caracteres.</summary>
    public string? ValueProposition { get; set; }

    /// <summary>Estilo de comunicación: tono, trato, vocabulario. Hasta 1000 caracteres.</summary>
    public string? CommunicationStyle { get; set; }

    /// <summary>Identidad visual: colores, tipografías, estética. Hasta 1000 caracteres.</summary>
    public string? VisualIdentity { get; set; }

    /// <summary>Objetivos comerciales. Hasta 1000 caracteres.</summary>
    public string? Objectives { get; set; }

    /// <summary>Restricciones comerciales (descuentos, plazos, zonas…). Hasta 1000 caracteres.</summary>
    public string? CommercialRestrictions { get; set; }

    /// <summary>Presupuesto publicitario mensual de referencia, en dólares.</summary>
    public decimal? MonthlyBudgetUsd { get; set; }

    /// <summary>Usuario que la creó.</summary>
    public string? CreatedByUserId { get; set; }

    /// <summary>Alta, en UTC. La fija la base.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Productos, beneficios, públicos, objeciones y mensajes de la marca.</summary>
    public List<BrandItem> Items { get; set; } = [];
}

/// <summary>Qué es un elemento del perfil de una marca. Se persiste como texto.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<BrandItemKind>))]
public enum BrandItemKind
{
    /// <summary>Un producto, con su margen.</summary>
    Product,

    /// <summary>Un beneficio verificable; el detalle dice cómo se verifica.</summary>
    Benefit,

    /// <summary>Un público objetivo.</summary>
    Audience,

    /// <summary>Una objeción habitual de los clientes; el detalle dice cómo se responde.</summary>
    Objection,

    /// <summary>Un mensaje que la marca permite o quiere decir.</summary>
    AllowedMessage,

    /// <summary>Una frase o promesa que la marca no puede decir.</summary>
    ForbiddenMessage,
}

/// <summary>
/// Un elemento de una de las listas del perfil de una marca. Tabla <c>BrandItems</c>. Todas las listas comparten
/// forma (un texto y un detalle), así que van en una sola tabla con su clase en <see cref="Kind"/>.
/// </summary>
public class BrandItem : IOrganizationScoped
{
    /// <summary>Identificador.</summary>
    public int Id { get; set; }

    /// <inheritdoc />
    public int OrganizationId { get; set; }

    /// <summary>Marca a la que pertenece.</summary>
    public int BrandId { get; set; }

    /// <summary>Qué es.</summary>
    public BrandItemKind Kind { get; set; }

    /// <summary>Nombre del producto, beneficio, público, objeción o frase. Hasta 300 caracteres; no se repite
    /// dentro de la misma lista de la marca.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Detalle: descripción, cómo se verifica un beneficio, cómo se responde una objeción. Hasta 1000
    /// caracteres.</summary>
    public string? Detail { get; set; }

    /// <summary>Margen del producto, en porcentaje; sólo para productos.</summary>
    public decimal? MarginPercent { get; set; }
}

/// <summary>Dónde se va a publicar una campaña. Se persiste como texto.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<BriefPlatform>))]
public enum BriefPlatform
{
    /// <summary>Reels de Meta (vertical 9:16).</summary>
    MetaReels,

    /// <summary>Historias de Meta (vertical 9:16).</summary>
    MetaStories,

    /// <summary>Feed de Meta (4:5 o 1:1).</summary>
    MetaFeed,

    /// <summary>Otra plataforma.</summary>
    Other,
}

/// <summary>Cuánto puede hacer el sistema por su cuenta con un brief. Se persiste como texto.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AutonomyLevel>))]
public enum AutonomyLevel
{
    /// <summary>Sólo propone conceptos; una persona decide qué se arma.</summary>
    Propose,

    /// <summary>Propone y arma las recetas; una persona aprueba antes de renderizar.</summary>
    Assisted,

    /// <summary>Propone, arma y renderiza sin esperar aprobación. Nunca publica.</summary>
    Automatic,
}

/// <summary>
/// Brief de una campaña: lo que se quiere lograr, para una marca. Tabla <c>CampaignBriefs</c>. Conserva el texto
/// original que escribió la persona además de los campos interpretados.
/// </summary>
public class CampaignBrief : IOrganizationScoped
{
    /// <summary>Identificador.</summary>
    public long Id { get; set; }

    /// <inheritdoc />
    public int OrganizationId { get; set; }

    /// <summary>Marca de la campaña.</summary>
    public int BrandId { get; set; }

    /// <summary>Marca.</summary>
    public Brand? Brand { get; set; }

    /// <summary>Nombre del brief. Hasta 200 caracteres.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Texto libre tal como lo escribió la persona. Hasta 4000 caracteres.</summary>
    public string? OriginalText { get; set; }

    /// <summary>Producto de la marca que se promociona; nulo si es la marca en general.</summary>
    public int? ProductId { get; set; }

    /// <summary>Ocasión o temporada. Hasta 200 caracteres.</summary>
    public string? Occasion { get; set; }

    /// <summary>Público al que apunta. Hasta 500 caracteres.</summary>
    public string? Audience { get; set; }

    /// <summary>Objetivo de la campaña. Hasta 500 caracteres.</summary>
    public string? Objective { get; set; }

    /// <summary>Mensaje principal. Hasta 1000 caracteres.</summary>
    public string? Message { get; set; }

    /// <summary>Enfoques a priorizar. Hasta 1000 caracteres.</summary>
    public string? Approaches { get; set; }

    /// <summary>Duración deseada de cada video, en segundos.</summary>
    public int? DurationSeconds { get; set; }

    /// <summary>Cantidad de videos pedida.</summary>
    public int? VideoCount { get; set; }

    /// <summary>Plataforma de destino.</summary>
    public BriefPlatform Platform { get; set; }

    /// <summary>Si se pide investigación externa.</summary>
    public bool UseExternalResearch { get; set; }

    /// <summary>Presupuesto máximo de generación (IA y cómputo), en dólares.</summary>
    public decimal? MaxGenerationBudgetUsd { get; set; }

    /// <summary>Nivel de autonomía.</summary>
    public AutonomyLevel Autonomy { get; set; }

    /// <summary>Usuario que lo creó.</summary>
    public string? CreatedByUserId { get; set; }

    /// <summary>Alta, en UTC. La fija la base.</summary>
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Plan creativo: los conceptos que el planificador propuso para un brief. Tabla <c>CreativePlans</c>. Guarda con
/// qué modelo y qué versión de la consigna se generó y cuánto costó. Un brief puede tener varios planes.
/// </summary>
public class CreativePlan : IOrganizationScoped
{
    /// <summary>Identificador.</summary>
    public long Id { get; set; }

    /// <inheritdoc />
    public int OrganizationId { get; set; }

    /// <summary>Brief del que sale.</summary>
    public long CampaignBriefId { get; set; }

    /// <summary>Brief.</summary>
    public CampaignBrief? Brief { get; set; }

    /// <summary>Estado de la generación.</summary>
    public MediaProcessingStatus Status { get; set; }

    /// <summary>Por qué falló. Hasta 1000 caracteres.</summary>
    public string? Error { get; set; }

    /// <summary>Conceptos pedidos.</summary>
    public int RequestedCount { get; set; }

    /// <summary>Proveedor que lo generó. Hasta 50 caracteres.</summary>
    public string? Provider { get; set; }

    /// <summary>Modelo que lo generó. Hasta 100 caracteres.</summary>
    public string? Model { get; set; }

    /// <summary>Versión de la consigna. Hasta 50 caracteres.</summary>
    public string? PromptVersion { get; set; }

    /// <summary>Lo que costó, en dólares.</summary>
    public decimal CostUsd { get; set; }

    /// <summary>Usuario que lo pidió.</summary>
    public string? CreatedByUserId { get; set; }

    /// <summary>Pedido, en UTC. Lo fija la base.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Conceptos del plan.</summary>
    public List<CreativeConcept> Concepts { get; set; } = [];
}

/// <summary>
/// Un concepto de anuncio dentro de un plan. Tabla <c>CreativeConcepts</c>. Lleva su hipótesis, su guion, el
/// material existente que usaría (<see cref="ConceptFragment"/>), el que falta, y la receta que se armó con él.
/// </summary>
public class CreativeConcept : IOrganizationScoped
{
    /// <summary>Identificador.</summary>
    public long Id { get; set; }

    /// <inheritdoc />
    public int OrganizationId { get; set; }

    /// <summary>Plan al que pertenece.</summary>
    public long CreativePlanId { get; set; }

    /// <summary>Orden dentro del plan, desde 1.</summary>
    public int Position { get; set; }

    /// <summary>Nombre corto. Hasta 180 caracteres.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Hipótesis. Hasta 1000 caracteres.</summary>
    public string Hypothesis { get; set; } = string.Empty;

    /// <summary>Enfoque. Hasta 500 caracteres.</summary>
    public string Approach { get; set; } = string.Empty;

    /// <summary>Gancho de apertura. Hasta 1000 caracteres.</summary>
    public string Hook { get; set; } = string.Empty;

    /// <summary>Guion. Hasta 4000 caracteres.</summary>
    public string Script { get; set; } = string.Empty;

    /// <summary>Desarrollo. Hasta 2000 caracteres.</summary>
    public string Development { get; set; } = string.Empty;

    /// <summary>Llamada a la acción. Hasta 500 caracteres.</summary>
    public string Cta { get; set; } = string.Empty;

    /// <summary>Público previsto. Hasta 500 caracteres.</summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>Material que requiere. Hasta 1000 caracteres.</summary>
    public string RequiredMaterial { get; set; } = string.Empty;

    /// <summary>Por qué se eligió. Hasta 1000 caracteres.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>Riesgos. Hasta 1000 caracteres.</summary>
    public string Risks { get; set; } = string.Empty;

    /// <summary>Novedad, de 1 a 5.</summary>
    public int Novelty { get; set; }

    /// <summary>Material que falta, un renglón por recurso. Hasta 2000 caracteres.</summary>
    public string? MissingText { get; set; }

    /// <summary>Mensajes prohibidos de la marca que aparecen en el concepto, un renglón por frase. Hasta 1000
    /// caracteres. Un concepto con alguno no se puede convertir en receta.</summary>
    public string? ViolationsText { get; set; }

    /// <summary>Receta armada a partir del concepto; nulo si todavía no se armó.</summary>
    public long? CreativeRecipeId { get; set; }

    /// <summary>Fragmentos existentes que usaría, en orden.</summary>
    public List<ConceptFragment> Fragments { get; set; } = [];
}

/// <summary>
/// Un fragmento existente que un concepto usaría. Tabla <c>ConceptFragments</c>. Como en una receta, guarda el
/// video y el tramo además del fragmento, que puede dejar de existir.
/// </summary>
public class ConceptFragment : IOrganizationScoped
{
    /// <summary>Identificador.</summary>
    public long Id { get; set; }

    /// <inheritdoc />
    public int OrganizationId { get; set; }

    /// <summary>Concepto al que pertenece.</summary>
    public long CreativeConceptId { get; set; }

    /// <summary>Lugar en el montaje propuesto, desde 1.</summary>
    public int Position { get; set; }

    /// <summary>Fragmento de la biblioteca; nulo si ya no existe.</summary>
    public long? MediaSegmentId { get; set; }

    /// <summary>Video original.</summary>
    public long MediaAssetId { get; set; }

    /// <summary>Video original.</summary>
    public MediaAsset? MediaAsset { get; set; }

    /// <summary>Inicio del tramo, en milisegundos.</summary>
    public int StartMs { get; set; }

    /// <summary>Fin del tramo, en milisegundos.</summary>
    public int EndMs { get; set; }
}

/// <summary>Cómo se puede usar una referencia externa. Se persiste como texto.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReferenceUsage>))]
public enum ReferenceUsage
{
    /// <summary>Sólo como inspiración: no se copia su texto ni se usa su material.</summary>
    ReferenceOnly,

    /// <summary>Utilizable: la organización tiene permiso para reutilizar su contenido.</summary>
    Usable,
}

/// <summary>Qué aporta una referencia externa. Se persiste como texto.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReferenceKind>))]
public enum ReferenceKind
{
    /// <summary>Un anuncio comparable.</summary>
    Ad,

    /// <summary>Una tendencia.</summary>
    Trend,

    /// <summary>Un concepto comercial.</summary>
    Concept,

    /// <summary>Un gancho.</summary>
    Hook,

    /// <summary>Una estructura narrativa.</summary>
    Structure,

    /// <summary>Un estilo.</summary>
    Style,

    /// <summary>Un formato.</summary>
    Format,

    /// <summary>Un dato de estacionalidad.</summary>
    Seasonality,
}

/// <summary>De dónde salió una referencia. Se persiste como texto.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReferenceOrigin>))]
public enum ReferenceOrigin
{
    /// <summary>La cargó una persona.</summary>
    Manual,

    /// <summary>La encontró el agente investigador.</summary>
    Researched,
}

/// <summary>
/// Fuente externa autorizada para investigar. Tabla <c>ResearchSources</c>. Se identifica por su dominio: una
/// referencia sólo se guarda si su dirección pertenece al dominio de una fuente activa. Lleva las condiciones de
/// uso y el permiso que reciben por defecto sus referencias.
/// </summary>
public class ResearchSource : IOrganizationScoped
{
    /// <summary>Identificador.</summary>
    public int Id { get; set; }

    /// <inheritdoc />
    public int OrganizationId { get; set; }

    /// <summary>Nombre. Hasta 150 caracteres.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Dominio, sin protocolo ni <c>www</c> (por ejemplo <c>ejemplo.com</c>). Único por organización.</summary>
    public string Domain { get; set; } = string.Empty;

    /// <summary>Condiciones de uso de la fuente. Hasta 1000 caracteres.</summary>
    public string? ConditionsOfUse { get; set; }

    /// <summary>Permiso que reciben por defecto sus referencias.</summary>
    public ReferenceUsage DefaultUsage { get; set; }

    /// <summary>Si se la puede consultar. Desactivada, deja de aceptar referencias nuevas.</summary>
    public bool IsActive { get; set; }

    /// <summary>Usuario que la autorizó.</summary>
    public string? CreatedByUserId { get; set; }

    /// <summary>Alta, en UTC. La fija la base.</summary>
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Referencia externa: un anuncio, una tendencia o una idea vista en una fuente autorizada. Tabla
/// <c>ExternalReferences</c>. Guarda la dirección y un resumen en texto; <b>nunca el material</b>: no se descarga
/// ni se reproduce. Lleva su procedencia y su permiso de uso.
/// </summary>
public class ExternalReference : IOrganizationScoped
{
    /// <summary>Identificador.</summary>
    public long Id { get; set; }

    /// <inheritdoc />
    public int OrganizationId { get; set; }

    /// <summary>Fuente autorizada de la que sale.</summary>
    public int ResearchSourceId { get; set; }

    /// <summary>Fuente.</summary>
    public ResearchSource? Source { get; set; }

    /// <summary>Brief para el que se buscó; nulo si es general.</summary>
    public long? CampaignBriefId { get; set; }

    /// <summary>Corrida de investigación que la encontró; nulo si la cargó una persona.</summary>
    public long? ResearchRunId { get; set; }

    /// <summary>Qué aporta.</summary>
    public ReferenceKind Kind { get; set; }

    /// <summary>Dirección. Hasta 450 caracteres; única por organización.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Título. Hasta 300 caracteres.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Qué se ve o qué enseña, en palabras propias. Hasta 2000 caracteres.</summary>
    public string? Summary { get; set; }

    /// <summary>Cómo se puede usar.</summary>
    public ReferenceUsage Usage { get; set; }

    /// <summary>De dónde salió.</summary>
    public ReferenceOrigin Origin { get; set; }

    /// <summary>Usuario que la cargó; nulo si la encontró el agente.</summary>
    public string? CreatedByUserId { get; set; }

    /// <summary>Alta, en UTC. La fija la base.</summary>
    public DateTime CreatedAt { get; set; }
}

/// <summary>Una corrida del agente investigador para un brief. Tabla <c>ResearchRuns</c>.</summary>
public class ResearchRun : IOrganizationScoped
{
    /// <summary>Identificador.</summary>
    public long Id { get; set; }

    /// <inheritdoc />
    public int OrganizationId { get; set; }

    /// <summary>Brief investigado.</summary>
    public long CampaignBriefId { get; set; }

    /// <summary>Estado.</summary>
    public MediaProcessingStatus Status { get; set; }

    /// <summary>Por qué falló. Hasta 1000 caracteres.</summary>
    public string? Error { get; set; }

    /// <summary>Consultas de búsqueda que hizo el modelo.</summary>
    public int SearchQueries { get; set; }

    /// <summary>Referencias que propuso el modelo.</summary>
    public int Found { get; set; }

    /// <summary>Referencias que se guardaron: las de un dominio autorizado y no repetidas.</summary>
    public int Kept { get; set; }

    /// <summary>Modelo. Hasta 100 caracteres.</summary>
    public string? Model { get; set; }

    /// <summary>Versión de la consigna. Hasta 50 caracteres.</summary>
    public string? PromptVersion { get; set; }

    /// <summary>Lo que costó, en dólares.</summary>
    public decimal CostUsd { get; set; }

    /// <summary>Usuario que la pidió.</summary>
    public string? CreatedByUserId { get; set; }

    /// <summary>Pedido, en UTC. Lo fija la base.</summary>
    public DateTime CreatedAt { get; set; }
}

/// <summary>Cuenta publicitaria de Meta. Tabla <c>MetaAdAccounts</c>. Espejo de la estructura de Meta: se
/// identifica por su identificador externo, único por organización.</summary>
public class MetaAdAccount : IOrganizationScoped
{
    /// <summary>Identificador.</summary>
    public int Id { get; set; }

    /// <inheritdoc />
    public int OrganizationId { get; set; }

    /// <summary>Identificador en Meta. Hasta 100 caracteres.</summary>
    public string ExternalId { get; set; } = string.Empty;

    /// <summary>Nombre. Hasta 200 caracteres.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Moneda en la que informa el gasto (código de tres letras).</summary>
    public string Currency { get; set; } = "USD";
}

/// <summary>Campaña de Meta. Tabla <c>MetaCampaigns</c>.</summary>
public class MetaCampaign : IOrganizationScoped
{
    /// <summary>Identificador.</summary>
    public long Id { get; set; }

    /// <inheritdoc />
    public int OrganizationId { get; set; }

    /// <summary>Cuenta a la que pertenece.</summary>
    public int MetaAdAccountId { get; set; }

    /// <summary>Identificador en Meta; si la fuente no lo trae, se arma con el nombre. Hasta 300 caracteres.</summary>
    public string ExternalId { get; set; } = string.Empty;

    /// <summary>Nombre. Hasta 300 caracteres.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Objetivo de la campaña, si la fuente lo informa. Hasta 100 caracteres.</summary>
    public string? Objective { get; set; }
}

/// <summary>Conjunto de anuncios de Meta. Tabla <c>MetaAdSets</c>.</summary>
public class MetaAdSet : IOrganizationScoped
{
    /// <summary>Identificador.</summary>
    public long Id { get; set; }

    /// <inheritdoc />
    public int OrganizationId { get; set; }

    /// <summary>Campaña a la que pertenece.</summary>
    public long MetaCampaignId { get; set; }

    /// <summary>Campaña.</summary>
    public MetaCampaign? Campaign { get; set; }

    /// <summary>Identificador en Meta; si la fuente no lo trae, se arma con el nombre. Hasta 300 caracteres.</summary>
    public string ExternalId { get; set; } = string.Empty;

    /// <summary>Nombre. Hasta 300 caracteres.</summary>
    public string Name { get; set; } = string.Empty;
}

/// <summary>Cómo se estableció el vínculo entre un anuncio y un video generado. Se persiste como texto.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AdLinkOrigin>))]
public enum AdLinkOrigin
{
    /// <summary>Lo eligió una persona.</summary>
    Manual,

    /// <summary>Lo estableció la plataforma al publicar el video.</summary>
    Automatic,
}

/// <summary>
/// Anuncio de Meta. Tabla <c>MetaAds</c>. Es el punto donde el rendimiento se une con la creatividad: puede estar
/// vinculado al video generado (<see cref="CreativeVariant"/>) que se publicó en él, y por ese vínculo se llega a
/// su receta, sus fragmentos, su concepto y su brief.
/// </summary>
public class MetaAd : IOrganizationScoped
{
    /// <summary>Identificador.</summary>
    public long Id { get; set; }

    /// <inheritdoc />
    public int OrganizationId { get; set; }

    /// <summary>Conjunto al que pertenece.</summary>
    public long MetaAdSetId { get; set; }

    /// <summary>Conjunto.</summary>
    public MetaAdSet? AdSet { get; set; }

    /// <summary>Identificador en Meta. Hasta 100 caracteres; único por organización.</summary>
    public string ExternalId { get; set; } = string.Empty;

    /// <summary>Nombre. Hasta 300 caracteres.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Video generado que se publicó en este anuncio; nulo si no está vinculado.</summary>
    public long? CreativeVariantId { get; set; }

    /// <summary>Cómo se estableció el vínculo; nulo si no está vinculado.</summary>
    public AdLinkOrigin? LinkOrigin { get; set; }

    /// <summary>Usuario que lo vinculó.</summary>
    public string? LinkedByUserId { get; set; }
}

/// <summary>
/// Métricas de un anuncio en un día. Tabla <c>MetaInsightsDaily</c>, una fila por anuncio y fecha. Sólo guarda
/// cantidades que se pueden sumar (gasto, impresiones, clics, reproducciones, compras, valor): las tasas (CTR,
/// CPC, CPM, CPA, ROAS) se calculan al consultar sobre el período que se mire, porque una tasa no se promedia.
/// </summary>
public class MetaInsightDaily : IOrganizationScoped
{
    /// <summary>Identificador.</summary>
    public long Id { get; set; }

    /// <inheritdoc />
    public int OrganizationId { get; set; }

    /// <summary>Anuncio.</summary>
    public long MetaAdId { get; set; }

    /// <summary>Día.</summary>
    public DateOnly Date { get; set; }

    /// <summary>Gasto, en la moneda de la cuenta.</summary>
    public decimal Spend { get; set; }

    /// <summary>Impresiones.</summary>
    public long Impressions { get; set; }

    /// <summary>Clics en el enlace.</summary>
    public long Clicks { get; set; }

    /// <summary>Reproducciones de video de 3 segundos; nulo si la fuente no las informa.</summary>
    public long? VideoPlays { get; set; }

    /// <summary>Reproducciones completas o de 15 segundos (ThruPlay); nulo si la fuente no las informa.</summary>
    public long? ThruPlays { get; set; }

    /// <summary>Compras atribuidas; nulo si la fuente no las informa.</summary>
    public decimal? Purchases { get; set; }

    /// <summary>Valor de las compras atribuidas, en la moneda de la cuenta; nulo si la fuente no lo informa.</summary>
    public decimal? PurchaseValue { get; set; }
}

/// <summary>Una importación de métricas desde un archivo. Tabla <c>MetaImports</c>. Deja registro de qué se cargó,
/// cuándo, cuántas filas entraron y cuáles se descartaron y por qué.</summary>
public class MetaImport : IOrganizationScoped
{
    /// <summary>Identificador.</summary>
    public long Id { get; set; }

    /// <inheritdoc />
    public int OrganizationId { get; set; }

    /// <summary>Nombre del archivo cargado. Hasta 260 caracteres.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Ruta del archivo en el almacenamiento.</summary>
    public string StoragePath { get; set; } = string.Empty;

    /// <summary>Moneda que se asume si el archivo no la dice.</summary>
    public string DefaultCurrency { get; set; } = "USD";

    /// <summary>Estado.</summary>
    public MediaProcessingStatus Status { get; set; }

    /// <summary>Por qué falló, o qué filas se descartaron. Hasta 2000 caracteres.</summary>
    public string? Notes { get; set; }

    /// <summary>Filas de métricas guardadas.</summary>
    public int Rows { get; set; }

    /// <summary>Filas descartadas.</summary>
    public int Skipped { get; set; }

    /// <summary>Anuncios distintos del archivo.</summary>
    public int Ads { get; set; }

    /// <summary>Primer día con datos.</summary>
    public DateOnly? FirstDate { get; set; }

    /// <summary>Último día con datos.</summary>
    public DateOnly? LastDate { get; set; }

    /// <summary>Usuario que lo cargó.</summary>
    public string? CreatedByUserId { get; set; }

    /// <summary>Carga, en UTC. La fija la base.</summary>
    public DateTime CreatedAt { get; set; }
}

/// <summary>Qué se cambia entre los brazos de un experimento. Se persiste como texto.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ExperimentVariable>))]
public enum ExperimentVariable
{
    /// <summary>El fragmento con que abre el video.</summary>
    Opening,

    /// <summary>El fragmento con que cierra.</summary>
    Closing,

    /// <summary>Los fragmentos del medio.</summary>
    Development,

    /// <summary>Con o sin subtítulos.</summary>
    Subtitles,

    /// <summary>Con o sin música.</summary>
    Music,

    /// <summary>La transición entre fragmentos.</summary>
    Transition,

    /// <summary>Otra cosa que el sistema no puede comprobar (la oferta, el texto del anuncio, la audiencia).</summary>
    Other,
}

/// <summary>Con qué se mide un experimento. Se persiste como texto. Son todas proporciones, que es lo que la
/// prueba estadística sabe comparar.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ExperimentMetric>))]
public enum ExperimentMetric
{
    /// <summary>Clics sobre impresiones.</summary>
    Ctr,

    /// <summary>Reproducciones de 3 segundos sobre impresiones.</summary>
    HookRate,

    /// <summary>Compras sobre clics.</summary>
    PurchaseRate,
}

/// <summary>
/// Experimento controlado: una comparación planeada de antemano entre anuncios que difieren en una sola cosa.
/// Tabla <c>Experiments</c>. Guarda la hipótesis, la variable, la métrica, el período y el mínimo de impresiones
/// por brazo, todos definidos antes de mirar resultados. El resultado no se guarda: se calcula de las métricas del
/// período. La conclusión es la que escribe una persona al cerrarlo.
/// </summary>
public class Experiment : IOrganizationScoped
{
    /// <summary>Identificador.</summary>
    public long Id { get; set; }

    /// <inheritdoc />
    public int OrganizationId { get; set; }

    /// <summary>Nombre. Hasta 200 caracteres.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Qué se cree que va a pasar y por qué. Hasta 1000 caracteres.</summary>
    public string Hypothesis { get; set; } = string.Empty;

    /// <summary>Qué se cambia entre los brazos.</summary>
    public ExperimentVariable Variable { get; set; }

    /// <summary>Con qué se mide.</summary>
    public ExperimentMetric Metric { get; set; }

    /// <summary>Primer día del período.</summary>
    public DateOnly StartDate { get; set; }

    /// <summary>Último día del período; nulo si todavía no se fijó.</summary>
    public DateOnly? EndDate { get; set; }

    /// <summary>Impresiones que necesita cada brazo para que el resultado se informe.</summary>
    public int MinImpressionsPerArm { get; set; }

    /// <summary>Conclusión escrita por una persona al cerrarlo. Hasta 2000 caracteres.</summary>
    public string? Conclusion { get; set; }

    /// <summary>Cuándo se cerró, en UTC con hora de la base; nulo si sigue abierto.</summary>
    public DateTime? ConcludedAt { get; set; }

    /// <summary>Hipótesis de la memoria de aprendizaje que este experimento pone a prueba; nulo si se creó suelto.</summary>
    public long? HypothesisId { get; set; }

    /// <summary>Usuario que lo creó.</summary>
    public string? CreatedByUserId { get; set; }

    /// <summary>Alta, en UTC. La fija la base.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Brazos; el primero es el control.</summary>
    public List<ExperimentArm> Arms { get; set; } = [];
}

/// <summary>Un brazo de un experimento: un anuncio. Tabla <c>ExperimentArms</c>. El de posición 1 es el control,
/// contra el que se comparan los demás.</summary>
public class ExperimentArm : IOrganizationScoped
{
    /// <summary>Identificador.</summary>
    public long Id { get; set; }

    /// <inheritdoc />
    public int OrganizationId { get; set; }

    /// <summary>Experimento al que pertenece.</summary>
    public long ExperimentId { get; set; }

    /// <summary>Orden, desde 1; el 1 es el control.</summary>
    public int Position { get; set; }

    /// <summary>Nombre del brazo. Hasta 100 caracteres.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Anuncio del brazo.</summary>
    public long MetaAdId { get; set; }
}

/// <summary>De dónde salió una hipótesis. Se persiste como texto.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<HypothesisOrigin>))]
public enum HypothesisOrigin
{
    /// <summary>La escribió una persona.</summary>
    Manual,

    /// <summary>Surgió de un patrón del análisis por componente (una correlación).</summary>
    Pattern,
}

/// <summary>Qué tanta confianza merece un aprendizaje. La calcula el sistema a partir de los datos; una persona no
/// puede subirla. Se persiste como texto.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LearningConfidence>))]
public enum LearningConfidence
{
    /// <summary>Una correlación, o un experimento con problemas de diseño, sin terminar o sin diferencia.</summary>
    Low,

    /// <summary>Una correlación firme en varias campañas, o un experimento limpio con diferencia probable.</summary>
    Medium,

    /// <summary>Un experimento limpio y terminado con diferencia clara.</summary>
    High,
}

/// <summary>Estado de un aprendizaje. Sólo los vigentes influyen en lo que se planifica. Se persiste como texto.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LearningStatus>))]
public enum LearningStatus
{
    /// <summary>Vigente.</summary>
    Active,

    /// <summary>Reemplazado por una versión corregida.</summary>
    Corrected,

    /// <summary>Invalidado: dejó de valer.</summary>
    Invalidated,
}

/// <summary>
/// Hipótesis: una conjetura sobre qué hace rendir mejor a una creatividad, todavía sin comprobar. Tabla
/// <c>Hypotheses</c>. Si surgió de un patrón guarda de cuál (dimensión, componente, campaña y período) para poder
/// volver a calcularlo. Su estado (abierta, en prueba, con aprendizaje) no se guarda: se deduce de sus experimentos
/// y aprendizajes; sólo se guarda si una persona la descartó.
/// </summary>
public class Hypothesis : IOrganizationScoped
{
    /// <summary>Identificador.</summary>
    public long Id { get; set; }

    /// <inheritdoc />
    public int OrganizationId { get; set; }

    /// <summary>Enunciado. Hasta 1000 caracteres.</summary>
    public string Statement { get; set; } = string.Empty;

    /// <summary>De dónde salió.</summary>
    public HypothesisOrigin Origin { get; set; }

    /// <summary>Sobre qué parte de la creatividad trata.</summary>
    public ExperimentVariable Variable { get; set; }

    /// <summary>Dimensión del análisis de la que surgió (<c>opening</c>, <c>music</c>…); nulo si es manual.</summary>
    public string? PatternDimension { get; set; }

    /// <summary>Clave del componente dentro de la dimensión; nulo si es manual.</summary>
    public string? PatternKey { get; set; }

    /// <summary>Nombre del componente para mostrar. Hasta 300 caracteres.</summary>
    public string? PatternLabel { get; set; }

    /// <summary>La lectura del patrón tal como estaba cuando se guardó la hipótesis. Hasta 1000 caracteres.</summary>
    public string? PatternReading { get; set; }

    /// <summary>Campaña a la que se limitó el análisis; nulo si eran todas.</summary>
    public long? PatternCampaignId { get; set; }

    /// <summary>Primer día del análisis; nulo si no tenía límite.</summary>
    public DateOnly? PatternFrom { get; set; }

    /// <summary>Último día del análisis; nulo si no tenía límite.</summary>
    public DateOnly? PatternTo { get; set; }

    /// <summary>Qué tan relevante es para el público al que se apunta, según una persona; nulo si no se valoró.</summary>
    public AssessmentLevel? AudienceRelevance { get; set; }

    /// <summary>Cuánto cuesta producirla, según una persona; nulo si no se valoró.</summary>
    public AssessmentLevel? ProductionCost { get; set; }

    /// <summary>Qué riesgo publicitario tiene (rechazo, políticas, marca), según una persona; nulo si no se valoró.</summary>
    public AssessmentLevel? AdRisk { get; set; }

    /// <summary>Qué margen podría dejar, según una persona; nulo si no se valoró.</summary>
    public AssessmentLevel? PotentialMargin { get; set; }

    /// <summary>Cuándo se descartó, en UTC con hora de la base; nulo si sigue en pie.</summary>
    public DateTime? DiscardedAt { get; set; }

    /// <summary>Por qué se descartó. Hasta 500 caracteres.</summary>
    public string? DiscardReason { get; set; }

    /// <summary>Usuario que la creó.</summary>
    public string? CreatedByUserId { get; set; }

    /// <summary>Alta, en UTC. La fija la base.</summary>
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Aprendizaje: algo que los datos permiten afirmar, con qué confianza y para qué sirve. Tabla <c>Learnings</c>.
/// Siempre tiene datos detrás: un experimento o un patrón. Lo que calcula el sistema (datos usados, resultado,
/// confianza, período, versión del criterio) queda fijo como estaba al registrarlo; lo que escribe una persona es
/// el enunciado, la recomendación y la aplicabilidad. No se edita: corregirlo crea una versión nueva y deja ésta
/// como <see cref="LearningStatus.Corrected"/>. No se borra: se invalida.
/// </summary>
public class Learning : IOrganizationScoped
{
    /// <summary>Identificador.</summary>
    public long Id { get; set; }

    /// <inheritdoc />
    public int OrganizationId { get; set; }

    /// <summary>Hipótesis de la que sale; nulo si el experimento no tenía una.</summary>
    public long? HypothesisId { get; set; }

    /// <summary>Experimento que lo sustenta; nulo si sale de un patrón.</summary>
    public long? ExperimentId { get; set; }

    /// <summary>Qué se aprendió. Hasta 1000 caracteres.</summary>
    public string Statement { get; set; } = string.Empty;

    /// <summary>Qué datos se usaron, descritos por el sistema. Hasta 1000 caracteres.</summary>
    public string DataUsed { get; set; } = string.Empty;

    /// <summary>El resultado en palabras, tal como lo calculó el sistema. Hasta 2000 caracteres.</summary>
    public string Result { get; set; } = string.Empty;

    /// <summary>Confianza calculada.</summary>
    public LearningConfidence Confidence { get; set; }

    /// <summary>Por qué tiene esa confianza. Hasta 500 caracteres.</summary>
    public string ConfidenceReason { get; set; } = string.Empty;

    /// <summary>Primer día de los datos; nulo si no tenían límite.</summary>
    public DateOnly? PeriodStart { get; set; }

    /// <summary>Último día de los datos; nulo si no tenían límite.</summary>
    public DateOnly? PeriodEnd { get; set; }

    /// <summary>Qué conviene hacer. Hasta 1000 caracteres.</summary>
    public string Recommendation { get; set; } = string.Empty;

    /// <summary>Para qué casos vale y para cuáles no. Hasta 1000 caracteres.</summary>
    public string Applicability { get; set; } = string.Empty;

    /// <summary>Versión del criterio con que se calculó la confianza.</summary>
    public string CriteriaVersion { get; set; } = string.Empty;

    /// <summary>Estado.</summary>
    public LearningStatus Status { get; set; }

    /// <summary>Por qué se corrigió o se invalidó. Hasta 1000 caracteres.</summary>
    public string? StatusReason { get; set; }

    /// <summary>Cuándo se corrigió o se invalidó, en UTC con hora de la base.</summary>
    public DateTime? StatusChangedAt { get; set; }

    /// <summary>Aprendizaje al que éste corrige; nulo si es una primera versión.</summary>
    public long? ReplacesLearningId { get; set; }

    /// <summary>Usuario que lo registró.</summary>
    public string? CreatedByUserId { get; set; }

    /// <summary>Alta, en UTC. La fija la base.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Lo que lo sustenta.</summary>
    public List<LearningEvidence> Evidence { get; set; } = [];
}

/// <summary>
/// Una pieza de la evidencia de un aprendizaje. Tabla <c>LearningEvidence</c>. Es un anuncio (con el video que
/// publicó, si está vinculado) o un tramo de un original (cuando el aprendizaje es sobre un fragmento).
/// </summary>
public class LearningEvidence : IOrganizationScoped
{
    /// <summary>Identificador.</summary>
    public long Id { get; set; }

    /// <inheritdoc />
    public int OrganizationId { get; set; }

    /// <summary>Aprendizaje al que pertenece.</summary>
    public long LearningId { get; set; }

    /// <summary>Anuncio cuyas métricas se usaron.</summary>
    public long? MetaAdId { get; set; }

    /// <summary>Video generado que publicó ese anuncio, tal como estaba vinculado al registrar.</summary>
    public long? CreativeVariantId { get; set; }

    /// <summary>Original del tramo sobre el que trata el aprendizaje.</summary>
    public long? MediaAssetId { get; set; }

    /// <summary>Inicio del tramo, en milisegundos.</summary>
    public int? StartMs { get; set; }

    /// <summary>Fin del tramo, en milisegundos.</summary>
    public int? EndMs { get; set; }
}

/// <summary>Valoración de una persona sobre algo que el sistema no puede medir. Se persiste como texto.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AssessmentLevel>))]
public enum AssessmentLevel
{
    /// <summary>Bajo.</summary>
    Low,

    /// <summary>Medio.</summary>
    Medium,

    /// <summary>Alto.</summary>
    High,
}

/// <summary>
/// Una versión de las ponderaciones del motor de priorización: cuánto pesa cada uno de los diez factores, de 0 a
/// 10. Tabla <c>PriorityWeightSets</c>. No se edita: cambiar las ponderaciones crea una versión nueva, y la vigente
/// es la de número más alto. Mientras una organización no guarde ninguna rigen las de fábrica (versión 0).
/// </summary>
public class PriorityWeightSet : IOrganizationScoped
{
    /// <summary>Identificador.</summary>
    public long Id { get; set; }

    /// <inheritdoc />
    public int OrganizationId { get; set; }

    /// <summary>Número de versión dentro de la organización, desde 1.</summary>
    public int Version { get; set; }

    /// <summary>Peso del rendimiento histórico.</summary>
    public int HistoricalPerformance { get; set; }

    /// <summary>Peso de la calidad de la evidencia.</summary>
    public int EvidenceQuality { get; set; }

    /// <summary>Peso de la similitud con creatividades existentes.</summary>
    public int Similarity { get; set; }

    /// <summary>Peso de la novedad.</summary>
    public int Novelty { get; set; }

    /// <summary>Peso de la relevancia para el público.</summary>
    public int AudienceRelevance { get; set; }

    /// <summary>Peso de la disponibilidad de material.</summary>
    public int MaterialAvailability { get; set; }

    /// <summary>Peso del costo de producción.</summary>
    public int ProductionCost { get; set; }

    /// <summary>Peso del riesgo publicitario.</summary>
    public int AdRisk { get; set; }

    /// <summary>Peso de la fatiga.</summary>
    public int Fatigue { get; set; }

    /// <summary>Peso del margen potencial.</summary>
    public int PotentialMargin { get; set; }

    /// <summary>Por qué se cambiaron. Hasta 500 caracteres.</summary>
    public string Note { get; set; } = string.Empty;

    /// <summary>Usuario que las guardó.</summary>
    public string? CreatedByUserId { get; set; }

    /// <summary>Alta, en UTC. La fija la base.</summary>
    public DateTime CreatedAt { get; set; }
}

/// <summary>Qué propone hacer un plan de producción con una hipótesis. Se persiste como texto.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PlanAction>))]
public enum PlanAction
{
    /// <summary>Producir aplicándola: la respalda un aprendizaje vigente de confianza media o alta.</summary>
    Apply,

    /// <summary>Probarla con un experimento: sólo hay una correlación o un aprendizaje de confianza baja.</summary>
    Test,

    /// <summary>Explorarla en chico: es una conjetura sin datos.</summary>
    Explore,
}

/// <summary>
/// Plan de producción basado en resultados: las hipótesis de mayor prioridad en el momento de armarlo, con qué se
/// propone hacer con cada una y por qué. Tabla <c>ProductionPlans</c>. Es una foto: guarda la versión de las
/// ponderaciones con que se calculó y no cambia aunque después cambien los datos, los aprendizajes o los pesos.
/// </summary>
public class ProductionPlan : IOrganizationScoped
{
    /// <summary>Identificador.</summary>
    public long Id { get; set; }

    /// <inheritdoc />
    public int OrganizationId { get; set; }

    /// <summary>Nombre. Hasta 200 caracteres.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Cuántas hipótesis se pidieron; el plan puede tener menos si no había tantas con puntaje.</summary>
    public int RequestedItems { get; set; }

    /// <summary>Versión de las ponderaciones con que se calcularon los puntajes; 0 son las de fábrica.</summary>
    public int WeightsVersion { get; set; }

    /// <summary>Usuario que lo armó.</summary>
    public string? CreatedByUserId { get; set; }

    /// <summary>Alta, en UTC. La fija la base.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Hipótesis elegidas, por orden de prioridad.</summary>
    public List<ProductionPlanItem> Items { get; set; } = [];
}

/// <summary>
/// Una hipótesis dentro de un plan de producción. Tabla <c>ProductionPlanItems</c>. Guarda copiado todo lo que
/// justificó elegirla (enunciado, puntaje, desglose por factor, aprendizaje que la respalda y su recomendación),
/// para que la explicación siga siendo la del día en que se armó el plan.
/// </summary>
public class ProductionPlanItem : IOrganizationScoped
{
    /// <summary>Identificador.</summary>
    public long Id { get; set; }

    /// <inheritdoc />
    public int OrganizationId { get; set; }

    /// <summary>Plan al que pertenece.</summary>
    public long ProductionPlanId { get; set; }

    /// <summary>Orden de prioridad, desde 1.</summary>
    public int Position { get; set; }

    /// <summary>Hipótesis elegida.</summary>
    public long HypothesisId { get; set; }

    /// <summary>Enunciado de la hipótesis al armar el plan. Hasta 1000 caracteres.</summary>
    public string Statement { get; set; } = string.Empty;

    /// <summary>Sobre qué parte de la creatividad trata (apertura, desarrollo, cierre, terminación u otra cosa).</summary>
    public ExperimentVariable Variable { get; set; }

    /// <summary>Componente concreto, si la hipótesis salió de un patrón. Hasta 300 caracteres.</summary>
    public string? Component { get; set; }

    /// <summary>Original del fragmento, si el componente es un fragmento.</summary>
    public long? MediaAssetId { get; set; }

    /// <summary>Inicio del fragmento, en milisegundos.</summary>
    public int? StartMs { get; set; }

    /// <summary>Fin del fragmento, en milisegundos.</summary>
    public int? EndMs { get; set; }

    /// <summary>Qué se propone hacer.</summary>
    public PlanAction Action { get; set; }

    /// <summary>Puntaje de prioridad al armar el plan, de 0 a 100.</summary>
    public decimal Score { get; set; }

    /// <summary>Por qué se eligió y por qué se propone eso. Hasta 4000 caracteres.</summary>
    public string Justification { get; set; } = string.Empty;

    /// <summary>El desglose del puntaje, factor por factor, como JSON de <c>FactorValue</c>.</summary>
    public string FactorsJson { get; set; } = string.Empty;

    /// <summary>Aprendizaje vigente que la respaldaba al armar el plan; nulo si no tenía.</summary>
    public long? LearningId { get; set; }

    /// <summary>La recomendación de ese aprendizaje, copiada. Hasta 1000 caracteres.</summary>
    public string? Recommendation { get; set; }
}

/// <summary>Cuánta autonomía tiene la plataforma para actuar en Meta. Se persiste como texto.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AutomationMode>))]
public enum AutomationMode
{
    /// <summary>La plataforma analiza y recomienda; toda acción la decide una persona.</summary>
    Manual,

    /// <summary>La plataforma prepara; una persona aprueba las acciones publicitarias críticas.</summary>
    SemiAutomatic,

    /// <summary>La plataforma ejecuta sola lo que entra en los límites y tiene respaldo; el resto pasa a aprobación.</summary>
    Automatic,
}

/// <summary>Estado de una acción de automatización. Se persiste como texto.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AutomationActionStatus>))]
public enum AutomationActionStatus
{
    /// <summary>Espera la decisión de una persona.</summary>
    PendingApproval,

    /// <summary>Aprobada y en cola para ejecutarse.</summary>
    Approved,

    /// <summary>Ejecutada de verdad en Meta.</summary>
    Executed,

    /// <summary>Simulada: se registró lo que se habría hecho, sin escribir en Meta.</summary>
    Simulated,

    /// <summary>Rechazada por una persona.</summary>
    Rejected,

    /// <summary>Bloqueada por una guarda (límites, derechos, ejecución real desactivada).</summary>
    Blocked,

    /// <summary>Cancelada por el kill switch.</summary>
    Cancelled,

    /// <summary>Falló al ejecutarse en Meta.</summary>
    Failed,
}

/// <summary>
/// Política de automatización de una organización: modo, límites, kill switch y si la ejecución real está
/// habilitada. Tabla <c>AutomationPolicies</c>, una fila por organización. Sin fila rige lo más restrictivo: modo
/// manual, sin límites definidos (nada se ejecuta) y ejecución real desactivada.
/// </summary>
public class AutomationPolicy : IOrganizationScoped
{
    /// <summary>Identificador.</summary>
    public int Id { get; set; }

    /// <inheritdoc />
    public int OrganizationId { get; set; }

    /// <summary>Modo de autonomía.</summary>
    public AutomationMode Mode { get; set; }

    /// <summary>Si las acciones escriben de verdad en Meta. Falso por defecto: todo se simula.</summary>
    public bool RealExecutionEnabled { get; set; }

    /// <summary>Si el kill switch está activo: no se acepta ni se ejecuta ninguna acción.</summary>
    public bool KillSwitchActive { get; set; }

    /// <summary>Presupuesto diario máximo de una acción; 0 si no se definió.</summary>
    public decimal MaxBudgetPerAction { get; set; }

    /// <summary>Presupuesto diario máximo que pueden comprometer todas las acciones de un día; 0 si no se definió.</summary>
    public decimal MaxDailyBudget { get; set; }

    /// <summary>Cantidad máxima de acciones por día; 0 si no se definió.</summary>
    public int MaxActionsPerDay { get; set; }

    /// <summary>Presupuesto diario máximo que puede comprometer un lote de acciones propuestas de una vez; 0 si no se definió.</summary>
    public decimal MaxBatchBudget { get; set; }
}

/// <summary>
/// Una acción publicitaria pedida a la plataforma: publicar un video generado como anuncio en un conjunto. Tabla
/// <c>AutomationActions</c>. Guarda quién la pidió, por qué necesitó aprobación, quién la decidió y qué pasó. Si
/// se creó con la ejecución real desactivada es una simulación y nunca llega a escribir en Meta.
/// </summary>
public class AutomationAction : IOrganizationScoped
{
    /// <summary>Identificador.</summary>
    public long Id { get; set; }

    /// <inheritdoc />
    public int OrganizationId { get; set; }

    /// <summary>Video generado a publicar.</summary>
    public long CreativeVariantId { get; set; }

    /// <summary>Conjunto de anuncios donde se publica.</summary>
    public long MetaAdSetId { get; set; }

    /// <summary>Nombre del anuncio. Hasta 300 caracteres.</summary>
    public string AdName { get; set; } = string.Empty;

    /// <summary>Presupuesto diario que compromete, en la moneda de la cuenta.</summary>
    public decimal DailyBudget { get; set; }

    /// <summary>Hipótesis que la respalda; con ella se comprueba si hay datos suficientes para ejecutarla sin aprobación.</summary>
    public long? HypothesisId { get; set; }

    /// <summary>Plan de producción del que el sistema la propuso; nulo si la pidió una persona suelta.</summary>
    public long? ProductionPlanId { get; set; }

    /// <summary>Si es una simulación: se decide al crearla, según la política de ese momento.</summary>
    public bool IsSimulation { get; set; }

    /// <summary>Estado.</summary>
    public AutomationActionStatus Status { get; set; }

    /// <summary>Por qué necesitó aprobación; nulo si se ejecutó sin ella. Hasta 500 caracteres.</summary>
    public string? ApprovalReason { get; set; }

    /// <summary>Qué pasó: lo que se hizo, lo que se habría hecho o por qué se bloqueó. Hasta 1000 caracteres.</summary>
    public string? Result { get; set; }

    /// <summary>Identificador del anuncio creado en Meta; sólo en ejecuciones reales.</summary>
    public string? ExternalId { get; set; }

    /// <summary>Usuario que la pidió; nulo si la propuso el sistema.</summary>
    public string? RequestedByUserId { get; set; }

    /// <summary>Alta, en UTC. La fija la base.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Usuario que la aprobó o la rechazó.</summary>
    public string? DecidedByUserId { get; set; }

    /// <summary>Cuándo se decidió, en UTC con hora de la base.</summary>
    public DateTime? DecidedAt { get; set; }

    /// <summary>Nota de quien decidió. Hasta 500 caracteres.</summary>
    public string? DecisionNote { get; set; }

    /// <summary>Cuándo terminó (ejecutada, simulada, bloqueada, cancelada o fallida), en UTC con hora de la base.</summary>
    public DateTime? FinishedAt { get; set; }
}

/// <summary>Una activación o desactivación del kill switch. Tabla <c>KillSwitchEvents</c>.</summary>
public class KillSwitchEvent : IOrganizationScoped
{
    /// <summary>Identificador.</summary>
    public long Id { get; set; }

    /// <inheritdoc />
    public int OrganizationId { get; set; }

    /// <summary>Verdadero si se activó; falso si se desactivó.</summary>
    public bool Activated { get; set; }

    /// <summary>Motivo. Hasta 500 caracteres.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>Acciones pendientes que canceló al activarse.</summary>
    public int CancelledActions { get; set; }

    /// <summary>Usuario que lo accionó.</summary>
    public string? UserId { get; set; }

    /// <summary>Cuándo, en UTC. La fija la base.</summary>
    public DateTime CreatedAt { get; set; }
}

/// <summary>Qué tipo de anomalía se detectó en las métricas de un anuncio. Se persiste como texto.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AnomalyKind>))]
public enum AnomalyKind
{
    /// <summary>El gasto del último día es varias veces el de los días anteriores.</summary>
    SpendSpike,

    /// <summary>El CTR del último día cayó a menos de la mitad del de los días anteriores.</summary>
    CtrDrop,

    /// <summary>Hubo gasto sin ninguna impresión.</summary>
    SpendWithoutImpressions,
}

/// <summary>
/// Una anomalía detectada en las métricas importadas de un anuncio. Tabla <c>AnomalyEvents</c>. Es una observación
/// para que una persona la mire, no un diagnóstico. Mientras haya alguna sin revisar, el modo automático no
/// ejecuta nada solo.
/// </summary>
public class AnomalyEvent : IOrganizationScoped
{
    /// <summary>Identificador.</summary>
    public long Id { get; set; }

    /// <inheritdoc />
    public int OrganizationId { get; set; }

    /// <summary>Anuncio.</summary>
    public long MetaAdId { get; set; }

    /// <summary>Qué se detectó.</summary>
    public AnomalyKind Kind { get; set; }

    /// <summary>Día de las métricas en que se ve.</summary>
    public DateOnly Date { get; set; }

    /// <summary>Lo observado, con sus cifras. Hasta 500 caracteres.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Cuándo se detectó, en UTC. La fija la base.</summary>
    public DateTime DetectedAt { get; set; }

    /// <summary>Cuándo la revisó una persona, en UTC con hora de la base; nulo si sigue abierta.</summary>
    public DateTime? ReviewedAt { get; set; }

    /// <summary>Usuario que la revisó.</summary>
    public string? ReviewedByUserId { get; set; }

    /// <summary>Qué concluyó quien la revisó. Hasta 500 caracteres.</summary>
    public string? ReviewNote { get; set; }
}

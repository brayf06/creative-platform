using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace CreativeIntelligence.Web.Services.Media;

/// <summary>
/// Almacenamiento de archivos de medios. Es un límite externo: hoy es el disco local, mañana puede ser un
/// almacenamiento en la nube. Los originales son inmutables: no hay operación para modificarlos ni borrarlos.
/// </summary>
public interface IMediaStorage
{
    /// <summary>
    /// Guarda un original direccionado por el SHA-256 de su contenido, dentro de la carpeta de la organización.
    /// Si ya existe un archivo con ese contenido no lo vuelve a escribir. Un contenido vacío no se guarda.
    /// </summary>
    /// <param name="organizationId">Organización dueña.</param>
    /// <param name="content">Contenido a guardar; se lee una sola vez, en secuencia.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Hash, tamaño y ruta relativa del original.</returns>
    Task<StoredOriginal> SaveOriginalAsync(int organizationId, Stream content, CancellationToken cancellationToken);

    /// <summary>Abre un archivo para lectura, con posicionamiento (lo necesita la reproducción por rangos).</summary>
    /// <param name="relativePath">Ruta relativa devuelta al guardar.</param>
    /// <returns>Flujo de sólo lectura; quien llama lo cierra.</returns>
    Stream OpenRead(string relativePath);

    /// <summary>Ruta local de un archivo, para pasársela a FFmpeg. Sólo la usan los trabajos del Worker.</summary>
    /// <param name="relativePath">Ruta relativa a la raíz del almacenamiento.</param>
    /// <returns>Ruta absoluta en disco.</returns>
    string GetLocalPath(string relativePath);

    /// <summary>Reserva la ruta de la miniatura de un original y crea su carpeta. La miniatura es un derivado:
    /// se nombra por el hash del original y se puede regenerar.</summary>
    /// <param name="organizationId">Organización dueña.</param>
    /// <param name="sha256">Hash del original.</param>
    /// <returns>Ruta relativa donde escribir la miniatura (JPEG).</returns>
    string ReserveThumbnailPath(int organizationId, string sha256);

    /// <summary>Reserva la ruta de la miniatura de un fragmento y crea su carpeta. Es un derivado regenerable,
    /// nombrado por el hash del original y el tramo: si el tramo cambia, la miniatura es otro archivo.</summary>
    /// <param name="organizationId">Organización dueña.</param>
    /// <param name="sha256">Hash del original.</param>
    /// <param name="startMs">Inicio del fragmento en milisegundos.</param>
    /// <param name="endMs">Fin del fragmento en milisegundos.</param>
    /// <returns>Ruta relativa donde escribir la miniatura (JPEG).</returns>
    string ReserveSegmentThumbnailPath(int organizationId, string sha256, int startMs, int endMs);

    /// <summary>Reserva la ruta del archivo de un video generado y crea su carpeta. Volver a renderizar la misma
    /// variante sobrescribe el archivo.</summary>
    /// <param name="organizationId">Organización dueña.</param>
    /// <param name="variantId">Video generado.</param>
    /// <returns>Ruta relativa donde escribir el video (MP4).</returns>
    string ReserveVariantPath(int organizationId, long variantId);
}

/// <summary>La herramienta de medios corrió y no pudo con el archivo, o no se pudo ejecutar. El mensaje es apto
/// para mostrar al usuario.</summary>
/// <param name="message">Qué pasó.</param>
public sealed class MediaToolException(string message) : Exception(message);

/// <summary>
/// Herramientas de procesamiento audiovisual. Es un límite externo: hoy son los ejecutables de FFmpeg. Sólo se
/// usan desde trabajos en segundo plano, nunca dentro de una solicitud web.
/// </summary>
public interface IMediaTools
{
    /// <summary>Lee las propiedades técnicas de un archivo de video o imagen.</summary>
    /// <param name="filePath">Ruta local del archivo.</param>
    /// <param name="cancellationToken">Cancelación; detiene el proceso externo.</param>
    /// <returns>Propiedades leídas.</returns>
    /// <exception cref="MediaToolException">Si el archivo no se puede leer, no tiene imagen, o la herramienta no
    /// está instalada o tardó demasiado.</exception>
    Task<MediaProbe> ProbeAsync(string filePath, CancellationToken cancellationToken);

    /// <summary>Genera una miniatura JPEG de hasta 480 píxeles de ancho.</summary>
    /// <param name="sourcePath">Ruta local del original.</param>
    /// <param name="targetPath">Ruta local donde escribir la miniatura; se sobrescribe.</param>
    /// <param name="seekMs">Instante del video del que tomar el cuadro; nulo para una imagen.</param>
    /// <param name="cancellationToken">Cancelación; detiene el proceso externo.</param>
    /// <exception cref="MediaToolException">Si no se pudo generar.</exception>
    Task CreateThumbnailAsync(string sourcePath, string targetPath, int? seekMs, CancellationToken cancellationToken);

    /// <summary>Calcula la huella visual de un cuadro: 64 bits que describen cómo cambia el brillo entre zonas
    /// vecinas de la imagen reducida a 9×8. Sobrevive a la recodificación y al cambio de tamaño.</summary>
    /// <param name="sourcePath">Ruta local del video o la imagen.</param>
    /// <param name="atMs">Instante del cuadro en milisegundos; nulo para una imagen.</param>
    /// <param name="cancellationToken">Cancelación; detiene el proceso externo.</param>
    /// <returns>La huella del cuadro.</returns>
    /// <exception cref="MediaToolException">Si no se pudo leer el cuadro.</exception>
    Task<ulong> FrameHashAsync(string sourcePath, int? atMs, CancellationToken cancellationToken);

    /// <summary>Mide el volumen del audio de un tramo de un video.</summary>
    /// <param name="sourcePath">Ruta local del video; debe tener pista de audio.</param>
    /// <param name="startMs">Inicio del tramo en milisegundos.</param>
    /// <param name="endMs">Fin del tramo en milisegundos.</param>
    /// <param name="cancellationToken">Cancelación; detiene el proceso externo.</param>
    /// <returns>Volumen medio y pico en dB; nulo si en el tramo no hay muestras de audio que medir.</returns>
    /// <exception cref="MediaToolException">Si el archivo no se puede leer.</exception>
    Task<AudioLevels?> MeasureAudioAsync(string sourcePath, int startMs, int endMs, CancellationToken cancellationToken);

    /// <summary>Extrae un cuadro de un video como JPEG reducido, para enviarlo a un modelo de IA.</summary>
    /// <param name="sourcePath">Ruta local del video.</param>
    /// <param name="targetPath">Ruta local donde escribir el cuadro; se sobrescribe.</param>
    /// <param name="atMs">Instante del cuadro en milisegundos.</param>
    /// <param name="maxSide">Tamaño máximo del lado más largo, en píxeles; el cuadro nunca se agranda.</param>
    /// <param name="cancellationToken">Cancelación; detiene el proceso externo.</param>
    /// <exception cref="MediaToolException">Si no se pudo extraer.</exception>
    Task ExtractFrameAsync(string sourcePath, string targetPath, int atMs, int maxSide, CancellationToken cancellationToken);

    /// <summary>Detecta los cortes de imagen de un video.</summary>
    /// <param name="filePath">Ruta local del video.</param>
    /// <param name="cancellationToken">Cancelación; detiene el proceso externo.</param>
    /// <returns>Tramos en orden; vacío si el video no tiene cortes.</returns>
    /// <exception cref="MediaToolException">Si el video no se puede leer, o Python o PySceneDetect no están
    /// disponibles, o la detección tardó demasiado.</exception>
    Task<List<SceneBoundary>> DetectScenesAsync(string filePath, CancellationToken cancellationToken);

    /// <summary>Transcribe la voz de un video con un modelo local.</summary>
    /// <param name="filePath">Ruta local del video; debe tener pista de audio.</param>
    /// <param name="cancellationToken">Cancelación; detiene el proceso externo.</param>
    /// <returns>Idioma, modelo usado y frases en orden; sin frases si no hay voz.</returns>
    /// <exception cref="MediaToolException">Si el audio no se puede leer, o Python o Whisper no están disponibles,
    /// o la transcripción tardó demasiado.</exception>
    Task<Transcription> TranscribeAsync(string filePath, CancellationToken cancellationToken);

    /// <summary>
    /// Renderiza un montaje: corta cada tramo de su original, lo lleva al formato de salida (escala hasta cubrir el
    /// cuadro y recorta el sobrante, misma cantidad de cuadros por segundo, audio estéreo a 48 kHz; silencio si el
    /// original no tiene audio) y los une en un MP4 (H.264 + AAC). No modifica los originales.
    /// </summary>
    /// <param name="plan">Formato de salida y tramos en orden.</param>
    /// <param name="targetPath">Ruta local donde escribir el video; se sobrescribe.</param>
    /// <param name="cancellationToken">Cancelación; detiene el proceso externo.</param>
    /// <exception cref="MediaToolException">Si no se pudo renderizar o tardó demasiado.</exception>
    Task RenderAsync(RenderPlan plan, string targetPath, CancellationToken cancellationToken);

    /// <summary>Lee la duración de un archivo de audio.</summary>
    /// <param name="filePath">Ruta local del archivo.</param>
    /// <param name="cancellationToken">Cancelación; detiene el proceso externo.</param>
    /// <returns>Duración en milisegundos.</returns>
    /// <exception cref="MediaToolException">Si el archivo no se puede leer o no tiene audio.</exception>
    Task<int> ProbeAudioAsync(string filePath, CancellationToken cancellationToken);

    /// <summary>Busca tramos en negro en un video y devuelve la duración del más largo.</summary>
    /// <param name="filePath">Ruta local del video.</param>
    /// <param name="cancellationToken">Cancelación; detiene el proceso externo.</param>
    /// <returns>Milisegundos del tramo en negro más largo; 0 si no hay ninguno.</returns>
    /// <exception cref="MediaToolException">Si el video no se puede leer.</exception>
    Task<int> LongestBlackMsAsync(string filePath, CancellationToken cancellationToken);
}

/// <summary>Reglas de qué se puede cargar en la biblioteca.</summary>
public static class MediaRules
{
    /// <summary>Tamaño máximo de un archivo: 2 GB.</summary>
    public const long MaxUploadBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>Duración mínima de un fragmento al editar sus límites: 100 ms.</summary>
    public const int MinSegmentMs = 100;

    /// <summary>Cantidad máxima de etiquetas por fragmento.</summary>
    public const int MaxTagsPerSegment = 20;

    /// <summary>Fracción máxima de bits distintos entre dos huellas visuales para considerarlas el mismo contenido.</summary>
    public const double DuplicateMaxDistance = 0.10;

    /// <summary>Diferencia máxima de duración, como fracción, entre dos videos para compararlos como duplicados.</summary>
    public const double DuplicateDurationTolerance = 0.03;

    /// <summary>Por debajo de este volumen medio (dB) un tramo se considera sin sonido.</summary>
    public const decimal AudioSilentBelowDb = -50m;

    /// <summary>Por debajo de este volumen medio (dB) un tramo se considera de audio bajo.</summary>
    public const decimal AudioLowBelowDb = -30m;

    /// <summary>Con un pico igual o mayor a este valor (dB) un tramo se considera saturado.</summary>
    public const decimal AudioClippedAtDb = -0.5m;

    /// <summary>Niveles que se guardan cuando en el tramo no hay muestras de audio que medir: silencio.</summary>
    public static readonly AudioLevels NoAudioSamples = new(-100m, -100m);

    /// <summary>
    /// Clasifica la calidad técnica del audio de un tramo a partir de su volumen. Es una medida objetiva del
    /// nivel, no un juicio sobre el contenido: no distingue voz de música ni detecta ruido de fondo. La saturación
    /// se evalúa primero porque distorsiona aunque el promedio sea normal.
    /// </summary>
    /// <param name="meanDb">Volumen medio en dB; nulo si no se midió o no hay audio.</param>
    /// <param name="peakDb">Pico en dB; nulo si no se midió o no hay audio.</param>
    /// <returns>La calidad, o nulo si no hay medición.</returns>
    public static AudioQuality? ClassifyAudio(decimal? meanDb, decimal? peakDb)
    {
        if (meanDb is null || peakDb is null)
        {
            return null;
        }

        if (meanDb < AudioSilentBelowDb)
        {
            return AudioQuality.Silent;
        }

        if (peakDb >= AudioClippedAtDb)
        {
            return AudioQuality.Clipped;
        }

        return meanDb < AudioLowBelowDb ? AudioQuality.Low : AudioQuality.Good;
    }

    /// <summary>Posiciones del video, como fracción de su duración, de las que se toma la huella.</summary>
    public static readonly double[] HashSamplePositions = [0.1, 0.3, 0.5, 0.7, 0.9];

    /// <summary>Une las huellas de los cuadros muestreados en un texto hexadecimal.</summary>
    /// <param name="frames">Huella de cada cuadro, en orden temporal.</param>
    /// <returns>16 hexadecimales por cuadro.</returns>
    public static string FormatHash(IEnumerable<ulong> frames)
    {
        return string.Concat(frames.Select(f => f.ToString("x16")));
    }

    /// <summary>Distancia entre dos huellas visuales: la fracción de bits en que difieren.</summary>
    /// <param name="left">Primera huella.</param>
    /// <param name="right">Segunda huella.</param>
    /// <returns>De 0 (idénticas) a 1; 1 si no tienen el mismo largo, porque no son comparables.</returns>
    public static double HashDistance(string left, string right)
    {
        if (left.Length != right.Length || left.Length == 0 || left.Length % 16 != 0)
        {
            return 1;
        }

        int different = 0;
        for (int i = 0; i < left.Length; i += 16)
        {
            ulong a = Convert.ToUInt64(left.Substring(i, 16), 16);
            ulong b = Convert.ToUInt64(right.Substring(i, 16), 16);
            different += System.Numerics.BitOperations.PopCount(a ^ b);
        }

        return different / (left.Length * 4.0);
    }

    private static readonly Dictionary<string, MediaFormat> Formats = new(StringComparer.OrdinalIgnoreCase)
    {
        [".mp4"] = new MediaFormat(MediaAssetKind.Video, "video/mp4"),
        [".m4v"] = new MediaFormat(MediaAssetKind.Video, "video/mp4"),
        [".mov"] = new MediaFormat(MediaAssetKind.Video, "video/quicktime"),
        [".webm"] = new MediaFormat(MediaAssetKind.Video, "video/webm"),
        [".mkv"] = new MediaFormat(MediaAssetKind.Video, "video/x-matroska"),
        [".avi"] = new MediaFormat(MediaAssetKind.Video, "video/x-msvideo"),
        [".jpg"] = new MediaFormat(MediaAssetKind.Image, "image/jpeg"),
        [".jpeg"] = new MediaFormat(MediaAssetKind.Image, "image/jpeg"),
        [".png"] = new MediaFormat(MediaAssetKind.Image, "image/png"),
        [".webp"] = new MediaFormat(MediaAssetKind.Image, "image/webp"),
    };

    /// <summary>Extensiones admitidas, con punto y en minúscula, para mostrar y para el selector de archivos.</summary>
    public static IReadOnlyCollection<string> Extensions => Formats.Keys;

    /// <summary>Busca el formato de una extensión.</summary>
    /// <param name="extension">Extensión con punto, en cualquier capitalización.</param>
    /// <param name="format">Formato, si la extensión está admitida.</param>
    /// <returns>Falso si la extensión no está admitida.</returns>
    public static bool TryGetFormat(string extension, [NotNullWhen(true)] out MediaFormat? format)
    {
        return Formats.TryGetValue(extension, out format);
    }
}

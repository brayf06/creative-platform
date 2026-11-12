using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace CreativeIntelligence.Web.Services.Media;

/// <summary>Configuración de las herramientas de medios (sección <c>MediaTools</c>). Todo tiene valor por defecto.</summary>
public sealed class MediaToolsOptions
{
    /// <summary>Ejecutable de ffmpeg: nombre si está en el PATH, o ruta completa.</summary>
    public string FfmpegPath { get; set; } = "ffmpeg";

    /// <summary>Ejecutable de ffprobe: nombre si está en el PATH, o ruta completa.</summary>
    public string FfprobePath { get; set; } = "ffprobe";

    /// <summary>Segundos máximos que puede tardar una invocación de FFmpeg antes de detenerla.</summary>
    public int TimeoutSeconds { get; set; } = 120;

    /// <summary>Intérprete de Python con PySceneDetect instalado: nombre si está en el PATH, o ruta (si es
    /// relativa, contra el directorio de trabajo). En desarrollo es el del entorno <c>tools/media/.venv</c>.</summary>
    public string PythonPath { get; set; } = "python";

    /// <summary>Carpeta de los scripts de medios (<c>tools/media</c>); si es relativa, contra el directorio de trabajo.</summary>
    public string ScriptsPath { get; set; } = "tools/media";

    /// <summary>Sensibilidad del detector de cortes; más bajo detecta más cortes.</summary>
    public double SceneThreshold { get; set; } = 27.0;

    /// <summary>Duración mínima de una escena en milisegundos; evita cortes espurios seguidos.</summary>
    public int MinSceneMs { get; set; } = 500;

    /// <summary>Segundos máximos que puede tardar la detección de escenas de un video.</summary>
    public int SceneTimeoutSeconds { get; set; } = 900;

    /// <summary>Tamaño del modelo de Whisper: <c>tiny</c>, <c>base</c>, <c>small</c>, <c>medium</c> o
    /// <c>large-v3</c>. Más grande es más preciso y más lento; corre en CPU.</summary>
    public string WhisperModel { get; set; } = "small";

    /// <summary>Idioma del audio (<c>es</c>, <c>en</c>…). Vacío para detectarlo en cada video.</summary>
    public string WhisperLanguage { get; set; } = string.Empty;

    /// <summary>Carpeta donde se descargan y guardan los modelos de Whisper; si es relativa, contra el
    /// directorio de trabajo.</summary>
    public string ModelsPath { get; set; } = "tools/media/models";

    /// <summary>Segundos máximos que puede tardar la transcripción de un video.</summary>
    public int TranscribeTimeoutSeconds { get; set; } = 1800;

    /// <summary>Segundos máximos de un render.</summary>
    public int RenderTimeoutSeconds { get; set; } = 900;
}

/// <summary>Herramientas de medios sobre los ejecutables de FFmpeg y el script de PySceneDetect, invocados como
/// procesos externos.</summary>
/// <param name="options">Rutas de los ejecutables, parámetros de detección y tiempos máximos.</param>
public sealed partial class FfmpegMediaTools(IOptions<MediaToolsOptions> options) : IMediaTools
{
    /// <inheritdoc />
    public async Task<MediaProbe> ProbeAsync(string filePath, CancellationToken cancellationToken)
    {
        string json = await RunAsync(
            options.Value.FfprobePath,
            ["-v", "error", "-print_format", "json", "-show_format", "-show_streams", filePath],
            "FFmpeg",
            options.Value.TimeoutSeconds,
            cancellationToken);

        ProbeOutput? output = JsonSerializer.Deserialize<ProbeOutput>(json);
        ProbeStream? video = output?.Streams?.FirstOrDefault(s => s.CodecType == "video");
        if (video is null || video.Width is null || video.Height is null)
        {
            throw new MediaToolException("El archivo no contiene video ni imagen legibles.");
        }

        ProbeStream? audio = output!.Streams!.FirstOrDefault(s => s.CodecType == "audio");

        // Un video grabado con el teléfono de costado guarda la rotación aparte: se informa el tamaño como se ve.
        bool rotated = video.SideData?.Any(d => d.Rotation is not null && Math.Abs(d.Rotation.Value) % 180 == 90) == true;
        int width = rotated ? video.Height.Value : video.Width.Value;
        int height = rotated ? video.Width.Value : video.Height.Value;

        int? durationMs = double.TryParse(output.Format?.Duration, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds)
            ? (int)Math.Round(seconds * 1000)
            : null;
        return new MediaProbe(
            durationMs,
            width,
            height,
            ParseRate(video.AvgFrameRate) ?? ParseRate(video.RFrameRate),
            video.CodecName,
            audio?.CodecName,
            output.Format?.FormatName);
    }

    /// <inheritdoc />
    public async Task CreateThumbnailAsync(string sourcePath, string targetPath, int? seekMs, CancellationToken cancellationToken)
    {
        List<string> arguments = ["-y", "-v", "error"];
        if (seekMs is not null)
        {
            arguments.AddRange(["-ss", (seekMs.Value / 1000.0).ToString("0.###", CultureInfo.InvariantCulture)]);
        }

        // -f image2 porque la ruta de destino no siempre termina en una extensión que ffmpeg reconozca.
        arguments.AddRange(
        [
            "-i", sourcePath, "-frames:v", "1", "-vf", "scale='min(480,iw)':-2", "-q:v", "4", "-f", "image2",
            "-c:v", "mjpeg", targetPath,
        ]);
        await RunAsync(options.Value.FfmpegPath, arguments, "FFmpeg", options.Value.TimeoutSeconds, cancellationToken);
        if (!File.Exists(targetPath) || new FileInfo(targetPath).Length == 0)
        {
            throw new MediaToolException("No se pudo generar la miniatura.");
        }
    }

    /// <inheritdoc />
    public async Task<AudioLevels?> MeasureAudioAsync(string sourcePath, int startMs, int endMs, CancellationToken cancellationToken)
    {
        // volumedetect no modifica nada: analiza el audio decodificado e informa los niveles por la salida de error.
        ProcessOutput result = await RunCoreAsync(
            options.Value.FfmpegPath,
            [
                "-hide_banner", "-nostats", "-v", "info",
                "-ss", (startMs / 1000.0).ToString("0.###", CultureInfo.InvariantCulture),
                "-t", ((endMs - startMs) / 1000.0).ToString("0.###", CultureInfo.InvariantCulture),
                "-i", sourcePath, "-vn", "-af", "volumedetect", "-f", "null", "-",
            ],
            "FFmpeg",
            options.Value.TimeoutSeconds,
            cancellationToken);
        decimal? mean = ParseDecibels(result.Error, "mean_volume");
        decimal? peak = ParseDecibels(result.Error, "max_volume");
        return mean is null || peak is null ? null : new AudioLevels(mean.Value, peak.Value);
    }

    /// <inheritdoc />
    public async Task<ulong> FrameHashAsync(string sourcePath, int? atMs, CancellationToken cancellationToken)
    {
        List<string> arguments = ["-v", "error"];
        if (atMs is not null)
        {
            arguments.AddRange(["-ss", (atMs.Value / 1000.0).ToString("0.###", CultureInfo.InvariantCulture)]);
        }

        // 9 columnas por 8 filas en gris: 72 bytes de brillo, crudos, por la salida estándar.
        arguments.AddRange(["-i", sourcePath, "-frames:v", "1", "-vf", "scale=9:8:flags=area,format=gray", "-f", "rawvideo", "-"]);
        byte[] pixels = await RunBinaryAsync(options.Value.FfmpegPath, arguments, cancellationToken);
        if (pixels.Length < 72)
        {
            throw new MediaToolException("No se pudo leer un cuadro para calcular la huella visual.");
        }

        // Cada bit dice si un píxel es más claro que su vecino de la derecha (8 comparaciones por fila, 8 filas).
        ulong hash = 0;
        for (int row = 0; row < 8; row++)
        {
            for (int column = 0; column < 8; column++)
            {
                hash <<= 1;
                if (pixels[(row * 9) + column] > pixels[(row * 9) + column + 1])
                {
                    hash |= 1;
                }
            }
        }

        return hash;
    }

    /// <inheritdoc />
    public async Task ExtractFrameAsync(string sourcePath, string targetPath, int atMs, int maxSide, CancellationToken cancellationToken)
    {
        // Se limita el lado más largo sin agrandar: el costo en tokens de una imagen depende de su tamaño.
        string side = maxSide.ToString(CultureInfo.InvariantCulture);
        string scale = $"scale='if(gt(iw,ih),min({side},iw),-2)':'if(gt(iw,ih),-2,min({side},ih))'";
        await RunAsync(
            options.Value.FfmpegPath,
            [
                "-y", "-v", "error", "-ss", (atMs / 1000.0).ToString("0.###", CultureInfo.InvariantCulture),
                "-i", sourcePath, "-frames:v", "1", "-vf", scale, "-q:v", "5", "-f", "image2", "-c:v", "mjpeg", targetPath,
            ],
            "FFmpeg",
            options.Value.TimeoutSeconds,
            cancellationToken);
        if (!File.Exists(targetPath) || new FileInfo(targetPath).Length == 0)
        {
            throw new MediaToolException("No se pudo extraer un cuadro del video.");
        }
    }

    /// <inheritdoc />
    public async Task<List<SceneBoundary>> DetectScenesAsync(string filePath, CancellationToken cancellationToken)
    {
        MediaToolsOptions settings = options.Value;
        string json = await RunScriptAsync(
            "detect_scenes.py",
            [
                filePath,
                "--threshold", settings.SceneThreshold.ToString(CultureInfo.InvariantCulture),
                "--min-scene-ms", settings.MinSceneMs.ToString(CultureInfo.InvariantCulture),
            ],
            "PySceneDetect",
            settings.SceneTimeoutSeconds,
            cancellationToken);
        SceneOutput? result = json.Length > 0 ? JsonSerializer.Deserialize<SceneOutput>(json) : null;
        if (result?.Scenes is null)
        {
            throw new MediaToolException("La detección de escenas no devolvió un resultado válido.");
        }

        return result.Scenes.Select(s => new SceneBoundary(s.StartMs, s.EndMs)).ToList();
    }

    /// <inheritdoc />
    public async Task<Transcription> TranscribeAsync(string filePath, CancellationToken cancellationToken)
    {
        MediaToolsOptions settings = options.Value;
        List<string> arguments =
        [
            filePath,
            "--model", settings.WhisperModel,
            "--model-dir", Path.GetFullPath(settings.ModelsPath),
            "--ffmpeg", settings.FfmpegPath,
        ];
        if (settings.WhisperLanguage.Length > 0)
        {
            arguments.AddRange(["--language", settings.WhisperLanguage]);
        }

        string json = await RunScriptAsync("transcribe.py", arguments, "Whisper", settings.TranscribeTimeoutSeconds, cancellationToken);
        TranscriptOutput? result = json.Length > 0 ? JsonSerializer.Deserialize<TranscriptOutput>(json) : null;
        if (result?.Lines is null || result.Model is null)
        {
            throw new MediaToolException("La transcripción no devolvió un resultado válido.");
        }

        return new Transcription(
            result.Language,
            result.Model,
            result.Lines.Select(l => new TranscriptLineItem(l.StartMs, l.EndMs, l.Text ?? string.Empty)).ToList());
    }

    // Corre un script de tools/media con el Python configurado y devuelve su última línea si es JSON; vacío si no.
    /// <inheritdoc />
    public async Task RenderAsync(RenderPlan plan, string targetPath, CancellationToken cancellationToken)
    {
        List<string> arguments = ["-y", "-v", "error"];
        foreach (RenderClip clip in plan.Clips)
        {
            arguments.AddRange(["-ss", Seconds(clip.StartMs), "-t", Seconds(clip.EndMs - clip.StartMs), "-i", clip.SourcePath]);
        }

        // Un original sin audio recibe una entrada de silencio de su misma duración: la unión exige que todos los
        // tramos tengan video y audio.
        Dictionary<int, int> silenceInputs = [];
        for (int i = 0; i < plan.Clips.Count; i++)
        {
            if (!plan.Clips[i].HasAudio)
            {
                silenceInputs[i] = plan.Clips.Count + silenceInputs.Count;
                arguments.AddRange(
                    ["-f", "lavfi", "-t", Seconds(plan.Clips[i].EndMs - plan.Clips[i].StartMs), "-i", "anullsrc=r=48000:cl=stereo"]);
            }
        }

        int musicInput = plan.Clips.Count + silenceInputs.Count;
        if (plan.MusicPath is not null)
        {
            // La música se repite si es más corta que el video; después se corta a su duración.
            arguments.AddRange(["-stream_loop", "-1", "-i", plan.MusicPath]);
        }

        const string audioFormat = "aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo";
        string fade = Seconds(plan.FadeMs);
        StringBuilder filter = new();
        StringBuilder joined = new();
        for (int i = 0; i < plan.Clips.Count; i++)
        {
            int clipMs = plan.Clips[i].EndMs - plan.Clips[i].StartMs;
            string duration = Seconds(clipMs);
            int audioInput = silenceInputs.TryGetValue(i, out int silence) ? silence : i;

            // El fundido va en el fin de cada tramo y en el inicio del siguiente; un tramo muy corto no lo lleva.
            bool fades = plan.FadeMs > 0 && clipMs >= plan.FadeMs * 3;
            string fadeIn = fades && i > 0 ? $":st=0:d={fade}" : string.Empty;
            string fadeOut = fades && i < plan.Clips.Count - 1 ? $":st={Seconds(clipMs - plan.FadeMs)}:d={fade}" : string.Empty;
            filter.Append(CultureInfo.InvariantCulture,
                $"[{i}:v]scale={plan.Width}:{plan.Height}:force_original_aspect_ratio=increase,crop={plan.Width}:{plan.Height},setsar=1,");
            filter.Append(CultureInfo.InvariantCulture, $"fps={plan.FrameRate},format=yuv420p,trim=duration={duration},setpts=PTS-STARTPTS");
            filter.Append(fadeIn.Length > 0 ? $",fade=t=in{fadeIn}" : string.Empty);
            filter.Append(fadeOut.Length > 0 ? $",fade=t=out{fadeOut}" : string.Empty);
            filter.Append(CultureInfo.InvariantCulture, $"[v{i}];[{audioInput}:a]{audioFormat},apad,atrim=duration={duration},asetpts=PTS-STARTPTS");
            filter.Append(fadeIn.Length > 0 ? $",afade=t=in{fadeIn}" : string.Empty);
            filter.Append(fadeOut.Length > 0 ? $",afade=t=out{fadeOut}" : string.Empty);
            filter.Append(CultureInfo.InvariantCulture, $"[a{i}];");
            joined.Append(CultureInfo.InvariantCulture, $"[v{i}][a{i}]");
        }

        filter.Append(joined).Append(CultureInfo.InvariantCulture, $"concat=n={plan.Clips.Count}:v=1:a=1[vc][ac]");
        string video = "[vc]";
        string audio = "[ac]";
        string? workingDirectory = null;
        if (plan.TextFilePath is not null)
        {
            // FFmpeg corre en la carpeta del archivo de textos y lo nombra sin ruta: una ruta de Windows dentro
            // de un filtro exige escapar los dos puntos y las barras.
            workingDirectory = Path.GetDirectoryName(plan.TextFilePath);
            filter.Append(CultureInfo.InvariantCulture, $";[vc]subtitles=filename={Path.GetFileName(plan.TextFilePath)}[vt]");
            video = "[vt]";
        }

        if (plan.MusicPath is not null)
        {
            int totalMs = plan.Clips.Sum(c => c.EndMs - c.StartMs);
            string volume = plan.MusicVolume.ToString("0.##", CultureInfo.InvariantCulture);
            filter.Append(CultureInfo.InvariantCulture,
                $";[{musicInput}:a]{audioFormat},volume={volume},atrim=duration={Seconds(totalMs)},afade=t=out:st={Seconds(Math.Max(0, totalMs - 1000))}:d=1[m]");
            filter.Append(";[ac][m]amix=inputs=2:duration=first:normalize=0[am]");
            audio = "[am]";
        }

        arguments.AddRange(
        [
            "-filter_complex", filter.ToString(), "-map", video, "-map", audio,
            "-c:v", "libx264", "-preset", "veryfast", "-crf", "20", "-c:a", "aac", "-b:a", "160k",
            "-movflags", "+faststart", "-f", "mp4", targetPath,
        ]);
        try
        {
            await RunAsync(
                options.Value.FfmpegPath, arguments, "FFmpeg", options.Value.RenderTimeoutSeconds, cancellationToken, workingDirectory);
        }
        catch (MediaToolException exception)
        {
            throw new MediaToolException($"No se pudo renderizar el video. {exception.Message}");
        }

        if (!File.Exists(targetPath) || new FileInfo(targetPath).Length == 0)
        {
            throw new MediaToolException("El render no generó ningún archivo.");
        }
    }

    /// <inheritdoc />
    public async Task<int> ProbeAudioAsync(string filePath, CancellationToken cancellationToken)
    {
        string json = await RunAsync(
            options.Value.FfprobePath,
            ["-v", "error", "-print_format", "json", "-show_format", "-show_streams", filePath],
            "FFmpeg",
            options.Value.TimeoutSeconds,
            cancellationToken);
        ProbeOutput? output = JsonSerializer.Deserialize<ProbeOutput>(json);
        if (output?.Streams?.Any(s => s.CodecType == "audio") != true
            || !double.TryParse(output.Format?.Duration, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds)
            || seconds <= 0)
        {
            throw new MediaToolException("El archivo no contiene audio legible.");
        }

        return (int)Math.Round(seconds * 1000);
    }

    /// <inheritdoc />
    public async Task<int> LongestBlackMsAsync(string filePath, CancellationToken cancellationToken)
    {
        // blackdetect no modifica nada: informa por la salida de error cada tramo en negro que encuentra.
        ProcessOutput result = await RunCoreAsync(
            options.Value.FfmpegPath,
            ["-hide_banner", "-nostats", "-v", "info", "-i", filePath, "-vf", "blackdetect=d=0.1:pix_th=0.10", "-an", "-f", "null", "-"],
            "FFmpeg",
            options.Value.RenderTimeoutSeconds,
            cancellationToken);
        return BlackDuration().Matches(result.Error)
            .Select(m => (int)Math.Round(double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * 1000))
            .DefaultIfEmpty(0)
            .Max();
    }

    [GeneratedRegex(@"black_duration:([0-9.]+)")]
    private static partial Regex BlackDuration();
    private static string Seconds(int milliseconds)
    {
        return (milliseconds / 1000.0).ToString("0.###", CultureInfo.InvariantCulture);
    }

    private async Task<string> RunScriptAsync(
        string scriptName, IReadOnlyList<string> arguments, string toolName, int timeoutSeconds, CancellationToken cancellationToken)
    {
        MediaToolsOptions settings = options.Value;
        string script = Path.Combine(Path.GetFullPath(settings.ScriptsPath), scriptName);
        if (!File.Exists(script))
        {
            throw new MediaToolException($"No se encontró el script {scriptName} en {Path.GetDirectoryName(script)}.");
        }

        // Una ruta con separadores es un archivo (se resuelve contra el directorio de trabajo); sin ellos, un
        // nombre a buscar en el PATH.
        string python = settings.PythonPath.Contains('/') || settings.PythonPath.Contains('\\')
            ? Path.GetFullPath(settings.PythonPath)
            : settings.PythonPath;
        string output = await RunAsync(python, [script, .. arguments], toolName, timeoutSeconds, cancellationToken);
        string lastLine = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault()
            ?? string.Empty;
        return lastLine.StartsWith('{') ? lastLine : string.Empty;
    }

    // Busca una línea como "mean_volume: -23.5 dB". El silencio digital absoluto se informa como "-inf".
    private static decimal? ParseDecibels(string output, string name)
    {
        Match match = Regex.Match(output, name + @":\s*(-inf|-?\d+(\.\d+)?)\s*dB");
        if (!match.Success)
        {
            return null;
        }

        return match.Groups[1].Value == "-inf"
            ? -100m
            : Math.Max(-100m, decimal.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
    }

    private static decimal? ParseRate(string? rate)
    {
        string[] parts = (rate ?? string.Empty).Split('/');
        if (parts.Length == 2
            && decimal.TryParse(parts[0], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal numerator)
            && decimal.TryParse(parts[1], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal denominator)
            && numerator > 0
            && denominator > 0)
        {
            return Math.Round(numerator / denominator, 3);
        }

        return null;
    }

    // Igual que RunAsync pero devuelve la salida estándar como bytes, para datos crudos.
    private async Task<byte[]> RunBinaryAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        ProcessStartInfo start = new(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = new() { StartInfo = start };
        try
        {
            process.Start();
        }
        catch (Win32Exception)
        {
            throw new MediaToolException($"No se encontró '{executable}'. Instalá FFmpeg o configurá su ruta en MediaTools.");
        }

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));
        using MemoryStream output = new();
        Task copy = process.StandardOutput.BaseStream.CopyToAsync(output, timeout.Token);
        Task<string> standardError = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            await copy;
            await standardError;
            if (process.ExitCode != 0)
            {
                throw new MediaToolException("FFmpeg no pudo leer el archivo para calcular la huella visual.");
            }

            return output.ToArray();
        }
        catch (OperationCanceledException)
        {
            process.Kill(true);
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            throw new MediaToolException($"FFmpeg tardó más de {options.Value.TimeoutSeconds} segundos y se detuvo.");
        }
    }

    private static async Task<string> RunAsync(
        string executable, IReadOnlyList<string> arguments, string toolName, int timeoutSeconds, CancellationToken cancellationToken,
        string? workingDirectory = null)
    {
        return (await RunCoreAsync(executable, arguments, toolName, timeoutSeconds, cancellationToken, workingDirectory)).Standard;
    }

    // Corre un proceso externo y devuelve sus dos salidas; lanza si termina con error o tarda demasiado.
    private static async Task<ProcessOutput> RunCoreAsync(
        string executable, IReadOnlyList<string> arguments, string toolName, int timeoutSeconds, CancellationToken cancellationToken,
        string? workingDirectory = null)
    {
        ProcessStartInfo start = new(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? string.Empty,
        };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = new() { StartInfo = start };
        try
        {
            process.Start();
        }
        catch (Win32Exception)
        {
            throw new MediaToolException(
                $"No se encontró '{executable}'. Instalá {toolName} o configurá su ruta en MediaTools.");
        }

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(timeout.Token);
        Task<string> standardError = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            string output = await standardOutput;
            string error = await standardError;
            if (process.ExitCode != 0)
            {
                string detail = error.Trim().Split('\n').LastOrDefault()?.Trim() ?? string.Empty;
                throw new MediaToolException($"{toolName} no pudo leer el archivo. {detail}".Trim());
            }

            return new ProcessOutput(output, error);
        }
        catch (OperationCanceledException)
        {
            process.Kill(true);
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            throw new MediaToolException($"{toolName} tardó más de {timeoutSeconds} segundos y se detuvo.");
        }
    }

    private sealed record ProcessOutput(string Standard, string Error);

    private sealed class TranscriptOutput
    {
        [JsonPropertyName("language")]
        public string? Language { get; set; }

        [JsonPropertyName("model")]
        public string? Model { get; set; }

        [JsonPropertyName("lines")]
        public List<TranscriptOutputLine>? Lines { get; set; }
    }

    private sealed class TranscriptOutputLine
    {
        [JsonPropertyName("start_ms")]
        public int StartMs { get; set; }

        [JsonPropertyName("end_ms")]
        public int EndMs { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }

    private sealed class SceneOutput
    {
        [JsonPropertyName("scenes")]
        public List<SceneRange>? Scenes { get; set; }
    }

    private sealed class SceneRange
    {
        [JsonPropertyName("start_ms")]
        public int StartMs { get; set; }

        [JsonPropertyName("end_ms")]
        public int EndMs { get; set; }
    }

    private sealed class ProbeOutput
    {
        [JsonPropertyName("streams")]
        public List<ProbeStream>? Streams { get; set; }

        [JsonPropertyName("format")]
        public ProbeFormat? Format { get; set; }
    }

    private sealed class ProbeStream
    {
        [JsonPropertyName("codec_type")]
        public string? CodecType { get; set; }

        [JsonPropertyName("codec_name")]
        public string? CodecName { get; set; }

        [JsonPropertyName("width")]
        public int? Width { get; set; }

        [JsonPropertyName("height")]
        public int? Height { get; set; }

        [JsonPropertyName("avg_frame_rate")]
        public string? AvgFrameRate { get; set; }

        [JsonPropertyName("r_frame_rate")]
        public string? RFrameRate { get; set; }

        [JsonPropertyName("side_data_list")]
        public List<ProbeSideData>? SideData { get; set; }
    }

    private sealed class ProbeSideData
    {
        [JsonPropertyName("rotation")]
        public double? Rotation { get; set; }
    }

    private sealed class ProbeFormat
    {
        [JsonPropertyName("format_name")]
        public string? FormatName { get; set; }

        [JsonPropertyName("duration")]
        public string? Duration { get; set; }
    }
}

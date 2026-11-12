using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace CreativeIntelligence.Web.Services.Media;

/// <summary>Transcripción de los videos de la organización activa: consulta y pedido.</summary>
/// <param name="db">Contexto de datos.</param>
/// <param name="jobs">Cola de trabajos.</param>
/// <param name="audit">Auditoría.</param>
public sealed class TranscriptService(AppDbContext db, JobService jobs, AuditService audit)
{
    /// <summary>Devuelve la transcripción de un video, en orden temporal, con su estado. No escribe.</summary>
    /// <param name="assetId">Video.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Estado, idioma, modelo y frases.</returns>
    /// <exception cref="BusinessException">404 <c>MEDIA_NOT_FOUND</c> si el archivo no existe en la organización
    /// activa.</exception>
    public async Task<TranscriptView> GetAsync(long assetId, CancellationToken cancellationToken)
    {
        TranscriptHeader header = await db.MediaAssets.AsNoTracking()
            .Where(a => a.Id == assetId)
            .Select(a => new TranscriptHeader
            {
                HasAudio = a.AudioCodec != null,
                Status = a.TranscriptStatus,
                Error = a.TranscriptError,
                Language = a.TranscriptLanguage,
                Model = a.TranscriptModel,
            })
            .FirstOrDefaultAsync(cancellationToken) ?? throw NotFound();

        List<TranscriptLineItem> lines = await db.MediaTranscriptLines.AsNoTracking()
            .Where(l => l.MediaAssetId == assetId)
            .OrderBy(l => l.StartMs)
            .Select(l => new TranscriptLineItem(l.StartMs, l.EndMs, l.Text))
            .ToListAsync(cancellationToken);
        return new TranscriptView(assetId, header.HasAudio, header.Status, header.Error, header.Language, header.Model, lines);
    }

    /// <summary>
    /// Pide (o vuelve a pedir) la transcripción de un video. Deja la transcripción <c>Pending</c>, encola el
    /// trabajo y lo audita, en una transacción. Al ejecutarse, el trabajo reemplaza la transcripción anterior.
    /// </summary>
    /// <param name="assetId">Video.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <exception cref="BusinessException">404 <c>MEDIA_NOT_FOUND</c> si no existe en la organización activa;
    /// 409 <c>MEDIA_NOT_READY</c> si todavía no terminó su análisis técnico; 409 <c>NO_AUDIO</c> si es una imagen
    /// o un video sin pista de audio.</exception>
    public async Task RequestAsync(long assetId, CancellationToken cancellationToken)
    {
        MediaAsset asset = await db.MediaAssets.FirstOrDefaultAsync(a => a.Id == assetId, cancellationToken) ?? throw NotFound();
        if (asset.ProcessingStatus != MediaProcessingStatus.Ready)
        {
            throw new BusinessException(409, "El archivo todavía no terminó su análisis técnico.", "MEDIA_NOT_READY");
        }

        if (asset.Kind != MediaAssetKind.Video || asset.AudioCodec is null)
        {
            throw new BusinessException(409, "El archivo no tiene audio para transcribir.", "NO_AUDIO");
        }

        await db.InTransactionAsync(
            async () =>
            {
                asset.TranscriptStatus = MediaProcessingStatus.Pending;
                asset.TranscriptError = null;
                audit.Record("media.transcribe", nameof(MediaAsset), assetId.ToString(), "Pidió transcribir el video.");
                await db.SaveChangesAsync(cancellationToken);
                await EnqueueAsync(assetId, Guid.NewGuid().ToString("N"), cancellationToken);
                return true;
            },
            cancellationToken);
    }

    /// <summary>Encola la transcripción de un video. Lo usa el análisis técnico al terminar bien.</summary>
    /// <param name="assetId">Video.</param>
    /// <param name="run">Distingue la transcripción inicial (<c>1</c>) de cada pedido manual.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>El trabajo encolado o el existente.</returns>
    public Task<EnqueueResult> EnqueueAsync(long assetId, string run, CancellationToken cancellationToken)
    {
        return jobs.EnqueueAsync(
            MediaTranscribeJobHandler.JobType,
            $"{MediaTranscribeJobHandler.JobType}:{assetId}:{run}",
            JsonSerializer.Serialize(new MediaProbePayload(assetId)),
            cancellationToken);
    }

    /// <summary>
    /// Texto que se dice dentro de un tramo: las frases cuyo punto medio cae en <c>[startMs, endMs)</c>, unidas
    /// por un espacio. Con ese criterio cada frase pertenece a un solo tramo aunque cruce un corte.
    /// </summary>
    /// <param name="lines">Frases del video.</param>
    /// <param name="startMs">Inicio del tramo, inclusive.</param>
    /// <param name="endMs">Fin del tramo, exclusive.</param>
    /// <returns>El texto del tramo; vacío si no se dice nada.</returns>
    public static string TextBetween(IEnumerable<TranscriptLineItem> lines, int startMs, int endMs)
    {
        return string.Join(
            " ",
            lines.Where(l => (l.StartMs + l.EndMs) / 2 >= startMs && (l.StartMs + l.EndMs) / 2 < endMs).Select(l => l.Text));
    }

    private static BusinessException NotFound()
    {
        return new BusinessException(404, "El archivo no existe.", "MEDIA_NOT_FOUND");
    }
}

/// <summary>
/// Transcripción de un video con Whisper local. Reemplaza las frases anteriores del video y guarda idioma y
/// modelo, en una transacción; además registra la ejecución en el uso de IA (costo 0, con su duración) para que
/// el tiempo de cómputo local quede medido. Nunca modifica el original. Es idempotente.
/// </summary>
/// <param name="db">Contexto de datos, con la organización del trabajo activa.</param>
/// <param name="storage">Almacenamiento.</param>
/// <param name="tools">Herramientas de medios.</param>
/// <param name="usage">Registro de uso de IA.</param>
/// <param name="index">Índice de búsqueda, para reindexar el video al terminar.</param>
public sealed class MediaTranscribeJobHandler(
    AppDbContext db, IMediaStorage storage, IMediaTools tools, AiUsageService usage, SegmentIndexService index)
    : IJobHandler
{
    /// <summary>Tipo de trabajo que atiende.</summary>
    public const string JobType = "media.transcribe";

    /// <inheritdoc />
    public string Type => JobType;

    /// <inheritdoc />
    /// <exception cref="PermanentJobException">Si el video ya no existe o la herramienta no puede transcribirlo;
    /// en ese caso la transcripción queda <c>Failed</c> con el motivo.</exception>
    public async Task ExecuteAsync(JobExecution execution, CancellationToken cancellationToken)
    {
        MediaProbePayload? payload = JsonSerializer.Deserialize<MediaProbePayload>(execution.Job.PayloadJson);
        long assetId = payload?.AssetId ?? 0;
        MediaAsset asset = await db.MediaAssets.FirstOrDefaultAsync(a => a.Id == assetId, cancellationToken)
            ?? throw new PermanentJobException("El video a transcribir ya no existe.");

        try
        {
            await execution.ReportAsync(5, "Transcribiendo el audio", cancellationToken);
            Stopwatch stopwatch = Stopwatch.StartNew();
            Transcription transcription = await tools.TranscribeAsync(storage.GetLocalPath(asset.StoragePath), cancellationToken);
            stopwatch.Stop();

            await execution.ReportAsync(90, "Guardando la transcripción", cancellationToken);
            await db.InTransactionAsync(
                async () =>
                {
                    await db.MediaTranscriptLines.Where(l => l.MediaAssetId == assetId).ExecuteDeleteAsync(cancellationToken);
                    db.MediaTranscriptLines.AddRange(transcription.Lines.Select(l => new MediaTranscriptLine
                    {
                        MediaAssetId = assetId,
                        StartMs = l.StartMs,
                        EndMs = l.EndMs,
                        Text = l.Text.Length > 1000 ? l.Text[..1000] : l.Text,
                    }));
                    asset.TranscriptStatus = MediaProcessingStatus.Ready;
                    asset.TranscriptError = null;
                    asset.TranscriptLanguage = transcription.Language;
                    asset.TranscriptModel = transcription.Model;
                    await db.SaveChangesAsync(cancellationToken);
                    await usage.RecordAsync(
                        new AiUsageEntry(
                            "media.transcription", AiUsageService.LocalProvider, transcription.Model, "n/a", 0, 0, 0m,
                            (int)stopwatch.ElapsedMilliseconds, execution.Job.Id),
                        cancellationToken);
                    await index.EnqueueIfConfiguredAsync(assetId, cancellationToken);
                    return true;
                },
                cancellationToken);
        }
        catch (MediaToolException exception)
        {
            asset.TranscriptStatus = MediaProcessingStatus.Failed;
            asset.TranscriptError = exception.Message.Length > 1000 ? exception.Message[..1000] : exception.Message;
            await db.SaveChangesAsync(CancellationToken.None);
            throw new PermanentJobException(exception.Message);
        }
    }
}

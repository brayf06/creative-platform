using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace CreativeIntelligence.Web.Services.Media;

/// <summary>
/// Análisis técnico de un archivo recién ingresado: lee sus propiedades con ffprobe y genera la miniatura.
/// Calcula además la huella visual y, si un archivo anterior de la organización tiene casi la misma, lo marca
/// como duplicado visual de aquél. Escribe las propiedades, la miniatura, la huella y el estado; nunca toca el
/// original. Si es un
/// video al que nunca se le pidieron escenas, encola su detección en la misma transacción, y lo mismo con la
/// transcripción si tiene audio. Es idempotente:
/// volver a ejecutarlo recalcula lo mismo y sobrescribe la miniatura.
/// </summary>
/// <param name="db">Contexto de datos, con la organización del trabajo activa.</param>
/// <param name="storage">Almacenamiento, para ubicar el original y la miniatura.</param>
/// <param name="tools">Herramientas de medios.</param>
/// <param name="scenes">Escenas, para encolar su detección cuando el video queda listo.</param>
/// <param name="transcripts">Transcripciones, para encolar la del video si tiene audio.</param>
public sealed class MediaProbeJobHandler(
    AppDbContext db, IMediaStorage storage, IMediaTools tools, SceneService scenes, TranscriptService transcripts)
    : IJobHandler
{
    /// <summary>Tipo de trabajo que atiende.</summary>
    public const string JobType = "media.probe";

    /// <inheritdoc />
    public string Type => JobType;

    /// <inheritdoc />
    /// <exception cref="PermanentJobException">Si el archivo ya no existe o FFmpeg no puede leerlo; en ese caso
    /// el archivo queda en estado <c>Failed</c> con el motivo.</exception>
    public async Task ExecuteAsync(JobExecution execution, CancellationToken cancellationToken)
    {
        MediaProbePayload? payload = JsonSerializer.Deserialize<MediaProbePayload>(execution.Job.PayloadJson);
        long assetId = payload?.AssetId ?? 0;
        MediaAsset asset = await db.MediaAssets.FirstOrDefaultAsync(a => a.Id == assetId, cancellationToken)
            ?? throw new PermanentJobException("El archivo a analizar ya no existe.");

        try
        {
            string sourcePath = storage.GetLocalPath(asset.StoragePath);
            await execution.ReportAsync(10, "Leyendo propiedades técnicas", cancellationToken);
            MediaProbe probe = await tools.ProbeAsync(sourcePath, cancellationToken);

            await execution.ReportAsync(60, "Generando miniatura", cancellationToken);
            bool isVideo = asset.Kind == MediaAssetKind.Video;
            string thumbnailPath = storage.ReserveThumbnailPath(asset.OrganizationId, asset.Sha256);

            // El cuadro del primer segundo evita el negro inicial; en clips muy cortos, el del medio.
            int? seekMs = isVideo ? Math.Min(1000, (probe.DurationMs ?? 0) / 2) : null;
            await tools.CreateThumbnailAsync(sourcePath, storage.GetLocalPath(thumbnailPath), seekMs, cancellationToken);

            asset.DurationMs = isVideo ? probe.DurationMs : null;
            asset.Width = probe.Width;
            asset.Height = probe.Height;
            asset.FrameRate = isVideo ? probe.FrameRate : null;
            asset.VideoCodec = probe.VideoCodec;
            asset.AudioCodec = probe.AudioCodec;
            asset.Container = probe.Container;
            asset.ThumbnailPath = thumbnailPath;

            await execution.ReportAsync(80, "Buscando duplicados visuales", cancellationToken);
            List<ulong> frameHashes = [];
            if (isVideo && probe.DurationMs > 0)
            {
                foreach (double position in MediaRules.HashSamplePositions)
                {
                    frameHashes.Add(await tools.FrameHashAsync(sourcePath, (int)(probe.DurationMs.Value * position), cancellationToken));
                }
            }
            else
            {
                frameHashes.Add(await tools.FrameHashAsync(sourcePath, null, cancellationToken));
            }

            asset.PerceptualHash = MediaRules.FormatHash(frameHashes);
            asset.DuplicateOfAssetId = await FindVisualDuplicateAsync(asset, cancellationToken);
            asset.ProcessingStatus = MediaProcessingStatus.Ready;
            asset.ProcessingError = null;

            // Sólo la primera vez: un reanálisis técnico no debe pisar escenas ya detectadas o corregidas.
            bool detectScenes = isVideo && asset.DurationMs > 0 && asset.SceneStatus is null;
            if (detectScenes)
            {
                asset.SceneStatus = MediaProcessingStatus.Pending;
            }

            bool transcribe = isVideo && asset.AudioCodec is not null && asset.TranscriptStatus is null;
            if (transcribe)
            {
                asset.TranscriptStatus = MediaProcessingStatus.Pending;
            }

            await db.InTransactionAsync(
                async () =>
                {
                    await db.SaveChangesAsync(cancellationToken);
                    if (detectScenes)
                    {
                        await scenes.EnqueueAsync(asset.Id, "1", false, cancellationToken);
                    }

                    if (transcribe)
                    {
                        await transcripts.EnqueueAsync(asset.Id, "1", cancellationToken);
                    }

                    return true;
                },
                cancellationToken);
        }
        catch (MediaToolException exception)
        {
            asset.ProcessingStatus = MediaProcessingStatus.Failed;
            asset.ProcessingError = exception.Message.Length > 1000 ? exception.Message[..1000] : exception.Message;
            await db.SaveChangesAsync(CancellationToken.None);
            throw new PermanentJobException(exception.Message);
        }
    }

    // Busca, entre los archivos anteriores de la organización que no son a su vez duplicados, uno del mismo tipo,
    // de duración parecida y con una huella casi igual. Devuelve el más antiguo, o nulo si no hay.
    private async Task<long?> FindVisualDuplicateAsync(MediaAsset asset, CancellationToken cancellationToken)
    {
        long assetId = asset.Id;
        MediaAssetKind kind = asset.Kind;
        IQueryable<MediaAsset> candidates = db.MediaAssets.AsNoTracking()
            .Where(a => a.Id < assetId && a.Kind == kind && a.PerceptualHash != null && a.DuplicateOfAssetId == null);
        if (asset.DurationMs is not null)
        {
            int minimum = (int)(asset.DurationMs.Value * (1 - MediaRules.DuplicateDurationTolerance));
            int maximum = (int)(asset.DurationMs.Value * (1 + MediaRules.DuplicateDurationTolerance));
            candidates = candidates.Where(a => a.DurationMs >= minimum && a.DurationMs <= maximum);
        }

        List<HashCandidate> hashes = await candidates
            .OrderBy(a => a.Id)
            .Select(a => new HashCandidate { Id = a.Id, Hash = a.PerceptualHash! })
            .ToListAsync(cancellationToken);
        string own = asset.PerceptualHash!;
        HashCandidate? match = hashes.FirstOrDefault(c => MediaRules.HashDistance(own, c.Hash) <= MediaRules.DuplicateMaxDistance);
        return match?.Id;
    }
}

using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace CreativeIntelligence.Web.Services.Media;

/// <summary>
/// Detección de escenas de un video: obtiene los cortes con PySceneDetect, los convierte en fragmentos visuales
/// contiguos que cubren todo el video, y por cada fragmento genera una miniatura y, si el video tiene audio,
/// mide su volumen. Reemplaza los fragmentos visuales del
/// video e incrementa su versión de escenas, en una transacción. Si el video tiene fragmentos corregidos a mano
/// sólo los reemplaza cuando el pedido lo confirmó; si no, falla sin tocar nada. Nunca modifica el original.
/// </summary>
/// <param name="db">Contexto de datos, con la organización del trabajo activa.</param>
/// <param name="storage">Almacenamiento.</param>
/// <param name="tools">Herramientas de medios.</param>
public sealed class MediaScenesJobHandler(AppDbContext db, IMediaStorage storage, IMediaTools tools) : IJobHandler
{
    /// <summary>Tipo de trabajo que atiende.</summary>
    public const string JobType = "media.scenes";

    /// <inheritdoc />
    public string Type => JobType;

    /// <inheritdoc />
    /// <exception cref="PermanentJobException">Si el video ya no existe, no tiene duración conocida, tiene
    /// correcciones manuales sin confirmación para descartarlas, o la herramienta no puede leerlo (en ese caso la
    /// detección queda <c>Failed</c> con el motivo).</exception>
    public async Task ExecuteAsync(JobExecution execution, CancellationToken cancellationToken)
    {
        MediaScenesPayload? payload = JsonSerializer.Deserialize<MediaScenesPayload>(execution.Job.PayloadJson);
        long assetId = payload?.AssetId ?? 0;
        bool replaceManual = payload?.ReplaceManual ?? false;
        MediaAsset asset = await db.MediaAssets.FirstOrDefaultAsync(a => a.Id == assetId, cancellationToken)
            ?? throw new PermanentJobException("El video a analizar ya no existe.");
        if (asset.Kind != MediaAssetKind.Video || asset.DurationMs is null or <= 0)
        {
            throw new PermanentJobException("El archivo no es un video con duración conocida.");
        }

        bool hasManualEdits = await db.MediaSegments.AnyAsync(
            s => s.MediaAssetId == assetId && s.Kind == MediaSegmentKind.Visual && s.Origin == MediaSegmentOrigin.Manual,
            cancellationToken);
        if (hasManualEdits && !replaceManual)
        {
            throw new PermanentJobException("El video tiene escenas corregidas a mano; la detección no las reemplaza sin confirmación.");
        }

        int durationMs = asset.DurationMs.Value;
        try
        {
            string sourcePath = storage.GetLocalPath(asset.StoragePath);
            await execution.ReportAsync(5, "Detectando cortes", cancellationToken);
            List<SceneBoundary> scenes = Normalize(await tools.DetectScenesAsync(sourcePath, cancellationToken), durationMs);

            List<MediaSegment> segments = [];
            for (int i = 0; i < scenes.Count; i++)
            {
                SceneBoundary scene = scenes[i];
                await execution.ReportAsync(
                    30 + (i * 65 / scenes.Count), $"Miniatura de la escena {i + 1} de {scenes.Count}", cancellationToken);
                string thumbnailPath = storage.ReserveSegmentThumbnailPath(
                    asset.OrganizationId, asset.Sha256, scene.StartMs, scene.EndMs);
                await tools.CreateThumbnailAsync(
                    sourcePath, storage.GetLocalPath(thumbnailPath), (scene.StartMs + scene.EndMs) / 2, cancellationToken);
                AudioLevels? levels = asset.AudioCodec is null
                    ? null
                    : await tools.MeasureAudioAsync(sourcePath, scene.StartMs, scene.EndMs, cancellationToken) ?? MediaRules.NoAudioSamples;
                segments.Add(new MediaSegment
                {
                    MediaAssetId = asset.Id,
                    Kind = MediaSegmentKind.Visual,
                    Origin = MediaSegmentOrigin.Detected,
                    StartMs = scene.StartMs,
                    EndMs = scene.EndMs,
                    ThumbnailPath = thumbnailPath,
                    AudioMeanDb = levels?.MeanDb,
                    AudioPeakDb = levels?.PeakDb,
                });
            }

            await db.InTransactionAsync(
                async () =>
                {
                    // Incrementar la versión bloquea la fila del video: una edición manual simultánea espera y
                    // después falla su guarda, en lugar de mezclarse con los fragmentos nuevos.
                    await db.MediaAssets
                        .Where(a => a.Id == assetId)
                        .ExecuteUpdateAsync(s => s.SetProperty(a => a.SegmentsVersion, a => a.SegmentsVersion + 1), cancellationToken);
                    // Se van todos los fragmentos del video: las escenas y los tramos narrativos armados sobre ellas.
                    await db.MediaSegments
                        .Where(s => s.MediaAssetId == assetId)
                        .ExecuteDeleteAsync(cancellationToken);
                    db.MediaSegments.AddRange(segments);
                    asset.SceneStatus = MediaProcessingStatus.Ready;
                    asset.SceneError = null;
                    await db.SaveChangesAsync(cancellationToken);
                    return true;
                },
                cancellationToken);
        }
        catch (MediaToolException exception)
        {
            asset.SceneStatus = MediaProcessingStatus.Failed;
            asset.SceneError = exception.Message.Length > 1000 ? exception.Message[..1000] : exception.Message;
            await db.SaveChangesAsync(CancellationToken.None);
            throw new PermanentJobException(exception.Message);
        }
    }

    /// <summary>
    /// Convierte los cortes detectados en tramos contiguos que cubren exactamente <c>[0, durationMs)</c>: descarta
    /// los que quedan fuera, pega cada inicio al fin del anterior y extiende el último hasta el final. Sin cortes,
    /// el video entero es una sola escena.
    /// </summary>
    /// <param name="detected">Tramos informados por el detector, en cualquier orden.</param>
    /// <param name="durationMs">Duración del video según ffprobe.</param>
    /// <returns>Al menos un tramo; todos con duración positiva.</returns>
    public static List<SceneBoundary> Normalize(List<SceneBoundary> detected, int durationMs)
    {
        List<SceneBoundary> result = [];
        int cursor = 0;
        foreach (SceneBoundary scene in detected.OrderBy(s => s.StartMs))
        {
            int end = Math.Min(scene.EndMs, durationMs);
            if (end <= cursor)
            {
                continue;
            }

            result.Add(new SceneBoundary(cursor, end));
            cursor = end;
        }

        if (result.Count == 0)
        {
            return [new SceneBoundary(0, durationMs)];
        }

        if (cursor < durationMs)
        {
            result[^1] = new SceneBoundary(result[^1].StartMs, durationMs);
        }

        return result;
    }
}

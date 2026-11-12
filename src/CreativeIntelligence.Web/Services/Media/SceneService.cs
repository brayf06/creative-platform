using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace CreativeIntelligence.Web.Services.Media;

/// <summary>Escenas (fragmentos visuales) de los videos de la organización activa: consulta y detección.
/// La edición manual está en <see cref="SegmentEditService"/>.</summary>
/// <param name="db">Contexto de datos.</param>
/// <param name="storage">Almacenamiento, para las miniaturas.</param>
/// <param name="jobs">Cola de trabajos.</param>
/// <param name="audit">Auditoría.</param>
public sealed class SceneService(AppDbContext db, IMediaStorage storage, JobService jobs, AuditService audit)
{
    /// <summary>Devuelve las escenas de un video, en orden temporal, con sus etiquetas, lo que se dice en cada
    /// una, el estado de la detección y si pueden elegirse para producción. No escribe.</summary>
    /// <param name="assetId">Video.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Estado de la detección y fragmentos visuales.</returns>
    /// <exception cref="BusinessException">404 <c>MEDIA_NOT_FOUND</c> si el archivo no existe en la organización
    /// activa.</exception>
    public async Task<SceneList> ListAsync(long assetId, CancellationToken cancellationToken)
    {
        SceneHeader header = await db.MediaAssets.AsNoTracking()
            .Where(a => a.Id == assetId)
            .Select(a => new SceneHeader
            {
                DurationMs = a.DurationMs,
                Status = a.SceneStatus,
                Error = a.SceneError,
                CanSelect = a.ReuseAuthorized
                    && (a.RightsExpireOn == null || a.RightsExpireOn >= DateOnly.FromDateTime(AppDbContext.UtcNow())),
                HasAudio = a.AudioCodec != null,
            })
            .FirstOrDefaultAsync(cancellationToken) ?? throw NotFound();

        List<SegmentItem> segments = await db.MediaSegments.AsNoTracking()
            .Where(s => s.MediaAssetId == assetId && s.Kind == MediaSegmentKind.Visual)
            .OrderBy(s => s.StartMs)
            .Select(s => new SegmentItem(
                s.Id,
                s.Kind,
                s.Origin,
                s.StartMs,
                s.EndMs,
                s.ThumbnailPath != null,
                s.SelectedForProduction,
                s.Tags.OrderBy(t => t.Text).Select(t => t.Text).ToList(),
                string.Empty,
                s.AudioMeanDb,
                s.AudioPeakDb))
            .ToListAsync(cancellationToken);

        // El texto de cada escena se deriva por tiempo de la transcripción del video (una sola consulta): así
        // sigue siendo correcto después de mover, dividir o fusionar cortes.
        List<TranscriptLineItem> lines = await db.MediaTranscriptLines.AsNoTracking()
            .Where(l => l.MediaAssetId == assetId)
            .OrderBy(l => l.StartMs)
            .Select(l => new TranscriptLineItem(l.StartMs, l.EndMs, l.Text))
            .ToListAsync(cancellationToken);
        List<SegmentItem> withTranscript = segments
            .Select(s => s with { Transcript = TranscriptService.TextBetween(lines, s.StartMs, s.EndMs) })
            .ToList();
        return new SceneList(
            assetId, header.DurationMs, header.Status, header.Error, header.CanSelect, header.HasAudio, withTranscript);
    }

    /// <summary>Abre la miniatura de un fragmento de la organización activa. No escribe.</summary>
    /// <param name="segmentId">Fragmento.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Flujo JPEG; quien lo recibe lo cierra.</returns>
    /// <exception cref="BusinessException">404 <c>SEGMENT_NOT_FOUND</c> si el fragmento no existe en la
    /// organización activa o no tiene miniatura.</exception>
    public async Task<MediaContent> OpenThumbnailAsync(long segmentId, CancellationToken cancellationToken)
    {
        string? path = await db.MediaSegments.AsNoTracking()
            .Where(s => s.Id == segmentId)
            .Select(s => s.ThumbnailPath)
            .FirstOrDefaultAsync(cancellationToken);
        if (path is null)
        {
            throw new BusinessException(404, "El fragmento no existe o no tiene miniatura.", "SEGMENT_NOT_FOUND");
        }

        return new MediaContent(storage.OpenRead(path), "image/jpeg", $"segmento-{segmentId}.jpg");
    }

    /// <summary>
    /// Pide (o vuelve a pedir) la detección de escenas de un video. Deja la detección <c>Pending</c>, encola el
    /// trabajo y lo audita, en una transacción. Si el video tiene fragmentos corregidos a mano sólo procede con
    /// confirmación explícita, porque la detección los reemplaza junto con sus etiquetas y su selección.
    /// </summary>
    /// <param name="assetId">Video.</param>
    /// <param name="discardManualEdits">Verdadero para descartar las correcciones manuales.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <exception cref="BusinessException">404 <c>MEDIA_NOT_FOUND</c> si no existe en la organización activa;
    /// 409 <c>NOT_A_VIDEO</c> si es una imagen; 409 <c>MEDIA_NOT_READY</c> si todavía no terminó su análisis
    /// técnico; 409 <c>HAS_MANUAL_EDITS</c> si hay correcciones manuales y no se confirmó descartarlas.</exception>
    public async Task RequestDetectionAsync(long assetId, bool discardManualEdits, CancellationToken cancellationToken)
    {
        MediaAsset asset = await db.MediaAssets.FirstOrDefaultAsync(a => a.Id == assetId, cancellationToken) ?? throw NotFound();
        if (asset.Kind != MediaAssetKind.Video)
        {
            throw new BusinessException(409, "Sólo los videos tienen escenas.", "NOT_A_VIDEO");
        }

        if (asset.ProcessingStatus != MediaProcessingStatus.Ready)
        {
            throw new BusinessException(409, "El video todavía no terminó su análisis técnico.", "MEDIA_NOT_READY");
        }

        bool hasManualEdits = await db.MediaSegments.AnyAsync(
            s => s.MediaAssetId == assetId && s.Kind == MediaSegmentKind.Visual && s.Origin == MediaSegmentOrigin.Manual,
            cancellationToken);
        if (hasManualEdits && !discardManualEdits)
        {
            throw new BusinessException(
                409,
                "El video tiene escenas corregidas a mano. Detectar de nuevo las descarta, con sus etiquetas y su selección.",
                "HAS_MANUAL_EDITS");
        }

        await db.InTransactionAsync(
            async () =>
            {
                asset.SceneStatus = MediaProcessingStatus.Pending;
                asset.SceneError = null;
                audit.Record(
                    "media.scenes", nameof(MediaAsset), assetId.ToString(),
                    hasManualEdits ? "Pidió detectar las escenas descartando las correcciones manuales." : "Pidió detectar las escenas.");
                await db.SaveChangesAsync(cancellationToken);
                await EnqueueAsync(assetId, Guid.NewGuid().ToString("N"), hasManualEdits, cancellationToken);
                return true;
            },
            cancellationToken);
    }

    /// <summary>Encola la detección de escenas de un video. Lo usa el análisis técnico al terminar bien.</summary>
    /// <param name="assetId">Video.</param>
    /// <param name="run">Distingue la detección inicial (<c>1</c>) de cada pedido manual.</param>
    /// <param name="replaceManual">Si debe descartar también los fragmentos corregidos a mano.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>El trabajo encolado o el existente.</returns>
    public Task<EnqueueResult> EnqueueAsync(long assetId, string run, bool replaceManual, CancellationToken cancellationToken)
    {
        return jobs.EnqueueAsync(
            MediaScenesJobHandler.JobType,
            $"{MediaScenesJobHandler.JobType}:{assetId}:{run}",
            JsonSerializer.Serialize(new MediaScenesPayload(assetId, replaceManual)),
            cancellationToken);
    }

    private static BusinessException NotFound()
    {
        return new BusinessException(404, "El archivo no existe.", "MEDIA_NOT_FOUND");
    }
}

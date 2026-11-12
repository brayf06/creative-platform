using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace CreativeIntelligence.Web.Services.Media;

/// <summary>
/// Edición manual de las escenas de un video: mover un corte, dividir, fusionar, etiquetar y elegir para
/// producción. Mantiene el invariante de que los fragmentos visuales son contiguos y cubren el video.
/// <para>
/// Concurrencia: toda operación que cambia límites lee primero <c>SegmentsVersion</c> del video y, ya dentro de
/// la transacción, la incrementa con la versión leída en la condición del <c>UPDATE</c>. Ese <c>UPDATE</c>
/// bloquea la fila del video hasta confirmar, así que dos ediciones simultáneas se ordenan y la segunda recibe
/// 409 en lugar de aplicarse sobre datos viejos.
/// </para>
/// Un fragmento editado pasa a origen <c>Manual</c> y pierde su miniatura y sus niveles de audio, que se
/// recalculan en segundo plano.
/// </summary>
/// <param name="db">Contexto de datos.</param>
/// <param name="jobs">Cola de trabajos, para regenerar miniaturas.</param>
/// <param name="audit">Auditoría.</param>
/// <param name="index">Índice de búsqueda: los cortes y las etiquetas cambian el texto indexado.</param>
public sealed class SegmentEditService(AppDbContext db, JobService jobs, AuditService audit, SegmentIndexService index)
{
    /// <summary>
    /// Mueve el corte entre un fragmento y el siguiente: cambia el fin de uno y el inicio del otro. Escribe los
    /// dos fragmentos, la versión del video, la auditoría y encola la regeneración de miniaturas, en una transacción.
    /// </summary>
    /// <param name="segmentId">Fragmento cuyo fin se mueve.</param>
    /// <param name="endMs">Nuevo corte en milisegundos.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <exception cref="BusinessException">404 <c>SEGMENT_NOT_FOUND</c> si no existe en la organización activa;
    /// 409 <c>NO_NEXT_SEGMENT</c> si es el último (su fin es el fin del video); 400 <c>BOUNDARY_OUT_OF_RANGE</c>
    /// si el corte dejaría a alguno de los dos con menos de 100 ms; 409 <c>SEGMENTS_CHANGED</c> si otra edición
    /// se aplicó antes.</exception>
    public async Task MoveBoundaryAsync(long segmentId, int endMs, CancellationToken cancellationToken)
    {
        EditTicket ticket = await BeginAsync(segmentId, cancellationToken);
        MediaSegment segment = await LoadAsync(segmentId, cancellationToken);
        MediaSegment next = await LoadNextAsync(segment, cancellationToken);
        if (endMs < segment.StartMs + MediaRules.MinSegmentMs || endMs > next.EndMs - MediaRules.MinSegmentMs)
        {
            throw new BusinessException(
                400,
                $"El corte debe quedar entre {segment.StartMs + MediaRules.MinSegmentMs} y {next.EndMs - MediaRules.MinSegmentMs} ms.",
                "BOUNDARY_OUT_OF_RANGE");
        }

        await CommitAsync(
            ticket,
            () =>
            {
                segment.EndMs = endMs;
                next.StartMs = endMs;
                MarkEdited(segment);
                MarkEdited(next);
                audit.Record("segment.move", nameof(MediaSegment), segmentId.ToString(), $"Movió el corte a {endMs} ms.");
            },
            cancellationToken);
    }

    /// <summary>
    /// Divide un fragmento en dos en el punto indicado. El original conserva sus etiquetas y su selección; el
    /// nuevo nace sin ellas. Escribe ambos, la versión del video, la auditoría y encola miniaturas, en una transacción.
    /// </summary>
    /// <param name="segmentId">Fragmento a dividir.</param>
    /// <param name="atMs">Punto de división en milisegundos.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <exception cref="BusinessException">404 <c>SEGMENT_NOT_FOUND</c> si no existe en la organización activa;
    /// 400 <c>BOUNDARY_OUT_OF_RANGE</c> si alguna de las dos partes tendría menos de 100 ms;
    /// 409 <c>SEGMENTS_CHANGED</c> si otra edición se aplicó antes.</exception>
    public async Task SplitAsync(long segmentId, int atMs, CancellationToken cancellationToken)
    {
        EditTicket ticket = await BeginAsync(segmentId, cancellationToken);
        MediaSegment segment = await LoadAsync(segmentId, cancellationToken);
        if (atMs < segment.StartMs + MediaRules.MinSegmentMs || atMs > segment.EndMs - MediaRules.MinSegmentMs)
        {
            throw new BusinessException(
                400,
                $"El punto de división debe quedar entre {segment.StartMs + MediaRules.MinSegmentMs} y {segment.EndMs - MediaRules.MinSegmentMs} ms.",
                "BOUNDARY_OUT_OF_RANGE");
        }

        await CommitAsync(
            ticket,
            () =>
            {
                db.MediaSegments.Add(new MediaSegment
                {
                    MediaAssetId = segment.MediaAssetId,
                    Kind = MediaSegmentKind.Visual,
                    Origin = MediaSegmentOrigin.Manual,
                    StartMs = atMs,
                    EndMs = segment.EndMs,
                });
                segment.EndMs = atMs;
                MarkEdited(segment);
                audit.Record("segment.split", nameof(MediaSegment), segmentId.ToString(), $"Dividió el fragmento en {atMs} ms.");
            },
            cancellationToken);
    }

    /// <summary>
    /// Fusiona un fragmento con el siguiente. El resultado conserva la selección del primero y la unión de las
    /// etiquetas de ambos; el segundo se elimina. Escribe el fragmento, la versión del video, la auditoría y
    /// encola miniaturas, en una transacción.
    /// </summary>
    /// <param name="segmentId">Fragmento que absorbe al siguiente.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <exception cref="BusinessException">404 <c>SEGMENT_NOT_FOUND</c> si no existe en la organización activa;
    /// 409 <c>NO_NEXT_SEGMENT</c> si es el último; 409 <c>SEGMENTS_CHANGED</c> si otra edición se aplicó antes.</exception>
    public async Task MergeWithNextAsync(long segmentId, CancellationToken cancellationToken)
    {
        EditTicket ticket = await BeginAsync(segmentId, cancellationToken);
        MediaSegment segment = await LoadAsync(segmentId, cancellationToken);
        MediaSegment next = await LoadNextAsync(segment, cancellationToken);

        await CommitAsync(
            ticket,
            () =>
            {
                HashSet<string> existing = new(segment.Tags.Select(t => t.Text), StringComparer.OrdinalIgnoreCase);
                foreach (MediaSegmentTag tag in next.Tags.Where(t => !existing.Contains(t.Text)).Take(MediaRules.MaxTagsPerSegment - existing.Count))
                {
                    segment.Tags.Add(new MediaSegmentTag { Text = tag.Text, Origin = tag.Origin });
                }

                segment.EndMs = next.EndMs;
                MarkEdited(segment);
                db.MediaSegments.Remove(next);
                audit.Record("segment.merge", nameof(MediaSegment), segmentId.ToString(), $"Fusionó el fragmento con el siguiente (#{next.Id}).");
            },
            cancellationToken);
    }

    /// <summary>Reemplaza las etiquetas de un fragmento. Escribe las etiquetas y la auditoría.</summary>
    /// <param name="segmentId">Fragmento.</param>
    /// <param name="tags">Etiquetas separadas por coma; nulo o vacío para quitarlas todas. Se ignoran las
    /// repetidas (sin distinguir mayúsculas) y los espacios al borde.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <exception cref="BusinessException">404 <c>SEGMENT_NOT_FOUND</c> si no existe en la organización activa;
    /// 400 <c>TAGS_INVALID</c> si hay más de 20 etiquetas o alguna supera los 50 caracteres.</exception>
    public async Task SetTagsAsync(long segmentId, string? tags, CancellationToken cancellationToken)
    {
        List<string> wanted = (tags ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (wanted.Count > MediaRules.MaxTagsPerSegment || wanted.Any(t => t.Length > 50))
        {
            throw new BusinessException(
                400, $"Se admiten hasta {MediaRules.MaxTagsPerSegment} etiquetas de hasta 50 caracteres cada una.", "TAGS_INVALID");
        }

        MediaSegment segment = await LoadAsync(segmentId, cancellationToken);
        segment.Tags.RemoveAll(t => !wanted.Contains(t.Text, StringComparer.OrdinalIgnoreCase));

        // Lo que la persona guarda pasa a ser suyo: una etiqueta que había puesto el análisis y se conserva
        // queda como manual, así un reanálisis no la quita.
        segment.Tags.ForEach(t => t.Origin = MediaSegmentOrigin.Manual);
        foreach (string text in wanted.Where(w => !segment.Tags.Any(t => string.Equals(t.Text, w, StringComparison.OrdinalIgnoreCase))))
        {
            segment.Tags.Add(new MediaSegmentTag { Text = text, Origin = MediaSegmentOrigin.Manual });
        }

        audit.Record("segment.tags", nameof(MediaSegment), segmentId.ToString(), $"Etiquetas: {string.Join(", ", wanted)}.");
        await db.InTransactionAsync(
            async () =>
            {
                await db.SaveChangesAsync(cancellationToken);
                await index.EnqueueIfConfiguredAsync(segment.MediaAssetId, cancellationToken);
                return true;
            },
            cancellationToken);
    }

    /// <summary>
    /// Elige o quita un fragmento para producción. Sólo se puede elegir si el video tiene autorización de
    /// reutilización vigente a la fecha de la base. Escribe el fragmento y la auditoría.
    /// </summary>
    /// <param name="segmentId">Fragmento.</param>
    /// <param name="selected">Verdadero para elegirlo.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <exception cref="BusinessException">404 <c>SEGMENT_NOT_FOUND</c> si no existe en la organización activa;
    /// 409 <c>REUSE_NOT_AUTHORIZED</c> si se quiere elegir y el video no tiene autorización vigente.</exception>
    public async Task SetSelectedAsync(long segmentId, bool selected, CancellationToken cancellationToken)
    {
        MediaSegment segment = await LoadAsync(segmentId, cancellationToken);
        if (selected)
        {
            long assetId = segment.MediaAssetId;
            bool authorized = await db.MediaAssets.AnyAsync(
                a => a.Id == assetId
                    && a.ReuseAuthorized
                    && (a.RightsExpireOn == null || a.RightsExpireOn >= DateOnly.FromDateTime(AppDbContext.UtcNow())),
                cancellationToken);
            if (!authorized)
            {
                throw new BusinessException(
                    409, "El video no tiene autorización de reutilización vigente: no se puede elegir para producción.",
                    "REUSE_NOT_AUTHORIZED");
            }
        }

        segment.SelectedForProduction = selected;
        audit.Record(
            "segment.select", nameof(MediaSegment), segmentId.ToString(),
            selected ? "Eligió el fragmento para producción." : "Quitó el fragmento de producción.");
        await db.SaveChangesAsync(cancellationToken);
    }

    // Lee qué video es y en qué versión están sus escenas ANTES de leer los fragmentos: si otra edición entra
    // después, la guarda de CommitAsync la detecta.
    private async Task<EditTicket> BeginAsync(long segmentId, CancellationToken cancellationToken)
    {
        long? assetId = await db.MediaSegments.AsNoTracking()
            .Where(s => s.Id == segmentId && s.Kind == MediaSegmentKind.Visual)
            .Select(s => (long?)s.MediaAssetId)
            .FirstOrDefaultAsync(cancellationToken);
        if (assetId is null)
        {
            throw NotFound();
        }

        int version = await db.MediaAssets.AsNoTracking()
            .Where(a => a.Id == assetId)
            .Select(a => a.SegmentsVersion)
            .FirstAsync(cancellationToken);
        return new EditTicket(assetId.Value, version);
    }

    private async Task<MediaSegment> LoadAsync(long segmentId, CancellationToken cancellationToken)
    {
        return await db.MediaSegments.Include(s => s.Tags)
            .FirstOrDefaultAsync(s => s.Id == segmentId && s.Kind == MediaSegmentKind.Visual, cancellationToken)
            ?? throw NotFound();
    }

    private async Task<MediaSegment> LoadNextAsync(MediaSegment segment, CancellationToken cancellationToken)
    {
        long assetId = segment.MediaAssetId;
        int endMs = segment.EndMs;
        return await db.MediaSegments.Include(s => s.Tags)
            .FirstOrDefaultAsync(
                s => s.MediaAssetId == assetId && s.Kind == MediaSegmentKind.Visual && s.StartMs == endMs,
                cancellationToken)
            ?? throw new BusinessException(409, "Es el último fragmento del video: no tiene siguiente.", "NO_NEXT_SEGMENT");
    }

    private Task<bool> CommitAsync(EditTicket ticket, Action apply, CancellationToken cancellationToken)
    {
        return db.InTransactionAsync(
            async () =>
            {
                int rows = await db.MediaAssets
                    .Where(a => a.Id == ticket.AssetId && a.SegmentsVersion == ticket.Version)
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.SegmentsVersion, a => a.SegmentsVersion + 1), cancellationToken);
                if (rows == 0)
                {
                    throw new BusinessException(
                        409, "Las escenas de este video cambiaron mientras editabas. Recargá y volvé a intentar.", "SEGMENTS_CHANGED");
                }

                apply();
                await db.SaveChangesAsync(cancellationToken);
                await jobs.EnqueueAsync(
                    MediaSegmentThumbnailsJobHandler.JobType,
                    $"{MediaSegmentThumbnailsJobHandler.JobType}:{ticket.AssetId}:{ticket.Version + 1}",
                    JsonSerializer.Serialize(new MediaProbePayload(ticket.AssetId)),
                    cancellationToken);
                await index.EnqueueIfConfiguredAsync(ticket.AssetId, cancellationToken);
                return true;
            },
            cancellationToken);
    }

    private static void MarkEdited(MediaSegment segment)
    {
        // La miniatura y los niveles de audio dependen del tramo: se vacían y un trabajo los recalcula.
        segment.Origin = MediaSegmentOrigin.Manual;
        segment.ThumbnailPath = null;
        segment.AudioMeanDb = null;
        segment.AudioPeakDb = null;
    }

    private static BusinessException NotFound()
    {
        return new BusinessException(404, "El fragmento no existe.", "SEGMENT_NOT_FOUND");
    }

    private sealed record EditTicket(long AssetId, int Version);
}

/// <summary>
/// Regenera lo que se deriva del tramo de cada fragmento de un video y quedó faltando tras una edición: la
/// miniatura y, si el video tiene audio, los niveles de volumen. Sólo escribe esos datos, y sólo si el fragmento
/// sigue teniendo el mismo tramo que cuando se calcularon. Nunca modifica el original.
/// </summary>
/// <param name="db">Contexto de datos, con la organización del trabajo activa.</param>
/// <param name="storage">Almacenamiento.</param>
/// <param name="tools">Herramientas de medios.</param>
public sealed class MediaSegmentThumbnailsJobHandler(AppDbContext db, IMediaStorage storage, IMediaTools tools) : IJobHandler
{
    /// <summary>Tipo de trabajo que atiende.</summary>
    public const string JobType = "media.segment-thumbnails";

    /// <inheritdoc />
    public string Type => JobType;

    /// <inheritdoc />
    /// <exception cref="PermanentJobException">Si el video ya no existe o FFmpeg no puede leerlo.</exception>
    public async Task ExecuteAsync(JobExecution execution, CancellationToken cancellationToken)
    {
        MediaProbePayload? payload = JsonSerializer.Deserialize<MediaProbePayload>(execution.Job.PayloadJson);
        long assetId = payload?.AssetId ?? 0;
        MediaLocation asset = await db.MediaAssets.AsNoTracking()
            .Where(a => a.Id == assetId)
            .Select(a => new MediaLocation { StoragePath = a.StoragePath, Sha256 = a.Sha256, HasAudio = a.AudioCodec != null })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new PermanentJobException("El video ya no existe.");
        bool hasAudio = asset.HasAudio;

        List<PendingSegment> pending = await db.MediaSegments.AsNoTracking()
            .Where(s => s.MediaAssetId == assetId && (s.ThumbnailPath == null || (hasAudio && s.AudioMeanDb == null)))
            .OrderBy(s => s.StartMs)
            .Select(s => new PendingSegment
            {
                StartMs = s.StartMs,
                EndMs = s.EndMs,
                NeedsThumbnail = s.ThumbnailPath == null,
                NeedsAudio = hasAudio && s.AudioMeanDb == null,
            })
            .ToListAsync(cancellationToken);
        string sourcePath = storage.GetLocalPath(asset.StoragePath);
        try
        {
            for (int i = 0; i < pending.Count; i++)
            {
                PendingSegment range = pending[i];
                await execution.ReportAsync(i * 100 / pending.Count, $"Fragmento {i + 1} de {pending.Count}", cancellationToken);

                // El tramo va en la condición de cada escritura: si el fragmento se volvió a editar, esto ya no le corresponde.
                IQueryable<MediaSegment> unchanged = db.MediaSegments
                    .Where(s => s.MediaAssetId == assetId && s.StartMs == range.StartMs && s.EndMs == range.EndMs);
                // El audio va antes que la miniatura: la pantalla deja de consultar cuando aparece la miniatura.
                if (range.NeedsAudio)
                {
                    AudioLevels levels = await tools.MeasureAudioAsync(sourcePath, range.StartMs, range.EndMs, cancellationToken)
                        ?? MediaRules.NoAudioSamples;
                    await unchanged.ExecuteUpdateAsync(
                        s => s.SetProperty(x => x.AudioMeanDb, (decimal?)levels.MeanDb).SetProperty(x => x.AudioPeakDb, (decimal?)levels.PeakDb),
                        cancellationToken);
                }

                if (range.NeedsThumbnail)
                {
                    string path = storage.ReserveSegmentThumbnailPath(db.CurrentOrganizationId, asset.Sha256, range.StartMs, range.EndMs);
                    await tools.CreateThumbnailAsync(sourcePath, storage.GetLocalPath(path), (range.StartMs + range.EndMs) / 2, cancellationToken);
                    await unchanged.ExecuteUpdateAsync(s => s.SetProperty(x => x.ThumbnailPath, path), cancellationToken);
                }
            }
        }
        catch (MediaToolException exception)
        {
            throw new PermanentJobException(exception.Message);
        }
    }
}

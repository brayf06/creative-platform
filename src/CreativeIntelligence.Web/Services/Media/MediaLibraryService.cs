using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace CreativeIntelligence.Web.Services.Media;

/// <summary>
/// Biblioteca multimedia de la organización activa: ingreso de archivos, listado, detalle, derechos y acceso al
/// original. Los originales no se modifican nunca; lo único editable de un archivo son sus datos de derechos.
/// </summary>
/// <param name="db">Contexto de datos.</param>
/// <param name="storage">Almacenamiento de originales.</param>
/// <param name="jobs">Cola de trabajos, para encolar el análisis técnico.</param>
/// <param name="audit">Auditoría.</param>
/// <param name="current">Usuario y organización que operan.</param>
public sealed class MediaLibraryService(
    AppDbContext db, IMediaStorage storage, JobService jobs, AuditService audit, CurrentContext current)
{
    /// <summary>
    /// Ingresa un archivo a la biblioteca. Guarda el contenido direccionado por su SHA-256 y, si la organización
    /// todavía no tenía ese contenido, crea el registro con su procedencia y derechos, lo audita y encola su
    /// análisis técnico (propiedades y miniatura), todo en una transacción. Es idempotente: cargar dos veces el mismo contenido devuelve el archivo existente como
    /// duplicado exacto, sin crear otro registro ni otra copia en disco.
    /// </summary>
    /// <param name="fileName">Nombre del archivo tal como lo envió el cliente; se descarta cualquier ruta.</param>
    /// <param name="rights">Titular, licencia y autorización de reutilización.</param>
    /// <param name="content">Contenido del archivo.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <param name="sourceKind">Procedencia; por defecto, carga manual.</param>
    /// <param name="sourceReference">Referencia de la procedencia (la ruta, si vino de una carpeta vigilada).</param>
    /// <returns>El archivo creado o el existente, y cuál de los dos casos fue.</returns>
    /// <exception cref="BusinessException">403 <c>NO_ORGANIZATION</c> si no hay organización activa;
    /// 415 <c>UNSUPPORTED_MEDIA_TYPE</c> si la extensión no está admitida; 400 <c>EMPTY_FILE</c> si el archivo
    /// está vacío.</exception>
    public async Task<IngestResult> IngestAsync(
        string fileName,
        MediaRightsBody rights,
        Stream content,
        CancellationToken cancellationToken,
        MediaSourceKind sourceKind = MediaSourceKind.Upload,
        string? sourceReference = null)
    {
        if (current.OrganizationId == 0)
        {
            throw new BusinessException(403, "No hay una organización activa.", "NO_ORGANIZATION");
        }

        string name = Path.GetFileName(fileName.Trim());
        if (name.Length > 260)
        {
            name = name[^260..];
        }

        if (!MediaRules.TryGetFormat(Path.GetExtension(name), out MediaFormat? format))
        {
            throw new BusinessException(
                415,
                $"El archivo {name} no es un formato admitido ({string.Join(", ", MediaRules.Extensions)}).",
                "UNSUPPORTED_MEDIA_TYPE");
        }

        StoredOriginal stored = await storage.SaveOriginalAsync(current.OrganizationId, content, cancellationToken);
        if (stored.SizeBytes == 0)
        {
            throw new BusinessException(400, $"El archivo {name} está vacío.", "EMPTY_FILE");
        }

        long? existingId = await db.MediaAssets.AsNoTracking()
            .Where(a => a.Sha256 == stored.Sha256)
            .Select(a => (long?)a.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (existingId is not null)
        {
            return new IngestResult(existingId.Value, name, IngestOutcome.Duplicate);
        }

        MediaAsset asset = new()
        {
            Kind = format.Kind,
            OriginalFileName = name,
            ContentType = format.ContentType,
            SizeBytes = stored.SizeBytes,
            Sha256 = stored.Sha256,
            StoragePath = stored.RelativePath,
            SourceKind = sourceKind,
            SourceReference = sourceReference is { Length: > 500 } ? sourceReference[^500..] : sourceReference,
            RightsHolder = Clean(rights.RightsHolder),
            LicenseNotes = Clean(rights.LicenseNotes),
            ReuseAuthorized = rights.ReuseAuthorized,
            RightsExpireOn = rights.RightsExpireOn,
            CreatedByUserId = current.UserId,
        };

        return await db.InTransactionAsync(
            async () =>
            {
                db.MediaAssets.Add(asset);
                await db.SaveChangesAsync(cancellationToken);
                audit.Record(
                    "media.ingest", nameof(MediaAsset), asset.Id.ToString(),
                    sourceKind == MediaSourceKind.WatchedFolder
                        ? $"Ingresó {name} desde una carpeta vigilada ({stored.SizeBytes} bytes)."
                        : $"Cargó {name} ({stored.SizeBytes} bytes).");
                await db.SaveChangesAsync(cancellationToken);
                await EnqueueProbeAsync(asset.Id, "1", cancellationToken);
                return new IngestResult(asset.Id, name, IngestOutcome.Created);
            },
            cancellationToken);
    }

    /// <summary>Lista los archivos de la organización activa, del más nuevo al más viejo. No escribe.</summary>
    /// <param name="filter">Filtros opcionales por tipo, autorización y nombre.</param>
    /// <param name="page">Paginación.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Página de archivos.</returns>
    public async Task<PagedResult<MediaAssetItem>> ListAsync(
        MediaFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        IQueryable<MediaAsset> query = db.MediaAssets.AsNoTracking();
        if (filter.Kind is not null)
        {
            query = query.Where(a => a.Kind == filter.Kind);
        }

        if (filter.ReuseAuthorized is not null)
        {
            query = query.Where(a => a.ReuseAuthorized == filter.ReuseAuthorized);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            string search = filter.Search.Trim();
            query = query.Where(a => a.OriginalFileName.Contains(search));
        }

        int total = await query.CountAsync(cancellationToken);
        List<MediaAssetItem> items = await query
            .OrderByDescending(a => a.Id)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(a => new MediaAssetItem(
                a.Id, a.OriginalFileName, a.Kind, a.SizeBytes, a.ReuseAuthorized, a.RightsHolder, a.RightsExpireOn,
                a.CreatedAt, a.ProcessingStatus, a.DurationMs, a.Width, a.Height, a.ThumbnailPath != null, a.DuplicateOfAssetId))
            .ToListAsync(cancellationToken);
        return new PagedResult<MediaAssetItem>(items, page.Page, page.PageSize, total);
    }

    /// <summary>Devuelve el detalle de un archivo de la organización activa. No escribe.</summary>
    /// <param name="id">Archivo.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>El detalle.</returns>
    /// <exception cref="BusinessException">404 <c>MEDIA_NOT_FOUND</c> si no existe en la organización activa.</exception>
    public async Task<MediaAssetDetail> GetAsync(long id, CancellationToken cancellationToken)
    {
        MediaAssetDetail? detail = await db.MediaAssets.AsNoTracking()
            .Where(a => a.Id == id)
            .Select(a => new MediaAssetDetail(
                a.Id, a.OriginalFileName, a.Kind, a.ContentType, a.SizeBytes, a.Sha256, a.SourceKind,
                a.SourceReference, a.RightsHolder, a.LicenseNotes, a.ReuseAuthorized, a.RightsExpireOn, a.CreatedAt,
                a.ProcessingStatus, a.ProcessingError, a.DurationMs, a.Width, a.Height, a.FrameRate, a.VideoCodec,
                a.AudioCodec, a.Container, a.DuplicateOfAssetId))
            .FirstOrDefaultAsync(cancellationToken);
        return detail ?? throw NotFound();
    }

    /// <summary>Abre el original de un archivo de la organización activa para verlo o reproducirlo. No escribe.</summary>
    /// <param name="id">Archivo.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Flujo de lectura con su tipo MIME y nombre; quien lo recibe cierra el flujo.</returns>
    /// <exception cref="BusinessException">404 <c>MEDIA_NOT_FOUND</c> si no existe en la organización activa.</exception>
    public async Task<MediaContent> OpenOriginalAsync(long id, CancellationToken cancellationToken)
    {
        MediaLocation? location = await db.MediaAssets.AsNoTracking()
            .Where(a => a.Id == id)
            .Select(a => new MediaLocation
            {
                StoragePath = a.StoragePath,
                ContentType = a.ContentType,
                FileName = a.OriginalFileName,
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (location is null)
        {
            throw NotFound();
        }

        return new MediaContent(storage.OpenRead(location.StoragePath), location.ContentType, location.FileName);
    }

    /// <summary>
    /// Cambia los datos de derechos de un archivo (titular, licencia, autorización y vigencia). Escribe el cambio
    /// y la auditoría en una transacción.
    /// </summary>
    /// <param name="id">Archivo.</param>
    /// <param name="rights">Datos nuevos; reemplazan a los anteriores.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <exception cref="BusinessException">404 <c>MEDIA_NOT_FOUND</c> si no existe en la organización activa.</exception>
    public async Task UpdateRightsAsync(long id, MediaRightsBody rights, CancellationToken cancellationToken)
    {
        MediaAsset asset = await db.MediaAssets.FirstOrDefaultAsync(a => a.Id == id, cancellationToken) ?? throw NotFound();
        asset.RightsHolder = Clean(rights.RightsHolder);
        asset.LicenseNotes = Clean(rights.LicenseNotes);
        asset.ReuseAuthorized = rights.ReuseAuthorized;
        asset.RightsExpireOn = rights.RightsExpireOn;
        audit.Record(
            "media.rights", nameof(MediaAsset), id.ToString(),
            rights.ReuseAuthorized ? "Autorizó la reutilización." : "Quitó la autorización de reutilización.");
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Abre la miniatura de un archivo de la organización activa. No escribe.</summary>
    /// <param name="id">Archivo.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Flujo JPEG; quien lo recibe lo cierra.</returns>
    /// <exception cref="BusinessException">404 <c>MEDIA_NOT_FOUND</c> si no existe en la organización activa;
    /// 404 <c>THUMBNAIL_NOT_READY</c> si todavía no tiene miniatura.</exception>
    public async Task<MediaContent> OpenThumbnailAsync(long id, CancellationToken cancellationToken)
    {
        MediaLocation? location = await db.MediaAssets.AsNoTracking()
            .Where(a => a.Id == id)
            .Select(a => new MediaLocation
            {
                StoragePath = a.ThumbnailPath ?? string.Empty,
                ContentType = "image/jpeg",
                FileName = a.OriginalFileName,
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (location is null)
        {
            throw NotFound();
        }

        if (location.StoragePath.Length == 0)
        {
            throw new BusinessException(404, "El archivo todavía no tiene miniatura.", "THUMBNAIL_NOT_READY");
        }

        return new MediaContent(storage.OpenRead(location.StoragePath), location.ContentType, location.FileName);
    }

    /// <summary>
    /// Vuelve a pedir el análisis técnico de un archivo (por ejemplo, tras un fallo o un cambio de versión de
    /// FFmpeg). Deja el archivo <c>Pending</c>, encola el trabajo y lo audita, en una transacción.
    /// </summary>
    /// <param name="id">Archivo.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <exception cref="BusinessException">404 <c>MEDIA_NOT_FOUND</c> si no existe en la organización activa.</exception>
    public async Task ReprocessAsync(long id, CancellationToken cancellationToken)
    {
        MediaAsset asset = await db.MediaAssets.FirstOrDefaultAsync(a => a.Id == id, cancellationToken) ?? throw NotFound();
        await db.InTransactionAsync(
            async () =>
            {
                asset.ProcessingStatus = MediaProcessingStatus.Pending;
                asset.ProcessingError = null;
                audit.Record("media.reprocess", nameof(MediaAsset), id.ToString(), "Pidió reanalizar el archivo.");
                await db.SaveChangesAsync(cancellationToken);
                await EnqueueProbeAsync(id, Guid.NewGuid().ToString("N"), cancellationToken);
                return true;
            },
            cancellationToken);
    }

    // La clave distingue el análisis inicial ("1") de cada reanálisis pedido a mano.
    private Task<EnqueueResult> EnqueueProbeAsync(long assetId, string run, CancellationToken cancellationToken)
    {
        return jobs.EnqueueAsync(
            MediaProbeJobHandler.JobType,
            $"{MediaProbeJobHandler.JobType}:{assetId}:{run}",
            JsonSerializer.Serialize(new MediaProbePayload(assetId)),
            cancellationToken);
    }

    private static string? Clean(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static BusinessException NotFound()
    {
        return new BusinessException(404, "El archivo no existe.", "MEDIA_NOT_FOUND");
    }
}

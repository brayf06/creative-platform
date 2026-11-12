using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace CreativeIntelligence.Web.Services.Composition;

/// <summary>
/// Música de la organización activa: pistas de audio con sus derechos, para usar de fondo en las creatividades.
/// Sólo una pista analizada y con autorización vigente puede entrar en una receta. El archivo se guarda por el hash
/// de su contenido y no se modifica.
/// </summary>
/// <param name="db">Contexto de datos.</param>
/// <param name="storage">Almacenamiento.</param>
/// <param name="jobs">Cola de trabajos.</param>
/// <param name="audit">Auditoría.</param>
/// <param name="current">Usuario y organización que operan.</param>
public sealed class MusicService(AppDbContext db, IMediaStorage storage, JobService jobs, AuditService audit, CurrentContext current)
{
    /// <summary>Tamaño máximo de una pista, en bytes.</summary>
    public const long MaxUploadBytes = 100L * 1024 * 1024;

    /// <summary>Extensiones de audio admitidas.</summary>
    public static readonly IReadOnlyList<string> Extensions = [".mp3", ".m4a", ".aac", ".wav", ".ogg"];

    /// <summary>Lista las pistas de la organización activa. No escribe.</summary>
    /// <param name="onlyUsable">Verdadero para traer sólo las analizadas y con autorización vigente.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Pistas por nombre.</returns>
    public async Task<List<MusicTrackItem>> ListAsync(bool onlyUsable, CancellationToken cancellationToken)
    {
        return await db.MusicTracks.AsNoTracking()
            .Where(m => !onlyUsable
                || (m.Status == MediaProcessingStatus.Ready && m.ReuseAuthorized
                    && (m.RightsExpireOn == null || m.RightsExpireOn >= DateOnly.FromDateTime(AppDbContext.UtcNow()))))
            .OrderBy(m => m.Name)
            .Select(m => new MusicTrackItem(
                m.Id,
                m.Name,
                m.OriginalFileName,
                m.DurationMs,
                m.Status,
                m.Error,
                m.RightsHolder,
                m.LicenseNotes,
                m.ReuseAuthorized && (m.RightsExpireOn == null || m.RightsExpireOn >= DateOnly.FromDateTime(AppDbContext.UtcNow())),
                m.RightsExpireOn,
                m.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Carga una pista de música con sus derechos y encola su análisis. Escribe el archivo, la pista, la auditoría y
    /// el trabajo.
    /// </summary>
    /// <param name="fileName">Nombre del archivo cargado.</param>
    /// <param name="name">Nombre de la pista; si falta se usa el del archivo.</param>
    /// <param name="rights">Titular, licencia, autorización y vigencia.</param>
    /// <param name="content">Contenido del archivo.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Identificador de la pista.</returns>
    /// <exception cref="BusinessException">415 <c>UNSUPPORTED_MEDIA_TYPE</c> si no es un formato de audio admitido;
    /// 400 <c>EMPTY_FILE</c>; 409 <c>MUSIC_DUPLICATE</c> si ese contenido ya está cargado.</exception>
    public async Task<long> UploadAsync(string fileName, string? name, MediaRightsBody rights, Stream content, CancellationToken cancellationToken)
    {
        string file = Path.GetFileName(fileName.Trim());
        if (file.Length > 260)
        {
            file = file[^260..];
        }

        if (!Extensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
        {
            throw new BusinessException(
                415, $"El archivo {file} no es un formato de audio admitido ({string.Join(", ", Extensions)}).", "UNSUPPORTED_MEDIA_TYPE");
        }

        StoredOriginal stored = await storage.SaveOriginalAsync(current.OrganizationId, content, cancellationToken);
        if (stored.SizeBytes == 0)
        {
            throw new BusinessException(400, $"El archivo {file} está vacío.", "EMPTY_FILE");
        }

        if (await db.MusicTracks.AnyAsync(m => m.Sha256 == stored.Sha256, cancellationToken))
        {
            throw new BusinessException(409, "Esa pista ya está cargada.", "MUSIC_DUPLICATE");
        }

        MusicTrack track = new()
        {
            Name = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(file) : name.Trim(),
            OriginalFileName = file,
            Sha256 = stored.Sha256,
            StoragePath = stored.RelativePath,
            SizeBytes = stored.SizeBytes,
            Status = MediaProcessingStatus.Pending,
            RightsHolder = string.IsNullOrWhiteSpace(rights.RightsHolder) ? null : rights.RightsHolder.Trim(),
            LicenseNotes = string.IsNullOrWhiteSpace(rights.LicenseNotes) ? null : rights.LicenseNotes.Trim(),
            ReuseAuthorized = rights.ReuseAuthorized,
            RightsExpireOn = rights.RightsExpireOn,
            CreatedByUserId = current.UserId,
        };
        return await db.InTransactionAsync(
            async () =>
            {
                db.MusicTracks.Add(track);
                await db.SaveChangesAsync(cancellationToken);
                audit.Record("music.upload", nameof(MusicTrack), track.Id.ToString(), $"Cargó la pista \"{track.Name}\" ({file}).");
                await db.SaveChangesAsync(cancellationToken);
                await jobs.EnqueueAsync(
                    MusicProbeJobHandler.JobType,
                    $"{MusicProbeJobHandler.JobType}:{track.Id}",
                    JsonSerializer.Serialize(new MusicProbePayload(track.Id)),
                    cancellationToken);
                return track.Id;
            },
            cancellationToken);
    }

    /// <summary>Autoriza o desautoriza una pista. Las recetas que ya la usan dejan de poder renderizarse si pierde
    /// la autorización. Escribe la pista y la auditoría.</summary>
    /// <param name="id">Pista.</param>
    /// <param name="authorized">Verdadero para autorizarla.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <exception cref="BusinessException">404 <c>MUSIC_NOT_FOUND</c> si no existe en la organización activa.</exception>
    public async Task SetAuthorizedAsync(long id, bool authorized, CancellationToken cancellationToken)
    {
        MusicTrack track = await db.MusicTracks.FirstOrDefaultAsync(m => m.Id == id, cancellationToken)
            ?? throw new BusinessException(404, "La pista de música no existe.", "MUSIC_NOT_FOUND");
        track.ReuseAuthorized = authorized;
        audit.Record(
            "music.rights", nameof(MusicTrack), id.ToString(),
            authorized ? "Autorizó el uso de la pista." : "Quitó la autorización de uso de la pista.");
        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Análisis de una pista de música: comprueba que el archivo tenga audio legible y guarda su duración. Un
/// archivo ilegible deja la pista fallida con el motivo, sin reintentos.</summary>
/// <param name="db">Contexto de datos, con la organización del trabajo activa.</param>
/// <param name="storage">Almacenamiento.</param>
/// <param name="tools">FFmpeg.</param>
public sealed class MusicProbeJobHandler(AppDbContext db, IMediaStorage storage, IMediaTools tools) : IJobHandler
{
    /// <summary>Tipo de trabajo que atiende.</summary>
    public const string JobType = "music.probe";

    /// <inheritdoc />
    public string Type => JobType;

    /// <inheritdoc />
    /// <exception cref="PermanentJobException">Si la pista ya no existe o el archivo no tiene audio legible.</exception>
    public async Task ExecuteAsync(JobExecution execution, CancellationToken cancellationToken)
    {
        MusicProbePayload? payload = JsonSerializer.Deserialize<MusicProbePayload>(execution.Job.PayloadJson);
        long trackId = payload?.TrackId ?? 0;
        MusicTrack track = await db.MusicTracks.FirstOrDefaultAsync(m => m.Id == trackId, cancellationToken)
            ?? throw new PermanentJobException("La pista de música ya no existe.");
        try
        {
            track.DurationMs = await tools.ProbeAudioAsync(storage.GetLocalPath(track.StoragePath), cancellationToken);
            track.Status = MediaProcessingStatus.Ready;
            track.Error = null;
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (MediaToolException exception)
        {
            track.Status = MediaProcessingStatus.Failed;
            track.Error = exception.Message.Length > 500 ? exception.Message[..500] : exception.Message;
            await db.SaveChangesAsync(cancellationToken);
            throw new PermanentJobException(exception.Message);
        }
    }
}

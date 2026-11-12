using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CreativeIntelligence.Web.Services.Media;

/// <summary>Configuración de la importación desde carpetas (sección <c>Ingest</c>).</summary>
public sealed class IngestOptions
{
    /// <summary>Carpetas raíz desde las que se permite importar. Una carpeta vigilada debe estar dentro de alguna.
    /// Si son relativas, se resuelven contra el directorio de trabajo. Vacío = no se puede vigilar ninguna.</summary>
    public List<string> AllowedRoots { get; set; } = [];

    /// <summary>Cada cuántos minutos el Worker revisa las carpetas activas.</summary>
    public int ScanIntervalMinutes { get; set; } = 5;

    /// <summary>Segundos que debe llevar un archivo sin modificarse para ingresarlo; evita tomar un archivo que
    /// todavía se está copiando.</summary>
    public int MinFileAgeSeconds { get; set; } = 10;
}

/// <summary>
/// Carpetas vigiladas de la organización activa: carpetas del servidor que se revisan periódicamente para ingresar
/// solos los archivos nuevos. Como una ruta la elige una persona desde el navegador, sólo se aceptan carpetas
/// dentro de las raíces configuradas (<see cref="IngestOptions.AllowedRoots"/>): son las "fuentes autorizadas".
/// Los archivos de la carpeta nunca se modifican ni se mueven; se copian a la biblioteca.
/// </summary>
/// <param name="db">Contexto de datos.</param>
/// <param name="jobs">Cola de trabajos.</param>
/// <param name="audit">Auditoría.</param>
/// <param name="current">Usuario y organización que operan.</param>
/// <param name="options">Raíces permitidas.</param>
public sealed class WatchedFolderService(
    AppDbContext db, JobService jobs, AuditService audit, CurrentContext current, IOptions<IngestOptions> options)
{
    /// <summary>Lista las carpetas vigiladas de la organización activa y las raíces permitidas. No escribe.</summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Raíces y carpetas.</returns>
    public async Task<WatchedFolderList> ListAsync(CancellationToken cancellationToken)
    {
        List<WatchedFolderItem> folders = await db.WatchedFolders.AsNoTracking()
            .OrderBy(f => f.Path)
            .Select(f => new WatchedFolderItem(
                f.Id, f.Path, f.IsActive, f.ReuseAuthorized, f.RightsHolder, f.LastScanAt, f.LastScanSummary,
                db.WatchedFiles.Count(w => w.WatchedFolderId == f.Id)))
            .ToListAsync(cancellationToken);
        return new WatchedFolderList(Roots(), folders);
    }

    /// <summary>
    /// Agrega una carpeta vigilada, activa, y encola su primera revisión. Escribe la carpeta, la auditoría y el
    /// trabajo en una transacción.
    /// </summary>
    /// <param name="body">Ruta y derechos por defecto.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Identificador de la carpeta.</returns>
    /// <exception cref="BusinessException">400 <c>FOLDER_NOT_ALLOWED</c> si la ruta no es válida o está fuera de las
    /// raíces permitidas; 400 <c>FOLDER_NOT_FOUND</c> si no existe en el servidor; 409 <c>FOLDER_ALREADY_WATCHED</c>
    /// si la organización ya la vigila.</exception>
    public async Task<int> CreateAsync(CreateWatchedFolderBody body, CancellationToken cancellationToken)
    {
        string path = Normalize(body.Path);
        if (!Roots().Any(root => IsInside(path, root)))
        {
            throw new BusinessException(
                400, "La carpeta debe estar dentro de una de las carpetas raíz permitidas para importar.", "FOLDER_NOT_ALLOWED");
        }

        if (!Directory.Exists(path))
        {
            throw new BusinessException(400, "La carpeta no existe en el servidor.", "FOLDER_NOT_FOUND");
        }

        if (await db.WatchedFolders.AnyAsync(f => f.Path == path, cancellationToken))
        {
            throw new BusinessException(409, "Esa carpeta ya está siendo vigilada.", "FOLDER_ALREADY_WATCHED");
        }

        WatchedFolder folder = new()
        {
            Path = path,
            IsActive = true,
            RightsHolder = string.IsNullOrWhiteSpace(body.RightsHolder) ? null : body.RightsHolder.Trim(),
            LicenseNotes = string.IsNullOrWhiteSpace(body.LicenseNotes) ? null : body.LicenseNotes.Trim(),
            ReuseAuthorized = body.ReuseAuthorized,
            RightsExpireOn = body.RightsExpireOn,
            CreatedByUserId = current.UserId,
        };
        return await db.InTransactionAsync(
            async () =>
            {
                db.WatchedFolders.Add(folder);
                await db.SaveChangesAsync(cancellationToken);
                audit.Record("folder.create", nameof(WatchedFolder), folder.Id.ToString(), $"Empezó a vigilar {path}.");
                await db.SaveChangesAsync(cancellationToken);
                await EnqueueScanAsync(folder.Id, Guid.NewGuid().ToString("N"), cancellationToken);
                return folder.Id;
            },
            cancellationToken);
    }

    /// <summary>Activa o pausa la revisión periódica de una carpeta. Escribe la carpeta y la auditoría.</summary>
    /// <param name="id">Carpeta.</param>
    /// <param name="active">Verdadero para que el Worker la revise.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <exception cref="BusinessException">404 <c>FOLDER_NOT_WATCHED</c> si no existe en la organización activa.</exception>
    public async Task SetActiveAsync(int id, bool active, CancellationToken cancellationToken)
    {
        WatchedFolder folder = await db.WatchedFolders.FirstOrDefaultAsync(f => f.Id == id, cancellationToken) ?? throw NotFound();
        folder.IsActive = active;
        audit.Record("folder.active", nameof(WatchedFolder), id.ToString(), active ? "Activó la vigilancia." : "Pausó la vigilancia.");
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Pide revisar una carpeta ahora, esté activa o pausada. Escribe el trabajo y la auditoría.</summary>
    /// <param name="id">Carpeta.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <exception cref="BusinessException">404 <c>FOLDER_NOT_WATCHED</c> si no existe en la organización activa.</exception>
    public async Task RequestScanAsync(int id, CancellationToken cancellationToken)
    {
        if (!await db.WatchedFolders.AnyAsync(f => f.Id == id, cancellationToken))
        {
            throw NotFound();
        }

        await EnqueueScanAsync(id, Guid.NewGuid().ToString("N"), cancellationToken);
    }

    /// <summary>
    /// Carpetas activas de <b>todas</b> las organizaciones. Cruza organizaciones a propósito: sólo lo usa el
    /// programador del Worker, que después encola cada revisión con la organización de su carpeta activada.
    /// </summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Carpetas activas con su organización.</returns>
    public async Task<List<ActiveFolder>> ActiveFoldersAsync(CancellationToken cancellationToken)
    {
        return await db.WatchedFolders.IgnoreQueryFilters().AsNoTracking()
            .Where(f => f.IsActive)
            .OrderBy(f => f.Id)
            .Select(f => new ActiveFolder(f.Id, f.OrganizationId))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Encola la revisión de una carpeta de la organización activa.</summary>
    /// <param name="folderId">Carpeta.</param>
    /// <param name="run">Parte de la clave de idempotencia: el programador usa el intervalo de tiempo, así dos
    /// Workers no encolan la misma revisión; un pedido manual usa un valor único.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>El trabajo encolado o el existente.</returns>
    public Task<EnqueueResult> EnqueueScanAsync(int folderId, string run, CancellationToken cancellationToken)
    {
        return jobs.EnqueueAsync(
            MediaFolderScanJobHandler.JobType,
            $"{MediaFolderScanJobHandler.JobType}:{folderId}:{run}",
            JsonSerializer.Serialize(new FolderScanPayload(folderId)),
            cancellationToken);
    }

    private List<string> Roots()
    {
        return options.Value.AllowedRoots.Where(r => !string.IsNullOrWhiteSpace(r)).Select(Normalize).ToList();
    }

    private static string Normalize(string path)
    {
        try
        {
            return System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path.Trim()));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new BusinessException(400, "La ruta de la carpeta no es válida.", "FOLDER_NOT_ALLOWED");
        }
    }

    // Dentro de la raíz o la raíz misma. Se compara por carpetas completas: "C:\medios2" no está dentro de "C:\medios".
    private static bool IsInside(string path, string root)
    {
        return path.Equals(root, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(root + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static BusinessException NotFound()
    {
        return new BusinessException(404, "La carpeta vigilada no existe.", "FOLDER_NOT_WATCHED");
    }
}

/// <summary>
/// Revisión de una carpeta vigilada. Recorre la carpeta y sus subcarpetas, y por cada archivo de formato admitido
/// que no se vio antes (o que cambió de tamaño o de fecha) lo ingresa a la biblioteca con los derechos de la
/// carpeta. Guarda qué pasó con cada archivo, así la próxima revisión no vuelve a leerlo, y deja en la carpeta la
/// fecha y el resumen de la revisión. Un archivo rechazado o bloqueado no detiene al resto. Nunca modifica ni mueve
/// los archivos de la carpeta. Es idempotente y se puede reanudar: cada archivo se confirma por separado.
/// </summary>
/// <param name="db">Contexto de datos, con la organización del trabajo activa.</param>
/// <param name="media">Biblioteca, para ingresar los archivos.</param>
/// <param name="options">Antigüedad mínima de un archivo.</param>
public sealed class MediaFolderScanJobHandler(AppDbContext db, MediaLibraryService media, IOptions<IngestOptions> options) : IJobHandler
{
    /// <summary>Tipo de trabajo que atiende.</summary>
    public const string JobType = "media.folder-scan";

    /// <inheritdoc />
    public string Type => JobType;

    /// <inheritdoc />
    /// <exception cref="PermanentJobException">Si la carpeta vigilada ya no existe en la base o en el disco.</exception>
    public async Task ExecuteAsync(JobExecution execution, CancellationToken cancellationToken)
    {
        FolderScanPayload? payload = JsonSerializer.Deserialize<FolderScanPayload>(execution.Job.PayloadJson);
        int folderId = payload?.FolderId ?? 0;
        WatchedFolder folder = await db.WatchedFolders.AsNoTracking().FirstOrDefaultAsync(f => f.Id == folderId, cancellationToken)
            ?? throw new PermanentJobException("La carpeta vigilada ya no existe.");
        if (!Directory.Exists(folder.Path))
        {
            await FinishAsync(folderId, "La carpeta no existe o no se puede leer desde el servidor.", cancellationToken);
            throw new PermanentJobException($"La carpeta {folder.Path} no existe o no se puede leer.");
        }

        MediaRightsBody rights = new()
        {
            RightsHolder = folder.RightsHolder,
            LicenseNotes = folder.LicenseNotes,
            ReuseAuthorized = folder.ReuseAuthorized,
            RightsExpireOn = folder.RightsExpireOn,
        };
        List<string> files = Directory.EnumerateFiles(folder.Path, "*", SearchOption.AllDirectories)
            .Where(f => MediaRules.TryGetFormat(Path.GetExtension(f), out _))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        Dictionary<string, WatchedFile> known = await db.WatchedFiles
            .Where(w => w.WatchedFolderId == folderId)
            .ToDictionaryAsync(w => w.RelativePath, StringComparer.OrdinalIgnoreCase, cancellationToken);

        // La fecha de modificación de un archivo la pone el reloj de esta máquina: se compara contra ese mismo reloj.
        DateTime newestAllowed = DateTime.UtcNow.AddSeconds(-options.Value.MinFileAgeSeconds);
        int ingested = 0;
        int duplicates = 0;
        int rejected = 0;
        int waiting = 0;
        for (int i = 0; i < files.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FileInfo info = new(files[i]);
            string relativePath = Path.GetRelativePath(folder.Path, files[i]);
            if (info.LastWriteTimeUtc > newestAllowed)
            {
                waiting++;
                continue;
            }

            if (known.TryGetValue(relativePath, out WatchedFile? seen) && seen.SizeBytes == info.Length && seen.LastWriteUtc == info.LastWriteTimeUtc)
            {
                continue;
            }

            await execution.ReportAsync(i * 100 / files.Count, $"Ingresando {relativePath}", cancellationToken);
            WatchedFile record = seen ?? new WatchedFile { WatchedFolderId = folderId, RelativePath = relativePath };
            record.SizeBytes = info.Length;
            record.LastWriteUtc = info.LastWriteTimeUtc;
            record.Message = null;
            try
            {
                await using FileStream stream = new(files[i], FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
                IngestResult result = await media.IngestAsync(
                    info.Name, rights, stream, cancellationToken, MediaSourceKind.WatchedFolder, files[i]);
                record.MediaAssetId = result.AssetId;
                record.Outcome = result.Outcome == IngestOutcome.Created ? WatchedFileOutcome.Ingested : WatchedFileOutcome.Duplicate;
                ingested += result.Outcome == IngestOutcome.Created ? 1 : 0;
                duplicates += result.Outcome == IngestOutcome.Duplicate ? 1 : 0;
            }
            catch (Common.BusinessException exception)
            {
                record.Outcome = WatchedFileOutcome.Rejected;
                record.Message = exception.Message.Length > 500 ? exception.Message[..500] : exception.Message;
                rejected++;
            }
            catch (IOException)
            {
                // Otro programa lo tiene abierto (por ejemplo, todavía se está copiando): se intenta en la próxima revisión.
                waiting++;
                continue;
            }

            if (seen is null)
            {
                db.WatchedFiles.Add(record);
                known[relativePath] = record;
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        await FinishAsync(
            folderId,
            $"{files.Count} archivos de formato admitido: {ingested} ingresados, {duplicates} ya estaban, {rejected} rechazados, {waiting} en espera.",
            cancellationToken);
    }

    private async Task FinishAsync(int folderId, string summary, CancellationToken cancellationToken)
    {
        await db.WatchedFolders.Where(f => f.Id == folderId).ExecuteUpdateAsync(
            s => s.SetProperty(f => f.LastScanAt, f => (DateTime?)AppDbContext.UtcNow()).SetProperty(f => f.LastScanSummary, summary),
            cancellationToken);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CreativeIntelligence.Web.Services.Jobs;

/// <summary>
/// Lado del Worker de la cola. Opera sobre trabajos de <b>todas</b> las organizaciones (ignora el filtro
/// multiempresa a propósito) y nunca se usa desde un controller. Cada transición lleva el estado de origen y el
/// dueño del arriendo en la condición del <c>UPDATE</c>: si no afectó filas, otro proceso ganó la carrera.
/// Toda hora es la de la base.
/// </summary>
/// <param name="db">Contexto de datos.</param>
/// <param name="options">Configuración de la cola.</param>
public sealed class JobQueue(AppDbContext db, IOptions<JobOptions> options)
{
    private const int ClaimRetries = 5;

    /// <summary>
    /// Toma el siguiente trabajo disponible: uno en cola cuya fecha ya llegó, o uno en curso cuyo arriendo venció
    /// (su Worker murió). Lo deja <c>Running</c>, suma un intento y le pone un arriendo a nombre de
    /// <paramref name="workerId"/>.
    /// </summary>
    /// <param name="workerId">Identificador del Worker que toma el trabajo.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>El trabajo tomado, o nulo si no hay ninguno disponible.</returns>
    public async Task<ClaimedJob?> ClaimNextAsync(string workerId, CancellationToken cancellationToken)
    {
        int leaseSeconds = options.Value.LeaseSeconds;

        // Si otro Worker toma el candidato entre la lectura y el UPDATE, se busca el siguiente.
        for (int attempt = 0; attempt < ClaimRetries; attempt++)
        {
            long? candidateId = await Claimable()
                .OrderBy(j => j.AvailableAt)
                .ThenBy(j => j.Id)
                .Select(j => (long?)j.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (candidateId is null)
            {
                return null;
            }

            int rows = await Claimable()
                .Where(j => j.Id == candidateId)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(j => j.Status, BackgroundJobStatus.Running)
                        .SetProperty(j => j.Attempts, j => j.Attempts + 1)
                        .SetProperty(j => j.LeaseOwner, workerId)
                        .SetProperty(j => j.LeaseExpiresAt, j => (DateTime?)AppDbContext.UtcNow().AddSeconds(leaseSeconds))
                        .SetProperty(j => j.StartedAt, j => j.StartedAt ?? (DateTime?)AppDbContext.UtcNow())
                        .SetProperty(j => j.UpdatedAt, j => AppDbContext.UtcNow()),
                    cancellationToken);
            if (rows == 1)
            {
                return await db.BackgroundJobs.IgnoreQueryFilters().AsNoTracking()
                    .Where(j => j.Id == candidateId)
                    .Select(j => new ClaimedJob(
                        j.Id, j.OrganizationId, j.CreatedByUserId, j.Type, j.PayloadJson, j.Attempts, j.MaxAttempts))
                    .FirstAsync(cancellationToken);
            }
        }

        return null;
    }

    /// <summary>Persiste el avance de un trabajo en curso. No hace nada si el Worker ya no tiene el arriendo.</summary>
    /// <param name="id">Trabajo.</param>
    /// <param name="workerId">Worker que informa.</param>
    /// <param name="percent">Avance; se acota a 0–100.</param>
    /// <param name="step">Descripción del paso; se recorta a 300 caracteres.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    public async Task ReportProgressAsync(
        long id, string workerId, int percent, string step, CancellationToken cancellationToken)
    {
        int bounded = Math.Clamp(percent, 0, 100);
        string trimmed = step.Length > 300 ? step[..300] : step;
        await Owned(id, workerId).ExecuteUpdateAsync(
            s => s.SetProperty(j => j.ProgressPercent, bounded)
                .SetProperty(j => j.ProgressStep, trimmed)
                .SetProperty(j => j.UpdatedAt, j => AppDbContext.UtcNow()),
            cancellationToken);
    }

    /// <summary>Latido: renueva el arriendo y dice si el trabajo debe detenerse.</summary>
    /// <param name="id">Trabajo.</param>
    /// <param name="workerId">Worker que late.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Verdadero si hay que detener el manejador: un usuario pidió cancelar, o el arriendo se perdió.</returns>
    public async Task<bool> HeartbeatAsync(long id, string workerId, CancellationToken cancellationToken)
    {
        int leaseSeconds = options.Value.LeaseSeconds;
        int rows = await Owned(id, workerId).ExecuteUpdateAsync(
            s => s.SetProperty(j => j.LeaseExpiresAt, j => (DateTime?)AppDbContext.UtcNow().AddSeconds(leaseSeconds)),
            cancellationToken);
        if (rows == 0)
        {
            return true;
        }

        return await Owned(id, workerId).AnyAsync(j => j.CancelRequested, cancellationToken);
    }

    /// <summary>Marca el trabajo como terminado bien, con avance 100. No hace nada si se perdió el arriendo.</summary>
    /// <param name="id">Trabajo.</param>
    /// <param name="workerId">Worker que lo ejecutó.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    public async Task CompleteAsync(long id, string workerId, CancellationToken cancellationToken)
    {
        await Owned(id, workerId).ExecuteUpdateAsync(
            s => s.SetProperty(j => j.Status, BackgroundJobStatus.Succeeded)
                .SetProperty(j => j.ProgressPercent, 100)
                .SetProperty(j => j.Error, (string?)null)
                .SetProperty(j => j.LeaseOwner, (string?)null)
                .SetProperty(j => j.LeaseExpiresAt, (DateTime?)null)
                .SetProperty(j => j.FinishedAt, j => (DateTime?)AppDbContext.UtcNow())
                .SetProperty(j => j.UpdatedAt, j => AppDbContext.UtcNow()),
            cancellationToken);
    }

    /// <summary>
    /// Registra un fallo. Si quedan intentos y <paramref name="allowRetry"/> es verdadero, el trabajo vuelve a la
    /// cola con una espera que se duplica en cada intento; si no, queda <c>Failed</c>.
    /// </summary>
    /// <param name="job">Trabajo que falló.</param>
    /// <param name="workerId">Worker que lo ejecutó.</param>
    /// <param name="error">Mensaje del error; se recorta a 4000 caracteres.</param>
    /// <param name="allowRetry">Falso para errores que reintentar no arregla (por ejemplo, tipo sin manejador).</param>
    /// <param name="cancellationToken">Cancelación.</param>
    public async Task FailAsync(
        ClaimedJob job, string workerId, string error, bool allowRetry, CancellationToken cancellationToken)
    {
        string trimmed = error.Length > 4000 ? error[..4000] : error;
        if (allowRetry && job.Attempts < job.MaxAttempts)
        {
            int waitSeconds = options.Value.RetryBaseSeconds * (1 << Math.Min(job.Attempts - 1, 10));
            await Owned(job.Id, workerId).ExecuteUpdateAsync(
                s => s.SetProperty(j => j.Status, BackgroundJobStatus.Pending)
                    .SetProperty(j => j.Error, trimmed)
                    .SetProperty(j => j.LeaseOwner, (string?)null)
                    .SetProperty(j => j.LeaseExpiresAt, (DateTime?)null)
                    .SetProperty(j => j.AvailableAt, j => AppDbContext.UtcNow().AddSeconds(waitSeconds))
                    .SetProperty(j => j.UpdatedAt, j => AppDbContext.UtcNow()),
                cancellationToken);
            return;
        }

        await Owned(job.Id, workerId).ExecuteUpdateAsync(
            s => s.SetProperty(j => j.Status, BackgroundJobStatus.Failed)
                .SetProperty(j => j.Error, trimmed)
                .SetProperty(j => j.LeaseOwner, (string?)null)
                .SetProperty(j => j.LeaseExpiresAt, (DateTime?)null)
                .SetProperty(j => j.FinishedAt, j => (DateTime?)AppDbContext.UtcNow())
                .SetProperty(j => j.UpdatedAt, j => AppDbContext.UtcNow()),
            cancellationToken);
    }

    /// <summary>
    /// Deja <c>Cancelled</c> un trabajo en curso <b>sólo si</b> un usuario pidió cancelarlo. Si el manejador se
    /// detuvo por otra razón (el Worker se apaga), no cambia nada: el arriendo vence y otro Worker lo retoma.
    /// </summary>
    /// <param name="id">Trabajo.</param>
    /// <param name="workerId">Worker que lo ejecutaba.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    public async Task MarkCancelledAsync(long id, string workerId, CancellationToken cancellationToken)
    {
        await Owned(id, workerId)
            .Where(j => j.CancelRequested)
            .ExecuteUpdateAsync(
                s => s.SetProperty(j => j.Status, BackgroundJobStatus.Cancelled)
                    .SetProperty(j => j.LeaseOwner, (string?)null)
                    .SetProperty(j => j.LeaseExpiresAt, (DateTime?)null)
                    .SetProperty(j => j.FinishedAt, j => (DateTime?)AppDbContext.UtcNow())
                    .SetProperty(j => j.UpdatedAt, j => AppDbContext.UtcNow()),
                cancellationToken);
    }

    /// <summary>Hora del cambio más reciente en la cola; punto de partida del notificador de progreso.</summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>La marca de agua, o <see cref="DateTime.MinValue"/> si no hay trabajos.</returns>
    public async Task<DateTime> LatestChangeAsync(CancellationToken cancellationToken)
    {
        DateTime? latest = await db.BackgroundJobs.IgnoreQueryFilters()
            .MaxAsync(j => (DateTime?)j.UpdatedAt, cancellationToken);
        return latest ?? DateTime.MinValue;
    }

    /// <summary>Trabajos de cualquier organización que cambiaron después de <paramref name="since"/>, en orden.</summary>
    /// <param name="since">Marca de agua anterior (hora de la base).</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Cambios, cada uno con la organización a la que hay que avisar.</returns>
    public async Task<List<JobChange>> ChangedSinceAsync(DateTime since, CancellationToken cancellationToken)
    {
        return await db.BackgroundJobs.IgnoreQueryFilters().AsNoTracking()
            .Where(j => j.UpdatedAt > since)
            .OrderBy(j => j.UpdatedAt)
            .Take(500)
            .Select(j => new JobChange(
                j.OrganizationId,
                j.UpdatedAt,
                new JobItem(
                    j.Id, j.Type, j.Status, j.ProgressPercent, j.ProgressStep, j.Attempts, j.MaxAttempts,
                    j.CancelRequested, j.Error, j.CreatedAt, j.StartedAt, j.FinishedAt)))
            .ToListAsync(cancellationToken);
    }

    private IQueryable<BackgroundJob> Claimable()
    {
        return db.BackgroundJobs.IgnoreQueryFilters().Where(j =>
            (j.Status == BackgroundJobStatus.Pending && j.AvailableAt <= AppDbContext.UtcNow())
            || (j.Status == BackgroundJobStatus.Running && j.LeaseExpiresAt < AppDbContext.UtcNow()));
    }

    private IQueryable<BackgroundJob> Owned(long id, string workerId)
    {
        return db.BackgroundJobs.IgnoreQueryFilters().Where(j =>
            j.Id == id && j.Status == BackgroundJobStatus.Running && j.LeaseOwner == workerId);
    }
}

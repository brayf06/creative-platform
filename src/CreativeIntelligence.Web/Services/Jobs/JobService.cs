using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CreativeIntelligence.Web.Services.Jobs;

/// <summary>
/// Operaciones de usuario sobre los trabajos de la organización activa: encolar, consultar, cancelar y reintentar.
/// Todas pasan por el filtro multiempresa: un trabajo de otra organización responde 404.
/// </summary>
/// <param name="db">Contexto de datos.</param>
/// <param name="audit">Auditoría.</param>
/// <param name="current">Usuario y organización que operan.</param>
/// <param name="options">Configuración de la cola.</param>
public sealed class JobService(AppDbContext db, AuditService audit, CurrentContext current, IOptions<JobOptions> options)
{
    /// <summary>Proyección de la entidad al DTO, compartida por las consultas de este servicio y de la cola.</summary>
    internal static readonly Expression<Func<BackgroundJob, JobItem>> ToItem = j => new JobItem(
        j.Id, j.Type, j.Status, j.ProgressPercent, j.ProgressStep, j.Attempts, j.MaxAttempts, j.CancelRequested,
        j.Error, j.CreatedAt, j.StartedAt, j.FinishedAt);

    /// <summary>
    /// Encola un trabajo. Es idempotente: si la organización ya tiene uno con la misma clave, no crea otro y
    /// devuelve el existente. Escribe el trabajo y un registro de auditoría en una transacción.
    /// </summary>
    /// <param name="type">Tipo de trabajo; debe existir un manejador.</param>
    /// <param name="idempotencyKey">Clave única por organización, hasta 200 caracteres
    /// (convención: <c>tipo:recurso:versión</c>).</param>
    /// <param name="payloadJson">Parámetros en JSON.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Identificador y si se creó.</returns>
    public async Task<EnqueueResult> EnqueueAsync(
        string type, string idempotencyKey, string payloadJson, CancellationToken cancellationToken)
    {
        long? existingId = await db.BackgroundJobs.AsNoTracking()
            .Where(j => j.IdempotencyKey == idempotencyKey)
            .Select(j => (long?)j.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (existingId is not null)
        {
            return new EnqueueResult(existingId.Value, false);
        }

        BackgroundJob job = new()
        {
            Type = type,
            IdempotencyKey = idempotencyKey,
            Status = BackgroundJobStatus.Pending,
            MaxAttempts = options.Value.MaxAttempts,
            PayloadJson = payloadJson,
            CreatedByUserId = current.UserId,
        };

        return await db.InTransactionAsync(
            async () =>
            {
                db.BackgroundJobs.Add(job);
                await db.SaveChangesAsync(cancellationToken);
                audit.Record("job.enqueue", nameof(BackgroundJob), job.Id.ToString(), $"Encoló un trabajo {type}.");
                await db.SaveChangesAsync(cancellationToken);
                return new EnqueueResult(job.Id, true);
            },
            cancellationToken);
    }

    /// <summary>Encola un trabajo de diagnóstico; cada llamada crea uno nuevo. Escribe trabajo y auditoría.</summary>
    /// <param name="body">Pasos, espera y fallos simulados.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Identificador del trabajo creado.</returns>
    public Task<EnqueueResult> EnqueueDiagnosticAsync(DiagnosticJobBody body, CancellationToken cancellationToken)
    {
        return EnqueueAsync(
            DiagnosticJobHandler.JobType,
            $"{DiagnosticJobHandler.JobType}:{Guid.NewGuid():N}",
            JsonSerializer.Serialize(body),
            cancellationToken);
    }

    /// <summary>Lista los trabajos de la organización activa, del más nuevo al más viejo. No escribe.</summary>
    /// <param name="page">Paginación.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Página de trabajos.</returns>
    public async Task<PagedResult<JobItem>> ListAsync(PageRequest page, CancellationToken cancellationToken)
    {
        IQueryable<BackgroundJob> query = db.BackgroundJobs.AsNoTracking();
        int total = await query.CountAsync(cancellationToken);
        List<JobItem> items = await query
            .OrderByDescending(j => j.Id)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(ToItem)
            .ToListAsync(cancellationToken);
        return new PagedResult<JobItem>(items, page.Page, page.PageSize, total);
    }

    /// <summary>Devuelve un trabajo de la organización activa. No escribe.</summary>
    /// <param name="id">Identificador.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>El trabajo.</returns>
    /// <exception cref="BusinessException">404 <c>JOB_NOT_FOUND</c> si no existe en la organización activa.</exception>
    public async Task<JobItem> GetAsync(long id, CancellationToken cancellationToken)
    {
        JobItem? item = await db.BackgroundJobs.AsNoTracking()
            .Where(j => j.Id == id)
            .Select(ToItem)
            .FirstOrDefaultAsync(cancellationToken);
        return item ?? throw NotFound();
    }

    /// <summary>Cuenta los trabajos de la organización activa por estado. No escribe.</summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Cantidades por estado.</returns>
    public async Task<JobSummary> SummaryAsync(CancellationToken cancellationToken)
    {
        List<JobStatusCount> counts = await db.BackgroundJobs.AsNoTracking()
            .GroupBy(j => j.Status)
            .Select(g => new JobStatusCount { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        Dictionary<BackgroundJobStatus, int> byStatus = counts.ToDictionary(c => c.Status, c => c.Count);
        return new JobSummary(
            byStatus.GetValueOrDefault(BackgroundJobStatus.Pending),
            byStatus.GetValueOrDefault(BackgroundJobStatus.Running),
            byStatus.GetValueOrDefault(BackgroundJobStatus.Succeeded),
            byStatus.GetValueOrDefault(BackgroundJobStatus.Failed),
            byStatus.GetValueOrDefault(BackgroundJobStatus.Cancelled));
    }

    /// <summary>
    /// Cancela un trabajo. Si está en cola pasa a <c>Cancelled</c>; si está en curso marca el pedido y el Worker
    /// lo detiene en el próximo latido. El estado de origen va en la condición del <c>UPDATE</c>, así que una
    /// carrera con el Worker no deja un estado inconsistente. Escribe el cambio y la auditoría en una transacción.
    /// </summary>
    /// <param name="id">Identificador.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <exception cref="BusinessException">404 <c>JOB_NOT_FOUND</c> si no existe en la organización activa;
    /// 409 <c>JOB_NOT_CANCELLABLE</c> si ya terminó o ya tenía la cancelación pedida.</exception>
    public Task CancelAsync(long id, CancellationToken cancellationToken)
    {
        return db.InTransactionAsync(
            async () =>
            {
                int rows = await db.BackgroundJobs
                    .Where(j => j.Id == id && j.Status == BackgroundJobStatus.Pending)
                    .ExecuteUpdateAsync(
                        s => s.SetProperty(j => j.Status, BackgroundJobStatus.Cancelled)
                            .SetProperty(j => j.FinishedAt, j => (DateTime?)AppDbContext.UtcNow())
                            .SetProperty(j => j.UpdatedAt, j => AppDbContext.UtcNow()),
                        cancellationToken);
                if (rows == 0)
                {
                    rows = await db.BackgroundJobs
                        .Where(j => j.Id == id && j.Status == BackgroundJobStatus.Running && !j.CancelRequested)
                        .ExecuteUpdateAsync(
                            s => s.SetProperty(j => j.CancelRequested, true)
                                .SetProperty(j => j.UpdatedAt, j => AppDbContext.UtcNow()),
                            cancellationToken);
                }

                if (rows == 0)
                {
                    bool exists = await db.BackgroundJobs.AnyAsync(j => j.Id == id, cancellationToken);
                    throw exists
                        ? new BusinessException(409, "El trabajo ya terminó o ya se pidió su cancelación.", "JOB_NOT_CANCELLABLE")
                        : NotFound();
                }

                audit.Record("job.cancel", nameof(BackgroundJob), id.ToString(), "Pidió cancelar el trabajo.");
                await db.SaveChangesAsync(cancellationToken);
                return true;
            },
            cancellationToken);
    }

    /// <summary>
    /// Vuelve a encolar un trabajo fallido o cancelado, con los intentos en cero. Escribe el cambio y la
    /// auditoría en una transacción.
    /// </summary>
    /// <param name="id">Identificador.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <exception cref="BusinessException">404 <c>JOB_NOT_FOUND</c> si no existe en la organización activa;
    /// 409 <c>JOB_NOT_RETRYABLE</c> si no está fallido ni cancelado.</exception>
    public Task RetryAsync(long id, CancellationToken cancellationToken)
    {
        return db.InTransactionAsync(
            async () =>
            {
                int rows = await db.BackgroundJobs
                    .Where(j => j.Id == id
                        && (j.Status == BackgroundJobStatus.Failed || j.Status == BackgroundJobStatus.Cancelled))
                    .ExecuteUpdateAsync(
                        s => s.SetProperty(j => j.Status, BackgroundJobStatus.Pending)
                            .SetProperty(j => j.Attempts, 0)
                            .SetProperty(j => j.ProgressPercent, 0)
                            .SetProperty(j => j.ProgressStep, (string?)null)
                            .SetProperty(j => j.CancelRequested, false)
                            .SetProperty(j => j.Error, (string?)null)
                            .SetProperty(j => j.FinishedAt, (DateTime?)null)
                            .SetProperty(j => j.AvailableAt, j => AppDbContext.UtcNow())
                            .SetProperty(j => j.UpdatedAt, j => AppDbContext.UtcNow()),
                        cancellationToken);
                if (rows == 0)
                {
                    bool exists = await db.BackgroundJobs.AnyAsync(j => j.Id == id, cancellationToken);
                    throw exists
                        ? new BusinessException(409, "Sólo se puede reintentar un trabajo fallido o cancelado.", "JOB_NOT_RETRYABLE")
                        : NotFound();
                }

                audit.Record("job.retry", nameof(BackgroundJob), id.ToString(), "Reintentó el trabajo.");
                await db.SaveChangesAsync(cancellationToken);
                return true;
            },
            cancellationToken);
    }

    private static BusinessException NotFound()
    {
        return new BusinessException(404, "El trabajo no existe.", "JOB_NOT_FOUND");
    }
}

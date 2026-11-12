using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CreativeIntelligence.Web.Services.Jobs;

/// <summary>
/// Toma y ejecuta trabajos de a uno. Lo usa el proceso Worker en un bucle; cada trabajo corre en su propio ámbito
/// de dependencias, con la organización del trabajo activada.
/// </summary>
/// <param name="scopes">Fábrica de ámbitos: uno por trabajo y otro para el latido.</param>
/// <param name="options">Configuración de la cola.</param>
/// <param name="logger">Log.</param>
public sealed class JobRunner(IServiceScopeFactory scopes, IOptions<JobOptions> options, ILogger<JobRunner> logger)
{
    /// <summary>Identificador de este Worker; es el dueño de los arriendos que toma.</summary>
    public string WorkerId { get; } = $"{Environment.MachineName}:{Guid.NewGuid():N}";

    /// <summary>
    /// Toma el siguiente trabajo y lo ejecuta hasta un estado final o hasta devolverlo a la cola. Mientras corre,
    /// un latido renueva el arriendo y detiene el manejador si un usuario pidió cancelar. Escribe el estado, el
    /// progreso y el error del trabajo.
    /// </summary>
    /// <param name="stopping">Se cancela cuando el proceso se detiene; el trabajo en curso queda para que otro
    /// Worker lo retome al vencer el arriendo.</param>
    /// <returns>Falso si la cola estaba vacía.</returns>
    public async Task<bool> ProcessNextAsync(CancellationToken stopping)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        JobQueue queue = scope.ServiceProvider.GetRequiredService<JobQueue>();
        ClaimedJob? job = await queue.ClaimNextAsync(WorkerId, stopping);
        if (job is null)
        {
            return false;
        }

        // Un trabajo retomado tras vencer el arriendo puede llegar ya sin intentos disponibles.
        if (job.Attempts > job.MaxAttempts)
        {
            await queue.FailAsync(job, WorkerId, "Se agotaron los intentos: el Worker que lo ejecutaba se detuvo.", false, stopping);
            return true;
        }

        scope.ServiceProvider.GetRequiredService<CurrentContext>().Set(job.OrganizationId, job.CreatedByUserId, "worker");
        IJobHandler? handler = scope.ServiceProvider.GetServices<IJobHandler>().FirstOrDefault(h => h.Type == job.Type);
        if (handler is null)
        {
            await queue.FailAsync(job, WorkerId, $"No hay manejador para el tipo '{job.Type}'.", false, stopping);
            return true;
        }

        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        Task heartbeat = HeartbeatLoopAsync(job.Id, cancellation);
        try
        {
            await handler.ExecuteAsync(new JobExecution(job, queue, WorkerId), cancellation.Token);
            await queue.CompleteAsync(job.Id, WorkerId, CancellationToken.None);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            await queue.MarkCancelledAsync(job.Id, WorkerId, CancellationToken.None);
        }
        catch (PermanentJobException exception)
        {
            await queue.FailAsync(job, WorkerId, exception.Message, false, CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !stopping.IsCancellationRequested)
        {
            // Cualquier error del manejador se registra en el trabajo: decide el reintento y es lo que ve el usuario.
            logger.LogError(exception, "Falló el trabajo {JobId} ({JobType}), intento {Attempt}", job.Id, job.Type, job.Attempts);
            await queue.FailAsync(job, WorkerId, DescribeError(exception), true, CancellationToken.None);
        }
        finally
        {
            await cancellation.CancelAsync();
            await heartbeat;
        }

        return true;
    }

    private async Task HeartbeatLoopAsync(long jobId, CancellationTokenSource cancellation)
    {
        // Ámbito propio: el DbContext del manejador no admite uso concurrente.
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        JobQueue queue = scope.ServiceProvider.GetRequiredService<JobQueue>();
        try
        {
            while (true)
            {
                await Task.Delay(options.Value.HeartbeatMs, cancellation.Token);
                bool mustStop = await queue.HeartbeatAsync(jobId, WorkerId, cancellation.Token);
                if (mustStop)
                {
                    await cancellation.CancelAsync();
                    return;
                }
            }
        }
        catch (Exception) when (cancellation.IsCancellationRequested)
        {
            // Fin normal: el manejador terminó o el proceso se detiene. Se filtra por la cancelación y no por el
            // tipo porque SQL Server, si lo cancelan en medio de un comando, lanza SqlException y no
            // OperationCanceledException.
        }
    }

    private static string DescribeError(Exception exception)
    {
        return exception is JsonException
            ? "Los parámetros del trabajo no son válidos."
            : exception.Message;
    }
}

/// <summary>
/// Trabajo de diagnóstico: recorre pasos con espera informando progreso, y puede fallar a propósito en los
/// primeros intentos. Sirve para verificar cola, progreso en vivo, reintentos y cancelación sin depender de
/// ningún módulo de negocio. No escribe nada fuera de su propio progreso.
/// </summary>
public sealed class DiagnosticJobHandler : IJobHandler
{
    /// <summary>Tipo de trabajo que atiende.</summary>
    public const string JobType = "diagnostic.progress";

    /// <inheritdoc />
    public string Type => JobType;

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Fallo simulado, si el intento actual está dentro de
    /// <c>FailFirstAttempts</c>.</exception>
    public async Task ExecuteAsync(JobExecution execution, CancellationToken cancellationToken)
    {
        DiagnosticJobBody body = JsonSerializer.Deserialize<DiagnosticJobBody>(execution.Job.PayloadJson)
            ?? new DiagnosticJobBody();
        int failAtStep = (body.Steps + 1) / 2;
        for (int step = 1; step <= body.Steps; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (step == failAtStep && execution.Job.Attempts <= body.FailFirstAttempts)
            {
                throw new InvalidOperationException($"Fallo simulado en el intento {execution.Job.Attempts}.");
            }

            await Task.Delay(body.StepDelayMs, cancellationToken);
            await execution.ReportAsync(step * 100 / body.Steps, $"Paso {step} de {body.Steps}", cancellationToken);
        }
    }
}

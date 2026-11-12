using Microsoft.Extensions.Options;

namespace CreativeIntelligence.Web.Services.Jobs;

/// <summary>Bucle de la cola de trabajos, alojado en el proceso de la Web: ejecuta trabajos mientras haya y espera
/// cuando la cola está vacía.</summary>
/// <param name="runner">Toma y ejecuta de a un trabajo.</param>
/// <param name="options">Configuración de la cola.</param>
/// <param name="logger">Log.</param>
public sealed class JobProcessor(JobRunner runner, IOptions<JobOptions> options, ILogger<JobProcessor> logger)
    : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Worker {WorkerId} iniciado", runner.WorkerId);
        while (!stoppingToken.IsCancellationRequested)
        {
            bool processed = false;
            try
            {
                processed = await runner.ProcessNextAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // Un error de infraestructura (base caída) no debe matar el bucle: se registra y se reintenta.
                logger.LogError(exception, "Error en el bucle del Worker");
            }

            if (!processed)
            {
                await Task.Delay(options.Value.PollMs, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }
    }
}

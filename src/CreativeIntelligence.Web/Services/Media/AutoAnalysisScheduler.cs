using Microsoft.Extensions.Options;

namespace CreativeIntelligence.Web.Services.Media;

/// <summary>
/// Programador del análisis automático: cada <c>Ai:AutoAnalyzeIntervalMinutes</c> pide una tanda de análisis en
/// cada organización que lo tiene activado. La tanda respeta los mismos topes que la manual (por video, por tanda
/// y presupuesto mensual), así que activar el modo automático no puede gastar más que eso.
/// </summary>
/// <param name="scopes">Fábrica de ámbitos: uno para listar y uno por organización, con ella activada.</param>
/// <param name="options">Intervalo.</param>
/// <param name="logger">Log.</param>
public sealed class AutoAnalysisScheduler(IServiceScopeFactory scopes, IOptions<AiOptions> options, ILogger<AutoAnalysisScheduler> logger)
    : BackgroundService
{
    /// <summary>Pide una tanda de análisis en cada organización con el modo automático activado.</summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Cantidad total de videos cuyo análisis quedó en la cola.</returns>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        List<int> organizations;
        await using (AsyncServiceScope scope = scopes.CreateAsyncScope())
        {
            organizations = await scope.ServiceProvider.GetRequiredService<AnalysisService>().AutoAnalyzeOrganizationsAsync(cancellationToken);
        }

        int enqueued = 0;
        foreach (int organizationId in organizations)
        {
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<CurrentContext>().Set(organizationId, null, "sistema");
            BatchResult result = await scope.ServiceProvider.GetRequiredService<AnalysisService>().RequestBatchAsync(cancellationToken);
            enqueued += result.Enqueued;
        }

        return enqueued;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        int minutes = Math.Max(1, options.Value.AutoAnalyzeIntervalMinutes);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                int enqueued = await RunOnceAsync(stoppingToken);
                if (enqueued > 0)
                {
                    logger.LogInformation("Análisis automático: {Count} videos encolados", enqueued);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // Una falla transitoria de la base no debe apagar el programador: se registra y se reintenta.
                logger.LogError(exception, "Error en el análisis automático");
            }

            await Task.Delay(TimeSpan.FromMinutes(minutes), stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }
}
using Microsoft.Extensions.Options;

namespace CreativeIntelligence.Web.Services.Media;

/// <summary>
/// Programador de las carpetas vigiladas: cada <c>Ingest:ScanIntervalMinutes</c> encola la revisión de todas las
/// carpetas activas. La clave de cada trabajo incluye el intervalo de tiempo, así que dos Workers corriendo a la
/// vez no encolan la misma revisión dos veces.
/// </summary>
/// <param name="scopes">Fábrica de ámbitos: uno para listar y uno por carpeta, con su organización activa.</param>
/// <param name="options">Intervalo de revisión.</param>
/// <param name="logger">Log.</param>
public sealed class FolderScanScheduler(IServiceScopeFactory scopes, IOptions<IngestOptions> options, ILogger<FolderScanScheduler> logger)
    : BackgroundService
{
    /// <summary>
    /// Encola la revisión de cada carpeta activa de cualquier organización para el intervalo indicado. Es
    /// idempotente dentro del mismo intervalo.
    /// </summary>
    /// <param name="interval">Identificador del intervalo de tiempo; forma parte de la clave de idempotencia.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Cantidad de revisiones encoladas ahora (sin contar las que ya existían).</returns>
    public async Task<int> EnqueueDueScansAsync(string interval, CancellationToken cancellationToken)
    {
        List<ActiveFolder> folders;
        await using (AsyncServiceScope scope = scopes.CreateAsyncScope())
        {
            folders = await scope.ServiceProvider.GetRequiredService<WatchedFolderService>().ActiveFoldersAsync(cancellationToken);
        }

        int enqueued = 0;
        foreach (ActiveFolder folder in folders)
        {
            // Cada carpeta se encola con su organización activada: el trabajo queda dentro de esa organización.
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<CurrentContext>().Set(folder.OrganizationId, null, "sistema");
            WatchedFolderService service = scope.ServiceProvider.GetRequiredService<WatchedFolderService>();
            if ((await service.EnqueueScanAsync(folder.FolderId, interval, cancellationToken)).Created)
            {
                enqueued++;
            }
        }

        return enqueued;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        int minutes = Math.Max(1, options.Value.ScanIntervalMinutes);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // El intervalo sólo arma la clave del trabajo; no decide ninguna regla de negocio.
                string interval = (DateTime.UtcNow.Ticks / TimeSpan.FromMinutes(minutes).Ticks).ToString();
                int enqueued = await EnqueueDueScansAsync(interval, stoppingToken);
                if (enqueued > 0)
                {
                    logger.LogInformation("Se encoló la revisión de {Count} carpetas vigiladas", enqueued);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // Una falla transitoria de la base no debe apagar el programador: se registra y se reintenta.
                logger.LogError(exception, "Error al programar la revisión de carpetas vigiladas");
            }

            await Task.Delay(TimeSpan.FromMinutes(minutes), stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }
}

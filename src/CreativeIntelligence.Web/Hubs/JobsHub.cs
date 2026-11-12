using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace CreativeIntelligence.Web.Hubs;

/// <summary>
/// Canal de progreso de trabajos. Cada conexión entra sólo al grupo de su organización activa, tomada de la
/// sesión (no de un parámetro del cliente), así que no puede recibir avisos de otra organización.
/// </summary>
[Authorize(Policy = Policies.OrgMember)]
public sealed class JobsHub : Hub
{
    /// <summary>Ruta del hub.</summary>
    public const string Path = "/hubs/jobs";

    /// <summary>Nombre del mensaje que recibe el cliente con un <see cref="JobItem"/>.</summary>
    public const string JobUpdated = "jobUpdated";

    /// <summary>Grupo de SignalR de una organización.</summary>
    /// <param name="organizationId">Organización.</param>
    /// <returns>Nombre del grupo.</returns>
    public static string GroupName(int organizationId)
    {
        return $"org-{organizationId}";
    }

    /// <inheritdoc />
    public override async Task OnConnectedAsync()
    {
        int organizationId = Context.User?.OrganizationId() ?? 0;
        if (organizationId != 0)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(organizationId));
        }

        await base.OnConnectedAsync();
    }
}

/// <summary>
/// Publica por SignalR los cambios de los trabajos. El Worker es otro proceso y sólo escribe en la base; este
/// servicio consulta periódicamente qué cambió desde la última marca de agua y avisa al grupo de cada organización.
/// </summary>
/// <param name="scopes">Fábrica de ámbitos, uno por consulta.</param>
/// <param name="hub">Contexto del hub.</param>
/// <param name="options">Configuración de la cola (intervalo <c>NotifyMs</c>).</param>
/// <param name="logger">Log.</param>
public sealed class JobProgressNotifier(
    IServiceScopeFactory scopes,
    IHubContext<JobsHub> hub,
    IOptions<JobOptions> options,
    ILogger<JobProgressNotifier> logger)
    : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        DateTime? watermark = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using AsyncServiceScope scope = scopes.CreateAsyncScope();
                JobQueue queue = scope.ServiceProvider.GetRequiredService<JobQueue>();
                if (watermark is null)
                {
                    // Al arrancar no se reenvía la historia: se parte del último cambio existente.
                    watermark = await queue.LatestChangeAsync(stoppingToken);
                }
                else
                {
                    List<JobChange> changes = await queue.ChangedSinceAsync(watermark.Value, stoppingToken);
                    foreach (JobChange change in changes)
                    {
                        await hub.Clients.Group(JobsHub.GroupName(change.OrganizationId))
                            .SendAsync(JobsHub.JobUpdated, change.Job, stoppingToken);
                        watermark = change.UpdatedAt;
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // Una falla transitoria de la base no debe apagar los avisos: se registra y se reintenta.
                logger.LogError(exception, "Error al notificar el progreso de trabajos");
            }

            await Task.Delay(options.Value.NotifyMs, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }
}

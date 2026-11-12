using System.ComponentModel.DataAnnotations;

namespace CreativeIntelligence.Web.Services.Jobs;

/// <summary>Configuración de la cola de trabajos (sección <c>Jobs</c>).</summary>
public sealed class JobOptions
{
    /// <summary>Segundos que dura el arriendo de un trabajo tomado; el latido lo renueva.</summary>
    public int LeaseSeconds { get; set; } = 60;

    /// <summary>Milisegundos entre latidos del Worker (renueva el arriendo y revisa la cancelación).</summary>
    public int HeartbeatMs { get; set; } = 2000;

    /// <summary>Milisegundos de espera del Worker cuando la cola está vacía.</summary>
    public int PollMs { get; set; } = 1000;

    /// <summary>Milisegundos entre consultas del notificador de progreso de la Web.</summary>
    public int NotifyMs { get; set; } = 1000;

    /// <summary>Base de la espera entre reintentos, en segundos; se duplica en cada intento.</summary>
    public int RetryBaseSeconds { get; set; } = 5;

    /// <summary>Intentos por defecto de un trabajo nuevo.</summary>
    public int MaxAttempts { get; set; } = 3;
}

/// <summary>
/// Fallo que reintentar no arregla (archivo ilegible, recurso que ya no existe). Un manejador lo lanza para que
/// el trabajo quede <c>Failed</c> de inmediato, sin gastar los intentos restantes.
/// </summary>
/// <param name="message">Mensaje para el usuario; queda como error del trabajo.</param>
public sealed class PermanentJobException(string message) : Exception(message);

/// <summary>Ejecuta un tipo de trabajo. Una implementación por tipo; debe ser idempotente y atender la cancelación.</summary>
public interface IJobHandler
{
    /// <summary>Tipo de trabajo que atiende; coincide con <c>BackgroundJob.Type</c>.</summary>
    string Type { get; }

    /// <summary>Ejecuta el trabajo. Si lanza, la cola lo reintenta o lo marca fallido.</summary>
    /// <param name="execution">Trabajo tomado y canal para informar progreso.</param>
    /// <param name="cancellationToken">Se cancela si un usuario pidió cancelar, si se perdió el arriendo o si el
    /// Worker se está deteniendo.</param>
    Task ExecuteAsync(JobExecution execution, CancellationToken cancellationToken);
}

/// <summary>Lo que recibe un manejador: el trabajo tomado y cómo informar el avance.</summary>
/// <param name="job">Trabajo tomado.</param>
/// <param name="queue">Cola, para persistir el progreso.</param>
/// <param name="workerId">Worker que tiene el arriendo.</param>
public sealed class JobExecution(ClaimedJob job, JobQueue queue, string workerId)
{
    /// <summary>Trabajo en ejecución.</summary>
    public ClaimedJob Job { get; } = job;

    /// <summary>Persiste el avance; la Web lo publica por SignalR.</summary>
    /// <param name="percent">Avance de 0 a 100.</param>
    /// <param name="step">Descripción del paso, hasta 300 caracteres.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    public Task ReportAsync(int percent, string step, CancellationToken cancellationToken)
    {
        return queue.ReportProgressAsync(Job.Id, workerId, percent, step, cancellationToken);
    }
}

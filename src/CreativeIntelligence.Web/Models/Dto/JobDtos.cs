using System.ComponentModel.DataAnnotations;

namespace CreativeIntelligence.Web.Models.Dto;

/// <summary>Un trabajo, para listados, detalle y avisos de progreso.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Type">Tipo de trabajo.</param>
/// <param name="Status">Estado; en JSON viaja como texto (<c>Pending</c>, <c>Running</c>…).</param>
/// <param name="ProgressPercent">Avance de 0 a 100.</param>
/// <param name="ProgressStep">Paso en curso; nulo si aún no informó.</param>
/// <param name="Attempts">Intentos hechos.</param>
/// <param name="MaxAttempts">Tope de intentos.</param>
/// <param name="CancelRequested">Si hay una cancelación pendiente de ser atendida por el Worker.</param>
/// <param name="Error">Último error; nulo si no falló.</param>
/// <param name="CreatedAt">Alta, UTC.</param>
/// <param name="StartedAt">Primer inicio, UTC; nulo si nunca corrió.</param>
/// <param name="FinishedAt">Fin, UTC; nulo si no terminó.</param>
public sealed record JobItem(
    long Id,
    string Type,
    BackgroundJobStatus Status,
    int ProgressPercent,
    string? ProgressStep,
    int Attempts,
    int MaxAttempts,
    bool CancelRequested,
    string? Error,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? FinishedAt);

/// <summary>Resultado de encolar.</summary>
/// <param name="Id">Identificador del trabajo, nuevo o existente.</param>
/// <param name="Created">Falso si ya existía un trabajo con esa clave de idempotencia y no se creó otro.</param>
public sealed record EnqueueResult(long Id, bool Created);

/// <summary>Cantidad de trabajos de la organización por estado.</summary>
/// <param name="Pending">En cola.</param>
/// <param name="Running">En curso.</param>
/// <param name="Succeeded">Terminados bien.</param>
/// <param name="Failed">Fallidos.</param>
/// <param name="Cancelled">Cancelados.</param>
public sealed record JobSummary(int Pending, int Running, int Succeeded, int Failed, int Cancelled);

/// <summary>Proyección intermedia del resumen: EF agrupa sobre propiedades, no sobre constructores.</summary>
public sealed class JobStatusCount
{
    /// <summary>Estado.</summary>
    public BackgroundJobStatus Status { get; init; }

    /// <summary>Cantidad de trabajos en ese estado.</summary>
    public int Count { get; init; }
}

/// <summary>Cambio de un trabajo detectado por el notificador, con la organización a la que hay que avisar.</summary>
/// <param name="OrganizationId">Organización dueña del trabajo.</param>
/// <param name="UpdatedAt">Marca de agua: hora de la base del cambio.</param>
/// <param name="Job">Estado actual del trabajo.</param>
public sealed record JobChange(int OrganizationId, DateTime UpdatedAt, JobItem Job);

/// <summary>Trabajo tomado por un Worker.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="OrganizationId">Organización dueña; el Worker la activa antes de ejecutar el manejador.</param>
/// <param name="CreatedByUserId">Usuario que lo encoló; nulo si fue el sistema.</param>
/// <param name="Type">Tipo; selecciona el manejador.</param>
/// <param name="PayloadJson">Parámetros en JSON.</param>
/// <param name="Attempts">Número de intento actual, desde 1.</param>
/// <param name="MaxAttempts">Tope de intentos.</param>
public sealed record ClaimedJob(
    long Id, int OrganizationId, string? CreatedByUserId, string Type, string PayloadJson, int Attempts, int MaxAttempts);

/// <summary>Parámetros del trabajo de diagnóstico, que recorre pasos con espera para probar la cola de punta a punta.</summary>
public sealed class DiagnosticJobBody
{
    /// <summary>Cantidad de pasos, de 1 a 60.</summary>
    [Range(1, 60, ErrorMessage = "Los pasos deben estar entre 1 y 60.")]
    public int Steps { get; set; } = 10;

    /// <summary>Espera por paso en milisegundos, de 0 a 5000.</summary>
    [Range(0, 5000, ErrorMessage = "La espera por paso debe estar entre 0 y 5000 ms.")]
    public int StepDelayMs { get; set; } = 500;

    /// <summary>Cuántos intentos iniciales fallan a propósito (0 a 10), para probar los reintentos.</summary>
    [Range(0, 10, ErrorMessage = "Los intentos que fallan deben estar entre 0 y 10.")]
    public int FailFirstAttempts { get; set; }
}

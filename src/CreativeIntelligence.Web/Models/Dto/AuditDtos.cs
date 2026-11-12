using Microsoft.EntityFrameworkCore;

namespace CreativeIntelligence.Web.Models.Dto;

/// <summary>Fila del listado de auditoría.</summary>
/// <param name="Id">Identificador del registro.</param>
/// <param name="CreatedAt">Momento de la acción, en UTC.</param>
/// <param name="UserName">Quién la hizo.</param>
/// <param name="Action">Código estable de la acción, por ejemplo <c>job.cancel</c>.</param>
/// <param name="EntityType">Tipo de entidad afectada.</param>
/// <param name="EntityId">Identificador de la entidad afectada.</param>
/// <param name="Summary">Descripción legible.</param>
public sealed record AuditItem(
    long Id, DateTime CreatedAt, string UserName, string Action, string EntityType, string EntityId, string Summary);

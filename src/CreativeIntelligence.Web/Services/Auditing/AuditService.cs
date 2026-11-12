using Microsoft.EntityFrameworkCore;

namespace CreativeIntelligence.Web.Services.Auditing;

/// <summary>Auditoría de operaciones de la organización activa.</summary>
/// <param name="db">Contexto de datos.</param>
/// <param name="current">Usuario y organización que operan.</param>
public sealed class AuditService(AppDbContext db, CurrentContext current)
{
    /// <summary>
    /// Agrega un registro de auditoría al contexto <b>sin guardar</b>: se persiste con el próximo
    /// <c>SaveChangesAsync</c> del servicio que lo llama, en la misma transacción que el cambio auditado.
    /// </summary>
    /// <param name="action">Código estable, <c>entidad.verbo</c>.</param>
    /// <param name="entityType">Tipo de entidad afectada.</param>
    /// <param name="entityId">Identificador de la entidad afectada.</param>
    /// <param name="summary">Descripción legible, hasta 1000 caracteres.</param>
    /// <param name="organizationId">Organización del registro; por defecto la activa. Se indica sólo al operar
    /// sobre otra organización (alta de una organización por el administrador de plataforma).</param>
    public void Record(string action, string entityType, string entityId, string summary, int? organizationId = null)
    {
        db.AuditEntries.Add(new AuditEntry
        {
            OrganizationId = organizationId ?? current.OrganizationId,
            UserId = current.UserId,
            UserName = current.UserName,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Summary = summary,
        });
    }

    /// <summary>Lista la auditoría de la organización activa, de la más reciente a la más antigua. No escribe.</summary>
    /// <param name="page">Paginación.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Página de registros.</returns>
    public async Task<PagedResult<AuditItem>> ListAsync(PageRequest page, CancellationToken cancellationToken)
    {
        IQueryable<AuditEntry> query = db.AuditEntries.AsNoTracking();
        int total = await query.CountAsync(cancellationToken);
        List<AuditItem> items = await query
            .OrderByDescending(e => e.Id)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(e => new AuditItem(e.Id, e.CreatedAt, e.UserName, e.Action, e.EntityType, e.EntityId, e.Summary))
            .ToListAsync(cancellationToken);
        return new PagedResult<AuditItem>(items, page.Page, page.PageSize, total);
    }
}

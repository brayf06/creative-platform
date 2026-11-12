using System.ComponentModel.DataAnnotations;

namespace CreativeIntelligence.Web.Services.Common;

/// <summary>
/// Error de negocio con status HTTP y código estable. El filtro global de la Web lo traduce a JSON o a un aviso
/// en pantalla; el mensaje es apto para mostrar al usuario.
/// </summary>
/// <param name="status">Status HTTP a devolver (400, 401, 403, 404, 409…).</param>
/// <param name="message">Mensaje para el usuario, en español.</param>
/// <param name="code">Código estable para el cliente, en mayúsculas, por ejemplo <c>JOB_NOT_FOUND</c>.</param>
public sealed class BusinessException(int status, string message, string code) : Exception(message)
{
    /// <summary>Status HTTP a devolver.</summary>
    public int Status { get; } = status;

    /// <summary>Código estable para el cliente.</summary>
    public string Code { get; } = code;
}

/// <summary>
/// Quién está operando y sobre qué organización, durante una solicitud web o la ejecución de un trabajo.
/// La Web lo carga desde los claims; el Worker, desde el trabajo que tomó. Con <see cref="OrganizationId"/> en 0
/// el filtro global no devuelve ninguna fila de negocio.
/// </summary>
public sealed class CurrentContext
{
    /// <summary>Organización activa; 0 si no hay ninguna.</summary>
    public int OrganizationId { get; private set; }

    /// <summary>Usuario que opera; nulo si es el sistema.</summary>
    public string? UserId { get; private set; }

    /// <summary>Nombre para la auditoría.</summary>
    public string UserName { get; private set; } = "sistema";

    /// <summary>Fija la organización y el usuario del ámbito actual.</summary>
    /// <param name="organizationId">Organización activa; 0 para ninguna.</param>
    /// <param name="userId">Usuario que opera; nulo si es el sistema.</param>
    /// <param name="userName">Nombre para la auditoría.</param>
    public void Set(int organizationId, string? userId, string userName)
    {
        OrganizationId = organizationId;
        UserId = userId;
        UserName = userName;
    }
}

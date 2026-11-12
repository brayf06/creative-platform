using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CreativeIntelligence.Web.Models.Dto;

/// <summary>Fila del listado de organizaciones.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Name">Nombre.</param>
/// <param name="IsActive">Si está activa.</param>
/// <param name="MemberCount">Cantidad de miembros.</param>
/// <param name="CreatedAt">Alta, UTC.</param>
public sealed record OrganizationItem(int Id, string Name, bool IsActive, int MemberCount, DateTime CreatedAt);

/// <summary>Organización a la que pertenece un usuario, con su rol en ella.</summary>
/// <param name="OrganizationId">Identificador de la organización.</param>
/// <param name="Name">Nombre de la organización.</param>
/// <param name="Role">Rol del usuario en ella.</param>
public sealed record MembershipItem(int OrganizationId, string Name, OrganizationRole Role);

/// <summary>Fila del listado de miembros.</summary>
/// <param name="Id">Identificador de la pertenencia.</param>
/// <param name="Email">Email del usuario.</param>
/// <param name="FullName">Nombre y apellido.</param>
/// <param name="Role">Rol en la organización.</param>
/// <param name="IsActive">Si puede entrar a la organización.</param>
/// <param name="CreatedAt">Alta, UTC.</param>
public sealed record MemberItem(int Id, string Email, string FullName, OrganizationRole Role, bool IsActive, DateTime CreatedAt);

/// <summary>Alta de una organización.</summary>
public sealed class CreateOrganizationBody
{
    /// <summary>Nombre, único en la plataforma. De 2 a 150 caracteres.</summary>
    [Required(ErrorMessage = "El nombre es obligatorio.")]
    [StringLength(150, MinimumLength = 2, ErrorMessage = "El nombre debe tener entre 2 y 150 caracteres.")]
    public string Name { get; set; } = string.Empty;
}

/// <summary>Alta de un miembro en la organización activa.</summary>
public sealed class AddMemberBody
{
    /// <summary>Email del usuario. Si ya existe en la plataforma se lo vincula; si no, se lo crea.</summary>
    [Required(ErrorMessage = "El email es obligatorio.")]
    [EmailAddress(ErrorMessage = "El email no es válido.")]
    [StringLength(256)]
    public string Email { get; set; } = string.Empty;

    /// <summary>Nombre y apellido. Sólo se usa al crear el usuario.</summary>
    [StringLength(150)]
    public string? FullName { get; set; }

    /// <summary>Contraseña inicial. Obligatoria sólo si el usuario no existe.</summary>
    [StringLength(100)]
    public string? Password { get; set; }

    /// <summary>Rol en la organización.</summary>
    public OrganizationRole Role { get; set; } = OrganizationRole.Viewer;
}

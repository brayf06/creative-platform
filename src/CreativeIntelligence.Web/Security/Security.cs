using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace CreativeIntelligence.Web.Security;

/// <summary>Claims propios de la sesión: organización activa y rol en ella.</summary>
public static class OrgClaims
{
    /// <summary>Identificador de la organización activa.</summary>
    public const string OrganizationId = "ci:org";

    /// <summary>Nombre de la organización activa.</summary>
    public const string OrganizationName = "ci:org_name";

    /// <summary>Rol en la organización activa: <c>Viewer</c>, <c>Editor</c> o <c>Admin</c>.</summary>
    public const string Role = "ci:org_role";

    /// <summary>Nombre y apellido del usuario.</summary>
    public const string FullName = "ci:full_name";
}

/// <summary>Nombres de las políticas de autorización.</summary>
public static class Policies
{
    /// <summary>Tiene una organización activa.</summary>
    public const string OrgMember = "OrgMember";

    /// <summary>Es editor o administrador de la organización activa.</summary>
    public const string OrgEditor = "OrgEditor";

    /// <summary>Es administrador de la organización activa.</summary>
    public const string OrgAdmin = "OrgAdmin";

    /// <summary>Es administrador de la plataforma.</summary>
    public const string PlatformAdmin = "PlatformAdmin";
}

/// <summary>Lectura de los claims de organización, para vistas y middleware.</summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>Organización activa; 0 si la sesión no tiene ninguna.</summary>
    /// <param name="user">Usuario de la solicitud.</param>
    /// <returns>Identificador de la organización, o 0.</returns>
    public static int OrganizationId(this ClaimsPrincipal user)
    {
        return int.TryParse(user.FindFirstValue(OrgClaims.OrganizationId), out int id) ? id : 0;
    }

    /// <summary>Si es administrador de la organización activa.</summary>
    /// <param name="user">Usuario de la solicitud.</param>
    /// <returns>Verdadero si su rol es <c>Admin</c>.</returns>
    public static bool IsOrgAdmin(this ClaimsPrincipal user)
    {
        return user.HasClaim(OrgClaims.Role, nameof(OrganizationRole.Admin));
    }

    /// <summary>Si puede modificar contenido y lanzar trabajos en la organización activa.</summary>
    /// <param name="user">Usuario de la solicitud.</param>
    /// <returns>Verdadero si su rol es <c>Admin</c> o <c>Editor</c>.</returns>
    public static bool CanEdit(this ClaimsPrincipal user)
    {
        return user.IsOrgAdmin() || user.HasClaim(OrgClaims.Role, nameof(OrganizationRole.Editor));
    }

    /// <summary>Si es administrador de la plataforma.</summary>
    /// <param name="user">Usuario de la solicitud.</param>
    /// <returns>Verdadero si tiene el rol de plataforma.</returns>
    public static bool IsPlatformAdmin(this ClaimsPrincipal user)
    {
        return user.IsInRole(DatabaseInitializer.PlatformAdminRole);
    }
}

/// <summary>
/// Agrega a la sesión los claims de la organización activa: la última que eligió el usuario si sigue siendo
/// miembro, o la primera de sus pertenencias. Se ejecuta al iniciar sesión y al cambiar de organización; un cambio
/// de rol o una baja se reflejan recién en el próximo inicio de sesión.
/// </summary>
/// <param name="userManager">Gestor de usuarios.</param>
/// <param name="roleManager">Gestor de roles.</param>
/// <param name="options">Opciones de Identity.</param>
/// <param name="organizations">Consulta de pertenencias.</param>
public sealed class OrganizationClaimsFactory(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager,
    IOptions<IdentityOptions> options,
    OrganizationService organizations)
    : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole>(userManager, roleManager, options)
{
    /// <inheritdoc />
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        ClaimsIdentity identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(OrgClaims.FullName, user.FullName));

        List<MembershipItem> memberships = await organizations.MembershipsOfUserAsync(user.Id, CancellationToken.None);
        MembershipItem? active = memberships.FirstOrDefault(m => m.OrganizationId == user.LastOrganizationId)
            ?? memberships.FirstOrDefault();
        if (active is not null)
        {
            identity.AddClaim(new Claim(OrgClaims.OrganizationId, active.OrganizationId.ToString()));
            identity.AddClaim(new Claim(OrgClaims.OrganizationName, active.Name));
            identity.AddClaim(new Claim(OrgClaims.Role, active.Role.ToString()));
        }

        return identity;
    }
}

/// <summary>Carga el <see cref="CurrentContext"/> de la solicitud desde los claims de la sesión.</summary>
/// <param name="next">Siguiente middleware.</param>
public sealed class CurrentContextMiddleware(RequestDelegate next)
{
    /// <summary>Fija organización y usuario del ámbito si hay sesión, y continúa.</summary>
    /// <param name="http">Solicitud.</param>
    /// <param name="current">Contexto del ámbito de la solicitud.</param>
    /// <returns>La tarea del resto del pipeline.</returns>
    public Task InvokeAsync(HttpContext http, CurrentContext current)
    {
        ClaimsPrincipal user = http.User;
        if (user.Identity?.IsAuthenticated == true)
        {
            current.Set(
                user.OrganizationId(),
                user.FindFirstValue(ClaimTypes.NameIdentifier),
                user.Identity.Name ?? "usuario");
        }

        return next(http);
    }
}

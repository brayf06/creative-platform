using System.Security.Claims;
using Microsoft.AspNetCore.Identity;

namespace CreativeIntelligence.Web.Services;

/// <summary>Inicio y cierre de sesión y cambio de organización activa. Vive en la Web porque maneja la cookie.</summary>
/// <param name="signIn">Gestor de sesión de Identity.</param>
/// <param name="users">Gestor de usuarios.</param>
/// <param name="organizations">Pertenencias.</param>
public sealed class AccountService(
    SignInManager<ApplicationUser> signIn, UserManager<ApplicationUser> users, OrganizationService organizations)
{
    /// <summary>Valida email y contraseña y emite la cookie de sesión con los claims de organización. Cuenta los
    /// intentos fallidos para el bloqueo de Identity.</summary>
    /// <param name="model">Credenciales.</param>
    /// <exception cref="BusinessException">403 <c>ACCOUNT_LOCKED</c> si la cuenta está bloqueada por intentos
    /// fallidos; 401 <c>INVALID_CREDENTIALS</c> si el email o la contraseña no coinciden.</exception>
    public async Task SignInAsync(LoginViewModel model)
    {
        SignInResult result = await signIn.PasswordSignInAsync(model.Email, model.Password, model.RememberMe, true);
        if (result.IsLockedOut)
        {
            throw new BusinessException(403, "La cuenta está bloqueada por intentos fallidos. Probá más tarde.", "ACCOUNT_LOCKED");
        }

        if (!result.Succeeded)
        {
            throw new BusinessException(401, "Email o contraseña incorrectos.", "INVALID_CREDENTIALS");
        }
    }

    /// <summary>Cierra la sesión.</summary>
    /// <returns>La tarea del cierre.</returns>
    public Task SignOutAsync()
    {
        return signIn.SignOutAsync();
    }

    /// <summary>Cambia la organización activa del usuario y renueva la cookie con los claims nuevos. Escribe
    /// <c>LastOrganizationId</c> del usuario.</summary>
    /// <param name="principal">Usuario de la solicitud.</param>
    /// <param name="organizationId">Organización elegida.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <exception cref="BusinessException">401 <c>NOT_SIGNED_IN</c> si no hay sesión; 403 <c>NOT_A_MEMBER</c> si no
    /// pertenece a esa organización.</exception>
    public async Task SwitchOrganizationAsync(ClaimsPrincipal principal, int organizationId, CancellationToken cancellationToken)
    {
        string? userId = users.GetUserId(principal);
        ApplicationUser? user = userId is null ? null : await users.FindByIdAsync(userId);
        if (user is null)
        {
            throw new BusinessException(401, "La sesión no es válida.", "NOT_SIGNED_IN");
        }

        await organizations.SwitchAsync(user.Id, organizationId, cancellationToken);
        await signIn.RefreshSignInAsync(user);
    }
}

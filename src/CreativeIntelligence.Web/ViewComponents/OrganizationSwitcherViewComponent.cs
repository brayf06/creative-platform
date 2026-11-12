using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace CreativeIntelligence.Web.ViewComponents;

/// <summary>Selector de organización de la barra superior: lista las organizaciones del usuario y marca la activa.</summary>
/// <param name="organizations">Consulta de pertenencias.</param>
public sealed class OrganizationSwitcherViewComponent(OrganizationService organizations) : ViewComponent
{
    /// <summary>Arma el selector para el usuario de la sesión.</summary>
    /// <returns>La vista del selector.</returns>
    public async Task<IViewComponentResult> InvokeAsync()
    {
        string userId = UserClaimsPrincipal.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        List<MembershipItem> memberships = await organizations.MembershipsOfUserAsync(userId, HttpContext.RequestAborted);
        return View(new OrganizationSwitcherViewModel(UserClaimsPrincipal.OrganizationId(), memberships));
    }
}

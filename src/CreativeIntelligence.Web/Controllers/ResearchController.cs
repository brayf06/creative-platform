using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CreativeIntelligence.Web.Controllers;

/// <summary>Investigación externa de la organización activa: fuentes autorizadas, referencias y corridas del agente.</summary>
/// <param name="research">Investigación.</param>
[Authorize(Policy = Policies.OrgMember)]
public sealed class ResearchController(ResearchService research) : Controller
{
    /// <summary>GET /Research — fuentes autorizadas y referencias guardadas.</summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Fuentes y referencias.</response>
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        return View(await research.OverviewAsync(cancellationToken));
    }

    /// <summary>POST /Research/AddSource — autoriza una fuente externa. Sólo el administrador de la organización:
    /// decide de dónde se puede tomar información.</summary>
    /// <param name="body">Nombre, dominio, condiciones y permiso por defecto.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="302">Vuelve a la pantalla, con aviso de éxito o con el error.</response>
    /// <response code="403">No es administrador de la organización.</response>
    [Authorize(Policy = Policies.OrgAdmin)]
    [HttpPost]
    public async Task<IActionResult> AddSource(ResearchSourceBody body, CancellationToken cancellationToken)
    {
        await research.AddSourceAsync(body, cancellationToken);
        TempData["Ok"] = "Fuente autorizada.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>POST /Research/SourceActive/{id} — activa o desactiva una fuente.</summary>
    /// <param name="id">Fuente.</param>
    /// <param name="active">Verdadero para activarla.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="302">Vuelve a la pantalla.</response>
    /// <response code="403">No es administrador de la organización.</response>
    [Authorize(Policy = Policies.OrgAdmin)]
    [HttpPost]
    public async Task<IActionResult> SourceActive(int id, bool active, CancellationToken cancellationToken)
    {
        await research.SetSourceActiveAsync(id, active, cancellationToken);
        return RedirectToAction(nameof(Index));
    }

    /// <summary>POST /Research/AddReference — guarda una referencia cargada a mano.</summary>
    /// <param name="body">Dirección, título, clase, resumen y brief opcional.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="302">Vuelve a la pantalla, con aviso de éxito o con el error (sitio no autorizado o ya guardada).</response>
    /// <response code="403">El rol no permite guardar referencias.</response>
    [Authorize(Policy = Policies.OrgEditor)]
    [HttpPost]
    public async Task<IActionResult> AddReference(ReferenceBody body, CancellationToken cancellationToken)
    {
        await research.AddReferenceAsync(body, cancellationToken);
        TempData["Ok"] = "Referencia guardada.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>POST /Research/Usage/{id} — cambia el permiso de una referencia.</summary>
    /// <param name="id">Referencia.</param>
    /// <param name="body">Permiso nuevo.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="302">Vuelve a la pantalla, con el error si la fuente no permite usar su material.</response>
    /// <response code="403">El rol no permite cambiar referencias.</response>
    [Authorize(Policy = Policies.OrgEditor)]
    [HttpPost]
    public async Task<IActionResult> Usage(long id, ReferenceUsageBody body, CancellationToken cancellationToken)
    {
        await research.SetUsageAsync(id, body.Usage, cancellationToken);
        return RedirectToAction(nameof(Index));
    }

    /// <summary>POST /Research/RemoveReference/{id} — quita una referencia.</summary>
    /// <param name="id">Referencia.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="302">Vuelve a la pantalla.</response>
    /// <response code="403">El rol no permite quitar referencias.</response>
    [Authorize(Policy = Policies.OrgEditor)]
    [HttpPost]
    public async Task<IActionResult> RemoveReference(long id, CancellationToken cancellationToken)
    {
        await research.RemoveReferenceAsync(id, cancellationToken);
        return RedirectToAction(nameof(Index));
    }

    /// <summary>GET /Research/ForBrief/{id} — corridas y referencias de un brief (AJAX).</summary>
    /// <param name="id">Brief.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Corridas y referencias.</response>
    [HttpGet]
    public async Task<IActionResult> ForBrief(long id, CancellationToken cancellationToken)
    {
        return Json(await research.ForBriefAsync(id, cancellationToken));
    }

    /// <summary>GET /Research/Estimate/{id} — cuánto costaría investigar para un brief, sin llamar al proveedor (AJAX).</summary>
    /// <param name="id">Brief.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Fuentes, referencias máximas, costo y tope.</response>
    /// <response code="403">El rol no permite pedir investigaciones.</response>
    /// <response code="404">El brief no existe en la organización activa.</response>
    [Authorize(Policy = Policies.OrgEditor)]
    [HttpGet]
    public async Task<IActionResult> Estimate(long id, CancellationToken cancellationToken)
    {
        return Json(await research.EstimateAsync(id, cancellationToken));
    }

    /// <summary>POST /Research/Start/{id} — pide una corrida de investigación para un brief (AJAX). Tiene costo.</summary>
    /// <param name="id">Brief.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Identificador de la corrida en cola.</response>
    /// <response code="403">El rol no permite pedir investigaciones.</response>
    /// <response code="404">El brief no existe en la organización activa.</response>
    /// <response code="409">Sin fuentes, sin clave del proveedor, estimación sobre el tope o ya hay una en curso.</response>
    [Authorize(Policy = Policies.OrgEditor)]
    [HttpPost]
    public async Task<IActionResult> Start(long id, CancellationToken cancellationToken)
    {
        return Json(new CreatedResponse(await research.RequestAsync(id, cancellationToken)));
    }
}

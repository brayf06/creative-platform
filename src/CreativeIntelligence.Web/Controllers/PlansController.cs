using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CreativeIntelligence.Web.Controllers;

/// <summary>Planes creativos de la organización activa: los conceptos propuestos para un brief y sus recetas.</summary>
/// <param name="strategy">Motor estratégico.</param>
[Authorize(Policy = Policies.OrgMember)]
public sealed class PlansController(StrategyService strategy) : Controller
{
    /// <summary>GET /Plans/ForBrief/{id} — planes de un brief (AJAX).</summary>
    /// <param name="id">Brief.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Planes del brief, del más nuevo al más viejo.</response>
    [HttpGet]
    public async Task<IActionResult> ForBrief(long id, CancellationToken cancellationToken)
    {
        return Json(await strategy.ListAsync(id, cancellationToken));
    }

    /// <summary>GET /Plans/Estimate/{id} — cuánto costaría generar el plan de un brief, sin llamar al proveedor (AJAX).</summary>
    /// <param name="id">Brief.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Conceptos, fragmentos, tokens, costo y tope.</response>
    /// <response code="403">El rol no permite pedir planes.</response>
    /// <response code="404">El brief no existe en la organización activa.</response>
    [Authorize(Policy = Policies.OrgEditor)]
    [HttpGet]
    public async Task<IActionResult> Estimate(long id, CancellationToken cancellationToken)
    {
        return Json(await strategy.EstimateAsync(id, cancellationToken));
    }

    /// <summary>POST /Plans/Start/{id} — pide generar el plan de un brief (AJAX). Tiene costo.</summary>
    /// <param name="id">Brief.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Identificador del plan en cola.</response>
    /// <response code="403">El rol no permite pedir planes.</response>
    /// <response code="404">El brief no existe en la organización activa.</response>
    /// <response code="409">Sin clave del proveedor, estimación sobre el tope, o ya hay un plan generándose.</response>
    [Authorize(Policy = Policies.OrgEditor)]
    [HttpPost]
    public async Task<IActionResult> Start(long id, CancellationToken cancellationToken)
    {
        return Json(new CreatedResponse(await strategy.RequestAsync(id, cancellationToken)));
    }

    /// <summary>GET /Plans/Detail/{id} — un plan con sus conceptos.</summary>
    /// <param name="id">Plan.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Detalle.</response>
    /// <response code="404">No existe en la organización activa.</response>
    [HttpGet]
    public async Task<IActionResult> Detail(long id, CancellationToken cancellationToken)
    {
        return View(await strategy.GetAsync(id, cancellationToken));
    }

    /// <summary>POST /Plans/Recipe/{id} — arma la receta de un concepto (AJAX).</summary>
    /// <param name="id">Concepto.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Identificador de la receta.</response>
    /// <response code="403">El rol no permite armar recetas.</response>
    /// <response code="404">El concepto no existe en la organización activa.</response>
    /// <response code="409">Ya tiene receta, no es realizable, o su material perdió la autorización.</response>
    [Authorize(Policy = Policies.OrgEditor)]
    [HttpPost]
    public async Task<IActionResult> Recipe(long id, CancellationToken cancellationToken)
    {
        return Json(new CreatedResponse(await strategy.CreateRecipeAsync(id, cancellationToken)));
    }

    /// <summary>POST /Plans/Recipes/{id} — arma las recetas de todos los conceptos realizables de un plan (AJAX).</summary>
    /// <param name="id">Plan.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Cuántas recetas se armaron.</response>
    /// <response code="403">El rol no permite armar recetas.</response>
    /// <response code="404">El plan no existe en la organización activa.</response>
    /// <response code="409">El material de algún concepto perdió la autorización.</response>
    [Authorize(Policy = Policies.OrgEditor)]
    [HttpPost]
    public async Task<IActionResult> Recipes(long id, CancellationToken cancellationToken)
    {
        return Json(new CountResponse(await strategy.CreateRecipesAsync(id, false, cancellationToken)));
    }
}

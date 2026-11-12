using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CreativeIntelligence.Web.Controllers;

/// <summary>Costos de IA de la organización activa. Sólo para su administrador.</summary>
/// <param name="usage">Uso de IA.</param>
[Authorize(Policy = Policies.OrgAdmin)]
public sealed class AiCostsController(AiUsageService usage) : Controller
{
    /// <summary>GET /AiCosts — uso y costo de IA en los últimos días, por tarea y modelo, con el gasto del mes
    /// contra el presupuesto.</summary>
    /// <param name="filter">Período en días (30 por defecto).</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Resumen, desglose y llamadas recientes.</response>
    /// <response code="400">El período está fuera de rango.</response>
    [HttpGet]
    public async Task<IActionResult> Index(AiCostFilter filter, CancellationToken cancellationToken)
    {
        return View(await usage.SummaryAsync(filter, cancellationToken));
    }
}

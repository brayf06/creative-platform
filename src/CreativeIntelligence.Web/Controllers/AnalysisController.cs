using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CreativeIntelligence.Web.Controllers;

/// <summary>Análisis con IA de las escenas de los videos de la organización activa.</summary>
/// <param name="analysis">Análisis.</param>
[Authorize(Policy = Policies.OrgMember)]
public sealed class AnalysisController(AnalysisService analysis) : Controller
{
    /// <summary>GET /Analysis/Get/{id} — análisis vigente de cada fragmento de un video, con su estado (AJAX).</summary>
    /// <param name="id">Video.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Estado y análisis por fragmento.</response>
    /// <response code="404">El archivo no existe en la organización activa.</response>
    [HttpGet]
    public async Task<IActionResult> Get(long id, CancellationToken cancellationToken)
    {
        return Json(await analysis.GetAsync(id, cancellationToken));
    }

    /// <summary>GET /Analysis/Estimate/{id} — cuánto costaría analizar el video, sin llamar al proveedor (AJAX).</summary>
    /// <param name="id">Video.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Escenas, cuadros, tokens y costo estimados.</response>
    /// <response code="403">El rol no permite pedir análisis.</response>
    /// <response code="404">El archivo no existe en la organización activa.</response>
    /// <response code="409">El video no tiene escenas detectadas o su transcripción está en curso.</response>
    [Authorize(Policy = Policies.OrgEditor)]
    [HttpGet]
    public async Task<IActionResult> Estimate(long id, CancellationToken cancellationToken)
    {
        return Json(await analysis.EstimateAsync(id, cancellationToken));
    }

    /// <summary>GET /Analysis/BatchEstimate — cuántos videos pendientes entrarían en una tanda y cuánto costaría (AJAX).</summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Estimación de la tanda.</response>
    /// <response code="403">El rol no permite pedir análisis.</response>
    [Authorize(Policy = Policies.OrgEditor)]
    [HttpGet]
    public async Task<IActionResult> BatchEstimate(CancellationToken cancellationToken)
    {
        return Json(await analysis.EstimateBatchAsync(cancellationToken));
    }

    /// <summary>POST /Analysis/BatchStart — pide el análisis de los videos pendientes que entran en la tanda (AJAX).
    /// Tiene costo.</summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Cuántos se encolaron, su costo estimado y cuántos quedaron afuera.</response>
    /// <response code="403">El rol no permite pedir análisis.</response>
    [Authorize(Policy = Policies.OrgEditor)]
    [HttpPost]
    public async Task<IActionResult> BatchStart(CancellationToken cancellationToken)
    {
        return Json(await analysis.RequestBatchAsync(cancellationToken));
    }

    /// <summary>POST /Analysis/Auto — activa o desactiva el análisis automático de videos nuevos (AJAX). Sólo el
    /// administrador de la organización: es una decisión de gasto.</summary>
    /// <param name="body">Si se activa.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Configuración guardada.</response>
    /// <response code="403">No es administrador de la organización.</response>
    [Authorize(Policy = Policies.OrgAdmin)]
    [HttpPost]
    public async Task<IActionResult> Auto(AutoAnalyzeBody body, CancellationToken cancellationToken)
    {
        await analysis.SetAutoAnalyzeAsync(body.Enabled, cancellationToken);
        return Json(new OkResponse());
    }

    /// <summary>POST /Analysis/Start/{id} — pide el análisis con IA de un video (AJAX). Tiene costo.</summary>
    /// <param name="id">Video.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Análisis encolado.</response>
    /// <response code="403">El rol no permite pedir análisis.</response>
    /// <response code="404">El archivo no existe en la organización activa.</response>
    /// <response code="409">Sin escenas, sin clave del proveedor, estimación sobre el tope, o ya hay uno en curso.</response>
    [Authorize(Policy = Policies.OrgEditor)]
    [HttpPost]
    public async Task<IActionResult> Start(long id, CancellationToken cancellationToken)
    {
        await analysis.RequestAsync(id, cancellationToken);
        return Json(new OkResponse());
    }
}

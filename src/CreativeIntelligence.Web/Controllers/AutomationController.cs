using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CreativeIntelligence.Web.Controllers;

/// <summary>Automatización publicitaria de la organización activa. Pedir una acción es de un editor; aprobarla,
/// cambiar la política y accionar el kill switch, de un administrador de la organización.</summary>
/// <param name="automation">Automatización.</param>
/// <param name="anomalies">Anomalías de las métricas.</param>
[Authorize(Policy = Policies.OrgMember)]
public sealed class AutomationController(AutomationService automation, AnomalyService anomalies) : Controller
{
    /// <summary>POST /Automation/Propose — el sistema propone las publicaciones de un plan de producción.</summary>
    /// <param name="body">Plan, conjunto de anuncios y presupuesto diario de cada publicación.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="302">Vuelve a la pantalla con lo que se propuso y lo que quedó afuera, o con el error si el
    /// lote supera su presupuesto.</response>
    /// <response code="403">El rol no permite pedir acciones.</response>
    [Authorize(Policy = Policies.OrgEditor)]
    [HttpPost]
    public async Task<IActionResult> Propose(ProposeFromPlanBody body, CancellationToken cancellationToken)
    {
        ProposalResult result = await automation.ProposeFromPlanAsync(body, cancellationToken);
        TempData["Ok"] = $"Se propusieron {result.ActionIds.Count} publicaciones."
            + (result.Skipped.Count == 0 ? string.Empty : " Quedaron afuera: " + string.Join(" ", result.Skipped));
        return RedirectToAction(nameof(Index));
    }

    /// <summary>POST /Automation/Scan — busca anomalías en las métricas importadas.</summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="302">Vuelve a la pantalla con la cantidad encontrada.</response>
    /// <response code="403">El rol no permite buscar anomalías.</response>
    [Authorize(Policy = Policies.OrgEditor)]
    [HttpPost]
    public async Task<IActionResult> Scan(CancellationToken cancellationToken)
    {
        TempData["Ok"] = $"Anomalías nuevas: {await anomalies.ScanAsync(cancellationToken)}.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>POST /Automation/ReviewAnomaly/{id} — da por revisada una anomalía.</summary>
    /// <param name="id">Anomalía.</param>
    /// <param name="body">Qué se concluyó.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="302">Vuelve a la pantalla.</response>
    /// <response code="403">Sólo un administrador de la organización revisa anomalías.</response>
    [Authorize(Policy = Policies.OrgAdmin)]
    [HttpPost]
    public async Task<IActionResult> ReviewAnomaly(long id, ReasonBody body, CancellationToken cancellationToken)
    {
        await anomalies.ReviewAsync(id, body.Reason, cancellationToken);
        return RedirectToAction(nameof(Index));
    }

    /// <summary>GET /Automation — política, kill switch, límites y acciones.</summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Pantalla de automatización.</response>
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        return View(await automation.OverviewAsync(cancellationToken));
    }

    /// <summary>POST /Automation/Policy — guarda el modo y los límites.</summary>
    /// <param name="body">Modo y límites.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="302">Vuelve a la pantalla.</response>
    /// <response code="403">Sólo un administrador de la organización cambia la política.</response>
    [Authorize(Policy = Policies.OrgAdmin)]
    [HttpPost]
    public async Task<IActionResult> Policy(AutomationPolicyBody body, CancellationToken cancellationToken)
    {
        await automation.SavePolicyAsync(body, cancellationToken);
        TempData["Ok"] = "Política guardada.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>POST /Automation/RealExecution — habilita o deshabilita la ejecución real en Meta.</summary>
    /// <param name="enabled">Verdadero para habilitarla.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="302">Vuelve a la pantalla, con el error si no hay conexión con Meta, faltan límites o el
    /// kill switch está activo.</response>
    /// <response code="403">Sólo un administrador de la organización la cambia.</response>
    [Authorize(Policy = Policies.OrgAdmin)]
    [HttpPost]
    public async Task<IActionResult> RealExecution(bool enabled, CancellationToken cancellationToken)
    {
        await automation.SetRealExecutionAsync(enabled, cancellationToken);
        return RedirectToAction(nameof(Index));
    }

    /// <summary>POST /Automation/KillSwitch — activa o desactiva el kill switch.</summary>
    /// <param name="activate">Verdadero para activarlo.</param>
    /// <param name="body">Motivo.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="302">Vuelve a la pantalla.</response>
    /// <response code="403">Sólo un administrador de la organización lo acciona.</response>
    [Authorize(Policy = Policies.OrgAdmin)]
    [HttpPost]
    public async Task<IActionResult> KillSwitch(bool activate, ReasonBody body, CancellationToken cancellationToken)
    {
        int cancelled = await automation.KillSwitchAsync(activate, body.Reason, cancellationToken);
        TempData["Ok"] = activate ? $"Kill switch activado. Se cancelaron {cancelled} acciones." : "Kill switch desactivado.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>POST /Automation/Request — pide publicar un video generado como anuncio.</summary>
    /// <param name="body">Video, conjunto, nombre, presupuesto e hipótesis.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="302">Vuelve a la pantalla, con el error si el pedido no es válido o está duplicado.</response>
    /// <response code="403">El rol no permite pedir acciones.</response>
    [Authorize(Policy = Policies.OrgEditor)]
    [HttpPost]
    [ActionName("Request")]
    public async Task<IActionResult> RequestAction(PublishRequestBody body, CancellationToken cancellationToken)
    {
        await automation.RequestAsync(body, cancellationToken);
        return RedirectToAction(nameof(Index));
    }

    /// <summary>POST /Automation/Approve/{id} — aprueba una acción que esperaba decisión.</summary>
    /// <param name="id">Acción.</param>
    /// <param name="body">Nota.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="302">Vuelve a la pantalla.</response>
    /// <response code="403">Sólo un administrador de la organización aprueba.</response>
    [Authorize(Policy = Policies.OrgAdmin)]
    [HttpPost]
    public async Task<IActionResult> Approve(long id, DecisionBody body, CancellationToken cancellationToken)
    {
        await automation.ApproveAsync(id, body.Note, cancellationToken);
        return RedirectToAction(nameof(Index));
    }

    /// <summary>POST /Automation/Reject/{id} — rechaza una acción que esperaba decisión.</summary>
    /// <param name="id">Acción.</param>
    /// <param name="body">Nota.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="302">Vuelve a la pantalla.</response>
    /// <response code="403">Sólo un administrador de la organización rechaza.</response>
    [Authorize(Policy = Policies.OrgAdmin)]
    [HttpPost]
    public async Task<IActionResult> Reject(long id, DecisionBody body, CancellationToken cancellationToken)
    {
        await automation.RejectAsync(id, body.Note, cancellationToken);
        return RedirectToAction(nameof(Index));
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CreativeIntelligence.Web.Controllers;

/// <summary>Experimentos controlados de la organización activa.</summary>
/// <param name="experiments">Experimentos.</param>
[Authorize(Policy = Policies.OrgMember)]
public sealed class ExperimentsController(ExperimentService experiments) : Controller
{
    /// <summary>GET /Experiments — experimentos de la organización.</summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Listado.</response>
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        return View(await experiments.ListAsync(cancellationToken));
    }

    /// <summary>GET /Experiments/Create — formulario de un experimento nuevo, con los anuncios para elegir. Con
    /// <c>hypothesisId</c> propone el enunciado y la variable de esa hipótesis.</summary>
    /// <param name="hypothesisId">Hipótesis que se va a probar; opcional.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Formulario.</response>
    /// <response code="403">El rol no permite crear experimentos.</response>
    /// <response code="404">La hipótesis no existe en la organización activa.</response>
    [Authorize(Policy = Policies.OrgEditor)]
    [HttpGet]
    public async Task<IActionResult> Create(long? hypothesisId, CancellationToken cancellationToken)
    {
        return View(await experiments.FormAsync(hypothesisId, cancellationToken));
    }

    /// <summary>POST /Experiments/Create — crea un experimento y abre su detalle.</summary>
    /// <param name="body">Hipótesis, variable, métrica, período, mínimo y brazos.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="302">Al detalle del experimento, o de vuelta al formulario con el error.</response>
    /// <response code="403">El rol no permite crear experimentos.</response>
    [Authorize(Policy = Policies.OrgEditor)]
    [HttpPost]
    public async Task<IActionResult> Create(CreateExperimentBody body, CancellationToken cancellationToken)
    {
        return RedirectToAction(nameof(Detail), new RouteId(await experiments.CreateAsync(body, cancellationToken)));
    }

    /// <summary>GET /Experiments/Detail/{id} — un experimento con su resultado.</summary>
    /// <param name="id">Experimento.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Detalle.</response>
    /// <response code="404">No existe en la organización activa.</response>
    [HttpGet]
    public async Task<IActionResult> Detail(long id, CancellationToken cancellationToken)
    {
        return View(await experiments.GetAsync(id, cancellationToken));
    }

    /// <summary>POST /Experiments/Conclude/{id} — cierra un experimento con su conclusión.</summary>
    /// <param name="id">Experimento.</param>
    /// <param name="body">Conclusión.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="302">Vuelve al detalle.</response>
    /// <response code="403">El rol no permite cerrar experimentos.</response>
    [Authorize(Policy = Policies.OrgEditor)]
    [HttpPost]
    public async Task<IActionResult> Conclude(long id, ConcludeExperimentBody body, CancellationToken cancellationToken)
    {
        await experiments.ConcludeAsync(id, body.Conclusion, cancellationToken);
        TempData["Ok"] = "Experimento cerrado.";
        return RedirectToAction(nameof(Detail), new RouteId(id));
    }
}

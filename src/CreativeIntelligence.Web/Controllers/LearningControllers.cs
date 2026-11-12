using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CreativeIntelligence.Web.Controllers;

/// <summary>Hipótesis de la organización activa.</summary>
/// <param name="hypotheses">Hipótesis.</param>
[Authorize(Policy = Policies.OrgMember)]
public sealed class HypothesesController(HypothesisService hypotheses) : Controller
{
    /// <summary>GET /Hypotheses — hipótesis de la organización con su estado.</summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Listado.</response>
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        return View(await hypotheses.ListAsync(cancellationToken));
    }

    /// <summary>POST /Hypotheses/Create — guarda una hipótesis escrita a mano y abre su detalle.</summary>
    /// <param name="body">Enunciado y sobre qué trata.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="302">Al detalle, o de vuelta al listado con el error.</response>
    /// <response code="403">El rol no permite crear hipótesis.</response>
    [Authorize(Policy = Policies.OrgEditor)]
    [HttpPost]
    public async Task<IActionResult> Create(CreateHypothesisBody body, CancellationToken cancellationToken)
    {
        return RedirectToAction(nameof(Detail), new RouteId(await hypotheses.CreateAsync(body, cancellationToken)));
    }

    /// <summary>POST /Hypotheses/FromPattern — guarda como hipótesis un patrón del análisis por componente.</summary>
    /// <param name="body">Qué grupo, de qué análisis.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="302">Al detalle de la hipótesis (la nueva o la que ya había), o de vuelta con el error si el
    /// patrón no tiene evidencia.</response>
    /// <response code="403">El rol no permite crear hipótesis.</response>
    [Authorize(Policy = Policies.OrgEditor)]
    [HttpPost]
    public async Task<IActionResult> FromPattern(PatternBody body, CancellationToken cancellationToken)
    {
        return RedirectToAction(nameof(Detail), new RouteId(await hypotheses.FromPatternAsync(body, cancellationToken)));
    }

    /// <summary>GET /Hypotheses/Detail/{id} — una hipótesis con su patrón, sus experimentos y sus aprendizajes.</summary>
    /// <param name="id">Hipótesis.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Detalle.</response>
    /// <response code="404">No existe en la organización activa.</response>
    [HttpGet]
    public async Task<IActionResult> Detail(long id, CancellationToken cancellationToken)
    {
        return View(await hypotheses.GetAsync(id, cancellationToken));
    }

    /// <summary>POST /Hypotheses/Assess/{id} — guarda lo que una persona valora de la hipótesis para priorizarla.</summary>
    /// <param name="id">Hipótesis.</param>
    /// <param name="body">Valoraciones de público, costo, riesgo y margen.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="302">Vuelve al detalle.</response>
    /// <response code="403">El rol no permite valorar hipótesis.</response>
    [Authorize(Policy = Policies.OrgEditor)]
    [HttpPost]
    public async Task<IActionResult> Assess(long id, AssessHypothesisBody body, CancellationToken cancellationToken)
    {
        await hypotheses.AssessAsync(id, body, cancellationToken);
        TempData["Ok"] = "Valoración guardada.";
        return RedirectToAction(nameof(Detail), new RouteId(id));
    }

    /// <summary>POST /Hypotheses/Discard/{id} — descarta una hipótesis.</summary>
    /// <param name="id">Hipótesis.</param>
    /// <param name="body">Motivo.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="302">Vuelve al detalle.</response>
    /// <response code="403">El rol no permite descartar hipótesis.</response>
    [Authorize(Policy = Policies.OrgEditor)]
    [HttpPost]
    public async Task<IActionResult> Discard(long id, ReasonBody body, CancellationToken cancellationToken)
    {
        await hypotheses.DiscardAsync(id, body.Reason, cancellationToken);
        return RedirectToAction(nameof(Detail), new RouteId(id));
    }
}

/// <summary>Memoria de aprendizaje de la organización activa.</summary>
/// <param name="learnings">Aprendizajes.</param>
[Authorize(Policy = Policies.OrgMember)]
public sealed class LearningsController(LearningService learnings) : Controller
{
    /// <summary>GET /Learnings — aprendizajes de la organización.</summary>
    /// <param name="status">Sólo los de este estado; vacío para todos.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Listado.</response>
    [HttpGet]
    public async Task<IActionResult> Index(LearningStatus? status, CancellationToken cancellationToken)
    {
        return View(new LearningIndexViewModel(status, await learnings.ListAsync(status, cancellationToken)));
    }

    /// <summary>GET /Learnings/Create — muestra lo que el sistema calcula de un experimento o de un patrón, para
    /// registrar el aprendizaje.</summary>
    /// <param name="experimentId">Experimento que lo sustenta.</param>
    /// <param name="hypothesisId">Hipótesis surgida de un patrón, si no hay experimento.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Formulario.</response>
    /// <response code="302">De vuelta, con el error, si los datos no alcanzan.</response>
    /// <response code="403">El rol no permite registrar aprendizajes.</response>
    [Authorize(Policy = Policies.OrgEditor)]
    [HttpGet]
    public async Task<IActionResult> Create(long? experimentId, long? hypothesisId, CancellationToken cancellationToken)
    {
        return View(await learnings.DraftAsync(experimentId, hypothesisId, cancellationToken));
    }

    /// <summary>POST /Learnings/Create — registra un aprendizaje y abre su detalle.</summary>
    /// <param name="body">Fuente y lo que escribe la persona.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="302">Al detalle, o de vuelta con el error.</response>
    /// <response code="403">El rol no permite registrar aprendizajes.</response>
    [Authorize(Policy = Policies.OrgEditor)]
    [HttpPost]
    public async Task<IActionResult> Create(CreateLearningBody body, CancellationToken cancellationToken)
    {
        return RedirectToAction(nameof(Detail), new RouteId(await learnings.CreateAsync(body, cancellationToken)));
    }

    /// <summary>GET /Learnings/Detail/{id} — un aprendizaje con su evidencia y lo que dicen hoy sus datos.</summary>
    /// <param name="id">Aprendizaje.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Detalle.</response>
    /// <response code="404">No existe en la organización activa.</response>
    [HttpGet]
    public async Task<IActionResult> Detail(long id, CancellationToken cancellationToken)
    {
        return View(await learnings.GetAsync(id, cancellationToken));
    }

    /// <summary>POST /Learnings/Correct/{id} — corrige un aprendizaje con una versión nueva y la abre.</summary>
    /// <param name="id">Aprendizaje a corregir.</param>
    /// <param name="body">Texto nuevo y motivo.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="302">Al detalle de la versión nueva, o de vuelta con el error.</response>
    /// <response code="403">El rol no permite corregir aprendizajes.</response>
    [Authorize(Policy = Policies.OrgEditor)]
    [HttpPost]
    public async Task<IActionResult> Correct(long id, CorrectLearningBody body, CancellationToken cancellationToken)
    {
        return RedirectToAction(nameof(Detail), new RouteId(await learnings.CorrectAsync(id, body, cancellationToken)));
    }

    /// <summary>POST /Learnings/Invalidate/{id} — invalida un aprendizaje.</summary>
    /// <param name="id">Aprendizaje.</param>
    /// <param name="body">Motivo.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="302">Vuelve al detalle.</response>
    /// <response code="403">El rol no permite invalidar aprendizajes.</response>
    [Authorize(Policy = Policies.OrgEditor)]
    [HttpPost]
    public async Task<IActionResult> Invalidate(long id, ReasonBody body, CancellationToken cancellationToken)
    {
        await learnings.InvalidateAsync(id, body.Reason, cancellationToken);
        return RedirectToAction(nameof(Detail), new RouteId(id));
    }
}

/// <summary>Priorización de las hipótesis de la organización activa.</summary>
/// <param name="priorities">Motor de priorización.</param>
[Authorize(Policy = Policies.OrgMember)]
public sealed class PrioritiesController(PriorityService priorities) : Controller
{
    /// <summary>GET /Priorities — hipótesis en pie ordenadas por prioridad, con el desglose de cada puntaje.</summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Tablero.</response>
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        return View(await priorities.BoardAsync(cancellationToken));
    }

    /// <summary>GET /Priorities/Weights — ponderaciones vigentes y su historia.</summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Ponderaciones.</response>
    [HttpGet]
    public async Task<IActionResult> Weights(CancellationToken cancellationToken)
    {
        return View(await priorities.WeightsAsync(cancellationToken));
    }

    /// <summary>POST /Priorities/Weights — guarda ponderaciones nuevas como una versión más.</summary>
    /// <param name="body">Pesos y motivo.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="302">Vuelve a las ponderaciones, con el error si todos los pesos son cero.</response>
    /// <response code="403">Sólo un administrador de la organización cambia las ponderaciones.</response>
    [Authorize(Policy = Policies.OrgAdmin)]
    [HttpPost]
    public async Task<IActionResult> Weights(PriorityWeightsBody body, CancellationToken cancellationToken)
    {
        TempData["Ok"] = $"Ponderaciones guardadas como versión {await priorities.SaveWeightsAsync(body, cancellationToken)}.";
        return RedirectToAction(nameof(Weights));
    }
}

/// <summary>Planes de producción de la organización activa.</summary>
/// <param name="plans">Planes de producción.</param>
[Authorize(Policy = Policies.OrgMember)]
public sealed class ProductionPlansController(ProductionPlanService plans) : Controller
{
    /// <summary>GET /ProductionPlans — planes de la organización.</summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Listado.</response>
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        return View(await plans.ListAsync(cancellationToken));
    }

    /// <summary>POST /ProductionPlans/Create — arma un plan con las hipótesis de mayor prioridad y lo abre.</summary>
    /// <param name="body">Nombre y cantidad de hipótesis.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="302">Al plan, o de vuelta al listado con el error si no hay hipótesis con puntaje.</response>
    /// <response code="403">El rol no permite armar planes.</response>
    [Authorize(Policy = Policies.OrgEditor)]
    [HttpPost]
    public async Task<IActionResult> Create(CreateProductionPlanBody body, CancellationToken cancellationToken)
    {
        return RedirectToAction(nameof(Detail), new RouteId(await plans.CreateAsync(body, cancellationToken)));
    }

    /// <summary>GET /ProductionPlans/Detail/{id} — un plan con la explicación de cada elección.</summary>
    /// <param name="id">Plan.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Detalle.</response>
    /// <response code="404">No existe en la organización activa.</response>
    [HttpGet]
    public async Task<IActionResult> Detail(long id, CancellationToken cancellationToken)
    {
        return View(await plans.GetAsync(id, cancellationToken));
    }
}

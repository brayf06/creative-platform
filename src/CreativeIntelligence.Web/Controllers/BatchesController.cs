using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CreativeIntelligence.Web.Controllers;

/// <summary>Lotes de creatividades de la organización activa.</summary>
/// <param name="batches">Lotes.</param>
/// <param name="composition">Composición, para los fragmentos disponibles.</param>
/// <param name="music">Música, para el formulario.</param>
[Authorize(Policy = Policies.OrgMember)]
public sealed class BatchesController(BatchService batches, CompositionService composition, MusicService music) : Controller
{
    /// <summary>GET /Batches — lotes de la organización con su avance.</summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Listado.</response>
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        return View(await batches.ListAsync(cancellationToken));
    }

    /// <summary>GET /Batches/Create — formulario de un lote nuevo, con los fragmentos disponibles.</summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Formulario.</response>
    /// <response code="403">El rol no permite crear lotes.</response>
    [Authorize(Policy = Policies.OrgEditor)]
    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        return View(new CompositionEditorViewModel(
            await composition.AvailableAsync(cancellationToken), await music.ListAsync(true, cancellationToken)));
    }

    /// <summary>POST /Batches/Preview — qué variantes saldrían, sin crear nada (AJAX).</summary>
    /// <param name="body">Parámetros y fragmentos.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Variantes que se generarían.</response>
    /// <response code="400">Datos inválidos.</response>
    /// <response code="403">El rol no permite crear lotes.</response>
    /// <response code="404">Un fragmento no existe en la organización activa.</response>
    /// <response code="409">Un fragmento es de un video sin autorización vigente.</response>
    [Authorize(Policy = Policies.OrgEditor)]
    [HttpPost]
    public async Task<IActionResult> Preview(CreateBatchBody body, CancellationToken cancellationToken)
    {
        return Json(await batches.PreviewAsync(body, cancellationToken));
    }

    /// <summary>POST /Batches/Create — crea el lote y encola sus renders (AJAX).</summary>
    /// <param name="body">Parámetros y fragmentos.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Identificador del lote.</response>
    /// <response code="400">Datos inválidos.</response>
    /// <response code="403">El rol no permite crear lotes.</response>
    /// <response code="404">Un fragmento no existe en la organización activa.</response>
    /// <response code="409">Sin autorización vigente, o los fragmentos no dan para cinco variantes distintas.</response>
    [Authorize(Policy = Policies.OrgEditor)]
    [HttpPost]
    public async Task<IActionResult> Create(CreateBatchBody body, CancellationToken cancellationToken)
    {
        return Json(new CreatedResponse(await batches.CreateAsync(body, cancellationToken)));
    }

    /// <summary>POST /Batches/Cancel/{id} — cancela los renders pendientes del lote (AJAX).</summary>
    /// <param name="id">Lote.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Cuántos videos se cancelaron.</response>
    /// <response code="403">El rol no permite cancelar.</response>
    /// <response code="404">No existe en la organización activa.</response>
    /// <response code="409">Un render terminó mientras se cancelaba.</response>
    [Authorize(Policy = Policies.OrgEditor)]
    [HttpPost]
    public async Task<IActionResult> Cancel(long id, CancellationToken cancellationToken)
    {
        return Json(new CountResponse(await batches.CancelAsync(id, cancellationToken)));
    }

    /// <summary>POST /Batches/Retry/{id} — vuelve a pedir los videos fallidos o cancelados del lote (AJAX).</summary>
    /// <param name="id">Lote.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Cuántos renders se volvieron a pedir.</response>
    /// <response code="403">El rol no permite renderizar.</response>
    /// <response code="404">No existe en la organización activa.</response>
    /// <response code="409">El material de alguna variante perdió la autorización.</response>
    [Authorize(Policy = Policies.OrgEditor)]
    [HttpPost]
    public async Task<IActionResult> Retry(long id, CancellationToken cancellationToken)
    {
        return Json(new CountResponse(await batches.RetryFailedAsync(id, cancellationToken)));
    }

    /// <summary>GET /Batches/Detail/{id} — un lote con sus variantes.</summary>
    /// <param name="id">Lote.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Detalle.</response>
    /// <response code="404">No existe en la organización activa.</response>
    [HttpGet]
    public async Task<IActionResult> Detail(long id, CancellationToken cancellationToken)
    {
        return View(await batches.GetAsync(id, cancellationToken));
    }

    /// <summary>GET /Batches/Status/{id} — el lote con el estado actual de sus videos (AJAX).</summary>
    /// <param name="id">Lote.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Detalle en JSON.</response>
    /// <response code="404">No existe en la organización activa.</response>
    [HttpGet]
    public async Task<IActionResult> Status(long id, CancellationToken cancellationToken)
    {
        return Json(await batches.GetAsync(id, cancellationToken));
    }
}

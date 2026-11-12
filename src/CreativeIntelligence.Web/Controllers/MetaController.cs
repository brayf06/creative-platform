using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CreativeIntelligence.Web.Controllers;

/// <summary>Rendimiento publicitario de la organización activa: métricas de los anuncios de Meta y su linaje.</summary>
/// <param name="meta">Rendimiento.</param>
/// <param name="performance">Análisis por componente.</param>
[Authorize(Policy = Policies.OrgMember)]
public sealed class MetaController(MetaService meta, PerformanceService performance) : Controller
{
    /// <summary>GET /Meta/Analysis — análisis por componente de un período.</summary>
    /// <param name="filter">Período y campaña.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Agrupaciones con sus lecturas y señales de fatiga.</response>
    [HttpGet]
    public async Task<IActionResult> Analysis(PerformanceFilter filter, CancellationToken cancellationToken)
    {
        return View(await performance.AnalyzeAsync(filter, cancellationToken));
    }

    /// <summary>GET /Meta — importaciones y anuncios con sus métricas del período.</summary>
    /// <param name="filter">Período.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Listado.</response>
    [HttpGet]
    public async Task<IActionResult> Index(MetaFilter filter, CancellationToken cancellationToken)
    {
        return View(await meta.OverviewAsync(filter, cancellationToken));
    }

    /// <summary>POST /Meta/Upload — recibe un informe en CSV y encola su importación.</summary>
    /// <param name="form">Archivo y moneda.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="302">Vuelve al listado, con aviso de éxito o con el error (no es CSV o está vacío).</response>
    /// <response code="403">El rol no permite importar.</response>
    [Authorize(Policy = Policies.OrgEditor)]
    [HttpPost]
    [RequestSizeLimit(MetaCsvParser.MaxBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MetaCsvParser.MaxBytes)]
    public async Task<IActionResult> Upload(UploadMetaCsvForm form, CancellationToken cancellationToken)
    {
        await using Stream content = form.File!.OpenReadStream();
        await meta.UploadAsync(form.File.FileName, form.Currency, content, cancellationToken);
        TempData["Ok"] = "Informe recibido. Se está importando.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>GET /Meta/Ad/{id} — un anuncio con sus métricas por día y su linaje.</summary>
    /// <param name="id">Anuncio.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Detalle.</response>
    /// <response code="404">No existe en la organización activa.</response>
    [HttpGet]
    public async Task<IActionResult> Ad(long id, CancellationToken cancellationToken)
    {
        return View(await meta.GetAsync(id, cancellationToken));
    }

    /// <summary>POST /Meta/Link/{id} — vincula un anuncio con el video generado que se publicó en él, o quita el vínculo.</summary>
    /// <param name="id">Anuncio.</param>
    /// <param name="body">Video generado; vacío para quitar el vínculo.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="302">Vuelve al anuncio, con el error si el video no existe.</response>
    /// <response code="403">El rol no permite vincular.</response>
    [Authorize(Policy = Policies.OrgEditor)]
    [HttpPost]
    public async Task<IActionResult> Link(long id, LinkAdBody body, CancellationToken cancellationToken)
    {
        await meta.LinkAsync(id, body.VariantId, cancellationToken);
        return RedirectToAction(nameof(Ad), new RouteId(id));
    }
}

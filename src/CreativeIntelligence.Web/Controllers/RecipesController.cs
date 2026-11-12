using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CreativeIntelligence.Web.Controllers;

/// <summary>Motor de composición de la organización activa: recetas y los videos generados de ellas.</summary>
/// <param name="composition">Composición.</param>
/// <param name="music">Música, para el editor.</param>
[Authorize(Policy = Policies.OrgMember)]
public sealed class RecipesController(CompositionService composition, MusicService music) : Controller
{
    /// <summary>GET /Recipes — recetas de la organización.</summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Listado.</response>
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        return View(await composition.ListAsync(cancellationToken));
    }

    /// <summary>GET /Recipes/Videos — todos los videos generados de la organización.</summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Listado.</response>
    [HttpGet]
    public async Task<IActionResult> Videos(CancellationToken cancellationToken)
    {
        return View(await composition.VideosAsync(cancellationToken));
    }

    /// <summary>GET /Recipes/Create — editor de una receta nueva, con los fragmentos disponibles. Con
    /// <c>?from=ID</c> arranca con los datos de esa receta, para guardarla cambiada como otra.</summary>
    /// <param name="from">Receta de la que se parte; opcional.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Editor.</response>
    /// <response code="403">El rol no permite crear recetas.</response>
    /// <response code="404">La receta de la que se parte no existe en la organización activa.</response>
    [Authorize(Policy = Policies.OrgEditor)]
    [HttpGet]
    public async Task<IActionResult> Create(long? from, CancellationToken cancellationToken)
    {
        return View(new CompositionEditorViewModel(
            await composition.AvailableAsync(cancellationToken),
            await music.ListAsync(true, cancellationToken),
            from is null ? null : await composition.DraftAsync(from.Value, cancellationToken)));
    }

    /// <summary>POST /Recipes/Create — crea una receta (AJAX).</summary>
    /// <param name="body">Nombre, concepto, formato y fragmentos en orden.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Identificador de la receta.</response>
    /// <response code="400">Datos inválidos, formato desconocido o montaje demasiado largo.</response>
    /// <response code="403">El rol no permite crear recetas.</response>
    /// <response code="404">Un fragmento no existe en la organización activa.</response>
    /// <response code="409">Un fragmento es de un video sin autorización de reutilización vigente.</response>
    [Authorize(Policy = Policies.OrgEditor)]
    [HttpPost]
    public async Task<IActionResult> Create(CreateRecipeBody body, CancellationToken cancellationToken)
    {
        return Json(new CreatedResponse(await composition.CreateAsync(body, cancellationToken)));
    }

    /// <summary>GET /Recipes/Detail/{id} — una receta con sus tramos y sus videos generados.</summary>
    /// <param name="id">Receta.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Detalle.</response>
    /// <response code="404">No existe en la organización activa.</response>
    [HttpGet]
    public async Task<IActionResult> Detail(long id, CancellationToken cancellationToken)
    {
        return View(await composition.GetAsync(id, cancellationToken));
    }

    /// <summary>GET /Recipes/Status/{id} — la receta con el estado actual de sus videos (AJAX).</summary>
    /// <param name="id">Receta.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Detalle en JSON.</response>
    /// <response code="404">No existe en la organización activa.</response>
    [HttpGet]
    public async Task<IActionResult> Status(long id, CancellationToken cancellationToken)
    {
        return Json(await composition.GetAsync(id, cancellationToken));
    }

    /// <summary>POST /Recipes/Render/{id} — pide generar un video de la receta (AJAX).</summary>
    /// <param name="id">Receta.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Identificador del video en cola.</response>
    /// <response code="403">El rol no permite renderizar.</response>
    /// <response code="404">No existe en la organización activa.</response>
    /// <response code="409">Un original perdió la autorización de reutilización.</response>
    [Authorize(Policy = Policies.OrgEditor)]
    [HttpPost]
    public async Task<IActionResult> Render(long id, CancellationToken cancellationToken)
    {
        return Json(new CreatedResponse(await composition.RenderAsync(id, cancellationToken)));
    }

    /// <summary>GET /Recipes/Video/{id} — archivo de un video generado, con soporte de rangos para reproducirlo.</summary>
    /// <param name="id">Video generado.</param>
    /// <param name="download">Verdadero para descargarlo en lugar de reproducirlo.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Contenido completo.</response>
    /// <response code="206">Rango pedido por el reproductor.</response>
    /// <response code="404">No existe en la organización activa o no terminó de generarse.</response>
    [HttpGet]
    public async Task<IActionResult> Video(long id, bool download, CancellationToken cancellationToken)
    {
        MediaContent content = await composition.OpenVariantAsync(id, cancellationToken);
        return download
            ? File(content.Content, content.ContentType, content.FileName)
            : File(content.Content, content.ContentType, enableRangeProcessing: true);
    }
}

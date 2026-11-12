using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CreativeIntelligence.Web.Controllers;

/// <summary>Escenas de los videos de la organización activa.</summary>
/// <param name="scenes">Consulta y detección de escenas.</param>
/// <param name="editor">Edición manual de escenas.</param>
[Authorize(Policy = Policies.OrgMember)]
public sealed class ScenesController(SceneService scenes, SegmentEditService editor) : Controller
{
    /// <summary>GET /Scenes/List/{id} — escenas de un video en orden temporal, con el estado de la detección (AJAX).</summary>
    /// <param name="id">Video.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Estado de la detección y fragmentos.</response>
    /// <response code="404">El archivo no existe en la organización activa.</response>
    [HttpGet]
    public async Task<IActionResult> List(long id, CancellationToken cancellationToken)
    {
        return Json(await scenes.ListAsync(id, cancellationToken));
    }

    /// <summary>GET /Scenes/Thumbnail/{id} — miniatura JPEG de un fragmento.</summary>
    /// <param name="id">Fragmento.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Miniatura.</response>
    /// <response code="404">El fragmento no existe en la organización activa o no tiene miniatura.</response>
    [HttpGet]
    public async Task<IActionResult> Thumbnail(long id, CancellationToken cancellationToken)
    {
        MediaContent content = await scenes.OpenThumbnailAsync(id, cancellationToken);
        return File(content.Content, content.ContentType);
    }

    /// <summary>POST /Scenes/Detect/{id} — pide (o vuelve a pedir) la detección de escenas de un video (AJAX).</summary>
    /// <param name="id">Video.</param>
    /// <param name="body">Si se confirma descartar las correcciones manuales.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Detección encolada.</response>
    /// <response code="403">El rol no permite pedir la detección.</response>
    /// <response code="404">El archivo no existe en la organización activa.</response>
    /// <response code="409">Es una imagen, el video todavía no terminó su análisis técnico, o tiene correcciones
    /// manuales y no se confirmó descartarlas (<c>HAS_MANUAL_EDITS</c>).</response>
    [Authorize(Policy = Policies.OrgEditor)]
    [HttpPost]
    public async Task<IActionResult> Detect(long id, DetectScenesBody body, CancellationToken cancellationToken)
    {
        await scenes.RequestDetectionAsync(id, body.DiscardManualEdits, cancellationToken);
        return Json(new OkResponse());
    }

    /// <summary>POST /Scenes/MoveBoundary/{id} — mueve el corte entre un fragmento y el siguiente (AJAX).</summary>
    /// <param name="id">Fragmento cuyo fin se mueve.</param>
    /// <param name="body">Nuevo corte.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Corte movido.</response>
    /// <response code="400">El corte dejaría un fragmento de menos de 100 ms.</response>
    /// <response code="404">El fragmento no existe en la organización activa.</response>
    /// <response code="409">Es el último fragmento, u otra edición se aplicó antes.</response>
    [Authorize(Policy = Policies.OrgEditor)]
    [HttpPost]
    public async Task<IActionResult> MoveBoundary(long id, MoveBoundaryBody body, CancellationToken cancellationToken)
    {
        await editor.MoveBoundaryAsync(id, body.EndMs, cancellationToken);
        return Json(new OkResponse());
    }

    /// <summary>POST /Scenes/Split/{id} — divide un fragmento en dos (AJAX).</summary>
    /// <param name="id">Fragmento.</param>
    /// <param name="body">Punto de división.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Fragmento dividido.</response>
    /// <response code="400">Alguna de las partes tendría menos de 100 ms.</response>
    /// <response code="404">El fragmento no existe en la organización activa.</response>
    /// <response code="409">Otra edición se aplicó antes.</response>
    [Authorize(Policy = Policies.OrgEditor)]
    [HttpPost]
    public async Task<IActionResult> Split(long id, SplitSegmentBody body, CancellationToken cancellationToken)
    {
        await editor.SplitAsync(id, body.AtMs, cancellationToken);
        return Json(new OkResponse());
    }

    /// <summary>POST /Scenes/Merge/{id} — fusiona un fragmento con el siguiente (AJAX).</summary>
    /// <param name="id">Fragmento que absorbe al siguiente.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Fragmentos fusionados.</response>
    /// <response code="404">El fragmento no existe en la organización activa.</response>
    /// <response code="409">Es el último fragmento, u otra edición se aplicó antes.</response>
    [Authorize(Policy = Policies.OrgEditor)]
    [HttpPost]
    public async Task<IActionResult> Merge(long id, CancellationToken cancellationToken)
    {
        await editor.MergeWithNextAsync(id, cancellationToken);
        return Json(new OkResponse());
    }

    /// <summary>POST /Scenes/Tags/{id} — reemplaza las etiquetas de un fragmento (AJAX).</summary>
    /// <param name="id">Fragmento.</param>
    /// <param name="body">Etiquetas separadas por coma.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Etiquetas guardadas.</response>
    /// <response code="400">Más de 20 etiquetas o alguna de más de 50 caracteres.</response>
    /// <response code="404">El fragmento no existe en la organización activa.</response>
    [Authorize(Policy = Policies.OrgEditor)]
    [HttpPost]
    public async Task<IActionResult> Tags(long id, SegmentTagsBody body, CancellationToken cancellationToken)
    {
        await editor.SetTagsAsync(id, body.Tags, cancellationToken);
        return Json(new OkResponse());
    }

    /// <summary>POST /Scenes/Select/{id} — elige o quita un fragmento para producción (AJAX).</summary>
    /// <param name="id">Fragmento.</param>
    /// <param name="body">Si se elige o se quita.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Selección guardada.</response>
    /// <response code="404">El fragmento no existe en la organización activa.</response>
    /// <response code="409">El video no tiene autorización de reutilización vigente.</response>
    [Authorize(Policy = Policies.OrgEditor)]
    [HttpPost]
    public async Task<IActionResult> Select(long id, SelectSegmentBody body, CancellationToken cancellationToken)
    {
        await editor.SetSelectedAsync(id, body.Selected, cancellationToken);
        return Json(new OkResponse());
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CreativeIntelligence.Web.Controllers;

/// <summary>Biblioteca multimedia de la organización activa.</summary>
/// <param name="media">Biblioteca.</param>
[Authorize(Policy = Policies.OrgMember)]
public sealed class MediaController(MediaLibraryService media) : Controller
{
    /// <summary>GET /Media — listado paginado de archivos, con filtros por tipo, autorización y nombre.</summary>
    /// <param name="filter">Filtros opcionales.</param>
    /// <param name="paging">Paginación.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Listado con el formulario de carga.</response>
    [HttpGet]
    public async Task<IActionResult> Index(MediaFilter filter, PageRequest paging, CancellationToken cancellationToken)
    {
        return View(new MediaIndexViewModel(await media.ListAsync(filter, paging, cancellationToken), filter));
    }

    /// <summary>POST /Media/Upload — ingresa un archivo a la biblioteca (AJAX, multipart). La carga masiva llama
    /// a esta acción una vez por archivo.</summary>
    /// <param name="form">Archivo y sus datos de derechos.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Archivo creado, o identificado como duplicado exacto de uno existente.</response>
    /// <response code="400">Falta el archivo, está vacío o los datos de derechos no son válidos.</response>
    /// <response code="403">El rol no permite cargar archivos.</response>
    /// <response code="415">Extensión no admitida.</response>
    [Authorize(Policy = Policies.OrgEditor)]
    [HttpPost]
    [RequestSizeLimit(MediaRules.MaxUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MediaRules.MaxUploadBytes)]
    public async Task<IActionResult> Upload(UploadMediaForm form, CancellationToken cancellationToken)
    {
        await using Stream content = form.File!.OpenReadStream();
        return Json(await media.IngestAsync(form.File.FileName, form, content, cancellationToken));
    }

    /// <summary>GET /Media/Detail/{id} — detalle de un archivo, con reproductor y datos de derechos.</summary>
    /// <param name="id">Archivo.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Detalle.</response>
    /// <response code="404">No existe en la organización activa.</response>
    [HttpGet]
    public async Task<IActionResult> Detail(long id, CancellationToken cancellationToken)
    {
        return View(await media.GetAsync(id, cancellationToken));
    }

    /// <summary>POST /Media/Rights/{id} — cambia titular, licencia, autorización y vigencia de un archivo.</summary>
    /// <param name="id">Archivo.</param>
    /// <param name="body">Datos de derechos nuevos.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="302">Vuelve al detalle, con aviso de éxito o con el error.</response>
    [Authorize(Policy = Policies.OrgEditor)]
    [HttpPost]
    public async Task<IActionResult> Rights(long id, MediaRightsBody body, CancellationToken cancellationToken)
    {
        await media.UpdateRightsAsync(id, body, cancellationToken);
        TempData["Ok"] = "Derechos actualizados.";
        return LocalRedirect($"/Media/Detail/{id}");
    }

    /// <summary>POST /Media/Reprocess/{id} — vuelve a encolar el análisis técnico de un archivo.</summary>
    /// <param name="id">Archivo.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="302">Vuelve al detalle, con aviso de éxito o con el error.</response>
    [Authorize(Policy = Policies.OrgEditor)]
    [HttpPost]
    public async Task<IActionResult> Reprocess(long id, CancellationToken cancellationToken)
    {
        await media.ReprocessAsync(id, cancellationToken);
        TempData["Ok"] = "Análisis encolado.";
        return LocalRedirect($"/Media/Detail/{id}");
    }

    /// <summary>GET /Media/Thumbnail/{id} — miniatura JPEG del archivo.</summary>
    /// <param name="id">Archivo.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Miniatura.</response>
    /// <response code="404">El archivo no existe en la organización activa o todavía no tiene miniatura.</response>
    [HttpGet]
    public async Task<IActionResult> Thumbnail(long id, CancellationToken cancellationToken)
    {
        MediaContent content = await media.OpenThumbnailAsync(id, cancellationToken);
        return File(content.Content, content.ContentType);
    }

    /// <summary>GET /Media/Original/{id} — sirve el archivo original, con soporte de rangos para reproducir video.</summary>
    /// <param name="id">Archivo.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Contenido completo.</response>
    /// <response code="206">Rango pedido por el reproductor.</response>
    /// <response code="404">No existe en la organización activa.</response>
    [HttpGet]
    public async Task<IActionResult> Original(long id, CancellationToken cancellationToken)
    {
        MediaContent content = await media.OpenOriginalAsync(id, cancellationToken);
        return File(content.Content, content.ContentType, enableRangeProcessing: true);
    }
}

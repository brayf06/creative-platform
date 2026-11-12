using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CreativeIntelligence.Web.Controllers;

/// <summary>Música de la organización activa, para usar de fondo en las creatividades.</summary>
/// <param name="music">Música.</param>
[Authorize(Policy = Policies.OrgMember)]
public sealed class MusicController(MusicService music) : Controller
{
    /// <summary>GET /Music — pistas de la organización con sus derechos.</summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Listado con el formulario de carga.</response>
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        return View(await music.ListAsync(false, cancellationToken));
    }

    /// <summary>POST /Music/Upload — carga una pista con sus derechos y encola su análisis.</summary>
    /// <param name="form">Archivo, nombre y derechos.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="302">Vuelve al listado, con aviso de éxito o con el error (formato no admitido, vacío o ya
    /// cargado).</response>
    /// <response code="403">El rol no permite cargar música.</response>
    [Authorize(Policy = Policies.OrgEditor)]
    [HttpPost]
    [RequestSizeLimit(MusicService.MaxUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MusicService.MaxUploadBytes)]
    public async Task<IActionResult> Upload(UploadMusicForm form, CancellationToken cancellationToken)
    {
        await using Stream content = form.File!.OpenReadStream();
        await music.UploadAsync(form.File.FileName, form.Name, form, content, cancellationToken);
        TempData["Ok"] = "Pista cargada. Se está analizando.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>POST /Music/Authorize/{id} — autoriza o desautoriza el uso de una pista.</summary>
    /// <param name="id">Pista.</param>
    /// <param name="authorized">Verdadero para autorizarla.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="302">Vuelve al listado, con aviso de éxito o con el error.</response>
    /// <response code="403">El rol no permite cambiar derechos.</response>
    [Authorize(Policy = Policies.OrgEditor)]
    [HttpPost]
    public async Task<IActionResult> Authorize(long id, bool authorized, CancellationToken cancellationToken)
    {
        await music.SetAuthorizedAsync(id, authorized, cancellationToken);
        TempData["Ok"] = authorized ? "Pista autorizada." : "Se quitó la autorización de la pista.";
        return RedirectToAction(nameof(Index));
    }
}

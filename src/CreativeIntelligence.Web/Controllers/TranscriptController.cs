using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CreativeIntelligence.Web.Controllers;

/// <summary>Transcripción de los videos de la organización activa.</summary>
/// <param name="transcripts">Transcripciones.</param>
[Authorize(Policy = Policies.OrgMember)]
public sealed class TranscriptController(TranscriptService transcripts) : Controller
{
    /// <summary>GET /Transcript/Get/{id} — transcripción de un video con su estado (AJAX).</summary>
    /// <param name="id">Video.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Estado, idioma, modelo y frases.</response>
    /// <response code="404">El archivo no existe en la organización activa.</response>
    [HttpGet]
    public async Task<IActionResult> Get(long id, CancellationToken cancellationToken)
    {
        return Json(await transcripts.GetAsync(id, cancellationToken));
    }

    /// <summary>POST /Transcript/Start/{id} — pide (o vuelve a pedir) la transcripción de un video (AJAX).</summary>
    /// <param name="id">Video.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Transcripción encolada.</response>
    /// <response code="403">El rol no permite pedirla.</response>
    /// <response code="404">El archivo no existe en la organización activa.</response>
    /// <response code="409">El archivo no tiene audio o todavía no terminó su análisis técnico.</response>
    [Authorize(Policy = Policies.OrgEditor)]
    [HttpPost]
    public async Task<IActionResult> Start(long id, CancellationToken cancellationToken)
    {
        await transcripts.RequestAsync(id, cancellationToken);
        return Json(new OkResponse());
    }
}

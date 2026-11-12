using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CreativeIntelligence.Web.Controllers;

/// <summary>Carpetas vigiladas de la organización activa. Sólo para su administrador: definen desde qué carpetas
/// del servidor entra material a la biblioteca.</summary>
/// <param name="folders">Carpetas vigiladas.</param>
[Authorize(Policy = Policies.OrgAdmin)]
public sealed class WatchedFoldersController(WatchedFolderService folders) : Controller
{
    /// <summary>GET /WatchedFolders — carpetas vigiladas y raíces permitidas.</summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Listado con el formulario de alta.</response>
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        return View(await folders.ListAsync(cancellationToken));
    }

    /// <summary>POST /WatchedFolders/Create — empieza a vigilar una carpeta y encola su primera revisión.</summary>
    /// <param name="body">Ruta y derechos que reciben los archivos que entren por ella.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="302">Vuelve al listado, con aviso de éxito o con el error (fuera de las raíces permitidas,
    /// inexistente, o ya vigilada).</response>
    [HttpPost]
    public async Task<IActionResult> Create(CreateWatchedFolderBody body, CancellationToken cancellationToken)
    {
        await folders.CreateAsync(body, cancellationToken);
        TempData["Ok"] = "Carpeta agregada. Su primera revisión ya está en la cola.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>POST /WatchedFolders/Scan/{id} — pide revisar una carpeta ahora.</summary>
    /// <param name="id">Carpeta.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="302">Vuelve al listado, con aviso de éxito o con el error.</response>
    [HttpPost]
    public async Task<IActionResult> Scan(int id, CancellationToken cancellationToken)
    {
        await folders.RequestScanAsync(id, cancellationToken);
        TempData["Ok"] = "Revisión encolada.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>POST /WatchedFolders/Active/{id} — activa o pausa la revisión periódica de una carpeta.</summary>
    /// <param name="id">Carpeta.</param>
    /// <param name="active">Verdadero para activarla.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="302">Vuelve al listado, con aviso de éxito o con el error.</response>
    [HttpPost]
    public async Task<IActionResult> Active(int id, bool active, CancellationToken cancellationToken)
    {
        await folders.SetActiveAsync(id, active, cancellationToken);
        TempData["Ok"] = active ? "Vigilancia activada." : "Vigilancia pausada.";
        return RedirectToAction(nameof(Index));
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CreativeIntelligence.Web.Controllers;

/// <summary>Perfiles de marca de la organización activa.</summary>
/// <param name="brands">Marcas.</param>
[Authorize(Policy = Policies.OrgMember)]
public sealed class BrandsController(BrandService brands) : Controller
{
    /// <summary>GET /Brands — marcas de la organización.</summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Listado con el formulario de alta.</response>
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        return View(await brands.ListAsync(cancellationToken));
    }

    /// <summary>POST /Brands/Create — crea una marca y abre su perfil.</summary>
    /// <param name="body">Datos del perfil; alcanza con el nombre.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="302">Al perfil de la marca nueva, o de vuelta al listado con el error (nombre repetido).</response>
    /// <response code="403">El rol no permite crear marcas.</response>
    [Authorize(Policy = Policies.OrgEditor)]
    [HttpPost]
    public async Task<IActionResult> Create(BrandBody body, CancellationToken cancellationToken)
    {
        return RedirectToAction(nameof(Detail), new RouteId(await brands.CreateAsync(body, cancellationToken)));
    }

    /// <summary>GET /Brands/Detail/{id} — perfil de una marca.</summary>
    /// <param name="id">Marca.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Perfil.</response>
    /// <response code="404">No existe en la organización activa.</response>
    [HttpGet]
    public async Task<IActionResult> Detail(int id, CancellationToken cancellationToken)
    {
        return View(await brands.GetAsync(id, cancellationToken));
    }

    /// <summary>POST /Brands/Update/{id} — guarda los datos generales de la marca.</summary>
    /// <param name="id">Marca.</param>
    /// <param name="body">Datos del perfil.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="302">Vuelve al perfil, con aviso de éxito o con el error.</response>
    /// <response code="403">El rol no permite cambiar marcas.</response>
    [Authorize(Policy = Policies.OrgEditor)]
    [HttpPost]
    public async Task<IActionResult> Update(int id, BrandBody body, CancellationToken cancellationToken)
    {
        await brands.UpdateAsync(id, body, cancellationToken);
        TempData["Ok"] = "Perfil guardado.";
        return RedirectToAction(nameof(Detail), new RouteId(id));
    }

    /// <summary>POST /Brands/AddItem/{id} — agrega un elemento a una lista del perfil.</summary>
    /// <param name="id">Marca.</param>
    /// <param name="body">Lista, texto, detalle y margen.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="302">Vuelve al perfil, con el error si el texto ya estaba en la lista.</response>
    /// <response code="403">El rol no permite cambiar marcas.</response>
    [Authorize(Policy = Policies.OrgEditor)]
    [HttpPost]
    public async Task<IActionResult> AddItem(int id, BrandItemBody body, CancellationToken cancellationToken)
    {
        await brands.AddItemAsync(id, body, cancellationToken);
        return RedirectToAction(nameof(Detail), new RouteId(id));
    }

    /// <summary>POST /Brands/RemoveItem/{id} — quita un elemento de una lista del perfil.</summary>
    /// <param name="id">Elemento.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="302">Vuelve al perfil, con el error si es un producto en uso.</response>
    /// <response code="403">El rol no permite cambiar marcas.</response>
    [Authorize(Policy = Policies.OrgEditor)]
    [HttpPost]
    public async Task<IActionResult> RemoveItem(int id, CancellationToken cancellationToken)
    {
        return RedirectToAction(nameof(Detail), new RouteId(await brands.RemoveItemAsync(id, cancellationToken)));
    }
}

/// <summary>Briefs de campaña de la organización activa.</summary>
/// <param name="briefs">Briefs.</param>
/// <param name="brands">Marcas, para el formulario.</param>
[Authorize(Policy = Policies.OrgMember)]
public sealed class BriefsController(BriefService briefs, BrandService brands) : Controller
{
    /// <summary>GET /Briefs — briefs de la organización.</summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Listado.</response>
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        return View(await briefs.ListAsync(cancellationToken));
    }

    /// <summary>GET /Briefs/Create — formulario de un brief nuevo.</summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Formulario.</response>
    /// <response code="403">El rol no permite crear briefs.</response>
    [Authorize(Policy = Policies.OrgEditor)]
    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        return View("Form", new BriefFormViewModel(await brands.OptionsAsync(cancellationToken), null));
    }

    /// <summary>GET /Briefs/Edit/{id} — formulario de un brief existente.</summary>
    /// <param name="id">Brief.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Formulario.</response>
    /// <response code="403">El rol no permite cambiar briefs.</response>
    /// <response code="404">No existe en la organización activa.</response>
    [Authorize(Policy = Policies.OrgEditor)]
    [HttpGet]
    public async Task<IActionResult> Edit(long id, CancellationToken cancellationToken)
    {
        return View("Form", new BriefFormViewModel(await brands.OptionsAsync(cancellationToken), await briefs.GetAsync(id, cancellationToken)));
    }

    /// <summary>POST /Briefs/Interpret — deduce campos del texto libre de un brief (AJAX). No guarda nada.</summary>
    /// <param name="body">Texto libre.</param>
    /// <response code="200">Lo que se pudo deducir.</response>
    /// <response code="403">El rol no permite crear briefs.</response>
    [Authorize(Policy = Policies.OrgEditor)]
    [HttpPost]
    public IActionResult Interpret(InterpretBriefBody body)
    {
        return Json(briefs.Interpret(body.Text));
    }

    /// <summary>POST /Briefs/Create — crea un brief (AJAX).</summary>
    /// <param name="body">Campos del brief.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Identificador del brief.</response>
    /// <response code="400">Datos inválidos o producto de otra marca.</response>
    /// <response code="403">El rol no permite crear briefs.</response>
    /// <response code="404">La marca no existe en la organización activa.</response>
    /// <response code="409">Algún texto contiene un mensaje que la marca prohíbe.</response>
    [Authorize(Policy = Policies.OrgEditor)]
    [HttpPost]
    public async Task<IActionResult> Create(BriefBody body, CancellationToken cancellationToken)
    {
        return Json(new CreatedResponse(await briefs.CreateAsync(body, cancellationToken)));
    }

    /// <summary>POST /Briefs/Update/{id} — guarda los cambios de un brief (AJAX).</summary>
    /// <param name="id">Brief.</param>
    /// <param name="body">Campos del brief.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Identificador del brief.</response>
    /// <response code="400">Datos inválidos o producto de otra marca.</response>
    /// <response code="403">El rol no permite cambiar briefs.</response>
    /// <response code="404">El brief o la marca no existen en la organización activa.</response>
    /// <response code="409">Algún texto contiene un mensaje que la marca prohíbe.</response>
    [Authorize(Policy = Policies.OrgEditor)]
    [HttpPost]
    public async Task<IActionResult> Update(long id, BriefBody body, CancellationToken cancellationToken)
    {
        await briefs.UpdateAsync(id, body, cancellationToken);
        return Json(new CreatedResponse(id));
    }

    /// <summary>GET /Briefs/Detail/{id} — un brief con todos sus campos.</summary>
    /// <param name="id">Brief.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Detalle.</response>
    /// <response code="404">No existe en la organización activa.</response>
    [HttpGet]
    public async Task<IActionResult> Detail(long id, CancellationToken cancellationToken)
    {
        return View(await briefs.GetAsync(id, cancellationToken));
    }
}

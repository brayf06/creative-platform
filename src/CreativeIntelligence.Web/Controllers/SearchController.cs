using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CreativeIntelligence.Web.Controllers;

/// <summary>Buscador de fragmentos de la organización activa.</summary>
/// <param name="search">Búsqueda.</param>
[Authorize(Policy = Policies.OrgMember)]
public sealed class SearchController(SearchService search) : Controller
{
    /// <summary>GET /Search — busca fragmentos por significado o por palabras, con filtros por función, duración,
    /// autorización y selección. Sin parámetros muestra los fragmentos más recientes.</summary>
    /// <param name="filter">Consulta y filtros.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <response code="200">Formulario y resultados.</response>
    /// <response code="400">Algún filtro está fuera de rango.</response>
    /// <response code="503">El proveedor de IA falló al procesar la consulta.</response>
    [HttpGet]
    public async Task<IActionResult> Index(SegmentSearchFilter filter, CancellationToken cancellationToken)
    {
        return View(new SearchViewModel(filter, await search.SearchAsync(filter, cancellationToken)));
    }
}

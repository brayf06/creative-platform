using System.ComponentModel.DataAnnotations;

namespace CreativeIntelligence.Web.Models.Dto;

/// <summary>Parámetros de paginación, validados.</summary>
public sealed class PageRequest
{
    /// <summary>Página, desde 1.</summary>
    [Range(1, 100000, ErrorMessage = "La página debe ser 1 o mayor.")]
    public int Page { get; set; } = 1;

    /// <summary>Filas por página, de 1 a 100.</summary>
    [Range(1, 100, ErrorMessage = "El tamaño de página debe estar entre 1 y 100.")]
    public int PageSize { get; set; } = 20;

    /// <summary>Filas a saltear en la consulta.</summary>
    public int Skip => (Page - 1) * PageSize;
}

/// <summary>Una página de resultados.</summary>
/// <param name="Items">Filas de la página.</param>
/// <param name="Page">Página devuelta, desde 1.</param>
/// <param name="PageSize">Filas por página pedidas.</param>
/// <param name="Total">Total de filas sin paginar.</param>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total)
{
    /// <summary>Cantidad de páginas; al menos 1.</summary>
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));
}

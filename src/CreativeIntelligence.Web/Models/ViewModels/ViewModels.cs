using System.ComponentModel.DataAnnotations;

namespace CreativeIntelligence.Web.Models.ViewModels;

/// <summary>Formulario de inicio de sesión.</summary>
public sealed class LoginViewModel
{
    /// <summary>Email del usuario.</summary>
    [Required(ErrorMessage = "El email es obligatorio.")]
    [EmailAddress(ErrorMessage = "El email no es válido.")]
    public string Email { get; set; } = string.Empty;

    /// <summary>Contraseña.</summary>
    [Required(ErrorMessage = "La contraseña es obligatoria.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    /// <summary>Mantener la sesión al cerrar el navegador.</summary>
    public bool RememberMe { get; set; }

    /// <summary>Ruta local a la que volver después de entrar; nula para ir al inicio.</summary>
    public string? ReturnUrl { get; set; }
}

/// <summary>Modelo de la página de error.</summary>
/// <param name="Status">Status HTTP.</param>
/// <param name="Message">Mensaje para el usuario.</param>
public sealed record ErrorViewModel(int Status, string Message);

/// <summary>Cuerpo JSON de un error de negocio en una llamada AJAX.</summary>
/// <param name="Code">Código estable, por ejemplo <c>JOB_NOT_FOUND</c>.</param>
/// <param name="Message">Mensaje para el usuario.</param>
public sealed record ErrorResponse(string Code, string Message);

/// <summary>Cuerpo JSON de una acción AJAX que no devuelve datos.</summary>
/// <param name="Ok">Siempre verdadero.</param>
public sealed record OkResponse(bool Ok = true);

/// <summary>Modelo de la pantalla de inicio.</summary>
/// <param name="Overview">Cuánto hay hecho en cada etapa del trabajo.</param>
/// <param name="Jobs">Trabajos de la organización por estado.</param>
public sealed record DashboardViewModel(DashboardOverview Overview, JobSummary Jobs);

/// <summary>Modelo del selector de organización de la barra superior.</summary>
/// <param name="CurrentId">Organización activa; 0 si no hay.</param>
/// <param name="Memberships">Organizaciones a las que pertenece el usuario.</param>
public sealed record OrganizationSwitcherViewModel(int CurrentId, IReadOnlyList<MembershipItem> Memberships);

/// <summary>Modelo del paginador.</summary>
/// <param name="Page">Página actual, desde 1.</param>
/// <param name="TotalPages">Cantidad de páginas.</param>
/// <param name="Total">Total de filas.</param>
/// <param name="Route">Parámetros de consulta a conservar al cambiar de página (filtros); nulo si no hay.</param>
public sealed record PagerViewModel(int Page, int TotalPages, int Total, Dictionary<string, string>? Route = null);

/// <summary>Formulario de carga de un archivo: el archivo más sus datos de derechos.</summary>
public sealed class UploadMediaForm : MediaRightsBody
{
    /// <summary>Archivo a ingresar.</summary>
    [Required(ErrorMessage = "Elegí un archivo.")]
    public IFormFile? File { get; set; }
}

/// <summary>Modelo de la pantalla de biblioteca.</summary>
/// <param name="Page">Página de archivos.</param>
/// <param name="Filter">Filtros aplicados, para repoblar el formulario y conservarlos al paginar.</param>
public sealed record MediaIndexViewModel(PagedResult<MediaAssetItem> Page, MediaFilter Filter)
{
    /// <summary>Filtros aplicados como parámetros de consulta, para los enlaces del paginador.</summary>
    public Dictionary<string, string> Route
    {
        get
        {
            Dictionary<string, string> route = [];
            if (Filter.Kind is not null)
            {
                route[nameof(Filter.Kind)] = Filter.Kind.Value.ToString();
            }

            if (Filter.ReuseAuthorized is not null)
            {
                route[nameof(Filter.ReuseAuthorized)] = Filter.ReuseAuthorized.Value ? "true" : "false";
            }

            if (!string.IsNullOrWhiteSpace(Filter.Search))
            {
                route[nameof(Filter.Search)] = Filter.Search;
            }

            return route;
        }
    }
}

/// <summary>Modelo del buscador.</summary>
/// <param name="Filter">Consulta y filtros aplicados, para repoblar el formulario.</param>
/// <param name="Result">Resultado de la búsqueda.</param>
public sealed record SearchViewModel(SegmentSearchFilter Filter, SearchResult Result);

/// <summary>Formato de valores para las vistas.</summary>
public static class Formatting
{
    /// <summary>Nombre para mostrar de la variable de un experimento.</summary>
    /// <param name="variable">Variable.</param>
    /// <returns>El nombre.</returns>
    public static string Variable(ExperimentVariable variable)
    {
        return variable switch
        {
            ExperimentVariable.Opening => "El fragmento de apertura",
            ExperimentVariable.Closing => "El fragmento de cierre",
            ExperimentVariable.Development => "El desarrollo",
            ExperimentVariable.Subtitles => "Los subtítulos",
            ExperimentVariable.Music => "La música",
            ExperimentVariable.Transition => "La transición",
            _ => "Otra cosa (no verificable)",
        };
    }

    /// <summary>Nombre para mostrar de lo que un plan de producción propone hacer.</summary>
    /// <param name="action">Propuesta.</param>
    /// <returns>El nombre.</returns>
    public static string PlanActionName(PlanAction action)
    {
        return action switch
        {
            PlanAction.Apply => "Producir aplicándola",
            PlanAction.Test => "Probar con un experimento",
            _ => "Explorar en chico",
        };
    }

    /// <summary>Nombre para mostrar de una valoración.</summary>
    /// <param name="level">Valoración.</param>
    /// <returns>El nombre.</returns>
    public static string Level(AssessmentLevel level)
    {
        return level switch
        {
            AssessmentLevel.High => "Alto",
            AssessmentLevel.Medium => "Medio",
            _ => "Bajo",
        };
    }

    /// <summary>Nombre para mostrar de un factor de priorización.</summary>
    /// <param name="factor">Factor.</param>
    /// <returns>El nombre.</returns>
    public static string Factor(PriorityFactor factor)
    {
        return factor switch
        {
            PriorityFactor.HistoricalPerformance => "Rendimiento histórico",
            PriorityFactor.EvidenceQuality => "Calidad de la evidencia",
            PriorityFactor.Similarity => "Similitud con lo existente",
            PriorityFactor.Novelty => "Novedad",
            PriorityFactor.AudienceRelevance => "Relevancia para el público",
            PriorityFactor.MaterialAvailability => "Disponibilidad de material",
            PriorityFactor.ProductionCost => "Costo de producción",
            PriorityFactor.AdRisk => "Riesgo publicitario",
            PriorityFactor.Fatigue => "Fatiga",
            _ => "Margen potencial",
        };
    }

    /// <summary>Nombre para mostrar de la confianza de un aprendizaje.</summary>
    /// <param name="confidence">Confianza.</param>
    /// <returns>El nombre.</returns>
    public static string Confidence(LearningConfidence confidence)
    {
        return confidence switch
        {
            LearningConfidence.High => "Alta",
            LearningConfidence.Medium => "Media",
            _ => "Baja",
        };
    }

    /// <summary>Nombre para mostrar del estado de un aprendizaje.</summary>
    /// <param name="status">Estado.</param>
    /// <returns>El nombre.</returns>
    public static string LearningStatusName(LearningStatus status)
    {
        return status switch
        {
            LearningStatus.Corrected => "Corregido",
            LearningStatus.Invalidated => "Invalidado",
            _ => "Vigente",
        };
    }

    /// <summary>Nombre para mostrar del punto en que está una hipótesis.</summary>
    /// <param name="state">Estado.</param>
    /// <returns>El nombre.</returns>
    public static string HypothesisStateName(HypothesisState state)
    {
        return state switch
        {
            HypothesisState.Testing => "En prueba",
            HypothesisState.Tested => "Probada, sin aprendizaje vigente",
            HypothesisState.Learned => "Con aprendizaje",
            HypothesisState.Discarded => "Descartada",
            _ => "Sin probar",
        };
    }

    /// <summary>Un período para mostrar; cada extremo puede faltar.</summary>
    /// <param name="from">Primer día.</param>
    /// <param name="to">Último día.</param>
    /// <returns>El texto.</returns>
    public static string Period(DateOnly? from, DateOnly? to)
    {
        return from is null && to is null
            ? "todo lo importado"
            : $"{from?.ToString("yyyy-MM-dd") ?? "el inicio"} a {to?.ToString("yyyy-MM-dd") ?? "sin fecha de fin"}";
    }

    /// <summary>Nombre para mostrar de la métrica de un experimento.</summary>
    /// <param name="metric">Métrica.</param>
    /// <returns>El nombre.</returns>
    public static string Metric(ExperimentMetric metric)
    {
        return metric switch
        {
            ExperimentMetric.HookRate => "Reproducciones de 3 s sobre impresiones",
            ExperimentMetric.PurchaseRate => "Compras sobre clics",
            _ => "CTR",
        };
    }

    /// <summary>Un número con el formato indicado, o una raya si no hay dato.</summary>
    /// <param name="value">Valor.</param>
    /// <param name="format">Formato numérico.</param>
    /// <returns>El texto a mostrar.</returns>
    public static string Number(decimal? value, string format)
    {
        return value is null ? "—" : value.Value.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Una proporción de 0 a 1 como porcentaje con dos decimales, o una raya si no hay dato.</summary>
    /// <param name="value">Proporción.</param>
    /// <returns>El texto a mostrar.</returns>
    public static string Percent(decimal? value)
    {
        return value is null ? "—" : (value.Value * 100).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " %";
    }

    /// <summary>Nombre para mostrar de una plataforma de destino.</summary>
    /// <param name="platform">Plataforma.</param>
    /// <returns>El nombre.</returns>
    public static string Platform(BriefPlatform platform)
    {
        return platform switch
        {
            BriefPlatform.MetaReels => "Reels (Meta)",
            BriefPlatform.MetaStories => "Historias (Meta)",
            BriefPlatform.MetaFeed => "Feed (Meta)",
            _ => "Otra",
        };
    }

    /// <summary>Descripción para mostrar de un nivel de autonomía.</summary>
    /// <param name="level">Nivel.</param>
    /// <returns>La descripción.</returns>
    public static string Autonomy(AutonomyLevel level)
    {
        return level switch
        {
            AutonomyLevel.Assisted => "Arma las recetas; apruebo antes de renderizar",
            AutonomyLevel.Automatic => "Arma y renderiza sin esperar aprobación (nunca publica)",
            _ => "Sólo propone conceptos",
        };
    }

    /// <summary>Duración legible: <c>m:ss</c>, con décimas si dura menos de un minuto.</summary>
    /// <param name="milliseconds">Duración en milisegundos; nulo si no aplica.</param>
    /// <returns>Texto como <c>0:07,5</c> o <c>2:31</c>; vacío si es nulo.</returns>
    public static string Duration(int? milliseconds)
    {
        if (milliseconds is null)
        {
            return string.Empty;
        }

        TimeSpan duration = TimeSpan.FromMilliseconds(milliseconds.Value);
        return duration.TotalMinutes < 1
            ? $"0:{duration.Seconds:00},{duration.Milliseconds / 100}"
            : $"{(int)duration.TotalMinutes}:{duration.Seconds:00}";
    }

    /// <summary>Tamaño legible: bytes, KB, MB o GB con un decimal.</summary>
    /// <param name="bytes">Tamaño en bytes.</param>
    /// <returns>Texto como <c>12,4 MB</c>.</returns>
    public static string Bytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} B" : $"{value:0.0} {units[unit]}";
    }
}

/// <summary>Lo que necesitan los editores de receta y de lote: los fragmentos disponibles y la música autorizada.</summary>
/// <param name="Segments">Fragmentos elegidos para producción con autorización vigente.</param>
/// <param name="Music">Pistas de música listas y con autorización vigente.</param>
/// <param name="Draft">Receta de la que se parte, si se está duplicando una; nulo para empezar de cero.</param>
public sealed record CompositionEditorViewModel(List<AvailableSegment> Segments, List<MusicTrackItem> Music, RecipeDraft? Draft = null);

/// <summary>Valor de ruta <c>id</c> para redirigir a una acción de detalle.</summary>
/// <param name="Id">Identificador.</param>
public sealed record RouteId(long Id);

/// <summary>Una de las listas del perfil de una marca, lista para dibujar.</summary>
/// <param name="BrandId">Marca.</param>
/// <param name="Kind">Qué lista es.</param>
/// <param name="Title">Título.</param>
/// <param name="Hint">Para qué sirve.</param>
/// <param name="TextLabel">Etiqueta del texto.</param>
/// <param name="DetailLabel">Etiqueta del detalle; nulo si la lista no lo usa.</param>
/// <param name="Items">Elementos.</param>
/// <param name="CanEdit">Si quien mira puede agregar y quitar.</param>
public sealed record BrandListViewModel(
    int BrandId, BrandItemKind Kind, string Title, string Hint, string TextLabel, string? DetailLabel, List<BrandItemView> Items, bool CanEdit);

/// <summary>Lo que necesita el formulario de un brief.</summary>
/// <param name="Brands">Marcas con sus productos.</param>
/// <param name="Brief">Brief que se edita; nulo si es nuevo.</param>
public sealed record BriefFormViewModel(List<BrandOption> Brands, BriefDetail? Brief);

/// <summary>Modelo del listado de aprendizajes.</summary>
/// <param name="Status">Estado por el que se filtra; nulo para todos.</param>
/// <param name="Items">Aprendizajes.</param>
public sealed record LearningIndexViewModel(LearningStatus? Status, List<LearningListItem> Items);

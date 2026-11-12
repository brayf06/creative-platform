using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CreativeIntelligence.Web.Models.Dto;

/// <summary>Una llamada a un proveedor de IA, para registrar su costo.</summary>
/// <param name="Task">Tarea para la que se usó el modelo, por ejemplo <c>scene.analysis</c>.</param>
/// <param name="Provider">Proveedor.</param>
/// <param name="Model">Identificador del modelo.</param>
/// <param name="PromptVersion">Versión del prompt.</param>
/// <param name="InputTokens">Tokens de entrada.</param>
/// <param name="OutputTokens">Tokens de salida.</param>
/// <param name="CostUsd">Costo en dólares.</param>
/// <param name="DurationMs">Duración en milisegundos.</param>
/// <param name="BackgroundJobId">Trabajo que hizo la llamada; nulo si fue interactiva.</param>
public sealed record AiUsageEntry(
    string Task,
    string Provider,
    string Model,
    string PromptVersion,
    int InputTokens,
    int OutputTokens,
    decimal CostUsd,
    int DurationMs,
    long? BackgroundJobId);

/// <summary>Período del resumen de costos.</summary>
public sealed class AiCostFilter
{
    /// <summary>Cantidad de días hacia atrás desde ahora, de 1 a 365.</summary>
    [Range(1, 365, ErrorMessage = "El período debe estar entre 1 y 365 días.")]
    public int Days { get; set; } = 30;
}

/// <summary>Uso de IA agrupado por tarea, proveedor y modelo.</summary>
/// <param name="Task">Tarea.</param>
/// <param name="Provider">Proveedor; <c>local</c> si corrió en el servidor.</param>
/// <param name="Model">Modelo.</param>
/// <param name="Calls">Cantidad de llamadas.</param>
/// <param name="InputTokens">Tokens de entrada.</param>
/// <param name="OutputTokens">Tokens de salida.</param>
/// <param name="CostUsd">Costo en dólares.</param>
/// <param name="DurationMs">Tiempo total de las llamadas en milisegundos.</param>
public sealed record AiCostGroup(
    string Task, string Provider, string Model, int Calls, long InputTokens, long OutputTokens, decimal CostUsd, long DurationMs);

/// <summary>Una llamada registrada, para el detalle.</summary>
/// <param name="Id">Identificador del registro.</param>
/// <param name="CreatedAt">Momento de la llamada, UTC.</param>
/// <param name="Task">Tarea.</param>
/// <param name="Provider">Proveedor.</param>
/// <param name="Model">Modelo.</param>
/// <param name="PromptVersion">Versión del prompt; <c>n/a</c> si no aplica.</param>
/// <param name="InputTokens">Tokens de entrada.</param>
/// <param name="OutputTokens">Tokens de salida.</param>
/// <param name="CostUsd">Costo en dólares.</param>
/// <param name="DurationMs">Duración en milisegundos.</param>
/// <param name="BackgroundJobId">Trabajo que la hizo; nulo si fue interactiva.</param>
public sealed record AiUsageItem(
    long Id,
    DateTime CreatedAt,
    string Task,
    string Provider,
    string Model,
    string PromptVersion,
    int InputTokens,
    int OutputTokens,
    decimal CostUsd,
    int DurationMs,
    long? BackgroundJobId);

/// <summary>Resumen del uso de IA de la organización.</summary>
/// <param name="Days">Días que abarca el período.</param>
/// <param name="Calls">Llamadas en el período.</param>
/// <param name="InputTokens">Tokens de entrada en el período.</param>
/// <param name="OutputTokens">Tokens de salida en el período.</param>
/// <param name="CostUsd">Costo en dólares en el período.</param>
/// <param name="LocalComputeMs">Tiempo de cómputo de los modelos que corren en el servidor (no se facturan, pero
/// ocupan CPU), en milisegundos.</param>
/// <param name="MonthCostUsd">Costo del mes calendario en curso (UTC), independiente del período elegido.</param>
/// <param name="MonthlyBudgetUsd">Presupuesto mensual configurado; 0 si no hay.</param>
/// <param name="Groups">Uso por tarea, proveedor y modelo, de mayor a menor costo.</param>
/// <param name="Recent">Las 50 llamadas más recientes del período.</param>
public sealed record AiCostSummary(
    int Days,
    int Calls,
    long InputTokens,
    long OutputTokens,
    decimal CostUsd,
    long LocalComputeMs,
    decimal MonthCostUsd,
    decimal MonthlyBudgetUsd,
    List<AiCostGroup> Groups,
    List<AiUsageItem> Recent)
{
    /// <summary>Fracción del presupuesto mensual ya gastada; nulo si no hay presupuesto.</summary>
    public decimal? BudgetUsed => MonthlyBudgetUsd > 0 ? MonthCostUsd / MonthlyBudgetUsd : null;
}

/// <summary>Proyección intermedia de un grupo de uso: EF agrega sobre propiedades, no sobre constructores.</summary>
public sealed class AiCostGroupRow
{
    /// <summary>Tarea, proveedor y modelo unidos por <c>|</c>.</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>Cantidad de llamadas.</summary>
    public int Calls { get; init; }

    /// <summary>Tokens de entrada.</summary>
    public long InputTokens { get; init; }

    /// <summary>Tokens de salida.</summary>
    public long OutputTokens { get; init; }

    /// <summary>Costo en dólares.</summary>
    public decimal CostUsd { get; init; }

    /// <summary>Tiempo total en milisegundos.</summary>
    public long DurationMs { get; init; }
}

/// <summary>Una escena tal como se le presenta al modelo.</summary>
/// <param name="Index">Número de escena, desde 1, en orden temporal.</param>
/// <param name="StartMs">Inicio en milisegundos.</param>
/// <param name="EndMs">Fin en milisegundos.</param>
/// <param name="Transcript">Lo que se dice en la escena; vacío si no hay voz.</param>
/// <param name="FramePaths">Rutas locales de los cuadros (JPEG) de la escena, en orden temporal.</param>
public sealed record SceneInput(int Index, int StartMs, int EndMs, string Transcript, List<string> FramePaths);

/// <summary>Pedido de análisis de las escenas de un video.</summary>
/// <param name="FileName">Nombre del archivo, como contexto.</param>
/// <param name="Scenes">Escenas a analizar.</param>
public sealed record VideoAnalysisRequest(string FileName, List<SceneInput> Scenes);

/// <summary>Lo que el modelo dice de una escena.</summary>
/// <param name="Index">Número de escena al que corresponde, desde 1.</param>
/// <param name="VisualDescription">Qué se ve.</param>
/// <param name="OnScreenText">Texto sobreimpreso; vacío si no hay.</param>
/// <param name="Product">Producto que aparece; vacío si no se identifica.</param>
/// <param name="Actions">Acciones observadas.</param>
/// <param name="CommercialAngle">Enfoque comercial.</param>
/// <param name="Functions">Funciones narrativas, de la taxonomía.</param>
/// <param name="MainMessage">Mensaje principal.</param>
/// <param name="Audience">Público al que parece dirigirse.</param>
/// <param name="VisualQuality">Calidad visual de 1 a 5.</param>
/// <param name="Tags">Etiquetas sugeridas.</param>
/// <param name="Confidence">Confianza del modelo, de 0 a 1.</param>
public sealed record SceneAnalysis(
    int Index,
    string VisualDescription,
    string OnScreenText,
    string Product,
    string Actions,
    string CommercialAngle,
    List<CreativeFunction> Functions,
    string MainMessage,
    string Audience,
    int VisualQuality,
    List<string> Tags,
    decimal Confidence);

/// <summary>Resultado de analizar un video, con lo que consumió.</summary>
/// <param name="Scenes">Análisis por escena.</param>
/// <param name="Provider">Proveedor que respondió.</param>
/// <param name="Model">Modelo usado.</param>
/// <param name="PromptVersion">Versión del prompt.</param>
/// <param name="InputTokens">Tokens de entrada facturados.</param>
/// <param name="OutputTokens">Tokens de salida facturados, incluido el razonamiento.</param>
/// <param name="CostUsd">Costo en dólares según las tarifas configuradas.</param>
/// <param name="DurationMs">Duración de la llamada en milisegundos.</param>
public sealed record AnalysisOutcome(
    List<SceneAnalysis> Scenes,
    string Provider,
    string Model,
    string PromptVersion,
    int InputTokens,
    int OutputTokens,
    decimal CostUsd,
    int DurationMs)
{
    /// <summary>Tramos narrativos que el modelo identificó agrupando escenas consecutivas; vacío si no informó
    /// ninguno (o si el resultado se guardó antes de que existieran).</summary>
    public List<NarrativeAnalysis> Narratives { get; init; } = [];
}

/// <summary>Un tramo narrativo según el modelo: escenas consecutivas que cumplen juntas una función.</summary>
/// <param name="FromScene">Primera escena del tramo, desde 1.</param>
/// <param name="ToScene">Última escena del tramo, inclusive.</param>
/// <param name="Function">Función que cumple el tramo.</param>
/// <param name="Summary">Qué pasa en el tramo, en una oración.</param>
public sealed record NarrativeAnalysis(int FromScene, int ToScene, CreativeFunction Function, string Summary);

/// <summary>Estimación del consumo de un análisis, antes de ejecutarlo.</summary>
/// <param name="InputTokens">Tokens de entrada estimados.</param>
/// <param name="OutputTokens">Tokens de salida estimados.</param>
/// <param name="CostUsd">Costo estimado en dólares.</param>
public sealed record AnalysisCost(int InputTokens, int OutputTokens, decimal CostUsd);

/// <summary>Para qué se pide un embedding; el proveedor lo usa para elegir el formato del texto.</summary>
public enum EmbeddingPurpose
{
    /// <summary>Texto que se va a indexar para ser encontrado.</summary>
    Document,

    /// <summary>Consulta de búsqueda.</summary>
    Query,
}

/// <summary>Resultado de pedir embeddings, con lo que consumió.</summary>
/// <param name="Vectors">Un vector por texto, en el mismo orden, normalizados a longitud 1.</param>
/// <param name="Provider">Proveedor que respondió.</param>
/// <param name="Model">Modelo usado.</param>
/// <param name="InputTokens">Tokens de entrada (estimados si el proveedor no los informa).</param>
/// <param name="CostUsd">Costo en dólares según la tarifa configurada.</param>
/// <param name="DurationMs">Duración de la llamada en milisegundos.</param>
public sealed record EmbeddingOutcome(
    List<float[]> Vectors, string Provider, string Model, int InputTokens, decimal CostUsd, int DurationMs);

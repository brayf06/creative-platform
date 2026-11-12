using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace CreativeIntelligence.Web.Services.Ai;

/// <summary>
/// Agente investigador: busca en la web referencias para un brief. Es un límite externo (un modelo de IA con
/// búsqueda): la implementación real es Gemini y los tests usan una falsa. Sólo devuelve direcciones y resúmenes
/// en texto; nunca descarga material. Quien lo llama descarta lo que no venga de una fuente autorizada.
/// </summary>
public interface IResearchAgent
{
    /// <summary>Si el proveedor tiene clave configurada.</summary>
    bool IsConfigured { get; }

    /// <summary>Modelo que usa.</summary>
    string Model { get; }

    /// <summary>Estima el costo de una corrida sin llamar al proveedor.</summary>
    /// <param name="inputCharacters">Caracteres del pedido.</param>
    /// <param name="maxReferences">Referencias pedidas como máximo.</param>
    /// <returns>Tokens y costo estimados, búsquedas incluidas.</returns>
    AnalysisCost Estimate(int inputCharacters, int maxReferences);

    /// <summary>Investiga para un brief.</summary>
    /// <param name="request">Marca, brief, dominios autorizados y cantidad máxima.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Las referencias propuestas y lo que costó.</returns>
    /// <exception cref="AiProviderException">Si el proveedor falla; indica si conviene reintentar.</exception>
    Task<ResearchOutcome> ResearchAsync(ResearchRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Consigna de la investigación externa. <b>Cambiar cualquier texto obliga a cambiar <see cref="Version"/></b>.
/// </summary>
public static class ResearchPrompt
{
    /// <summary>Versión de la consigna.</summary>
    public const string Version = "research-v1";

    /// <summary>Consultas de búsqueda que se suponen para estimar el costo.</summary>
    public const int EstimatedSearchQueries = 8;

    /// <summary>Consigna de sistema.</summary>
    public const string SystemInstruction =
        "Sos un investigador de publicidad en video. Buscás en la web referencias que ayuden a planificar una campaña: "
        + "anuncios comparables, tendencias, conceptos comerciales, hooks, estructuras narrativas, estilos, formatos y "
        + "datos de estacionalidad. Trabajás en español rioplatense. Reglas sin excepción: "
        + "(1) Sólo devolvés páginas de los sitios autorizados que se te indican; si no encontrás nada en ellos, devolvés una lista vacía. "
        + "(2) De cada referencia das su dirección exacta, un título y un resumen con tus palabras de qué enseña y por qué sirve. "
        + "No copiás textos, guiones ni frases de la página. "
        + "(3) No inventás direcciones: sólo las que encontraste al buscar. "
        + "(4) No descargás ni describís cuadro por cuadro videos ajenos: resumís la idea, la estructura y el gancho.";

    /// <summary>Arma el texto del pedido: qué se busca y en qué sitios.</summary>
    /// <param name="request">Lo que se va a investigar.</param>
    /// <returns>El texto que recibe el modelo.</returns>
    public static string BuildInput(ResearchRequest request)
    {
        BriefDetail brief = request.Brief;
        StringBuilder text = new();
        text.Append(CultureInfo.InvariantCulture, $"Marca: {request.Brand.Name}\n");
        Line(text, "Propuesta de valor", request.Brand.ValueProposition);
        Line(text, "Producto", brief.ProductName);
        Line(text, "Ocasión", brief.Occasion);
        Line(text, "Público", brief.Audience);
        Line(text, "Objetivo", brief.Objective);
        Line(text, "Mensaje", brief.Message);
        Line(text, "Enfoques", brief.Approaches);
        Line(text, "Pedido original", brief.OriginalText);
        text.Append(CultureInfo.InvariantCulture, $"Plataforma: {brief.Platform}\n\nSitios autorizados (buscá sólo en ellos):\n");
        foreach (string domain in request.Domains)
        {
            text.Append(CultureInfo.InvariantCulture, $"- {domain}\n");
        }

        text.Append(CultureInfo.InvariantCulture, $"\nDevolvé hasta {request.MaxReferences} referencias, las más útiles primero.");
        return text.ToString();
    }

    private static void Line(StringBuilder text, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            text.Append(CultureInfo.InvariantCulture, $"{label}: {value}\n");
        }
    }
}

/// <summary>Investigación externa con Gemini y su herramienta de búsqueda web, con salida JSON estructurada.</summary>
/// <param name="gemini">Protocolo de la API.</param>
/// <param name="options">Modelo y tarifas.</param>
public sealed class GeminiResearchAgent(GeminiInteractions gemini, IOptions<GeminiOptions> options) : IResearchAgent
{
    private static readonly Dictionary<string, ReferenceKind> Kinds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ad"] = ReferenceKind.Ad,
        ["trend"] = ReferenceKind.Trend,
        ["concept"] = ReferenceKind.Concept,
        ["hook"] = ReferenceKind.Hook,
        ["structure"] = ReferenceKind.Structure,
        ["style"] = ReferenceKind.Style,
        ["format"] = ReferenceKind.Format,
        ["seasonality"] = ReferenceKind.Seasonality,
    };

    private static readonly JsonNode ResponseSchema = JsonNode.Parse(
        """
        {
          "type": "object",
          "properties": {
            "references": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "url": { "type": "string" },
                  "title": { "type": "string" },
                  "kind": { "type": "string", "enum": ["ad", "trend", "concept", "hook", "structure", "style", "format", "seasonality"] },
                  "summary": { "type": "string" }
                },
                "required": ["url", "title", "kind", "summary"]
              }
            }
          },
          "required": ["references"]
        }
        """)!;

    /// <inheritdoc />
    public bool IsConfigured => gemini.IsConfigured;

    /// <inheritdoc />
    public string Model => options.Value.ResearchModel;

    /// <inheritdoc />
    public AnalysisCost Estimate(int inputCharacters, int maxReferences)
    {
        // Entrada: consigna, esquema y lo que la búsqueda le devuelve al modelo (~1500 por consulta).
        int inputTokens = 500 + (inputCharacters / 3) + (ResearchPrompt.EstimatedSearchQueries * 1500);
        // Salida: ~120 tokens por referencia más el razonamiento.
        int outputTokens = (maxReferences * 120) + 800;
        return new AnalysisCost(inputTokens, outputTokens, Cost(inputTokens, outputTokens, ResearchPrompt.EstimatedSearchQueries));
    }

    /// <inheritdoc />
    public async Task<ResearchOutcome> ResearchAsync(ResearchRequest request, CancellationToken cancellationToken)
    {
        GeminiOptions settings = options.Value;
        InteractionResult result = await gemini.SendAsync(
            settings.ResearchModel,
            ResearchPrompt.SystemInstruction,
            [new InteractionPart("text", ResearchPrompt.BuildInput(request))],
            ResponseSchema,
            settings.ThinkingLevel,
            cancellationToken,
            webSearch: true);
        ResearchOutput? output = Parse(result.Json);
        if (output?.References is null)
        {
            throw new AiProviderException("Gemini devolvió una investigación que no se pudo interpretar.", false);
        }

        return new ResearchOutcome(
            output.References
                .Where(r => r.Url is not null && r.Kind is not null && Kinds.ContainsKey(r.Kind))
                .Select(r => new ReferenceProposal(r.Url!, r.Title ?? string.Empty, Kinds[r.Kind!], r.Summary ?? string.Empty))
                .ToList(),
            result.SearchQueries,
            "gemini",
            settings.ResearchModel,
            ResearchPrompt.Version,
            result.InputTokens,
            result.OutputTokens,
            Cost(result.InputTokens, result.OutputTokens, result.SearchQueries),
            result.DurationMs);
    }

    private static ResearchOutput? Parse(string json)
    {
        try
        {
            return json.Length == 0 ? null : JsonSerializer.Deserialize<ResearchOutput>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Tokens al precio del modelo de análisis (es el mismo modelo por defecto) más las consultas de búsqueda.
    private decimal Cost(int inputTokens, int outputTokens, int searchQueries)
    {
        GeminiOptions settings = options.Value;
        return Math.Round(
            (((inputTokens * settings.AnalysisInputPricePerMillion) + (outputTokens * settings.AnalysisOutputPricePerMillion)) / 1_000_000m)
            + (searchQueries * settings.SearchPricePerThousand / 1000m),
            6);
    }

    private sealed class ResearchOutput
    {
        [JsonPropertyName("references")]
        public List<ReferenceOutput>? References { get; set; }
    }

    private sealed class ReferenceOutput
    {
        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("kind")]
        public string? Kind { get; set; }

        [JsonPropertyName("summary")]
        public string? Summary { get; set; }
    }
}

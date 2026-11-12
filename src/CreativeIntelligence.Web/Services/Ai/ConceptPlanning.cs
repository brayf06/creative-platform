using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace CreativeIntelligence.Web.Services.Ai;

/// <summary>
/// Planificador de conceptos: a partir de una marca, un brief y los fragmentos disponibles propone conceptos de
/// anuncios. Es un límite externo (un modelo de IA): la implementación real es Gemini y los tests usan una falsa.
/// </summary>
public interface IConceptPlanner
{
    /// <summary>Si el proveedor tiene clave configurada.</summary>
    bool IsConfigured { get; }

    /// <summary>Modelo que usa.</summary>
    string Model { get; }

    /// <summary>Estima el costo de un plan sin llamar al proveedor.</summary>
    /// <param name="inputCharacters">Caracteres del pedido.</param>
    /// <param name="concepts">Conceptos pedidos.</param>
    /// <returns>Tokens y costo estimados.</returns>
    AnalysisCost Estimate(int inputCharacters, int concepts);

    /// <summary>Pide un plan de conceptos.</summary>
    /// <param name="request">Marca, brief, fragmentos disponibles y cantidad.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Los conceptos propuestos y lo que costó.</returns>
    /// <exception cref="AiProviderException">Si el proveedor falla; indica si conviene reintentar.</exception>
    Task<PlanOutcome> PlanAsync(PlanRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Consigna del plan de conceptos. <b>Cambiar cualquier texto obliga a cambiar <see cref="Version"/></b>: la
/// versión queda guardada en cada plan y dice con qué consigna se generó.
/// </summary>
public static class ConceptPlanPrompt
{
    /// <summary>Versión de la consigna.</summary>
    public const string Version = "concept-plan-v3";

    /// <summary>Consigna de sistema.</summary>
    public const string SystemInstruction =
        "Sos un estratega creativo de publicidad en video para redes sociales. Trabajás en español rioplatense. "
        + "Proponés conceptos de anuncios a partir del perfil de una marca, un brief y una lista de fragmentos de video "
        + "que la marca ya tiene y puede reutilizar. Respetás estas reglas sin excepción: "
        + "(1) Los conceptos tienen que ser distintos entre sí: otra hipótesis, otro enfoque u otro hook; no variaciones de redacción. "
        + "(2) Sólo afirmás beneficios que figuran en el perfil de la marca. "
        + "(3) Nunca usás, ni con otras palabras, los mensajes que la marca prohíbe. "
        + "(4) Priorizás armar cada anuncio con los fragmentos disponibles: en 'fragments' ponés, en el orden del montaje, "
        + "los números de los fragmentos que usarías. Sólo números de la lista. "
        + "(5) Si para un concepto falta material, lo decís en 'missing' con precisión (qué toma haría falta). "
        + "Un concepto sin fragmentos y con faltantes es válido; inventar fragmentos no lo es. "
        + "(6) La hipótesis dice qué creés que va a pasar y por qué, en una frase que se pueda comprobar con resultados. "
        + "(7) 'novelty' va de 1 (muy parecido a lo que la marca ya hace) a 5 (muy distinto). "
        + "(8) Las referencias externas son inspiración de idea, estructura o gancho: nunca copiás sus textos ni proponés "
        + "usar su material. Las marcadas 'sólo referencia' no se citan ni se nombran en el anuncio. "
        + "(9) Los aprendizajes vigentes son lo que los resultados reales de la marca permitieron afirmar, cada uno con su "
        + "confianza. Los de confianza alta o media los aplicás donde correspondan según su aplicabilidad y lo decís en 'reason'. "
        + "Los de confianza baja son indicios: podés proponer un concepto que los ponga a prueba, sin darlos por ciertos. "
        + "No prometés resultados ni rendimiento: proponés hipótesis.";

    /// <summary>Arma el texto del pedido: perfil de la marca, brief, fragmentos disponibles y cantidad.</summary>
    /// <param name="request">Lo que se va a planificar.</param>
    /// <returns>El texto que recibe el modelo.</returns>
    public static string BuildInput(PlanRequest request)
    {
        BrandDetail brand = request.Brand;
        BriefDetail brief = request.Brief;
        StringBuilder text = new();
        text.Append(CultureInfo.InvariantCulture, $"# Marca: {brand.Name}\n");
        Line(text, "Propuesta de valor", brand.ValueProposition);
        Line(text, "Estilo de comunicación", brand.CommunicationStyle);
        Line(text, "Identidad visual", brand.VisualIdentity);
        Line(text, "Objetivos", brand.Objectives);
        Line(text, "Restricciones comerciales", brand.CommercialRestrictions);
        List(text, "Productos", brand.Of(BrandItemKind.Product));
        List(text, "Beneficios verificables", brand.Of(BrandItemKind.Benefit));
        List(text, "Públicos", brand.Of(BrandItemKind.Audience));
        List(text, "Objeciones", brand.Of(BrandItemKind.Objection));
        List(text, "Mensajes permitidos", brand.Of(BrandItemKind.AllowedMessage));
        List(text, "MENSAJES PROHIBIDOS (no usar ni reformular)", brand.Of(BrandItemKind.ForbiddenMessage));

        text.Append(CultureInfo.InvariantCulture, $"\n# Brief: {brief.Name}\n");
        Line(text, "Texto original", brief.OriginalText);
        Line(text, "Producto", brief.ProductName);
        Line(text, "Ocasión", brief.Occasion);
        Line(text, "Público", brief.Audience);
        Line(text, "Objetivo", brief.Objective);
        Line(text, "Mensaje principal", brief.Message);
        Line(text, "Enfoques a priorizar", brief.Approaches);
        Line(text, "Duración de cada video", brief.DurationSeconds is null ? null : $"{brief.DurationSeconds} segundos");
        Line(text, "Plataforma", brief.Platform.ToString());

        text.Append("\n# Fragmentos disponibles\n");
        if (request.Fragments.Count == 0)
        {
            text.Append("No hay fragmentos disponibles: todos los conceptos van con 'fragments' vacío y sus faltantes.\n");
        }

        foreach (PlanFragment fragment in request.Fragments)
        {
            string functions = fragment.Functions.Count == 0 ? "sin función asignada" : string.Join(", ", fragment.Functions);
            text.Append(CultureInfo.InvariantCulture,
                $"{fragment.Index}. {fragment.DurationMs / 1000.0:0.0} s · {functions} · {fragment.Description}\n");
        }

        if (request.References.Count > 0)
        {
            text.Append("\n# Referencias externas (inspiración; no copiar)\n");
            foreach (PlanReference reference in request.References)
            {
                string usage = reference.Usage == ReferenceUsage.Usable ? "utilizable" : "sólo referencia";
                text.Append(CultureInfo.InvariantCulture, $"- [{reference.Kind}, {usage}] {reference.Title}: {reference.Summary}\n");
            }
        }

        if (request.Learnings.Count > 0)
        {
            text.Append("\n# Aprendizajes vigentes (de resultados reales de la marca)\n");
            foreach (PlanLearning learning in request.Learnings)
            {
                string confidence = learning.Confidence switch
                {
                    LearningConfidence.High => "alta",
                    LearningConfidence.Medium => "media",
                    _ => "baja",
                };
                text.Append(
                    CultureInfo.InvariantCulture,
                    $"- [confianza {confidence}] {learning.Statement} Recomendación: {learning.Recommendation} Vale para: {learning.Applicability}\n");
            }
        }

        text.Append(CultureInfo.InvariantCulture, $"\nProponé exactamente {request.Count} conceptos distintos.");
        return text.ToString();
    }

    private static void Line(StringBuilder text, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            text.Append(CultureInfo.InvariantCulture, $"{label}: {value}\n");
        }
    }

    private static void List(StringBuilder text, string label, List<BrandItemView> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        text.Append(CultureInfo.InvariantCulture, $"{label}:\n");
        foreach (BrandItemView item in items)
        {
            text.Append(CultureInfo.InvariantCulture, $"- {item.Text}{(item.Detail is null ? string.Empty : $" ({item.Detail})")}\n");
        }
    }
}

/// <summary>Plan de conceptos con Gemini: una llamada de sólo texto con salida JSON estructurada.</summary>
/// <param name="gemini">Protocolo de la API.</param>
/// <param name="options">Modelo y tarifas.</param>
public sealed class GeminiConceptPlanner(GeminiInteractions gemini, IOptions<GeminiOptions> options) : IConceptPlanner
{
    private static readonly JsonNode ResponseSchema = JsonNode.Parse(
        """
        {
          "type": "object",
          "properties": {
            "concepts": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "title": { "type": "string" },
                  "hypothesis": { "type": "string" },
                  "approach": { "type": "string" },
                  "hook": { "type": "string" },
                  "script": { "type": "string" },
                  "development": { "type": "string" },
                  "cta": { "type": "string" },
                  "audience": { "type": "string" },
                  "required_material": { "type": "string" },
                  "reason": { "type": "string" },
                  "risks": { "type": "string" },
                  "novelty": { "type": "integer" },
                  "fragments": { "type": "array", "items": { "type": "integer" } },
                  "missing": { "type": "array", "items": { "type": "string" } }
                },
                "required": ["title", "hypothesis", "approach", "hook", "script", "development", "cta", "audience",
                             "required_material", "reason", "risks", "novelty", "fragments", "missing"]
              }
            }
          },
          "required": ["concepts"]
        }
        """)!;

    /// <inheritdoc />
    public bool IsConfigured => gemini.IsConfigured;

    /// <inheritdoc />
    public string Model => options.Value.PlanningModel;

    /// <inheritdoc />
    public AnalysisCost Estimate(int inputCharacters, int concepts)
    {
        // Entrada: consigna y esquema (~700) más el texto (~3 caracteres por token).
        int inputTokens = 700 + (inputCharacters / 3);
        // Salida: ~450 tokens de JSON por concepto más el razonamiento.
        int outputTokens = (concepts * 450) + 1500;
        return new AnalysisCost(inputTokens, outputTokens, Cost(inputTokens, outputTokens));
    }

    /// <inheritdoc />
    public async Task<PlanOutcome> PlanAsync(PlanRequest request, CancellationToken cancellationToken)
    {
        GeminiOptions settings = options.Value;
        InteractionResult result = await gemini.SendAsync(
            settings.PlanningModel,
            ConceptPlanPrompt.SystemInstruction,
            [new InteractionPart("text", ConceptPlanPrompt.BuildInput(request))],
            ResponseSchema,
            settings.PlanningThinkingLevel,
            cancellationToken);
        PlanOutput? output = Parse(result.Json);
        if (output?.Concepts is null || output.Concepts.Count == 0)
        {
            throw new AiProviderException("Gemini devolvió un plan que no se pudo interpretar.", false);
        }

        return new PlanOutcome(
            output.Concepts
                .Select(c => new ConceptProposal(
                    c.Title ?? string.Empty,
                    c.Hypothesis ?? string.Empty,
                    c.Approach ?? string.Empty,
                    c.Hook ?? string.Empty,
                    c.Script ?? string.Empty,
                    c.Development ?? string.Empty,
                    c.Cta ?? string.Empty,
                    c.Audience ?? string.Empty,
                    c.RequiredMaterial ?? string.Empty,
                    c.Reason ?? string.Empty,
                    c.Risks ?? string.Empty,
                    c.Novelty,
                    c.Fragments ?? [],
                    c.Missing ?? []))
                .ToList(),
            "gemini",
            settings.PlanningModel,
            ConceptPlanPrompt.Version,
            result.InputTokens,
            result.OutputTokens,
            Cost(result.InputTokens, result.OutputTokens),
            result.DurationMs);
    }

    private static PlanOutput? Parse(string json)
    {
        try
        {
            return json.Length == 0 ? null : JsonSerializer.Deserialize<PlanOutput>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private decimal Cost(int inputTokens, int outputTokens)
    {
        GeminiOptions settings = options.Value;
        return Math.Round(
            ((inputTokens * settings.PlanningInputPricePerMillion) + (outputTokens * settings.PlanningOutputPricePerMillion)) / 1_000_000m,
            6);
    }

    private sealed class PlanOutput
    {
        [JsonPropertyName("concepts")]
        public List<ConceptOutput>? Concepts { get; set; }
    }

    private sealed class ConceptOutput
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("hypothesis")]
        public string? Hypothesis { get; set; }

        [JsonPropertyName("approach")]
        public string? Approach { get; set; }

        [JsonPropertyName("hook")]
        public string? Hook { get; set; }

        [JsonPropertyName("script")]
        public string? Script { get; set; }

        [JsonPropertyName("development")]
        public string? Development { get; set; }

        [JsonPropertyName("cta")]
        public string? Cta { get; set; }

        [JsonPropertyName("audience")]
        public string? Audience { get; set; }

        [JsonPropertyName("required_material")]
        public string? RequiredMaterial { get; set; }

        [JsonPropertyName("reason")]
        public string? Reason { get; set; }

        [JsonPropertyName("risks")]
        public string? Risks { get; set; }

        [JsonPropertyName("novelty")]
        public int Novelty { get; set; }

        [JsonPropertyName("fragments")]
        public List<int>? Fragments { get; set; }

        [JsonPropertyName("missing")]
        public List<string>? Missing { get; set; }
    }
}

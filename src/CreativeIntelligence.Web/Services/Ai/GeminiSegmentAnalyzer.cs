using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace CreativeIntelligence.Web.Services.Ai;

/// <summary>Configuración de Gemini (sección <c>Ai:Gemini</c>).</summary>
public sealed class GeminiOptions
{
    /// <summary>Clave de la API. Vacía = proveedor sin configurar. No va al repositorio: se define con la variable
    /// de entorno <c>Ai__Gemini__ApiKey</c> o en un archivo de configuración fuera de git.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Dirección base de la API.</summary>
    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta/";

    /// <summary>Revisión de la API que se envía en el encabezado <c>Api-Revision</c>.</summary>
    public string ApiRevision { get; set; } = "2026-05-20";

    /// <summary>Modelo para el análisis de escenas: una tarea de clasificación, va con el modelo económico.</summary>
    public string AnalysisModel { get; set; } = "gemini-3.5-flash-lite";

    /// <summary>Nivel de razonamiento: <c>minimal</c>, <c>low</c>, <c>medium</c> o <c>high</c>. El razonamiento
    /// se cobra como salida.</summary>
    public string ThinkingLevel { get; set; } = "low";

    /// <summary>Dólares por millón de tokens de entrada del modelo de análisis. Tarifa al 2026-11-11.</summary>
    public decimal AnalysisInputPricePerMillion { get; set; } = 0.30m;

    /// <summary>Dólares por millón de tokens de salida (incluido el razonamiento). Tarifa al 2026-11-11.</summary>
    public decimal AnalysisOutputPricePerMillion { get; set; } = 2.50m;

    /// <summary>Modelo de embeddings para la búsqueda.</summary>
    public string EmbeddingModel { get; set; } = "gemini-embedding-2";

    /// <summary>Dimensiones del vector. 768 es uno de los tamaños recomendados y reduce almacenamiento y cómputo.
    /// Cambiarlo invalida el índice: hay que volver a indexar.</summary>
    public int EmbeddingDimensions { get; set; } = 768;

    /// <summary>Dólares por millón de tokens de texto del modelo de embeddings. Tarifa al 2026-11-11.</summary>
    public decimal EmbeddingPricePerMillion { get; set; } = 0.20m;

    /// <summary>Modelo para el plan de conceptos. Es una tarea de razonamiento: conviene uno más capaz que el de
    /// clasificación. Por defecto es el mismo, hasta elegir otro con la clave en la mano.</summary>
    public string PlanningModel { get; set; } = "gemini-3.5-flash-lite";

    /// <summary>Nivel de razonamiento del plan de conceptos.</summary>
    public string PlanningThinkingLevel { get; set; } = "medium";

    /// <summary>Dólares por millón de tokens de entrada del modelo de planificación.</summary>
    public decimal PlanningInputPricePerMillion { get; set; } = 0.30m;

    /// <summary>Dólares por millón de tokens de salida (incluido el razonamiento) del modelo de planificación.</summary>
    public decimal PlanningOutputPricePerMillion { get; set; } = 2.50m;

    /// <summary>Modelo para la investigación externa, que busca en la web.</summary>
    public string ResearchModel { get; set; } = "gemini-3.5-flash-lite";

    /// <summary>Dólares por cada 1000 consultas de búsqueda web. Tarifa consultada el 2026-11-11 (las primeras
    /// 5000 del mes son gratis; acá se estima sin contar esa franquicia).</summary>
    public decimal SearchPricePerThousand { get; set; } = 14m;

    /// <summary>Segundos máximos de espera por una respuesta.</summary>
    public int TimeoutSeconds { get; set; } = 180;
}

/// <summary>
/// Análisis de escenas con Gemini: arma el pedido con los cuadros y lo que se dice en cada escena, y convierte la
/// respuesta en el análisis por escena y los tramos narrativos. El protocolo lo maneja <see cref="GeminiInteractions"/>.
/// </summary>
/// <param name="gemini">Protocolo de la API.</param>
/// <param name="options">Modelo y tarifas.</param>
public sealed class GeminiSegmentAnalyzer(GeminiInteractions gemini, IOptions<GeminiOptions> options) : ISegmentAnalyzer
{
    // Tokens que el proveedor cobra por una imagen de hasta 384 px de lado.
    private const int TokensPerFrame = 258;

    private static readonly Dictionary<string, CreativeFunction> Functions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["hook"] = CreativeFunction.Hook,
        ["problem"] = CreativeFunction.Problem,
        ["presentation"] = CreativeFunction.Presentation,
        ["demonstration"] = CreativeFunction.Demonstration,
        ["benefit"] = CreativeFunction.Benefit,
        ["social_proof"] = CreativeFunction.SocialProof,
        ["objection"] = CreativeFunction.Objection,
        ["comparison"] = CreativeFunction.Comparison,
        ["offer"] = CreativeFunction.Offer,
        ["cta"] = CreativeFunction.Cta,
        ["broll"] = CreativeFunction.Broll,
        ["closing"] = CreativeFunction.Closing,
    };

    private static readonly JsonNode ResponseSchema = JsonNode.Parse(
        """
        {
          "type": "object",
          "properties": {
            "scenes": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "index": { "type": "integer" },
                  "visual_description": { "type": "string" },
                  "on_screen_text": { "type": "string" },
                  "product": { "type": "string" },
                  "actions": { "type": "string" },
                  "commercial_angle": { "type": "string" },
                  "functions": {
                    "type": "array",
                    "items": {
                      "type": "string",
                      "enum": ["hook", "problem", "presentation", "demonstration", "benefit", "social_proof",
                               "objection", "comparison", "offer", "cta", "broll", "closing"]
                    }
                  },
                  "main_message": { "type": "string" },
                  "audience": { "type": "string" },
                  "visual_quality": { "type": "integer" },
                  "tags": { "type": "array", "items": { "type": "string" } },
                  "confidence": { "type": "number" }
                },
                "required": ["index", "visual_description", "on_screen_text", "product", "actions", "commercial_angle",
                             "functions", "main_message", "audience", "visual_quality", "tags", "confidence"]
              }
            },
            "narrative_segments": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "from_scene": { "type": "integer" },
                  "to_scene": { "type": "integer" },
                  "function": {
                    "type": "string",
                    "enum": ["hook", "problem", "presentation", "demonstration", "benefit", "social_proof",
                             "objection", "comparison", "offer", "cta", "broll", "closing"]
                  },
                  "summary": { "type": "string" }
                },
                "required": ["from_scene", "to_scene", "function", "summary"]
              }
            }
          },
          "required": ["scenes", "narrative_segments"]
        }
        """)!;

    /// <inheritdoc />
    public bool IsConfigured => gemini.IsConfigured;

    /// <inheritdoc />
    public string Model => options.Value.AnalysisModel;

    /// <inheritdoc />
    public AnalysisCost Estimate(int scenes, int frames, int transcriptCharacters)
    {
        // Entrada: imágenes + consigna y esquema (~900) + encabezado por escena + texto (~3 caracteres por token).
        int inputTokens = (frames * TokensPerFrame) + 900 + (scenes * 25) + (transcriptCharacters / 3);
        // Salida: ~230 tokens de JSON por escena más el razonamiento.
        int outputTokens = (scenes * 230) + 400;
        return new AnalysisCost(inputTokens, outputTokens, Cost(inputTokens, outputTokens));
    }

    /// <inheritdoc />
    public async Task<AnalysisOutcome> AnalyzeAsync(VideoAnalysisRequest request, CancellationToken cancellationToken)
    {
        GeminiOptions settings = options.Value;
        if (!IsConfigured)
        {
            throw new AiProviderException("Falta configurar la clave de Gemini.", false);
        }

        List<InteractionPart> input = [new InteractionPart("text", SceneAnalysisPrompt.Instructions(request.FileName, request.Scenes.Count))];
        foreach (SceneInput scene in request.Scenes)
        {
            input.Add(new InteractionPart("text", SceneAnalysisPrompt.SceneHeader(scene)));
            foreach (string framePath in scene.FramePaths)
            {
                byte[] bytes = await File.ReadAllBytesAsync(framePath, cancellationToken);
                input.Add(new InteractionPart("image", null, Convert.ToBase64String(bytes), "image/jpeg"));
            }
        }

        InteractionResult result = await gemini.SendAsync(
            settings.AnalysisModel, SceneAnalysisPrompt.SystemInstruction, input, ResponseSchema, settings.ThinkingLevel, cancellationToken);
        string json = result.Json;
        int inputTokens = result.InputTokens;
        int outputTokens = result.OutputTokens;        ScenesOutput? output = ParseOutput(json);
        if (output?.Scenes is null || output.Scenes.Count == 0)
        {
            throw new AiProviderException("Gemini devolvió una respuesta que no se pudo interpretar.", false);
        }

        return new AnalysisOutcome(
            output.Scenes.Select(ToScene).ToList(),
            "gemini",
            settings.AnalysisModel,
            SceneAnalysisPrompt.Version,
            inputTokens,
            outputTokens,
            Cost(inputTokens, outputTokens),
            result.DurationMs)
        {
            // Un tramo con una función que no es de la taxonomía se descarta.
            Narratives = (output.Narratives ?? [])
                .Where(n => n.Function is not null && Functions.ContainsKey(n.Function))
                .Select(n => new NarrativeAnalysis(n.FromScene, n.ToScene, Functions[n.Function!], n.Summary ?? string.Empty))
                .ToList(),
        };
    }

    private static ScenesOutput? ParseOutput(string json)
    {
        try
        {
            return json.Length == 0 ? null : JsonSerializer.Deserialize<ScenesOutput>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static SceneAnalysis ToScene(SceneOutput scene)
    {
        return new SceneAnalysis(
            scene.Index,
            scene.VisualDescription ?? string.Empty,
            scene.OnScreenText ?? string.Empty,
            scene.Product ?? string.Empty,
            scene.Actions ?? string.Empty,
            scene.CommercialAngle ?? string.Empty,
            (scene.Functions ?? []).Where(Functions.ContainsKey).Select(f => Functions[f]).Distinct().ToList(),
            scene.MainMessage ?? string.Empty,
            scene.Audience ?? string.Empty,
            scene.VisualQuality,
            scene.Tags ?? [],
            (decimal)Math.Clamp(scene.Confidence, 0, 1));
    }

    private decimal Cost(int inputTokens, int outputTokens)
    {
        GeminiOptions settings = options.Value;
        return Math.Round(
            ((inputTokens * settings.AnalysisInputPricePerMillion) + (outputTokens * settings.AnalysisOutputPricePerMillion)) / 1_000_000m,
            6);
    }

    private sealed class ScenesOutput
    {
        [JsonPropertyName("scenes")]
        public List<SceneOutput>? Scenes { get; set; }

        [JsonPropertyName("narrative_segments")]
        public List<NarrativeOutput>? Narratives { get; set; }
    }

    private sealed class NarrativeOutput
    {
        [JsonPropertyName("from_scene")]
        public int FromScene { get; set; }

        [JsonPropertyName("to_scene")]
        public int ToScene { get; set; }

        [JsonPropertyName("function")]
        public string? Function { get; set; }

        [JsonPropertyName("summary")]
        public string? Summary { get; set; }
    }

    private sealed class SceneOutput
    {
        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("visual_description")]
        public string? VisualDescription { get; set; }

        [JsonPropertyName("on_screen_text")]
        public string? OnScreenText { get; set; }

        [JsonPropertyName("product")]
        public string? Product { get; set; }

        [JsonPropertyName("actions")]
        public string? Actions { get; set; }

        [JsonPropertyName("commercial_angle")]
        public string? CommercialAngle { get; set; }

        [JsonPropertyName("functions")]
        public List<string>? Functions { get; set; }

        [JsonPropertyName("main_message")]
        public string? MainMessage { get; set; }

        [JsonPropertyName("audience")]
        public string? Audience { get; set; }

        [JsonPropertyName("visual_quality")]
        public int VisualQuality { get; set; }

        [JsonPropertyName("tags")]
        public List<string>? Tags { get; set; }

        [JsonPropertyName("confidence")]
        public double Confidence { get; set; }
    }
}

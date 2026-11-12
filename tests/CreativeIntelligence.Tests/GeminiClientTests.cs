using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace CreativeIntelligence.Tests;

/// <summary>
/// Contrato del cliente de Gemini contra la forma documentada de la API Interactions (revisión 2026-05-20), con
/// un servidor HTTP falso: no usa red ni clave real. No reemplaza una prueba contra el servicio verdadero.
/// </summary>
public sealed class GeminiClientTests : IDisposable
{
    private const string CompletedResponse =
        """
        {
          "id": "v1_abc", "object": "interaction", "model": "gemini-3.5-flash-lite", "status": "completed",
          "steps": [
            { "type": "thought", "content": [ { "type": "text", "text": "pensando" } ] },
            { "type": "model_output", "content": [ { "type": "text", "text": "{\"scenes\":[{\"index\":1,\"visual_description\":\"Una mano muestra uñas terminadas\",\"on_screen_text\":\"LISTAS EN 5 MIN\",\"product\":\"Esmalte\",\"actions\":\"Muestra el resultado\",\"commercial_angle\":\"practicidad\",\"functions\":[\"hook\",\"social_proof\",\"inventada\"],\"main_message\":\"Uñas perfectas rápido\",\"audience\":\"Mujeres\",\"visual_quality\":9,\"tags\":[\"uñas\",\"resultado\"],\"confidence\":0.85}]}" } ] }
          ],
          "usage": { "total_input_tokens": 1200, "total_output_tokens": 300, "total_thought_tokens": 100, "total_tokens": 1600 }
        }
        """;

    private readonly string _framePath = Path.Combine(Path.GetTempPath(), $"ci-frame-{Guid.NewGuid():N}.jpg");

    public GeminiClientTests()
    {
        File.WriteAllBytes(_framePath, [0xFF, 0xD8, 0xFF, 0xD9]);
    }

    public void Dispose()
    {
        File.Delete(_framePath);
    }

    [Fact]
    public async Task El_pedido_respeta_la_api_y_la_respuesta_se_convierte_en_analisis_con_su_costo()
    {
        StubHandler handler = new(HttpStatusCode.OK, CompletedResponse);
        GeminiSegmentAnalyzer analyzer = Create(handler, "clave-de-prueba");

        AnalysisOutcome outcome = await analyzer.AnalyzeAsync(Request(), CancellationToken.None);

        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/interactions", handler.Url);
        Assert.Equal("clave-de-prueba", handler.Headers["x-goog-api-key"]);
        Assert.Equal("2026-05-20", handler.Headers["Api-Revision"]);
        using JsonDocument body = JsonDocument.Parse(handler.Body);
        JsonElement root = body.RootElement;
        Assert.Equal("gemini-3.5-flash-lite", root.GetProperty("model").GetString());
        Assert.False(root.GetProperty("store").GetBoolean());
        Assert.Equal(SceneAnalysisPrompt.SystemInstruction, root.GetProperty("system_instruction").GetString());
        Assert.Equal("application/json", root.GetProperty("response_format").GetProperty("mime_type").GetString());
        Assert.Equal("object", root.GetProperty("response_format").GetProperty("schema").GetProperty("type").GetString());
        Assert.Equal("low", root.GetProperty("generation_config").GetProperty("thinking_level").GetString());
        JsonElement[] input = root.GetProperty("input").EnumerateArray().ToArray();
        Assert.Equal(["text", "text", "image"], input.Select(p => p.GetProperty("type").GetString()));
        Assert.Contains("Se dice: \"Mirá esto\"", input[1].GetProperty("text").GetString());
        Assert.Equal("image/jpeg", input[2].GetProperty("mime_type").GetString());
        Assert.Equal(Convert.ToBase64String([0xFF, 0xD8, 0xFF, 0xD9]), input[2].GetProperty("data").GetString());
        Assert.False(input[2].TryGetProperty("text", out _));
        Assert.DoesNotContain("clave-de-prueba", handler.Body);

        SceneAnalysis scene = Assert.Single(outcome.Scenes);
        Assert.Equal("Una mano muestra uñas terminadas", scene.VisualDescription);
        Assert.Equal("LISTAS EN 5 MIN", scene.OnScreenText);
        Assert.Equal([CreativeFunction.Hook, CreativeFunction.SocialProof], scene.Functions);
        Assert.Equal(0.85m, scene.Confidence);
        Assert.Equal("gemini", outcome.Provider);
        Assert.Equal(SceneAnalysisPrompt.Version, outcome.PromptVersion);
        Assert.Equal(1200, outcome.InputTokens);
        // El razonamiento se cobra como salida: 300 + 100.
        Assert.Equal(400, outcome.OutputTokens);
        Assert.Equal(0.00136m, outcome.CostUsd);
    }

    [Fact]
    public async Task Los_tramos_narrativos_de_la_respuesta_se_leen_y_los_de_funcion_desconocida_se_descartan()
    {
        const string response =
            """
            {
              "status": "completed",
              "steps": [ { "type": "model_output", "content": [ { "type": "text", "text": "{\"scenes\":[{\"index\":1,\"visual_description\":\"x\",\"functions\":[\"hook\"],\"visual_quality\":3,\"tags\":[],\"confidence\":0.5}],\"narrative_segments\":[{\"from_scene\":1,\"to_scene\":2,\"function\":\"problem\",\"summary\":\"Plantea el problema\"},{\"from_scene\":3,\"to_scene\":3,\"function\":\"inventada\",\"summary\":\"x\"}]}" } ] } ],
              "usage": { "total_input_tokens": 10, "total_output_tokens": 5, "total_thought_tokens": 0 }
            }
            """;
        StubHandler handler = new(HttpStatusCode.OK, response);

        AnalysisOutcome outcome = await Create(handler, "clave-de-prueba").AnalyzeAsync(Request(), CancellationToken.None);

        NarrativeAnalysis narrative = Assert.Single(outcome.Narratives);
        Assert.Equal(new NarrativeAnalysis(1, 2, CreativeFunction.Problem, "Plantea el problema"), narrative);
        using JsonDocument body = JsonDocument.Parse(handler.Body);
        Assert.True(body.RootElement.GetProperty("response_format").GetProperty("schema").GetProperty("properties").TryGetProperty("narrative_segments", out _));
        // La respuesta de la revisión anterior del prompt, sin tramos, sigue siendo válida.
        Assert.Empty((await Create(new StubHandler(HttpStatusCode.OK, CompletedResponse), "clave-de-prueba").AnalyzeAsync(Request(), CancellationToken.None)).Narratives);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.Forbidden, false)]
    public async Task Un_error_http_se_informa_con_su_mensaje_y_si_conviene_reintentar(HttpStatusCode status, bool retryable)
    {
        StubHandler handler = new(status, """{"error":{"code":"x","message":"Detalle del proveedor"}}""");
        GeminiSegmentAnalyzer analyzer = Create(handler, "clave-de-prueba");

        AiProviderException exception = await Assert.ThrowsAsync<AiProviderException>(() => analyzer.AnalyzeAsync(Request(), CancellationToken.None));

        Assert.Equal(retryable, exception.Retryable);
        Assert.Contains(((int)status).ToString(), exception.Message);
        Assert.Contains("Detalle del proveedor", exception.Message);
        Assert.DoesNotContain("clave-de-prueba", exception.Message);
    }

    [Theory]
    [InlineData("""{"status":"incomplete","steps":[]}""")]
    [InlineData("""{"status":"completed","steps":[{"type":"model_output","content":[{"type":"text","text":"no es json"}]}]}""")]
    [InlineData("""{"status":"completed","steps":[]}""")]
    public async Task Una_respuesta_incompleta_o_ilegible_es_un_error_definitivo(string response)
    {
        GeminiSegmentAnalyzer analyzer = Create(new StubHandler(HttpStatusCode.OK, response), "clave-de-prueba");

        AiProviderException exception = await Assert.ThrowsAsync<AiProviderException>(() => analyzer.AnalyzeAsync(Request(), CancellationToken.None));

        Assert.False(exception.Retryable);
    }

    [Fact]
    public async Task Sin_clave_no_esta_configurado_y_no_llama_al_proveedor()
    {
        StubHandler handler = new(HttpStatusCode.OK, CompletedResponse);
        GeminiSegmentAnalyzer analyzer = Create(handler, "");

        Assert.False(analyzer.IsConfigured);
        await Assert.ThrowsAsync<AiProviderException>(() => analyzer.AnalyzeAsync(Request(), CancellationToken.None));
        Assert.Null(handler.Url);
    }

    [Fact]
    public void La_estimacion_usa_258_tokens_por_cuadro_y_las_tarifas_configuradas()
    {
        GeminiSegmentAnalyzer analyzer = Create(new StubHandler(HttpStatusCode.OK, "{}"), "clave-de-prueba");

        AnalysisCost cost = analyzer.Estimate(scenes: 10, frames: 20, transcriptCharacters: 600);

        Assert.Equal((20 * 258) + 900 + 250 + 200, cost.InputTokens);
        Assert.Equal(2700, cost.OutputTokens);
        Assert.Equal(Math.Round(((cost.InputTokens * 0.30m) + (2700 * 2.50m)) / 1_000_000m, 6), cost.CostUsd);
        Assert.InRange(cost.CostUsd, 0.005m, 0.02m);
    }

    private VideoAnalysisRequest Request()
    {
        return new VideoAnalysisRequest("anuncio.mp4", [new SceneInput(1, 0, 2000, "Mirá esto", [_framePath])]);
    }

    private static GeminiSegmentAnalyzer Create(StubHandler handler, string apiKey)
    {
        IOptions<GeminiOptions> options = Options.Create(new GeminiOptions { ApiKey = apiKey });
        return new GeminiSegmentAnalyzer(new GeminiInteractions(new HttpClient(handler), options), options);
    }

    private sealed class StubHandler(HttpStatusCode status, string response) : HttpMessageHandler
    {
        public string? Url { get; private set; }

        public string Body { get; private set; } = string.Empty;

        public Dictionary<string, string> Headers { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Url = request.RequestUri!.ToString();
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            foreach (KeyValuePair<string, IEnumerable<string>> header in request.Headers)
            {
                Headers[header.Key] = string.Join(",", header.Value);
            }

            return new HttpResponseMessage(status) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }
}

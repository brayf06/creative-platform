using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace CreativeIntelligence.Web.Services.Ai;

/// <summary>Una parte de lo que se le envía al modelo: texto o una imagen.</summary>
/// <param name="Type"><c>text</c> o <c>image</c>.</param>
/// <param name="Text">Texto; nulo si es una imagen.</param>
/// <param name="Data">Imagen en base 64; nulo si es texto.</param>
/// <param name="MimeType">Tipo de la imagen; nulo si es texto.</param>
public sealed record InteractionPart(string Type, string? Text, string? Data = null, string? MimeType = null);

/// <summary>Lo que devuelve una llamada al modelo.</summary>
/// <param name="Json">Salida del modelo, que se pidió en JSON; vacío si no devolvió texto.</param>
/// <param name="InputTokens">Tokens de entrada cobrados.</param>
/// <param name="OutputTokens">Tokens de salida cobrados, incluido el razonamiento.</param>
/// <param name="DurationMs">Cuánto tardó la llamada.</param>
public sealed record InteractionResult(string Json, int InputTokens, int OutputTokens, int DurationMs)
{
    /// <summary>Consultas de búsqueda web que hizo el modelo; se cobran aparte de los tokens.</summary>
    public int SearchQueries { get; init; }
}

/// <summary>
/// Protocolo de la API Interactions de Gemini (<c>POST /v1beta/interactions</c>): arma el pedido con salida JSON
/// estructurada, lo envía y traduce la respuesta y los errores. Es el único lugar que conoce la forma de la API;
/// los clientes de cada tarea (análisis de escenas, plan de conceptos) sólo aportan su consigna y su esquema. Los
/// pedidos van con <c>store: false</c> para que el proveedor no conserve lo enviado ni lo respondido.
/// </summary>
/// <param name="http">Cliente HTTP.</param>
/// <param name="options">Clave, dirección y revisión de la API.</param>
public sealed class GeminiInteractions(HttpClient http, IOptions<GeminiOptions> options)
{
    /// <summary>Si hay clave configurada.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.Value.ApiKey);

    /// <summary>Envía un pedido al modelo y devuelve su salida en JSON.</summary>
    /// <param name="model">Modelo.</param>
    /// <param name="systemInstruction">Consigna de sistema.</param>
    /// <param name="input">Partes del pedido, en orden.</param>
    /// <param name="schema">Esquema JSON que debe cumplir la salida.</param>
    /// <param name="thinkingLevel">Nivel de razonamiento: <c>minimal</c>, <c>low</c>, <c>medium</c> o <c>high</c>.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <param name="webSearch">Verdadero para permitirle al modelo buscar en la web (herramienta
    /// <c>google_search</c>). Cada consulta que haga se cobra aparte.</param>
    /// <returns>La salida, los tokens cobrados y las consultas de búsqueda hechas.</returns>
    /// <exception cref="AiProviderException">Si falta la clave, el proveedor responde con error, tarda demasiado o
    /// no completa la respuesta. Indica si conviene reintentar.</exception>
    public async Task<InteractionResult> SendAsync(
        string model, string systemInstruction, IReadOnlyList<InteractionPart> input, JsonNode schema, string thinkingLevel,
        CancellationToken cancellationToken, bool webSearch = false)
    {
        GeminiOptions settings = options.Value;
        if (!IsConfigured)
        {
            throw new AiProviderException("Falta configurar la clave de Gemini.", false);
        }

        InteractionRequest body = new()
        {
            Model = model,
            SystemInstruction = systemInstruction,
            Input = input.Select(p => new InputPart { Type = p.Type, Text = p.Text, Data = p.Data, MimeType = p.MimeType }).ToList(),
            ResponseFormat = new ResponseFormat { Type = "text", MimeType = "application/json", Schema = schema },
            GenerationConfig = new GenerationConfig { ThinkingLevel = thinkingLevel },
            Tools = webSearch ? [new Tool { Type = "google_search" }] : null,
            Store = false,
        };
        using HttpRequestMessage message = new(HttpMethod.Post, new Uri(new Uri(settings.BaseUrl), "interactions"));
        message.Headers.Add("x-goog-api-key", settings.ApiKey);
        message.Headers.Add("Api-Revision", settings.ApiRevision);
        message.Content = JsonContent.Create(body);

        Stopwatch stopwatch = Stopwatch.StartNew();
        InteractionResponse response = await SendAsync(message, settings.TimeoutSeconds, cancellationToken);
        stopwatch.Stop();
        if (response.Status != "completed")
        {
            throw new AiProviderException($"Gemini no completó la respuesta (estado: {response.Status ?? "desconocido"}).", false);
        }

        string json = string.Concat(
            (response.Steps ?? [])
                .Where(s => s.Type == "model_output")
                .SelectMany(s => s.Content ?? [])
                .Where(c => c.Type == "text")
                .Select(c => c.Text));

        // El razonamiento se cobra como salida.
        return new InteractionResult(
            json,
            response.Usage?.TotalInputTokens ?? 0,
            (response.Usage?.TotalOutputTokens ?? 0) + (response.Usage?.TotalThoughtTokens ?? 0),
            (int)stopwatch.ElapsedMilliseconds)
        {
            SearchQueries = (response.Steps ?? []).Where(s => s.Type == "google_search_call").Sum(s => s.Arguments?.Queries?.Count ?? 0),
        };
    }

    private async Task<InteractionResponse> SendAsync(HttpRequestMessage message, int timeoutSeconds, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            using HttpResponseMessage response = await http.SendAsync(message, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                string detail = await ErrorMessageAsync(response, timeout.Token);
                bool retryable = response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout
                    || (int)response.StatusCode >= 500;
                throw new AiProviderException($"Gemini respondió {(int)response.StatusCode}. {detail}".Trim(), retryable);
            }

            return await response.Content.ReadFromJsonAsync<InteractionResponse>(timeout.Token)
                ?? throw new AiProviderException("Gemini devolvió una respuesta vacía.", false);
        }
        catch (HttpRequestException)
        {
            throw new AiProviderException("No se pudo conectar con Gemini.", true);
        }
        catch (JsonException)
        {
            throw new AiProviderException("Gemini devolvió una respuesta que no se pudo interpretar.", false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiProviderException($"Gemini tardó más de {timeoutSeconds} segundos en responder.", true);
        }
    }

    private static async Task<string> ErrorMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            string? message = JsonSerializer.Deserialize<ErrorEnvelope>(body)?.Error?.Message;
            return message is null ? string.Empty : (message.Length > 300 ? message[..300] : message);
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    private sealed class InteractionRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("system_instruction")]
        public string SystemInstruction { get; set; } = string.Empty;

        [JsonPropertyName("input")]
        public List<InputPart> Input { get; set; } = [];

        [JsonPropertyName("response_format")]
        public ResponseFormat? ResponseFormat { get; set; }

        [JsonPropertyName("generation_config")]
        public GenerationConfig? GenerationConfig { get; set; }

        [JsonPropertyName("tools")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<Tool>? Tools { get; set; }

        [JsonPropertyName("store")]
        public bool Store { get; set; }
    }

    private sealed class InputPart
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("text")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Text { get; set; }

        [JsonPropertyName("data")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Data { get; set; }

        [JsonPropertyName("mime_type")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? MimeType { get; set; }
    }

    private sealed class ResponseFormat
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("mime_type")]
        public string MimeType { get; set; } = string.Empty;

        [JsonPropertyName("schema")]
        public JsonNode? Schema { get; set; }
    }

    private sealed class GenerationConfig
    {
        [JsonPropertyName("thinking_level")]
        public string ThinkingLevel { get; set; } = string.Empty;
    }

    private sealed class InteractionResponse
    {
        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("steps")]
        public List<Step>? Steps { get; set; }

        [JsonPropertyName("usage")]
        public Usage? Usage { get; set; }
    }

    private sealed class Tool
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;
    }

    private sealed class Step
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("content")]
        public List<StepContent>? Content { get; set; }

        [JsonPropertyName("arguments")]
        public StepArguments? Arguments { get; set; }
    }

    private sealed class StepArguments
    {
        [JsonPropertyName("queries")]
        public List<string>? Queries { get; set; }
    }

    private sealed class StepContent
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }

    private sealed class Usage
    {
        [JsonPropertyName("total_input_tokens")]
        public int TotalInputTokens { get; set; }

        [JsonPropertyName("total_output_tokens")]
        public int TotalOutputTokens { get; set; }

        [JsonPropertyName("total_thought_tokens")]
        public int TotalThoughtTokens { get; set; }
    }

    private sealed class ErrorEnvelope
    {
        [JsonPropertyName("error")]
        public ErrorBody? Error { get; set; }
    }

    private sealed class ErrorBody
    {
        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }
}

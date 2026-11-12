using System.Diagnostics;
using System.Net.Http.Json;
using System.Net;
using System.Text.Json.Serialization;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace CreativeIntelligence.Web.Services.Ai;

/// <summary>
/// Embeddings con Gemini (<c>batchEmbedContents</c>). El modelo <c>gemini-embedding-2</c> no recibe un tipo de
/// tarea aparte: la tarea va como prefijo del texto, distinto para documentos y para consultas. La API no informa
/// los tokens consumidos, así que se estiman por largo.
/// </summary>
/// <param name="http">Cliente HTTP.</param>
/// <param name="options">Clave, modelo, dimensiones y tarifa.</param>
public sealed class GeminiEmbeddingProvider(HttpClient http, IOptions<GeminiOptions> options) : IEmbeddingProvider
{
    private const int BatchSize = 50;

    /// <inheritdoc />
    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.Value.ApiKey);

    /// <inheritdoc />
    public string Model => options.Value.EmbeddingModel;

    /// <inheritdoc />
    public async Task<EmbeddingOutcome> EmbedAsync(List<string> texts, EmbeddingPurpose purpose, CancellationToken cancellationToken)
    {
        GeminiOptions settings = options.Value;
        if (!IsConfigured)
        {
            throw new AiProviderException("Falta configurar la clave de Gemini.", false);
        }

        List<string> prepared = texts
            .Select(t => purpose == EmbeddingPurpose.Query ? $"task: search result | query: {t}" : $"title: none | text: {t}")
            .ToList();
        List<float[]> vectors = [];
        Stopwatch stopwatch = Stopwatch.StartNew();
        foreach (string[] batch in prepared.Chunk(BatchSize))
        {
            BatchRequest body = new()
            {
                Requests = batch
                    .Select(text => new EmbedRequest
                    {
                        Model = $"models/{settings.EmbeddingModel}",
                        Content = new EmbedContent { Parts = [new EmbedPart { Text = text }] },
                        OutputDimensionality = settings.EmbeddingDimensions,
                    })
                    .ToList(),
            };
            using HttpRequestMessage message = new(
                HttpMethod.Post, new Uri(new Uri(settings.BaseUrl), $"models/{settings.EmbeddingModel}:batchEmbedContents"));
            message.Headers.Add("x-goog-api-key", settings.ApiKey);
            message.Content = JsonContent.Create(body);

            BatchResponse response = await SendAsync(message, settings.TimeoutSeconds, cancellationToken);
            if (response.Embeddings is null || response.Embeddings.Count != batch.Length || response.Embeddings.Any(e => e.Values is null))
            {
                throw new AiProviderException("Gemini devolvió una cantidad de embeddings distinta de la pedida.", false);
            }

            // Se normaliza siempre: la búsqueda asume vectores de longitud 1 y no depende de que el proveedor lo haga.
            vectors.AddRange(response.Embeddings.Select(e => VectorMath.Normalize(e.Values!)));
        }

        stopwatch.Stop();
        int tokens = prepared.Sum(t => t.Length) / 3;
        decimal cost = Math.Round(tokens * settings.EmbeddingPricePerMillion / 1_000_000m, 6);
        return new EmbeddingOutcome(vectors, "gemini", settings.EmbeddingModel, tokens, cost, (int)stopwatch.ElapsedMilliseconds);
    }

    private async Task<BatchResponse> SendAsync(HttpRequestMessage message, int timeoutSeconds, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            using HttpResponseMessage response = await http.SendAsync(message, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                bool retryable = response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout
                    || (int)response.StatusCode >= 500;
                throw new AiProviderException($"Gemini respondió {(int)response.StatusCode} al pedir embeddings.", retryable);
            }

            return await response.Content.ReadFromJsonAsync<BatchResponse>(timeout.Token)
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

    private sealed class BatchRequest
    {
        [JsonPropertyName("requests")]
        public List<EmbedRequest> Requests { get; set; } = [];
    }

    private sealed class EmbedRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public EmbedContent? Content { get; set; }

        [JsonPropertyName("output_dimensionality")]
        public int OutputDimensionality { get; set; }
    }

    private sealed class EmbedContent
    {
        [JsonPropertyName("parts")]
        public List<EmbedPart> Parts { get; set; } = [];
    }

    private sealed class EmbedPart
    {
        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;
    }

    private sealed class BatchResponse
    {
        [JsonPropertyName("embeddings")]
        public List<EmbeddingValues>? Embeddings { get; set; }
    }

    private sealed class EmbeddingValues
    {
        [JsonPropertyName("values")]
        public float[]? Values { get; set; }
    }
}

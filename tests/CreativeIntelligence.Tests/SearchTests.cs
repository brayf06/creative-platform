using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CreativeIntelligence.Tests;

/// <summary>
/// Proveedor de embeddings falso y determinista: cada palabra suma en una posición del vector según su hash, así
/// dos textos se parecen en la medida en que comparten palabras. Alcanza para probar el orden por similitud sin
/// llamar a un proveedor pago.
/// </summary>
public sealed class FakeEmbeddingProvider : IEmbeddingProvider
{
    public bool IsConfigured { get; set; } = true;

    public string Model => "embeddings-falsos";

    public int DocumentCalls { get; private set; }

    public int QueryCalls { get; private set; }

    public List<string> LastTexts { get; private set; } = [];

    public void Reset()
    {
        IsConfigured = true;
        DocumentCalls = 0;
        QueryCalls = 0;
        LastTexts = [];
    }

    public Task<EmbeddingOutcome> EmbedAsync(List<string> texts, EmbeddingPurpose purpose, CancellationToken cancellationToken)
    {
        if (purpose == EmbeddingPurpose.Query)
        {
            QueryCalls++;
        }
        else
        {
            DocumentCalls++;
            LastTexts = texts;
        }

        List<float[]> vectors = texts.Select(Vector).ToList();
        return Task.FromResult(new EmbeddingOutcome(vectors, "falso", Model, texts.Sum(t => t.Length) / 3, 0.00001m, 5));
    }

    private static float[] Vector(string text)
    {
        float[] vector = new float[128];
        foreach (string word in text.ToLowerInvariant().Split([' ', '.', ',', ':', ';', '"'], StringSplitOptions.RemoveEmptyEntries))
        {
            int position = word.Aggregate(17, (hash, c) => unchecked((hash * 31) + c)) & 127;
            vector[position] += 1;
        }

        return VectorMath.Normalize(vector);
    }
}

[Collection(DatabaseCollection.Name)]
public sealed class SearchTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task La_busqueda_por_significado_ordena_por_similitud_y_respeta_los_filtros()
    {
        Scenario scenario = await ArrangeAnalyzedAsync();
        await using AsyncServiceScope scope = fixture.Scope(scenario.Organization);
        SearchService search = scope.ServiceProvider.GetRequiredService<SearchService>();

        SearchResult result = await search.SearchAsync(new SegmentSearchFilter { Query = "Descripción de la escena 2 Mensaje 2" }, CancellationToken.None);

        Assert.Equal(SearchMode.Semantic, result.Mode);
        Assert.Equal(3, result.Candidates);
        Assert.Equal(scenario.Segments[1], result.Hits[0].SegmentId);
        Assert.Equal(result.Hits.OrderByDescending(h => h.Score).Select(h => h.SegmentId), result.Hits.Select(h => h.SegmentId));
        Assert.All(result.Hits, h => Assert.InRange(h.Score!.Value, 0, 1.001));
        Assert.Equal("cortes.mp4", result.Hits[0].FileName);
        Assert.Equal("Descripción de la escena 2", result.Hits[0].VisualDescription);
        Assert.Equal([CreativeFunction.Demonstration], result.Hits[0].Functions);
        Assert.True(result.Hits[0].HasThumbnail);

        // "Hooks de menos de 4 segundos": el filtro de función deja sólo la escena 1.
        SearchResult hooks = await search.SearchAsync(
            new SegmentSearchFilter { Query = "escena", Function = CreativeFunction.Hook, MaxDurationSeconds = 4 }, CancellationToken.None);
        Assert.Equal(scenario.Segments[0], Assert.Single(hooks.Hits).SegmentId);

        // La tercera escena dura 1,5 s.
        SearchResult brief = await search.SearchAsync(new SegmentSearchFilter { MaxDurationSeconds = 1.8m }, CancellationToken.None);
        Assert.Equal(SearchMode.Filters, brief.Mode);
        Assert.Equal(scenario.Segments[2], Assert.Single(brief.Hits).SegmentId);
        Assert.Null(brief.Hits[0].Score);

        SearchResult limited = await search.SearchAsync(new SegmentSearchFilter { Query = "escena", Take = 1 }, CancellationToken.None);
        Assert.Single(limited.Hits);
        Assert.Equal(3, limited.Candidates);
    }

    [Fact]
    public async Task Los_filtros_de_autorizacion_y_seleccion_usan_el_estado_actual_del_video()
    {
        Scenario scenario = await ArrangeAnalyzedAsync();
        await using AsyncServiceScope scope = fixture.Scope(scenario.Organization);
        SearchService search = scope.ServiceProvider.GetRequiredService<SearchService>();
        MediaLibraryService media = scope.ServiceProvider.GetRequiredService<MediaLibraryService>();
        SegmentSearchFilter authorized = new() { OnlyAuthorized = true };
        SegmentSearchFilter selected = new() { OnlySelected = true };

        Assert.Empty((await search.SearchAsync(authorized, CancellationToken.None)).Hits);
        Assert.All((await search.SearchAsync(new SegmentSearchFilter(), CancellationToken.None)).Hits, h => Assert.False(h.ReuseAuthorized));

        await media.UpdateRightsAsync(scenario.Asset, new MediaRightsBody { ReuseAuthorized = true }, CancellationToken.None);
        Assert.Equal(3, (await search.SearchAsync(authorized, CancellationToken.None)).Hits.Count);
        Assert.Empty((await search.SearchAsync(selected, CancellationToken.None)).Hits);

        await scope.ServiceProvider.GetRequiredService<SegmentEditService>().SetSelectedAsync(scenario.Segments[1], true, CancellationToken.None);
        SearchHit hit = Assert.Single((await search.SearchAsync(selected, CancellationToken.None)).Hits);
        Assert.Equal(scenario.Segments[1], hit.SegmentId);
        Assert.True(hit.SelectedForProduction);
        Assert.True(hit.ReuseAuthorized);

        await media.UpdateRightsAsync(
            scenario.Asset, new MediaRightsBody { ReuseAuthorized = true, RightsExpireOn = new DateOnly(2020, 1, 1) }, CancellationToken.None);
        Assert.Empty((await search.SearchAsync(authorized, CancellationToken.None)).Hits);
    }

    [Fact]
    public async Task Sin_proveedor_de_embeddings_se_busca_por_palabras_y_deben_estar_todas()
    {
        Scenario scenario = await ArrangeAnalyzedAsync();
        fixture.Embeddings.IsConfigured = false;
        await using AsyncServiceScope scope = fixture.Scope(scenario.Organization);
        SearchService search = scope.ServiceProvider.GetRequiredService<SearchService>();

        SearchResult byTag = await search.SearchAsync(new SegmentSearchFilter { Query = "escena-2" }, CancellationToken.None);
        Assert.Equal(SearchMode.Keyword, byTag.Mode);
        Assert.False(byTag.SemanticAvailable);
        Assert.Equal(scenario.Segments[1], Assert.Single(byTag.Hits).SegmentId);

        SearchResult byDescription = await search.SearchAsync(new SegmentSearchFilter { Query = "Mensaje 3" }, CancellationToken.None);
        Assert.Equal(scenario.Segments[2], Assert.Single(byDescription.Hits).SegmentId);

        Assert.Equal(3, (await search.SearchAsync(new SegmentSearchFilter { Query = "Esmalte uñas" }, CancellationToken.None)).Hits.Count);
        Assert.Empty((await search.SearchAsync(new SegmentSearchFilter { Query = "Esmalte inexistente" }, CancellationToken.None)).Hits);
        Assert.Equal(0, fixture.Embeddings.QueryCalls);
    }

    [Fact]
    public async Task El_indice_solo_vuelve_a_pedir_lo_que_cambio_y_la_consulta_repetida_no_se_vuelve_a_pagar()
    {
        Scenario scenario = await ArrangeAnalyzedAsync();
        await using AsyncServiceScope scope = fixture.Scope(scenario.Organization);
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        SearchService search = scope.ServiceProvider.GetRequiredService<SearchService>();
        SegmentIndexService index = scope.ServiceProvider.GetRequiredService<SegmentIndexService>();
        Assert.Equal(3, await db.SegmentEmbeddings.CountAsync());
        Assert.Equal(1, fixture.Embeddings.DocumentCalls);
        Assert.Contains("Función: hook, gancho inicial; problema", fixture.Embeddings.LastTexts[0]);
        Assert.Contains("Etiquetas:", fixture.Embeddings.LastTexts[0]);

        // Nada cambió: el trabajo no llama al proveedor.
        await index.EnqueueIfConfiguredAsync(scenario.Asset, CancellationToken.None);
        await DrainAsync();
        Assert.Equal(1, fixture.Embeddings.DocumentCalls);

        // Cambia la etiqueta de una escena: se vuelve a indexar sólo esa.
        await scope.ServiceProvider.GetRequiredService<SegmentEditService>().SetTagsAsync(scenario.Segments[2], "navidad", CancellationToken.None);
        await DrainAsync();
        Assert.Equal(2, fixture.Embeddings.DocumentCalls);
        Assert.Contains("navidad", Assert.Single(fixture.Embeddings.LastTexts));
        Assert.Equal(3, await db.SegmentEmbeddings.CountAsync());
        SearchResult result = await search.SearchAsync(new SegmentSearchFilter { Query = "navidad" }, CancellationToken.None);
        Assert.Equal(scenario.Segments[2], result.Hits[0].SegmentId);

        await search.SearchAsync(new SegmentSearchFilter { Query = "Navidad" }, CancellationToken.None);
        Assert.Equal(1, fixture.Embeddings.QueryCalls);
        Assert.Equal(1, await db.AiUsageRecords.CountAsync(u => u.Task == "search.query"));
        Assert.Equal(2, await db.AiUsageRecords.CountAsync(u => u.Task == "segment.embedding"));
    }

    [Fact]
    public async Task Otra_organizacion_no_encuentra_nada_aunque_busque_lo_mismo()
    {
        Scenario scenario = await ArrangeAnalyzedAsync();
        int other = await fixture.NewOrganizationAsync();
        await using AsyncServiceScope scope = fixture.Scope(other);
        SearchService search = scope.ServiceProvider.GetRequiredService<SearchService>();

        Assert.Empty((await search.SearchAsync(new SegmentSearchFilter { Query = "Descripción de la escena 2" }, CancellationToken.None)).Hits);
        Assert.Empty((await search.SearchAsync(new SegmentSearchFilter(), CancellationToken.None)).Hits);
        fixture.Embeddings.IsConfigured = false;
        Assert.Empty((await search.SearchAsync(new SegmentSearchFilter { Query = "Esmalte" }, CancellationToken.None)).Hits);
        Assert.NotEqual(0, scenario.Asset);
    }

    [Fact]
    public void Un_vector_se_guarda_y_se_recupera_igual_y_normalizado_su_producto_consigo_mismo_es_uno()
    {
        float[] vector = VectorMath.Normalize([3f, 4f, 0f]);

        Assert.Equal(vector, VectorMath.FromBytes(VectorMath.ToBytes(vector)));
        Assert.Equal(1.0, VectorMath.Dot(vector, vector), 5);
        Assert.Equal(0.0, VectorMath.Dot(vector, VectorMath.Normalize([0f, 0f, 1f])), 5);
        Assert.Equal(0.0, VectorMath.Dot(vector, [1f]), 5);
    }

    [Fact]
    public async Task El_cliente_de_embeddings_de_Gemini_arma_el_pedido_documentado_y_normaliza_la_respuesta()
    {
        EmbeddingStub handler = new(HttpStatusCode.OK, """{"embeddings":[{"values":[3,4]},{"values":[0,2]}]}""");
        GeminiEmbeddingProvider provider = new(new HttpClient(handler), Options.Create(new GeminiOptions { ApiKey = "clave-de-prueba" }));

        EmbeddingOutcome documents = await provider.EmbedAsync(["hook de uñas", "cierre"], EmbeddingPurpose.Document, CancellationToken.None);

        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/models/gemini-embedding-2:batchEmbedContents", handler.Url);
        Assert.Equal("clave-de-prueba", handler.ApiKey);
        using JsonDocument body = JsonDocument.Parse(handler.Body);
        JsonElement[] requests = body.RootElement.GetProperty("requests").EnumerateArray().ToArray();
        Assert.Equal(2, requests.Length);
        Assert.Equal("models/gemini-embedding-2", requests[0].GetProperty("model").GetString());
        Assert.Equal(768, requests[0].GetProperty("output_dimensionality").GetInt32());
        Assert.Equal("title: none | text: hook de uñas", requests[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString());
        Assert.Equal([0.6f, 0.8f], documents.Vectors[0]);
        Assert.Equal([0f, 1f], documents.Vectors[1]);
        Assert.Equal("gemini-embedding-2", documents.Model);

        // La respuesta fija trae dos vectores para un solo texto: el cliente la rechaza, pero el pedido ya se capturó.
        await Assert.ThrowsAsync<AiProviderException>(() => provider.EmbedAsync(["uñas"], EmbeddingPurpose.Query, CancellationToken.None));
        using JsonDocument query = JsonDocument.Parse(handler.Body);
        Assert.Equal(
            "task: search result | query: uñas",
            query.RootElement.GetProperty("requests")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString());

        GeminiEmbeddingProvider throttled = new(
            new HttpClient(new EmbeddingStub(HttpStatusCode.TooManyRequests, "{}")), Options.Create(new GeminiOptions { ApiKey = "clave-de-prueba" }));
        AiProviderException exception = await Assert.ThrowsAsync<AiProviderException>(
            () => throttled.EmbedAsync(["x"], EmbeddingPurpose.Document, CancellationToken.None));
        Assert.True(exception.Retryable);
    }

    // Video real con tres escenas, analizado por el proveedor falso e indexado.
    private async Task<Scenario> ArrangeAnalyzedAsync()
    {
        await fixture.ClearJobsAsync();
        fixture.Analyzer.Reset();
        fixture.Embeddings.Reset();
        int org = await fixture.NewOrganizationAsync();
        await using AsyncServiceScope scope = fixture.Scope(org);
        long assetId;
        await using (FileStream stream = File.OpenRead(fixture.SampleCutsVideoPath))
        {
            MediaLibraryService media = scope.ServiceProvider.GetRequiredService<MediaLibraryService>();
            assetId = (await media.IngestAsync("cortes.mp4", new MediaRightsBody(), stream, CancellationToken.None)).AssetId;
        }

        await DrainAsync();
        await scope.ServiceProvider.GetRequiredService<AnalysisService>().RequestAsync(assetId, CancellationToken.None);
        await DrainAsync();
        SceneList scenes = await scope.ServiceProvider.GetRequiredService<SceneService>().ListAsync(assetId, CancellationToken.None);
        return new Scenario(org, assetId, scenes.Segments.Select(s => s.Id).ToList());
    }

    private async Task DrainAsync()
    {
        JobRunner runner = fixture.Services.GetRequiredService<JobRunner>();
        while (await runner.ProcessNextAsync(CancellationToken.None))
        {
        }
    }

    private sealed record Scenario(int Organization, long Asset, List<long> Segments);

    private sealed class EmbeddingStub(HttpStatusCode status, string response) : HttpMessageHandler
    {
        public string? Url { get; private set; }

        public string? ApiKey { get; private set; }

        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Url = request.RequestUri!.ToString();
            ApiKey = request.Headers.GetValues("x-goog-api-key").Single();
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }
}

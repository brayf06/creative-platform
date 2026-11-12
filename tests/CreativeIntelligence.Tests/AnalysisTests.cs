using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CreativeIntelligence.Tests;

/// <summary>Proveedor de IA falso: responde un análisis fijo por escena, cuenta las llamadas y puede fallar a pedido.
/// Los tests automáticos nunca llaman a un proveedor pago.</summary>
public sealed class FakeSegmentAnalyzer : ISegmentAnalyzer
{
    public bool IsConfigured { get; set; } = true;

    public string Model => "modelo-falso";

    public int Calls { get; private set; }

    public decimal CostPerCall { get; set; } = 0.002m;

    public Queue<AiProviderException> Errors { get; } = new();

    public VideoAnalysisRequest? LastRequest { get; private set; }

    /// <summary>Tramos narrativos que devuelve; vacío salvo que un test los defina.</summary>
    public List<NarrativeAnalysis> Narratives { get; set; } = [];

    public void Reset()
    {
        Narratives = [];
        IsConfigured = true;
        Calls = 0;
        CostPerCall = 0.002m;
        Errors.Clear();
        LastRequest = null;
    }

    public AnalysisCost Estimate(int scenes, int frames, int transcriptCharacters)
    {
        return new AnalysisCost(frames * 258, scenes * 200, CostPerCall);
    }

    public Task<AnalysisOutcome> AnalyzeAsync(VideoAnalysisRequest request, CancellationToken cancellationToken)
    {
        Calls++;
        LastRequest = request;
        if (Errors.TryDequeue(out AiProviderException? error))
        {
            throw error;
        }

        Assert.All(request.Scenes, s => Assert.All(s.FramePaths, p => Assert.True(new FileInfo(p).Length > 0)));
        List<SceneAnalysis> scenes = request.Scenes
            .Select(s => new SceneAnalysis(
                s.Index, $"Descripción de la escena {s.Index}", "", "Esmalte", "Aplica el producto", "practicidad",
                s.Index == 1 ? [CreativeFunction.Hook, CreativeFunction.Problem] : [CreativeFunction.Demonstration],
                $"Mensaje {s.Index}", "Mujeres 25-40", 4, ["uñas", "hook", $"escena-{s.Index}"], 0.8m))
            .ToList();
        return Task.FromResult(
            new AnalysisOutcome(scenes, "falso", Model, SceneAnalysisPrompt.Version, 1500, 600, CostPerCall, 42) { Narratives = Narratives });
    }
}

[Collection(DatabaseCollection.Name)]
public sealed class AnalysisTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Analizar_un_video_documenta_cada_escena_registra_el_costo_y_no_se_vuelve_a_pagar()
    {
        Scenario scenario = await ArrangeAsync();
        await using AsyncServiceScope scope = fixture.Scope(scenario.Organization);
        AnalysisService analysis = scope.ServiceProvider.GetRequiredService<AnalysisService>();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        AnalysisEstimate estimate = await analysis.EstimateAsync(scenario.Asset, CancellationToken.None);
        Assert.True(estimate.Configured);
        Assert.Equal(3, estimate.Scenes);
        Assert.Equal(5, estimate.Frames);
        Assert.False(estimate.Cached);
        Assert.Equal(0.002m, estimate.CostUsd);
        Assert.Null((await analysis.GetAsync(scenario.Asset, CancellationToken.None)).Status);

        await analysis.RequestAsync(scenario.Asset, CancellationToken.None);
        Assert.Equal(MediaProcessingStatus.Pending, (await analysis.GetAsync(scenario.Asset, CancellationToken.None)).Status);
        await JobTests.AssertBusinessAsync(409, "ANALYSIS_IN_PROGRESS", () => analysis.RequestAsync(scenario.Asset, CancellationToken.None));
        await DrainAsync();

        AnalysisView view = await analysis.GetAsync(scenario.Asset, CancellationToken.None);
        Assert.Equal(MediaProcessingStatus.Ready, view.Status);
        Assert.Equal(3, view.Items.Count);
        Assert.Equal([CreativeFunction.Hook, CreativeFunction.Problem], view.Items[0].Functions);
        Assert.Equal("Descripción de la escena 2", view.Items[1].VisualDescription);
        Assert.All(view.Items, i => Assert.False(i.IsStale));
        Assert.All(view.Items, i => Assert.Equal(SceneAnalysisPrompt.Version, i.PromptVersion));
        Assert.Equal(1, fixture.Analyzer.Calls);
        Assert.Equal(3, fixture.Analyzer.LastRequest!.Scenes.Count);

        AiUsageRecord usage = await db.AiUsageRecords.AsNoTracking().SingleAsync(u => u.Task == "scene.analysis");
        Assert.Equal(0.002m, usage.CostUsd);
        Assert.Equal(1500, usage.InputTokens);
        SceneList scenes = await scope.ServiceProvider.GetRequiredService<SceneService>().ListAsync(scenario.Asset, CancellationToken.None);
        Assert.Contains("uñas", scenes.Segments[0].Tags);

        // Mismo video, mismas escenas, mismo modelo y prompt: sale del cache, sin llamar al proveedor ni registrar costo.
        AnalysisEstimate second = await analysis.EstimateAsync(scenario.Asset, CancellationToken.None);
        Assert.True(second.Cached);
        Assert.Equal(0m, second.CostUsd);
        await analysis.RequestAsync(scenario.Asset, CancellationToken.None);
        await DrainAsync();

        Assert.Equal(1, fixture.Analyzer.Calls);
        Assert.Equal(1, await db.AiUsageRecords.CountAsync(u => u.Task == "scene.analysis"));
        Assert.Equal(6, await db.SegmentAnalyses.CountAsync());
        Assert.Equal(3, await db.SegmentAnalyses.CountAsync(a => a.IsCurrent));
        Assert.Equal(3, (await analysis.GetAsync(scenario.Asset, CancellationToken.None)).Items.Count);
    }

    [Fact]
    public async Task No_se_analiza_sin_clave_ni_por_encima_del_tope_ni_sin_escenas_ni_desde_otra_organizacion()
    {
        Scenario scenario = await ArrangeAsync();
        int other = await fixture.NewOrganizationAsync();
        await using AsyncServiceScope scope = fixture.Scope(scenario.Organization);
        AnalysisService analysis = scope.ServiceProvider.GetRequiredService<AnalysisService>();

        fixture.Analyzer.IsConfigured = false;
        await JobTests.AssertBusinessAsync(409, "AI_NOT_CONFIGURED", () => analysis.RequestAsync(scenario.Asset, CancellationToken.None));
        fixture.Analyzer.IsConfigured = true;
        fixture.Analyzer.CostPerCall = 0.06m;
        await JobTests.AssertBusinessAsync(409, "BUDGET_EXCEEDED", () => analysis.RequestAsync(scenario.Asset, CancellationToken.None));
        fixture.Analyzer.CostPerCall = 0.002m;

        long image;
        await using (FileStream stream = File.OpenRead(fixture.SampleImagePath))
        {
            MediaLibraryService media = scope.ServiceProvider.GetRequiredService<MediaLibraryService>();
            image = (await media.IngestAsync("producto.png", new MediaRightsBody(), stream, CancellationToken.None)).AssetId;
        }

        await JobTests.AssertBusinessAsync(409, "SCENES_NOT_READY", () => analysis.EstimateAsync(image, CancellationToken.None));

        await using AsyncServiceScope otherScope = fixture.Scope(other);
        AnalysisService foreign = otherScope.ServiceProvider.GetRequiredService<AnalysisService>();
        await JobTests.AssertBusinessAsync(404, "MEDIA_NOT_FOUND", () => foreign.GetAsync(scenario.Asset, CancellationToken.None));
        await JobTests.AssertBusinessAsync(404, "MEDIA_NOT_FOUND", () => foreign.EstimateAsync(scenario.Asset, CancellationToken.None));
        await JobTests.AssertBusinessAsync(404, "MEDIA_NOT_FOUND", () => foreign.RequestAsync(scenario.Asset, CancellationToken.None));
        Assert.Equal(0, fixture.Analyzer.Calls);
    }

    [Fact]
    public async Task Un_error_transitorio_del_proveedor_se_reintenta_y_uno_definitivo_deja_el_analisis_fallido()
    {
        Scenario scenario = await ArrangeAsync();
        await using AsyncServiceScope scope = fixture.Scope(scenario.Organization);
        AnalysisService analysis = scope.ServiceProvider.GetRequiredService<AnalysisService>();

        fixture.Analyzer.Errors.Enqueue(new AiProviderException("Gemini respondió 429.", true));
        await analysis.RequestAsync(scenario.Asset, CancellationToken.None);
        await DrainAsync();
        Assert.Equal(MediaProcessingStatus.Ready, (await analysis.GetAsync(scenario.Asset, CancellationToken.None)).Status);
        Assert.Equal(2, fixture.Analyzer.Calls);

        // Otro prompt u otras escenas darían otra clave; acá se fuerza una llamada nueva editando una escena.
        await scope.ServiceProvider.GetRequiredService<SegmentEditService>().MoveBoundaryAsync(scenario.Segments[0], 1500, CancellationToken.None);
        fixture.Analyzer.Errors.Enqueue(new AiProviderException("Gemini respondió 400. Pedido inválido.", false));
        await analysis.RequestAsync(scenario.Asset, CancellationToken.None);
        await DrainAsync();

        AnalysisView view = await analysis.GetAsync(scenario.Asset, CancellationToken.None);
        Assert.Equal(MediaProcessingStatus.Failed, view.Status);
        Assert.Equal("Gemini respondió 400. Pedido inválido.", view.Error);
        Assert.Equal(3, fixture.Analyzer.Calls);
        JobService jobs = scope.ServiceProvider.GetRequiredService<JobService>();
        JobItem last = (await jobs.ListAsync(new PageRequest(), CancellationToken.None)).Items.First(j => j.Type == MediaAnalyzeJobHandler.JobType);
        Assert.Equal(BackgroundJobStatus.Failed, last.Status);
        Assert.Equal(1, last.Attempts);
    }

    [Fact]
    public async Task Editar_una_escena_deja_su_analisis_desactualizado_y_reanalizar_respeta_las_etiquetas_manuales()
    {
        Scenario scenario = await ArrangeAsync();
        await using AsyncServiceScope scope = fixture.Scope(scenario.Organization);
        AnalysisService analysis = scope.ServiceProvider.GetRequiredService<AnalysisService>();
        SegmentEditService editor = scope.ServiceProvider.GetRequiredService<SegmentEditService>();
        await analysis.RequestAsync(scenario.Asset, CancellationToken.None);
        await DrainAsync();

        await editor.SetTagsAsync(scenario.Segments[2], "Hook, propia", CancellationToken.None);
        await editor.MoveBoundaryAsync(scenario.Segments[0], 1500, CancellationToken.None);
        AnalysisView stale = await analysis.GetAsync(scenario.Asset, CancellationToken.None);
        Assert.Equal([true, true, false], stale.Items.Select(i => i.IsStale));

        await analysis.RequestAsync(scenario.Asset, CancellationToken.None);
        await DrainAsync();

        AnalysisView fresh = await analysis.GetAsync(scenario.Asset, CancellationToken.None);
        Assert.All(fresh.Items, i => Assert.False(i.IsStale));
        Assert.Equal(2, fixture.Analyzer.Calls);
        SceneList scenes = await scope.ServiceProvider.GetRequiredService<SceneService>().ListAsync(scenario.Asset, CancellationToken.None);
        // "hook" ya existía como etiqueta automática; al guardarla la persona pasó a manual y sobrevivió al reanálisis.
        Assert.Equal(["escena-3", "hook", "propia", "uñas"], scenes.Segments[2].Tags);
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        long third = scenario.Segments[2];
        List<string> manual = await db.MediaSegmentTags.Where(t => t.MediaSegmentId == third && t.Origin == MediaSegmentOrigin.Manual)
            .OrderBy(t => t.Text).Select(t => t.Text).ToListAsync();
        Assert.Equal(["hook", "propia"], manual);
    }

    [Theory]
    [InlineData(new[] { 0, 1500 }, 10, new[] { 1 })]
    [InlineData(new[] { 0, 2000, 2000, 9000 }, 10, new[] { 2, 3 })]
    [InlineData(new[] { 0, 9000, 9000, 18000 }, 5, new[] { 1, 1 })]
    public void Los_cuadros_por_escena_dependen_de_su_duracion_y_respetan_el_tope(int[] ranges, int maxFrames, int[] expected)
    {
        List<SceneInput> scenes = ranges.Chunk(2).Select((r, i) => new SceneInput(i + 1, r[0], r[1], "", [])).ToList();

        List<List<int>> plan = AnalysisService.PlanFrames(scenes, maxFrames);

        Assert.Equal(expected, plan.Select(p => p.Count));
        Assert.All(plan.Zip(scenes), pair => Assert.All(pair.First, t => Assert.InRange(t, pair.Second.StartMs + 1, pair.Second.EndMs - 1)));
        Assert.Throws<BusinessException>(() => AnalysisService.PlanFrames(scenes, scenes.Count - 1));
    }

    // Video real con tres escenas detectadas por el pipeline, para que haya cuadros que extraer.
    private async Task<Scenario> ArrangeAsync()
    {
        await fixture.ClearJobsAsync();
        fixture.Analyzer.Reset();
        int org = await fixture.NewOrganizationAsync();
        await using AsyncServiceScope scope = fixture.Scope(org);
        long assetId;
        await using (FileStream stream = File.OpenRead(fixture.SampleCutsVideoPath))
        {
            MediaLibraryService media = scope.ServiceProvider.GetRequiredService<MediaLibraryService>();
            assetId = (await media.IngestAsync("cortes.mp4", new MediaRightsBody(), stream, CancellationToken.None)).AssetId;
        }

        await DrainAsync();
        SceneList scenes = await scope.ServiceProvider.GetRequiredService<SceneService>().ListAsync(assetId, CancellationToken.None);
        Assert.Equal(3, scenes.Segments.Count);
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
}

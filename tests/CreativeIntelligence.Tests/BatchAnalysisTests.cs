using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CreativeIntelligence.Tests;

/// <summary>Análisis en lote y automático, tramos narrativos y los filtros del buscador que dependen de ellos.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class BatchAnalysisTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task La_tanda_analiza_los_pendientes_y_respeta_el_tope_por_video_la_clave_y_el_presupuesto()
    {
        Scenario scenario = await ArrangeAsync(2);
        await using AsyncServiceScope scope = fixture.Scope(scenario.Organization);
        AnalysisService analysis = scope.ServiceProvider.GetRequiredService<AnalysisService>();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        BatchEstimate estimate = await analysis.EstimateBatchAsync(CancellationToken.None);
        Assert.Equal(2, estimate.Pending);
        Assert.Equal(2, estimate.Videos);
        Assert.Equal(0.004m, estimate.CostUsd);
        Assert.False(estimate.AutoEnabled);

        // Por encima del tope por video no entra ninguno; sin clave, tampoco.
        fixture.Analyzer.CostPerCall = 0.06m;
        BatchEstimate tooExpensive = await analysis.EstimateBatchAsync(CancellationToken.None);
        Assert.Equal(0, tooExpensive.Videos);
        Assert.Equal(2, tooExpensive.OverVideoCap);
        fixture.Analyzer.CostPerCall = 0.002m;
        fixture.Analyzer.IsConfigured = false;
        BatchEstimate noKey = await analysis.EstimateBatchAsync(CancellationToken.None);
        Assert.Equal(0, noKey.Videos);
        Assert.Equal(2, noKey.NotConfigured);
        Assert.Equal(0, (await analysis.RequestBatchAsync(CancellationToken.None)).Enqueued);
        fixture.Analyzer.IsConfigured = true;

        // Con el presupuesto del mes casi agotado entra uno solo; el otro queda para otra tanda.
        AiUsageService usage = scope.ServiceProvider.GetRequiredService<AiUsageService>();
        AiOptions options = fixture.Services.GetRequiredService<IOptions<AiOptions>>().Value;
        await usage.RecordAsync(
            new AiUsageEntry("otra.tarea", "gemini", "modelo", "v1", 0, 0, options.MonthlyBudgetUsd - 0.003m, 0, null), CancellationToken.None);
        BatchEstimate tight = await analysis.EstimateBatchAsync(CancellationToken.None);
        Assert.Equal(0.003m, tight.LimitUsd);
        Assert.Equal(1, tight.Videos);
        Assert.Equal(1, tight.OverBudget);

        BatchResult result = await analysis.RequestBatchAsync(CancellationToken.None);
        Assert.Equal(1, result.Enqueued);
        Assert.Equal(1, result.Skipped);
        await DrainAsync();

        Assert.Equal(1, fixture.Analyzer.Calls);
        Assert.Equal(MediaProcessingStatus.Ready, (await analysis.GetAsync(scenario.Assets[0], CancellationToken.None)).Status);
        Assert.Null((await analysis.GetAsync(scenario.Assets[1], CancellationToken.None)).Status);
        Assert.Equal(1, (await analysis.EstimateBatchAsync(CancellationToken.None)).Pending);
        Assert.Equal(1, await db.AuditEntries.CountAsync(a => a.Action == "media.analyze"));
    }

    [Fact]
    public async Task El_modo_automatico_solo_analiza_en_las_organizaciones_que_lo_activaron()
    {
        Scenario on = await ArrangeAsync(1);
        Scenario off = await ArrangeAsync(1, keepJobs: true);
        AutoAnalysisScheduler scheduler = new(
            fixture.Services.GetRequiredService<IServiceScopeFactory>(),
            fixture.Services.GetRequiredService<IOptions<AiOptions>>(),
            NullLogger<AutoAnalysisScheduler>.Instance);

        Assert.Equal(0, await scheduler.RunOnceAsync(CancellationToken.None));

        await using (AsyncServiceScope scope = fixture.Scope(on.Organization))
        {
            AnalysisService analysis = scope.ServiceProvider.GetRequiredService<AnalysisService>();
            await analysis.SetAutoAnalyzeAsync(true, CancellationToken.None);
            Assert.True((await analysis.EstimateBatchAsync(CancellationToken.None)).AutoEnabled);
        }

        try
        {
            Assert.Equal(1, await scheduler.RunOnceAsync(CancellationToken.None));
            // Lo que ya está pedido no se vuelve a pedir en la pasada siguiente.
            Assert.Equal(0, await scheduler.RunOnceAsync(CancellationToken.None));
            await DrainAsync();
            Assert.Equal(1, fixture.Analyzer.Calls);

            await using AsyncServiceScope onScope = fixture.Scope(on.Organization);
            Assert.Equal(
                MediaProcessingStatus.Ready,
                (await onScope.ServiceProvider.GetRequiredService<AnalysisService>().GetAsync(on.Assets[0], CancellationToken.None)).Status);
            await using AsyncServiceScope offScope = fixture.Scope(off.Organization);
            Assert.Null((await offScope.ServiceProvider.GetRequiredService<AnalysisService>().GetAsync(off.Assets[0], CancellationToken.None)).Status);
        }
        finally
        {
            // Queda apagado para que no analice videos de otros tests.
            await using AsyncServiceScope scope = fixture.Scope(on.Organization);
            await scope.ServiceProvider.GetRequiredService<AnalysisService>().SetAutoAnalyzeAsync(false, CancellationToken.None);
        }
    }

    [Fact]
    public async Task Los_tramos_narrativos_abarcan_varias_escenas_se_reemplazan_al_reanalizar_y_se_pueden_buscar()
    {
        Scenario scenario = await ArrangeAsync(1);
        fixture.Embeddings.Reset();
        fixture.Embeddings.IsConfigured = false;
        await using AsyncServiceScope scope = fixture.Scope(scenario.Organization);
        AnalysisService analysis = scope.ServiceProvider.GetRequiredService<AnalysisService>();
        SearchService search = scope.ServiceProvider.GetRequiredService<SearchService>();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        long asset = scenario.Assets[0];
        SceneList scenes = await scope.ServiceProvider.GetRequiredService<SceneService>().ListAsync(asset, CancellationToken.None);

        // El segundo se superpone con el primero y el tercero no existe: se descartan.
        fixture.Analyzer.Narratives =
        [
            new NarrativeAnalysis(1, 2, CreativeFunction.Problem, "Plantea el problema de las uñas quebradizas"),
            new NarrativeAnalysis(2, 3, CreativeFunction.Offer, "Superpuesto"),
            new NarrativeAnalysis(3, 3, CreativeFunction.Cta, "Invita a comprar"),
            new NarrativeAnalysis(4, 9, CreativeFunction.Closing, "Fuera de rango"),
        ];
        await analysis.RequestAsync(asset, CancellationToken.None);
        await DrainAsync();

        AnalysisView view = await analysis.GetAsync(asset, CancellationToken.None);
        Assert.Equal(3, view.Items.Count);
        Assert.Equal(2, view.Narratives.Count);
        Assert.Equal(CreativeFunction.Problem, view.Narratives[0].Function);
        Assert.Equal(scenes.Segments[0].StartMs, view.Narratives[0].StartMs);
        Assert.Equal(scenes.Segments[1].EndMs, view.Narratives[0].EndMs);
        Assert.Equal("Invita a comprar", view.Narratives[1].Summary);
        // La franja de escenas no cambia: los tramos narrativos no son cortes.
        Assert.Equal(3, (await scope.ServiceProvider.GetRequiredService<SceneService>().ListAsync(asset, CancellationToken.None)).Segments.Count);

        SearchResult narratives = await search.SearchAsync(new SegmentSearchFilter { Kind = MediaSegmentKind.Narrative }, CancellationToken.None);
        Assert.Equal(2, narratives.Hits.Count);
        Assert.All(narratives.Hits, h => Assert.Equal(MediaSegmentKind.Narrative, h.Kind));
        Assert.Equal(3, (await search.SearchAsync(new SegmentSearchFilter { Kind = MediaSegmentKind.Visual }, CancellationToken.None)).Hits.Count);
        Assert.Equal(5, (await search.SearchAsync(new SegmentSearchFilter(), CancellationToken.None)).Hits.Count);
        SearchHit byWords = Assert.Single((await search.SearchAsync(new SegmentSearchFilter { Query = "quebradizas" }, CancellationToken.None)).Hits);
        Assert.Equal(view.Narratives[0].SegmentId, byWords.SegmentId);

        // La función escrita al principio de la consulta se vuelve filtro y se informa.
        SearchResult interpreted = await search.SearchAsync(new SegmentSearchFilter { Query = "CTA de menos de 60 segundos" }, CancellationToken.None);
        Assert.Equal(SearchMode.Filters, interpreted.Mode);
        Assert.Equal(2, interpreted.Interpreted.Count);
        Assert.Equal(view.Narratives[1].SegmentId, Assert.Single(interpreted.Hits).SegmentId);
        // Lo elegido en el formulario manda sobre lo deducido.
        SearchResult explicitFilter = await search.SearchAsync(
            new SegmentSearchFilter { Query = "CTA", Function = CreativeFunction.Problem }, CancellationToken.None);
        Assert.Empty(explicitFilter.Interpreted);
        // El tramo "Problema" y la primera escena, que el proveedor falso también clasifica así; el CTA no.
        Assert.Equal(
            new[] { view.Narratives[0].SegmentId, scenes.Segments[0].Id }.Order(), explicitFilter.Hits.Select(h => h.SegmentId).Order());

        // Reanalizar con otras escenas reemplaza los tramos: no se acumulan.
        await scope.ServiceProvider.GetRequiredService<SegmentEditService>().MoveBoundaryAsync(scenes.Segments[0].Id, 1500, CancellationToken.None);
        fixture.Analyzer.Narratives = [new NarrativeAnalysis(1, 3, CreativeFunction.Demonstration, "Todo el anuncio")];
        await analysis.RequestAsync(asset, CancellationToken.None);
        await DrainAsync();

        NarrativeItem only = Assert.Single((await analysis.GetAsync(asset, CancellationToken.None)).Narratives);
        Assert.Equal(CreativeFunction.Demonstration, only.Function);
        Assert.Equal(1, await db.MediaSegments.CountAsync(s => s.Kind == MediaSegmentKind.Narrative));
        Assert.Equal(3, await db.MediaSegments.CountAsync(s => s.Kind == MediaSegmentKind.Visual));
    }

    [Fact]
    public async Task El_filtro_de_audio_del_buscador_coincide_con_la_clasificacion_de_cada_escena()
    {
        Scenario scenario = await ArrangeAsync(1);
        await using AsyncServiceScope scope = fixture.Scope(scenario.Organization);
        SearchService search = scope.ServiceProvider.GetRequiredService<SearchService>();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Niveles fijados a mano en el borde de cada umbral: el filtro en la base debe clasificar igual que la regla.
        List<MediaSegment> segments = await db.MediaSegments.OrderBy(s => s.StartMs).ToListAsync();
        (decimal Mean, decimal Peak)[] levels = [(-50.1m, -40m), (-30m, -3m), (-30.1m, -0.6m)];
        for (int i = 0; i < segments.Count; i++)
        {
            segments[i].AudioMeanDb = levels[i].Mean;
            segments[i].AudioPeakDb = levels[i].Peak;
        }

        await db.SaveChangesAsync();

        foreach (AudioQuality quality in Enum.GetValues<AudioQuality>())
        {
            SearchResult result = await search.SearchAsync(new SegmentSearchFilter { Audio = quality }, CancellationToken.None);
            List<long> expected = segments.Where(s => MediaRules.ClassifyAudio(s.AudioMeanDb, s.AudioPeakDb) == quality).Select(s => s.Id).ToList();
            Assert.Equal(expected.Order(), result.Hits.Select(h => h.SegmentId).Order());
            Assert.All(result.Hits, h => Assert.Equal(quality, h.AudioQuality));
        }

        Assert.Single((await search.SearchAsync(new SegmentSearchFilter { Audio = AudioQuality.Silent }, CancellationToken.None)).Hits);
        Assert.Single((await search.SearchAsync(new SegmentSearchFilter { Audio = AudioQuality.Good }, CancellationToken.None)).Hits);
        Assert.Single((await search.SearchAsync(new SegmentSearchFilter { Audio = AudioQuality.Low }, CancellationToken.None)).Hits);

        segments[2].AudioPeakDb = -0.5m;
        await db.SaveChangesAsync();
        Assert.Equal(segments[2].Id, Assert.Single((await search.SearchAsync(new SegmentSearchFilter { Audio = AudioQuality.Clipped }, CancellationToken.None)).Hits).SegmentId);
    }

    // Una organización nueva con uno o dos videos reales con escenas detectadas y sin analizar.
    private async Task<Scenario> ArrangeAsync(int videos, bool keepJobs = false)
    {
        if (!keepJobs)
        {
            await fixture.ClearJobsAsync();
            fixture.Analyzer.Reset();
        }

        int org = await fixture.NewOrganizationAsync();
        await using AsyncServiceScope scope = fixture.Scope(org);
        MediaLibraryService media = scope.ServiceProvider.GetRequiredService<MediaLibraryService>();
        List<long> assets = [];
        string[] samples = [fixture.SampleCutsVideoPath, fixture.SampleVideoPath];
        foreach (string path in samples.Take(videos))
        {
            await using FileStream stream = File.OpenRead(path);
            assets.Add((await media.IngestAsync(Path.GetFileName(path), new MediaRightsBody(), stream, CancellationToken.None)).AssetId);
        }

        await DrainAsync();
        return new Scenario(org, assets);
    }

    private async Task DrainAsync()
    {
        JobRunner runner = fixture.Services.GetRequiredService<JobRunner>();
        while (await runner.ProcessNextAsync(CancellationToken.None))
        {
        }
    }

    private sealed record Scenario(int Organization, List<long> Assets);
}

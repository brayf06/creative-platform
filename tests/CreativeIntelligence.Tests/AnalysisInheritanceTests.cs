using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CreativeIntelligence.Tests;

/// <summary>Un duplicado visual hereda el análisis de su original. Usa el video de cortes y su copia recodificada.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class AnalysisInheritanceTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Un_duplicado_visual_hereda_el_analisis_del_original_sin_llamar_al_proveedor_ni_necesitar_la_clave()
    {
        await fixture.ClearJobsAsync();
        fixture.Analyzer.Reset();
        fixture.Embeddings.Reset();
        int org = await fixture.NewOrganizationAsync();
        await using AsyncServiceScope scope = fixture.Scope(org);
        MediaLibraryService media = scope.ServiceProvider.GetRequiredService<MediaLibraryService>();
        AnalysisService analysis = scope.ServiceProvider.GetRequiredService<AnalysisService>();
        SceneService scenes = scope.ServiceProvider.GetRequiredService<SceneService>();
        long original = await IngestAsync(media, "anuncio.mp4", fixture.SampleCutsVideoPath);
        long copy = await IngestAsync(media, "anuncio para whatsapp.mp4", fixture.SampleRecodedVideoPath);
        await DrainAsync();
        Assert.Equal(original, (await media.GetAsync(copy, CancellationToken.None)).DuplicateOfAssetId);

        // Mientras el original no esté analizado no hay nada que heredar: la copia se estimaría con costo.
        AnalysisEstimate before = await analysis.EstimateAsync(copy, CancellationToken.None);
        Assert.Null(before.InheritsFromAssetId);
        Assert.True(before.CostUsd > 0);

        await analysis.RequestAsync(original, CancellationToken.None);
        await DrainAsync();
        Assert.Equal(1, fixture.Analyzer.Calls);
        long firstOriginalSegment = (await scenes.ListAsync(original, CancellationToken.None)).Segments[0].Id;
        await scope.ServiceProvider.GetRequiredService<SegmentEditService>().SetTagsAsync(firstOriginalSegment, "uñas, hook, favorita", CancellationToken.None);
        long firstCopySegment = (await scenes.ListAsync(copy, CancellationToken.None)).Segments[0].Id;
        await scope.ServiceProvider.GetRequiredService<SegmentEditService>().SetTagsAsync(firstCopySegment, "Favorita, propia", CancellationToken.None);
        await DrainAsync();

        AnalysisEstimate estimate = await analysis.EstimateAsync(copy, CancellationToken.None);
        Assert.Equal(original, estimate.InheritsFromAssetId);
        Assert.Equal(0m, estimate.CostUsd);

        // Heredar no llama al proveedor: funciona aunque no haya clave.
        fixture.Analyzer.IsConfigured = false;
        await analysis.RequestAsync(copy, CancellationToken.None);
        await DrainAsync();

        AnalysisView inherited = await analysis.GetAsync(copy, CancellationToken.None);
        AnalysisView source = await analysis.GetAsync(original, CancellationToken.None);
        Assert.Equal(MediaProcessingStatus.Ready, inherited.Status);
        Assert.Equal(3, inherited.Items.Count);
        Assert.All(inherited.Items, i => Assert.True(i.Inherited));
        Assert.All(inherited.Items, i => Assert.False(i.IsStale));
        Assert.All(source.Items, i => Assert.False(i.Inherited));
        Assert.Equal(source.Items.Select(i => i.VisualDescription), inherited.Items.Select(i => i.VisualDescription));
        Assert.Equal(source.Items.Select(i => i.Functions), inherited.Items.Select(i => i.Functions));
        Assert.Equal(source.Items.Select(i => i.Model), inherited.Items.Select(i => i.Model));
        Assert.Empty(inherited.Items.Select(i => i.SegmentId).Intersect(source.Items.Select(i => i.SegmentId)));

        Assert.Equal(1, fixture.Analyzer.Calls);
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.AiUsageRecords.CountAsync(u => u.Task == "scene.analysis"));

        // Las etiquetas del original llegan como automáticas, sin duplicar la que la persona ya había puesto.
        SegmentItem firstCopy = (await scenes.ListAsync(copy, CancellationToken.None)).Segments[0];
        Assert.Equal(["Favorita", "hook", "propia", "uñas"], firstCopy.Tags);
        List<string> manual = await db.MediaSegmentTags.Where(t => t.MediaSegmentId == firstCopySegment && t.Origin == MediaSegmentOrigin.Manual)
            .OrderBy(t => t.Text).Select(t => t.Text).ToListAsync();
        Assert.Equal(["Favorita", "propia"], manual);

        // El heredado entra al índice de búsqueda como cualquier otro.
        SearchResult found = await scope.ServiceProvider.GetRequiredService<SearchService>()
            .SearchAsync(new SegmentSearchFilter { Query = "Descripción de la escena 2" }, CancellationToken.None);
        Assert.Contains(found.Hits, h => h.AssetId == copy);
    }

    [Theory]
    // Mismos cortes en una copia un 2 % más larga: cada tramo hereda del suyo.
    [InlineData(new[] { 0, 2040, 2040, 4080, 4080, 5610 }, 5610, new[] { 0, 2000, 2000, 4000, 4000, 5500 }, 5500, new[] { 0, 1, 2 })]
    // La copia detectó un corte de más: sus dos mitades heredan del mismo tramo del original.
    [InlineData(new[] { 0, 1000, 1000, 2000, 2000, 4000 }, 4000, new[] { 0, 2000, 2000, 4000 }, 4000, new[] { 0, 0, 1 })]
    // La copia detectó un corte de menos: su tramo largo hereda del que más cubre.
    [InlineData(new[] { 0, 4000 }, 4000, new[] { 0, 1000, 1000, 4000 }, 4000, new[] { 1 })]
    // Ningún tramo del original cubre la mitad del de destino: no hereda.
    [InlineData(new[] { 0, 4000 }, 4000, new[] { 0, 1000, 1000, 2000, 2000, 3000, 3000, 4000 }, 4000, new[] { -1 })]
    [InlineData(new[] { 0, 4000 }, 4000, new int[0], 4000, new[] { -1 })]
    public void Cada_tramo_hereda_del_tramo_del_original_con_el_que_mas_se_superpone(
        int[] targets, int targetDuration, int[] sources, int sourceDuration, int[] expected)
    {
        List<TimeRange> targetRanges = targets.Chunk(2).Select((r, i) => new TimeRange(i, r[0], r[1])).ToList();
        List<TimeRange> sourceRanges = sources.Chunk(2).Select((r, i) => new TimeRange(i, r[0], r[1])).ToList();

        Assert.Equal(expected, AnalysisService.MatchRanges(targetRanges, targetDuration, sourceRanges, sourceDuration));
    }

    private static async Task<long> IngestAsync(MediaLibraryService media, string fileName, string sourcePath)
    {
        await using FileStream stream = File.OpenRead(sourcePath);
        return (await media.IngestAsync(fileName, new MediaRightsBody(), stream, CancellationToken.None)).AssetId;
    }

    private async Task DrainAsync()
    {
        JobRunner runner = fixture.Services.GetRequiredService<JobRunner>();
        while (await runner.ProcessNextAsync(CancellationToken.None))
        {
        }
    }
}

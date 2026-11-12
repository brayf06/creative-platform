using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CreativeIntelligence.Tests;

/// <summary>Edición manual de escenas. Los fragmentos de partida se insertan directo (0–2000, 2000–4000,
/// 4000–5500) para que cada test sea rápido; el último test usa el pipeline real.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class SegmentEditTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Mover_un_corte_cambia_el_fin_de_un_fragmento_y_el_inicio_del_siguiente()
    {
        Scenario scenario = await ArrangeAsync();
        await using AsyncServiceScope scope = fixture.Scope(scenario.Organization);
        SegmentEditService editor = scope.ServiceProvider.GetRequiredService<SegmentEditService>();

        await editor.MoveBoundaryAsync(scenario.Segments[0], 1500, CancellationToken.None);

        List<SegmentItem> segments = await ListAsync(scenario);
        Assert.Equal([0, 1500, 1500, 4000, 4000, 5500], Ranges(segments));
        Assert.Equal([MediaSegmentOrigin.Manual, MediaSegmentOrigin.Manual, MediaSegmentOrigin.Detected], segments.Select(s => s.Origin));
        Assert.False(segments[0].HasThumbnail);

        await JobTests.AssertBusinessAsync(400, "BOUNDARY_OUT_OF_RANGE", () => editor.MoveBoundaryAsync(scenario.Segments[0], 50, CancellationToken.None));
        await JobTests.AssertBusinessAsync(400, "BOUNDARY_OUT_OF_RANGE", () => editor.MoveBoundaryAsync(scenario.Segments[0], 3950, CancellationToken.None));
        await JobTests.AssertBusinessAsync(409, "NO_NEXT_SEGMENT", () => editor.MoveBoundaryAsync(scenario.Segments[2], 5000, CancellationToken.None));
        Assert.Equal([0, 1500, 1500, 4000, 4000, 5500], Ranges(await ListAsync(scenario)));
    }

    [Fact]
    public async Task Dividir_un_fragmento_crea_dos_contiguos_y_fusionarlos_vuelve_a_uno_con_las_etiquetas_de_ambos()
    {
        Scenario scenario = await ArrangeAsync();
        await using AsyncServiceScope scope = fixture.Scope(scenario.Organization);
        SegmentEditService editor = scope.ServiceProvider.GetRequiredService<SegmentEditService>();
        await editor.SetTagsAsync(scenario.Segments[1], "hook", CancellationToken.None);

        await JobTests.AssertBusinessAsync(400, "BOUNDARY_OUT_OF_RANGE", () => editor.SplitAsync(scenario.Segments[1], 2050, CancellationToken.None));
        await editor.SplitAsync(scenario.Segments[1], 3000, CancellationToken.None);

        List<SegmentItem> split = await ListAsync(scenario);
        Assert.Equal([0, 2000, 2000, 3000, 3000, 4000, 4000, 5500], Ranges(split));
        Assert.Equal(["hook"], split[1].Tags);
        Assert.Empty(split[2].Tags);

        await editor.SetTagsAsync(split[2].Id, "demostración, Hook", CancellationToken.None);
        await editor.MergeWithNextAsync(scenario.Segments[1], CancellationToken.None);

        List<SegmentItem> merged = await ListAsync(scenario);
        Assert.Equal([0, 2000, 2000, 4000, 4000, 5500], Ranges(merged));
        Assert.Equal(["demostración", "hook"], merged[1].Tags);
        await JobTests.AssertBusinessAsync(409, "NO_NEXT_SEGMENT", () => editor.MergeWithNextAsync(scenario.Segments[2], CancellationToken.None));
    }

    [Fact]
    public async Task Las_etiquetas_se_reemplazan_sin_repetidos_y_con_limites()
    {
        Scenario scenario = await ArrangeAsync();
        await using AsyncServiceScope scope = fixture.Scope(scenario.Organization);
        SegmentEditService editor = scope.ServiceProvider.GetRequiredService<SegmentEditService>();

        await editor.SetTagsAsync(scenario.Segments[0], " uñas , Uñas, practicidad ,, ", CancellationToken.None);
        Assert.Equal(["practicidad", "uñas"], (await ListAsync(scenario))[0].Tags);

        await editor.SetTagsAsync(scenario.Segments[0], "practicidad, resultado", CancellationToken.None);
        Assert.Equal(["practicidad", "resultado"], (await ListAsync(scenario))[0].Tags);

        await JobTests.AssertBusinessAsync(400, "TAGS_INVALID",
            () => editor.SetTagsAsync(scenario.Segments[0], new string('x', 51), CancellationToken.None));
        await JobTests.AssertBusinessAsync(400, "TAGS_INVALID",
            () => editor.SetTagsAsync(scenario.Segments[0], string.Join(",", Enumerable.Range(1, 21)), CancellationToken.None));

        await editor.SetTagsAsync(scenario.Segments[0], null, CancellationToken.None);
        Assert.Empty((await ListAsync(scenario))[0].Tags);
    }

    [Fact]
    public async Task Solo_se_elige_para_produccion_un_fragmento_de_un_video_con_autorizacion_vigente()
    {
        Scenario scenario = await ArrangeAsync();
        await using AsyncServiceScope scope = fixture.Scope(scenario.Organization);
        SegmentEditService editor = scope.ServiceProvider.GetRequiredService<SegmentEditService>();
        MediaLibraryService media = scope.ServiceProvider.GetRequiredService<MediaLibraryService>();
        SceneService scenes = scope.ServiceProvider.GetRequiredService<SceneService>();

        Assert.False((await scenes.ListAsync(scenario.Asset, CancellationToken.None)).CanSelect);
        await JobTests.AssertBusinessAsync(409, "REUSE_NOT_AUTHORIZED",
            () => editor.SetSelectedAsync(scenario.Segments[0], true, CancellationToken.None));

        await media.UpdateRightsAsync(
            scenario.Asset, new MediaRightsBody { ReuseAuthorized = true, RightsExpireOn = new DateOnly(2020, 1, 1) }, CancellationToken.None);
        Assert.False((await scenes.ListAsync(scenario.Asset, CancellationToken.None)).CanSelect);
        await JobTests.AssertBusinessAsync(409, "REUSE_NOT_AUTHORIZED",
            () => editor.SetSelectedAsync(scenario.Segments[0], true, CancellationToken.None));

        await media.UpdateRightsAsync(
            scenario.Asset, new MediaRightsBody { ReuseAuthorized = true, RightsExpireOn = new DateOnly(2999, 1, 1) }, CancellationToken.None);
        Assert.True((await scenes.ListAsync(scenario.Asset, CancellationToken.None)).CanSelect);
        await editor.SetSelectedAsync(scenario.Segments[0], true, CancellationToken.None);
        Assert.True((await ListAsync(scenario))[0].SelectedForProduction);

        await editor.SetSelectedAsync(scenario.Segments[0], false, CancellationToken.None);
        Assert.False((await ListAsync(scenario))[0].SelectedForProduction);
    }

    [Fact]
    public async Task Dos_ediciones_simultaneas_sobre_el_mismo_video_no_se_aplican_las_dos()
    {
        Scenario scenario = await ArrangeAsync();
        await using AsyncServiceScope scope1 = fixture.Scope(scenario.Organization);
        await using AsyncServiceScope scope2 = fixture.Scope(scenario.Organization);
        SegmentEditService editor1 = scope1.ServiceProvider.GetRequiredService<SegmentEditService>();
        SegmentEditService editor2 = scope2.ServiceProvider.GetRequiredService<SegmentEditService>();

        Task first = editor1.SplitAsync(scenario.Segments[0], 1000, CancellationToken.None);
        Task second = editor2.SplitAsync(scenario.Segments[1], 3000, CancellationToken.None);
        Exception?[] outcomes = await Task.WhenAll(CaptureAsync(first), CaptureAsync(second));

        List<SegmentItem> segments = await ListAsync(scenario);
        int failures = outcomes.Count(o => o is not null);
        Assert.Equal(3 + (2 - failures), segments.Count);
        Assert.All(outcomes.Where(o => o is not null), o => Assert.Equal("SEGMENTS_CHANGED", Assert.IsType<BusinessException>(o).Code));
        for (int i = 1; i < segments.Count; i++)
        {
            Assert.Equal(segments[i - 1].EndMs, segments[i].StartMs);
        }

        // Con la versión ya vieja, una edición preparada antes de otra falla siempre.
        await using AsyncServiceScope stale = fixture.Scope(scenario.Organization);
        AppDbContext db = stale.ServiceProvider.GetRequiredService<AppDbContext>();
        int version = await db.MediaAssets.Where(a => a.Id == scenario.Asset).Select(a => a.SegmentsVersion).SingleAsync();
        Assert.Equal(2 - failures, version);
    }

    [Fact]
    public async Task Los_fragmentos_de_otra_organizacion_no_se_pueden_editar_y_cada_edicion_queda_auditada()
    {
        Scenario scenario = await ArrangeAsync();
        int other = await fixture.NewOrganizationAsync();
        await using (AsyncServiceScope otherScope = fixture.Scope(other))
        {
            SegmentEditService foreign = otherScope.ServiceProvider.GetRequiredService<SegmentEditService>();
            long id = scenario.Segments[0];
            await JobTests.AssertBusinessAsync(404, "SEGMENT_NOT_FOUND", () => foreign.MoveBoundaryAsync(id, 1500, CancellationToken.None));
            await JobTests.AssertBusinessAsync(404, "SEGMENT_NOT_FOUND", () => foreign.SplitAsync(id, 1000, CancellationToken.None));
            await JobTests.AssertBusinessAsync(404, "SEGMENT_NOT_FOUND", () => foreign.MergeWithNextAsync(id, CancellationToken.None));
            await JobTests.AssertBusinessAsync(404, "SEGMENT_NOT_FOUND", () => foreign.SetTagsAsync(id, "x", CancellationToken.None));
            await JobTests.AssertBusinessAsync(404, "SEGMENT_NOT_FOUND", () => foreign.SetSelectedAsync(id, false, CancellationToken.None));
        }

        await using AsyncServiceScope scope = fixture.Scope(scenario.Organization);
        SegmentEditService editor = scope.ServiceProvider.GetRequiredService<SegmentEditService>();
        await editor.MoveBoundaryAsync(scenario.Segments[0], 1500, CancellationToken.None);
        await editor.SetTagsAsync(scenario.Segments[0], "hook", CancellationToken.None);

        AuditService audit = scope.ServiceProvider.GetRequiredService<AuditService>();
        List<string> actions = (await audit.ListAsync(new PageRequest(), CancellationToken.None)).Items.Select(e => e.Action).ToList();
        Assert.Contains("segment.move", actions);
        Assert.Contains("segment.tags", actions);
        Assert.Equal([0, 1500, 1500, 4000, 4000, 5500], Ranges(await ListAsync(scenario)));
    }

    [Fact]
    public async Task Con_el_pipeline_real_una_edicion_regenera_la_miniatura_y_redetectar_exige_confirmar_el_descarte()
    {
        await fixture.ClearJobsAsync();
        int org = await fixture.NewOrganizationAsync();
        JobRunner runner = fixture.Services.GetRequiredService<JobRunner>();
        long assetId;
        await using (AsyncServiceScope ingest = fixture.Scope(org))
        {
            await using FileStream stream = File.OpenRead(fixture.SampleCutsVideoPath);
            MediaLibraryService media = ingest.ServiceProvider.GetRequiredService<MediaLibraryService>();
            assetId = (await media.IngestAsync("cortes.mp4", new MediaRightsBody(), stream, CancellationToken.None)).AssetId;
        }

        await DrainAsync(runner);
        Scenario scenario = new(org, assetId, []);
        List<SegmentItem> detected = await ListAsync(scenario);
        Assert.Equal(3, detected.Count);

        await using AsyncServiceScope scope = fixture.Scope(org);
        SegmentEditService editor = scope.ServiceProvider.GetRequiredService<SegmentEditService>();
        SceneService scenes = scope.ServiceProvider.GetRequiredService<SceneService>();
        await editor.SplitAsync(detected[1].Id, 3000, CancellationToken.None);
        await editor.SetTagsAsync(detected[1].Id, "hook", CancellationToken.None);
        Assert.Contains(await ListAsync(scenario), s => !s.HasThumbnail);

        await DrainAsync(runner);
        List<SegmentItem> edited = await ListAsync(scenario);
        Assert.Equal(4, edited.Count);
        Assert.All(edited, s => Assert.True(s.HasThumbnail));

        // Sin confirmación, las correcciones manuales se conservan.
        await JobTests.AssertBusinessAsync(409, "HAS_MANUAL_EDITS", () => scenes.RequestDetectionAsync(assetId, false, CancellationToken.None));
        Assert.Equal(4, (await ListAsync(scenario)).Count);

        await scenes.RequestDetectionAsync(assetId, true, CancellationToken.None);
        await DrainAsync(runner);
        List<SegmentItem> redetected = await ListAsync(scenario);
        Assert.Equal(3, redetected.Count);
        Assert.All(redetected, s => Assert.Equal(MediaSegmentOrigin.Detected, s.Origin));
        Assert.All(redetected, s => Assert.Empty(s.Tags));
    }

    private async Task<Scenario> ArrangeAsync()
    {
        int org = await fixture.NewOrganizationAsync();
        await using AsyncServiceScope scope = fixture.Scope(org);
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        MediaAsset asset = new()
        {
            Kind = MediaAssetKind.Video,
            OriginalFileName = "video.mp4",
            ContentType = "video/mp4",
            SizeBytes = 1,
            Sha256 = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
            StoragePath = "no-existe",
            ProcessingStatus = MediaProcessingStatus.Ready,
            SceneStatus = MediaProcessingStatus.Ready,
            DurationMs = 5500,
        };
        db.MediaAssets.Add(asset);
        await db.SaveChangesAsync();
        int[][] ranges = [[0, 2000], [2000, 4000], [4000, 5500]];
        List<MediaSegment> segments = ranges
            .Select(r => new MediaSegment
            {
                MediaAssetId = asset.Id,
                Kind = MediaSegmentKind.Visual,
                Origin = MediaSegmentOrigin.Detected,
                StartMs = r[0],
                EndMs = r[1],
                ThumbnailPath = "no-existe.jpg",
            })
            .ToList();
        db.MediaSegments.AddRange(segments);
        await db.SaveChangesAsync();
        return new Scenario(org, asset.Id, segments.Select(s => s.Id).ToList());
    }

    private async Task<List<SegmentItem>> ListAsync(Scenario scenario)
    {
        await using AsyncServiceScope scope = fixture.Scope(scenario.Organization);
        return (await scope.ServiceProvider.GetRequiredService<SceneService>().ListAsync(scenario.Asset, CancellationToken.None)).Segments;
    }

    private static IEnumerable<int> Ranges(List<SegmentItem> segments)
    {
        return segments.SelectMany(s => new[] { s.StartMs, s.EndMs });
    }

    private static async Task<Exception?> CaptureAsync(Task task)
    {
        try
        {
            await task;
            return null;
        }
        catch (BusinessException exception)
        {
            return exception;
        }
    }

    private static async Task DrainAsync(JobRunner runner)
    {
        while (await runner.ProcessNextAsync(CancellationToken.None))
        {
        }
    }

    private sealed record Scenario(int Organization, long Asset, List<long> Segments);
}

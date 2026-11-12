using Microsoft.Extensions.DependencyInjection;

namespace CreativeIntelligence.Tests;

/// <summary>Detección de escenas con PySceneDetect y FFmpeg reales sobre clips con cortes conocidos.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class SceneTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Un_video_con_cortes_queda_dividido_en_escenas_contiguas_con_miniatura()
    {
        await fixture.ClearJobsAsync();
        int org = await fixture.NewOrganizationAsync();
        long id = await IngestAsync(org, "cortes.mp4", fixture.SampleCutsVideoPath);
        JobRunner runner = fixture.Services.GetRequiredService<JobRunner>();

        // Primero el análisis técnico, que al terminar encola la detección de escenas.
        Assert.True(await runner.ProcessNextAsync(CancellationToken.None));
        Assert.Equal(MediaProcessingStatus.Pending, (await ListAsync(org, id)).Status);
        Assert.True(await runner.ProcessNextAsync(CancellationToken.None));
        Assert.False(await runner.ProcessNextAsync(CancellationToken.None));

        SceneList scenes = await ListAsync(org, id);
        Assert.Equal(MediaProcessingStatus.Ready, scenes.Status);
        Assert.Equal(3, scenes.Segments.Count);
        Assert.Equal(0, scenes.Segments[0].StartMs);
        Assert.InRange(scenes.Segments[0].EndMs, 1900, 2100);
        Assert.InRange(scenes.Segments[1].EndMs, 3900, 4100);
        Assert.Equal(scenes.DurationMs, scenes.Segments[2].EndMs);
        for (int i = 1; i < scenes.Segments.Count; i++)
        {
            Assert.Equal(scenes.Segments[i - 1].EndMs, scenes.Segments[i].StartMs);
        }

        Assert.All(scenes.Segments, s => Assert.Equal(MediaSegmentKind.Visual, s.Kind));
        Assert.All(scenes.Segments, s => Assert.Equal(MediaSegmentOrigin.Detected, s.Origin));
        Assert.All(scenes.Segments, s => Assert.True(s.HasThumbnail));

        await using AsyncServiceScope scope = fixture.Scope(org);
        SceneService service = scope.ServiceProvider.GetRequiredService<SceneService>();
        MediaContent thumbnail = await service.OpenThumbnailAsync(scenes.Segments[1].Id, CancellationToken.None);
        await using Stream stream = thumbnail.Content;
        Assert.Equal(0xFF, stream.ReadByte());
        Assert.Equal(0xD8, stream.ReadByte());
    }

    [Fact]
    public async Task Un_video_sin_cortes_es_una_sola_escena_de_toda_su_duracion()
    {
        await fixture.ClearJobsAsync();
        int org = await fixture.NewOrganizationAsync();
        long id = await IngestAsync(org, "plano.mp4", fixture.SampleVideoPath);
        JobRunner runner = fixture.Services.GetRequiredService<JobRunner>();
        await runner.ProcessNextAsync(CancellationToken.None);
        await runner.ProcessNextAsync(CancellationToken.None);

        SceneList scenes = await ListAsync(org, id);
        SegmentItem only = Assert.Single(scenes.Segments);
        Assert.Equal(0, only.StartMs);
        Assert.Equal(scenes.DurationMs, only.EndMs);
    }

    [Fact]
    public async Task Volver_a_detectar_reemplaza_las_escenas_en_lugar_de_duplicarlas()
    {
        await fixture.ClearJobsAsync();
        int org = await fixture.NewOrganizationAsync();
        long id = await IngestAsync(org, "cortes.mp4", fixture.SampleCutsVideoPath);
        JobRunner runner = fixture.Services.GetRequiredService<JobRunner>();
        await runner.ProcessNextAsync(CancellationToken.None);
        await runner.ProcessNextAsync(CancellationToken.None);
        long[] firstIds = (await ListAsync(org, id)).Segments.Select(s => s.Id).ToArray();

        await using (AsyncServiceScope scope = fixture.Scope(org))
        {
            await scope.ServiceProvider.GetRequiredService<SceneService>().RequestDetectionAsync(id, false, CancellationToken.None);
        }

        Assert.Equal(MediaProcessingStatus.Pending, (await ListAsync(org, id)).Status);
        Assert.True(await runner.ProcessNextAsync(CancellationToken.None));

        SceneList scenes = await ListAsync(org, id);
        Assert.Equal(MediaProcessingStatus.Ready, scenes.Status);
        Assert.Equal(3, scenes.Segments.Count);
        Assert.Empty(scenes.Segments.Select(s => s.Id).Intersect(firstIds));
    }

    [Fact]
    public async Task Las_escenas_no_se_piden_para_imagenes_ni_videos_sin_analizar_ni_desde_otra_organizacion()
    {
        await fixture.ClearJobsAsync();
        int org = await fixture.NewOrganizationAsync();
        int other = await fixture.NewOrganizationAsync();
        long imageId = await IngestAsync(org, "producto.png", fixture.SampleImagePath);
        long videoId = await IngestAsync(org, "cortes.mp4", fixture.SampleCutsVideoPath);

        await using (AsyncServiceScope scope = fixture.Scope(org))
        {
            SceneService service = scope.ServiceProvider.GetRequiredService<SceneService>();
            await JobTests.AssertBusinessAsync(409, "NOT_A_VIDEO", () => service.RequestDetectionAsync(imageId, false, CancellationToken.None));
            await JobTests.AssertBusinessAsync(409, "MEDIA_NOT_READY", () => service.RequestDetectionAsync(videoId, false, CancellationToken.None));
        }

        JobRunner runner = fixture.Services.GetRequiredService<JobRunner>();
        while (await runner.ProcessNextAsync(CancellationToken.None))
        {
        }

        Assert.Null((await ListAsync(org, imageId)).Status);
        Assert.Empty((await ListAsync(org, imageId)).Segments);
        long segmentId = (await ListAsync(org, videoId)).Segments[0].Id;

        await using AsyncServiceScope otherScope = fixture.Scope(other);
        SceneService otherService = otherScope.ServiceProvider.GetRequiredService<SceneService>();
        await JobTests.AssertBusinessAsync(404, "MEDIA_NOT_FOUND", () => otherService.ListAsync(videoId, CancellationToken.None));
        await JobTests.AssertBusinessAsync(404, "MEDIA_NOT_FOUND", () => otherService.RequestDetectionAsync(videoId, false, CancellationToken.None));
        await JobTests.AssertBusinessAsync(404, "SEGMENT_NOT_FOUND", () => otherService.OpenThumbnailAsync(segmentId, CancellationToken.None));
    }

    [Theory]
    [InlineData(new[] { 0, 2000, 2000, 4000, 4000, 5500 }, 5500, new[] { 0, 2000, 2000, 4000, 4000, 5500 })]
    [InlineData(new int[0], 3000, new[] { 0, 3000 })]
    [InlineData(new[] { 0, 2000, 2000, 5480 }, 5500, new[] { 0, 2000, 2000, 5500 })]
    [InlineData(new[] { 0, 2000, 2000, 9000 }, 5500, new[] { 0, 2000, 2000, 5500 })]
    [InlineData(new[] { 100, 2000, 2100, 4000 }, 4000, new[] { 0, 2000, 2000, 4000 })]
    [InlineData(new[] { 0, 2000, 6000, 7000 }, 5500, new[] { 0, 2000, 2000, 5500 })]
    public void Los_cortes_detectados_se_normalizan_a_tramos_contiguos_que_cubren_el_video(int[] detected, int durationMs, int[] expected)
    {
        List<SceneBoundary> input = detected.Chunk(2).Select(p => new SceneBoundary(p[0], p[1])).ToList();

        List<SceneBoundary> result = MediaScenesJobHandler.Normalize(input, durationMs);

        Assert.Equal(expected, result.SelectMany(s => new[] { s.StartMs, s.EndMs }));
    }

    private async Task<long> IngestAsync(int organizationId, string fileName, string sourcePath)
    {
        await using AsyncServiceScope scope = fixture.Scope(organizationId);
        MediaLibraryService media = scope.ServiceProvider.GetRequiredService<MediaLibraryService>();
        await using FileStream stream = File.OpenRead(sourcePath);
        return (await media.IngestAsync(fileName, new MediaRightsBody(), stream, CancellationToken.None)).AssetId;
    }

    private async Task<SceneList> ListAsync(int organizationId, long assetId)
    {
        await using AsyncServiceScope scope = fixture.Scope(organizationId);
        return await scope.ServiceProvider.GetRequiredService<SceneService>().ListAsync(assetId, CancellationToken.None);
    }
}

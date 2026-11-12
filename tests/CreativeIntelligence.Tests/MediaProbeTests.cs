using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;

namespace CreativeIntelligence.Tests;

/// <summary>Análisis técnico con FFmpeg real sobre clips generados por el propio FFmpeg al iniciar la corrida.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class MediaProbeTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Un_video_ingresado_queda_con_sus_propiedades_y_su_miniatura()
    {
        await fixture.ClearJobsAsync();
        int org = await fixture.NewOrganizationAsync();
        byte[] original = await File.ReadAllBytesAsync(fixture.SampleVideoPath);
        long id = await IngestAsync(org, "clip vertical.mp4", original);
        Assert.Equal(MediaProcessingStatus.Pending, (await GetAsync(org, id)).ProcessingStatus);

        Assert.True(await fixture.Services.GetRequiredService<JobRunner>().ProcessNextAsync(CancellationToken.None));

        MediaAssetDetail detail = await GetAsync(org, id);
        Assert.Equal(MediaProcessingStatus.Ready, detail.ProcessingStatus);
        Assert.Null(detail.ProcessingError);
        Assert.Equal(320, detail.Width);
        Assert.Equal(568, detail.Height);
        Assert.InRange(detail.DurationMs!.Value, 1900, 2200);
        Assert.Equal(30m, detail.FrameRate);
        Assert.Equal("h264", detail.VideoCodec);
        Assert.Equal("aac", detail.AudioCodec);

        await using AsyncServiceScope scope = fixture.Scope(org);
        MediaLibraryService media = scope.ServiceProvider.GetRequiredService<MediaLibraryService>();
        MediaAssetItem item = Assert.Single((await media.ListAsync(new MediaFilter(), new PageRequest(), CancellationToken.None)).Items);
        Assert.True(item.HasThumbnail);
        Assert.Equal(detail.DurationMs, item.DurationMs);

        byte[] thumbnail = await ReadAsync(await media.OpenThumbnailAsync(id, CancellationToken.None));
        Assert.Equal([0xFF, 0xD8], thumbnail.Take(2));

        // El análisis sólo lee el original: sigue idéntico byte a byte.
        byte[] stored = await ReadAsync(await media.OpenOriginalAsync(id, CancellationToken.None));
        Assert.Equal(SHA256.HashData(original), SHA256.HashData(stored));
    }

    [Fact]
    public async Task Una_imagen_queda_con_resolucion_y_miniatura_y_sin_duracion()
    {
        await fixture.ClearJobsAsync();
        int org = await fixture.NewOrganizationAsync();
        long id = await IngestAsync(org, "producto.png", await File.ReadAllBytesAsync(fixture.SampleImagePath));

        await fixture.Services.GetRequiredService<JobRunner>().ProcessNextAsync(CancellationToken.None);

        MediaAssetDetail detail = await GetAsync(org, id);
        Assert.Equal(MediaProcessingStatus.Ready, detail.ProcessingStatus);
        Assert.Equal(640, detail.Width);
        Assert.Equal(360, detail.Height);
        Assert.Null(detail.DurationMs);
        Assert.Null(detail.FrameRate);
        Assert.Null(detail.AudioCodec);
        Assert.Equal("png", detail.VideoCodec);
    }

    [Fact]
    public async Task Un_archivo_ilegible_queda_fallido_con_el_motivo_y_no_se_reintenta()
    {
        await fixture.ClearJobsAsync();
        int org = await fixture.NewOrganizationAsync();
        long id = await IngestAsync(org, "roto.mp4", "esto no es un video"u8.ToArray());
        JobRunner runner = fixture.Services.GetRequiredService<JobRunner>();

        Assert.True(await runner.ProcessNextAsync(CancellationToken.None));
        Assert.False(await runner.ProcessNextAsync(CancellationToken.None));

        MediaAssetDetail detail = await GetAsync(org, id);
        Assert.Equal(MediaProcessingStatus.Failed, detail.ProcessingStatus);
        Assert.False(string.IsNullOrWhiteSpace(detail.ProcessingError));

        await using AsyncServiceScope scope = fixture.Scope(org);
        JobService jobs = scope.ServiceProvider.GetRequiredService<JobService>();
        JobItem job = Assert.Single((await jobs.ListAsync(new PageRequest(), CancellationToken.None)).Items);
        Assert.Equal(BackgroundJobStatus.Failed, job.Status);
        Assert.Equal(1, job.Attempts);
        MediaLibraryService media = scope.ServiceProvider.GetRequiredService<MediaLibraryService>();
        await JobTests.AssertBusinessAsync(404, "THUMBNAIL_NOT_READY", () => media.OpenThumbnailAsync(id, CancellationToken.None));
    }

    [Fact]
    public async Task Reanalizar_vuelve_a_encolar_el_analisis_y_un_duplicado_no_encola_otro()
    {
        await fixture.ClearJobsAsync();
        int org = await fixture.NewOrganizationAsync();
        byte[] bytes = await File.ReadAllBytesAsync(fixture.SampleImagePath);
        long id = await IngestAsync(org, "a.png", bytes);
        Assert.Equal(id, await IngestAsync(org, "copia.png", bytes));
        JobRunner runner = fixture.Services.GetRequiredService<JobRunner>();
        Assert.True(await runner.ProcessNextAsync(CancellationToken.None));
        Assert.False(await runner.ProcessNextAsync(CancellationToken.None));

        await using (AsyncServiceScope scope = fixture.Scope(org))
        {
            await scope.ServiceProvider.GetRequiredService<MediaLibraryService>().ReprocessAsync(id, CancellationToken.None);
        }

        Assert.Equal(MediaProcessingStatus.Pending, (await GetAsync(org, id)).ProcessingStatus);
        Assert.True(await runner.ProcessNextAsync(CancellationToken.None));
        Assert.Equal(MediaProcessingStatus.Ready, (await GetAsync(org, id)).ProcessingStatus);
    }

    private async Task<long> IngestAsync(int organizationId, string fileName, byte[] bytes)
    {
        await using AsyncServiceScope scope = fixture.Scope(organizationId);
        MediaLibraryService media = scope.ServiceProvider.GetRequiredService<MediaLibraryService>();
        using MemoryStream stream = new(bytes);
        return (await media.IngestAsync(fileName, new MediaRightsBody(), stream, CancellationToken.None)).AssetId;
    }

    private async Task<MediaAssetDetail> GetAsync(int organizationId, long id)
    {
        await using AsyncServiceScope scope = fixture.Scope(organizationId);
        return await scope.ServiceProvider.GetRequiredService<MediaLibraryService>().GetAsync(id, CancellationToken.None);
    }

    private static async Task<byte[]> ReadAsync(MediaContent content)
    {
        await using Stream stream = content.Content;
        using MemoryStream copy = new();
        await stream.CopyToAsync(copy);
        return copy.ToArray();
    }
}

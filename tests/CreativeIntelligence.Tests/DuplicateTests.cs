using Microsoft.Extensions.DependencyInjection;

namespace CreativeIntelligence.Tests;

/// <summary>Duplicados visuales con FFmpeg real: el mismo contenido recodificado, a otra resolución o en otro formato.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class DuplicateTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Un_video_recodificado_a_otra_resolucion_se_reconoce_como_duplicado_visual_del_original()
    {
        await fixture.ClearJobsAsync();
        int org = await fixture.NewOrganizationAsync();
        long original = await IngestAsync(org, "anuncio.mp4", fixture.SampleCutsVideoPath);
        long recoded = await IngestAsync(org, "anuncio para whatsapp.mp4", fixture.SampleRecodedVideoPath);
        long different = await IngestAsync(org, "otro.mp4", fixture.SampleVideoPath);
        Assert.NotEqual(original, recoded);

        await DrainAsync();

        MediaAssetDetail originalDetail = await GetAsync(org, original);
        MediaAssetDetail recodedDetail = await GetAsync(org, recoded);
        Assert.Null(originalDetail.DuplicateOfAssetId);
        Assert.Equal(original, recodedDetail.DuplicateOfAssetId);
        Assert.Null((await GetAsync(org, different)).DuplicateOfAssetId);

        // No son el mismo archivo: cambian los bytes, el hash y la resolución.
        Assert.NotEqual(originalDetail.Sha256, recodedDetail.Sha256);
        Assert.NotEqual(originalDetail.Width, recodedDetail.Width);

        await using AsyncServiceScope scope = fixture.Scope(org);
        MediaLibraryService media = scope.ServiceProvider.GetRequiredService<MediaLibraryService>();
        PagedResult<MediaAssetItem> list = await media.ListAsync(new MediaFilter(), new PageRequest(), CancellationToken.None);
        Assert.Equal(original, list.Items.Single(i => i.Id == recoded).DuplicateOfAssetId);
    }

    [Fact]
    public async Task Una_imagen_convertida_a_otro_formato_y_tamano_es_duplicado_y_una_imagen_distinta_no()
    {
        await fixture.ClearJobsAsync();
        int org = await fixture.NewOrganizationAsync();
        long png = await IngestAsync(org, "producto.png", fixture.SampleImagePath);
        long jpeg = await IngestAsync(org, "producto chico.jpg", fixture.SampleImageJpegPath);
        long other = await IngestAsync(org, "barras.png", fixture.SampleOtherImagePath);

        await DrainAsync();

        Assert.Null((await GetAsync(org, png)).DuplicateOfAssetId);
        Assert.Equal(png, (await GetAsync(org, jpeg)).DuplicateOfAssetId);
        Assert.Null((await GetAsync(org, other)).DuplicateOfAssetId);
    }

    [Fact]
    public async Task Un_duplicado_solo_se_busca_dentro_de_la_misma_organizacion()
    {
        await fixture.ClearJobsAsync();
        int orgA = await fixture.NewOrganizationAsync();
        int orgB = await fixture.NewOrganizationAsync();
        await IngestAsync(orgA, "anuncio.mp4", fixture.SampleCutsVideoPath);
        long inB = await IngestAsync(orgB, "anuncio recodificado.mp4", fixture.SampleRecodedVideoPath);

        await DrainAsync();

        Assert.Null((await GetAsync(orgB, inB)).DuplicateOfAssetId);
    }

    [Theory]
    [InlineData("0000000000000000", "0000000000000000", 0.0)]
    [InlineData("0000000000000000", "ffffffffffffffff", 1.0)]
    [InlineData("0000000000000000", "000000000000000f", 0.0625)]
    [InlineData("00000000000000000000000000000000", "0000000000000000ffffffffffffffff", 0.5)]
    [InlineData("0000000000000000", "00000000000000000000000000000000", 1.0)]
    [InlineData("", "", 1.0)]
    public void La_distancia_entre_huellas_es_la_fraccion_de_bits_distintos(string left, string right, double expected)
    {
        Assert.Equal(expected, MediaRules.HashDistance(left, right), 6);
    }

    [Fact]
    public void La_huella_se_escribe_con_16_hexadecimales_por_cuadro()
    {
        Assert.Equal("000000000000000fffffffffffffffff", MediaRules.FormatHash([15UL, ulong.MaxValue]));
    }

    private async Task<long> IngestAsync(int organizationId, string fileName, string sourcePath)
    {
        await using AsyncServiceScope scope = fixture.Scope(organizationId);
        MediaLibraryService media = scope.ServiceProvider.GetRequiredService<MediaLibraryService>();
        await using FileStream stream = File.OpenRead(sourcePath);
        return (await media.IngestAsync(fileName, new MediaRightsBody(), stream, CancellationToken.None)).AssetId;
    }

    private async Task<MediaAssetDetail> GetAsync(int organizationId, long id)
    {
        await using AsyncServiceScope scope = fixture.Scope(organizationId);
        return await scope.ServiceProvider.GetRequiredService<MediaLibraryService>().GetAsync(id, CancellationToken.None);
    }

    private async Task DrainAsync()
    {
        JobRunner runner = fixture.Services.GetRequiredService<JobRunner>();
        while (await runner.ProcessNextAsync(CancellationToken.None))
        {
        }
    }
}

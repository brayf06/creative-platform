using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;

namespace CreativeIntelligence.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class MediaTests(DatabaseFixture fixture)
{
    private static readonly PageRequest FirstPage = new();

    [Fact]
    public async Task Ingresar_un_archivo_lo_guarda_por_su_hash_sin_alterar_el_contenido()
    {
        int org = await fixture.NewOrganizationAsync();
        byte[] bytes = RandomContent();
        string expectedHash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        await using AsyncServiceScope scope = fixture.Scope(org);
        MediaLibraryService media = scope.ServiceProvider.GetRequiredService<MediaLibraryService>();

        IngestResult result = await IngestAsync(media, @"C:\carpeta\Demo Uñas.MP4", bytes, new MediaRightsBody { RightsHolder = "  Marca  ", ReuseAuthorized = true });

        Assert.Equal(IngestOutcome.Created, result.Outcome);
        MediaAssetDetail detail = await media.GetAsync(result.AssetId, CancellationToken.None);
        Assert.Equal("Demo Uñas.MP4", detail.FileName);
        Assert.Equal(MediaAssetKind.Video, detail.Kind);
        Assert.Equal("video/mp4", detail.ContentType);
        Assert.Equal(bytes.Length, detail.SizeBytes);
        Assert.Equal(expectedHash, detail.Sha256);
        Assert.Equal("Marca", detail.RightsHolder);
        Assert.True(detail.ReuseAuthorized);
        Assert.Equal(MediaSourceKind.Upload, detail.SourceKind);

        MediaContent content = await media.OpenOriginalAsync(result.AssetId, CancellationToken.None);
        await using Stream stream = content.Content;
        Assert.True(stream.CanSeek);
        using MemoryStream copy = new();
        await stream.CopyToAsync(copy);
        Assert.Equal(bytes, copy.ToArray());
    }

    [Fact]
    public async Task El_mismo_contenido_con_otro_nombre_se_reconoce_como_duplicado_y_no_se_guarda_dos_veces()
    {
        int org = await fixture.NewOrganizationAsync();
        byte[] bytes = RandomContent();
        await using AsyncServiceScope scope = fixture.Scope(org);
        MediaLibraryService media = scope.ServiceProvider.GetRequiredService<MediaLibraryService>();

        IngestResult first = await IngestAsync(media, "a.mp4", bytes);
        IngestResult second = await IngestAsync(media, "copia de a.mov", bytes);

        Assert.Equal(IngestOutcome.Duplicate, second.Outcome);
        Assert.Equal(first.AssetId, second.AssetId);
        Assert.Equal(1, (await media.ListAsync(new MediaFilter(), FirstPage, CancellationToken.None)).Total);
        string originals = Path.Combine(fixture.StorageRoot, $"org-{org}", "originals");
        Assert.Single(Directory.GetFiles(originals, "*", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(Path.Combine(fixture.StorageRoot, $"org-{org}", "tmp")));
    }

    [Fact]
    public async Task Cada_organizacion_tiene_su_propia_biblioteca_aunque_el_contenido_sea_el_mismo()
    {
        int orgA = await fixture.NewOrganizationAsync();
        int orgB = await fixture.NewOrganizationAsync();
        byte[] bytes = RandomContent();
        long idA;
        await using (AsyncServiceScope scopeA = fixture.Scope(orgA))
        {
            idA = (await IngestAsync(scopeA.ServiceProvider.GetRequiredService<MediaLibraryService>(), "a.png", bytes)).AssetId;
        }

        await using AsyncServiceScope scopeB = fixture.Scope(orgB);
        MediaLibraryService mediaB = scopeB.ServiceProvider.GetRequiredService<MediaLibraryService>();
        await JobTests.AssertBusinessAsync(404, "MEDIA_NOT_FOUND", () => mediaB.GetAsync(idA, CancellationToken.None));
        await JobTests.AssertBusinessAsync(404, "MEDIA_NOT_FOUND", () => mediaB.OpenOriginalAsync(idA, CancellationToken.None));
        await JobTests.AssertBusinessAsync(404, "MEDIA_NOT_FOUND",
            () => mediaB.UpdateRightsAsync(idA, new MediaRightsBody { ReuseAuthorized = true }, CancellationToken.None));
        Assert.Equal(0, (await mediaB.ListAsync(new MediaFilter(), FirstPage, CancellationToken.None)).Total);

        IngestResult inB = await IngestAsync(mediaB, "a.png", bytes);
        Assert.Equal(IngestOutcome.Created, inB.Outcome);
        Assert.NotEqual(idA, inB.AssetId);
    }

    [Fact]
    public async Task Se_rechazan_los_formatos_no_admitidos_los_archivos_vacios_y_la_carga_sin_organizacion()
    {
        int org = await fixture.NewOrganizationAsync();
        await using AsyncServiceScope scope = fixture.Scope(org);
        MediaLibraryService media = scope.ServiceProvider.GetRequiredService<MediaLibraryService>();

        await JobTests.AssertBusinessAsync(415, "UNSUPPORTED_MEDIA_TYPE", () => IngestAsync(media, "virus.exe", RandomContent()));
        await JobTests.AssertBusinessAsync(415, "UNSUPPORTED_MEDIA_TYPE", () => IngestAsync(media, "sin-extension", RandomContent()));
        await JobTests.AssertBusinessAsync(400, "EMPTY_FILE", () => IngestAsync(media, "vacio.mp4", []));
        Assert.Equal(0, (await media.ListAsync(new MediaFilter(), FirstPage, CancellationToken.None)).Total);
        Assert.False(Directory.Exists(Path.Combine(fixture.StorageRoot, $"org-{org}", "originals")));

        await using AsyncServiceScope noOrg = fixture.Scope(0);
        MediaLibraryService mediaNone = noOrg.ServiceProvider.GetRequiredService<MediaLibraryService>();
        await JobTests.AssertBusinessAsync(403, "NO_ORGANIZATION", () => IngestAsync(mediaNone, "a.mp4", RandomContent()));
    }

    [Fact]
    public async Task El_listado_filtra_por_tipo_autorizacion_y_nombre()
    {
        int org = await fixture.NewOrganizationAsync();
        await using AsyncServiceScope scope = fixture.Scope(org);
        MediaLibraryService media = scope.ServiceProvider.GetRequiredService<MediaLibraryService>();
        await IngestAsync(media, "hook uñas.mp4", RandomContent(), new MediaRightsBody { ReuseAuthorized = true });
        await IngestAsync(media, "testimonio.mov", RandomContent());
        await IngestAsync(media, "producto.png", RandomContent(), new MediaRightsBody { ReuseAuthorized = true });

        Assert.Equal(3, await CountAsync(media, new MediaFilter()));
        Assert.Equal(2, await CountAsync(media, new MediaFilter { Kind = MediaAssetKind.Video }));
        Assert.Equal(1, await CountAsync(media, new MediaFilter { Kind = MediaAssetKind.Image }));
        Assert.Equal(2, await CountAsync(media, new MediaFilter { ReuseAuthorized = true }));
        Assert.Equal(1, await CountAsync(media, new MediaFilter { ReuseAuthorized = false }));
        Assert.Equal(1, await CountAsync(media, new MediaFilter { Search = "uñas" }));
        Assert.Equal(1, await CountAsync(media, new MediaFilter { Kind = MediaAssetKind.Video, ReuseAuthorized = true }));

        PagedResult<MediaAssetItem> page = await media.ListAsync(new MediaFilter(), new PageRequest { PageSize = 2 }, CancellationToken.None);
        Assert.Equal(["producto.png", "testimonio.mov"], page.Items.Select(i => i.FileName));
        Assert.Equal(2, page.TotalPages);
    }

    [Fact]
    public async Task Cambiar_los_derechos_se_audita_y_no_toca_el_original()
    {
        int org = await fixture.NewOrganizationAsync();
        byte[] bytes = RandomContent();
        await using AsyncServiceScope scope = fixture.Scope(org);
        MediaLibraryService media = scope.ServiceProvider.GetRequiredService<MediaLibraryService>();
        long id = (await IngestAsync(media, "a.mp4", bytes)).AssetId;
        Assert.False((await media.GetAsync(id, CancellationToken.None)).ReuseAuthorized);

        DateOnly until = new(2027, 12, 31);
        await media.UpdateRightsAsync(
            id,
            new MediaRightsBody { RightsHolder = "Marca", LicenseNotes = "Sólo redes", ReuseAuthorized = true, RightsExpireOn = until },
            CancellationToken.None);

        MediaAssetDetail detail = await media.GetAsync(id, CancellationToken.None);
        Assert.True(detail.ReuseAuthorized);
        Assert.Equal(until, detail.RightsExpireOn);
        Assert.Equal("Sólo redes", detail.LicenseNotes);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), detail.Sha256);

        AuditService audit = scope.ServiceProvider.GetRequiredService<AuditService>();
        Assert.Equal(["media.rights", "job.enqueue", "media.ingest"], (await audit.ListAsync(FirstPage, CancellationToken.None)).Items.Select(e => e.Action));
    }

    private static async Task<IngestResult> IngestAsync(
        MediaLibraryService media, string fileName, byte[] bytes, MediaRightsBody? rights = null)
    {
        using MemoryStream stream = new(bytes);
        return await media.IngestAsync(fileName, rights ?? new MediaRightsBody(), stream, CancellationToken.None);
    }

    private static async Task<int> CountAsync(MediaLibraryService media, MediaFilter filter)
    {
        return (await media.ListAsync(filter, FirstPage, CancellationToken.None)).Total;
    }

    private static byte[] RandomContent()
    {
        return Encoding.UTF8.GetBytes($"contenido de prueba {Guid.NewGuid():N}");
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CreativeIntelligence.Tests;

/// <summary>Carpetas vigiladas sobre carpetas temporales reales dentro de la raíz permitida de la corrida.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class WatchedFolderTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Revisar_una_carpeta_ingresa_los_archivos_admitidos_con_sus_derechos_y_no_los_vuelve_a_ingresar()
    {
        await fixture.ClearJobsAsync();
        int org = await fixture.NewOrganizationAsync();
        string folder = NewFolder();
        File.Copy(fixture.SampleImagePath, Path.Combine(folder, "producto.png"));
        Directory.CreateDirectory(Path.Combine(folder, "campaña"));
        File.Copy(fixture.SampleOtherImagePath, Path.Combine(folder, "campaña", "barras.png"));
        File.Copy(fixture.SampleImagePath, Path.Combine(folder, "campaña", "copia del producto.png"));
        await File.WriteAllTextAsync(Path.Combine(folder, "notas.txt"), "no es un medio");
        await File.WriteAllBytesAsync(Path.Combine(folder, "vacio.mp4"), []);
        byte[] before = await File.ReadAllBytesAsync(Path.Combine(folder, "producto.png"));

        await using AsyncServiceScope scope = fixture.Scope(org);
        WatchedFolderService folders = scope.ServiceProvider.GetRequiredService<WatchedFolderService>();
        MediaLibraryService media = scope.ServiceProvider.GetRequiredService<MediaLibraryService>();
        int id = await folders.CreateAsync(
            new CreateWatchedFolderBody { Path = folder, RightsHolder = "Marca", ReuseAuthorized = true }, CancellationToken.None);
        await DrainAsync();

        WatchedFolderItem item = Assert.Single((await folders.ListAsync(CancellationToken.None)).Folders);
        Assert.NotNull(item.LastScanAt);
        Assert.Equal("4 archivos de formato admitido: 2 ingresados, 1 ya estaban, 1 rechazados, 0 en espera.", item.LastScanSummary);
        Assert.Equal(4, item.FileCount);

        PagedResult<MediaAssetItem> assets = await media.ListAsync(new MediaFilter(), new PageRequest(), CancellationToken.None);
        Assert.Equal(2, assets.Total);
        Assert.All(assets.Items, a => Assert.True(a.ReuseAuthorized));
        Assert.All(assets.Items, a => Assert.Equal("Marca", a.RightsHolder));
        MediaAssetDetail detail = await media.GetAsync(assets.Items.Single(a => a.FileName == "barras.png").Id, CancellationToken.None);
        Assert.Equal(MediaSourceKind.WatchedFolder, detail.SourceKind);
        Assert.Equal(Path.Combine(folder, "campaña", "barras.png"), detail.SourceReference);

        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        WatchedFile rejected = await db.WatchedFiles.AsNoTracking().SingleAsync(w => w.Outcome == WatchedFileOutcome.Rejected);
        Assert.Equal("vacio.mp4", rejected.RelativePath);
        Assert.Contains("vacío", rejected.Message);

        // Segunda revisión sin cambios: no ingresa nada ni toca los archivos de la carpeta.
        await folders.RequestScanAsync(id, CancellationToken.None);
        await DrainAsync();
        Assert.Equal(2, (await media.ListAsync(new MediaFilter(), new PageRequest(), CancellationToken.None)).Total);
        Assert.Equal(
            "4 archivos de formato admitido: 0 ingresados, 0 ya estaban, 0 rechazados, 0 en espera.",
            (await folders.ListAsync(CancellationToken.None)).Folders[0].LastScanSummary);
        Assert.Equal(before, await File.ReadAllBytesAsync(Path.Combine(folder, "producto.png")));
        Assert.True(File.Exists(Path.Combine(folder, "notas.txt")));

        // Aparece un archivo nuevo: sólo ése se ingresa.
        File.Copy(fixture.SampleImageJpegPath, Path.Combine(folder, "nuevo.jpg"));
        await folders.RequestScanAsync(id, CancellationToken.None);
        await DrainAsync();
        Assert.Equal(3, (await media.ListAsync(new MediaFilter(), new PageRequest(), CancellationToken.None)).Total);
        Assert.StartsWith("5 archivos de formato admitido: 1 ingresados", (await folders.ListAsync(CancellationToken.None)).Folders[0].LastScanSummary);

        List<string> actions = (await scope.ServiceProvider.GetRequiredService<AuditService>().ListAsync(new PageRequest { PageSize = 100 }, CancellationToken.None))
            .Items.Select(e => e.Action).ToList();
        Assert.Contains("folder.create", actions);
        Assert.Equal(3, actions.Count(a => a == "media.ingest"));
    }

    [Fact]
    public async Task Solo_se_vigila_una_carpeta_existente_dentro_de_una_raiz_permitida_y_una_sola_vez()
    {
        await fixture.ClearJobsAsync();
        int org = await fixture.NewOrganizationAsync();
        string folder = NewFolder();
        await using AsyncServiceScope scope = fixture.Scope(org);
        WatchedFolderService folders = scope.ServiceProvider.GetRequiredService<WatchedFolderService>();

        Assert.Equal([fixture.WatchRoot], (await folders.ListAsync(CancellationToken.None)).AllowedRoots);
        await AssertRejectedAsync(folders, Path.GetTempPath(), "FOLDER_NOT_ALLOWED");
        await AssertRejectedAsync(folders, fixture.WatchRoot + "2", "FOLDER_NOT_ALLOWED");
        await AssertRejectedAsync(folders, Path.Combine(folder, "..", "..", ".."), "FOLDER_NOT_ALLOWED");
        await AssertRejectedAsync(folders, Path.Combine(folder, "no-existe"), "FOLDER_NOT_FOUND");

        await folders.CreateAsync(new CreateWatchedFolderBody { Path = folder + Path.DirectorySeparatorChar }, CancellationToken.None);
        await JobTests.AssertBusinessAsync(409, "FOLDER_ALREADY_WATCHED",
            () => folders.CreateAsync(new CreateWatchedFolderBody { Path = folder }, CancellationToken.None));
        Assert.Single((await folders.ListAsync(CancellationToken.None)).Folders);
    }

    [Fact]
    public async Task El_programador_encola_una_revision_por_carpeta_activa_y_no_la_repite_en_el_mismo_intervalo()
    {
        await fixture.ClearJobsAsync();
        int org = await fixture.NewOrganizationAsync();
        int other = await fixture.NewOrganizationAsync();
        int active;
        int paused;
        await using (AsyncServiceScope scope = fixture.Scope(org))
        {
            WatchedFolderService folders = scope.ServiceProvider.GetRequiredService<WatchedFolderService>();
            active = await folders.CreateAsync(new CreateWatchedFolderBody { Path = NewFolder() }, CancellationToken.None);
            paused = await folders.CreateAsync(new CreateWatchedFolderBody { Path = NewFolder() }, CancellationToken.None);
            await folders.SetActiveAsync(paused, false, CancellationToken.None);
        }

        await DrainAsync();
        await using AsyncServiceScope otherScope = fixture.Scope(other);
        WatchedFolderService foreign = otherScope.ServiceProvider.GetRequiredService<WatchedFolderService>();
        Assert.Empty((await foreign.ListAsync(CancellationToken.None)).Folders);
        await JobTests.AssertBusinessAsync(404, "FOLDER_NOT_WATCHED", () => foreign.RequestScanAsync(active, CancellationToken.None));
        await JobTests.AssertBusinessAsync(404, "FOLDER_NOT_WATCHED", () => foreign.SetActiveAsync(active, false, CancellationToken.None));

        // Lo que hace el programador del Worker: listar las activas de todas las organizaciones y encolar cada una
        // con su organización activada.
        List<ActiveFolder> due = (await foreign.ActiveFoldersAsync(CancellationToken.None)).Where(f => f.OrganizationId == org).ToList();
        ActiveFolder only = Assert.Single(due);
        Assert.Equal(active, only.FolderId);
        await using AsyncServiceScope system = fixture.Scope(only.OrganizationId);
        WatchedFolderService scheduler = system.ServiceProvider.GetRequiredService<WatchedFolderService>();
        Assert.True((await scheduler.EnqueueScanAsync(only.FolderId, "intervalo-1", CancellationToken.None)).Created);
        Assert.False((await scheduler.EnqueueScanAsync(only.FolderId, "intervalo-1", CancellationToken.None)).Created);
        Assert.True((await scheduler.EnqueueScanAsync(only.FolderId, "intervalo-2", CancellationToken.None)).Created);
    }

    [Fact]
    public async Task Si_la_carpeta_desaparece_la_revision_falla_sin_reintentos_y_lo_deja_dicho()
    {
        await fixture.ClearJobsAsync();
        int org = await fixture.NewOrganizationAsync();
        string folder = NewFolder();
        await using AsyncServiceScope scope = fixture.Scope(org);
        WatchedFolderService folders = scope.ServiceProvider.GetRequiredService<WatchedFolderService>();
        await folders.CreateAsync(new CreateWatchedFolderBody { Path = folder }, CancellationToken.None);
        Directory.Delete(folder);

        await DrainAsync();

        Assert.Equal(
            "La carpeta no existe o no se puede leer desde el servidor.",
            (await folders.ListAsync(CancellationToken.None)).Folders[0].LastScanSummary);
        JobService jobs = scope.ServiceProvider.GetRequiredService<JobService>();
        JobItem job = Assert.Single((await jobs.ListAsync(new PageRequest(), CancellationToken.None)).Items);
        Assert.Equal(BackgroundJobStatus.Failed, job.Status);
        Assert.Equal(1, job.Attempts);
    }

    private string NewFolder()
    {
        string folder = Path.Combine(fixture.WatchRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static Task AssertRejectedAsync(WatchedFolderService folders, string path, string code)
    {
        return JobTests.AssertBusinessAsync(400, code, () => folders.CreateAsync(new CreateWatchedFolderBody { Path = path }, CancellationToken.None));
    }

    private async Task DrainAsync()
    {
        JobRunner runner = fixture.Services.GetRequiredService<JobRunner>();
        while (await runner.ProcessNextAsync(CancellationToken.None))
        {
        }
    }
}

using System.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CreativeIntelligence.Tests;

/// <summary>
/// Base de prueba en LocalDB, recreada desde las migraciones al empezar la corrida. Nunca apunta a otra base:
/// la cadena de conexión está fija acá.
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    // La base es siempre la de tests. CI_TEST_SQL_SERVER sólo cambia cómo se llega a la instancia: sirve cuando
    // sqllocaldb la da por detenida aunque su proceso siga vivo, y hay que entrar por su canalización
    // (np:\\.\pipe\LOCALDB#XXXXXXXX\tsql\query).
    private static readonly string ConnectionString =
        $@"Server={Environment.GetEnvironmentVariable("CI_TEST_SQL_SERVER") ?? @"(localdb)\MSSQLLocalDB"};"
        + "Database=CreativeIntelligence_Test;Trusted_Connection=True;TrustServerCertificate=True";

    public ServiceProvider Services { get; private set; } = null!;

    /// <summary>Proveedor de IA falso que usan todos los servicios de la corrida.</summary>
    public FakeSegmentAnalyzer Analyzer { get; } = new();

    /// <summary>Proveedor de embeddings falso que usan todos los servicios de la corrida.</summary>
    public FakeEmbeddingProvider Embeddings { get; } = new();

    public FakeConceptPlanner Planner { get; } = new();

    public FakeResearchAgent Research { get; } = new();

    /// <summary>Publicador de Meta falso: cuenta las llamadas de escritura que recibe.</summary>
    public FakeMetaPublisher Publisher { get; } = new();

    /// <summary>Carpeta temporal de almacenamiento de esta corrida; se borra al terminar.</summary>
    public string StorageRoot { get; } = Path.Combine(Path.GetTempPath(), $"ci-tests-{Guid.NewGuid():N}");

    /// <summary>Única raíz desde la que los tests pueden vigilar carpetas.</summary>
    public string WatchRoot => Path.Combine(StorageRoot, "watch");

    /// <summary>Clip de 2 s, 320×568, 30 fps, H.264 + AAC, generado con FFmpeg al iniciar la corrida.</summary>
    public string SampleVideoPath => Path.Combine(StorageRoot, "samples", "clip.mp4");

    /// <summary>Imagen PNG de 640×360 generada con FFmpeg al iniciar la corrida.</summary>
    public string SampleImagePath => Path.Combine(StorageRoot, "samples", "imagen.png");

    /// <summary>El video de cortes recodificado a 240×426 y con mucha compresión: misma imagen, otros bytes.</summary>
    public string SampleRecodedVideoPath => Path.Combine(StorageRoot, "samples", "recodificado.mp4");

    /// <summary>La imagen de prueba convertida a JPEG de 320×180.</summary>
    public string SampleImageJpegPath => Path.Combine(StorageRoot, "samples", "imagen.jpg");

    /// <summary>Otra imagen PNG de 640×360, con barras de color: no se parece a la de prueba.</summary>
    public string SampleOtherImagePath => Path.Combine(StorageRoot, "samples", "barras.png");

    /// <summary>Video de unos 5 s con una voz sintetizada (en inglés: es la única voz que trae FFmpeg) que dice
    /// "This product makes your nails look perfect in five minutes. Order yours today."</summary>
    public string SampleVoiceVideoPath => Path.Combine(StorageRoot, "samples", "voz.mp4");

    /// <summary>Video de 5,5 s con cortes a los 2 s y a los 4 s (rojo, carta de ajuste, azul).</summary>
    public string SampleCutsVideoPath => Path.Combine(StorageRoot, "samples", "cortes.mp4");

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(StorageRoot, "samples"));
        Directory.CreateDirectory(WatchRoot);
        await RunFfmpegAsync(
            "-f", "lavfi", "-i", "testsrc=duration=2:size=320x568:rate=30",
            "-f", "lavfi", "-i", "sine=frequency=440:duration=2",
            "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", SampleVideoPath);
        await RunFfmpegAsync("-f", "lavfi", "-i", "testsrc=size=640x360", "-frames:v", "1", SampleImagePath);
        await RunFfmpegAsync(
            "-f", "lavfi", "-i", "color=c=red:d=2:s=320x568:r=30",
            "-f", "lavfi", "-i", "testsrc=d=2:s=320x568:r=30",
            "-f", "lavfi", "-i", "color=c=blue:d=1.5:s=320x568:r=30",
            "-filter_complex", "[0:v][1:v][2:v]concat=n=3:v=1:a=0",
            "-c:v", "libx264", "-pix_fmt", "yuv420p", SampleCutsVideoPath);
        await RunFfmpegAsync(
            "-f", "lavfi", "-i", "testsrc=duration=8:size=320x568:rate=30",
            "-f", "lavfi", "-i", "flite=text='This product makes your nails look perfect in five minutes. Order yours today.':voice=slt",
            "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", SampleVoiceVideoPath);
        await RunFfmpegAsync(
            "-i", SampleCutsVideoPath, "-vf", "scale=240:426", "-c:v", "libx264", "-crf", "34", "-pix_fmt", "yuv420p",
            SampleRecodedVideoPath);
        await RunFfmpegAsync("-i", SampleImagePath, "-vf", "scale=320:180", "-q:v", "8", SampleImageJpegPath);
        await RunFfmpegAsync("-f", "lavfi", "-i", "smptebars=size=640x360", "-frames:v", "1", SampleOtherImagePath);
        string toolsFolder = Path.Combine(RepositoryRoot(), "tools", "media");

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = ConnectionString,
                ["Storage:RootPath"] = StorageRoot,
                ["Ingest:AllowedRoots:0"] = WatchRoot,
                ["Ingest:MinFileAgeSeconds"] = "0",
                ["MediaTools:ScriptsPath"] = toolsFolder,
                ["MediaTools:ModelsPath"] = Path.Combine(toolsFolder, "models"),
                ["MediaTools:WhisperModel"] = "tiny",
                ["MediaTools:PythonPath"] = Path.Combine(toolsFolder, ".venv", "Scripts", "python.exe"),
                ["Jobs:RetryBaseSeconds"] = "0",
                ["Jobs:HeartbeatMs"] = "100",
                ["Jobs:LeaseSeconds"] = "30",
                ["Jobs:MaxAttempts"] = "3",
            })
            .Build();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddInfrastructure(configuration);
        services.AddApplication(configuration);
        services.AddIdentityCore<ApplicationUser>().AddRoles<IdentityRole>().AddEntityFrameworkStores<AppDbContext>();
        services.AddScoped<OrganizationService>();
        // La última inscripción gana: ningún test llama a Gemini.
        services.AddSingleton<ISegmentAnalyzer>(Analyzer);
        services.AddSingleton<IEmbeddingProvider>(Embeddings);
        services.AddSingleton<IConceptPlanner>(Planner);
        services.AddSingleton<IResearchAgent>(Research);
        services.AddSingleton<IMetaPublisher>(Publisher);
        Services = services.BuildServiceProvider();

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await Services.DisposeAsync();
        if (Directory.Exists(StorageRoot))
        {
            Directory.Delete(StorageRoot, true);
        }
    }

    /// <summary>Ámbito con la organización (y opcionalmente el usuario) activos, como una solicitud web.</summary>
    public AsyncServiceScope Scope(int organizationId, string? userId = null)
    {
        AsyncServiceScope scope = Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentContext>().Set(organizationId, userId, "tester");
        return scope;
    }

    public async Task<int> NewOrganizationAsync()
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Organization organization = new() { Name = $"Org {Guid.NewGuid():N}" };
        db.Organizations.Add(organization);
        await db.SaveChangesAsync();
        return organization.Id;
    }

    public async Task<string> NewUserAsync()
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        UserManager<ApplicationUser> users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        string email = $"{Guid.NewGuid():N}@test.local";
        ApplicationUser user = new() { UserName = email, Email = email, FullName = "Usuario de prueba" };
        IdentityResult result = await users.CreateAsync(user, "Prueba.2026");
        Assert.True(result.Succeeded);
        return user.Id;
    }

    public static string RepositoryRoot()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "CreativeIntelligence.slnx")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        return root.FullName;
    }

    // Los tests de medios necesitan FFmpeg en el PATH, igual que el Worker.
    public static async Task RunFfmpegAsync(params string[] arguments)
    {
        ProcessStartInfo start = new("ffmpeg") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        start.ArgumentList.Add("-y");
        start.ArgumentList.Add("-v");
        start.ArgumentList.Add("error");
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start)!;
        string error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, $"FFmpeg no pudo generar el archivo de prueba: {error}");
    }

    /// <summary>La cola es global: cada test que toma trabajos parte de una cola vacía.</summary>
    public async Task ClearJobsAsync()
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.AiUsageRecords.IgnoreQueryFilters().ExecuteDeleteAsync();
        await db.BackgroundJobs.IgnoreQueryFilters().ExecuteDeleteAsync();
        // Las carpetas de tests anteriores ya no existen en disco: no deben quedar activas para el programador.
        await db.WatchedFolders.IgnoreQueryFilters().ExecuteDeleteAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
    public const string Name = "database";
}

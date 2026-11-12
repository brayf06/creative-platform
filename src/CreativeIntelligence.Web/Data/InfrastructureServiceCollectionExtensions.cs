using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CreativeIntelligence.Web.Data;

/// <summary>Registro de la infraestructura: SQL Server, almacenamiento de medios en disco, herramientas de medios y
/// el proveedor de IA (Gemini).</summary>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>Registra el <see cref="AppDbContext"/> sobre SQL Server,
    /// y el almacenamiento de medios local.</summary>
    /// <param name="services">Contenedor.</param>
    /// <param name="configuration">Configuración; lee <c>ConnectionStrings:Default</c> y <c>Storage:RootPath</c>.</param>
    /// <returns>El mismo contenedor.</returns>
    /// <exception cref="InvalidOperationException">Si falta la cadena de conexión o la carpeta de almacenamiento: la
    /// aplicación no arranca.</exception>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        string? connectionString = configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Falta la configuración obligatoria ConnectionStrings:Default.");
        }

        services.AddDbContext<AppDbContext>(o => o.UseSqlServer(connectionString));
        if (string.IsNullOrWhiteSpace(configuration["Storage:RootPath"]))
        {
            throw new InvalidOperationException("Falta la configuración obligatoria Storage:RootPath.");
        }

        services.Configure<StorageOptions>(configuration.GetSection("Storage"));
        services.AddSingleton<IMediaStorage, LocalMediaStorage>();
        services.Configure<MediaToolsOptions>(configuration.GetSection("MediaTools"));
        services.AddSingleton<IMediaTools, FfmpegMediaTools>();
        services.Configure<GeminiOptions>(configuration.GetSection("Ai:Gemini"));
        services.AddHttpClient<GeminiInteractions>();
        services.AddTransient<GeminiSegmentAnalyzer>();
        services.AddTransient<IConceptPlanner, GeminiConceptPlanner>();
        services.AddTransient<IResearchAgent, GeminiResearchAgent>();
        // No hay conexión con Meta todavía: hasta tener app y token, este publicador se declara no configurado.
        services.AddSingleton<IMetaPublisher, NotConnectedMetaPublisher>();
        services.AddTransient<ISegmentAnalyzer>(provider => provider.GetRequiredService<GeminiSegmentAnalyzer>());
        services.AddHttpClient<GeminiEmbeddingProvider>();
        services.AddTransient<IEmbeddingProvider>(provider => provider.GetRequiredService<GeminiEmbeddingProvider>());
        return services;
    }
}

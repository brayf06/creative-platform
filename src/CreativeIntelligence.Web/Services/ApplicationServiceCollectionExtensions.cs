using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CreativeIntelligence.Web.Services;

/// <summary>Registro de los servicios de aplicación compartidos por la Web y el Worker.</summary>
public static class ApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Registra el contexto actual, la auditoría, la cola de trabajos con sus manejadores y el registro de uso de
    /// IA. <c>OrganizationService</c> no se registra acá porque necesita Identity, que sólo configura la Web.
    /// </summary>
    /// <param name="services">Contenedor.</param>
    /// <param name="configuration">Configuración; lee la sección <c>Jobs</c>.</param>
    /// <returns>El mismo contenedor.</returns>
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JobOptions>(configuration.GetSection("Jobs"));
        services.Configure<AiOptions>(configuration.GetSection("Ai"));
        services.Configure<IngestOptions>(configuration.GetSection("Ingest"));
        services.AddScoped<CurrentContext>();
        services.AddScoped<AuditService>();
        services.AddScoped<DashboardService>();
        services.AddScoped<JobService>();
        services.AddScoped<JobQueue>();
        services.AddScoped<AiUsageService>();
        services.AddScoped<MediaLibraryService>();
        services.AddScoped<SceneService>();
        services.AddScoped<SegmentEditService>();
        services.AddScoped<TranscriptService>();
        services.AddScoped<AnalysisService>();
        services.AddScoped<SegmentIndexService>();
        services.AddScoped<SearchService>();
        services.AddScoped<WatchedFolderService>();
        services.AddScoped<CompositionService>();
        services.AddScoped<BatchService>();
        services.AddScoped<MusicService>();
        services.AddScoped<BrandService>();
        services.AddScoped<BriefService>();
        services.AddScoped<MetaService>();
        services.AddScoped<PerformanceService>();
        services.AddScoped<ExperimentService>();
        services.AddScoped<HypothesisService>();
        services.AddScoped<LearningService>();
        services.AddScoped<PriorityService>();
        services.AddScoped<ProductionPlanService>();
        services.AddScoped<AnomalyService>();
        services.AddScoped<AutomationService>();
        services.AddScoped<ResearchService>();
        services.AddScoped<StrategyService>();
        services.AddScoped<IJobHandler, DiagnosticJobHandler>();
        services.AddScoped<IJobHandler, MediaProbeJobHandler>();
        services.AddScoped<IJobHandler, MediaScenesJobHandler>();
        services.AddScoped<IJobHandler, MediaSegmentThumbnailsJobHandler>();
        services.AddScoped<IJobHandler, MediaTranscribeJobHandler>();
        services.AddScoped<IJobHandler, MediaAnalyzeJobHandler>();
        services.AddScoped<IJobHandler, MediaEmbedJobHandler>();
        services.AddScoped<IJobHandler, MediaFolderScanJobHandler>();
        services.AddScoped<IJobHandler, CreativeRenderJobHandler>();
        services.AddScoped<IJobHandler, MusicProbeJobHandler>();
        services.AddScoped<IJobHandler, StrategyPlanJobHandler>();
        services.AddScoped<IJobHandler, ResearchJobHandler>();
        services.AddScoped<IJobHandler, MetaImportJobHandler>();
        services.AddScoped<IJobHandler, AutomationExecuteJobHandler>();
        services.AddSingleton<JobRunner>();
        return services;
    }
}

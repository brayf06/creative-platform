using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore;

namespace CreativeIntelligence.Web.Data;

/// <summary>
/// Contexto de EF Core de toda la aplicación (Identity + negocio). Aplica el filtro multiempresa a toda entidad
/// <see cref="IOrganizationScoped"/> usando la organización de <see cref="CurrentContext"/>, y completa
/// <c>OrganizationId</c> al insertar. Las consultas que deben cruzar organizaciones (cola del Worker, inicio de
/// sesión) lo dicen explícitamente con <c>IgnoreQueryFilters()</c>.
/// </summary>
/// <param name="options">Opciones con el proveedor (SQL Server).</param>
/// <param name="current">Organización y usuario del ámbito actual.</param>
public class AppDbContext(DbContextOptions<AppDbContext> options, CurrentContext current)
    : IdentityDbContext<ApplicationUser>(options)
{
    /// <summary>Organización contra la que filtran las consultas de este contexto; 0 = ninguna fila.</summary>
    public int CurrentOrganizationId => current.OrganizationId;

    /// <summary>Organizaciones. Sin filtro: las consultan el administrador de plataforma y el inicio de sesión.</summary>
    public DbSet<Organization> Organizations => Set<Organization>();

    /// <summary>Miembros de la organización activa.</summary>
    public DbSet<OrganizationMember> OrganizationMembers => Set<OrganizationMember>();

    /// <summary>Trabajos en segundo plano de la organización activa.</summary>
    public DbSet<BackgroundJob> BackgroundJobs => Set<BackgroundJob>();

    /// <summary>Auditoría de la organización activa.</summary>
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    /// <summary>Uso de IA de la organización activa.</summary>
    public DbSet<AiUsageRecord> AiUsageRecords => Set<AiUsageRecord>();

    /// <summary>Archivos originales de la biblioteca de la organización activa.</summary>
    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();

    /// <summary>Fragmentos de los videos de la organización activa.</summary>
    public DbSet<MediaSegment> MediaSegments => Set<MediaSegment>();

    /// <summary>Etiquetas de los fragmentos de la organización activa.</summary>
    public DbSet<MediaSegmentTag> MediaSegmentTags => Set<MediaSegmentTag>();

    /// <summary>Frases de las transcripciones de los videos de la organización activa.</summary>
    public DbSet<MediaTranscriptLine> MediaTranscriptLines => Set<MediaTranscriptLine>();

    /// <summary>Análisis con IA de los fragmentos de la organización activa.</summary>
    public DbSet<SegmentAnalysis> SegmentAnalyses => Set<SegmentAnalysis>();

    /// <summary>Funciones narrativas asignadas por los análisis de la organización activa.</summary>
    public DbSet<SegmentAnalysisFunction> SegmentAnalysisFunctions => Set<SegmentAnalysisFunction>();

    /// <summary>Resultados guardados de llamadas a modelos de IA de la organización activa.</summary>
    public DbSet<AiResultCache> AiResultCaches => Set<AiResultCache>();

    /// <summary>Vectores de búsqueda de los fragmentos de la organización activa.</summary>
    public DbSet<SegmentEmbedding> SegmentEmbeddings => Set<SegmentEmbedding>();

    /// <summary>Carpetas vigiladas de la organización activa.</summary>
    public DbSet<WatchedFolder> WatchedFolders => Set<WatchedFolder>();

    /// <summary>Archivos ya vistos en las carpetas vigiladas de la organización activa.</summary>
    public DbSet<WatchedFile> WatchedFiles => Set<WatchedFile>();

    /// <summary>Marcas.</summary>
    public DbSet<Brand> Brands => Set<Brand>();

    /// <summary>Elementos de los perfiles de marca.</summary>
    public DbSet<BrandItem> BrandItems => Set<BrandItem>();

    /// <summary>Briefs de campaña.</summary>
    public DbSet<CampaignBrief> CampaignBriefs => Set<CampaignBrief>();

    /// <summary>Cuentas publicitarias de Meta.</summary>
    public DbSet<MetaAdAccount> MetaAdAccounts => Set<MetaAdAccount>();

    /// <summary>Campañas de Meta.</summary>
    public DbSet<MetaCampaign> MetaCampaigns => Set<MetaCampaign>();

    /// <summary>Conjuntos de anuncios de Meta.</summary>
    public DbSet<MetaAdSet> MetaAdSets => Set<MetaAdSet>();

    /// <summary>Anuncios de Meta.</summary>
    public DbSet<MetaAd> MetaAds => Set<MetaAd>();

    /// <summary>Métricas diarias por anuncio.</summary>
    public DbSet<MetaInsightDaily> MetaInsightsDaily => Set<MetaInsightDaily>();

    /// <summary>Importaciones de métricas.</summary>
    public DbSet<MetaImport> MetaImports => Set<MetaImport>();

    /// <summary>Experimentos.</summary>
    public DbSet<Experiment> Experiments => Set<Experiment>();

    /// <summary>Brazos de los experimentos.</summary>
    public DbSet<ExperimentArm> ExperimentArms => Set<ExperimentArm>();
    /// <summary>Hipótesis.</summary>
    public DbSet<Hypothesis> Hypotheses => Set<Hypothesis>();

    /// <summary>Aprendizajes.</summary>
    public DbSet<Learning> Learnings => Set<Learning>();

    /// <summary>Evidencia de los aprendizajes.</summary>
    public DbSet<LearningEvidence> LearningEvidence => Set<LearningEvidence>();

    /// <summary>Versiones de las ponderaciones de priorización.</summary>
    public DbSet<PriorityWeightSet> PriorityWeightSets => Set<PriorityWeightSet>();

    /// <summary>Planes de producción.</summary>
    public DbSet<ProductionPlan> ProductionPlans => Set<ProductionPlan>();

    /// <summary>Hipótesis de los planes de producción.</summary>
    public DbSet<ProductionPlanItem> ProductionPlanItems => Set<ProductionPlanItem>();

    /// <summary>Políticas de automatización.</summary>
    public DbSet<AutomationPolicy> AutomationPolicies => Set<AutomationPolicy>();

    /// <summary>Acciones de automatización.</summary>
    public DbSet<AutomationAction> AutomationActions => Set<AutomationAction>();

    /// <summary>Activaciones del kill switch.</summary>
    public DbSet<KillSwitchEvent> KillSwitchEvents => Set<KillSwitchEvent>();

    /// <summary>Anomalías detectadas en las métricas.</summary>
    public DbSet<AnomalyEvent> AnomalyEvents => Set<AnomalyEvent>();

    /// <summary>Fuentes externas autorizadas.</summary>
    public DbSet<ResearchSource> ResearchSources => Set<ResearchSource>();

    /// <summary>Referencias externas.</summary>
    public DbSet<ExternalReference> ExternalReferences => Set<ExternalReference>();

    /// <summary>Corridas del agente investigador.</summary>
    public DbSet<ResearchRun> ResearchRuns => Set<ResearchRun>();

    /// <summary>Planes creativos.</summary>
    public DbSet<CreativePlan> CreativePlans => Set<CreativePlan>();

    /// <summary>Conceptos de los planes.</summary>
    public DbSet<CreativeConcept> CreativeConcepts => Set<CreativeConcept>();

    /// <summary>Fragmentos propuestos por los conceptos.</summary>
    public DbSet<ConceptFragment> ConceptFragments => Set<ConceptFragment>();

    /// <summary>Pistas de música.</summary>
    public DbSet<MusicTrack> MusicTracks => Set<MusicTrack>();

    /// <summary>Textos sobre el video de las recetas.</summary>
    public DbSet<RecipeOverlay> RecipeOverlays => Set<RecipeOverlay>();

    /// <summary>Lotes de creatividades.</summary>
    public DbSet<CreativeBatch> CreativeBatches => Set<CreativeBatch>();

    /// <summary>Recetas de creatividades.</summary>
    public DbSet<CreativeRecipe> CreativeRecipes => Set<CreativeRecipe>();

    /// <summary>Tramos de las recetas.</summary>
    public DbSet<RecipeItem> RecipeItems => Set<RecipeItem>();

    /// <summary>Videos generados.</summary>
    public DbSet<CreativeVariant> CreativeVariants => Set<CreativeVariant>();

    /// <summary>Controles de calidad de los videos generados.</summary>
    public DbSet<QualityCheck> QualityChecks => Set<QualityCheck>();

    /// <summary>
    /// Hora UTC de la base (<c>SYSUTCDATETIME()</c>). Sólo se puede usar dentro de una expresión LINQ: así "ahora"
    /// es uno solo para la Web y todos los Workers, sin depender del reloj de cada máquina.
    /// </summary>
    /// <exception cref="InvalidOperationException">Si se invoca fuera de una consulta.</exception>
    public static DateTime UtcNow()
    {
        throw new InvalidOperationException("AppDbContext.UtcNow() sólo se usa dentro de consultas LINQ.");
    }

    /// <summary>Ejecuta <paramref name="work"/> dentro de una transacción y la confirma si no lanzó. Si ya hay
    /// una transacción abierta en este contexto se suma a ella: un servicio puede llamar a otro (por ejemplo,
    /// ingresar un archivo y encolar su análisis) y todo se confirma o se deshace junto.</summary>
    /// <param name="work">Operaciones a ejecutar; pueden llamar varias veces a <c>SaveChangesAsync</c>.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Lo que devuelva <paramref name="work"/>.</returns>
    public async Task<T> InTransactionAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken)
    {
        if (Database.CurrentTransaction is not null)
        {
            return await work();
        }

        await using IDbContextTransaction transaction = await Database.BeginTransactionAsync(cancellationToken);
        T result = await work();
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    /// <inheritdoc />
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        foreach (EntityEntry<IOrganizationScoped> entry in ChangeTracker.Entries<IOrganizationScoped>())
        {
            if (entry.State != EntityState.Added)
            {
                continue;
            }

            if (entry.Entity.OrganizationId == 0)
            {
                entry.Entity.OrganizationId = CurrentOrganizationId;
            }

            if (entry.Entity.OrganizationId == 0)
            {
                throw new InvalidOperationException(
                    $"Se intentó guardar {entry.Metadata.ClrType.Name} sin organización activa.");
            }
        }

        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        builder.HasDbFunction(typeof(AppDbContext).GetMethod(nameof(UtcNow))!)
            .HasName("SYSUTCDATETIME")
            .IsBuiltIn();

        // Un filtro por entidad, a la vista. Un test recorre el modelo y falla si falta alguno.
        builder.Entity<OrganizationMember>().HasQueryFilter(e => e.OrganizationId == CurrentOrganizationId);
        builder.Entity<BackgroundJob>().HasQueryFilter(e => e.OrganizationId == CurrentOrganizationId);
        builder.Entity<AuditEntry>().HasQueryFilter(e => e.OrganizationId == CurrentOrganizationId);
        builder.Entity<AiUsageRecord>().HasQueryFilter(e => e.OrganizationId == CurrentOrganizationId);
        builder.Entity<MediaAsset>().HasQueryFilter(e => e.OrganizationId == CurrentOrganizationId);
        builder.Entity<MediaSegment>().HasQueryFilter(e => e.OrganizationId == CurrentOrganizationId);
        builder.Entity<MediaSegmentTag>().HasQueryFilter(e => e.OrganizationId == CurrentOrganizationId);
        builder.Entity<MediaTranscriptLine>().HasQueryFilter(e => e.OrganizationId == CurrentOrganizationId);
        builder.Entity<SegmentAnalysis>().HasQueryFilter(e => e.OrganizationId == CurrentOrganizationId);
        builder.Entity<SegmentAnalysisFunction>().HasQueryFilter(e => e.OrganizationId == CurrentOrganizationId);
        builder.Entity<AiResultCache>().HasQueryFilter(e => e.OrganizationId == CurrentOrganizationId);
        builder.Entity<SegmentEmbedding>().HasQueryFilter(e => e.OrganizationId == CurrentOrganizationId);
        builder.Entity<WatchedFolder>().HasQueryFilter(e => e.OrganizationId == CurrentOrganizationId);
        builder.Entity<WatchedFile>().HasQueryFilter(e => e.OrganizationId == CurrentOrganizationId);
        builder.Entity<CreativeBatch>().HasQueryFilter(e => e.OrganizationId == CurrentOrganizationId);
        builder.Entity<MusicTrack>().HasQueryFilter(e => e.OrganizationId == CurrentOrganizationId);
        builder.Entity<MetaAdAccount>().HasQueryFilter(e => e.OrganizationId == CurrentOrganizationId);
        builder.Entity<MetaCampaign>().HasQueryFilter(e => e.OrganizationId == CurrentOrganizationId);
        builder.Entity<MetaAdSet>().HasQueryFilter(e => e.OrganizationId == CurrentOrganizationId);
        builder.Entity<MetaAd>().HasQueryFilter(e => e.OrganizationId == CurrentOrganizationId);
        builder.Entity<MetaInsightDaily>().HasQueryFilter(e => e.OrganizationId == CurrentOrganizationId);
        builder.Entity<MetaImport>().HasQueryFilter(e => e.OrganizationId == CurrentOrganizationId);
        builder.Entity<Experiment>().HasQueryFilter(e => e.OrganizationId == CurrentOrganizationId);
        builder.Entity<ExperimentArm>().HasQueryFilter(e => e.OrganizationId == CurrentOrganizationId);
        builder.Entity<Hypothesis>().HasQueryFilter(e => e.OrganizationId == CurrentOrganizationId);
        builder.Entity<Learning>().HasQueryFilter(e => e.OrganizationId == CurrentOrganizationId);
        builder.Entity<LearningEvidence>().HasQueryFilter(e => e.OrganizationId == CurrentOrganizationId);
        builder.Entity<PriorityWeightSet>().HasQueryFilter(e => e.OrganizationId == CurrentOrganizationId);
        builder.Entity<ProductionPlan>().HasQueryFilter(e => e.OrganizationId == CurrentOrganizationId);
        builder.Entity<ProductionPlanItem>().HasQueryFilter(e => e.OrganizationId == CurrentOrganizationId);
        builder.Entity<AutomationPolicy>().HasQueryFilter(e => e.OrganizationId == CurrentOrganizationId);
        builder.Entity<AutomationAction>().HasQueryFilter(e => e.OrganizationId == CurrentOrganizationId);
        builder.Entity<KillSwitchEvent>().HasQueryFilter(e => e.OrganizationId == CurrentOrganizationId);
        builder.Entity<AnomalyEvent>().HasQueryFilter(e => e.OrganizationId == CurrentOrganizationId);
        builder.Entity<ResearchSource>().HasQueryFilter(e => e.OrganizationId == CurrentOrganizationId);
        builder.Entity<ExternalReference>().HasQueryFilter(e => e.OrganizationId == CurrentOrganizationId);
        builder.Entity<ResearchRun>().HasQueryFilter(e => e.OrganizationId == CurrentOrganizationId);
        builder.Entity<CreativePlan>().HasQueryFilter(e => e.OrganizationId == CurrentOrganizationId);
        builder.Entity<CreativeConcept>().HasQueryFilter(e => e.OrganizationId == CurrentOrganizationId);
        builder.Entity<ConceptFragment>().HasQueryFilter(e => e.OrganizationId == CurrentOrganizationId);
        builder.Entity<Brand>().HasQueryFilter(e => e.OrganizationId == CurrentOrganizationId);
        builder.Entity<BrandItem>().HasQueryFilter(e => e.OrganizationId == CurrentOrganizationId);
        builder.Entity<CampaignBrief>().HasQueryFilter(e => e.OrganizationId == CurrentOrganizationId);
        builder.Entity<RecipeOverlay>().HasQueryFilter(e => e.OrganizationId == CurrentOrganizationId);
        builder.Entity<CreativeRecipe>().HasQueryFilter(e => e.OrganizationId == CurrentOrganizationId);
        builder.Entity<RecipeItem>().HasQueryFilter(e => e.OrganizationId == CurrentOrganizationId);
        builder.Entity<CreativeVariant>().HasQueryFilter(e => e.OrganizationId == CurrentOrganizationId);
        builder.Entity<QualityCheck>().HasQueryFilter(e => e.OrganizationId == CurrentOrganizationId);
    }
}

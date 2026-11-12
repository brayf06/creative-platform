using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;

namespace CreativeIntelligence.Web.Data;

/// <summary>Mapeo de <see cref="ApplicationUser"/>: largo del nombre.</summary>
public sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.Property(e => e.FullName).HasMaxLength(150);
    }
}

/// <summary>Mapeo de <see cref="Organization"/>: nombre único.</summary>
public sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.Property(e => e.Name).HasMaxLength(150);
        builder.HasIndex(e => e.Name).IsUnique();
        builder.Property(e => e.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
    }
}

/// <summary>Mapeo de <see cref="OrganizationMember"/>: único por organización y usuario; rol como texto.</summary>
public sealed class OrganizationMemberConfiguration : IEntityTypeConfiguration<OrganizationMember>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OrganizationMember> builder)
    {
        builder.Property(e => e.Role).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.HasIndex(nameof(OrganizationMember.OrganizationId), nameof(OrganizationMember.UserId)).IsUnique();
        builder.HasIndex(e => e.UserId);
        builder.HasOne(e => e.Organization).WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Mapeo de <see cref="BackgroundJob"/>: clave de idempotencia única por organización e índice de la cola.</summary>
public sealed class BackgroundJobConfiguration : IEntityTypeConfiguration<BackgroundJob>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<BackgroundJob> builder)
    {
        builder.Property(e => e.Type).HasMaxLength(100);
        builder.Property(e => e.IdempotencyKey).HasMaxLength(200);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.ProgressStep).HasMaxLength(300);
        builder.Property(e => e.LeaseOwner).HasMaxLength(100);
        builder.Property(e => e.CreatedByUserId).HasMaxLength(450);
        builder.Property(e => e.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property(e => e.UpdatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property(e => e.AvailableAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.HasIndex(nameof(BackgroundJob.OrganizationId), nameof(BackgroundJob.IdempotencyKey)).IsUnique();
        // La toma de trabajos busca por estado y fecha disponible, sin filtrar por organización.
        builder.HasIndex(nameof(BackgroundJob.Status), nameof(BackgroundJob.AvailableAt));
        builder.HasIndex(e => e.UpdatedAt);
        builder.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Mapeo de <see cref="AuditEntry"/>: índice para listar por organización y fecha.</summary>
public sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.Property(e => e.UserId).HasMaxLength(450);
        builder.Property(e => e.UserName).HasMaxLength(256);
        builder.Property(e => e.Action).HasMaxLength(100);
        builder.Property(e => e.EntityType).HasMaxLength(100);
        builder.Property(e => e.EntityId).HasMaxLength(100);
        builder.Property(e => e.Summary).HasMaxLength(1000);
        builder.Property(e => e.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.HasIndex(nameof(IOrganizationScoped.OrganizationId), "CreatedAt");
        builder.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Mapeo de <see cref="AiUsageRecord"/>: costo con 6 decimales e índice por organización y fecha.</summary>
public sealed class AiUsageRecordConfiguration : IEntityTypeConfiguration<AiUsageRecord>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<AiUsageRecord> builder)
    {
        builder.Property(e => e.Task).HasMaxLength(100);
        builder.Property(e => e.Provider).HasMaxLength(100);
        builder.Property(e => e.Model).HasMaxLength(100);
        builder.Property(e => e.PromptVersion).HasMaxLength(50);
        builder.Property(e => e.CostUsd).HasPrecision(18, 6);
        builder.Property(e => e.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.HasIndex(nameof(IOrganizationScoped.OrganizationId), "CreatedAt");
        builder.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BackgroundJob>().WithMany().HasForeignKey(e => e.BackgroundJobId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Mapeo de <see cref="MediaAsset"/>: un solo archivo por organización y hash de contenido.</summary>
public sealed class MediaAssetConfiguration : IEntityTypeConfiguration<MediaAsset>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MediaAsset> builder)
    {
        builder.Property(e => e.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.SourceKind).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.OriginalFileName).HasMaxLength(260);
        builder.Property(e => e.ContentType).HasMaxLength(100);
        builder.Property(e => e.Sha256).HasMaxLength(64).IsFixedLength().IsUnicode(false);
        builder.Property(e => e.StoragePath).HasMaxLength(300);
        builder.Property(e => e.SourceReference).HasMaxLength(500);
        builder.Property(e => e.ProcessingStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.ProcessingError).HasMaxLength(1000);
        builder.Property(e => e.FrameRate).HasPrecision(7, 3);
        builder.Property(e => e.VideoCodec).HasMaxLength(50);
        builder.Property(e => e.AudioCodec).HasMaxLength(50);
        builder.Property(e => e.Container).HasMaxLength(100);
        builder.Property(e => e.ThumbnailPath).HasMaxLength(300);
        builder.Property(e => e.PerceptualHash).HasMaxLength(80).IsUnicode(false);
        builder.HasOne<MediaAsset>().WithMany().HasForeignKey(e => e.DuplicateOfAssetId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(e => e.SceneStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.SceneError).HasMaxLength(1000);
        builder.Property(e => e.AnalysisStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.AnalysisError).HasMaxLength(1000);
        builder.Property(e => e.TranscriptStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.TranscriptError).HasMaxLength(1000);
        builder.Property(e => e.TranscriptLanguage).HasMaxLength(10);
        builder.Property(e => e.TranscriptModel).HasMaxLength(100);
        builder.Property(e => e.RightsHolder).HasMaxLength(200);
        builder.Property(e => e.LicenseNotes).HasMaxLength(1000);
        builder.Property(e => e.CreatedByUserId).HasMaxLength(450);
        builder.Property(e => e.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.HasIndex(nameof(MediaAsset.OrganizationId), nameof(MediaAsset.Sha256)).IsUnique();
        builder.HasIndex(nameof(MediaAsset.OrganizationId), nameof(MediaAsset.CreatedAt));
        builder.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Mapeo de <see cref="MediaSegment"/>: índice por video y orden temporal; el fin es mayor que el inicio.</summary>
public sealed class MediaSegmentConfiguration : IEntityTypeConfiguration<MediaSegment>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MediaSegment> builder)
    {
        builder.Property(e => e.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Origin).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.ThumbnailPath).HasMaxLength(300);
        builder.Property(e => e.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property(e => e.AudioMeanDb).HasPrecision(5, 1);
        builder.Property(e => e.AudioPeakDb).HasPrecision(5, 1);
        builder.ToTable(t => t.HasCheckConstraint("CK_MediaSegments_Range", "[StartMs] >= 0 AND [EndMs] > [StartMs]"));
        builder.HasIndex(nameof(MediaSegment.MediaAssetId), nameof(MediaSegment.Kind), nameof(MediaSegment.StartMs));
        builder.HasIndex(nameof(MediaSegment.OrganizationId));
        builder.HasOne(e => e.MediaAsset).WithMany().HasForeignKey(e => e.MediaAssetId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Mapeo de <see cref="MediaSegmentTag"/>: una etiqueta no se repite dentro de un fragmento.</summary>
public sealed class MediaSegmentTagConfiguration : IEntityTypeConfiguration<MediaSegmentTag>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MediaSegmentTag> builder)
    {
        builder.Property(e => e.Text).HasMaxLength(50);
        builder.Property(e => e.Origin).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(nameof(MediaSegmentTag.MediaSegmentId), nameof(MediaSegmentTag.Text)).IsUnique();
        builder.HasIndex(nameof(MediaSegmentTag.OrganizationId), nameof(MediaSegmentTag.Text));
        builder.HasOne<MediaSegment>().WithMany(s => s.Tags).HasForeignKey(e => e.MediaSegmentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Mapeo de <see cref="MediaTranscriptLine"/>: índice por video y orden temporal.</summary>
public sealed class MediaTranscriptLineConfiguration : IEntityTypeConfiguration<MediaTranscriptLine>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MediaTranscriptLine> builder)
    {
        builder.Property(e => e.Text).HasMaxLength(1000);
        builder.HasIndex(nameof(MediaTranscriptLine.MediaAssetId), nameof(MediaTranscriptLine.StartMs));
        builder.HasIndex(nameof(MediaTranscriptLine.OrganizationId));
        builder.HasOne<MediaAsset>().WithMany().HasForeignKey(e => e.MediaAssetId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Mapeo de <see cref="SegmentAnalysis"/>: índice por fragmento y vigencia.</summary>
public sealed class SegmentAnalysisConfiguration : IEntityTypeConfiguration<SegmentAnalysis>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SegmentAnalysis> builder)
    {
        builder.Property(e => e.VisualDescription).HasMaxLength(1000);
        builder.Property(e => e.OnScreenText).HasMaxLength(500);
        builder.Property(e => e.Product).HasMaxLength(200);
        builder.Property(e => e.Actions).HasMaxLength(500);
        builder.Property(e => e.CommercialAngle).HasMaxLength(300);
        builder.Property(e => e.MainMessage).HasMaxLength(500);
        builder.Property(e => e.Audience).HasMaxLength(300);
        builder.Property(e => e.Confidence).HasPrecision(4, 3);
        builder.Property(e => e.Provider).HasMaxLength(50);
        builder.Property(e => e.Model).HasMaxLength(100);
        builder.Property(e => e.PromptVersion).HasMaxLength(50);
        builder.Property(e => e.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.HasIndex(nameof(SegmentAnalysis.MediaSegmentId), nameof(SegmentAnalysis.IsCurrent));
        builder.HasIndex(nameof(SegmentAnalysis.OrganizationId));
        builder.HasOne(e => e.MediaSegment).WithMany().HasForeignKey(e => e.MediaSegmentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Mapeo de <see cref="SegmentAnalysisFunction"/>: una función no se repite dentro de un análisis.</summary>
public sealed class SegmentAnalysisFunctionConfiguration : IEntityTypeConfiguration<SegmentAnalysisFunction>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SegmentAnalysisFunction> builder)
    {
        builder.Property(e => e.Function).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(nameof(SegmentAnalysisFunction.SegmentAnalysisId), nameof(SegmentAnalysisFunction.Function)).IsUnique();
        builder.HasIndex(nameof(SegmentAnalysisFunction.OrganizationId), nameof(SegmentAnalysisFunction.Function));
        builder.HasOne<SegmentAnalysis>().WithMany(a => a.Functions).HasForeignKey(e => e.SegmentAnalysisId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Mapeo de <see cref="AiResultCache"/>: clave única por organización.</summary>
public sealed class AiResultCacheConfiguration : IEntityTypeConfiguration<AiResultCache>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<AiResultCache> builder)
    {
        builder.Property(e => e.CacheKey).HasMaxLength(64).IsFixedLength().IsUnicode(false);
        builder.Property(e => e.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.HasIndex(nameof(AiResultCache.OrganizationId), nameof(AiResultCache.CacheKey)).IsUnique();
        builder.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Mapeo de <see cref="SegmentEmbedding"/>: un vector por fragmento y modelo.</summary>
public sealed class SegmentEmbeddingConfiguration : IEntityTypeConfiguration<SegmentEmbedding>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SegmentEmbedding> builder)
    {
        builder.Property(e => e.Model).HasMaxLength(100);
        builder.Property(e => e.TextHash).HasMaxLength(64).IsFixedLength().IsUnicode(false);
        builder.Property(e => e.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.HasIndex(nameof(SegmentEmbedding.MediaSegmentId), nameof(SegmentEmbedding.Model)).IsUnique();
        builder.HasIndex(nameof(SegmentEmbedding.OrganizationId), nameof(SegmentEmbedding.Model));
        builder.HasOne<MediaSegment>().WithMany().HasForeignKey(e => e.MediaSegmentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Mapeo de <see cref="WatchedFolder"/>: una ruta no se vigila dos veces en la misma organización.</summary>
public sealed class WatchedFolderConfiguration : IEntityTypeConfiguration<WatchedFolder>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<WatchedFolder> builder)
    {
        builder.Property(e => e.Path).HasMaxLength(500);
        builder.Property(e => e.RightsHolder).HasMaxLength(200);
        builder.Property(e => e.LicenseNotes).HasMaxLength(1000);
        builder.Property(e => e.LastScanSummary).HasMaxLength(500);
        builder.Property(e => e.CreatedByUserId).HasMaxLength(450);
        builder.Property(e => e.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.HasIndex(nameof(WatchedFolder.OrganizationId), nameof(WatchedFolder.Path)).IsUnique();
        builder.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Mapeo de <see cref="WatchedFile"/>: un registro por archivo de la carpeta.</summary>
public sealed class WatchedFileConfiguration : IEntityTypeConfiguration<WatchedFile>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<WatchedFile> builder)
    {
        builder.Property(e => e.RelativePath).HasMaxLength(400);
        builder.Property(e => e.Outcome).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Message).HasMaxLength(500);
        builder.HasIndex(nameof(WatchedFile.WatchedFolderId), nameof(WatchedFile.RelativePath)).IsUnique();
        builder.HasIndex(nameof(WatchedFile.OrganizationId));
        builder.HasOne<WatchedFolder>().WithMany().HasForeignKey(e => e.WatchedFolderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<MediaAsset>().WithMany().HasForeignKey(e => e.MediaAssetId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Mapeo de <see cref="CreativeRecipe"/>.</summary>
public sealed class CreativeRecipeConfiguration : IEntityTypeConfiguration<CreativeRecipe>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<CreativeRecipe> builder)
    {
        builder.Property(e => e.Name).HasMaxLength(200);
        builder.Property(e => e.Concept).HasMaxLength(1000);
        builder.Property(e => e.Hypothesis).HasMaxLength(1000);
        builder.Property(e => e.CreatedByUserId).HasMaxLength(450);
        builder.Property(e => e.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.HasIndex(nameof(CreativeRecipe.OrganizationId), nameof(CreativeRecipe.CreatedAt));
        builder.HasMany(e => e.Items).WithOne().HasForeignKey(i => i.CreativeRecipeId).OnDelete(DeleteBehavior.Cascade);
        builder.Property(e => e.Transition).HasConversion<string>().HasMaxLength(20).HasDefaultValue(RecipeTransition.Cut);
        builder.Property(e => e.MusicVolumePercent).HasDefaultValue(25);
        builder.HasMany(e => e.Overlays).WithOne().HasForeignKey(o => o.CreativeRecipeId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<MusicTrack>().WithMany().HasForeignKey(e => e.MusicTrackId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(nameof(CreativeRecipe.CreativeBatchId));
        builder.HasOne<CreativeBatch>().WithMany().HasForeignKey(e => e.CreativeBatchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Mapeo de <see cref="RecipeItem"/>: el original no se puede borrar mientras una receta lo use; el
/// fragmento sí puede desaparecer, y la receta conserva su tramo.</summary>
public sealed class RecipeItemConfiguration : IEntityTypeConfiguration<RecipeItem>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RecipeItem> builder)
    {
        builder.Property(e => e.Role).HasConversion<string>().HasMaxLength(30);
        builder.ToTable(t => t.HasCheckConstraint("CK_RecipeItems_Range", "[EndMs] > [StartMs] AND [StartMs] >= 0"));
        builder.HasIndex(nameof(RecipeItem.CreativeRecipeId), nameof(RecipeItem.Position)).IsUnique();
        builder.HasIndex(nameof(RecipeItem.MediaAssetId));
        builder.HasIndex(nameof(RecipeItem.OrganizationId));
        builder.HasOne(e => e.MediaAsset).WithMany().HasForeignKey(e => e.MediaAssetId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MediaSegment>().WithMany().HasForeignKey(e => e.MediaSegmentId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Mapeo de <see cref="CreativeVariant"/>.</summary>
public sealed class CreativeVariantConfiguration : IEntityTypeConfiguration<CreativeVariant>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<CreativeVariant> builder)
    {
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Error).HasMaxLength(1000);
        builder.Property(e => e.StoragePath).HasMaxLength(400);
        builder.Property(e => e.CreatedByUserId).HasMaxLength(450);
        builder.Property(e => e.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.HasIndex(nameof(CreativeVariant.CreativeRecipeId));
        builder.HasIndex(nameof(CreativeVariant.OrganizationId));
        builder.HasOne<CreativeRecipe>().WithMany().HasForeignKey(e => e.CreativeRecipeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(e => e.Checks).WithOne().HasForeignKey(c => c.CreativeVariantId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Mapeo de <see cref="QualityCheck"/>: una regla se registra una vez por video.</summary>
public sealed class QualityCheckConfiguration : IEntityTypeConfiguration<QualityCheck>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<QualityCheck> builder)
    {
        builder.Property(e => e.Rule).HasMaxLength(50);
        builder.Property(e => e.Detail).HasMaxLength(500);
        builder.HasIndex(nameof(QualityCheck.CreativeVariantId), nameof(QualityCheck.Rule)).IsUnique();
        builder.HasIndex(nameof(QualityCheck.OrganizationId));
        builder.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Mapeo de <see cref="CreativeBatch"/>.</summary>
public sealed class CreativeBatchConfiguration : IEntityTypeConfiguration<CreativeBatch>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<CreativeBatch> builder)
    {
        builder.Property(e => e.Name).HasMaxLength(200);
        builder.Property(e => e.Brief).HasMaxLength(2000);
        builder.Property(e => e.SimilarityThreshold).HasPrecision(4, 3);
        builder.Property(e => e.CreatedByUserId).HasMaxLength(450);
        builder.Property(e => e.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.HasIndex(nameof(CreativeBatch.OrganizationId), nameof(CreativeBatch.CreatedAt));
        builder.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Mapeo de <see cref="RecipeOverlay"/>.</summary>
public sealed class RecipeOverlayConfiguration : IEntityTypeConfiguration<RecipeOverlay>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RecipeOverlay> builder)
    {
        builder.Property(e => e.Text).HasMaxLength(120);
        builder.Property(e => e.Placement).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Moment).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(nameof(RecipeOverlay.CreativeRecipeId), nameof(RecipeOverlay.Position)).IsUnique();
        builder.HasIndex(nameof(RecipeOverlay.OrganizationId));
        builder.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Mapeo de <see cref="MusicTrack"/>: el mismo contenido no se carga dos veces en una organización.</summary>
public sealed class MusicTrackConfiguration : IEntityTypeConfiguration<MusicTrack>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MusicTrack> builder)
    {
        builder.Property(e => e.Name).HasMaxLength(200);
        builder.Property(e => e.OriginalFileName).HasMaxLength(260);
        builder.Property(e => e.Sha256).HasMaxLength(64).IsUnicode(false);
        builder.Property(e => e.StoragePath).HasMaxLength(400);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Error).HasMaxLength(500);
        builder.Property(e => e.RightsHolder).HasMaxLength(200);
        builder.Property(e => e.LicenseNotes).HasMaxLength(1000);
        builder.Property(e => e.CreatedByUserId).HasMaxLength(450);
        builder.Property(e => e.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.HasIndex(nameof(MusicTrack.OrganizationId), nameof(MusicTrack.Sha256)).IsUnique();
        builder.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Mapeo de <see cref="Brand"/>: el nombre no se repite en la organización.</summary>
public sealed class BrandConfiguration : IEntityTypeConfiguration<Brand>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Brand> builder)
    {
        builder.Property(e => e.Name).HasMaxLength(150);
        builder.Property(e => e.ValueProposition).HasMaxLength(1000);
        builder.Property(e => e.CommunicationStyle).HasMaxLength(1000);
        builder.Property(e => e.VisualIdentity).HasMaxLength(1000);
        builder.Property(e => e.Objectives).HasMaxLength(1000);
        builder.Property(e => e.CommercialRestrictions).HasMaxLength(1000);
        builder.Property(e => e.MonthlyBudgetUsd).HasPrecision(12, 2);
        builder.Property(e => e.CreatedByUserId).HasMaxLength(450);
        builder.Property(e => e.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.HasIndex(nameof(Brand.OrganizationId), nameof(Brand.Name)).IsUnique();
        builder.HasMany(e => e.Items).WithOne().HasForeignKey(i => i.BrandId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Mapeo de <see cref="BrandItem"/>: un texto no se repite en la misma lista de la marca.</summary>
public sealed class BrandItemConfiguration : IEntityTypeConfiguration<BrandItem>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<BrandItem> builder)
    {
        builder.Property(e => e.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Text).HasMaxLength(300);
        builder.Property(e => e.Detail).HasMaxLength(1000);
        builder.Property(e => e.MarginPercent).HasPrecision(5, 2);
        builder.HasIndex(nameof(BrandItem.BrandId), nameof(BrandItem.Kind), nameof(BrandItem.Text)).IsUnique();
        builder.HasIndex(nameof(BrandItem.OrganizationId));
        builder.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Mapeo de <see cref="CampaignBrief"/>: ni la marca ni el producto se pueden borrar mientras un brief los use.</summary>
public sealed class CampaignBriefConfiguration : IEntityTypeConfiguration<CampaignBrief>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<CampaignBrief> builder)
    {
        builder.Property(e => e.Name).HasMaxLength(200);
        builder.Property(e => e.OriginalText).HasMaxLength(4000);
        builder.Property(e => e.Occasion).HasMaxLength(200);
        builder.Property(e => e.Audience).HasMaxLength(500);
        builder.Property(e => e.Objective).HasMaxLength(500);
        builder.Property(e => e.Message).HasMaxLength(1000);
        builder.Property(e => e.Approaches).HasMaxLength(1000);
        builder.Property(e => e.Platform).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Autonomy).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.MaxGenerationBudgetUsd).HasPrecision(12, 2);
        builder.Property(e => e.CreatedByUserId).HasMaxLength(450);
        builder.Property(e => e.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.HasIndex(nameof(CampaignBrief.OrganizationId), nameof(CampaignBrief.CreatedAt));
        builder.HasOne(e => e.Brand).WithMany().HasForeignKey(e => e.BrandId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BrandItem>().WithMany().HasForeignKey(e => e.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Mapeo de <see cref="CreativePlan"/>.</summary>
public sealed class CreativePlanConfiguration : IEntityTypeConfiguration<CreativePlan>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<CreativePlan> builder)
    {
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Error).HasMaxLength(1000);
        builder.Property(e => e.Provider).HasMaxLength(50);
        builder.Property(e => e.Model).HasMaxLength(100);
        builder.Property(e => e.PromptVersion).HasMaxLength(50);
        builder.Property(e => e.CostUsd).HasPrecision(12, 6);
        builder.Property(e => e.CreatedByUserId).HasMaxLength(450);
        builder.Property(e => e.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.HasIndex(nameof(CreativePlan.CampaignBriefId));
        builder.HasIndex(nameof(CreativePlan.OrganizationId));
        builder.HasOne(e => e.Brief).WithMany().HasForeignKey(e => e.CampaignBriefId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(e => e.Concepts).WithOne().HasForeignKey(c => c.CreativePlanId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Mapeo de <see cref="CreativeConcept"/>.</summary>
public sealed class CreativeConceptConfiguration : IEntityTypeConfiguration<CreativeConcept>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<CreativeConcept> builder)
    {
        builder.Property(e => e.Title).HasMaxLength(180);
        builder.Property(e => e.Hypothesis).HasMaxLength(1000);
        builder.Property(e => e.Approach).HasMaxLength(500);
        builder.Property(e => e.Hook).HasMaxLength(1000);
        builder.Property(e => e.Script).HasMaxLength(4000);
        builder.Property(e => e.Development).HasMaxLength(2000);
        builder.Property(e => e.Cta).HasMaxLength(500);
        builder.Property(e => e.Audience).HasMaxLength(500);
        builder.Property(e => e.RequiredMaterial).HasMaxLength(1000);
        builder.Property(e => e.Reason).HasMaxLength(1000);
        builder.Property(e => e.Risks).HasMaxLength(1000);
        builder.Property(e => e.MissingText).HasMaxLength(2000);
        builder.Property(e => e.ViolationsText).HasMaxLength(1000);
        builder.HasIndex(nameof(CreativeConcept.CreativePlanId), nameof(CreativeConcept.Position)).IsUnique();
        builder.HasIndex(nameof(CreativeConcept.OrganizationId));
        builder.HasOne<CreativeRecipe>().WithMany().HasForeignKey(e => e.CreativeRecipeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(e => e.Fragments).WithOne().HasForeignKey(f => f.CreativeConceptId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Mapeo de <see cref="ConceptFragment"/>: el original no se borra mientras un concepto lo use; el fragmento
/// puede desaparecer.</summary>
public sealed class ConceptFragmentConfiguration : IEntityTypeConfiguration<ConceptFragment>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ConceptFragment> builder)
    {
        builder.HasIndex(nameof(ConceptFragment.CreativeConceptId), nameof(ConceptFragment.Position)).IsUnique();
        builder.HasIndex(nameof(ConceptFragment.OrganizationId));
        builder.HasOne(e => e.MediaAsset).WithMany().HasForeignKey(e => e.MediaAssetId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MediaSegment>().WithMany().HasForeignKey(e => e.MediaSegmentId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Mapeo de <see cref="ResearchSource"/>: un dominio se autoriza una vez por organización.</summary>
public sealed class ResearchSourceConfiguration : IEntityTypeConfiguration<ResearchSource>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ResearchSource> builder)
    {
        builder.Property(e => e.Name).HasMaxLength(150);
        builder.Property(e => e.Domain).HasMaxLength(200);
        builder.Property(e => e.ConditionsOfUse).HasMaxLength(1000);
        builder.Property(e => e.DefaultUsage).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.CreatedByUserId).HasMaxLength(450);
        builder.Property(e => e.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.HasIndex(nameof(ResearchSource.OrganizationId), nameof(ResearchSource.Domain)).IsUnique();
        builder.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Mapeo de <see cref="ExternalReference"/>: una dirección se guarda una vez por organización.</summary>
public sealed class ExternalReferenceConfiguration : IEntityTypeConfiguration<ExternalReference>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ExternalReference> builder)
    {
        builder.Property(e => e.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Url).HasMaxLength(450);
        builder.Property(e => e.Title).HasMaxLength(300);
        builder.Property(e => e.Summary).HasMaxLength(2000);
        builder.Property(e => e.Usage).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Origin).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.CreatedByUserId).HasMaxLength(450);
        builder.Property(e => e.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.HasIndex(nameof(ExternalReference.OrganizationId), nameof(ExternalReference.Url)).IsUnique();
        builder.HasIndex(nameof(ExternalReference.CampaignBriefId));
        builder.HasOne(e => e.Source).WithMany().HasForeignKey(e => e.ResearchSourceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CampaignBrief>().WithMany().HasForeignKey(e => e.CampaignBriefId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ResearchRun>().WithMany().HasForeignKey(e => e.ResearchRunId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Mapeo de <see cref="ResearchRun"/>.</summary>
public sealed class ResearchRunConfiguration : IEntityTypeConfiguration<ResearchRun>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ResearchRun> builder)
    {
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Error).HasMaxLength(1000);
        builder.Property(e => e.Model).HasMaxLength(100);
        builder.Property(e => e.PromptVersion).HasMaxLength(50);
        builder.Property(e => e.CostUsd).HasPrecision(12, 6);
        builder.Property(e => e.CreatedByUserId).HasMaxLength(450);
        builder.Property(e => e.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.HasIndex(nameof(ResearchRun.CampaignBriefId));
        builder.HasIndex(nameof(ResearchRun.OrganizationId));
        builder.HasOne<CampaignBrief>().WithMany().HasForeignKey(e => e.CampaignBriefId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Mapeo de <see cref="MetaAdAccount"/>.</summary>
public sealed class MetaAdAccountConfiguration : IEntityTypeConfiguration<MetaAdAccount>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MetaAdAccount> builder)
    {
        builder.Property(e => e.ExternalId).HasMaxLength(100);
        builder.Property(e => e.Name).HasMaxLength(200);
        builder.Property(e => e.Currency).HasMaxLength(3).IsUnicode(false);
        builder.HasIndex(nameof(MetaAdAccount.OrganizationId), nameof(MetaAdAccount.ExternalId)).IsUnique();
        builder.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Mapeo de <see cref="MetaCampaign"/>.</summary>
public sealed class MetaCampaignConfiguration : IEntityTypeConfiguration<MetaCampaign>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MetaCampaign> builder)
    {
        builder.Property(e => e.ExternalId).HasMaxLength(300);
        builder.Property(e => e.Name).HasMaxLength(300);
        builder.Property(e => e.Objective).HasMaxLength(100);
        builder.HasIndex(nameof(MetaCampaign.MetaAdAccountId), nameof(MetaCampaign.ExternalId)).IsUnique();
        builder.HasIndex(nameof(MetaCampaign.OrganizationId));
        builder.HasOne<MetaAdAccount>().WithMany().HasForeignKey(e => e.MetaAdAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Mapeo de <see cref="MetaAdSet"/>.</summary>
public sealed class MetaAdSetConfiguration : IEntityTypeConfiguration<MetaAdSet>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MetaAdSet> builder)
    {
        builder.Property(e => e.ExternalId).HasMaxLength(300);
        builder.Property(e => e.Name).HasMaxLength(300);
        builder.HasIndex(nameof(MetaAdSet.MetaCampaignId), nameof(MetaAdSet.ExternalId)).IsUnique();
        builder.HasIndex(nameof(MetaAdSet.OrganizationId));
        builder.HasOne(e => e.Campaign).WithMany().HasForeignKey(e => e.MetaCampaignId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Mapeo de <see cref="MetaAd"/>: un anuncio se guarda una vez por organización; el video vinculado no se
/// puede borrar mientras un anuncio lo use.</summary>
public sealed class MetaAdConfiguration : IEntityTypeConfiguration<MetaAd>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MetaAd> builder)
    {
        builder.Property(e => e.ExternalId).HasMaxLength(100);
        builder.Property(e => e.Name).HasMaxLength(300);
        builder.Property(e => e.LinkOrigin).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.LinkedByUserId).HasMaxLength(450);
        builder.HasIndex(nameof(MetaAd.OrganizationId), nameof(MetaAd.ExternalId)).IsUnique();
        builder.HasIndex(nameof(MetaAd.CreativeVariantId));
        builder.HasOne(e => e.AdSet).WithMany().HasForeignKey(e => e.MetaAdSetId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CreativeVariant>().WithMany().HasForeignKey(e => e.CreativeVariantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Mapeo de <see cref="MetaInsightDaily"/>: una fila por anuncio y día.</summary>
public sealed class MetaInsightDailyConfiguration : IEntityTypeConfiguration<MetaInsightDaily>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MetaInsightDaily> builder)
    {
        builder.ToTable("MetaInsightsDaily");
        builder.Property(e => e.Spend).HasPrecision(14, 2);
        builder.Property(e => e.Purchases).HasPrecision(12, 2);
        builder.Property(e => e.PurchaseValue).HasPrecision(14, 2);
        builder.HasIndex(nameof(MetaInsightDaily.MetaAdId), nameof(MetaInsightDaily.Date)).IsUnique();
        builder.HasIndex(nameof(MetaInsightDaily.OrganizationId), nameof(MetaInsightDaily.Date));
        builder.HasOne<MetaAd>().WithMany().HasForeignKey(e => e.MetaAdId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Mapeo de <see cref="MetaImport"/>.</summary>
public sealed class MetaImportConfiguration : IEntityTypeConfiguration<MetaImport>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MetaImport> builder)
    {
        builder.Property(e => e.FileName).HasMaxLength(260);
        builder.Property(e => e.StoragePath).HasMaxLength(400);
        builder.Property(e => e.DefaultCurrency).HasMaxLength(3).IsUnicode(false);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Notes).HasMaxLength(2000);
        builder.Property(e => e.CreatedByUserId).HasMaxLength(450);
        builder.Property(e => e.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.HasIndex(nameof(MetaImport.OrganizationId), nameof(MetaImport.CreatedAt));
        builder.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Mapeo de <see cref="Experiment"/>.</summary>
public sealed class ExperimentConfiguration : IEntityTypeConfiguration<Experiment>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Experiment> builder)
    {
        builder.Property(e => e.Name).HasMaxLength(200);
        builder.Property(e => e.Hypothesis).HasMaxLength(1000);
        builder.Property(e => e.Variable).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Metric).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Conclusion).HasMaxLength(2000);
        builder.Property(e => e.CreatedByUserId).HasMaxLength(450);
        builder.Property(e => e.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.HasIndex(nameof(Experiment.OrganizationId), nameof(Experiment.CreatedAt));
        builder.HasMany(e => e.Arms).WithOne().HasForeignKey(a => a.ExperimentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Hypothesis>().WithMany().HasForeignKey(e => e.HypothesisId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Mapeo de <see cref="ExperimentArm"/>: un anuncio está en un solo brazo de un experimento.</summary>
public sealed class ExperimentArmConfiguration : IEntityTypeConfiguration<ExperimentArm>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ExperimentArm> builder)
    {
        builder.Property(e => e.Name).HasMaxLength(100);
        builder.HasIndex(nameof(ExperimentArm.ExperimentId), nameof(ExperimentArm.Position)).IsUnique();
        builder.HasIndex(nameof(ExperimentArm.ExperimentId), nameof(ExperimentArm.MetaAdId)).IsUnique();
        builder.HasIndex(nameof(ExperimentArm.OrganizationId));
        builder.HasOne<MetaAd>().WithMany().HasForeignKey(e => e.MetaAdId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Mapeo de <see cref="Hypothesis"/>.</summary>
public sealed class HypothesisConfiguration : IEntityTypeConfiguration<Hypothesis>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Hypothesis> builder)
    {
        builder.ToTable("Hypotheses");
        builder.Property(e => e.Statement).HasMaxLength(1000);
        builder.Property(e => e.Origin).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Variable).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.PatternDimension).HasMaxLength(20);
        builder.Property(e => e.PatternKey).HasMaxLength(300);
        builder.Property(e => e.PatternLabel).HasMaxLength(300);
        builder.Property(e => e.PatternReading).HasMaxLength(1000);
        builder.Property(e => e.DiscardReason).HasMaxLength(500);
        builder.Property(e => e.AudienceRelevance).HasConversion<string>().HasMaxLength(10);
        builder.Property(e => e.ProductionCost).HasConversion<string>().HasMaxLength(10);
        builder.Property(e => e.AdRisk).HasConversion<string>().HasMaxLength(10);
        builder.Property(e => e.PotentialMargin).HasConversion<string>().HasMaxLength(10);
        builder.Property(e => e.CreatedByUserId).HasMaxLength(450);
        builder.Property(e => e.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.HasIndex(nameof(Hypothesis.OrganizationId), nameof(Hypothesis.CreatedAt));
        builder.HasOne<MetaCampaign>().WithMany().HasForeignKey(e => e.PatternCampaignId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Mapeo de <see cref="Learning"/>.</summary>
public sealed class LearningConfiguration : IEntityTypeConfiguration<Learning>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Learning> builder)
    {
        builder.Property(e => e.Statement).HasMaxLength(1000);
        builder.Property(e => e.DataUsed).HasMaxLength(1000);
        builder.Property(e => e.Result).HasMaxLength(2000);
        builder.Property(e => e.Confidence).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.ConfidenceReason).HasMaxLength(500);
        builder.Property(e => e.Recommendation).HasMaxLength(1000);
        builder.Property(e => e.Applicability).HasMaxLength(1000);
        builder.Property(e => e.CriteriaVersion).HasMaxLength(50);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.StatusReason).HasMaxLength(1000);
        builder.Property(e => e.CreatedByUserId).HasMaxLength(450);
        builder.Property(e => e.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.HasIndex(nameof(Learning.OrganizationId), nameof(Learning.Status), nameof(Learning.CreatedAt));
        builder.HasMany(e => e.Evidence).WithOne().HasForeignKey(e => e.LearningId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Hypothesis>().WithMany().HasForeignKey(e => e.HypothesisId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Experiment>().WithMany().HasForeignKey(e => e.ExperimentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Learning>().WithMany().HasForeignKey(e => e.ReplacesLearningId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Mapeo de <see cref="LearningEvidence"/>.</summary>
public sealed class LearningEvidenceConfiguration : IEntityTypeConfiguration<LearningEvidence>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<LearningEvidence> builder)
    {
        builder.ToTable("LearningEvidence");
        builder.HasIndex(nameof(LearningEvidence.OrganizationId));
        builder.HasOne<MetaAd>().WithMany().HasForeignKey(e => e.MetaAdId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CreativeVariant>().WithMany().HasForeignKey(e => e.CreativeVariantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MediaAsset>().WithMany().HasForeignKey(e => e.MediaAssetId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Mapeo de <see cref="PriorityWeightSet"/>: el número de versión no se repite dentro de una organización.</summary>
public sealed class PriorityWeightSetConfiguration : IEntityTypeConfiguration<PriorityWeightSet>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PriorityWeightSet> builder)
    {
        builder.Property(e => e.Note).HasMaxLength(500);
        builder.Property(e => e.CreatedByUserId).HasMaxLength(450);
        builder.Property(e => e.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.HasIndex(nameof(PriorityWeightSet.OrganizationId), nameof(PriorityWeightSet.Version)).IsUnique();
        builder.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Mapeo de <see cref="ProductionPlan"/>.</summary>
public sealed class ProductionPlanConfiguration : IEntityTypeConfiguration<ProductionPlan>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ProductionPlan> builder)
    {
        builder.Property(e => e.Name).HasMaxLength(200);
        builder.Property(e => e.CreatedByUserId).HasMaxLength(450);
        builder.Property(e => e.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.HasIndex(nameof(ProductionPlan.OrganizationId), nameof(ProductionPlan.CreatedAt));
        builder.HasMany(e => e.Items).WithOne().HasForeignKey(i => i.ProductionPlanId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Mapeo de <see cref="ProductionPlanItem"/>: una hipótesis aparece una sola vez en un plan.</summary>
public sealed class ProductionPlanItemConfiguration : IEntityTypeConfiguration<ProductionPlanItem>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ProductionPlanItem> builder)
    {
        builder.Property(e => e.Statement).HasMaxLength(1000);
        builder.Property(e => e.Variable).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Component).HasMaxLength(300);
        builder.Property(e => e.Action).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Score).HasPrecision(5, 1);
        builder.Property(e => e.Justification).HasMaxLength(4000);
        builder.Property(e => e.Recommendation).HasMaxLength(1000);
        builder.HasIndex(nameof(ProductionPlanItem.ProductionPlanId), nameof(ProductionPlanItem.Position)).IsUnique();
        builder.HasIndex(nameof(ProductionPlanItem.ProductionPlanId), nameof(ProductionPlanItem.HypothesisId)).IsUnique();
        builder.HasIndex(nameof(ProductionPlanItem.OrganizationId));
        builder.HasOne<Hypothesis>().WithMany().HasForeignKey(e => e.HypothesisId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Learning>().WithMany().HasForeignKey(e => e.LearningId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MediaAsset>().WithMany().HasForeignKey(e => e.MediaAssetId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Mapeo de <see cref="AutomationPolicy"/>: una por organización.</summary>
public sealed class AutomationPolicyConfiguration : IEntityTypeConfiguration<AutomationPolicy>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<AutomationPolicy> builder)
    {
        builder.ToTable("AutomationPolicies");
        builder.Property(e => e.Mode).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.MaxBudgetPerAction).HasPrecision(18, 2);
        builder.Property(e => e.MaxDailyBudget).HasPrecision(18, 2);
        builder.Property(e => e.MaxBatchBudget).HasPrecision(18, 2);
        builder.HasIndex(e => e.OrganizationId).IsUnique();
        builder.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Mapeo de <see cref="AutomationAction"/>.</summary>
public sealed class AutomationActionConfiguration : IEntityTypeConfiguration<AutomationAction>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<AutomationAction> builder)
    {
        builder.Property(e => e.AdName).HasMaxLength(300);
        builder.Property(e => e.DailyBudget).HasPrecision(18, 2);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.ApprovalReason).HasMaxLength(500);
        builder.Property(e => e.Result).HasMaxLength(1000);
        builder.Property(e => e.ExternalId).HasMaxLength(100);
        builder.Property(e => e.RequestedByUserId).HasMaxLength(450);
        builder.Property(e => e.DecidedByUserId).HasMaxLength(450);
        builder.Property(e => e.DecisionNote).HasMaxLength(500);
        builder.Property(e => e.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.HasIndex(nameof(AutomationAction.OrganizationId), nameof(AutomationAction.CreatedAt));
        builder.HasIndex(nameof(AutomationAction.CreativeVariantId), nameof(AutomationAction.MetaAdSetId));
        builder.HasOne<CreativeVariant>().WithMany().HasForeignKey(e => e.CreativeVariantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MetaAdSet>().WithMany().HasForeignKey(e => e.MetaAdSetId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProductionPlan>().WithMany().HasForeignKey(e => e.ProductionPlanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Hypothesis>().WithMany().HasForeignKey(e => e.HypothesisId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Mapeo de <see cref="KillSwitchEvent"/>.</summary>
public sealed class KillSwitchEventConfiguration : IEntityTypeConfiguration<KillSwitchEvent>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<KillSwitchEvent> builder)
    {
        builder.Property(e => e.Reason).HasMaxLength(500);
        builder.Property(e => e.UserId).HasMaxLength(450);
        builder.Property(e => e.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.HasIndex(nameof(KillSwitchEvent.OrganizationId), nameof(KillSwitchEvent.CreatedAt));
        builder.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Mapeo de <see cref="AnomalyEvent"/>: una misma anomalía de un anuncio en un día se registra una sola vez.</summary>
public sealed class AnomalyEventConfiguration : IEntityTypeConfiguration<AnomalyEvent>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<AnomalyEvent> builder)
    {
        builder.Property(e => e.Kind).HasConversion<string>().HasMaxLength(30);
        builder.Property(e => e.Description).HasMaxLength(500);
        builder.Property(e => e.ReviewedByUserId).HasMaxLength(450);
        builder.Property(e => e.ReviewNote).HasMaxLength(500);
        builder.Property(e => e.DetectedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.HasIndex(nameof(AnomalyEvent.MetaAdId), nameof(AnomalyEvent.Kind), nameof(AnomalyEvent.Date)).IsUnique();
        builder.HasIndex(nameof(AnomalyEvent.OrganizationId), nameof(AnomalyEvent.ReviewedAt));
        builder.HasOne<MetaAd>().WithMany().HasForeignKey(e => e.MetaAdId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

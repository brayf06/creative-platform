using Microsoft.EntityFrameworkCore;

namespace CreativeIntelligence.Web.Services.Composition;

/// <summary>
/// Lotes de creatividades de la organización activa. Un lote toma un conjunto de fragmentos autorizados, genera
/// variantes distintas entre sí (<see cref="VariantGenerator"/>), crea una receta por variante y encola el render
/// de cada una. El avance se deduce del estado de sus videos.
/// </summary>
/// <param name="db">Contexto de datos.</param>
/// <param name="composition">Recetas y renders.</param>
/// <param name="audit">Auditoría.</param>
/// <param name="current">Usuario y organización que operan.</param>
/// <param name="jobs">Cola de trabajos, para cancelar renders.</param>
public sealed class BatchService(AppDbContext db, CompositionService composition, AuditService audit, CurrentContext current, JobService jobs)
{
    /// <summary>Cantidad mínima de variantes de un lote.</summary>
    public const int MinCount = 5;

    /// <summary>Cantidad máxima de variantes de un lote.</summary>
    public const int MaxCount = 50;

    /// <summary>Motivo con el que queda un video cuyo render se canceló.</summary>
    public const string CancelledMessage = "Cancelado antes de renderizarse.";

    /// <summary>Lista los lotes de la organización activa con su avance, del más nuevo al más viejo. No escribe.</summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Hasta 200 lotes.</returns>
    public async Task<List<BatchListItem>> ListAsync(CancellationToken cancellationToken)
    {
        return await db.CreativeBatches.AsNoTracking()
            .OrderByDescending(b => b.Id)
            .Take(200)
            .Select(b => new BatchListItem(
                b.Id,
                b.Name,
                b.RequestedCount,
                db.CreativeRecipes.Count(r => r.CreativeBatchId == b.Id),
                db.CreativeVariants.Count(v => v.Status == MediaProcessingStatus.Ready
                    && db.CreativeRecipes.Any(r => r.Id == v.CreativeRecipeId && r.CreativeBatchId == b.Id)),
                db.CreativeVariants.Count(v => v.Status == MediaProcessingStatus.Failed
                    && db.CreativeRecipes.Any(r => r.Id == v.CreativeRecipeId && r.CreativeBatchId == b.Id)),
                b.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Calcula qué variantes saldrían de un lote, sin crear nada. Comprueba que los fragmentos existan y
    /// estén autorizados. No escribe.</summary>
    /// <param name="body">Parámetros y fragmentos del lote.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Las variantes que se generarían y cuántos candidatos se descartaron por parecidos.</returns>
    /// <exception cref="BusinessException">400 <c>UNKNOWN_FORMAT</c>; 404 <c>SEGMENT_NOT_FOUND</c>; 409
    /// <c>REUSE_NOT_AUTHORIZED</c>.</exception>
    public async Task<BatchPreview> PreviewAsync(CreateBatchBody body, CancellationToken cancellationToken)
    {
        GeneratedPlan plan = await PlanAsync(body, cancellationToken);
        return new BatchPreview(
            body.Count,
            plan.Discarded,
            plan.Variants.Select(v => new PreviewVariant(v.Select(f => f.SegmentId).ToList(), v.Sum(f => f.DurationMs))).ToList());
    }

    /// <summary>
    /// Crea un lote: genera las variantes, crea una receta por cada una y encola su render. Si los fragmentos dan
    /// para menos variantes que las pedidas (pero al menos <see cref="MinCount"/>), crea las que hay. Escribe el
    /// lote, las recetas, los videos pendientes, los trabajos y la auditoría en una transacción.
    /// </summary>
    /// <param name="body">Parámetros y fragmentos del lote.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Identificador del lote.</returns>
    /// <exception cref="BusinessException">Los de la vista previa, más 409 <c>NOT_ENOUGH_VARIETY</c> si con esos
    /// fragmentos y ese umbral salen menos de <see cref="MinCount"/> variantes distintas.</exception>
    public async Task<long> CreateAsync(CreateBatchBody body, CancellationToken cancellationToken)
    {
        OutputFormat format = Format(body);
        GeneratedPlan plan = await PlanAsync(body, cancellationToken);
        if (plan.Variants.Count < MinCount)
        {
            throw new BusinessException(
                409,
                $"Con estos fragmentos sólo salen {plan.Variants.Count} variantes distintas y un lote necesita al menos {MinCount}. "
                + "Agregá fragmentos, asignales funciones distintas o subí el umbral de similitud.",
                "NOT_ENOUGH_VARIETY");
        }

        string name = body.Name.Trim();
        CreativeBatch batch = new()
        {
            Name = name,
            Brief = string.IsNullOrWhiteSpace(body.Brief) ? null : body.Brief.Trim(),
            Width = format.Width,
            Height = format.Height,
            RequestedCount = body.Count,
            MaxDurationMs = body.MaxDurationSeconds * 1000,
            SimilarityThreshold = body.SimilarityPercent / 100m,
            Seed = body.Seed,
            DiscardedBySimilarity = plan.Discarded,
            CreatedByUserId = current.UserId,
        };
        return await db.InTransactionAsync(
            async () =>
            {
                db.CreativeBatches.Add(batch);
                await db.SaveChangesAsync(cancellationToken);
                audit.Record(
                    "batch.create", nameof(CreativeBatch), batch.Id.ToString(),
                    $"Creó el lote \"{name}\" con {plan.Variants.Count} variantes de {body.Count} pedidas "
                    + $"({format.Name}, similitud máxima {body.SimilarityPercent} %).");
                await db.SaveChangesAsync(cancellationToken);
                for (int i = 0; i < plan.Variants.Count; i++)
                {
                    CreateRecipeBody recipe = new()
                    {
                        Name = $"{name} · {i + 1:00}",
                        Concept = batch.Brief is { Length: > 1000 } ? batch.Brief[..1000] : batch.Brief,
                        Format = format.Name,
                        Subtitles = body.Subtitles,
                        Transition = body.Transition,
                        MusicTrackId = body.MusicTrackId,
                        MusicVolumePercent = body.MusicVolumePercent,
                        Overlays = body.Overlays,
                        Items = plan.Variants[i].Select(f => new RecipeItemBody { SegmentId = f.SegmentId, Role = f.Role }).ToList(),
                    };
                    long recipeId = await composition.CreateAsync(recipe, batch.Id, cancellationToken);
                    await composition.RenderAsync(recipeId, cancellationToken);
                }

                return batch.Id;
            },
            cancellationToken);
    }

    /// <summary>Devuelve un lote con sus parámetros y, por cada variante, su receta y su video más reciente con
    /// los controles de calidad. No escribe.</summary>
    /// <param name="id">Lote.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>El lote.</returns>
    /// <exception cref="BusinessException">404 <c>BATCH_NOT_FOUND</c> si no existe en la organización activa.</exception>
    public async Task<BatchDetail> GetAsync(long id, CancellationToken cancellationToken)
    {
        CreativeBatch batch = await db.CreativeBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, cancellationToken)
            ?? throw new BusinessException(404, "El lote no existe.", "BATCH_NOT_FOUND");
        List<BatchRecipeRow> recipes = await db.CreativeRecipes.AsNoTracking()
            .Where(r => r.CreativeBatchId == id)
            .OrderBy(r => r.Id)
            .Select(r => new BatchRecipeRow { RecipeId = r.Id, Name = r.Name, Items = r.Items.Count })
            .ToListAsync(cancellationToken);
        List<BatchVideoRow> videos = await db.CreativeVariants.AsNoTracking()
            .Where(v => db.CreativeRecipes.Any(r => r.Id == v.CreativeRecipeId && r.CreativeBatchId == id))
            .OrderByDescending(v => v.Id)
            .Select(v => new BatchVideoRow
            {
                RecipeId = v.CreativeRecipeId,
                Video = new VariantView(
                    v.Id,
                    v.Status,
                    v.Error,
                    v.DurationMs,
                    v.Width,
                    v.Height,
                    v.SizeBytes,
                    v.CreatedAt,
                    v.Checks.OrderBy(c => c.Rule).Select(c => new QualityResult(c.Rule, c.Passed, c.Detail)).ToList(),
                    v.RenderMs),
            })
            .ToListAsync(cancellationToken);

        // Una receta puede tener más de un video si se volvió a renderizar: el lote muestra el más reciente.
        Dictionary<long, VariantView> latest = videos.GroupBy(v => v.RecipeId).ToDictionary(g => g.Key, g => g.First().Video);
        return new BatchDetail(
            batch.Id, batch.Name, batch.Brief, batch.Width, batch.Height, batch.RequestedCount, batch.MaxDurationMs,
            batch.SimilarityThreshold, batch.Seed, batch.DiscardedBySimilarity, batch.CreatedAt,
            recipes.Select(r => new BatchVariantView(r.RecipeId, r.Name, r.Items, latest.GetValueOrDefault(r.RecipeId))).ToList());
    }

    /// <summary>
    /// Cancela lo que falta de un lote: los renders en cola se cancelan, a los que están corriendo se les pide que
    /// paren, y sus videos quedan fallidos con el motivo. Un render que justo termina mientras se cancela queda
    /// como terminado. Escribe los trabajos, los videos y la auditoría en una transacción.
    /// </summary>
    /// <param name="id">Lote.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Cuántos videos se cancelaron.</returns>
    /// <exception cref="BusinessException">404 <c>BATCH_NOT_FOUND</c>; 409 <c>JOB_NOT_CANCELLABLE</c> si un render
    /// terminó mientras se cancelaba (se puede volver a pedir).</exception>
    public async Task<int> CancelAsync(long id, CancellationToken cancellationToken)
    {
        await EnsureExistsAsync(id, cancellationToken);
        List<long> pending = await db.CreativeVariants.AsNoTracking()
            .Where(v => v.Status == MediaProcessingStatus.Pending
                && db.CreativeRecipes.Any(r => r.Id == v.CreativeRecipeId && r.CreativeBatchId == id))
            .Select(v => v.Id)
            .ToListAsync(cancellationToken);
        if (pending.Count == 0)
        {
            return 0;
        }

        List<string> keys = pending.Select(v => $"{CreativeRenderJobHandler.JobType}:{v}").ToList();
        List<long> jobIds = await db.BackgroundJobs.AsNoTracking()
            .Where(j => keys.Contains(j.IdempotencyKey)
                && !j.CancelRequested
                && (j.Status == BackgroundJobStatus.Pending || j.Status == BackgroundJobStatus.Running))
            .Select(j => j.Id)
            .ToListAsync(cancellationToken);
        return await db.InTransactionAsync(
            async () =>
            {
                foreach (long jobId in jobIds)
                {
                    await jobs.CancelAsync(jobId, cancellationToken);
                }

                int cancelled = await db.CreativeVariants
                    .Where(v => pending.Contains(v.Id) && v.Status == MediaProcessingStatus.Pending)
                    .ExecuteUpdateAsync(
                        s => s.SetProperty(v => v.Status, MediaProcessingStatus.Failed)
                            .SetProperty(v => v.Error, CancelledMessage)
                            .SetProperty(v => v.FinishedAt, v => (DateTime?)AppDbContext.UtcNow()),
                        cancellationToken);
                audit.Record("batch.cancel", nameof(CreativeBatch), id.ToString(), $"Canceló {cancelled} videos pendientes del lote.");
                await db.SaveChangesAsync(cancellationToken);
                return cancelled;
            },
            cancellationToken);
    }

    /// <summary>
    /// Vuelve a pedir el render de las variantes del lote cuyo video más reciente falló o se canceló. Cada una
    /// recibe un video nuevo; se vuelve a comprobar la autorización de su material. Escribe los videos pendientes,
    /// los trabajos y la auditoría en una transacción.
    /// </summary>
    /// <param name="id">Lote.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Cuántos renders se volvieron a pedir.</returns>
    /// <exception cref="BusinessException">404 <c>BATCH_NOT_FOUND</c>; 409 <c>REUSE_NOT_AUTHORIZED</c> si el material
    /// de alguna perdió la autorización.</exception>
    public async Task<int> RetryFailedAsync(long id, CancellationToken cancellationToken)
    {
        BatchDetail batch = await GetAsync(id, cancellationToken);
        List<long> failed = batch.Variants
            .Where(v => v.Video is null || v.Video.Status == MediaProcessingStatus.Failed)
            .Select(v => v.RecipeId)
            .ToList();
        if (failed.Count == 0)
        {
            return 0;
        }

        return await db.InTransactionAsync(
            async () =>
            {
                foreach (long recipeId in failed)
                {
                    await composition.RenderAsync(recipeId, cancellationToken);
                }

                audit.Record("batch.retry", nameof(CreativeBatch), id.ToString(), $"Volvió a pedir {failed.Count} videos fallidos del lote.");
                await db.SaveChangesAsync(cancellationToken);
                return failed.Count;
            },
            cancellationToken);
    }

    private async Task EnsureExistsAsync(long id, CancellationToken cancellationToken)
    {
        if (!await db.CreativeBatches.AnyAsync(b => b.Id == id, cancellationToken))
        {
            throw new BusinessException(404, "El lote no existe.", "BATCH_NOT_FOUND");
        }
    }

    private static OutputFormat Format(CreateBatchBody body)
    {
        return CompositionRules.Formats.FirstOrDefault(f => f.Name == body.Format)
            ?? throw new BusinessException(400, "El formato de salida no existe.", "UNKNOWN_FORMAT");
    }

    // Valida el conjunto de fragmentos y corre el generador. Un fragmento repetido en el pedido cuenta una vez,
    // con la primera función que se le dio.
    private async Task<GeneratedPlan> PlanAsync(CreateBatchBody body, CancellationToken cancellationToken)
    {
        Format(body);
        List<RecipeItemBody> items = body.Items.GroupBy(i => i.SegmentId).Select(g => g.First()).ToList();
        Dictionary<long, RecipeSource> sources = await composition.SourcesAsync(items.Select(i => i.SegmentId).ToList(), cancellationToken);
        List<PoolFragment> pool = items
            .Select(i => new PoolFragment(i.SegmentId, sources[i.SegmentId].EndMs - sources[i.SegmentId].StartMs, i.Role))
            .ToList();
        return VariantGenerator.Generate(pool, body.Count, body.MaxDurationSeconds * 1000, body.SimilarityPercent / 100m, body.Seed);
    }
}

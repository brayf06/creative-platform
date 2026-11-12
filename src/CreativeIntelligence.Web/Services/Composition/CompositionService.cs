using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace CreativeIntelligence.Web.Services.Composition;

/// <summary>Reglas del motor de composición que no dependen de la base: formatos de salida, límites de una receta
/// y control de calidad de un archivo generado.</summary>
public static class CompositionRules
{
    /// <summary>Cantidad máxima de tramos de una receta.</summary>
    public const int MaxItems = 30;

    /// <summary>Duración máxima de un montaje, en milisegundos.</summary>
    public const int MaxDurationMs = 180_000;

    /// <summary>Cuadros por segundo de todo video generado.</summary>
    public const int FrameRate = 30;

    /// <summary>Duración mínima de un tramo recortado, en milisegundos.</summary>
    public const int MinTrimMs = 200;

    /// <summary>Cantidad máxima de textos sobre el video de una receta.</summary>
    public const int MaxOverlays = 5;

    /// <summary>Duración del fundido a negro al final de un tramo y al principio del siguiente, en milisegundos.</summary>
    public const int FadeMs = 200;

    /// <summary>Un tramo en negro de esta duración o más hace fallar el control de calidad. Queda por encima de lo
    /// que dura un fundido entre tramos (dos veces <see cref="FadeMs"/>).</summary>
    public const int MaxBlackMs = 500;

    /// <summary>Formatos de salida disponibles.</summary>
    public static readonly IReadOnlyList<OutputFormat> Formats =
    [
        new OutputFormat("9:16", 1080, 1920, "Vertical 9:16 · 1080×1920 (Reels, Stories)"),
        new OutputFormat("4:5", 1080, 1350, "Vertical 4:5 · 1080×1350 (Feed)"),
        new OutputFormat("1:1", 1080, 1080, "Cuadrado 1:1 · 1080×1080 (Feed)"),
        new OutputFormat("16:9", 1920, 1080, "Horizontal 16:9 · 1920×1080"),
    ];

    /// <summary>
    /// Controla un archivo generado contra lo que la receta pedía: duración, resolución y presencia de audio.
    /// La duración admite una diferencia de 100 ms más 40 ms por tramo, porque cada tramo se ajusta a cuadros
    /// enteros.
    /// </summary>
    /// <param name="expectedDurationMs">Suma de las duraciones de los tramos.</param>
    /// <param name="clips">Cantidad de tramos.</param>
    /// <param name="width">Ancho pedido.</param>
    /// <param name="height">Alto pedido.</param>
    /// <param name="probe">Propiedades leídas del archivo generado.</param>
    /// <returns>Un resultado por regla.</returns>
    public static List<QualityResult> Evaluate(int expectedDurationMs, int clips, int width, int height, MediaProbe probe)
    {
        int tolerance = 100 + (40 * clips);
        bool durationOk = probe.DurationMs is not null && Math.Abs(probe.DurationMs.Value - expectedDurationMs) <= tolerance;
        return
        [
            new QualityResult(
                "duration", durationOk,
                $"Esperada {expectedDurationMs} ms (±{tolerance}); el archivo dura {probe.DurationMs?.ToString() ?? "desconocido"} ms."),
            new QualityResult(
                "resolution", probe.Width == width && probe.Height == height,
                $"Esperada {width}×{height}; el archivo tiene {probe.Width}×{probe.Height}."),
            new QualityResult(
                "audio", probe.AudioCodec is not null,
                probe.AudioCodec is null ? "El archivo no tiene pista de audio." : $"Pista de audio {probe.AudioCodec}."),
        ];
    }

    /// <summary>Controles que dependen de la terminación: que no haya un tramo largo en negro y, si la receta pedía
    /// subtítulos, que se haya quemado al menos uno.</summary>
    /// <param name="longestBlackMs">Duración del tramo en negro más largo del archivo.</param>
    /// <param name="subtitlesRequested">Si la receta pedía subtítulos.</param>
    /// <param name="subtitleLines">Cuántos subtítulos se quemaron.</param>
    /// <returns>Un resultado por regla aplicada.</returns>
    public static List<QualityResult> EvaluateFinishing(int longestBlackMs, bool subtitlesRequested, int subtitleLines)
    {
        List<QualityResult> results =
        [
            new QualityResult(
                "black", longestBlackMs < MaxBlackMs,
                longestBlackMs == 0
                    ? "Sin tramos en negro."
                    : $"El tramo en negro más largo dura {longestBlackMs} ms (se admite menos de {MaxBlackMs})."),
        ];
        if (subtitlesRequested)
        {
            results.Add(new QualityResult(
                "subtitles", subtitleLines > 0,
                subtitleLines > 0
                    ? $"{subtitleLines} subtítulo(s) quemados."
                    : "La receta pedía subtítulos pero ninguno de sus tramos tiene voz transcripta."));
        }

        return results;
    }
}

/// <summary>
/// Motor de composición de la organización activa: recetas (qué tramos se unen y en qué formato) y los videos que
/// se generan de ellas. Una receta sólo admite tramos de videos con autorización de reutilización vigente, y la
/// autorización se vuelve a comprobar cada vez que se pide un render y cuando el render corre. El render es un
/// trabajo de la cola: nunca corre dentro de una solicitud web.
/// </summary>
/// <param name="db">Contexto de datos.</param>
/// <param name="jobs">Cola de trabajos.</param>
/// <param name="audit">Auditoría.</param>
/// <param name="current">Usuario y organización que operan.</param>
/// <param name="storage">Almacenamiento, para servir los videos generados.</param>
public sealed class CompositionService(AppDbContext db, JobService jobs, AuditService audit, CurrentContext current, IMediaStorage storage)
{
    /// <summary>Lista los fragmentos que se pueden usar en una receta: elegidos para producción, de videos con
    /// autorización vigente a la fecha de la base. No escribe.</summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Fragmentos, agrupados por video y en orden de tiempo.</returns>
    public async Task<List<AvailableSegment>> AvailableAsync(CancellationToken cancellationToken)
    {
        return await db.MediaSegments.AsNoTracking()
            .Where(s => s.SelectedForProduction
                && s.MediaAsset!.Kind == MediaAssetKind.Video
                && s.MediaAsset.ReuseAuthorized
                && (s.MediaAsset.RightsExpireOn == null || s.MediaAsset.RightsExpireOn >= DateOnly.FromDateTime(AppDbContext.UtcNow())))
            .OrderBy(s => s.MediaAssetId)
            .ThenBy(s => s.StartMs)
            .Select(s => new AvailableSegment(
                s.Id,
                s.MediaAssetId,
                s.MediaAsset!.OriginalFileName,
                s.StartMs,
                s.EndMs,
                s.ThumbnailPath != null,
                db.SegmentAnalyses
                    .Where(a => a.MediaSegmentId == s.Id && a.IsCurrent)
                    .SelectMany(a => a.Functions)
                    .Select(f => f.Function)
                    .ToList()))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Lista todos los videos generados de la organización activa, del más nuevo al más viejo, con su
    /// receta, su lote y en cuántos anuncios se publicó cada uno. No escribe.</summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Hasta 200 videos.</returns>
    public async Task<List<GeneratedVideoItem>> VideosAsync(CancellationToken cancellationToken)
    {
        return await db.CreativeVariants.AsNoTracking()
            .OrderByDescending(v => v.Id)
            .Take(200)
            .Join(
                db.CreativeRecipes,
                v => v.CreativeRecipeId,
                r => r.Id,
                (v, r) => new GeneratedVideoItem(
                    v.Id, r.Id, r.Name, r.CreativeBatchId,
                    db.CreativeBatches.Where(b => b.Id == r.CreativeBatchId).Select(b => b.Name).FirstOrDefault(),
                    v.Status, v.Error, v.DurationMs, v.Width, v.Height, v.CreatedAt,
                    db.MetaAds.Count(a => a.CreativeVariantId == v.Id)))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Lista las recetas de la organización activa, de la más nueva a la más vieja. No escribe.</summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Hasta 200 recetas con su cantidad de tramos y de videos generados.</returns>
    public async Task<List<RecipeListItem>> ListAsync(CancellationToken cancellationToken)
    {
        return await db.CreativeRecipes.AsNoTracking()
            .OrderByDescending(r => r.Id)
            .Take(200)
            .Select(r => new RecipeListItem(
                r.Id,
                r.Name,
                r.Width,
                r.Height,
                r.Items.Count,
                r.Items.Sum(i => i.EndMs - i.StartMs),
                db.CreativeVariants.Count(v => v.CreativeRecipeId == r.Id),
                db.CreativeVariants.Count(v => v.CreativeRecipeId == r.Id && v.Status == MediaProcessingStatus.Ready),
                r.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Crea una receta con los fragmentos indicados, en ese orden. De cada fragmento copia el video y el tramo, así
    /// la receta no cambia si después se editan los cortes. Escribe la receta y la auditoría.
    /// </summary>
    /// <param name="body">Nombre, concepto, formato y fragmentos.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Identificador de la receta.</returns>
    /// <exception cref="BusinessException">400 <c>UNKNOWN_FORMAT</c>; 400 <c>RECIPE_INVALID</c> si no tiene
    /// fragmentos, tiene demasiados o el montaje es demasiado largo; 404 <c>SEGMENT_NOT_FOUND</c> si un fragmento no
    /// existe en la organización activa; 409 <c>REUSE_NOT_AUTHORIZED</c> si alguno es de un video sin autorización
    /// vigente.</exception>
    public Task<long> CreateAsync(CreateRecipeBody body, CancellationToken cancellationToken)
    {
        return CreateAsync(body, null, cancellationToken);
    }

    /// <summary>Igual que <see cref="CreateAsync(CreateRecipeBody, CancellationToken)"/>, dejando registrado el lote
    /// que generó la receta.</summary>
    /// <param name="body">Nombre, concepto, formato y fragmentos.</param>
    /// <param name="batchId">Lote que la genera; nulo si la arma una persona.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Identificador de la receta.</returns>
    /// <exception cref="BusinessException">Los mismos que al crear una receta a mano.</exception>
    public async Task<long> CreateAsync(CreateRecipeBody body, long? batchId, CancellationToken cancellationToken)
    {
        OutputFormat format = CompositionRules.Formats.FirstOrDefault(f => f.Name == body.Format)
            ?? throw new BusinessException(400, "El formato de salida no existe.", "UNKNOWN_FORMAT");
        if (body.Items.Count is 0 or > CompositionRules.MaxItems)
        {
            throw new BusinessException(
                400, $"Una receta lleva entre 1 y {CompositionRules.MaxItems} fragmentos.", "RECIPE_INVALID");
        }

        Dictionary<long, RecipeSource> sources = await SourcesAsync(body.Items.Select(i => i.SegmentId).Distinct().ToList(), cancellationToken);
        // Cada tramo es el fragmento entero o la parte que se haya recortado, siempre dentro del fragmento.
        List<TimeRange> ranges = [];
        foreach (RecipeItemBody item in body.Items)
        {
            RecipeSource source = sources[item.SegmentId];
            TimeRange range = new(item.SegmentId, item.StartMs ?? source.StartMs, item.EndMs ?? source.EndMs);
            if (range.StartMs < source.StartMs || range.EndMs > source.EndMs || range.EndMs - range.StartMs < CompositionRules.MinTrimMs)
            {
                throw new BusinessException(
                    400,
                    $"El recorte de \"{source.FileName}\" debe quedar entre {source.StartMs} y {source.EndMs} ms y durar al menos "
                    + $"{CompositionRules.MinTrimMs} ms.",
                    "RECIPE_INVALID");
            }

            ranges.Add(range);
        }

        int duration = ranges.Sum(r => r.EndMs - r.StartMs);
        if (duration > CompositionRules.MaxDurationMs)
        {
            throw new BusinessException(
                400, $"El montaje dura {duration / 1000} s y el máximo es {CompositionRules.MaxDurationMs / 1000} s.", "RECIPE_INVALID");
        }

        if (body.MusicTrackId is not null)
        {
            long musicId = body.MusicTrackId.Value;
            if (!await db.MusicTracks.AnyAsync(m => m.Id == musicId && m.Status == MediaProcessingStatus.Ready, cancellationToken))
            {
                throw new BusinessException(404, "La pista de música no existe o todavía no está lista.", "MUSIC_NOT_FOUND");
            }

            if (!await db.MusicTracks.AnyAsync(
                    m => m.Id == musicId && m.ReuseAuthorized
                        && (m.RightsExpireOn == null || m.RightsExpireOn >= DateOnly.FromDateTime(AppDbContext.UtcNow())),
                    cancellationToken))
            {
                throw new BusinessException(
                    409, "La pista de música no tiene autorización vigente: no puede entrar en una receta.", "REUSE_NOT_AUTHORIZED");
            }
        }

        CreativeRecipe recipe = new()
        {
            Subtitles = body.Subtitles,
            Transition = body.Transition,
            MusicTrackId = body.MusicTrackId,
            MusicVolumePercent = body.MusicVolumePercent,
            Overlays = body.Overlays
                .Where(o => !string.IsNullOrWhiteSpace(o.Text))
                .Select((o, index) => new RecipeOverlay { Position = index + 1, Text = o.Text!.Trim(), Placement = o.Placement, Moment = o.Moment })
                .ToList(),
            Name = body.Name.Trim(),
            Concept = string.IsNullOrWhiteSpace(body.Concept) ? null : body.Concept.Trim(),
            Hypothesis = string.IsNullOrWhiteSpace(body.Hypothesis) ? null : body.Hypothesis.Trim(),
            Width = format.Width,
            Height = format.Height,
            CreativeBatchId = batchId,
            CreatedByUserId = current.UserId,
            Items = body.Items
                .Select((item, index) => new RecipeItem
                {
                    Position = index + 1,
                    MediaAssetId = sources[item.SegmentId].AssetId,
                    MediaSegmentId = item.SegmentId,
                    StartMs = ranges[index].StartMs,
                    EndMs = ranges[index].EndMs,
                    Role = item.Role,
                })
                .ToList(),
        };
        return await db.InTransactionAsync(
            async () =>
            {
                db.CreativeRecipes.Add(recipe);
                await db.SaveChangesAsync(cancellationToken);
                audit.Record(
                    "recipe.create", nameof(CreativeRecipe), recipe.Id.ToString(),
                    $"Creó la receta \"{recipe.Name}\" con {recipe.Items.Count} fragmentos ({format.Name}, {duration / 1000.0:0.0} s).");
                await db.SaveChangesAsync(cancellationToken);
                return recipe.Id;
            },
            cancellationToken);
    }

    /// <summary>Lee los fragmentos indicados con su video y su tramo, y comprueba que existan y que sus videos
    /// tengan autorización vigente. No escribe. Lo usan la creación de recetas y los lotes.</summary>
    /// <param name="segmentIds">Fragmentos, sin repetir.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Los fragmentos por identificador.</returns>
    /// <exception cref="BusinessException">404 <c>SEGMENT_NOT_FOUND</c>; 409 <c>REUSE_NOT_AUTHORIZED</c>.</exception>
    public async Task<Dictionary<long, RecipeSource>> SourcesAsync(List<long> segmentIds, CancellationToken cancellationToken)
    {
        Dictionary<long, RecipeSource> sources = await db.MediaSegments.AsNoTracking()
            .Where(s => segmentIds.Contains(s.Id))
            .Select(s => new RecipeSource
            {
                SegmentId = s.Id,
                AssetId = s.MediaAssetId,
                FileName = s.MediaAsset!.OriginalFileName,
                StartMs = s.StartMs,
                EndMs = s.EndMs,
                IsVideo = s.MediaAsset.Kind == MediaAssetKind.Video,
                Authorized = s.MediaAsset.ReuseAuthorized
                    && (s.MediaAsset.RightsExpireOn == null || s.MediaAsset.RightsExpireOn >= DateOnly.FromDateTime(AppDbContext.UtcNow())),
            })
            .ToDictionaryAsync(s => s.SegmentId, cancellationToken);
        if (sources.Count != segmentIds.Count || sources.Values.Any(s => !s.IsVideo))
        {
            throw new BusinessException(404, "Alguno de los fragmentos elegidos ya no existe.", "SEGMENT_NOT_FOUND");
        }

        RecipeSource? forbidden = sources.Values.FirstOrDefault(s => !s.Authorized);
        if (forbidden is not null)
        {
            throw new BusinessException(
                409, $"El video \"{forbidden.FileName}\" no tiene autorización de reutilización vigente: no puede entrar en una receta.",
                "REUSE_NOT_AUTHORIZED");
        }

        return sources;
    }

    /// <summary>Devuelve una receta con sus tramos (y de qué video sale cada uno) y sus videos generados con sus
    /// controles de calidad. No escribe.</summary>
    /// <param name="id">Receta.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>La receta.</returns>
    /// <exception cref="BusinessException">404 <c>RECIPE_NOT_FOUND</c> si no existe en la organización activa.</exception>
    public async Task<RecipeDetail> GetAsync(long id, CancellationToken cancellationToken)
    {
        CreativeRecipe recipe = await db.CreativeRecipes.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
            ?? throw NotFound();
        List<RecipeItemView> items = await db.RecipeItems.AsNoTracking()
            .Where(i => i.CreativeRecipeId == id)
            .OrderBy(i => i.Position)
            .Select(i => new RecipeItemView(
                i.Position,
                i.Role,
                i.MediaAssetId,
                i.MediaAsset!.OriginalFileName,
                i.StartMs,
                i.EndMs,
                i.MediaSegmentId,
                i.MediaAsset.ReuseAuthorized
                    && (i.MediaAsset.RightsExpireOn == null || i.MediaAsset.RightsExpireOn >= DateOnly.FromDateTime(AppDbContext.UtcNow()))))
            .ToListAsync(cancellationToken);
        List<VariantView> variants = await db.CreativeVariants.AsNoTracking()
            .Where(v => v.CreativeRecipeId == id)
            .OrderByDescending(v => v.Id)
            .Select(v => new VariantView(
                v.Id,
                v.Status,
                v.Error,
                v.DurationMs,
                v.Width,
                v.Height,
                v.SizeBytes,
                v.CreatedAt,
                v.Checks.OrderBy(c => c.Rule).Select(c => new QualityResult(c.Rule, c.Passed, c.Detail)).ToList(),
                v.RenderMs))
            .ToListAsync(cancellationToken);
        return new RecipeDetail(
            recipe.Id, recipe.Name, recipe.Concept, recipe.Hypothesis, recipe.Width, recipe.Height,
            items.Sum(i => i.EndMs - i.StartMs), recipe.CreatedAt, items, variants)
        {
            Subtitles = recipe.Subtitles,
            Transition = recipe.Transition,
            MusicVolumePercent = recipe.MusicVolumePercent,
            MusicName = await db.MusicTracks.AsNoTracking()
                .Where(m => m.Id == recipe.MusicTrackId)
                .Select(m => m.Name)
                .FirstOrDefaultAsync(cancellationToken),
            Overlays = await OverlaysAsync(db, id, cancellationToken),
        };
    }

    /// <summary>
    /// Arma el borrador de una receta nueva a partir de una existente: como las recetas no se modifican, cambiar
    /// una es crear otra partiendo de ella. Conserva los tramos cuyo fragmento sigue existiendo, ajustados a sus
    /// límites actuales, e informa cuántos no se pudieron conservar. No escribe.
    /// </summary>
    /// <param name="recipeId">Receta de la que se parte.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>El borrador.</returns>
    /// <exception cref="BusinessException">404 <c>RECIPE_NOT_FOUND</c> si no existe en la organización activa.</exception>
    public async Task<RecipeDraft> DraftAsync(long recipeId, CancellationToken cancellationToken)
    {
        CreativeRecipe recipe = await db.CreativeRecipes.AsNoTracking().FirstOrDefaultAsync(r => r.Id == recipeId, cancellationToken)
            ?? throw NotFound();
        List<DraftItem> rows = await db.RecipeItems.AsNoTracking()
            .Where(i => i.CreativeRecipeId == recipeId)
            .OrderBy(i => i.Position)
            .Select(i => new DraftItem
            {
                SegmentId = i.MediaSegmentId,
                AssetId = i.MediaAssetId,
                FileName = i.MediaAsset!.OriginalFileName,
                StartMs = i.StartMs,
                EndMs = i.EndMs,
                SegmentStartMs = db.MediaSegments.Where(s => s.Id == i.MediaSegmentId).Select(s => (int?)s.StartMs).FirstOrDefault(),
                SegmentEndMs = db.MediaSegments.Where(s => s.Id == i.MediaSegmentId).Select(s => (int?)s.EndMs).FirstOrDefault(),
                HasThumbnail = db.MediaSegments.Any(s => s.Id == i.MediaSegmentId && s.ThumbnailPath != null),
                Role = i.Role,
            })
            .ToListAsync(cancellationToken);

        // Si el fragmento se editó después, el tramo se ajusta a lo que hoy abarca; si ya no se superponen, se pierde.
        List<DraftItem> kept = rows
            .Where(r => r.SegmentStartMs is not null && r.SegmentEndMs is not null)
            .Select(r => new DraftItem
            {
                SegmentId = r.SegmentId,
                AssetId = r.AssetId,
                FileName = r.FileName,
                StartMs = Math.Max(r.StartMs, r.SegmentStartMs!.Value),
                EndMs = Math.Min(r.EndMs, r.SegmentEndMs!.Value),
                SegmentStartMs = r.SegmentStartMs,
                SegmentEndMs = r.SegmentEndMs,
                HasThumbnail = r.HasThumbnail,
                Role = r.Role,
            })
            .Where(r => r.EndMs - r.StartMs >= CompositionRules.MinTrimMs)
            .ToList();
        string name = $"Copia de {recipe.Name}";
        return new RecipeDraft(
            name.Length > 200 ? name[..200] : name,
            recipe.Concept,
            recipe.Hypothesis,
            CompositionRules.Formats.FirstOrDefault(f => f.Width == recipe.Width && f.Height == recipe.Height)?.Name ?? CompositionRules.Formats[0].Name,
            recipe.Subtitles,
            recipe.Transition,
            recipe.MusicTrackId,
            recipe.MusicVolumePercent,
            await OverlaysAsync(db, recipeId, cancellationToken),
            kept,
            rows.Count - kept.Count);
    }

    /// <summary>
    /// Pide generar un video de una receta. Vuelve a comprobar que todos sus originales sigan autorizados. Escribe
    /// la variante en estado pendiente, la auditoría y el trabajo de render, en una transacción.
    /// </summary>
    /// <param name="recipeId">Receta.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Identificador del video que se va a generar.</returns>
    /// <exception cref="BusinessException">404 <c>RECIPE_NOT_FOUND</c>; 409 <c>REUSE_NOT_AUTHORIZED</c> si algún
    /// original perdió la autorización desde que se armó la receta.</exception>
    public async Task<long> RenderAsync(long recipeId, CancellationToken cancellationToken)
    {
        if (!await db.CreativeRecipes.AnyAsync(r => r.Id == recipeId, cancellationToken))
        {
            throw NotFound();
        }

        string? forbidden = await UnauthorizedSourceAsync(db, recipeId, cancellationToken);
        if (forbidden is not null)
        {
            throw new BusinessException(
                409, $"\"{forbidden}\" ya no tiene autorización de reutilización vigente: la receta no se puede renderizar.",
                "REUSE_NOT_AUTHORIZED");
        }

        CreativeVariant variant = new()
        {
            CreativeRecipeId = recipeId,
            Status = MediaProcessingStatus.Pending,
            CreatedByUserId = current.UserId,
        };
        return await db.InTransactionAsync(
            async () =>
            {
                db.CreativeVariants.Add(variant);
                await db.SaveChangesAsync(cancellationToken);
                audit.Record("recipe.render", nameof(CreativeVariant), variant.Id.ToString(), $"Pidió renderizar la receta #{recipeId}.");
                await db.SaveChangesAsync(cancellationToken);
                await jobs.EnqueueAsync(
                    CreativeRenderJobHandler.JobType,
                    $"{CreativeRenderJobHandler.JobType}:{variant.Id}",
                    JsonSerializer.Serialize(new RenderPayload(variant.Id)),
                    cancellationToken);
                return variant.Id;
            },
            cancellationToken);
    }

    /// <summary>Abre el archivo de un video generado, para reproducirlo o descargarlo. No escribe.</summary>
    /// <param name="variantId">Video generado.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Contenido MP4; quien llama lo cierra.</returns>
    /// <exception cref="BusinessException">404 <c>VARIANT_NOT_FOUND</c> si no existe en la organización activa o
    /// todavía no terminó.</exception>
    public async Task<MediaContent> OpenVariantAsync(long variantId, CancellationToken cancellationToken)
    {
        string? path = await db.CreativeVariants.AsNoTracking()
            .Where(v => v.Id == variantId && v.Status == MediaProcessingStatus.Ready)
            .Select(v => v.StoragePath)
            .FirstOrDefaultAsync(cancellationToken);
        if (path is null)
        {
            throw new BusinessException(404, "El video no existe o todavía no terminó de generarse.", "VARIANT_NOT_FOUND");
        }

        return new MediaContent(storage.OpenRead(path), "video/mp4", $"creatividad-{variantId}.mp4");
    }

    /// <summary>Nombre del primer original de una receta que no tiene autorización vigente, o el de su música si es
    /// ella la que no la tiene; nulo si todo está autorizado. Lo usan el pedido de render y el trabajo.</summary>
    /// <param name="db">Contexto de datos, con la organización activa.</param>
    /// <param name="recipeId">Receta.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Nombre del material sin autorización, o nulo.</returns>
    public static async Task<string?> UnauthorizedSourceAsync(AppDbContext db, long recipeId, CancellationToken cancellationToken)
    {
        string? video = await db.RecipeItems.AsNoTracking()
            .Where(i => i.CreativeRecipeId == recipeId
                && !(i.MediaAsset!.ReuseAuthorized
                    && (i.MediaAsset.RightsExpireOn == null || i.MediaAsset.RightsExpireOn >= DateOnly.FromDateTime(AppDbContext.UtcNow()))))
            .OrderBy(i => i.Position)
            .Select(i => i.MediaAsset!.OriginalFileName)
            .FirstOrDefaultAsync(cancellationToken);
        return video ?? await db.MusicTracks.AsNoTracking()
            .Where(m => db.CreativeRecipes.Any(r => r.Id == recipeId && r.MusicTrackId == m.Id)
                && !(m.ReuseAuthorized && (m.RightsExpireOn == null || m.RightsExpireOn >= DateOnly.FromDateTime(AppDbContext.UtcNow()))))
            .Select(m => m.Name)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>Textos sobre el video de una receta, en orden. No escribe.</summary>
    /// <param name="db">Contexto de datos, con la organización activa.</param>
    /// <param name="recipeId">Receta.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Los textos.</returns>
    public static async Task<List<OverlayView>> OverlaysAsync(AppDbContext db, long recipeId, CancellationToken cancellationToken)
    {
        return await db.RecipeOverlays.AsNoTracking()
            .Where(o => o.CreativeRecipeId == recipeId)
            .OrderBy(o => o.Position)
            .Select(o => new OverlayView(o.Text, o.Placement, o.Moment))
            .ToListAsync(cancellationToken);
    }
    private static BusinessException NotFound()
    {
        return new BusinessException(404, "La receta no existe.", "RECIPE_NOT_FOUND");
    }
}

/// <summary>
/// Render de un video generado. Comprueba otra vez la autorización de cada original (pudo cambiar mientras el
/// trabajo esperaba), corta y une los tramos con FFmpeg en el formato de la receta, lee el archivo resultante y le
/// aplica el control de calidad. Es idempotente: repetirlo sobrescribe el mismo archivo y reemplaza sus controles.
/// Un fallo de FFmpeg o de derechos no se reintenta: deja la variante fallida con el motivo.
/// </summary>
/// <param name="db">Contexto de datos, con la organización del trabajo activa.</param>
/// <param name="storage">Almacenamiento.</param>
/// <param name="tools">FFmpeg.</param>
public sealed class CreativeRenderJobHandler(AppDbContext db, IMediaStorage storage, IMediaTools tools) : IJobHandler
{
    /// <summary>Tipo de trabajo que atiende.</summary>
    public const string JobType = "creative.render";

    /// <inheritdoc />
    public string Type => JobType;

    /// <inheritdoc />
    /// <exception cref="PermanentJobException">Si la variante ya no existe, si un original perdió la autorización o
    /// si FFmpeg no pudo generar el archivo.</exception>
    public async Task ExecuteAsync(JobExecution execution, CancellationToken cancellationToken)
    {
        RenderPayload? payload = JsonSerializer.Deserialize<RenderPayload>(execution.Job.PayloadJson);
        long variantId = payload?.VariantId ?? 0;
        CreativeVariant variant = await db.CreativeVariants.Include(v => v.Checks).FirstOrDefaultAsync(v => v.Id == variantId, cancellationToken)
            ?? throw new PermanentJobException("El video a generar ya no existe.");
        CreativeRecipe recipe = await db.CreativeRecipes.AsNoTracking().FirstAsync(r => r.Id == variant.CreativeRecipeId, cancellationToken);
        List<RenderSource> sources = await db.RecipeItems.AsNoTracking()
            .Where(i => i.CreativeRecipeId == recipe.Id)
            .OrderBy(i => i.Position)
            .Select(i => new RenderSource
            {
                AssetId = i.MediaAssetId,
                StoragePath = i.MediaAsset!.StoragePath,
                StartMs = i.StartMs,
                EndMs = i.EndMs,
                HasAudio = i.MediaAsset.AudioCodec != null,
            })
            .ToListAsync(cancellationToken);

        string? forbidden = await CompositionService.UnauthorizedSourceAsync(db, recipe.Id, cancellationToken);
        if (forbidden is not null)
        {
            await FailAsync(variant, $"\"{forbidden}\" ya no tiene autorización de reutilización vigente.", cancellationToken);
        }

        await execution.ReportAsync(5, "Renderizando", cancellationToken);
        int totalMs = sources.Sum(s => s.EndMs - s.StartMs);
        List<TimedText> subtitles = recipe.Subtitles ? await SubtitlesAsync(sources, cancellationToken) : [];
        List<TimedText> texts =
        [
            .. subtitles,
            .. SubtitleBuilder.FromOverlays(totalMs, await CompositionService.OverlaysAsync(db, recipe.Id, cancellationToken)),
        ];
        string? musicPath = await db.MusicTracks.AsNoTracking()
            .Where(m => m.Id == recipe.MusicTrackId)
            .Select(m => m.StoragePath)
            .FirstOrDefaultAsync(cancellationToken);

        string relativePath = storage.ReserveVariantPath(variant.OrganizationId, variant.Id);
        string localPath = storage.GetLocalPath(relativePath);
        string workFolder = Path.Combine(Path.GetTempPath(), $"ci-render-{variant.Id}-{Guid.NewGuid():N}");
        string? textFile = null;
        if (texts.Count > 0)
        {
            Directory.CreateDirectory(workFolder);
            textFile = Path.Combine(workFolder, "textos.ass");
            await File.WriteAllTextAsync(textFile, SubtitleBuilder.BuildAss(recipe.Width, recipe.Height, texts), cancellationToken);
        }

        RenderPlan plan = new(
            recipe.Width, recipe.Height, CompositionRules.FrameRate,
            sources.Select(s => new RenderClip(storage.GetLocalPath(s.StoragePath), s.StartMs, s.EndMs, s.HasAudio)).ToList())
        {
            FadeMs = recipe.Transition == RecipeTransition.FadeBlack ? CompositionRules.FadeMs : 0,
            TextFilePath = textFile,
            MusicPath = musicPath is null ? null : storage.GetLocalPath(musicPath),
            MusicVolume = recipe.MusicVolumePercent / 100m,
        };
        MediaProbe probe;
        int longestBlackMs;
        long started = Stopwatch.GetTimestamp();
        try
        {
            await tools.RenderAsync(plan, localPath, cancellationToken);
            variant.RenderMs = (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            await execution.ReportAsync(90, "Controlando la calidad", cancellationToken);
            probe = await tools.ProbeAsync(localPath, cancellationToken);
            longestBlackMs = await tools.LongestBlackMsAsync(localPath, cancellationToken);
        }
        catch (MediaToolException exception)
        {
            await FailAsync(variant, exception.Message, cancellationToken);
            return;
        }
        finally
        {
            if (Directory.Exists(workFolder))
            {
                Directory.Delete(workFolder, true);
            }
        }

        List<QualityResult> results =
        [
            .. CompositionRules.Evaluate(totalMs, sources.Count, recipe.Width, recipe.Height, probe),
            .. CompositionRules.EvaluateFinishing(longestBlackMs, recipe.Subtitles, subtitles.Count),
        ];        db.QualityChecks.RemoveRange(variant.Checks);
        variant.Checks = results.Select(r => new QualityCheck { Rule = r.Rule, Passed = r.Passed, Detail = r.Detail }).ToList();
        variant.Status = MediaProcessingStatus.Ready;
        variant.Error = null;

        // Si el lote se canceló mientras este render corría, la base ya tiene el video como fallido con su motivo,
        // pero acá se leyó antes: se fuerza la escritura para que un video terminado no conserve un error viejo.
        db.Entry(variant).Property(v => v.Error).IsModified = true;
        variant.StoragePath = relativePath;
        variant.DurationMs = probe.DurationMs;
        variant.Width = probe.Width;
        variant.Height = probe.Height;
        variant.SizeBytes = new FileInfo(localPath).Length;
        await db.SaveChangesAsync(cancellationToken);
        await db.CreativeVariants.Where(v => v.Id == variant.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.FinishedAt, v => AppDbContext.UtcNow()), cancellationToken);
    }

    // Lo que se dice en cada tramo, con los tiempos del montaje: sale de la transcripción de cada original.
    private async Task<List<TimedText>> SubtitlesAsync(List<RenderSource> sources, CancellationToken cancellationToken)
    {
        List<long> assetIds = sources.Select(s => s.AssetId).Distinct().ToList();
        List<AssetSpeechLine> lines = await db.MediaTranscriptLines.AsNoTracking()
            .Where(l => assetIds.Contains(l.MediaAssetId))
            .OrderBy(l => l.StartMs)
            .Select(l => new AssetSpeechLine { AssetId = l.MediaAssetId, StartMs = l.StartMs, EndMs = l.EndMs, Text = l.Text })
            .ToListAsync(cancellationToken);
        ILookup<long, TranscriptLineItem> byAsset = lines.ToLookup(l => l.AssetId, l => new TranscriptLineItem(l.StartMs, l.EndMs, l.Text));
        return SubtitleBuilder.FromTranscript(
            sources.Select(s => new ClipSpeech(s.StartMs, s.EndMs, byAsset[s.AssetId].ToList())).ToList());
    }

    // Deja la variante fallida con el motivo y corta el trabajo sin reintentos: repetirlo daría lo mismo.
    private async Task FailAsync(CreativeVariant variant, string reason, CancellationToken cancellationToken)
    {
        variant.Status = MediaProcessingStatus.Failed;
        variant.Error = reason.Length > 1000 ? reason[..1000] : reason;
        await db.SaveChangesAsync(cancellationToken);
        await db.CreativeVariants.Where(v => v.Id == variant.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.FinishedAt, v => AppDbContext.UtcNow()), cancellationToken);
        throw new PermanentJobException(reason);
    }
}

using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CreativeIntelligence.Web.Services.Media;

/// <summary>
/// Análisis con IA de las escenas de un video: estimación, pedido y consulta. No es automático: cada análisis
/// cuesta dinero, así que lo pide una persona después de ver la estimación, y un tope por video lo limita.
/// </summary>
/// <param name="db">Contexto de datos.</param>
/// <param name="analyzer">Proveedor de IA.</param>
/// <param name="jobs">Cola de trabajos.</param>
/// <param name="audit">Auditoría.</param>
/// <param name="index">Índice de búsqueda, para reindexar tras heredar un análisis.</param>
/// <param name="usage">Uso de IA, para el tope de cada tanda.</param>
/// <param name="options">Tope de costo y de cuadros.</param>
public sealed class AnalysisService(
    AppDbContext db,
    ISegmentAnalyzer analyzer,
    JobService jobs,
    AuditService audit,
    SegmentIndexService index,
    AiUsageService usage,
    IOptions<AiOptions> options)
{
    /// <summary>Devuelve el análisis vigente de cada fragmento de un video. No escribe.</summary>
    /// <param name="assetId">Video.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Estado y análisis por fragmento, en orden temporal.</returns>
    /// <exception cref="BusinessException">404 <c>MEDIA_NOT_FOUND</c> si no existe en la organización activa.</exception>
    public async Task<AnalysisView> GetAsync(long assetId, CancellationToken cancellationToken)
    {
        SceneHeader header = await db.MediaAssets.AsNoTracking()
            .Where(a => a.Id == assetId)
            .Select(a => new SceneHeader { Status = a.AnalysisStatus, Error = a.AnalysisError })
            .FirstOrDefaultAsync(cancellationToken) ?? throw NotFound();

        List<NarrativeItem> narratives = await db.SegmentAnalyses.AsNoTracking()
            .Where(a => a.IsCurrent && a.MediaSegment!.MediaAssetId == assetId && a.MediaSegment.Kind == MediaSegmentKind.Narrative)
            .OrderBy(a => a.MediaSegment!.StartMs)
            .Select(a => new NarrativeItem(
                a.MediaSegmentId,
                a.MediaSegment!.StartMs,
                a.MediaSegment.EndMs,
                a.Functions.OrderBy(f => f.Id).Select(f => (CreativeFunction?)f.Function).FirstOrDefault(),
                a.VisualDescription))
            .ToListAsync(cancellationToken);

        List<SegmentAnalysisItem> items = await db.SegmentAnalyses.AsNoTracking()
            .Where(a => a.IsCurrent && a.MediaSegment!.MediaAssetId == assetId && a.MediaSegment.Kind == MediaSegmentKind.Visual)
            .OrderBy(a => a.MediaSegment!.StartMs)
            .Select(a => new SegmentAnalysisItem(
                a.MediaSegmentId,
                a.StartMs != a.MediaSegment!.StartMs || a.EndMs != a.MediaSegment!.EndMs,
                a.VisualDescription,
                a.OnScreenText,
                a.Product,
                a.Actions,
                a.CommercialAngle,
                a.MainMessage,
                a.Audience,
                a.VisualQuality,
                a.Confidence,
                a.Functions.OrderBy(f => f.Function).Select(f => f.Function).ToList(),
                a.Model,
                a.PromptVersion,
                a.CreatedAt,
                a.InheritedFromAnalysisId != null))
            .ToListAsync(cancellationToken);
        return new AnalysisView(assetId, header.Status, header.Error, items, narratives);
    }

    /// <summary>Estima cuánto costaría analizar un video, sin llamar al proveedor. No escribe.</summary>
    /// <param name="assetId">Video.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Escenas, cuadros, tokens y costo estimados, y si el resultado ya está en cache.</returns>
    /// <exception cref="BusinessException">404 <c>MEDIA_NOT_FOUND</c> si no existe en la organización activa;
    /// 409 <c>SCENES_NOT_READY</c> si el video todavía no tiene escenas detectadas o su transcripción está en
    /// curso.</exception>
    public async Task<AnalysisEstimate> EstimateAsync(long assetId, CancellationToken cancellationToken)
    {
        AnalysisPlan plan = await PlanAsync(assetId, cancellationToken);
        long? inheritsFrom = await InheritableSourceAsync(assetId, cancellationToken);
        bool cached = await db.AiResultCaches.AnyAsync(c => c.CacheKey == plan.CacheKey, cancellationToken);
        int frames = plan.FrameTimes.Sum(f => f.Count);
        AnalysisCost cost = analyzer.Estimate(plan.Scenes.Count, frames, plan.Scenes.Sum(s => s.Transcript.Length));
        return new AnalysisEstimate(
            analyzer.IsConfigured, analyzer.Model, plan.Scenes.Count, frames, cost.InputTokens, cost.OutputTokens,
            cached || inheritsFrom is not null ? 0m : cost.CostUsd, options.Value.MaxCostPerVideoUsd, cached, inheritsFrom);
    }

    /// <summary>
    /// Pide el análisis con IA de un video. Deja el análisis <c>Pending</c>, encola el trabajo y lo audita con su
    /// costo estimado, en una transacción.
    /// </summary>
    /// <param name="assetId">Video.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <exception cref="BusinessException">404 <c>MEDIA_NOT_FOUND</c> si no existe en la organización activa;
    /// 409 <c>SCENES_NOT_READY</c> si no tiene escenas o su transcripción está en curso; 409
    /// <c>AI_NOT_CONFIGURED</c> si falta la clave del proveedor y no hay un original del que heredar; 409 <c>BUDGET_EXCEEDED</c> si la estimación
    /// supera el tope por video; 409 <c>ANALYSIS_IN_PROGRESS</c> si ya hay un análisis en curso.</exception>
    public async Task RequestAsync(long assetId, CancellationToken cancellationToken)
    {
        AnalysisEstimate estimate = await EstimateAsync(assetId, cancellationToken);

        // Heredar del original no llama al proveedor: no hace falta la clave.
        if (!estimate.Configured && estimate.InheritsFromAssetId is null)
        {
            throw new BusinessException(409, "Falta configurar la clave del proveedor de IA.", "AI_NOT_CONFIGURED");
        }

        if (estimate.CostUsd > estimate.MaxCostUsd)
        {
            throw new BusinessException(
                409,
                $"El análisis costaría unos USD {estimate.CostUsd:0.0000} y el tope por video es USD {estimate.MaxCostUsd:0.0000}.",
                "BUDGET_EXCEEDED");
        }

        await db.InTransactionAsync(
            async () =>
            {
                // El estado de origen va en la condición: dos pedidos simultáneos no encolan dos análisis pagos.
                int rows = await db.MediaAssets
                    .Where(a => a.Id == assetId && (a.AnalysisStatus == null || a.AnalysisStatus != MediaProcessingStatus.Pending))
                    .ExecuteUpdateAsync(
                        s => s.SetProperty(a => a.AnalysisStatus, (MediaProcessingStatus?)MediaProcessingStatus.Pending)
                            .SetProperty(a => a.AnalysisError, (string?)null),
                        cancellationToken);
                if (rows == 0)
                {
                    throw new BusinessException(409, "Ya hay un análisis en curso para este video.", "ANALYSIS_IN_PROGRESS");
                }

                audit.Record(
                    "media.analyze", nameof(MediaAsset), assetId.ToString(),
                    estimate.InheritsFromAssetId is null
                        ? $"Pidió el análisis con IA ({estimate.Model}, estimado USD {estimate.CostUsd:0.0000})."
                        : $"Pidió heredar el análisis del archivo #{estimate.InheritsFromAssetId}, del que es duplicado visual.");
                await db.SaveChangesAsync(cancellationToken);
                await jobs.EnqueueAsync(
                    MediaAnalyzeJobHandler.JobType,
                    $"{MediaAnalyzeJobHandler.JobType}:{assetId}:{Guid.NewGuid():N}",
                    JsonSerializer.Serialize(new MediaProbePayload(assetId)),
                    cancellationToken);
                return true;
            },
            cancellationToken);
    }

    /// <summary>
    /// Estima una tanda de análisis en lote: los videos con escenas que nunca se analizaron, cuántos entran y
    /// cuánto costaría. No escribe ni llama al proveedor.
    /// </summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>La estimación de la tanda.</returns>
    public async Task<BatchEstimate> EstimateBatchAsync(CancellationToken cancellationToken)
    {
        BatchPlan plan = await PlanBatchAsync(cancellationToken);
        bool autoEnabled = await db.Organizations.AsNoTracking()
            .Where(o => o.Id == db.CurrentOrganizationId)
            .Select(o => o.AutoAnalyzeNewVideos)
            .FirstOrDefaultAsync(cancellationToken);
        return new BatchEstimate(
            analyzer.IsConfigured, analyzer.Model, plan.Pending, plan.Included.Count, plan.Included.Sum(e => e.Estimate.Scenes),
            plan.Included.Count(e => e.Estimate.InheritsFromAssetId is not null), plan.Included.Sum(e => e.Estimate.CostUsd),
            plan.LimitUsd, plan.OverVideoCap, plan.OverBudget, plan.NotConfigured, autoEnabled);
    }

    /// <summary>
    /// Pide el análisis de todos los videos pendientes que entran en la tanda: cada uno queda <c>Pending</c> con su
    /// trabajo en la cola y su registro de auditoría. Respeta el tope por video, el tope por tanda y lo que queda del
    /// presupuesto mensual; los que no entran quedan para la siguiente. Sin clave del proveedor sólo entran los
    /// duplicados que heredan de un original ya analizado.
    /// </summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Cuántos se encolaron, su costo estimado y cuántos quedaron afuera.</returns>
    public async Task<BatchResult> RequestBatchAsync(CancellationToken cancellationToken)
    {
        BatchPlan plan = await PlanBatchAsync(cancellationToken);
        foreach (BatchEntry entry in plan.Included)
        {
            await RequestAsync(entry.AssetId, cancellationToken);
        }

        return new BatchResult(plan.Included.Count, plan.Included.Sum(e => e.Estimate.CostUsd), plan.Pending - plan.Included.Count);
    }

    /// <summary>Activa o desactiva el análisis automático de videos nuevos en la organización activa. Escribe la
    /// organización y la auditoría.</summary>
    /// <param name="enabled">Verdadero para activarlo.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    public async Task SetAutoAnalyzeAsync(bool enabled, CancellationToken cancellationToken)
    {
        int organizationId = db.CurrentOrganizationId;
        Organization organization = await db.Organizations.FirstAsync(o => o.Id == organizationId, cancellationToken);
        organization.AutoAnalyzeNewVideos = enabled;
        audit.Record(
            "analysis.auto", nameof(Organization), organizationId.ToString(),
            enabled ? "Activó el análisis automático de videos nuevos." : "Desactivó el análisis automático de videos nuevos.");
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Organizaciones activas con el análisis automático encendido. Cruza organizaciones a propósito: sólo lo usa
    /// el programador, que después pide cada tanda con la organización correspondiente activada.
    /// </summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Identificadores de las organizaciones.</returns>
    public async Task<List<int>> AutoAnalyzeOrganizationsAsync(CancellationToken cancellationToken)
    {
        return await db.Organizations.AsNoTracking()
            .Where(o => o.IsActive && o.AutoAnalyzeNewVideos)
            .OrderBy(o => o.Id)
            .Select(o => o.Id)
            .ToListAsync(cancellationToken);
    }

    // Recorre los videos pendientes en orden de carga y decide cuáles entran: primero el tope por video, después la
    // clave del proveedor, después el tope de la tanda.
    private async Task<BatchPlan> PlanBatchAsync(CancellationToken cancellationToken)
    {
        List<long> pending = await db.MediaAssets.AsNoTracking()
            .Where(a => a.Kind == MediaAssetKind.Video
                && a.SceneStatus == MediaProcessingStatus.Ready
                && a.TranscriptStatus != MediaProcessingStatus.Pending
                && a.AnalysisStatus == null
                && db.MediaSegments.Any(s => s.MediaAssetId == a.Id && s.Kind == MediaSegmentKind.Visual))
            .OrderBy(a => a.Id)
            .Take(500)
            .Select(a => a.Id)
            .ToListAsync(cancellationToken);
        decimal limit = await usage.BatchLimitAsync(cancellationToken);

        List<BatchEntry> included = [];
        decimal cost = 0m;
        int overVideoCap = 0;
        int overBudget = 0;
        int notConfigured = 0;
        foreach (long assetId in pending)
        {
            AnalysisEstimate estimate = await EstimateAsync(assetId, cancellationToken);
            bool inherits = estimate.InheritsFromAssetId is not null;
            if (!inherits && estimate.CostUsd > estimate.MaxCostUsd)
            {
                overVideoCap++;
            }
            else if (!inherits && !estimate.Configured)
            {
                notConfigured++;
            }
            else if (cost + estimate.CostUsd > limit)
            {
                overBudget++;
            }
            else
            {
                included.Add(new BatchEntry(assetId, estimate));
                cost += estimate.CostUsd;
            }
        }

        return new BatchPlan(pending.Count, included, limit, overVideoCap, overBudget, notConfigured);
    }

    /// <summary>
    /// Arma lo que se le enviaría al modelo para un video: sus escenas con lo que se dice en cada una, los
    /// instantes de los cuadros y la clave de cache. No escribe. Lo usan la estimación y el trabajo.
    /// </summary>
    /// <param name="assetId">Video.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>El plan de análisis.</returns>
    /// <exception cref="BusinessException">404 <c>MEDIA_NOT_FOUND</c>; 409 <c>SCENES_NOT_READY</c>.</exception>
    public async Task<AnalysisPlan> PlanAsync(long assetId, CancellationToken cancellationToken)
    {
        AnalysisAssetInfo asset = await db.MediaAssets.AsNoTracking()
            .Where(a => a.Id == assetId)
            .Select(a => new AnalysisAssetInfo
            {
                FileName = a.OriginalFileName,
                Sha256 = a.Sha256,
                StoragePath = a.StoragePath,
                IsVideo = a.Kind == MediaAssetKind.Video,
                SceneStatus = a.SceneStatus,
                TranscriptStatus = a.TranscriptStatus,
            })
            .FirstOrDefaultAsync(cancellationToken) ?? throw NotFound();
        if (!asset.IsVideo || asset.SceneStatus != MediaProcessingStatus.Ready)
        {
            throw new BusinessException(409, "El video todavía no tiene escenas detectadas.", "SCENES_NOT_READY");
        }

        if (asset.TranscriptStatus == MediaProcessingStatus.Pending)
        {
            throw new BusinessException(409, "La transcripción está en curso: esperá a que termine para analizar.", "SCENES_NOT_READY");
        }

        List<SegmentItem> segments = await db.MediaSegments.AsNoTracking()
            .Where(s => s.MediaAssetId == assetId && s.Kind == MediaSegmentKind.Visual)
            .OrderBy(s => s.StartMs)
            .Select(s => new SegmentItem(s.Id, s.Kind, s.Origin, s.StartMs, s.EndMs, false, false, new List<string>(), string.Empty))
            .ToListAsync(cancellationToken);
        if (segments.Count == 0)
        {
            throw new BusinessException(409, "El video todavía no tiene escenas detectadas.", "SCENES_NOT_READY");
        }

        List<TranscriptLineItem> lines = await db.MediaTranscriptLines.AsNoTracking()
            .Where(l => l.MediaAssetId == assetId)
            .OrderBy(l => l.StartMs)
            .Select(l => new TranscriptLineItem(l.StartMs, l.EndMs, l.Text))
            .ToListAsync(cancellationToken);

        List<SceneInput> scenes = segments
            .Select((s, i) => new SceneInput(i + 1, s.StartMs, s.EndMs, TranscriptService.TextBetween(lines, s.StartMs, s.EndMs), []))
            .ToList();
        List<List<int>> frameTimes = PlanFrames(scenes, options.Value.MaxFramesPerVideo);
        return new AnalysisPlan(
            asset.FileName, asset.Sha256, asset.StoragePath, scenes, segments.Select(s => s.Id).ToList(), frameTimes,
            CacheKey(analyzer.Model, asset.Sha256, scenes, frameTimes));
    }

    /// <summary>
    /// Si el video es un duplicado visual de otro que ya tiene análisis vigente, devuelve ese otro. No escribe.
    /// </summary>
    /// <param name="assetId">Video.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>El archivo original analizado, o nulo si no es duplicado o el original no está analizado.</returns>
    public async Task<long?> InheritableSourceAsync(long assetId, CancellationToken cancellationToken)
    {
        long? originalId = await db.MediaAssets.AsNoTracking()
            .Where(a => a.Id == assetId)
            .Select(a => a.DuplicateOfAssetId)
            .FirstOrDefaultAsync(cancellationToken);
        if (originalId is null)
        {
            return null;
        }

        bool analyzed = await db.SegmentAnalyses.AnyAsync(a => a.IsCurrent && a.MediaSegment!.MediaAssetId == originalId, cancellationToken);
        return analyzed ? originalId : null;
    }

    /// <summary>
    /// Copia al video el análisis vigente de su original, sin llamar al proveedor. Cada escena del video recibe el
    /// análisis de la escena del original con la que más se superpone en el tiempo (ver <see cref="MatchRanges"/>);
    /// las que no se corresponden con ninguna quedan sin análisis. En una transacción: versiona el análisis de
    /// cada escena, copia las etiquetas del original como automáticas (sin duplicar las que ya tenga), deja el
    /// análisis <c>Ready</c> y encola el reindexado. No registra costo porque no lo hay.
    /// </summary>
    /// <param name="assetId">Video duplicado.</param>
    /// <param name="sourceAssetId">Original del que se hereda.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Cantidad de escenas que heredaron análisis; 0 si ninguna se corresponde.</returns>
    public async Task<int> InheritAsync(long assetId, long sourceAssetId, CancellationToken cancellationToken)
    {
        int targetDuration = await DurationAsync(assetId, cancellationToken);
        int sourceDuration = await DurationAsync(sourceAssetId, cancellationToken);
        List<TimeRange> targets = await db.MediaSegments.AsNoTracking()
            .Where(s => s.MediaAssetId == assetId && s.Kind == MediaSegmentKind.Visual)
            .OrderBy(s => s.StartMs)
            .Select(s => new TimeRange(s.Id, s.StartMs, s.EndMs))
            .ToListAsync(cancellationToken);
        List<SegmentAnalysis> sources = await db.SegmentAnalyses.AsNoTracking()
            .Include(a => a.Functions)
            .Where(a => a.IsCurrent && a.MediaSegment!.MediaAssetId == sourceAssetId && a.MediaSegment.Kind == MediaSegmentKind.Visual)
            .OrderBy(a => a.StartMs)
            .ToListAsync(cancellationToken);
        List<int> matches = MatchRanges(
            targets, targetDuration, sources.Select(a => new TimeRange(a.Id, a.StartMs, a.EndMs)).ToList(), sourceDuration);

        List<long> inherited = [];
        List<SegmentAnalysis> rows = [];
        Dictionary<long, long> sourceSegmentByTarget = [];
        for (int i = 0; i < targets.Count; i++)
        {
            if (matches[i] < 0)
            {
                continue;
            }

            SegmentAnalysis source = sources[matches[i]];
            inherited.Add(targets[i].Id);
            sourceSegmentByTarget[targets[i].Id] = source.MediaSegmentId;
            rows.Add(new SegmentAnalysis
            {
                MediaSegmentId = targets[i].Id,
                StartMs = targets[i].StartMs,
                EndMs = targets[i].EndMs,
                VisualDescription = source.VisualDescription,
                OnScreenText = source.OnScreenText,
                Product = source.Product,
                Actions = source.Actions,
                CommercialAngle = source.CommercialAngle,
                MainMessage = source.MainMessage,
                Audience = source.Audience,
                VisualQuality = source.VisualQuality,
                Confidence = source.Confidence,
                Provider = source.Provider,
                Model = source.Model,
                PromptVersion = source.PromptVersion,
                IsCurrent = true,
                InheritedFromAnalysisId = source.Id,
                Functions = source.Functions.Select(f => new SegmentAnalysisFunction { Function = f.Function }).ToList(),
            });
        }

        if (rows.Count == 0)
        {
            return 0;
        }

        List<long> sourceSegments = sourceSegmentByTarget.Values.Distinct().ToList();
        List<MediaSegmentTag> sourceTags = await db.MediaSegmentTags.AsNoTracking()
            .Where(t => sourceSegments.Contains(t.MediaSegmentId))
            .ToListAsync(cancellationToken);
        await db.InTransactionAsync(
            async () =>
            {
                await db.SegmentAnalyses
                    .Where(a => inherited.Contains(a.MediaSegmentId) && a.IsCurrent)
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.IsCurrent, false), cancellationToken);
                db.SegmentAnalyses.AddRange(rows);

                await db.MediaSegmentTags
                    .Where(t => inherited.Contains(t.MediaSegmentId) && t.Origin == MediaSegmentOrigin.Detected)
                    .ExecuteDeleteAsync(cancellationToken);
                List<MediaSegmentTag> manual = await db.MediaSegmentTags.AsNoTracking()
                    .Where(t => inherited.Contains(t.MediaSegmentId))
                    .ToListAsync(cancellationToken);
                foreach (long segmentId in inherited)
                {
                    HashSet<string> taken = new(manual.Where(t => t.MediaSegmentId == segmentId).Select(t => t.Text), StringComparer.OrdinalIgnoreCase);
                    foreach (MediaSegmentTag tag in sourceTags.Where(t => t.MediaSegmentId == sourceSegmentByTarget[segmentId]))
                    {
                        if (taken.Count < MediaRules.MaxTagsPerSegment && taken.Add(tag.Text))
                        {
                            db.MediaSegmentTags.Add(new MediaSegmentTag { MediaSegmentId = segmentId, Text = tag.Text, Origin = MediaSegmentOrigin.Detected });
                        }
                    }
                }

                await db.SaveChangesAsync(cancellationToken);
                await db.MediaAssets.Where(a => a.Id == assetId).ExecuteUpdateAsync(
                    s => s.SetProperty(a => a.AnalysisStatus, (MediaProcessingStatus?)MediaProcessingStatus.Ready)
                        .SetProperty(a => a.AnalysisError, (string?)null),
                    cancellationToken);
                await index.EnqueueIfConfiguredAsync(assetId, cancellationToken);
                return true;
            },
            cancellationToken);
        return rows.Count;
    }

    /// <summary>
    /// Empareja los tramos de un video con los de otro que tiene la misma imagen. Como las dos copias pueden
    /// diferir un poco en duración y en dónde cayó cada corte, se comparan posiciones relativas a la duración de
    /// cada una. A cada tramo de destino le corresponde el de origen con el que más se superpone, siempre que esa
    /// superposición cubra al menos la mitad del tramo de destino.
    /// </summary>
    /// <param name="targets">Tramos del video que hereda.</param>
    /// <param name="targetDurationMs">Su duración.</param>
    /// <param name="sources">Tramos del video original.</param>
    /// <param name="sourceDurationMs">Su duración.</param>
    /// <returns>Por cada tramo de destino, el índice del tramo de origen que le corresponde, o -1 si ninguno.</returns>
    public static List<int> MatchRanges(List<TimeRange> targets, int targetDurationMs, List<TimeRange> sources, int sourceDurationMs)
    {
        List<int> matches = [];
        foreach (TimeRange target in targets)
        {
            double start = target.StartMs / (double)Math.Max(1, targetDurationMs);
            double end = target.EndMs / (double)Math.Max(1, targetDurationMs);
            int best = -1;
            double bestOverlap = 0;
            for (int i = 0; i < sources.Count; i++)
            {
                double sourceStart = sources[i].StartMs / (double)Math.Max(1, sourceDurationMs);
                double sourceEnd = sources[i].EndMs / (double)Math.Max(1, sourceDurationMs);
                double overlap = Math.Min(end, sourceEnd) - Math.Max(start, sourceStart);
                if (overlap > bestOverlap)
                {
                    best = i;
                    bestOverlap = overlap;
                }
            }

            matches.Add(bestOverlap >= (end - start) / 2 ? best : -1);
        }

        return matches;
    }

    private async Task<int> DurationAsync(long assetId, CancellationToken cancellationToken)
    {
        return await db.MediaAssets.AsNoTracking()
            .Where(a => a.Id == assetId)
            .Select(a => a.DurationMs ?? 0)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Decide de qué instantes tomar cuadros para cada escena: uno si dura menos de 2 s, dos si dura menos de 6 s
    /// y tres si dura más. Si el total supera el tope, baja a un cuadro por escena.
    /// </summary>
    /// <param name="scenes">Escenas.</param>
    /// <param name="maxFrames">Tope de cuadros por video.</param>
    /// <returns>Los instantes (en milisegundos) de los cuadros de cada escena, en el mismo orden.</returns>
    /// <exception cref="BusinessException">409 <c>TOO_MANY_SCENES</c> si ni con un cuadro por escena se respeta el tope.</exception>
    public static List<List<int>> PlanFrames(List<SceneInput> scenes, int maxFrames)
    {
        List<List<int>> plan = scenes.Select(s => FrameTimes(s.StartMs, s.EndMs)).ToList();
        if (plan.Sum(f => f.Count) <= maxFrames)
        {
            return plan;
        }

        if (scenes.Count > maxFrames)
        {
            throw new BusinessException(
                409, $"El video tiene {scenes.Count} escenas y el tope es de {maxFrames} cuadros por análisis.", "TOO_MANY_SCENES");
        }

        return scenes.Select(s => new List<int> { (s.StartMs + s.EndMs) / 2 }).ToList();
    }

    private static List<int> FrameTimes(int startMs, int endMs)
    {
        int length = endMs - startMs;
        int count = length < 2000 ? 1 : length < 6000 ? 2 : 3;
        return Enumerable.Range(1, count).Select(i => startMs + (length * i / (count + 1))).ToList();
    }

    // La clave resume todo lo que determina la respuesta: modelo, prompt, contenido del video, tramos, cuadros y texto.
    private static string CacheKey(string model, string sha256, List<SceneInput> scenes, List<List<int>> frameTimes)
    {
        StringBuilder input = new();
        input.Append(model).Append('|').Append(SceneAnalysisPrompt.Version).Append('|').Append(sha256);
        for (int i = 0; i < scenes.Count; i++)
        {
            input.Append('|').Append(scenes[i].StartMs).Append('-').Append(scenes[i].EndMs)
                .Append(':').Append(string.Join(',', frameTimes[i]))
                .Append(':').Append(scenes[i].Transcript);
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input.ToString())));
    }

    private static BusinessException NotFound()
    {
        return new BusinessException(404, "El archivo no existe.", "MEDIA_NOT_FOUND");
    }

    private sealed record BatchEntry(long AssetId, AnalysisEstimate Estimate);

    private sealed record BatchPlan(
        int Pending, List<BatchEntry> Included, decimal LimitUsd, int OverVideoCap, int OverBudget, int NotConfigured);
}

/// <summary>
/// Trabajo de análisis con IA de un video. Si el video es un duplicado visual de otro ya analizado, copia ese
/// análisis y termina. Si no, y el mismo análisis ya está en cache, lo reutiliza sin llamar al
/// proveedor; si no, extrae los cuadros, llama al modelo una sola vez para todo el video, guarda el resultado en
/// cache y registra tokens y costo. Después, en una transacción, versiona el análisis de cada fragmento (el
/// anterior deja de ser vigente), reemplaza las etiquetas automáticas (las manuales no se tocan), reemplaza los
/// tramos narrativos detectados y deja el
/// análisis <c>Ready</c>. Un fragmento cuyo tramo cambió mientras corría el trabajo se saltea. Nunca modifica el
/// original.
/// </summary>
/// <param name="db">Contexto de datos, con la organización del trabajo activa.</param>
/// <param name="storage">Almacenamiento.</param>
/// <param name="tools">Herramientas de medios, para extraer cuadros.</param>
/// <param name="analyzer">Proveedor de IA.</param>
/// <param name="analysis">Arma el plan de análisis.</param>
/// <param name="usage">Registro de uso de IA.</param>
/// <param name="index">Índice de búsqueda, para reindexar el video al terminar.</param>
/// <param name="options">Tamaño de los cuadros.</param>
public sealed class MediaAnalyzeJobHandler(
    AppDbContext db,
    IMediaStorage storage,
    IMediaTools tools,
    ISegmentAnalyzer analyzer,
    AnalysisService analysis,
    AiUsageService usage,
    SegmentIndexService index,
    IOptions<AiOptions> options)
    : IJobHandler
{
    /// <summary>Tipo de trabajo que atiende.</summary>
    public const string JobType = "media.analyze";

    /// <inheritdoc />
    public string Type => JobType;

    /// <inheritdoc />
    /// <exception cref="PermanentJobException">Si el video no se puede analizar o el proveedor lo rechaza de forma
    /// definitiva; el análisis queda <c>Failed</c> con el motivo.</exception>
    /// <exception cref="AiProviderException">Si el proveedor falla de forma transitoria: el trabajo se reintenta.</exception>
    public async Task ExecuteAsync(JobExecution execution, CancellationToken cancellationToken)
    {
        MediaProbePayload? payload = JsonSerializer.Deserialize<MediaProbePayload>(execution.Job.PayloadJson);
        long assetId = payload?.AssetId ?? 0;
        try
        {
            // Un duplicado visual de un video ya analizado copia ese análisis: no se paga dos veces por la misma imagen.
            long? original = await analysis.InheritableSourceAsync(assetId, cancellationToken);
            if (original is not null)
            {
                await execution.ReportAsync(50, $"Heredando el análisis del archivo #{original}", cancellationToken);
                if (await analysis.InheritAsync(assetId, original.Value, cancellationToken) > 0)
                {
                    return;
                }
            }

            await execution.ReportAsync(5, "Preparando las escenas", cancellationToken);
            AnalysisPlan plan = await analysis.PlanAsync(assetId, cancellationToken);
            string? cachedJson = await db.AiResultCaches.AsNoTracking()
                .Where(c => c.CacheKey == plan.CacheKey)
                .Select(c => c.ResultJson)
                .FirstOrDefaultAsync(cancellationToken);
            AnalysisOutcome? outcome = cachedJson is null ? null : JsonSerializer.Deserialize<AnalysisOutcome>(cachedJson);
            bool fromCache = outcome is not null;
            outcome ??= await CallProviderAsync(execution, plan, cancellationToken);

            await execution.ReportAsync(90, "Guardando el análisis", cancellationToken);
            await db.InTransactionAsync(
                async () =>
                {
                    if (!fromCache)
                    {
                        db.AiResultCaches.Add(new AiResultCache { CacheKey = plan.CacheKey, ResultJson = JsonSerializer.Serialize(outcome) });
                        await usage.RecordAsync(
                            new AiUsageEntry(
                                "scene.analysis", outcome.Provider, outcome.Model, outcome.PromptVersion, outcome.InputTokens,
                                outcome.OutputTokens, outcome.CostUsd, outcome.DurationMs, execution.Job.Id),
                            cancellationToken);
                    }

                    await SaveAnalysesAsync(plan, outcome, cancellationToken);
                    await index.EnqueueIfConfiguredAsync(assetId, cancellationToken);
                    await db.MediaAssets.Where(a => a.Id == assetId).ExecuteUpdateAsync(
                        s => s.SetProperty(a => a.AnalysisStatus, (MediaProcessingStatus?)MediaProcessingStatus.Ready)
                            .SetProperty(a => a.AnalysisError, (string?)null),
                        cancellationToken);
                    return true;
                },
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException
            && (IsPermanent(exception) || execution.Job.Attempts >= execution.Job.MaxAttempts))
        {
            // Queda Failed con el motivo cuando reintentar no sirve o ya no quedan intentos; si no, sigue Pending.
            string message = exception.Message.Length > 1000 ? exception.Message[..1000] : exception.Message;
            await db.MediaAssets.Where(a => a.Id == assetId).ExecuteUpdateAsync(
                s => s.SetProperty(a => a.AnalysisStatus, (MediaProcessingStatus?)MediaProcessingStatus.Failed)
                    .SetProperty(a => a.AnalysisError, message),
                CancellationToken.None);
            throw new PermanentJobException(exception.Message);
        }
    }

    private static bool IsPermanent(Exception exception)
    {
        return exception is BusinessException or MediaToolException or JsonException
            || (exception is AiProviderException provider && !provider.Retryable);
    }

    private async Task<AnalysisOutcome> CallProviderAsync(JobExecution execution, AnalysisPlan plan, CancellationToken cancellationToken)
    {
        DirectoryInfo workFolder = Directory.CreateTempSubdirectory("ci-analysis-");
        try
        {
            string sourcePath = storage.GetLocalPath(plan.StoragePath);
            List<SceneInput> scenes = [];
            for (int i = 0; i < plan.Scenes.Count; i++)
            {
                await execution.ReportAsync(10 + (i * 40 / plan.Scenes.Count), $"Extrayendo cuadros de la escena {i + 1}", cancellationToken);
                List<string> frames = [];
                foreach (int atMs in plan.FrameTimes[i])
                {
                    string framePath = Path.Combine(workFolder.FullName, $"{i + 1}-{atMs}.jpg");
                    await tools.ExtractFrameAsync(sourcePath, framePath, atMs, options.Value.FrameMaxSide, cancellationToken);
                    frames.Add(framePath);
                }

                scenes.Add(plan.Scenes[i] with { FramePaths = frames });
            }

            await execution.ReportAsync(55, "Consultando al modelo", cancellationToken);
            return await analyzer.AnalyzeAsync(new VideoAnalysisRequest(plan.FileName, scenes), cancellationToken);
        }
        finally
        {
            workFolder.Delete(true);
        }
    }

    private async Task SaveAnalysesAsync(AnalysisPlan plan, AnalysisOutcome outcome, CancellationToken cancellationToken)
    {
        // Sólo se guardan los fragmentos que siguen teniendo el tramo que se analizó.
        List<long> segmentIds = [];
        List<SegmentAnalysis> rows = [];
        Dictionary<long, List<string>> tagsBySegment = [];
        List<long> planned = plan.SegmentIds;
        List<MediaSegment> current = await db.MediaSegments.AsNoTracking()
            .Where(s => planned.Contains(s.Id))
            .ToListAsync(cancellationToken);
        foreach (SceneAnalysis scene in outcome.Scenes.Where(s => s.Index >= 1 && s.Index <= plan.Scenes.Count))
        {
            SceneInput input = plan.Scenes[scene.Index - 1];
            long segmentId = plan.SegmentIds[scene.Index - 1];
            if (segmentIds.Contains(segmentId) || !current.Any(s => s.Id == segmentId && s.StartMs == input.StartMs && s.EndMs == input.EndMs))
            {
                continue;
            }

            segmentIds.Add(segmentId);
            tagsBySegment[segmentId] = scene.Tags;
            rows.Add(new SegmentAnalysis
            {
                MediaSegmentId = segmentId,
                StartMs = input.StartMs,
                EndMs = input.EndMs,
                VisualDescription = Limit(scene.VisualDescription, 1000),
                OnScreenText = Limit(scene.OnScreenText, 500),
                Product = Limit(scene.Product, 200),
                Actions = Limit(scene.Actions, 500),
                CommercialAngle = Limit(scene.CommercialAngle, 300),
                MainMessage = Limit(scene.MainMessage, 500),
                Audience = Limit(scene.Audience, 300),
                VisualQuality = Math.Clamp(scene.VisualQuality, 1, 5),
                Confidence = Math.Clamp(scene.Confidence, 0m, 1m),
                Provider = outcome.Provider,
                Model = outcome.Model,
                PromptVersion = outcome.PromptVersion,
                IsCurrent = true,
                Functions = scene.Functions.Distinct().Select(f => new SegmentAnalysisFunction { Function = f }).ToList(),
            });
        }

        await db.SegmentAnalyses
            .Where(a => segmentIds.Contains(a.MediaSegmentId) && a.IsCurrent)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.IsCurrent, false), cancellationToken);
        db.SegmentAnalyses.AddRange(rows);

        // Etiquetas: se reemplazan las automáticas; una que la persona ya puso a mano no se duplica.
        await db.MediaSegmentTags
            .Where(t => segmentIds.Contains(t.MediaSegmentId) && t.Origin == MediaSegmentOrigin.Detected)
            .ExecuteDeleteAsync(cancellationToken);
        List<MediaSegmentTag> manual = await db.MediaSegmentTags.AsNoTracking()
            .Where(t => segmentIds.Contains(t.MediaSegmentId))
            .ToListAsync(cancellationToken);
        foreach (long segmentId in segmentIds)
        {
            HashSet<string> taken = new(manual.Where(t => t.MediaSegmentId == segmentId).Select(t => t.Text), StringComparer.OrdinalIgnoreCase);
            foreach (string tag in tagsBySegment[segmentId].Select(t => Limit(t.Trim(), 50)).Where(t => t.Length > 0))
            {
                if (taken.Count < MediaRules.MaxTagsPerSegment && taken.Add(tag))
                {
                    db.MediaSegmentTags.Add(new MediaSegmentTag { MediaSegmentId = segmentId, Text = tag, Origin = MediaSegmentOrigin.Detected });
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        // Los tramos narrativos se arman sobre los números de escena: sólo valen si todas las escenas siguen como se
        // analizaron.
        if (segmentIds.Count == plan.Scenes.Count)
        {
            await SaveNarrativesAsync(plan, outcome, current, cancellationToken);
        }
    }

    // Reemplaza los tramos narrativos detectados del video. Cada tramo va de la primera a la última de sus escenas
    // y lleva su propio análisis (función y resumen); usa la miniatura de su primera escena. Se descartan los que
    // el modelo devuelve fuera de rango o superpuestos con uno anterior.
    private async Task SaveNarrativesAsync(
        AnalysisPlan plan, AnalysisOutcome outcome, List<MediaSegment> scenes, CancellationToken cancellationToken)
    {
        long assetId = scenes[0].MediaAssetId;
        await db.MediaSegments
            .Where(s => s.MediaAssetId == assetId && s.Kind == MediaSegmentKind.Narrative && s.Origin == MediaSegmentOrigin.Detected)
            .ExecuteDeleteAsync(cancellationToken);

        int lastScene = 0;
        foreach (NarrativeAnalysis narrative in outcome.Narratives.OrderBy(n => n.FromScene))
        {
            if (narrative.FromScene <= lastScene || narrative.ToScene < narrative.FromScene || narrative.ToScene > plan.Scenes.Count)
            {
                continue;
            }

            lastScene = narrative.ToScene;
            long firstSceneId = plan.SegmentIds[narrative.FromScene - 1];
            db.SegmentAnalyses.Add(new SegmentAnalysis
            {
                MediaSegment = new MediaSegment
                {
                    MediaAssetId = assetId,
                    Kind = MediaSegmentKind.Narrative,
                    Origin = MediaSegmentOrigin.Detected,
                    StartMs = plan.Scenes[narrative.FromScene - 1].StartMs,
                    EndMs = plan.Scenes[narrative.ToScene - 1].EndMs,
                    ThumbnailPath = scenes.First(s => s.Id == firstSceneId).ThumbnailPath,
                },
                StartMs = plan.Scenes[narrative.FromScene - 1].StartMs,
                EndMs = plan.Scenes[narrative.ToScene - 1].EndMs,
                VisualDescription = Limit(narrative.Summary, 1000),
                Provider = outcome.Provider,
                Model = outcome.Model,
                PromptVersion = outcome.PromptVersion,
                IsCurrent = true,
                Functions = [new SegmentAnalysisFunction { Function = narrative.Function }],
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static string Limit(string? value, int max)
    {
        string text = value?.Trim() ?? string.Empty;
        return text.Length > max ? text[..max] : text;
    }
}

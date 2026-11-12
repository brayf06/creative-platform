using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace CreativeIntelligence.Web.Services.Media;

/// <summary>
/// Índice de búsqueda: arma el texto que representa a cada fragmento (análisis, lo que se dice y etiquetas) y
/// encola su indexado. El indexado sólo se pide si hay proveedor de embeddings configurado.
/// </summary>
/// <param name="db">Contexto de datos.</param>
/// <param name="embeddings">Proveedor de embeddings.</param>
/// <param name="jobs">Cola de trabajos.</param>
public sealed class SegmentIndexService(AppDbContext db, IEmbeddingProvider embeddings, JobService jobs)
{
    private static readonly Dictionary<CreativeFunction, string> FunctionNames = new()
    {
        [CreativeFunction.Hook] = "hook, gancho inicial",
        [CreativeFunction.Problem] = "problema",
        [CreativeFunction.Presentation] = "presentación del producto",
        [CreativeFunction.Demonstration] = "demostración",
        [CreativeFunction.Benefit] = "beneficio, resultado",
        [CreativeFunction.SocialProof] = "prueba social, testimonio",
        [CreativeFunction.Objection] = "objeción",
        [CreativeFunction.Comparison] = "comparación, antes y después",
        [CreativeFunction.Offer] = "oferta, precio",
        [CreativeFunction.Cta] = "llamado a la acción, CTA",
        [CreativeFunction.Broll] = "b-roll, imagen de apoyo",
        [CreativeFunction.Closing] = "cierre",
    };

    /// <summary>
    /// Encola el indexado de los fragmentos de un video, si hay proveedor de embeddings configurado. Se llama
    /// cada vez que cambia algo que entra en el texto del índice (análisis, transcripción, etiquetas, cortes); el
    /// trabajo sólo vuelve a pedir el embedding de los fragmentos cuyo texto cambió.
    /// </summary>
    /// <param name="assetId">Video.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    public async Task EnqueueIfConfiguredAsync(long assetId, CancellationToken cancellationToken)
    {
        if (!embeddings.IsConfigured)
        {
            return;
        }

        await jobs.EnqueueAsync(
            MediaEmbedJobHandler.JobType,
            $"{MediaEmbedJobHandler.JobType}:{assetId}:{Guid.NewGuid():N}",
            JsonSerializer.Serialize(new MediaProbePayload(assetId)),
            cancellationToken);
    }

    /// <summary>Arma el texto de índice de cada fragmento de un video: sus escenas y sus tramos narrativos. No escribe.</summary>
    /// <param name="assetId">Video.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Un texto por fragmento; vacío si el fragmento no tiene análisis, voz ni etiquetas.</returns>
    public async Task<List<SegmentText>> BuildTextsAsync(long assetId, CancellationToken cancellationToken)
    {
        List<SegmentRow> segments = await db.MediaSegments.AsNoTracking()
            .Where(s => s.MediaAssetId == assetId)
            .OrderBy(s => s.StartMs)
            .Select(s => new SegmentRow
            {
                SegmentId = s.Id,
                AssetId = s.MediaAssetId,
                StartMs = s.StartMs,
                EndMs = s.EndMs,
                Tags = s.Tags.OrderBy(t => t.Text).Select(t => t.Text).ToList(),
            })
            .ToListAsync(cancellationToken);
        List<long> ids = segments.Select(s => s.SegmentId).ToList();
        Dictionary<long, AnalysisRow> analyses = await SearchService.AnalysesAsync(db, ids, cancellationToken);
        List<TranscriptRow> lines = await SearchService.TranscriptAsync(db, [assetId], cancellationToken);

        return segments
            .Select(s => new SegmentText(s.SegmentId, Compose(analyses.GetValueOrDefault(s.SegmentId), SearchService.Said(lines, s), s.Tags)))
            .ToList();
    }

    private static string Compose(AnalysisRow? analysis, string said, List<string> tags)
    {
        List<string> parts = [];
        if (analysis is not null)
        {
            Add(parts, "Función", string.Join("; ", analysis.Functions.Select(f => FunctionNames[f])));
            Add(parts, "Se ve", analysis.VisualDescription);
            Add(parts, "Mensaje", analysis.MainMessage);
            Add(parts, "Texto en pantalla", analysis.OnScreenText);
            Add(parts, "Producto", analysis.Product);
            Add(parts, "Acciones", analysis.Actions);
            Add(parts, "Enfoque", analysis.CommercialAngle);
            Add(parts, "Público", analysis.Audience);
        }

        Add(parts, "Se dice", said);
        Add(parts, "Etiquetas", string.Join(", ", tags));
        return string.Join(". ", parts);
    }

    private static void Add(List<string> parts, string label, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add($"{label}: {value.Trim()}");
        }
    }
}

/// <summary>
/// Indexado de los fragmentos de un video. Pide el embedding sólo de los fragmentos cuyo texto cambió desde la
/// última vez (compara un hash del texto), reemplaza esos vectores, borra los de fragmentos que quedaron sin
/// texto y registra el consumo, en una transacción. Es idempotente.
/// </summary>
/// <param name="db">Contexto de datos, con la organización del trabajo activa.</param>
/// <param name="index">Arma los textos.</param>
/// <param name="embeddings">Proveedor de embeddings.</param>
/// <param name="usage">Registro de uso de IA.</param>
public sealed class MediaEmbedJobHandler(AppDbContext db, SegmentIndexService index, IEmbeddingProvider embeddings, AiUsageService usage)
    : IJobHandler
{
    /// <summary>Tipo de trabajo que atiende.</summary>
    public const string JobType = "media.embed";

    /// <inheritdoc />
    public string Type => JobType;

    /// <inheritdoc />
    /// <exception cref="PermanentJobException">Si el proveedor rechaza el pedido de forma definitiva.</exception>
    /// <exception cref="AiProviderException">Si el proveedor falla de forma transitoria: el trabajo se reintenta.</exception>
    public async Task ExecuteAsync(JobExecution execution, CancellationToken cancellationToken)
    {
        MediaProbePayload? payload = JsonSerializer.Deserialize<MediaProbePayload>(execution.Job.PayloadJson);
        string model = embeddings.Model;
        List<SegmentText> texts = await index.BuildTextsAsync(payload?.AssetId ?? 0, cancellationToken);
        List<long> ids = texts.Select(t => t.SegmentId).ToList();
        Dictionary<long, string> indexed = await db.SegmentEmbeddings.AsNoTracking()
            .Where(e => ids.Contains(e.MediaSegmentId) && e.Model == model)
            .ToDictionaryAsync(e => e.MediaSegmentId, e => e.TextHash, cancellationToken);

        List<SegmentText> pending = texts.Where(t => t.Text.Length > 0 && indexed.GetValueOrDefault(t.SegmentId) != Hash(t.Text)).ToList();
        List<long> stale = texts.Where(t => t.Text.Length == 0).Select(t => t.SegmentId).Concat(pending.Select(t => t.SegmentId)).ToList();
        EmbeddingOutcome? outcome = null;
        try
        {
            if (pending.Count > 0)
            {
                await execution.ReportAsync(20, $"Indexando {pending.Count} fragmentos", cancellationToken);
                outcome = await embeddings.EmbedAsync(pending.Select(t => t.Text).ToList(), EmbeddingPurpose.Document, cancellationToken);
            }
        }
        catch (AiProviderException exception) when (!exception.Retryable)
        {
            throw new PermanentJobException(exception.Message);
        }

        await db.InTransactionAsync(
            async () =>
            {
                await db.SegmentEmbeddings
                    .Where(e => stale.Contains(e.MediaSegmentId) && e.Model == model)
                    .ExecuteDeleteAsync(cancellationToken);
                if (outcome is not null)
                {
                    for (int i = 0; i < pending.Count; i++)
                    {
                        db.SegmentEmbeddings.Add(new SegmentEmbedding
                        {
                            MediaSegmentId = pending[i].SegmentId,
                            Model = model,
                            Dimensions = outcome.Vectors[i].Length,
                            Vector = VectorMath.ToBytes(outcome.Vectors[i]),
                            TextHash = Hash(pending[i].Text),
                        });
                    }

                    await usage.RecordAsync(
                        new AiUsageEntry(
                            "segment.embedding", outcome.Provider, outcome.Model, "n/a", outcome.InputTokens, 0, outcome.CostUsd,
                            outcome.DurationMs, execution.Job.Id),
                        cancellationToken);
                }

                await db.SaveChangesAsync(cancellationToken);
                return true;
            },
            cancellationToken);
    }

    internal static string Hash(string text)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}

/// <summary>
/// Buscador de fragmentos de la organización activa. Los filtros se resuelven en la base; el orden por
/// significado, en memoria, comparando el vector de la consulta con el de cada fragmento candidato (con una
/// biblioteca de cientos de videos son miles de vectores, y alcanza).
/// </summary>
/// <param name="db">Contexto de datos.</param>
/// <param name="embeddings">Proveedor de embeddings.</param>
/// <param name="usage">Registro de uso de IA.</param>
public sealed class SearchService(AppDbContext db, IEmbeddingProvider embeddings, AiUsageService usage)
{
    /// <summary>
    /// Busca fragmentos (escenas y tramos narrativos). Primero interpreta la consulta para separar los filtros que
    /// venían escritos en ella (ver <see cref="SearchQueryParser"/>). Con texto y proveedor configurado ordena por
    /// significado; con texto y sin proveedor,
    /// exige todas las palabras; sin texto, aplica sólo los filtros. La consulta semántica llama al proveedor
    /// una vez por texto distinto: el vector se guarda en cache y se registra su costo.
    /// </summary>
    /// <param name="filter">Consulta y filtros.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Los fragmentos encontrados y cómo se resolvió la búsqueda.</returns>
    /// <exception cref="BusinessException">503 <c>AI_PROVIDER_FAILED</c> si el proveedor de embeddings falla al
    /// convertir la consulta.</exception>
    public async Task<SearchResult> SearchAsync(SegmentSearchFilter filter, CancellationToken cancellationToken)
    {
        // Lo que la persona eligió en el formulario manda; lo que se deduce del texto sólo completa lo que dejó vacío.
        ParsedQuery parsed = SearchQueryParser.Parse(filter.Query ?? string.Empty);
        List<string> interpreted = [];
        SegmentSearchFilter effective = new()
        {
            Query = parsed.Text,
            Function = filter.Function ?? parsed.Function,
            MaxDurationSeconds = filter.MaxDurationSeconds ?? parsed.MaxDurationSeconds,
            Kind = filter.Kind,
            Audio = filter.Audio,
            OnlyAuthorized = filter.OnlyAuthorized || parsed.OnlyAuthorized,
            OnlySelected = filter.OnlySelected || parsed.OnlySelected,
            Take = filter.Take,
        };
        if (filter.Function is null && parsed.Function is not null)
        {
            interpreted.Add($"función: {parsed.Function}");
        }

        if (filter.MaxDurationSeconds is null && parsed.MaxDurationSeconds is not null)
        {
            interpreted.Add($"duración de hasta {parsed.MaxDurationSeconds:0.##} s");
        }

        if (!filter.OnlyAuthorized && parsed.OnlyAuthorized)
        {
            interpreted.Add("sólo con autorización de reutilización");
        }

        if (!filter.OnlySelected && parsed.OnlySelected)
        {
            interpreted.Add("sólo elegidos para producción");
        }

        filter = effective;
        string query = parsed.Text;
        IQueryable<MediaSegment> candidates = Filtered(filter);
        if (query.Length == 0)
        {
            int total = await candidates.CountAsync(cancellationToken);
            List<long> recent = await candidates.OrderByDescending(s => s.Id).Take(filter.Take).Select(s => s.Id).ToListAsync(cancellationToken);
            return new SearchResult(SearchMode.Filters, embeddings.IsConfigured, total, await HitsAsync(recent, null, cancellationToken), interpreted);
        }

        if (!embeddings.IsConfigured)
        {
            foreach (string term in query.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(6))
            {
                candidates = WithTerm(candidates, term);
            }

            int total = await candidates.CountAsync(cancellationToken);
            List<long> matched = await candidates.OrderByDescending(s => s.Id).Take(filter.Take).Select(s => s.Id).ToListAsync(cancellationToken);
            return new SearchResult(SearchMode.Keyword, false, total, await HitsAsync(matched, null, cancellationToken), interpreted);
        }

        string model = embeddings.Model;
        float[] queryVector = await QueryVectorAsync(query, cancellationToken);
        List<SegmentVector> vectors = await candidates
            .Join(
                db.SegmentEmbeddings.Where(e => e.Model == model),
                s => s.Id,
                e => e.MediaSegmentId,
                (s, e) => new SegmentVector { SegmentId = s.Id, Vector = e.Vector })
            .ToListAsync(cancellationToken);
        Dictionary<long, double> scores = vectors
            .Select(v => KeyValuePair.Create(v.SegmentId, VectorMath.Dot(queryVector, VectorMath.FromBytes(v.Vector))))
            .OrderByDescending(p => p.Value)
            .Take(filter.Take)
            .ToDictionary(p => p.Key, p => p.Value);
        return new SearchResult(
            SearchMode.Semantic, true, vectors.Count, await HitsAsync(scores.Keys.ToList(), scores, cancellationToken), interpreted);
    }

    /// <summary>Análisis vigente de un conjunto de fragmentos, por fragmento. Una sola consulta.</summary>
    /// <param name="db">Contexto de datos.</param>
    /// <param name="segmentIds">Fragmentos.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Diccionario por fragmento; los no analizados no figuran.</returns>
    internal static async Task<Dictionary<long, AnalysisRow>> AnalysesAsync(
        AppDbContext db, List<long> segmentIds, CancellationToken cancellationToken)
    {
        return await db.SegmentAnalyses.AsNoTracking()
            .Where(a => a.IsCurrent && segmentIds.Contains(a.MediaSegmentId))
            .Select(a => new AnalysisRow
            {
                SegmentId = a.MediaSegmentId,
                VisualDescription = a.VisualDescription,
                OnScreenText = a.OnScreenText,
                Product = a.Product,
                Actions = a.Actions,
                CommercialAngle = a.CommercialAngle,
                MainMessage = a.MainMessage,
                Audience = a.Audience,
                Functions = a.Functions.OrderBy(f => f.Function).Select(f => f.Function).ToList(),
            })
            .ToDictionaryAsync(a => a.SegmentId, cancellationToken);
    }

    /// <summary>Frases transcriptas de un conjunto de videos. Una sola consulta.</summary>
    /// <param name="db">Contexto de datos.</param>
    /// <param name="assetIds">Videos.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Las frases, en orden temporal.</returns>
    internal static async Task<List<TranscriptRow>> TranscriptAsync(AppDbContext db, List<long> assetIds, CancellationToken cancellationToken)
    {
        return await db.MediaTranscriptLines.AsNoTracking()
            .Where(l => assetIds.Contains(l.MediaAssetId))
            .OrderBy(l => l.StartMs)
            .Select(l => new TranscriptRow { AssetId = l.MediaAssetId, StartMs = l.StartMs, EndMs = l.EndMs, Text = l.Text })
            .ToListAsync(cancellationToken);
    }

    /// <summary>Lo que se dice en un fragmento, a partir de las frases de su video.</summary>
    /// <param name="lines">Frases de uno o más videos.</param>
    /// <param name="segment">Fragmento.</param>
    /// <returns>El texto del fragmento; vacío si no se dice nada.</returns>
    internal static string Said(List<TranscriptRow> lines, SegmentRow segment)
    {
        return TranscriptService.TextBetween(
            lines.Where(l => l.AssetId == segment.AssetId).Select(l => new TranscriptLineItem(l.StartMs, l.EndMs, l.Text)),
            segment.StartMs,
            segment.EndMs);
    }

    private IQueryable<MediaSegment> Filtered(SegmentSearchFilter filter)
    {
        IQueryable<MediaSegment> query = db.MediaSegments.AsNoTracking();
        if (filter.Kind is not null)
        {
            MediaSegmentKind kind = filter.Kind.Value;
            query = query.Where(s => s.Kind == kind);
        }

        // Los mismos umbrales que MediaRules.ClassifyAudio, escritos como condición para que los resuelva la base.
        query = filter.Audio switch
        {
            AudioQuality.Silent => query.Where(s => s.AudioMeanDb < MediaRules.AudioSilentBelowDb),
            AudioQuality.Clipped => query.Where(s => s.AudioMeanDb >= MediaRules.AudioSilentBelowDb && s.AudioPeakDb >= MediaRules.AudioClippedAtDb),
            AudioQuality.Low => query.Where(s => s.AudioMeanDb >= MediaRules.AudioSilentBelowDb
                && s.AudioMeanDb < MediaRules.AudioLowBelowDb && s.AudioPeakDb < MediaRules.AudioClippedAtDb),
            AudioQuality.Good => query.Where(s => s.AudioMeanDb >= MediaRules.AudioLowBelowDb && s.AudioPeakDb < MediaRules.AudioClippedAtDb),
            _ => query,
        };

        if (filter.MaxDurationSeconds is not null)
        {
            int maxMs = (int)(filter.MaxDurationSeconds.Value * 1000);
            query = query.Where(s => s.EndMs - s.StartMs <= maxMs);
        }

        if (filter.Function is not null)
        {
            CreativeFunction function = filter.Function.Value;
            query = query.Where(s => db.SegmentAnalyses.Any(
                a => a.MediaSegmentId == s.Id && a.IsCurrent && a.Functions.Any(f => f.Function == function)));
        }

        if (filter.OnlyAuthorized)
        {
            query = query.Where(s => s.MediaAsset!.ReuseAuthorized
                && (s.MediaAsset.RightsExpireOn == null || s.MediaAsset.RightsExpireOn >= DateOnly.FromDateTime(AppDbContext.UtcNow())));
        }

        if (filter.OnlySelected)
        {
            query = query.Where(s => s.SelectedForProduction);
        }

        return query;
    }

    // Una palabra coincide si está en el análisis vigente, en una etiqueta o en lo que se dice durante el fragmento.
    private IQueryable<MediaSegment> WithTerm(IQueryable<MediaSegment> query, string term)
    {
        return query.Where(s =>
            s.Tags.Any(t => t.Text.Contains(term))
            || db.SegmentAnalyses.Any(a => a.MediaSegmentId == s.Id && a.IsCurrent
                && (a.VisualDescription.Contains(term) || a.MainMessage.Contains(term) || a.OnScreenText.Contains(term)
                    || a.Product.Contains(term) || a.Actions.Contains(term) || a.CommercialAngle.Contains(term)
                    || a.Audience.Contains(term)))
            || db.MediaTranscriptLines.Any(l => l.MediaAssetId == s.MediaAssetId && l.Text.Contains(term)
                && (l.StartMs + l.EndMs) / 2 >= s.StartMs && (l.StartMs + l.EndMs) / 2 < s.EndMs));
    }

    private async Task<float[]> QueryVectorAsync(string query, CancellationToken cancellationToken)
    {
        string cacheKey = MediaEmbedJobHandler.Hash($"{embeddings.Model}|query|{query.ToLowerInvariant()}");
        string? cached = await db.AiResultCaches.AsNoTracking()
            .Where(c => c.CacheKey == cacheKey)
            .Select(c => c.ResultJson)
            .FirstOrDefaultAsync(cancellationToken);
        if (cached is not null)
        {
            return VectorMath.FromBytes(Convert.FromBase64String(cached));
        }

        EmbeddingOutcome outcome;
        try
        {
            outcome = await embeddings.EmbedAsync([query], EmbeddingPurpose.Query, cancellationToken);
        }
        catch (AiProviderException exception)
        {
            throw new BusinessException(503, $"No se pudo consultar al proveedor de IA. {exception.Message}", "AI_PROVIDER_FAILED");
        }

        float[] vector = outcome.Vectors[0];
        db.AiResultCaches.Add(new AiResultCache { CacheKey = cacheKey, ResultJson = Convert.ToBase64String(VectorMath.ToBytes(vector)) });
        await usage.RecordAsync(
            new AiUsageEntry("search.query", outcome.Provider, outcome.Model, "n/a", outcome.InputTokens, 0, outcome.CostUsd, outcome.DurationMs, null),
            cancellationToken);
        return vector;
    }

    private async Task<List<SearchHit>> HitsAsync(List<long> ids, Dictionary<long, double>? scores, CancellationToken cancellationToken)
    {
        List<SegmentRow> rows = await db.MediaSegments.AsNoTracking()
            .Where(s => ids.Contains(s.Id))
            .Select(s => new SegmentRow
            {
                SegmentId = s.Id,
                AssetId = s.MediaAssetId,
                FileName = s.MediaAsset!.OriginalFileName,
                StartMs = s.StartMs,
                EndMs = s.EndMs,
                HasThumbnail = s.ThumbnailPath != null,
                ReuseAuthorized = s.MediaAsset.ReuseAuthorized
                    && (s.MediaAsset.RightsExpireOn == null || s.MediaAsset.RightsExpireOn >= DateOnly.FromDateTime(AppDbContext.UtcNow())),
                SelectedForProduction = s.SelectedForProduction,
                Tags = s.Tags.OrderBy(t => t.Text).Select(t => t.Text).ToList(),
                Kind = s.Kind,
                AudioMeanDb = s.AudioMeanDb,
                AudioPeakDb = s.AudioPeakDb,
            })
            .ToListAsync(cancellationToken);
        Dictionary<long, AnalysisRow> analyses = await AnalysesAsync(db, ids, cancellationToken);
        List<TranscriptRow> lines = await TranscriptAsync(db, rows.Select(r => r.AssetId).Distinct().ToList(), cancellationToken);

        // Se respeta el orden pedido (por similitud o por novedad), no el que devolvió la base.
        return ids
            .Join(rows, id => id, r => r.SegmentId, (id, r) => r)
            .Select(r =>
            {
                AnalysisRow? analysis = analyses.GetValueOrDefault(r.SegmentId);
                return new SearchHit(
                    r.SegmentId, r.AssetId, r.FileName, r.StartMs, r.EndMs,
                    scores is null ? null : Math.Round(scores[r.SegmentId], 3),
                    r.HasThumbnail, r.ReuseAuthorized, r.SelectedForProduction,
                    analysis?.Functions ?? [], analysis?.VisualDescription ?? string.Empty, analysis?.MainMessage ?? string.Empty,
                    Said(lines, r), r.Tags, r.Kind, r.AudioMeanDb, r.AudioPeakDb);
            })
            .ToList();
    }
}

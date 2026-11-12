namespace CreativeIntelligence.Web.Services.Composition;

/// <summary>
/// Genera montajes distintos a partir de un conjunto de fragmentos. No usa IA: combina los fragmentos según su
/// función (un hook al principio si hay hooks, de uno a cuatro fragmentos de desarrollo, un cierre al final si hay
/// CTA, cierre u oferta) y descarta los montajes demasiado parecidos a uno ya aceptado. Con la misma semilla
/// produce siempre lo mismo, así la vista previa coincide con el lote que se crea.
/// </summary>
public static class VariantGenerator
{
    /// <summary>Cantidad máxima de fragmentos de desarrollo por montaje.</summary>
    public const int MaxBodyFragments = 4;

    /// <summary>
    /// Qué tan parecidos son dos montajes, de 0 a 1. El 70 % lo aporta el material compartido (duración de los
    /// fragmentos en común sobre la duración de todos los que usa alguno de los dos) y el 30 % el orden (posiciones
    /// en las que los dos tienen el mismo fragmento). Dos montajes iguales dan 1; los mismos fragmentos en otro
    /// orden dan entre 0,7 y 1.
    /// </summary>
    /// <param name="left">Un montaje.</param>
    /// <param name="right">Otro montaje.</param>
    /// <returns>Similitud entre 0 y 1.</returns>
    public static decimal Similarity(IReadOnlyList<PoolFragment> left, IReadOnlyList<PoolFragment> right)
    {
        if (left.Count == 0 || right.Count == 0)
        {
            return 0m;
        }

        Dictionary<long, int> durations = left.Concat(right).GroupBy(f => f.SegmentId).ToDictionary(g => g.Key, g => g.First().DurationMs);
        HashSet<long> leftIds = left.Select(f => f.SegmentId).ToHashSet();
        HashSet<long> rightIds = right.Select(f => f.SegmentId).ToHashSet();
        decimal shared = leftIds.Intersect(rightIds).Sum(id => durations[id]);
        decimal all = durations.Values.Sum();
        int samePosition = left.Zip(right).Count(pair => pair.First.SegmentId == pair.Second.SegmentId);
        return (0.7m * shared / all) + (0.3m * samePosition / Math.Max(left.Count, right.Count));
    }

    /// <summary>Genera hasta <paramref name="count"/> montajes distintos.</summary>
    /// <param name="pool">Fragmentos disponibles, sin repetir.</param>
    /// <param name="count">Cantidad pedida.</param>
    /// <param name="maxDurationMs">Duración máxima de cada montaje.</param>
    /// <param name="threshold">Similitud a partir de la cual un montaje se descarta por parecerse a otro.</param>
    /// <param name="seed">Semilla: la misma da siempre los mismos montajes.</param>
    /// <returns>Los montajes aceptados (pueden ser menos que los pedidos si los fragmentos no dan para más) y
    /// cuántos candidatos se descartaron por parecidos.</returns>
    public static GeneratedPlan Generate(IReadOnlyList<PoolFragment> pool, int count, int maxDurationMs, decimal threshold, int seed)
    {
        List<PoolFragment> hooks = pool.Where(f => f.Role == CreativeFunction.Hook).ToList();
        List<PoolFragment> closings = pool
            .Where(f => f.Role is CreativeFunction.Cta or CreativeFunction.Closing or CreativeFunction.Offer)
            .ToList();
        List<PoolFragment> body = pool.Except(hooks).Except(closings).ToList();

        // Sin hook ni cierre el montaje es sólo desarrollo: se piden al menos dos fragmentos para que haya montaje.
        int minBody = hooks.Count == 0 && closings.Count == 0 ? Math.Min(2, body.Count) : Math.Min(1, body.Count);
        int maxBody = Math.Min(body.Count, MaxBodyFragments);
        Random random = new(seed);
        List<List<PoolFragment>> accepted = [];
        int discarded = 0;
        int attempts = Math.Max(500, count * 100);
        while (accepted.Count < count && attempts-- > 0)
        {
            List<PoolFragment> middle = body.OrderBy(_ => random.Next()).Take(random.Next(minBody, maxBody + 1)).ToList();
            PoolFragment? hook = hooks.Count > 0 ? hooks[random.Next(hooks.Count)] : null;
            PoolFragment? closing = closings.Count > 0 ? closings[random.Next(closings.Count)] : null;
            int fixedMs = (hook?.DurationMs ?? 0) + (closing?.DurationMs ?? 0);
            while (middle.Count > 0 && fixedMs + middle.Sum(f => f.DurationMs) > maxDurationMs)
            {
                middle.RemoveAt(middle.Count - 1);
            }

            List<PoolFragment> candidate = [];
            if (hook is not null)
            {
                candidate.Add(hook);
            }

            candidate.AddRange(middle);
            if (closing is not null)
            {
                candidate.Add(closing);
            }

            if (candidate.Count == 0 || candidate.Sum(f => f.DurationMs) > maxDurationMs)
            {
                continue;
            }

            if (accepted.Any(other => Similarity(candidate, other) >= threshold))
            {
                discarded++;
                continue;
            }

            accepted.Add(candidate);
        }

        return new GeneratedPlan(accepted, discarded);
    }
}

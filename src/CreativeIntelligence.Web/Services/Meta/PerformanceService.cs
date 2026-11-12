using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace CreativeIntelligence.Web.Services.Meta;

/// <summary>
/// Reglas del análisis de rendimiento que no dependen de la base: cuánta evidencia hay, qué tan distinguible del
/// azar es una diferencia, cuándo un anuncio muestra posible fatiga y cómo se agrupan los anuncios por componente.
/// Los umbrales son puntos de partida elegidos al construir el módulo, sin calibrar con datos reales: están acá,
/// con nombre, para poder revisarlos.
/// </summary>
public static class PerformanceRules
{
    /// <summary>Con menos anuncios que esto, la evidencia es insuficiente.</summary>
    public const int MinAds = 2;

    /// <summary>Con menos impresiones que esto, la evidencia es insuficiente.</summary>
    public const long MinImpressions = 5_000;

    /// <summary>Anuncios a partir de los cuales la evidencia puede ser moderada.</summary>
    public const int ModerateAds = 5;

    /// <summary>Impresiones a partir de las cuales la evidencia puede ser moderada.</summary>
    public const long ModerateImpressions = 50_000;

    /// <summary>Días con datos que necesita un anuncio para buscarle fatiga.</summary>
    public const int FatigueMinDays = 6;

    /// <summary>Impresiones que necesita cada mitad del período para buscar fatiga.</summary>
    public const long FatigueMinImpressions = 2_000;

    /// <summary>Caída relativa del CTR a partir de la cual se señala posible fatiga.</summary>
    public const decimal FatigueMinDrop = 0.20m;

    /// <summary>Cuánta evidencia hay detrás de un grupo de anuncios. Para ser moderada exige, además de volumen,
    /// anuncios de más de una campaña: si todos son de la misma, el grupo y la campaña no se pueden separar.</summary>
    /// <param name="ads">Anuncios del grupo.</param>
    /// <param name="campaigns">Campañas distintas.</param>
    /// <param name="impressions">Impresiones del grupo.</param>
    /// <returns>El nivel de evidencia.</returns>
    public static EvidenceLevel Evidence(int ads, int campaigns, long impressions)
    {
        if (ads < MinAds || impressions < MinImpressions)
        {
            return EvidenceLevel.Insufficient;
        }

        return ads >= ModerateAds && campaigns >= 2 && impressions >= ModerateImpressions ? EvidenceLevel.Moderate : EvidenceLevel.Preliminary;
    }

    /// <summary>
    /// Estadístico z de la diferencia entre dos proporciones (por ejemplo, dos CTR). Es una aproximación: trata
    /// cada impresión como independiente, y no lo son (una persona ve el anuncio varias veces), así que exagera
    /// algo la certeza.
    /// </summary>
    /// <param name="hits">Aciertos del primer grupo (clics).</param>
    /// <param name="trials">Intentos del primer grupo (impresiones).</param>
    /// <param name="otherHits">Aciertos del segundo grupo.</param>
    /// <param name="otherTrials">Intentos del segundo grupo.</param>
    /// <returns>El estadístico (positivo si el primer grupo es mayor), o nulo si no se puede calcular.</returns>
    public static double? ZScore(long hits, long trials, long otherHits, long otherTrials)
    {
        if (trials <= 0 || otherTrials <= 0)
        {
            return null;
        }

        double pooled = (double)(hits + otherHits) / (trials + otherTrials);
        double error = Math.Sqrt(pooled * (1 - pooled) * ((1.0 / trials) + (1.0 / otherTrials)));
        return error == 0 ? null : (((double)hits / trials) - ((double)otherHits / otherTrials)) / error;
    }

    /// <summary>Clasifica un estadístico z: compatible con el azar, diferencia probable (|z| ≥ 1,96) o clara
    /// (|z| ≥ 2,58).</summary>
    /// <param name="z">Estadístico; nulo si no se pudo calcular.</param>
    /// <returns>La fuerza de la señal.</returns>
    public static SignalStrength Strength(double? z)
    {
        return z is null ? SignalStrength.None
            : Math.Abs(z.Value) >= 2.58 ? SignalStrength.Clear
            : Math.Abs(z.Value) >= 1.96 ? SignalStrength.Probable
            : SignalStrength.NotDistinguishable;
    }

    /// <summary>
    /// Busca posible fatiga en un anuncio: compara el CTR de la primera mitad de sus días con el de la segunda. La
    /// señala sólo si hay días e impresiones suficientes, la caída es de al menos un 20 % y difícilmente sea azar.
    /// Es una observación: el CTR también cae por estacionalidad, cambios de presupuesto o de audiencia.
    /// </summary>
    /// <param name="days">Días del anuncio, en orden.</param>
    /// <returns>La caída observada, o nulo si no hay señal.</returns>
    public static FatigueSignal? Fatigue(IReadOnlyList<DayPoint> days)
    {
        if (days.Count < FatigueMinDays)
        {
            return null;
        }

        int half = days.Count / 2;
        long firstImpressions = days.Take(half).Sum(d => d.Impressions);
        long firstClicks = days.Take(half).Sum(d => d.Clicks);
        long lastImpressions = days.Skip(days.Count - half).Sum(d => d.Impressions);
        long lastClicks = days.Skip(days.Count - half).Sum(d => d.Clicks);
        if (firstImpressions < FatigueMinImpressions || lastImpressions < FatigueMinImpressions || firstClicks == 0)
        {
            return null;
        }

        decimal first = (decimal)firstClicks / firstImpressions;
        decimal last = (decimal)lastClicks / lastImpressions;
        decimal drop = (first - last) / first;
        double? z = ZScore(lastClicks, lastImpressions, firstClicks, firstImpressions);
        return drop >= FatigueMinDrop && z is <= -1.96 ? new FatigueSignal(first, last, drop, days.Count) : null;
    }

    /// <summary>Suma las métricas de varios anuncios. Una cantidad opcional queda sin dato sólo si ninguno la informa.</summary>
    /// <param name="metrics">Métricas a sumar.</param>
    /// <returns>La suma.</returns>
    public static AdMetrics Sum(IReadOnlyCollection<AdMetrics> metrics)
    {
        return new AdMetrics(
            metrics.Sum(m => m.Spend),
            metrics.Sum(m => m.Impressions),
            metrics.Sum(m => m.Clicks),
            metrics.Any(m => m.VideoPlays is not null) ? metrics.Sum(m => m.VideoPlays ?? 0) : null,
            metrics.Any(m => m.ThruPlays is not null) ? metrics.Sum(m => m.ThruPlays ?? 0) : null,
            metrics.Any(m => m.Purchases is not null) ? metrics.Sum(m => m.Purchases ?? 0) : null,
            metrics.Any(m => m.PurchaseValue is not null) ? metrics.Sum(m => m.PurchaseValue ?? 0) : null,
            metrics.Sum(m => m.Days));
    }

    /// <summary>
    /// Agrupa los anuncios por los valores de una dimensión y compara cada grupo con el resto. Un anuncio puede
    /// caer en varios grupos (usa varios fragmentos). Función pura.
    /// </summary>
    /// <param name="ads">Anuncios vinculados, con sus métricas y sus componentes.</param>
    /// <param name="keys">Qué valores de la dimensión tiene cada anuncio.</param>
    /// <returns>Los grupos, del que más impresiones tiene al que menos.</returns>
    public static List<ComponentGroup> Group(IReadOnlyList<AdComponents> ads, Func<AdComponents, IEnumerable<ComponentKey>> keys)
    {
        Dictionary<string, ComponentKey> labels = [];
        Dictionary<string, List<AdComponents>> members = [];
        foreach (AdComponents ad in ads)
        {
            foreach (ComponentKey key in keys(ad).DistinctBy(k => k.Key))
            {
                labels.TryAdd(key.Key, key);
                if (!members.TryGetValue(key.Key, out List<AdComponents>? list))
                {
                    list = [];
                    members[key.Key] = list;
                }

                list.Add(ad);
            }
        }

        List<ComponentGroup> groups = [];
        foreach (KeyValuePair<string, List<AdComponents>> entry in members)
        {
            HashSet<long> inside = entry.Value.Select(a => a.AdId).ToHashSet();
            List<AdComponents> others = ads.Where(a => !inside.Contains(a.AdId)).ToList();
            AdMetrics metrics = Sum(entry.Value.Select(a => a.Metrics).ToList());
            AdMetrics rest = Sum(others.Select(a => a.Metrics).ToList());
            int campaigns = entry.Value.Select(a => a.CampaignId).Distinct().Count();
            EvidenceLevel evidence = Evidence(entry.Value.Count, campaigns, metrics.Impressions);

            // La señal sólo se calcula si hay evidencia y hay con qué comparar.
            double? z = evidence == EvidenceLevel.Insufficient || others.Count == 0
                ? null
                : ZScore(metrics.Clicks, metrics.Impressions, rest.Clicks, rest.Impressions);
            SignalStrength signal = Strength(z);
            groups.Add(new ComponentGroup(
                entry.Key, labels[entry.Key].Label, labels[entry.Key].Fragment, entry.Value.Count, campaigns, metrics, rest, evidence, signal,
                z is > 0, Reading(entry.Value.Count, campaigns, metrics, rest, others.Count, evidence, signal, z is > 0), inside.Order().ToList()));
        }

        return groups.OrderByDescending(g => g.Metrics.Impressions).ThenBy(g => g.Label, StringComparer.Ordinal).ToList();
    }

    /// <summary>Escribe la lectura de un grupo: qué se observa, qué tan firme es y qué no se puede concluir.</summary>
    /// <param name="ads">Anuncios del grupo.</param>
    /// <param name="campaigns">Campañas distintas.</param>
    /// <param name="metrics">Métricas del grupo.</param>
    /// <param name="rest">Métricas del resto.</param>
    /// <param name="restAds">Anuncios del resto.</param>
    /// <param name="evidence">Nivel de evidencia.</param>
    /// <param name="signal">Señal de la diferencia de CTR.</param>
    /// <param name="higher">Si el CTR del grupo es mayor.</param>
    /// <returns>La lectura.</returns>
    public static string Reading(
        int ads, int campaigns, AdMetrics metrics, AdMetrics rest, int restAds, EvidenceLevel evidence, SignalStrength signal, bool higher)
    {
        if (evidence == EvidenceLevel.Insufficient)
        {
            return $"Datos insuficientes: {ads} anuncio(s) y {metrics.Impressions} impresiones. Todavía no se puede decir nada.";
        }

        if (restAds == 0)
        {
            return "Todos los anuncios analizados lo tienen: no hay con qué comparar.";
        }

        string difference = signal switch
        {
            SignalStrength.Clear => $"una diferencia clara {(higher ? "a favor" : "en contra")}",
            SignalStrength.Probable => $"una diferencia probable {(higher ? "a favor" : "en contra")}",
            _ => "una diferencia que no se distingue del azar",
        };
        string scope = campaigns == 1
            ? "todos estos anuncios son de una misma campaña, así que el componente y la campaña no se pueden separar"
            : $"los anuncios son de {campaigns} campañas";
        return $"CTR {Percent(metrics.Ctr)} frente a {Percent(rest.Ctr)} del resto: {difference}. Es una correlación, no una causa: "
            + $"{scope}, y presupuesto, audiencia y fecha no están controlados.";
    }

    private static string Percent(decimal? value)
    {
        return value is null ? "sin dato" : (value.Value * 100).ToString("0.00", CultureInfo.InvariantCulture) + " %";
    }
}

/// <summary>
/// Análisis por componente del rendimiento de la organización activa: relaciona las métricas de los anuncios con lo
/// que comparten sus creatividades (el fragmento con que abren, con que cierran, cada fragmento, la estructura y la
/// terminación). Sólo ve los anuncios vinculados a un video generado. Todo lo que devuelve son observaciones y
/// correlaciones con su nivel de evidencia: no atribuye causas ni declara ganadores.
/// </summary>
/// <param name="db">Contexto de datos.</param>
/// <param name="meta">Métricas importadas.</param>
public sealed class PerformanceService(AppDbContext db, MetaService meta)
{
    /// <summary>Analiza un período. No escribe.</summary>
    /// <param name="filter">Período y campaña.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Las agrupaciones con sus lecturas y los anuncios con señal de posible fatiga.</returns>
    public async Task<PerformanceAnalysis> AnalyzeAsync(PerformanceFilter filter, CancellationToken cancellationToken)
    {
        List<CampaignOption> campaigns = await db.MetaCampaigns.AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new CampaignOption(c.Id, c.Name))
            .ToListAsync(cancellationToken);
        List<LinkedAdRow> ads = await db.MetaAds.AsNoTracking()
            .Where(a => filter.CampaignId == null || a.AdSet!.MetaCampaignId == filter.CampaignId)
            .Select(a => new LinkedAdRow
            {
                AdId = a.Id,
                Name = a.Name,
                CampaignId = a.AdSet!.MetaCampaignId,
                CampaignName = a.AdSet.Campaign!.Name,
                RecipeId = db.CreativeVariants.Where(v => v.Id == a.CreativeVariantId).Select(v => (long?)v.CreativeRecipeId).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);
        Dictionary<long, AdTotalsRow> totals = (await meta.TotalsAsync(new MetaFilter { From = filter.From, To = filter.To }, null, cancellationToken))
            .ToDictionary(t => t.AdId);
        List<LinkedAdRow> withData = ads.Where(a => totals.ContainsKey(a.AdId)).ToList();
        List<LinkedAdRow> linked = withData.Where(a => a.RecipeId is not null).ToList();

        List<AdComponents> components = await BuildAsync(linked, totals, cancellationToken);
        return new PerformanceAnalysis(
            filter, campaigns, withData.Count, linked.Count, Dimensions(components), await FatigueAsync(filter, withData, cancellationToken));
    }

    /// <summary>Devuelve los componentes de las creatividades de unos anuncios; los que no están vinculados a un
    /// video no aparecen. No trae métricas. No escribe.</summary>
    /// <param name="adIds">Anuncios.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Un elemento por anuncio vinculado.</returns>
    public async Task<List<AdComponents>> ComponentsOfAsync(List<long> adIds, CancellationToken cancellationToken)
    {
        List<LinkedAdRow> linked = await db.MetaAds.AsNoTracking()
            .Where(a => adIds.Contains(a.Id) && a.CreativeVariantId != null)
            .Select(a => new LinkedAdRow
            {
                AdId = a.Id,
                Name = a.Name,
                CampaignId = a.AdSet!.MetaCampaignId,
                RecipeId = db.CreativeVariants.Where(v => v.Id == a.CreativeVariantId).Select(v => (long?)v.CreativeRecipeId).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);
        return await BuildAsync(linked, [], cancellationToken);
    }

    /// <summary>Devuelve los componentes de las recetas más recientes de la organización, publicadas o no: sirve
    /// para saber cuánto se usa ya un componente. En cada elemento, <see cref="AdComponents.AdId"/> es la receta.
    /// No trae métricas. No escribe.</summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Hasta 500 recetas.</returns>
    public async Task<List<AdComponents>> RecipeComponentsAsync(CancellationToken cancellationToken)
    {
        List<LinkedAdRow> recipes = await db.CreativeRecipes.AsNoTracking()
            .OrderByDescending(r => r.Id)
            .Take(500)
            .Select(r => new LinkedAdRow { AdId = r.Id, Name = r.Name, RecipeId = r.Id })
            .ToListAsync(cancellationToken);
        return await BuildAsync(recipes, [], cancellationToken);
    }

    // Lee la terminación y los tramos de las recetas de los anuncios vinculados y arma sus componentes.
    private async Task<List<AdComponents>> BuildAsync(
        List<LinkedAdRow> linked, Dictionary<long, AdTotalsRow> totals, CancellationToken cancellationToken)
    {
        List<long> recipeIds = linked.Select(a => a.RecipeId!.Value).Distinct().ToList();
        Dictionary<long, RecipeFinishRow> finishes = await db.CreativeRecipes.AsNoTracking()
            .Where(r => recipeIds.Contains(r.Id))
            .Select(r => new RecipeFinishRow { RecipeId = r.Id, Subtitles = r.Subtitles, Transition = r.Transition, Music = r.MusicTrackId != null })
            .ToDictionaryAsync(r => r.RecipeId, cancellationToken);
        ILookup<long, ComponentRef> items = (await db.RecipeItems.AsNoTracking()
                .Where(i => recipeIds.Contains(i.CreativeRecipeId))
                .OrderBy(i => i.Position)
                .Select(i => new RecipeComponentRow
                {
                    RecipeId = i.CreativeRecipeId,
                    Component = new ComponentRef(i.MediaAssetId, i.MediaAsset!.OriginalFileName, i.StartMs, i.EndMs, i.MediaSegmentId, i.Role),
                })
                .ToListAsync(cancellationToken))
            .ToLookup(i => i.RecipeId, i => i.Component);
        return linked
            .Select(a => new AdComponents
            {
                AdId = a.AdId,
                AdName = a.Name,
                CampaignId = a.CampaignId,
                Metrics = totals.TryGetValue(a.AdId, out AdTotalsRow? row) ? MetaService.ToMetrics(row) : new AdMetrics(0, 0, 0, null, null, null, null, 0),
                Items = items[a.RecipeId!.Value].ToList(),
                Subtitles = finishes[a.RecipeId.Value].Subtitles,
                Transition = finishes[a.RecipeId.Value].Transition,
                Music = finishes[a.RecipeId.Value].Music,
            })
            .ToList();
    }

    /// <summary>Arma las agrupaciones de un conjunto de anuncios. Función pura.</summary>
    /// <param name="ads">Anuncios vinculados, con sus métricas y sus componentes.</param>
    /// <returns>Una agrupación por dimensión.</returns>
    public static List<ComponentDimension> Dimensions(IReadOnlyList<AdComponents> ads)
    {
        return
        [
            new ComponentDimension(
                "opening", "Fragmento de apertura", "El primer fragmento del video: lo que se ve en los primeros segundos.",
                PerformanceRules.Group(ads, a => a.Items.Take(1).Select(Fragment))),
            new ComponentDimension(
                "closing", "Fragmento de cierre", "El último fragmento, en los videos de más de un fragmento.",
                PerformanceRules.Group(ads, a => a.Items.Count > 1 ? a.Items.TakeLast(1).Select(Fragment) : [])),
            new ComponentDimension(
                "fragment", "Fragmentos", "Cada fragmento, esté donde esté dentro del video. Un anuncio cuenta en todos los que usa.",
                PerformanceRules.Group(ads, a => a.Items.Select(Fragment))),
            new ComponentDimension(
                "structure", "Estructura", "La secuencia de funciones de los fragmentos, en los videos que las tienen asignadas.",
                PerformanceRules.Group(ads, a => a.Items.Count > 0 && a.Items.All(i => i.Role is not null)
                    ? [new ComponentKey(string.Join(">", a.Items.Select(i => i.Role)), string.Join(" → ", a.Items.Select(i => i.Role)))]
                    : [])),
            new ComponentDimension(
                "subtitles", "Subtítulos", "Videos con y sin subtítulos quemados.",
                PerformanceRules.Group(ads, a => [new ComponentKey(a.Subtitles ? "yes" : "no", a.Subtitles ? "Con subtítulos" : "Sin subtítulos")])),
            new ComponentDimension(
                "music", "Música", "Videos con y sin música de fondo.",
                PerformanceRules.Group(ads, a => [new ComponentKey(a.Music ? "yes" : "no", a.Music ? "Con música" : "Sin música")])),
            new ComponentDimension(
                "transition", "Transición", "Cómo se pasa de un fragmento al siguiente.",
                PerformanceRules.Group(ads, a => [new ComponentKey(
                    a.Transition.ToString(), a.Transition == RecipeTransition.FadeBlack ? "Fundido a negro" : "Corte directo")])),
        ];
    }

    private static ComponentKey Fragment(ComponentRef item)
    {
        return new ComponentKey($"{item.AssetId}:{item.StartMs}-{item.EndMs}", item.FileName, item);
    }

    // Busca posible fatiga en cada anuncio con datos, esté vinculado o no: la fatiga es del anuncio.
    private async Task<List<FatigueItem>> FatigueAsync(PerformanceFilter filter, List<LinkedAdRow> ads, CancellationToken cancellationToken)
    {
        List<long> adIds = ads.Select(a => a.AdId).ToList();
        ILookup<long, DayPoint> days = (await db.MetaInsightsDaily.AsNoTracking()
                .Where(i => adIds.Contains(i.MetaAdId) && (filter.From == null || i.Date >= filter.From) && (filter.To == null || i.Date <= filter.To))
                .OrderBy(i => i.Date)
                .Select(i => new AdDayRow { AdId = i.MetaAdId, Point = new DayPoint(i.Date, i.Impressions, i.Clicks) })
                .ToListAsync(cancellationToken))
            .ToLookup(d => d.AdId, d => d.Point);
        List<FatigueItem> result = [];
        foreach (LinkedAdRow ad in ads)
        {
            FatigueSignal? signal = PerformanceRules.Fatigue(days[ad.AdId].ToList());
            if (signal is not null)
            {
                result.Add(new FatigueItem(ad.AdId, ad.Name, ad.CampaignName, signal));
            }
        }

        return result.OrderByDescending(f => f.Signal.Drop).ToList();
    }
}

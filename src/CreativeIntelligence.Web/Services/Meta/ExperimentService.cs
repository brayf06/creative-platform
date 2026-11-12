using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace CreativeIntelligence.Web.Services.Meta;

/// <summary>Reglas de los experimentos que no dependen de la base: cómo se evalúa el resultado y qué problemas de
/// diseño se avisan.</summary>
public static class ExperimentRules
{
    /// <summary>Impresiones mínimas por brazo que se proponen al crear un experimento.</summary>
    public const int DefaultMinImpressions = 5_000;

    /// <summary>Cantidad mínima de brazos.</summary>
    public const int MinArms = 2;

    /// <summary>Cantidad máxima de brazos.</summary>
    public const int MaxArms = 4;

    /// <summary>Un brazo con más de estas veces las impresiones de otro se avisa como reparto desparejo.</summary>
    public const decimal MaxImbalance = 2m;

    // Umbrales de |z| para "probable" (5 %) y "clara" (1 %) según cuántos brazos se comparan con el control. Con más
    // de una comparación el umbral sube (corrección de Bonferroni): si no, cuantos más brazos, más fácil ver una
    // diferencia que es azar.
    private static readonly (double Probable, double Clear)[] Thresholds = [(1.96, 2.58), (2.24, 2.81), (2.39, 2.94)];

    /// <summary>
    /// En qué difieren las creatividades de dos anuncios: el fragmento de apertura, el de cierre, los del medio,
    /// los subtítulos, la música y la transición. Sirve para comprobar que un experimento cambia una sola cosa.
    /// </summary>
    /// <param name="left">Una creatividad.</param>
    /// <param name="right">La otra.</param>
    /// <returns>Las variables en que difieren.</returns>
    public static List<ExperimentVariable> Differences(AdComponents left, AdComponents right)
    {
        List<ExperimentVariable> result = [];
        if (Key(left.Items.FirstOrDefault()) != Key(right.Items.FirstOrDefault()))
        {
            result.Add(ExperimentVariable.Opening);
        }

        if (Key(Last(left)) != Key(Last(right)))
        {
            result.Add(ExperimentVariable.Closing);
        }

        if (!Middle(left).SequenceEqual(Middle(right)))
        {
            result.Add(ExperimentVariable.Development);
        }

        if (left.Subtitles != right.Subtitles)
        {
            result.Add(ExperimentVariable.Subtitles);
        }

        if (left.Music != right.Music)
        {
            result.Add(ExperimentVariable.Music);
        }

        if (left.Transition != right.Transition)
        {
            result.Add(ExperimentVariable.Transition);
        }

        return result;
    }

    /// <summary>
    /// Evalúa un experimento: calcula la métrica de cada brazo, compara cada uno con el control y arma el veredicto,
    /// el resumen y los avisos. No informa diferencias si algún brazo no llegó al mínimo de impresiones. Función pura.
    /// </summary>
    /// <param name="metric">Con qué se mide.</param>
    /// <param name="variable">Qué se declaró cambiar.</param>
    /// <param name="arms">Brazos; el primero es el control.</param>
    /// <param name="minImpressions">Impresiones mínimas por brazo.</param>
    /// <param name="finished">Si el período ya terminó.</param>
    /// <returns>El resultado.</returns>
    public static ExperimentResult Evaluate(
        ExperimentMetric metric, ExperimentVariable variable, IReadOnlyList<ArmData> arms, int minImpressions, bool finished)
    {
        List<string> warnings = Warnings(variable, arms);
        ArmData control = arms[0];
        (long Hits, long Trials)? controlCounts = Counts(metric, control.Metrics);
        bool enough = arms.All(a => a.Metrics.Impressions >= minImpressions && Counts(metric, a.Metrics) is { Trials: > 0 });
        (double Probable, double Clear) threshold = Thresholds[Math.Clamp(arms.Count - 2, 0, Thresholds.Length - 1)];

        List<ArmResult> results = [];
        foreach (ArmData arm in arms)
        {
            (long Hits, long Trials)? counts = Counts(metric, arm.Metrics);
            decimal? rate = counts is { Trials: > 0 } ? (decimal)counts.Value.Hits / counts.Value.Trials : null;
            bool isControl = ReferenceEquals(arm, control);
            double? z = isControl || !enough || counts is null || controlCounts is null
                ? null
                : PerformanceRules.ZScore(counts.Value.Hits, counts.Value.Trials, controlCounts.Value.Hits, controlCounts.Value.Trials);
            SignalStrength signal = z is null ? SignalStrength.None
                : Math.Abs(z.Value) >= threshold.Clear ? SignalStrength.Clear
                : Math.Abs(z.Value) >= threshold.Probable ? SignalStrength.Probable
                : SignalStrength.NotDistinguishable;
            decimal? controlRate = controlCounts is { Trials: > 0, Hits: > 0 } ? (decimal)controlCounts.Value.Hits / controlCounts.Value.Trials : null;
            results.Add(new ArmResult(
                arm.Name, arm.AdId, arm.AdName, isControl, arm.Metrics, rate, signal, z is > 0,
                isControl || rate is null || controlRate is null ? null : (rate.Value - controlRate.Value) / controlRate.Value));
        }

        ExperimentVerdict verdict = !enough ? ExperimentVerdict.NotEnoughData
            : results.Any(r => r.Signal == SignalStrength.Clear) ? ExperimentVerdict.ClearDifference
            : results.Any(r => r.Signal == SignalStrength.Probable) ? ExperimentVerdict.ProbableDifference
            : ExperimentVerdict.NoDifference;
        // Una diferencia con problemas de diseño se informa, pero el resumen dice que puede deberse a ellos.
        string caution = warnings.Count > 0 && verdict is ExperimentVerdict.ClearDifference or ExperimentVerdict.ProbableDifference
            ? " Atención: el diseño tiene problemas (ver los avisos) y la diferencia puede deberse a ellos."
            : string.Empty;
        return new ExperimentResult(verdict, !finished, Summary(metric, verdict, results, minImpressions, !finished) + caution, warnings, results);
    }

    // Aciertos e intentos de la métrica; nulo si las métricas no traen el dato.
    private static (long Hits, long Trials)? Counts(ExperimentMetric metric, AdMetrics metrics)
    {
        return metric switch
        {
            ExperimentMetric.HookRate => metrics.VideoPlays is null ? null : (metrics.VideoPlays.Value, metrics.Impressions),
            ExperimentMetric.PurchaseRate => metrics.Purchases is null ? null : ((long)Math.Round(metrics.Purchases.Value), metrics.Clicks),
            _ => (metrics.Clicks, metrics.Impressions),
        };
    }

    private static List<string> Warnings(ExperimentVariable variable, IReadOnlyList<ArmData> arms)
    {
        List<string> warnings = [];
        if (arms.Select(a => a.CampaignId).Distinct().Count() > 1)
        {
            warnings.Add("Los brazos están en campañas distintas: su objetivo, su presupuesto o su audiencia pueden explicar la diferencia.");
        }

        long most = arms.Max(a => a.Metrics.Impressions);
        long least = arms.Min(a => a.Metrics.Impressions);
        if (least > 0 && most > least * MaxImbalance)
        {
            warnings.Add($"El reparto fue desparejo: un brazo tuvo más de {MaxImbalance:0} veces las impresiones de otro. Meta pudo haber favorecido a uno.");
        }

        if (variable == ExperimentVariable.Other)
        {
            warnings.Add("La variable es algo que el sistema no ve en las creatividades: no puede comprobar que sea lo único que cambia.");
            return warnings;
        }

        if (arms.Any(a => a.Components is null))
        {
            warnings.Add("Algún brazo no está vinculado a un video generado: no se puede comprobar en qué difieren las creatividades.");
            return warnings;
        }

        for (int i = 1; i < arms.Count; i++)
        {
            List<ExperimentVariable> differences = Differences(arms[0].Components!, arms[i].Components!);
            if (!differences.Contains(variable))
            {
                warnings.Add($"«{arms[i].Name}» no difiere del control en lo que el experimento dice probar.");
            }

            List<ExperimentVariable> extra = differences.Where(d => d != variable).ToList();
            if (extra.Count > 0)
            {
                warnings.Add(
                    $"«{arms[i].Name}» difiere del control en más de una cosa ({string.Join(", ", extra.Select(VariableName))}): "
                    + "no se puede saber cuál explica la diferencia.");
            }
        }

        return warnings;
    }

    private static string Summary(ExperimentMetric metric, ExperimentVerdict verdict, List<ArmResult> arms, int minImpressions, bool provisional)
    {
        string prefix = provisional ? "Resultado provisional, el período no terminó. " : string.Empty;
        ArmResult control = arms[0];
        if (verdict == ExperimentVerdict.NotEnoughData)
        {
            return prefix + $"Todavía no hay datos suficientes: cada brazo necesita al menos {minImpressions} impresiones y el dato de la métrica.";
        }

        string name = MetricName(metric);
        if (verdict == ExperimentVerdict.NoDifference)
        {
            return prefix + $"Con {name} como medida, ningún brazo se distingue del control «{control.Name}» ({Percent(control.Rate)}). "
                + "No haber visto una diferencia no prueba que no la haya: puede faltar volumen.";
        }

        ArmResult best = arms.Where(a => a.Signal is SignalStrength.Clear or SignalStrength.Probable)
            .OrderByDescending(a => a.Signal).ThenByDescending(a => Math.Abs(a.Lift ?? 0)).First();
        string strength = best.Signal == SignalStrength.Clear ? "clara" : "probable";
        return prefix + $"Con {name} como medida, «{best.Name}» tuvo {Percent(best.Rate)} frente a {Percent(control.Rate)} del control «{control.Name}»: "
            + $"una diferencia {strength} {(best.Higher ? "a favor" : "en contra")} ({(best.Lift ?? 0) * 100:+0;-0} %). "
            + "Vale para esta audiencia y este período; conviene repetirlo antes de generalizar.";
    }

    private static string MetricName(ExperimentMetric metric)
    {
        return metric switch
        {
            ExperimentMetric.HookRate => "reproducciones de 3 segundos sobre impresiones",
            ExperimentMetric.PurchaseRate => "compras sobre clics",
            _ => "CTR",
        };
    }

    private static string VariableName(ExperimentVariable variable)
    {
        return variable switch
        {
            ExperimentVariable.Opening => "la apertura",
            ExperimentVariable.Closing => "el cierre",
            ExperimentVariable.Development => "el desarrollo",
            ExperimentVariable.Subtitles => "los subtítulos",
            ExperimentVariable.Music => "la música",
            ExperimentVariable.Transition => "la transición",
            _ => "otra cosa",
        };
    }

    private static string Percent(decimal? value)
    {
        return value is null ? "sin dato" : (value.Value * 100).ToString("0.00", CultureInfo.InvariantCulture) + " %";
    }

    private static string Key(ComponentRef? item)
    {
        return item is null ? string.Empty : $"{item.AssetId}:{item.StartMs}-{item.EndMs}";
    }

    // El cierre sólo existe en un video de más de un fragmento.
    private static ComponentRef? Last(AdComponents ad)
    {
        return ad.Items.Count > 1 ? ad.Items[^1] : null;
    }

    private static List<string> Middle(AdComponents ad)
    {
        return ad.Items.Count > 2 ? ad.Items.Skip(1).SkipLast(1).Select(Key).ToList() : [];
    }
}

/// <summary>
/// Experimentos controlados de la organización activa. Un experimento se define antes de mirar resultados
/// (hipótesis, variable, métrica, período y mínimo de impresiones) y compara anuncios que deberían diferir en una
/// sola cosa. El resultado se calcula sobre las métricas importadas cada vez que se consulta. "Controlado" llega
/// hasta donde el sistema puede ver: comprueba campaña, reparto y diferencias entre creatividades, pero no asigna
/// las personas al azar; eso lo hace la herramienta de pruebas A/B de Meta.
/// </summary>
/// <param name="db">Contexto de datos.</param>
/// <param name="meta">Métricas importadas.</param>
/// <param name="performance">Componentes de las creatividades.</param>
/// <param name="audit">Auditoría.</param>
/// <param name="current">Usuario y organización que operan.</param>
public sealed class ExperimentService(AppDbContext db, MetaService meta, PerformanceService performance, AuditService audit, CurrentContext current)
{
    /// <summary>Lista los experimentos, del más nuevo al más viejo. No escribe.</summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Hasta 200 experimentos.</returns>
    public async Task<List<ExperimentListItem>> ListAsync(CancellationToken cancellationToken)
    {
        return await db.Experiments.AsNoTracking()
            .OrderByDescending(e => e.Id)
            .Take(200)
            .Select(e => new ExperimentListItem(e.Id, e.Name, e.Variable, e.Metric, e.StartDate, e.EndDate, e.Arms.Count, e.ConcludedAt != null))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Lista los anuncios que se pueden elegir como brazo. No escribe.</summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Hasta 500 anuncios, por campaña y nombre.</returns>
    public async Task<List<AdOption>> AdOptionsAsync(CancellationToken cancellationToken)
    {
        return await db.MetaAds.AsNoTracking()
            .OrderBy(a => a.AdSet!.Campaign!.Name)
            .ThenBy(a => a.Name)
            .Take(500)
            .Select(a => new AdOption(a.Id, a.AdSet!.Campaign!.Name + " · " + a.Name))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Arma el formulario de un experimento nuevo; si viene de una hipótesis, propone su enunciado y su
    /// variable. No escribe.</summary>
    /// <param name="hypothesisId">Hipótesis que se va a probar; nulo si el experimento es suelto.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Los anuncios para elegir y lo propuesto.</returns>
    /// <exception cref="BusinessException">404 <c>HYPOTHESIS_NOT_FOUND</c> si la hipótesis no existe en la organización activa.</exception>
    public async Task<ExperimentForm> FormAsync(long? hypothesisId, CancellationToken cancellationToken)
    {
        List<AdOption> ads = await AdOptionsAsync(cancellationToken);
        if (hypothesisId is null)
        {
            return new ExperimentForm(ads, null, null, ExperimentVariable.Opening);
        }

        return await db.Hypotheses.AsNoTracking()
            .Where(h => h.Id == hypothesisId)
            .Select(h => new ExperimentForm(ads, h.Id, h.Statement, h.Variable))
            .FirstOrDefaultAsync(cancellationToken) ?? throw new BusinessException(404, "La hipótesis no existe.", "HYPOTHESIS_NOT_FOUND");
    }

    /// <summary>Crea un experimento. Escribe el experimento, sus brazos y la auditoría.</summary>
    /// <param name="body">Hipótesis, variable, métrica, período, mínimo y brazos.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Identificador del experimento.</returns>
    /// <exception cref="BusinessException">400 <c>EXPERIMENT_INVALID</c> si no tiene de 2 a 4 brazos, repite un
    /// anuncio o termina antes de empezar; 404 <c>AD_NOT_FOUND</c> o <c>HYPOTHESIS_NOT_FOUND</c> si un anuncio o la
    /// hipótesis no existen en la organización activa.</exception>
    public async Task<long> CreateAsync(CreateExperimentBody body, CancellationToken cancellationToken)
    {
        List<ExperimentArmBody> arms = body.Arms.Where(a => a.AdId is not null).ToList();
        if (arms.Count is < ExperimentRules.MinArms or > ExperimentRules.MaxArms)
        {
            throw new BusinessException(
                400, $"Un experimento lleva de {ExperimentRules.MinArms} a {ExperimentRules.MaxArms} brazos, cada uno con su anuncio.", "EXPERIMENT_INVALID");
        }

        if (arms.Select(a => a.AdId).Distinct().Count() != arms.Count)
        {
            throw new BusinessException(400, "Un anuncio no puede estar en dos brazos del mismo experimento.", "EXPERIMENT_INVALID");
        }

        if (body.EndDate is not null && body.EndDate < body.StartDate)
        {
            throw new BusinessException(400, "El experimento no puede terminar antes de empezar.", "EXPERIMENT_INVALID");
        }

        List<long> adIds = arms.Select(a => a.AdId!.Value).ToList();
        Dictionary<long, string> names = await db.MetaAds.AsNoTracking()
            .Where(a => adIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.Name, cancellationToken);
        if (names.Count != adIds.Count)
        {
            throw new BusinessException(404, "Alguno de los anuncios elegidos no existe.", "AD_NOT_FOUND");
        }

        if (body.HypothesisId is not null && !await db.Hypotheses.AnyAsync(h => h.Id == body.HypothesisId, cancellationToken))
        {
            throw new BusinessException(404, "La hipótesis no existe.", "HYPOTHESIS_NOT_FOUND");
        }

        Experiment experiment = new()
        {
            Name = body.Name.Trim(),
            Hypothesis = body.Hypothesis.Trim(),
            Variable = body.Variable,
            Metric = body.Metric,
            StartDate = body.StartDate!.Value,
            EndDate = body.EndDate,
            MinImpressionsPerArm = body.MinImpressionsPerArm,
            HypothesisId = body.HypothesisId,
            CreatedByUserId = current.UserId,
            Arms = arms
                .Select((arm, index) => new ExperimentArm
                {
                    Position = index + 1,
                    MetaAdId = arm.AdId!.Value,
                    Name = Limit(string.IsNullOrWhiteSpace(arm.Name) ? names[arm.AdId.Value] : arm.Name, 100),
                })
                .ToList(),
        };
        return await db.InTransactionAsync(
            async () =>
            {
                db.Experiments.Add(experiment);
                await db.SaveChangesAsync(cancellationToken);
                audit.Record(
                    "experiment.create", nameof(Experiment), experiment.Id.ToString(),
                    $"Creó el experimento \"{experiment.Name}\" con {experiment.Arms.Count} brazos.");
                await db.SaveChangesAsync(cancellationToken);
                return experiment.Id;
            },
            cancellationToken);
    }

    /// <summary>Devuelve un experimento con su resultado calculado sobre las métricas de su período. No escribe.</summary>
    /// <param name="id">Experimento.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>El experimento.</returns>
    /// <exception cref="BusinessException">404 <c>EXPERIMENT_NOT_FOUND</c> si no existe en la organización activa.</exception>
    public async Task<ExperimentDetail> GetAsync(long id, CancellationToken cancellationToken)
    {
        Experiment experiment = await db.Experiments.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, cancellationToken) ?? throw NotFound();
        List<ArmRow> arms = await db.ExperimentArms.AsNoTracking()
            .Where(a => a.ExperimentId == id)
            .OrderBy(a => a.Position)
            .Join(
                db.MetaAds,
                a => a.MetaAdId,
                ad => ad.Id,
                (a, ad) => new ArmRow { Name = a.Name, AdId = ad.Id, AdName = ad.Name, CampaignId = ad.AdSet!.MetaCampaignId })
            .ToListAsync(cancellationToken);
        MetaFilter period = new() { From = experiment.StartDate, To = experiment.EndDate };
        Dictionary<long, AdTotalsRow> totals = (await meta.TotalsAsync(period, null, cancellationToken)).ToDictionary(t => t.AdId);
        Dictionary<long, AdComponents> components = (await performance.ComponentsOfAsync(arms.Select(a => a.AdId).ToList(), cancellationToken))
            .ToDictionary(c => c.AdId);
        DateOnly today = await db.Experiments.AsNoTracking()
            .Where(e => e.Id == id)
            .Select(e => DateOnly.FromDateTime(AppDbContext.UtcNow()))
            .FirstAsync(cancellationToken);

        List<ArmData> data = arms
            .Select(a => new ArmData(
                a.Name, a.AdId, a.AdName, a.CampaignId,
                totals.TryGetValue(a.AdId, out AdTotalsRow? row) ? MetaService.ToMetrics(row) : new AdMetrics(0, 0, 0, null, null, null, null, 0),
                components.GetValueOrDefault(a.AdId)))
            .ToList();
        return new ExperimentDetail(
            experiment.Id, experiment.Name, experiment.Hypothesis, experiment.Variable, experiment.Metric, experiment.StartDate, experiment.EndDate,
            experiment.MinImpressionsPerArm, experiment.Conclusion, experiment.ConcludedAt,
            ExperimentRules.Evaluate(
                experiment.Metric, experiment.Variable, data, experiment.MinImpressionsPerArm,
                experiment.EndDate is not null && experiment.EndDate < today),
            experiment.HypothesisId);
    }

    /// <summary>Cierra un experimento con la conclusión de una persona. Si no tenía fecha de fin, queda la de hoy.
    /// Escribe el experimento y la auditoría.</summary>
    /// <param name="id">Experimento.</param>
    /// <param name="conclusion">Qué se concluye y qué se decide.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <exception cref="BusinessException">404 <c>EXPERIMENT_NOT_FOUND</c>.</exception>
    public async Task ConcludeAsync(long id, string conclusion, CancellationToken cancellationToken)
    {
        if (!await db.Experiments.AnyAsync(e => e.Id == id, cancellationToken))
        {
            throw NotFound();
        }

        string text = conclusion.Trim();
        await db.InTransactionAsync(
            async () =>
            {
                await db.Experiments.Where(e => e.Id == id).ExecuteUpdateAsync(
                    s => s.SetProperty(e => e.Conclusion, text)
                        .SetProperty(e => e.ConcludedAt, e => (DateTime?)AppDbContext.UtcNow())
                        .SetProperty(e => e.EndDate, e => e.EndDate ?? DateOnly.FromDateTime(AppDbContext.UtcNow())),
                    cancellationToken);
                audit.Record("experiment.conclude", nameof(Experiment), id.ToString(), "Cerró el experimento con su conclusión.");
                await db.SaveChangesAsync(cancellationToken);
                return true;
            },
            cancellationToken);
    }

    private static string Limit(string value, int max)
    {
        string text = value.Trim();
        return text.Length > max ? text[..max] : text;
    }

    private static BusinessException NotFound()
    {
        return new BusinessException(404, "El experimento no existe.", "EXPERIMENT_NOT_FOUND");
    }
}

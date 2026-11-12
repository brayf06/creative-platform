using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace CreativeIntelligence.Web.Services.Automation;

/// <summary>
/// Reglas de detección de anomalías que no dependen de la base. Miran el último día con datos de un anuncio contra
/// sus días anteriores. Lo que encuentran es una observación para que una persona la revise: un gasto que salta o
/// un CTR que se cae también pueden ser un cambio hecho a propósito. Los umbrales son puntos de partida, sin
/// calibrar con datos reales.
/// </summary>
public static class AnomalyRules
{
    /// <summary>Días anteriores que hacen falta para comparar.</summary>
    public const int MinHistoryDays = 3;

    /// <summary>Veces el gasto promedio anterior a partir de las cuales el gasto del día es un salto.</summary>
    public const decimal SpendSpikeFactor = 3m;

    /// <summary>Gasto mínimo del día para mirar un salto: por debajo, cualquier variación es ruido.</summary>
    public const decimal MinSpend = 5m;

    /// <summary>Caída relativa del CTR a partir de la cual se señala.</summary>
    public const decimal CtrDropMin = 0.5m;

    /// <summary>Impresiones que necesitan el día y el período anterior para comparar el CTR.</summary>
    public const long MinImpressions = 1_000;

    /// <summary>Busca anomalías en el último día de un anuncio. Función pura.</summary>
    /// <param name="days">Días del anuncio, en orden.</param>
    /// <returns>Lo encontrado; vacío si no hay nada o no hay con qué comparar.</returns>
    public static List<AnomalyFinding> Detect(IReadOnlyList<AnomalyDay> days)
    {
        List<AnomalyFinding> findings = [];
        if (days.Count == 0)
        {
            return findings;
        }

        AnomalyDay last = days[^1];
        string date = last.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (last.Spend > 0 && last.Impressions == 0)
        {
            findings.Add(new AnomalyFinding(
                AnomalyKind.SpendWithoutImpressions, last.Date, $"El {date} gastó {Money(last.Spend)} sin ninguna impresión. Puede ser un error del informe."));
        }

        List<AnomalyDay> previous = days.Take(days.Count - 1).ToList();
        if (previous.Count < MinHistoryDays)
        {
            return findings;
        }

        decimal average = previous.Average(d => d.Spend);
        if (average > 0 && last.Spend >= MinSpend && last.Spend >= average * SpendSpikeFactor)
        {
            findings.Add(new AnomalyFinding(
                AnomalyKind.SpendSpike, last.Date,
                $"El {date} gastó {Money(last.Spend)}, {(last.Spend / average).ToString("0.0", CultureInfo.InvariantCulture)} veces el promedio de los "
                + $"{previous.Count} días anteriores ({Money(average)}). Puede ser un cambio de presupuesto hecho a propósito."));
        }

        long impressions = previous.Sum(d => d.Impressions);
        long clicks = previous.Sum(d => d.Clicks);
        if (impressions >= MinImpressions && last.Impressions >= MinImpressions && clicks > 0)
        {
            decimal before = (decimal)clicks / impressions;
            decimal now = (decimal)last.Clicks / last.Impressions;
            decimal drop = (before - now) / before;
            if (drop >= CtrDropMin && PerformanceRules.ZScore(last.Clicks, last.Impressions, clicks, impressions) is <= -2.58)
            {
                findings.Add(new AnomalyFinding(
                    AnomalyKind.CtrDrop, last.Date,
                    $"El {date} el CTR fue {Percent(now)}, una caída del {(drop * 100).ToString("0", CultureInfo.InvariantCulture)} % frente al {Percent(before)} "
                    + $"de los {previous.Count} días anteriores. Puede deberse a un cambio de audiencia o de ubicación."));
            }
        }

        return findings;
    }

    private static string Money(decimal value)
    {
        return value.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private static string Percent(decimal value)
    {
        return (value * 100).ToString("0.00", CultureInfo.InvariantCulture) + " %";
    }
}

/// <summary>
/// Anomalías de la organización activa. Se buscan al terminar cada importación de métricas y a pedido. Cada una
/// queda abierta hasta que una persona la revisa; mientras haya alguna abierta, el modo automático no ejecuta
/// nada solo.
/// </summary>
/// <param name="db">Contexto de datos.</param>
/// <param name="audit">Auditoría.</param>
/// <param name="current">Usuario y organización que operan.</param>
public sealed class AnomalyService(AppDbContext db, AuditService audit, CurrentContext current)
{
    /// <summary>
    /// Busca anomalías en el último día con datos de cada anuncio y registra las que no estaban. Volver a buscar no
    /// duplica. Escribe las anomalías nuevas.
    /// </summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Cantidad de anomalías nuevas.</returns>
    public async Task<int> ScanAsync(CancellationToken cancellationToken)
    {
        ILookup<long, AnomalyDay> days = (await db.MetaInsightsDaily.AsNoTracking()
                .OrderBy(i => i.Date)
                .Select(i => new AnomalyDayRow { AdId = i.MetaAdId, Day = new AnomalyDay(i.Date, i.Spend, i.Impressions, i.Clicks) })
                .ToListAsync(cancellationToken))
            .ToLookup(r => r.AdId, r => r.Day);
        List<AnomalyEvent> known = await db.AnomalyEvents.AsNoTracking().ToListAsync(cancellationToken);
        HashSet<string> keys = known.Select(e => Key(e.MetaAdId, e.Kind, e.Date)).ToHashSet();
        List<AnomalyEvent> found = [];
        foreach (IGrouping<long, AnomalyDay> ad in days)
        {
            foreach (AnomalyFinding finding in AnomalyRules.Detect(ad.ToList()))
            {
                if (keys.Add(Key(ad.Key, finding.Kind, finding.Date)))
                {
                    found.Add(new AnomalyEvent { MetaAdId = ad.Key, Kind = finding.Kind, Date = finding.Date, Description = finding.Description });
                }
            }
        }

        if (found.Count > 0)
        {
            db.AnomalyEvents.AddRange(found);
            await db.SaveChangesAsync(cancellationToken);
        }

        return found.Count;
    }

    /// <summary>Lista las anomalías, las abiertas primero y después las más nuevas. No escribe.</summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Hasta 100 anomalías.</returns>
    public async Task<List<AnomalyItem>> ListAsync(CancellationToken cancellationToken)
    {
        return await db.AnomalyEvents.AsNoTracking()
            .OrderBy(e => e.ReviewedAt != null)
            .ThenByDescending(e => e.Id)
            .Take(100)
            .Select(e => new AnomalyItem(
                e.Id, e.MetaAdId, db.MetaAds.Where(a => a.Id == e.MetaAdId).Select(a => a.Name).First(), e.Kind, e.Date, e.Description,
                e.ReviewedAt != null, e.ReviewNote))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Cuenta las anomalías que nadie revisó. No escribe.</summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>La cantidad.</returns>
    public async Task<int> OpenCountAsync(CancellationToken cancellationToken)
    {
        return await db.AnomalyEvents.CountAsync(e => e.ReviewedAt == null, cancellationToken);
    }

    /// <summary>Da por revisada una anomalía, con lo que la persona concluyó. Escribe la anomalía y la auditoría.</summary>
    /// <param name="id">Anomalía.</param>
    /// <param name="note">Qué concluyó.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <exception cref="BusinessException">404 <c>ANOMALY_NOT_FOUND</c> si no existe en la organización activa o ya estaba revisada.</exception>
    public async Task ReviewAsync(long id, string note, CancellationToken cancellationToken)
    {
        string text = note.Trim();
        await db.InTransactionAsync(
            async () =>
            {
                int rows = await db.AnomalyEvents.Where(e => e.Id == id && e.ReviewedAt == null).ExecuteUpdateAsync(
                    s => s.SetProperty(e => e.ReviewedAt, e => (DateTime?)AppDbContext.UtcNow())
                        .SetProperty(e => e.ReviewedByUserId, current.UserId)
                        .SetProperty(e => e.ReviewNote, text),
                    cancellationToken);
                if (rows == 0)
                {
                    throw new BusinessException(404, "La anomalía no existe o ya estaba revisada.", "ANOMALY_NOT_FOUND");
                }

                audit.Record("anomaly.review", nameof(AnomalyEvent), id.ToString(), $"Revisó la anomalía: {text}");
                await db.SaveChangesAsync(cancellationToken);
                return true;
            },
            cancellationToken);
    }

    private static string Key(long adId, AnomalyKind kind, DateOnly date)
    {
        return $"{adId}|{kind}|{date:yyyy-MM-dd}";
    }
}

using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CreativeIntelligence.Web.Services.Ai;

/// <summary>Registro y consulta del uso de IA de la organización activa.</summary>
/// <param name="db">Contexto de datos.</param>
/// <param name="options">Presupuesto mensual.</param>
public sealed class AiUsageService(AppDbContext db, IOptions<AiOptions> options)
{
    /// <summary>Proveedor con el que se registran los modelos que corren en el servidor.</summary>
    public const string LocalProvider = "local";

    /// <summary>Guarda una llamada a un proveedor de IA en la organización activa. Escribe un registro.</summary>
    /// <param name="entry">Datos de la llamada.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Identificador del registro.</returns>
    public async Task<long> RecordAsync(AiUsageEntry entry, CancellationToken cancellationToken)
    {
        AiUsageRecord record = new()
        {
            Task = entry.Task,
            Provider = entry.Provider,
            Model = entry.Model,
            PromptVersion = entry.PromptVersion,
            InputTokens = entry.InputTokens,
            OutputTokens = entry.OutputTokens,
            CostUsd = entry.CostUsd,
            DurationMs = entry.DurationMs,
            BackgroundJobId = entry.BackgroundJobId,
        };
        db.AiUsageRecords.Add(record);
        await db.SaveChangesAsync(cancellationToken);
        return record.Id;
    }

    /// <summary>Costo de IA de la organización activa en el mes calendario en curso, según la fecha de la base.</summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Dólares gastados en el mes.</returns>
    public async Task<decimal> MonthCostAsync(CancellationToken cancellationToken)
    {
        return await db.AiUsageRecords.AsNoTracking()
            .Where(u => u.CreatedAt.Year == AppDbContext.UtcNow().Year && u.CreatedAt.Month == AppDbContext.UtcNow().Month)
            .SumAsync(u => u.CostUsd, cancellationToken);
    }

    /// <summary>Lo que se puede gastar en una tanda de análisis: el menor entre el tope por lote y lo que queda del
    /// presupuesto mensual. A diferencia de la pantalla de costos, acá el presupuesto mensual sí limita.</summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Dólares disponibles para la tanda; 0 si el presupuesto del mes está agotado.</returns>
    public async Task<decimal> BatchLimitAsync(CancellationToken cancellationToken)
    {
        AiOptions settings = options.Value;
        if (settings.MonthlyBudgetUsd <= 0)
        {
            return settings.MaxCostPerBatchUsd;
        }

        decimal remaining = Math.Max(0m, settings.MonthlyBudgetUsd - await MonthCostAsync(cancellationToken));
        return Math.Min(settings.MaxCostPerBatchUsd, remaining);
    }

    /// <summary>
    /// Resume el uso de IA de la organización activa en los últimos días: totales, desglose por tarea y modelo,
    /// las llamadas más recientes y el gasto del mes contra el presupuesto. Las fechas se comparan con la hora de
    /// la base. No escribe.
    /// </summary>
    /// <param name="filter">Período.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>El resumen.</returns>
    public async Task<AiCostSummary> SummaryAsync(AiCostFilter filter, CancellationToken cancellationToken)
    {
        int days = filter.Days;
        IQueryable<AiUsageRecord> period = db.AiUsageRecords.AsNoTracking()
            .Where(u => u.CreatedAt >= AppDbContext.UtcNow().AddDays(-days));

        // La clave del grupo es un solo texto: agrupar por varias columnas pediría un tipo anónimo.
        List<AiCostGroupRow> rows = await period
            .GroupBy(u => u.Task + "|" + u.Provider + "|" + u.Model)
            .Select(g => new AiCostGroupRow
            {
                Key = g.Key,
                Calls = g.Count(),
                InputTokens = g.Sum(u => (long)u.InputTokens),
                OutputTokens = g.Sum(u => (long)u.OutputTokens),
                CostUsd = g.Sum(u => u.CostUsd),
                DurationMs = g.Sum(u => (long)u.DurationMs),
            })
            .ToListAsync(cancellationToken);
        List<AiCostGroup> groups = rows
            .Select(r =>
            {
                string[] parts = r.Key.Split('|');
                return new AiCostGroup(parts[0], parts[1], parts[2], r.Calls, r.InputTokens, r.OutputTokens, r.CostUsd, r.DurationMs);
            })
            .OrderByDescending(g => g.CostUsd)
            .ThenByDescending(g => g.DurationMs)
            .ToList();

        List<AiUsageItem> recent = await period
            .OrderByDescending(u => u.Id)
            .Take(50)
            .Select(u => new AiUsageItem(
                u.Id, u.CreatedAt, u.Task, u.Provider, u.Model, u.PromptVersion, u.InputTokens, u.OutputTokens, u.CostUsd,
                u.DurationMs, u.BackgroundJobId))
            .ToListAsync(cancellationToken);

        decimal monthCost = await MonthCostAsync(cancellationToken);

        return new AiCostSummary(
            days,
            groups.Sum(g => g.Calls),
            groups.Sum(g => g.InputTokens),
            groups.Sum(g => g.OutputTokens),
            groups.Sum(g => g.CostUsd),
            groups.Where(g => g.Provider == LocalProvider).Sum(g => g.DurationMs),
            monthCost,
            options.Value.MonthlyBudgetUsd,
            groups,
            recent);
    }
}

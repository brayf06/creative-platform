using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CreativeIntelligence.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class AiCostTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task El_resumen_suma_por_tarea_y_modelo_separa_el_computo_local_y_respeta_el_periodo()
    {
        int org = await fixture.NewOrganizationAsync();
        int other = await fixture.NewOrganizationAsync();
        await using AsyncServiceScope scope = fixture.Scope(org);
        AiUsageService usage = scope.ServiceProvider.GetRequiredService<AiUsageService>();
        await usage.RecordAsync(new AiUsageEntry("scene.analysis", "gemini", "gemini-3.5-flash-lite", "scene-analysis-v1", 2000, 800, 0.0026m, 4000, null), CancellationToken.None);
        await usage.RecordAsync(new AiUsageEntry("scene.analysis", "gemini", "gemini-3.5-flash-lite", "scene-analysis-v1", 1000, 400, 0.0013m, 3000, null), CancellationToken.None);
        await usage.RecordAsync(new AiUsageEntry("media.transcription", AiUsageService.LocalProvider, "faster-whisper:small", "n/a", 0, 0, 0m, 9000, null), CancellationToken.None);
        long old = await usage.RecordAsync(new AiUsageEntry("segment.embedding", "gemini", "gemini-embedding-2", "n/a", 500, 0, 0.0001m, 200, null), CancellationToken.None);

        // Una llamada de hace 40 días: fuera del período de 30 y del mes en curso, dentro del de 90.
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.AiUsageRecords.Where(u => u.Id == old).ExecuteUpdateAsync(s => s.SetProperty(u => u.CreatedAt, u => AppDbContext.UtcNow().AddDays(-40)));

        AiCostSummary month = await usage.SummaryAsync(new AiCostFilter(), CancellationToken.None);
        Assert.Equal(30, month.Days);
        Assert.Equal(3, month.Calls);
        Assert.Equal(3000, month.InputTokens);
        Assert.Equal(1200, month.OutputTokens);
        Assert.Equal(0.0039m, month.CostUsd);
        Assert.Equal(9000, month.LocalComputeMs);
        Assert.Equal(0.0039m, month.MonthCostUsd);
        Assert.Equal(20m, month.MonthlyBudgetUsd);
        Assert.Equal(0.000195m, month.BudgetUsed);
        Assert.Equal(["scene.analysis", "media.transcription"], month.Groups.Select(g => g.Task));
        AiCostGroup analysis = month.Groups[0];
        Assert.Equal(("gemini", "gemini-3.5-flash-lite", 2, 7000L), (analysis.Provider, analysis.Model, analysis.Calls, analysis.DurationMs));
        Assert.Equal(3, month.Recent.Count);
        Assert.Equal("media.transcription", month.Recent[0].Task);

        AiCostSummary quarter = await usage.SummaryAsync(new AiCostFilter { Days = 90 }, CancellationToken.None);
        Assert.Equal(4, quarter.Calls);
        Assert.Equal(0.004m, quarter.CostUsd);
        Assert.Equal(0.0039m, quarter.MonthCostUsd);
        Assert.Equal(3, quarter.Groups.Count);

        await using AsyncServiceScope otherScope = fixture.Scope(other);
        AiCostSummary empty = await otherScope.ServiceProvider.GetRequiredService<AiUsageService>().SummaryAsync(new AiCostFilter(), CancellationToken.None);
        Assert.Equal(0, empty.Calls);
        Assert.Equal(0m, empty.MonthCostUsd);
        Assert.Empty(empty.Groups);
        Assert.Empty(empty.Recent);
    }
}
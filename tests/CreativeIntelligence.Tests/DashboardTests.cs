using Microsoft.Extensions.DependencyInjection;

namespace CreativeIntelligence.Tests;

/// <summary>Pantalla de inicio y listado de videos generados.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class DashboardTests(DatabaseFixture fixture)
{
    [Fact]
    public void El_paso_siguiente_es_el_primer_eslabon_vacio_y_lo_urgente_va_primero()
    {
        DashboardOverview empty = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        Assert.Equal(new NextStep("Cargá los primeros videos en la biblioteca.", "Media", "Index", false), DashboardRules.Next(empty));
        Assert.Equal(("Media", "Index"), Where(empty with { MediaFiles = 3 }));
        Assert.Equal(("Recipes", "Index"), Where(empty with { MediaFiles = 3, ProductionSegments = 2 }));
        // Una receta sin video listo todavía no alcanza.
        Assert.Equal(("Recipes", "Index"), Where(empty with { MediaFiles = 3, ProductionSegments = 2, Recipes = 1 }));
        DashboardOverview produced = empty with { MediaFiles = 3, ProductionSegments = 2, Recipes = 1, ReadyVideos = 1 };
        Assert.Equal(("Meta", "Index"), Where(produced));
        Assert.Equal("Vinculá cada anuncio con el video que publicó: sin eso no se puede analizar.", DashboardRules.Next(produced with { Ads = 4 }).Text);
        Assert.Equal(("Meta", "Analysis"), Where(produced with { Ads = 4, LinkedAds = 2 }));
        Assert.Equal(("Hypotheses", "Index"), Where(produced with { Ads = 4, LinkedAds = 2, Hypotheses = 1 }));
        DashboardOverview complete = produced with { Ads = 4, LinkedAds = 2, Hypotheses = 1, ActiveLearnings = 1 };
        Assert.Equal(("ProductionPlans", "Index"), Where(complete));
        // La estrategia (marcas y briefs) es opcional en el recorrido: no frena el paso siguiente.
        Assert.False(DashboardRules.Next(complete).Urgent);

        // Lo que espera a una persona va antes que cualquier paso del recorrido, y las anomalías antes que las aprobaciones.
        Assert.Equal(new NextStep("Hay 2 acción(es) esperando aprobación.", "Automation", "Index", true), DashboardRules.Next(empty with { PendingApprovals = 2 }));
        Assert.Equal(
            new NextStep("Hay 1 anomalía(s) en las métricas sin revisar.", "Automation", "Index", true),
            DashboardRules.Next(complete with { PendingApprovals = 2, OpenAnomalies = 1 }));
    }

    [Fact]
    public async Task El_inicio_cuenta_lo_de_la_organizacion_activa_y_los_videos_generados_se_listan()
    {
        int org = await fixture.NewOrganizationAsync();
        await using AsyncServiceScope scope = fixture.Scope(org);
        DashboardService dashboard = scope.ServiceProvider.GetRequiredService<DashboardService>();
        CompositionService composition = scope.ServiceProvider.GetRequiredService<CompositionService>();

        Assert.Equal(new DashboardOverview(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0), await dashboard.OverviewAsync(CancellationToken.None));
        Assert.Empty(await composition.VideosAsync(CancellationToken.None));

        long hypothesis = await scope.ServiceProvider.GetRequiredService<HypothesisService>()
            .CreateAsync(new CreateHypothesisBody { Statement = "Una", Variable = ExperimentVariable.Other }, CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<BrandService>().CreateAsync(new BrandBody { Name = "Marca" }, CancellationToken.None);
        DashboardOverview overview = await dashboard.OverviewAsync(CancellationToken.None);
        Assert.Equal((1, 1, 0), (overview.Hypotheses, overview.Brands, overview.ActiveLearnings));

        // Una hipótesis descartada deja de contar.
        await scope.ServiceProvider.GetRequiredService<HypothesisService>().DiscardAsync(hypothesis, "No va", CancellationToken.None);
        Assert.Equal(0, (await dashboard.OverviewAsync(CancellationToken.None)).Hypotheses);
    }

    private static (string Controller, string Action) Where(DashboardOverview overview)
    {
        NextStep next = DashboardRules.Next(overview);
        return (next.Controller, next.Action);
    }
}

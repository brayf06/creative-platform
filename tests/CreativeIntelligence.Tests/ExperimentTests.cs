using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CreativeIntelligence.Tests;

/// <summary>Experimentos controlados: evaluación del resultado, avisos de diseño y flujo completo.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class ExperimentTests(DatabaseFixture fixture)
{
    private static readonly ComponentRef A = new(1, "uno.mp4", 0, 2000, 11, null);
    private static readonly ComponentRef B = new(1, "uno.mp4", 2000, 4000, 12, null);
    private static readonly ComponentRef C = new(2, "dos.mp4", 0, 3000, 21, null);
    private static readonly ComponentRef D = new(2, "dos.mp4", 3000, 5000, 22, null);

    [Fact]
    public void Las_diferencias_entre_dos_creatividades_se_detectan_por_apertura_cierre_desarrollo_y_terminacion()
    {
        Assert.Empty(ExperimentRules.Differences(Creative([A, B, C]), Creative([A, B, C])));
        Assert.Equal([ExperimentVariable.Opening], ExperimentRules.Differences(Creative([A, B, C]), Creative([D, B, C])));
        Assert.Equal([ExperimentVariable.Closing], ExperimentRules.Differences(Creative([A, B, C]), Creative([A, B, D])));
        Assert.Equal([ExperimentVariable.Development], ExperimentRules.Differences(Creative([A, B, C]), Creative([A, D, C])));
        // Un video de un solo fragmento no tiene cierre: contra uno de dos, difiere en el cierre.
        Assert.Equal([ExperimentVariable.Closing], ExperimentRules.Differences(Creative([A]), Creative([A, B])));
        Assert.Equal(
            [ExperimentVariable.Subtitles, ExperimentVariable.Music, ExperimentVariable.Transition],
            ExperimentRules.Differences(Creative([A, B]), Creative([A, B], subtitles: true, music: true, transition: RecipeTransition.FadeBlack)));
    }

    [Fact]
    public void Un_experimento_bien_armado_informa_la_diferencia_con_el_control()
    {
        ExperimentResult result = ExperimentRules.Evaluate(
            ExperimentMetric.Ctr, ExperimentVariable.Opening,
            [Arm("Control", 1, 10_000, 100, Creative([A, B])), Arm("Nueva apertura", 2, 10_000, 150, Creative([C, B]))],
            5_000, finished: true);

        Assert.Equal((ExperimentVerdict.ClearDifference, false), (result.Verdict, result.Provisional));
        Assert.Empty(result.Warnings);
        Assert.Equal((true, 0.01m, SignalStrength.None, null), (result.Arms[0].IsControl, result.Arms[0].Rate, result.Arms[0].Signal, result.Arms[0].Lift));
        Assert.Equal((false, 0.015m, SignalStrength.Clear, true, 0.5m), (result.Arms[1].IsControl, result.Arms[1].Rate, result.Arms[1].Signal, result.Arms[1].Higher, result.Arms[1].Lift));
        Assert.Equal(
            "Con CTR como medida, «Nueva apertura» tuvo 1.50 % frente a 1.00 % del control «Control»: una diferencia clara a favor (+50 %). "
            + "Vale para esta audiencia y este período; conviene repetirlo antes de generalizar.",
            result.Summary);
    }

    [Fact]
    public void Sin_el_minimo_de_impresiones_o_sin_el_dato_no_se_informa_ninguna_diferencia()
    {
        ExperimentResult few = ExperimentRules.Evaluate(
            ExperimentMetric.Ctr, ExperimentVariable.Opening,
            [Arm("Control", 1, 10_000, 100, Creative([A, B])), Arm("Variante", 2, 4_999, 150, Creative([C, B]))], 5_000, finished: false);

        Assert.Equal((ExperimentVerdict.NotEnoughData, true), (few.Verdict, few.Provisional));
        Assert.All(few.Arms, a => Assert.Equal(SignalStrength.None, a.Signal));
        Assert.StartsWith("Resultado provisional, el período no terminó. Todavía no hay datos suficientes: cada brazo necesita al menos 5000 impresiones", few.Summary);

        // La métrica es reproducciones de 3 segundos y el informe no las trae.
        ExperimentResult noData = ExperimentRules.Evaluate(
            ExperimentMetric.HookRate, ExperimentVariable.Opening,
            [Arm("Control", 1, 10_000, 100, Creative([A, B])), Arm("Variante", 2, 10_000, 150, Creative([C, B]))], 5_000, finished: true);
        Assert.Equal(ExperimentVerdict.NotEnoughData, noData.Verdict);
        Assert.All(noData.Arms, a => Assert.Null(a.Rate));
    }

    [Fact]
    public void Con_mas_brazos_hace_falta_una_diferencia_mayor_para_informarla()
    {
        ArmData control = Arm("Control", 1, 10_000, 100, Creative([A, B]));
        ArmData variant = Arm("Variante", 2, 10_000, 132, Creative([C, B]));

        // 1,32 % contra 1,00 %: con dos brazos es una diferencia probable.
        ExperimentResult two = ExperimentRules.Evaluate(ExperimentMetric.Ctr, ExperimentVariable.Opening, [control, variant], 5_000, finished: true);
        Assert.Equal((ExperimentVerdict.ProbableDifference, SignalStrength.Probable), (two.Verdict, two.Arms[1].Signal));

        // La misma diferencia con un tercer brazo ya no alcanza: se hacen dos comparaciones y el umbral sube.
        ExperimentResult three = ExperimentRules.Evaluate(
            ExperimentMetric.Ctr, ExperimentVariable.Opening, [control, variant, Arm("Otra", 3, 10_000, 101, Creative([D, B]))], 5_000, finished: true);
        Assert.Equal((ExperimentVerdict.NoDifference, SignalStrength.NotDistinguishable), (three.Verdict, three.Arms[1].Signal));
        Assert.Contains("ningún brazo se distingue del control «Control» (1.00 %)", three.Summary);
        Assert.Contains("No haber visto una diferencia no prueba que no la haya", three.Summary);
    }

    [Fact]
    public void Las_compras_sobre_clics_tambien_se_comparan_y_una_diferencia_en_contra_se_dice()
    {
        ArmData control = new("Control", 1, "a", 1, new AdMetrics(100m, 20_000, 1000, null, null, 60m, 3000m, 5), Creative([A, B]));
        ArmData variant = new("Sin música", 2, "b", 1, new AdMetrics(100m, 20_000, 1000, null, null, 30m, 1500m, 5), Creative([A, B], music: true));

        ExperimentResult result = ExperimentRules.Evaluate(ExperimentMetric.PurchaseRate, ExperimentVariable.Music, [control, variant], 5_000, finished: true);

        Assert.Equal((ExperimentVerdict.ClearDifference, 0.03m, false, -0.5m), (result.Verdict, result.Arms[1].Rate, result.Arms[1].Higher, result.Arms[1].Lift));
        Assert.Contains("una diferencia clara en contra (-50 %)", result.Summary);
        Assert.Contains("compras sobre clics", result.Summary);
    }

    [Fact]
    public void Los_problemas_de_diseno_se_avisan()
    {
        // Otra campaña, reparto desparejo y una creatividad que cambia dos cosas.
        ExperimentResult messy = ExperimentRules.Evaluate(
            ExperimentMetric.Ctr, ExperimentVariable.Opening,
            [
                Arm("Control", 1, 30_000, 300, Creative([A, B])),
                Arm("Variante", 2, 10_000, 150, Creative([C, B], subtitles: true), campaign: 9),
            ],
            5_000, finished: true);
        Assert.Equal(3, messy.Warnings.Count);
        Assert.Contains(messy.Warnings, w => w.StartsWith("Los brazos están en campañas distintas"));
        Assert.Contains(messy.Warnings, w => w.StartsWith("El reparto fue desparejo"));
        Assert.Contains(messy.Warnings, w => w == "«Variante» difiere del control en más de una cosa (los subtítulos): no se puede saber cuál explica la diferencia.");
        // Los avisos no cambian el cálculo: lo acompañan, y el resumen lo dice.
        Assert.Equal(ExperimentVerdict.ClearDifference, messy.Verdict);
        Assert.EndsWith("Atención: el diseño tiene problemas (ver los avisos) y la diferencia puede deberse a ellos.", messy.Summary);
        // Sin diferencia que informar, no hay nada que matizar.
        Assert.DoesNotContain("Atención", ExperimentRules.Evaluate(
            ExperimentMetric.Ctr, ExperimentVariable.Opening,
            [Arm("Control", 1, 10_000, 100, Creative([A, B])), Arm("Variante", 2, 10_000, 101, Creative([C, B]), campaign: 9)], 5_000, finished: true).Summary);

        // Dice probar la apertura y las creatividades abren igual.
        ExperimentResult same = ExperimentRules.Evaluate(
            ExperimentMetric.Ctr, ExperimentVariable.Opening,
            [Arm("Control", 1, 10_000, 100, Creative([A, B])), Arm("Variante", 2, 10_000, 100, Creative([A, C]))], 5_000, finished: true);
        Assert.Equal(
            ["«Variante» no difiere del control en lo que el experimento dice probar.", "«Variante» difiere del control en más de una cosa (el cierre): no se puede saber cuál explica la diferencia."],
            same.Warnings);

        // Un anuncio sin creatividad vinculada, y una variable que el sistema no ve.
        ExperimentResult unlinked = ExperimentRules.Evaluate(
            ExperimentMetric.Ctr, ExperimentVariable.Opening,
            [Arm("Control", 1, 10_000, 100, Creative([A, B])), Arm("Variante", 2, 10_000, 100, null)], 5_000, finished: true);
        Assert.StartsWith("Algún brazo no está vinculado a un video generado", Assert.Single(unlinked.Warnings));
        ExperimentResult other = ExperimentRules.Evaluate(
            ExperimentMetric.Ctr, ExperimentVariable.Other,
            [Arm("Control", 1, 10_000, 100, null), Arm("Variante", 2, 10_000, 100, null)], 5_000, finished: true);
        Assert.StartsWith("La variable es algo que el sistema no ve", Assert.Single(other.Warnings));
    }

    [Fact]
    public async Task Un_experimento_se_crea_se_calcula_sobre_su_periodo_y_se_cierra_con_una_conclusion()
    {
        await fixture.ClearJobsAsync();
        int org = await fixture.NewOrganizationAsync();
        await using AsyncServiceScope scope = fixture.Scope(org);
        MetaService meta = scope.ServiceProvider.GetRequiredService<MetaService>();
        ExperimentService experiments = scope.ServiceProvider.GetRequiredService<ExperimentService>();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        const string csv =
            """
            Identificador del anuncio,Nombre del anuncio,Día,Importe gastado (USD),Impresiones,Clics en el enlace,Identificador de la campaña,Nombre de la campaña
            201,Control,2020-03-01,10,5000,50,c1,Prueba
            201,Control,2020-03-02,10,5000,50,c1,Prueba
            202,Variante,2020-03-01,10,5000,80,c1,Prueba
            202,Variante,2020-03-02,10,5000,70,c1,Prueba
            202,Variante,2020-03-09,10,50000,10,c1,Prueba
            203,Otro,2020-03-01,10,5000,60,c2,Otra campaña
            """;
        await using (MemoryStream stream = new(Encoding.UTF8.GetBytes(csv)))
        {
            await meta.UploadAsync("informe.csv", "USD", stream, CancellationToken.None);
        }

        await DrainAsync();
        Dictionary<string, long> ads = (await meta.OverviewAsync(new MetaFilter(), CancellationToken.None)).Ads.ToDictionary(a => a.ExternalId, a => a.Id);
        Assert.Equal(["Otra campaña · Otro", "Prueba · Control", "Prueba · Variante"], (await experiments.AdOptionsAsync(CancellationToken.None)).Select(o => o.Label));
        CreateExperimentBody body = new()
        {
            Name = "  Apertura con pregunta  ",
            Hypothesis = "Abrir con una pregunta sube el CTR",
            Variable = ExperimentVariable.Other,
            Metric = ExperimentMetric.Ctr,
            StartDate = new DateOnly(2020, 3, 1),
            EndDate = new DateOnly(2020, 3, 2),
            MinImpressionsPerArm = 5000,
            Arms =
            [
                new ExperimentArmBody { AdId = ads["201"] },
                new ExperimentArmBody { Name = " Con pregunta ", AdId = ads["202"] },
                new ExperimentArmBody(),
            ],
        };

        long id = await experiments.CreateAsync(body, CancellationToken.None);

        ExperimentDetail detail = await experiments.GetAsync(id, CancellationToken.None);
        Assert.Equal(("Apertura con pregunta", ExperimentVariable.Other, 5000), (detail.Name, detail.Variable, detail.MinImpressionsPerArm));
        // Sólo cuenta el período: el día 9 de la variante, con muchas impresiones y casi sin clics, queda afuera.
        Assert.Equal(["Control", "Con pregunta"], detail.Result.Arms.Select(a => a.Name));
        Assert.Equal((10_000L, 100L, 10_000L, 150L), (detail.Result.Arms[0].Metrics.Impressions, detail.Result.Arms[0].Metrics.Clicks, detail.Result.Arms[1].Metrics.Impressions, detail.Result.Arms[1].Metrics.Clicks));
        // El período terminó hace años: el resultado es definitivo.
        Assert.Equal((ExperimentVerdict.ClearDifference, false, 0.5m), (detail.Result.Verdict, detail.Result.Provisional, detail.Result.Arms[1].Lift));
        Assert.StartsWith("La variable es algo que el sistema no ve", Assert.Single(detail.Result.Warnings));
        Assert.Equal(new AdExperiment(id, "Apertura con pregunta", "Con pregunta"), Assert.Single((await meta.GetAsync(ads["202"], CancellationToken.None)).Experiments));

        // Sin fecha de fin sigue en curso: provisional, y entra todo lo que haya desde el inicio.
        body.EndDate = null;
        body.Arms.Add(new ExperimentArmBody { AdId = ads["203"] });
        long open = await experiments.CreateAsync(body, CancellationToken.None);
        ExperimentDetail running = await experiments.GetAsync(open, CancellationToken.None);
        Assert.True(running.Result.Provisional);
        Assert.Equal(3, running.Result.Arms.Count);
        Assert.Equal(60_000L, running.Result.Arms[1].Metrics.Impressions);
        Assert.Contains(running.Result.Warnings, w => w.StartsWith("Los brazos están en campañas distintas"));
        Assert.Contains(running.Result.Warnings, w => w.StartsWith("El reparto fue desparejo"));

        await experiments.ConcludeAsync(open, "  No concluyente: el reparto fue desparejo. Repetir.  ", CancellationToken.None);
        ExperimentDetail closed = await experiments.GetAsync(open, CancellationToken.None);
        Assert.Equal("No concluyente: el reparto fue desparejo. Repetir.", closed.Conclusion);
        Assert.NotNull(closed.ConcludedAt);
        Assert.NotNull(closed.EndDate);
        Assert.Equal([(open, true), (id, false)], (await experiments.ListAsync(CancellationToken.None)).Select(e => (e.Id, e.Concluded)));
        Assert.Equal(2, await db.AuditEntries.CountAsync(a => a.Action == "experiment.create"));

        // Un experimento mal definido no se crea.
        body.Arms = [new ExperimentArmBody { AdId = ads["201"] }];
        await JobTests.AssertBusinessAsync(400, "EXPERIMENT_INVALID", () => experiments.CreateAsync(body, CancellationToken.None));
        body.Arms = [new ExperimentArmBody { AdId = ads["201"] }, new ExperimentArmBody { AdId = ads["201"] }];
        await JobTests.AssertBusinessAsync(400, "EXPERIMENT_INVALID", () => experiments.CreateAsync(body, CancellationToken.None));
        body.Arms = [new ExperimentArmBody { AdId = ads["201"] }, new ExperimentArmBody { AdId = long.MaxValue }];
        await JobTests.AssertBusinessAsync(404, "AD_NOT_FOUND", () => experiments.CreateAsync(body, CancellationToken.None));
        body.Arms = [new ExperimentArmBody { AdId = ads["201"] }, new ExperimentArmBody { AdId = ads["202"] }];
        body.EndDate = new DateOnly(2020, 2, 1);
        await JobTests.AssertBusinessAsync(400, "EXPERIMENT_INVALID", () => experiments.CreateAsync(body, CancellationToken.None));

        await using AsyncServiceScope otherScope = fixture.Scope(await fixture.NewOrganizationAsync());
        ExperimentService foreign = otherScope.ServiceProvider.GetRequiredService<ExperimentService>();
        Assert.Empty(await foreign.ListAsync(CancellationToken.None));
        Assert.Empty(await foreign.AdOptionsAsync(CancellationToken.None));
        await JobTests.AssertBusinessAsync(404, "EXPERIMENT_NOT_FOUND", () => foreign.GetAsync(id, CancellationToken.None));
        await JobTests.AssertBusinessAsync(404, "EXPERIMENT_NOT_FOUND", () => foreign.ConcludeAsync(id, "x", CancellationToken.None));
        body.EndDate = null;
        await JobTests.AssertBusinessAsync(404, "AD_NOT_FOUND", () => foreign.CreateAsync(body, CancellationToken.None));
    }

    private static AdComponents Creative(
        List<ComponentRef> items, bool subtitles = false, bool music = false, RecipeTransition transition = RecipeTransition.Cut)
    {
        return new AdComponents { Items = items, Subtitles = subtitles, Music = music, Transition = transition };
    }

    private static ArmData Arm(string name, long adId, long impressions, long clicks, AdComponents? creative, long campaign = 1)
    {
        return new ArmData(name, adId, $"Anuncio {adId}", campaign, new AdMetrics(50m, impressions, clicks, null, null, null, null, 4), creative);
    }

    private async Task DrainAsync()
    {
        JobRunner runner = fixture.Services.GetRequiredService<JobRunner>();
        while (await runner.ProcessNextAsync(CancellationToken.None))
        {
        }
    }
}

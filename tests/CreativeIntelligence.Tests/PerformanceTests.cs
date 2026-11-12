using System.Text;
using Microsoft.Extensions.DependencyInjection;

namespace CreativeIntelligence.Tests;

/// <summary>Análisis por componente: niveles de evidencia, señal estadística, fatiga, agrupación y el análisis completo.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class PerformanceTests(DatabaseFixture fixture)
{
    private static readonly ComponentRef F1 = new(1, "uno.mp4", 0, 2000, 11, CreativeFunction.Hook);
    private static readonly ComponentRef F2 = new(1, "uno.mp4", 2000, 4000, 12, CreativeFunction.Demonstration);
    private static readonly ComponentRef F3 = new(2, "dos.mp4", 0, 3000, 21, CreativeFunction.Cta);
    private static readonly ComponentRef F4 = new(2, "dos.mp4", 3000, 5000, 22, null);

    [Theory]
    [InlineData(1, 1, 100_000, EvidenceLevel.Insufficient)]
    [InlineData(2, 1, 4_999, EvidenceLevel.Insufficient)]
    [InlineData(2, 1, 5_000, EvidenceLevel.Preliminary)]
    [InlineData(4, 2, 60_000, EvidenceLevel.Preliminary)]
    [InlineData(5, 1, 50_000, EvidenceLevel.Preliminary)]
    [InlineData(5, 2, 49_999, EvidenceLevel.Preliminary)]
    [InlineData(5, 2, 50_000, EvidenceLevel.Moderate)]
    public void La_evidencia_depende_de_cuantos_anuncios_campanas_e_impresiones_hay(int ads, int campaigns, long impressions, EvidenceLevel expected)
    {
        Assert.Equal(expected, PerformanceRules.Evidence(ads, campaigns, impressions));
    }

    [Fact]
    public void La_senal_estadistica_distingue_una_diferencia_real_de_una_compatible_con_el_azar()
    {
        // 1,5 % contra 1,0 % con 10.000 impresiones por lado: difícilmente azar.
        double clear = PerformanceRules.ZScore(150, 10_000, 100, 10_000)!.Value;
        Assert.InRange(clear, 3.1, 3.3);
        Assert.Equal(SignalStrength.Clear, PerformanceRules.Strength(clear));
        // 1,2 % contra 1,0 %: compatible con el azar.
        double weak = PerformanceRules.ZScore(120, 10_000, 100, 10_000)!.Value;
        Assert.InRange(weak, 1.3, 1.4);
        Assert.Equal(SignalStrength.NotDistinguishable, PerformanceRules.Strength(weak));
        // El signo dice cuál es mayor, y con la misma tasa no hay diferencia.
        Assert.True(PerformanceRules.ZScore(100, 10_000, 150, 10_000) < 0);
        Assert.Equal(0.0, PerformanceRules.ZScore(50, 1000, 100, 2000)!.Value);
        Assert.Equal(SignalStrength.Probable, PerformanceRules.Strength(-2.0));
        // Sin impresiones, o sin ningún clic en ningún lado, no se puede calcular.
        Assert.Null(PerformanceRules.ZScore(0, 0, 10, 1000));
        Assert.Null(PerformanceRules.ZScore(0, 1000, 0, 1000));
        Assert.Equal(SignalStrength.None, PerformanceRules.Strength(null));
    }

    [Fact]
    public void La_fatiga_solo_se_senala_con_dias_volumen_y_una_caida_que_no_sea_azar()
    {
        Assert.Equal(
            new FatigueSignal(0.03m, 0.015m, 0.5m, 6),
            PerformanceRules.Fatigue(Days(6, i => i < 3 ? 30 : 15, 1000)));
        // Con un día impar en el medio, se comparan los extremos.
        Assert.Equal(7, PerformanceRules.Fatigue(Days(7, i => i < 3 ? 30 : 15, 1000))!.Days);
        // Pocos días, poco volumen, caída chica o CTR que sube: sin señal.
        Assert.Null(PerformanceRules.Fatigue(Days(5, i => i < 2 ? 30 : 15, 1000)));
        Assert.Null(PerformanceRules.Fatigue(Days(6, i => i < 3 ? 6 : 3, 200)));
        Assert.Null(PerformanceRules.Fatigue(Days(6, i => i < 3 ? 30 : 27, 1000)));
        Assert.Null(PerformanceRules.Fatigue(Days(6, i => i < 3 ? 15 : 30, 1000)));
        // Una caída grande en proporción pero con tan pocos clics que puede ser azar.
        Assert.Null(PerformanceRules.Fatigue(Days(6, i => i < 3 ? 4 : 2, 1000)));
    }

    [Fact]
    public void Los_anuncios_se_agrupan_por_componente_y_cada_grupo_se_compara_con_el_resto()
    {
        List<AdComponents> ads =
        [
            Ad(1, 1, 10_000, 200, [F1, F2]),
            Ad(2, 1, 10_000, 150, [F1, F3]),
            Ad(3, 2, 10_000, 80, [F4, F2]),
        ];

        List<ComponentDimension> dimensions = PerformanceService.Dimensions(ads);

        ComponentDimension opening = dimensions.Single(d => d.Name == "opening");
        Assert.Equal(["1:0-2000", "2:3000-5000"], opening.Groups.Select(g => g.Key));
        ComponentGroup first = opening.Groups[0];
        Assert.Equal((2, 1, 20_000L, 350L, 10_000L, 80L), (first.Ads, first.Campaigns, first.Metrics.Impressions, first.Metrics.Clicks, first.Rest.Impressions, first.Rest.Clicks));
        Assert.Equal((EvidenceLevel.Preliminary, SignalStrength.Clear, true), (first.Evidence, first.CtrSignal, first.CtrHigher));
        Assert.Equal(F1, first.Fragment);
        Assert.Contains("CTR 1.75 % frente a 0.80 % del resto: una diferencia clara a favor", first.Reading);
        Assert.Contains("Es una correlación, no una causa", first.Reading);
        Assert.Contains("una misma campaña", first.Reading);
        // Un solo anuncio: no se calcula ninguna señal y la lectura lo dice.
        ComponentGroup alone = opening.Groups[1];
        Assert.Equal((EvidenceLevel.Insufficient, SignalStrength.None), (alone.Evidence, alone.CtrSignal));
        Assert.StartsWith("Datos insuficientes: 1 anuncio(s) y 10000 impresiones", alone.Reading);

        // Un anuncio cuenta en cada fragmento que usa, esté en la posición que esté.
        ComponentDimension fragments = dimensions.Single(d => d.Name == "fragment");
        Assert.Equal(4, fragments.Groups.Count);
        ComponentGroup shared = fragments.Groups.Single(g => g.Key == "1:2000-4000");
        Assert.Equal((2, 2, 280L), (shared.Ads, shared.Campaigns, shared.Metrics.Clicks));
        Assert.Contains("los anuncios son de 2 campañas", shared.Reading);
        Assert.Equal(SignalStrength.NotDistinguishable, shared.CtrSignal);

        // El cierre sólo existe en videos de más de un fragmento; la estructura, si todos tienen función.
        Assert.Equal(["1:2000-4000", "2:0-3000"], dimensions.Single(d => d.Name == "closing").Groups.Select(g => g.Key).Order());
        Assert.Equal(["Hook>Cta", "Hook>Demonstration"], dimensions.Single(d => d.Name == "structure").Groups.Select(g => g.Key).Order());
        // Si todos comparten un valor, no hay con qué comparar.
        ComponentGroup subtitles = Assert.Single(dimensions.Single(d => d.Name == "subtitles").Groups);
        Assert.Equal((3, SignalStrength.None, "Todos los anuncios analizados lo tienen: no hay con qué comparar."), (subtitles.Ads, subtitles.CtrSignal, subtitles.Reading));
        Assert.All(dimensions, d => Assert.All(d.Groups, g => Assert.DoesNotContain("ganador", g.Reading, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Al_sumar_metricas_una_cantidad_opcional_queda_sin_dato_solo_si_nadie_la_informa()
    {
        AdMetrics sum = PerformanceRules.Sum([new AdMetrics(10m, 100, 5, 40, null, null, null, 2), new AdMetrics(5m, 50, 1, null, null, 2m, null, 1)]);

        Assert.Equal(new AdMetrics(15m, 150, 6, 40, null, 2m, null, 3), sum);
        Assert.Equal(new AdMetrics(0m, 0, 0, null, null, null, null, 0), PerformanceRules.Sum([]));
    }

    [Fact]
    public async Task El_analisis_lee_solo_los_anuncios_vinculados_y_respeta_campana_y_periodo()
    {
        await fixture.ClearJobsAsync();
        int org = await fixture.NewOrganizationAsync();
        await using AsyncServiceScope scope = fixture.Scope(org);
        CompositionService composition = scope.ServiceProvider.GetRequiredService<CompositionService>();
        MetaService meta = scope.ServiceProvider.GetRequiredService<MetaService>();
        PerformanceService performance = scope.ServiceProvider.GetRequiredService<PerformanceService>();
        long asset;
        await using (FileStream stream = File.OpenRead(fixture.SampleCutsVideoPath))
        {
            asset = (await scope.ServiceProvider.GetRequiredService<MediaLibraryService>()
                .IngestAsync("cortes.mp4", new MediaRightsBody { ReuseAuthorized = true }, stream, CancellationToken.None)).AssetId;
        }

        await DrainAsync();
        List<SegmentItem> scenes = (await scope.ServiceProvider.GetRequiredService<SceneService>().ListAsync(asset, CancellationToken.None)).Segments;

        // Tres creatividades: dos abren con la misma escena.
        long[][] montages = [[scenes[0].Id, scenes[1].Id], [scenes[0].Id, scenes[2].Id], [scenes[2].Id]];
        List<long> videos = [];
        foreach (long[] montage in montages)
        {
            long recipe = await composition.CreateAsync(
                new CreateRecipeBody { Name = $"Receta {videos.Count + 1}", Format = "1:1", Subtitles = videos.Count == 2, Items = montage.Select(id => new RecipeItemBody { SegmentId = id }).ToList() },
                CancellationToken.None);
            videos.Add(await composition.RenderAsync(recipe, CancellationToken.None));
        }

        // Cuatro anuncios en dos campañas; el 104 no se vincula. El 101 tiene seis días con el CTR cayendo.
        StringBuilder csv = new("Identificador del anuncio,Nombre del anuncio,Día,Importe gastado (USD),Impresiones,Clics en el enlace,Identificador de la campaña,Nombre de la campaña\n");
        for (int day = 1; day <= 6; day++)
        {
            csv.Append(System.Globalization.CultureInfo.InvariantCulture, $"101,Abre con escena 1 (a),2026-11-0{day},10,2000,{(day <= 3 ? 60 : 30)},c1,Navidad\n");
        }

        csv.Append("102,Abre con escena 1 (b),2026-11-01,20,6000,90,c1,Navidad\n");
        csv.Append("103,Abre con escena 3,2026-11-01,20,8000,40,c2,Reyes\n");
        csv.Append("104,Sin vincular,2026-11-01,20,5000,50,c2,Reyes\n");
        await using (MemoryStream stream = new(Encoding.UTF8.GetBytes(csv.ToString())))
        {
            await meta.UploadAsync("informe.csv", "USD", stream, CancellationToken.None);
        }

        await DrainAsync();
        Dictionary<string, long> ads = (await meta.OverviewAsync(new MetaFilter(), CancellationToken.None)).Ads.ToDictionary(a => a.ExternalId, a => a.Id);
        await meta.LinkAsync(ads["101"], videos[0], CancellationToken.None);
        await meta.LinkAsync(ads["102"], videos[1], CancellationToken.None);
        await meta.LinkAsync(ads["103"], videos[2], CancellationToken.None);

        PerformanceAnalysis analysis = await performance.AnalyzeAsync(new PerformanceFilter(), CancellationToken.None);

        Assert.Equal((4, 3, 2), (analysis.AdsWithData, analysis.LinkedAds, analysis.Campaigns.Count));
        ComponentDimension opening = analysis.Dimensions.Single(d => d.Name == "opening");
        ComponentGroup sceneOne = opening.Groups.Single(g => g.Fragment!.SegmentId == scenes[0].Id);
        // 101 (12.000 impresiones, 270 clics) + 102 (6.000, 90) contra 103 (8.000, 40).
        Assert.Equal((2, 1, 18_000L, 360L, 8_000L, 40L), (sceneOne.Ads, sceneOne.Campaigns, sceneOne.Metrics.Impressions, sceneOne.Metrics.Clicks, sceneOne.Rest.Impressions, sceneOne.Rest.Clicks));
        Assert.Equal((EvidenceLevel.Preliminary, SignalStrength.Clear, true, "cortes.mp4"), (sceneOne.Evidence, sceneOne.CtrSignal, sceneOne.CtrHigher, sceneOne.Fragment!.FileName));
        Assert.Equal(EvidenceLevel.Insufficient, opening.Groups.Single(g => g.Fragment!.SegmentId == scenes[2].Id).Evidence);
        // La escena 3 está en dos creatividades (cierra una y es toda la otra): cuenta en las dos.
        Assert.Equal(2, analysis.Dimensions.Single(d => d.Name == "fragment").Groups.Single(g => g.Fragment!.SegmentId == scenes[2].Id).Ads);
        Assert.Equal(["no", "yes"], analysis.Dimensions.Single(d => d.Name == "subtitles").Groups.Select(g => g.Key).Order());
        // Sin funciones asignadas no hay estructura que comparar.
        Assert.Empty(analysis.Dimensions.Single(d => d.Name == "structure").Groups);
        FatigueItem fatigue = Assert.Single(analysis.Fatigue);
        Assert.Equal((ads["101"], "Navidad", 0.03m, 0.015m, 6), (fatigue.AdId, fatigue.CampaignName, fatigue.Signal.FirstCtr, fatigue.Signal.LastCtr, fatigue.Signal.Days));

        // Dentro de una campaña sólo se comparan sus anuncios.
        long reyes = analysis.Campaigns.Single(c => c.Name == "Reyes").Id;
        PerformanceAnalysis oneCampaign = await performance.AnalyzeAsync(new PerformanceFilter { CampaignId = reyes }, CancellationToken.None);
        Assert.Equal((2, 1), (oneCampaign.AdsWithData, oneCampaign.LinkedAds));
        Assert.Equal(
            "Datos insuficientes: 1 anuncio(s) y 8000 impresiones. Todavía no se puede decir nada.",
            oneCampaign.Dimensions.Single(d => d.Name == "music").Groups.Single().Reading);

        // Sólo el primer día: el 101 queda con un día y ya no hay fatiga que buscar.
        PerformanceAnalysis firstDay = await performance.AnalyzeAsync(new PerformanceFilter { From = new DateOnly(2026, 11, 1), To = new DateOnly(2026, 11, 1) }, CancellationToken.None);
        Assert.Empty(firstDay.Fatigue);
        Assert.Equal(8_000L, firstDay.Dimensions.Single(d => d.Name == "opening").Groups.Single(g => g.Fragment!.SegmentId == scenes[0].Id).Metrics.Impressions);
        Assert.Equal(0, (await performance.AnalyzeAsync(new PerformanceFilter { From = new DateOnly(2027, 1, 1) }, CancellationToken.None)).AdsWithData);

        await using AsyncServiceScope otherScope = fixture.Scope(await fixture.NewOrganizationAsync());
        PerformanceAnalysis foreign = await otherScope.ServiceProvider.GetRequiredService<PerformanceService>().AnalyzeAsync(new PerformanceFilter(), CancellationToken.None);
        Assert.Equal((0, 0, 0), (foreign.AdsWithData, foreign.LinkedAds, foreign.Campaigns.Count));
        Assert.All(foreign.Dimensions, d => Assert.Empty(d.Groups));
    }

    private static List<DayPoint> Days(int count, Func<int, int> clicks, long impressions)
    {
        return Enumerable.Range(0, count).Select(i => new DayPoint(new DateOnly(2026, 11, 1).AddDays(i), impressions, clicks(i))).ToList();
    }

    private static AdComponents Ad(long id, long campaign, long impressions, long clicks, List<ComponentRef> items)
    {
        return new AdComponents
        {
            AdId = id,
            AdName = $"Anuncio {id}",
            CampaignId = campaign,
            Metrics = new AdMetrics(100m, impressions, clicks, null, null, null, null, 3),
            Items = items,
        };
    }

    private async Task DrainAsync()
    {
        JobRunner runner = fixture.Services.GetRequiredService<JobRunner>();
        while (await runner.ProcessNextAsync(CancellationToken.None))
        {
        }
    }
}

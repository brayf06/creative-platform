using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CreativeIntelligence.Tests;

/// <summary>Rendimiento en Meta: lectura del informe, métricas, importación, vínculo con el video y linaje.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class MetaTests(DatabaseFixture fixture)
{
    private const string SpanishReport =
        """"
        "Nombre de la campaña","Nombre del conjunto de anuncios","Nombre del anuncio","Identificador del anuncio","Día","Importe gastado (UYU)","Impresiones","Clics (todos)","Clics en el enlace","Reproducciones de video de 3 segundos","ThruPlays","Compras","Valor de conversión de compras"
        "Navidad","Mujeres 25-45","Hook regalo, versión ""A""","1201","2026-11-01","1.234,50","10.000","400","150","3.000","900","5","9.876,00"
        "Navidad","Mujeres 25-45","Hook regalo, versión ""A""","1201","2026-11-02","765,50","5.000","200","50","1.000","300","","",
        "Navidad","Mujeres 25-45","Demostración","1202","2026-11-01","100","2000","30","10","","","0","0"
        "Navidad","Mujeres 25-45","Sin fecha","1203","ayer","10","100","1","1","","","",""
        ,,,,,"2.110,00","17.100","631","211",,,,
        """";

    [Theory]
    [InlineData("1234.56", 1234.56)]
    [InlineData("1,234.56", 1234.56)]
    [InlineData("1.234,56", 1234.56)]
    [InlineData("1234,56", 1234.56)]
    [InlineData("1,234", 1234.0)]
    [InlineData("10.000", 10000.0)]
    [InlineData("1.234.567", 1234567.0)]
    [InlineData("0,5", 0.5)]
    [InlineData("12.5", 12.5)]
    [InlineData(" $ 1 500,25 ", 1500.25)]
    [InlineData("-3", -3.0)]
    [InlineData("", null)]
    [InlineData("n/d", null)]
    public void Un_numero_se_lee_con_punto_o_coma_decimal_y_separador_de_miles(string text, double? expected)
    {
        Assert.Equal(expected is null ? null : (decimal?)Convert.ToDecimal(expected.Value), MetaCsvParser.ParseDecimal(text));
    }

    [Fact]
    public void El_informe_en_espanol_se_lee_por_nombre_de_columna_y_descarta_lo_que_no_sirve()
    {
        MetaCsvResult result = MetaCsvParser.Parse("﻿" + SpanishReport);

        Assert.Empty(result.MissingColumns);
        Assert.Equal("UYU", result.Currency);
        Assert.Equal(3, result.Rows.Count);
        Assert.Equal(2, result.Skipped);
        Assert.Equal(["Fila 5: la fecha no se entiende.", "Fila 6: no tiene identificador de anuncio (puede ser la fila de totales)."], result.Problems);
        MetaCsvRow first = result.Rows[0];
        Assert.Equal(("1201", "Hook regalo, versión \"A\"", new DateOnly(2026, 11, 1), 1234.50m, 10000L), (first.AdId, first.AdName, first.Date, first.Spend, first.Impressions));
        // Si están las dos columnas de clics, vale la de clics en el enlace.
        Assert.Equal((150L, 3000L, 900L, 5m, 9876.00m), (first.Clicks, first.VideoPlays!.Value, first.ThruPlays!.Value, first.Purchases!.Value, first.PurchaseValue!.Value));
        Assert.Equal(("Navidad", "Mujeres 25-45", null), (first.CampaignName, first.AdSetName, first.CampaignId));
        // Una celda vacía es "sin dato", no cero.
        Assert.Equal((null, null), (result.Rows[1].Purchases, result.Rows[1].PurchaseValue));
        Assert.Equal((null, 0m), (result.Rows[2].VideoPlays, result.Rows[2].Purchases));
    }

    [Fact]
    public void El_informe_en_ingles_con_punto_y_coma_tambien_se_lee_y_sin_columnas_obligatorias_no()
    {
        const string english =
            """
            Ad ID;Ad name;Reporting starts;Amount spent (USD);Impressions;Link clicks;Campaign ID;Campaign name;Ad set ID;Ad set name;Account ID;Account name
            77;Winter hook;03/11/2026;12.5;1,000;20;c1;Winter;s1;Broad;act_9;Main account
            """;

        MetaCsvResult result = MetaCsvParser.Parse(english);

        MetaCsvRow row = Assert.Single(result.Rows);
        Assert.Equal(("77", "Winter hook", new DateOnly(2026, 11, 3), 12.5m, 1000L, 20L), (row.AdId, row.AdName, row.Date, row.Spend, row.Impressions, row.Clicks));
        Assert.Equal(("c1", "s1", "act_9", "Main account", "USD"), (row.CampaignId, row.AdSetId, row.AccountId, row.AccountName, result.Currency));

        MetaCsvResult incomplete = MetaCsvParser.Parse("Nombre del anuncio,Impresiones\nUno,10");
        Assert.Empty(incomplete.Rows);
        Assert.Equal(["Identificador del anuncio", "Día", "Importe gastado"], incomplete.MissingColumns);
        Assert.Equal(MetaCsvParser.Required, MetaCsvParser.Parse("").MissingColumns);
    }

    [Fact]
    public void Las_tasas_se_calculan_sobre_las_sumas_y_son_nulas_si_no_hay_con_que_dividir()
    {
        AdMetrics metrics = new(200m, 10_000, 250, 4000, 1000, 8m, 600m, 2);

        Assert.Equal((0.025m, 0.8m, 20m, 25m, 3m, 0.4m, 0.25m), (metrics.Ctr, metrics.Cpc, metrics.Cpm, metrics.Cpa, metrics.Roas, metrics.HookRate, metrics.HoldRate));
        AdMetrics empty = new(0m, 0, 0, null, null, null, null, 0);
        Assert.Equal((null, null, null, null, null, null, null), (empty.Ctr, empty.Cpc, empty.Cpm, empty.Cpa, empty.Roas, empty.HookRate, empty.HoldRate));
        // Sin compras no hay costo por compra, aunque haya gasto.
        Assert.Null(new AdMetrics(50m, 1000, 10, null, null, 0m, 0m, 1).Cpa);
    }

    [Fact]
    public async Task Un_informe_se_importa_se_puede_reimportar_sin_duplicar_y_se_consulta_por_periodo()
    {
        int org = await fixture.NewOrganizationAsync();
        await fixture.ClearJobsAsync();
        await using AsyncServiceScope scope = fixture.Scope(org);
        MetaService meta = scope.ServiceProvider.GetRequiredService<MetaService>();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await JobTests.AssertBusinessAsync(415, "UNSUPPORTED_MEDIA_TYPE", () => UploadAsync(meta, "informe.xlsx", SpanishReport));
        await JobTests.AssertBusinessAsync(400, "EMPTY_FILE", () => UploadAsync(meta, "vacio.csv", ""));
        long importId = await UploadAsync(meta, "informe.csv", SpanishReport);
        Assert.Equal(MediaProcessingStatus.Pending, (await meta.OverviewAsync(new MetaFilter(), CancellationToken.None)).Imports.Single().Status);
        await DrainAsync();

        MetaOverview overview = await meta.OverviewAsync(new MetaFilter(), CancellationToken.None);
        MetaImportItem import = overview.Imports.Single(i => i.Id == importId);
        Assert.Equal((MediaProcessingStatus.Ready, 3, 2, 2), (import.Status, import.Rows, import.Skipped, import.Ads));
        Assert.Equal((new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 2)), (import.FirstDate, import.LastDate));
        Assert.Contains("la fecha no se entiende", import.Notes);
        // Del que más gastó al que menos, con las tasas calculadas sobre las sumas de los dos días.
        Assert.Equal(["Hook regalo, versión \"A\"", "Demostración"], overview.Ads.Select(a => a.Name));
        AdListItem hook = overview.Ads[0];
        Assert.Equal(("UYU", "Navidad", "Mujeres 25-45", 2000.00m, 15_000L, 200L, 2), (hook.Currency, hook.CampaignName, hook.AdSetName, hook.Metrics.Spend, hook.Metrics.Impressions, hook.Metrics.Clicks, hook.Metrics.Days));
        Assert.Equal((5m, 9876.00m, 400m, 4.938m), (hook.Metrics.Purchases, hook.Metrics.PurchaseValue, hook.Metrics.Cpa, hook.Metrics.Roas));
        Assert.Null(hook.VariantId);
        Assert.Equal((1, 1, 1), (await db.MetaAdAccounts.CountAsync(), await db.MetaCampaigns.CountAsync(), await db.MetaAdSets.CountAsync()));

        // Sólo el primer día.
        MetaOverview firstDay = await meta.OverviewAsync(new MetaFilter { From = new DateOnly(2026, 11, 1), To = new DateOnly(2026, 11, 1) }, CancellationToken.None);
        Assert.Equal((1234.50m, 1), (firstDay.Ads.Single(a => a.Id == hook.Id).Metrics.Spend, firstDay.Ads.Single(a => a.Id == hook.Id).Metrics.Days));
        Assert.Empty((await meta.OverviewAsync(new MetaFilter { From = new DateOnly(2027, 1, 1) }, CancellationToken.None)).Ads);

        // Un informe más nuevo corrige el día 2 y agrega el día 3: reemplaza, no suma, y no duplica la estructura.
        const string newer =
            """"
            Identificador del anuncio,Nombre del anuncio,Día,Importe gastado (UYU),Impresiones,Nombre de la campaña,Nombre del conjunto de anuncios
            1201,"Hook regalo, versión ""A""",2026-11-02,800,6000,Navidad,Mujeres 25-45
            1201,"Hook regalo, versión ""A""",2026-11-03,100,1000,Navidad,Mujeres 25-45
            """";
        await UploadAsync(meta, "nuevo.csv", newer);
        await DrainAsync();

        AdDetail detail = await meta.GetAsync(hook.Id, CancellationToken.None);
        Assert.Equal([1234.50m, 800m, 100m], detail.Days.Select(d => d.Metrics.Spend));
        Assert.Equal((2134.50m, 17_000L, 3), (detail.Totals.Spend, detail.Totals.Impressions, detail.Totals.Days));
        Assert.Equal(("Cuenta importada", "1201"), (detail.AccountName, detail.ExternalId));
        Assert.Null(detail.Lineage);
        Assert.Equal((2, 1, 4), (await db.MetaAds.CountAsync(), await db.MetaCampaigns.CountAsync(), await db.MetaInsightsDaily.CountAsync()));

        // Un archivo sin las columnas obligatorias queda fallido con el motivo.
        long broken = await UploadAsync(meta, "roto.csv", "Nombre del anuncio,Impresiones\nUno,10");
        await DrainAsync();
        MetaImportItem failed = (await meta.OverviewAsync(new MetaFilter(), CancellationToken.None)).Imports.Single(i => i.Id == broken);
        Assert.Equal(MediaProcessingStatus.Failed, failed.Status);
        Assert.Contains("Identificador del anuncio", failed.Notes);

        await using AsyncServiceScope otherScope = fixture.Scope(await fixture.NewOrganizationAsync());
        MetaService foreign = otherScope.ServiceProvider.GetRequiredService<MetaService>();
        MetaOverview empty = await foreign.OverviewAsync(new MetaFilter(), CancellationToken.None);
        Assert.Empty(empty.Ads);
        Assert.Empty(empty.Imports);
        await JobTests.AssertBusinessAsync(404, "AD_NOT_FOUND", () => foreign.GetAsync(hook.Id, CancellationToken.None));
        await JobTests.AssertBusinessAsync(404, "AD_NOT_FOUND", () => foreign.LinkAsync(hook.Id, null, CancellationToken.None));
    }

    [Fact]
    public async Task Un_anuncio_vinculado_a_su_video_muestra_el_linaje_completo_hasta_el_brief()
    {
        await fixture.ClearJobsAsync();
        fixture.Planner.Reset();
        int org = await fixture.NewOrganizationAsync();
        await using AsyncServiceScope scope = fixture.Scope(org);
        MetaService meta = scope.ServiceProvider.GetRequiredService<MetaService>();
        StrategyService strategy = scope.ServiceProvider.GetRequiredService<StrategyService>();
        CompositionService composition = scope.ServiceProvider.GetRequiredService<CompositionService>();

        // Una creatividad que nace de un brief: marca → brief → plan → concepto → receta → video.
        long asset;
        await using (FileStream stream = File.OpenRead(fixture.SampleCutsVideoPath))
        {
            asset = (await scope.ServiceProvider.GetRequiredService<MediaLibraryService>()
                .IngestAsync("cortes.mp4", new MediaRightsBody { ReuseAuthorized = true }, stream, CancellationToken.None)).AssetId;
        }

        await DrainAsync();
        SegmentEditService editor = scope.ServiceProvider.GetRequiredService<SegmentEditService>();
        foreach (SegmentItem segment in (await scope.ServiceProvider.GetRequiredService<SceneService>().ListAsync(asset, CancellationToken.None)).Segments)
        {
            await editor.SetSelectedAsync(segment.Id, true, CancellationToken.None);
        }

        int brand = await scope.ServiceProvider.GetRequiredService<BrandService>().CreateAsync(new BrandBody { Name = "Uñas Ya" }, CancellationToken.None);
        long brief = await scope.ServiceProvider.GetRequiredService<BriefService>().CreateAsync(
            new BriefBody { BrandId = brand, Name = "Navidad", VideoCount = 1, Autonomy = AutonomyLevel.Automatic }, CancellationToken.None);
        long planId = await strategy.RequestAsync(brief, CancellationToken.None);
        await DrainAsync();
        ConceptView concept = (await strategy.GetAsync(planId, CancellationToken.None)).Concepts.Single();
        long variantId = (await composition.GetAsync(concept.RecipeId!.Value, CancellationToken.None)).Variants.Single().Id;
        await UploadAsync(meta, "informe.csv", SpanishReport);
        await DrainAsync();
        long adId = (await meta.OverviewAsync(new MetaFilter(), CancellationToken.None)).Ads[0].Id;

        // Y una receta armada a mano, sin brief. Mientras su video no termina, no se puede vincular.
        long manualRecipe = await composition.CreateAsync(
            new CreateRecipeBody { Name = "A mano", Format = "1:1", Items = [new RecipeItemBody { SegmentId = concept.Fragments[0].SegmentId!.Value, Role = CreativeFunction.Hook }] },
            CancellationToken.None);
        long pending = await composition.RenderAsync(manualRecipe, CancellationToken.None);
        await JobTests.AssertBusinessAsync(404, "VARIANT_NOT_FOUND", () => meta.LinkAsync(adId, pending, CancellationToken.None));
        await JobTests.AssertBusinessAsync(404, "VARIANT_NOT_FOUND", () => meta.LinkAsync(adId, long.MaxValue, CancellationToken.None));
        await DrainAsync();
        await meta.LinkAsync(adId, variantId, CancellationToken.None);

        AdDetail detail = await meta.GetAsync(adId, CancellationToken.None);
        AdLineage lineage = detail.Lineage!;
        Assert.Equal(AdLinkOrigin.Manual, detail.LinkOrigin);
        Assert.Equal((variantId, concept.RecipeId.Value, "Concepto 1", "Hook 1"), (lineage.VariantId, lineage.RecipeId, lineage.RecipeName, lineage.Hook));
        Assert.Equal((concept.Id, "Concepto 1", planId, brief, "Navidad", "Uñas Ya"), (lineage.ConceptId!.Value, lineage.ConceptTitle, lineage.PlanId!.Value, lineage.BriefId!.Value, lineage.BriefName, lineage.BrandName));
        Assert.Null(lineage.BatchId);
        Assert.Equal(concept.Fragments.Select(f => (f.AssetId, f.StartMs, f.EndMs)), lineage.Items.Select(i => (i.AssetId, i.StartMs, i.EndMs)));
        Assert.All(lineage.Items, i => Assert.Equal("cortes.mp4", i.FileName));
        Assert.Contains(detail.Candidates, c => c.VariantId == variantId && c.RecipeName == "Concepto 1");
        Assert.Equal(variantId, (await meta.OverviewAsync(new MetaFilter(), CancellationToken.None)).Ads.Single(a => a.Id == adId).VariantId);

        // Vinculado a la receta hecha a mano: hay receta y fragmentos, pero no concepto ni brief.
        await meta.LinkAsync(adId, pending, CancellationToken.None);
        AdLineage manual = (await meta.GetAsync(adId, CancellationToken.None)).Lineage!;
        Assert.Equal(("A mano", null, null, null), (manual.RecipeName, manual.ConceptId, manual.BriefId, manual.BrandName));
        Assert.Equal(CreativeFunction.Hook, Assert.Single(manual.Items).Role);

        await meta.LinkAsync(adId, null, CancellationToken.None);
        AdDetail unlinked = await meta.GetAsync(adId, CancellationToken.None);
        Assert.Equal((null, null), (unlinked.Lineage, unlinked.LinkOrigin));
    }

    private static async Task<long> UploadAsync(MetaService meta, string fileName, string content)
    {
        await using MemoryStream stream = new(Encoding.UTF8.GetBytes(content));
        return await meta.UploadAsync(fileName, "USD", stream, CancellationToken.None);
    }

    private async Task DrainAsync()
    {
        JobRunner runner = fixture.Services.GetRequiredService<JobRunner>();
        while (await runner.ProcessNextAsync(CancellationToken.None))
        {
        }
    }
}

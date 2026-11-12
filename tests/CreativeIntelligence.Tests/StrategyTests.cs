using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CreativeIntelligence.Tests;

/// <summary>Perfiles de marca, briefs de campaña, interpretación del texto libre y mensajes prohibidos.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class StrategyTests(DatabaseFixture fixture)
{
    [Theory]
    [InlineData("Con este esmalte la CURA  garantizada llega sola", new[] { "cura garantizada" })]
    [InlineData("Resultados médicos comprobados y el mas barato", new[] { "el más barato", "Resultados Medicos" })]
    [InlineData("Uñas lindas en cinco minutos", new string[0])]
    [InlineData(null, new string[0])]
    public void Un_mensaje_prohibido_se_detecta_sin_importar_mayusculas_acentos_ni_espacios(string? text, string[] expected)
    {
        List<string> forbidden = ["cura garantizada", "el más barato", "Resultados Medicos", "   "];

        Assert.Equal(expected, MessageRules.Violations(text, forbidden));
    }

    [Theory]
    [InlineData(
        "Quiero generar 20 anuncios de Navidad orientados a mujeres que buscan regalos originales y prácticos. Utilizá nuestros videos históricos y referencias actuales del mercado.",
        20, null, null, "Navidad", true)]
    [InlineData("15 videos de 30 segundos para Reels por el Día de la Madre", 15, 30, BriefPlatform.MetaReels, "Día de la Madre", null)]
    [InlineData("Necesito piezas para historias de Black Friday, mirando qué hace la competencia", null, null, BriefPlatform.MetaStories, "Black Friday", true)]
    [InlineData("Campaña de otoño en el feed: 8 creatividades", 8, null, BriefPlatform.MetaFeed, "Otoño", null)]
    [InlineData("Mostrar el producto a mujeres de 30 años que lo usan 2 veces por semana", null, null, null, null, null)]
    [InlineData("200 anuncios de 500 segundos", null, null, null, null, null)]
    [InlineData("", null, null, null, null, null)]
    public void El_texto_libre_de_un_brief_completa_solo_lo_que_dice_de_forma_explicita(
        string text, int? count, int? duration, BriefPlatform? platform, string? occasion, bool? research)
    {
        BriefInterpretation result = BriefTextParser.Parse(text);

        Assert.Equal((count, duration, platform, occasion, research), (result.VideoCount, result.DurationSeconds, result.Platform, result.Occasion, result.UseExternalResearch));
        int expected = new[] { count is not null, duration is not null, platform is not null, occasion is not null, research is not null }.Count(v => v);
        Assert.Equal(expected, result.Understood.Count);
    }

    [Fact]
    public async Task El_perfil_de_una_marca_guarda_sus_datos_y_sus_listas_sin_repetidos()
    {
        int org = await fixture.NewOrganizationAsync();
        await using AsyncServiceScope scope = fixture.Scope(org);
        BrandService brands = scope.ServiceProvider.GetRequiredService<BrandService>();

        int brandId = await brands.CreateAsync(new BrandBody { Name = "  Uñas Ya  " }, CancellationToken.None);
        await JobTests.AssertBusinessAsync(409, "BRAND_EXISTS", () => brands.CreateAsync(new BrandBody { Name = "Uñas Ya" }, CancellationToken.None));
        int otherBrand = await brands.CreateAsync(new BrandBody { Name = "Otra" }, CancellationToken.None);
        await JobTests.AssertBusinessAsync(409, "BRAND_EXISTS", () => brands.UpdateAsync(otherBrand, new BrandBody { Name = "Uñas Ya" }, CancellationToken.None));

        await brands.UpdateAsync(
            brandId,
            new BrandBody
            {
                Name = "Uñas Ya",
                ValueProposition = "Uñas de salón en casa",
                CommunicationStyle = "Cercano, de vos",
                VisualIdentity = "Rosa y blanco",
                Objectives = "Vender kits",
                CommercialRestrictions = "Sin descuentos mayores al 30 %",
                MonthlyBudgetUsd = 1500.50m,
            },
            CancellationToken.None);
        int product = await brands.AddItemAsync(
            brandId, new BrandItemBody { Kind = BrandItemKind.Product, Text = " Kit inicial ", Detail = "Esmalte y lámpara", MarginPercent = 42.5m }, CancellationToken.None);
        await brands.AddItemAsync(
            brandId, new BrandItemBody { Kind = BrandItemKind.Benefit, Text = "Seca en 60 segundos", Detail = "Medido con lámpara", MarginPercent = 10 }, CancellationToken.None);
        await brands.AddItemAsync(brandId, new BrandItemBody { Kind = BrandItemKind.ForbiddenMessage, Text = "cura garantizada" }, CancellationToken.None);
        await brands.AddItemAsync(brandId, new BrandItemBody { Kind = BrandItemKind.AllowedMessage, Text = "Kit inicial" }, CancellationToken.None);
        await JobTests.AssertBusinessAsync(
            409, "BRAND_ITEM_EXISTS", () => brands.AddItemAsync(brandId, new BrandItemBody { Kind = BrandItemKind.Product, Text = "Kit inicial" }, CancellationToken.None));
        await JobTests.AssertBusinessAsync(
            404, "BRAND_NOT_FOUND", () => brands.AddItemAsync(int.MaxValue, new BrandItemBody { Kind = BrandItemKind.Product, Text = "x" }, CancellationToken.None));

        BrandDetail detail = await brands.GetAsync(brandId, CancellationToken.None);
        Assert.Equal(("Uñas Ya", "Uñas de salón en casa", "Cercano, de vos", 1500.50m), (detail.Name, detail.ValueProposition, detail.CommunicationStyle, detail.MonthlyBudgetUsd));
        BrandItemView kit = Assert.Single(detail.Of(BrandItemKind.Product));
        Assert.Equal(("Kit inicial", "Esmalte y lámpara", 42.5m), (kit.Text, kit.Detail, kit.MarginPercent));
        // El margen sólo tiene sentido en un producto.
        Assert.Null(Assert.Single(detail.Of(BrandItemKind.Benefit)).MarginPercent);
        Assert.Empty(detail.Of(BrandItemKind.Audience));
        BrandListItem listed = (await brands.ListAsync(CancellationToken.None)).Single(b => b.Id == brandId);
        Assert.Equal((1, 1, 0), (listed.Products, listed.ForbiddenMessages, listed.Briefs));
        Assert.Equal(["cura garantizada"], await brands.ForbiddenInAsync(brandId, ["Nada", null, "Una CURA GARANTIZADA"], CancellationToken.None));

        Assert.Equal(brandId, await brands.RemoveItemAsync(product, CancellationToken.None));
        Assert.Empty((await brands.GetAsync(brandId, CancellationToken.None)).Of(BrandItemKind.Product));
        await JobTests.AssertBusinessAsync(404, "BRAND_ITEM_NOT_FOUND", () => brands.RemoveItemAsync(product, CancellationToken.None));

        await using AsyncServiceScope otherScope = fixture.Scope(await fixture.NewOrganizationAsync());
        BrandService foreign = otherScope.ServiceProvider.GetRequiredService<BrandService>();
        Assert.Empty(await foreign.ListAsync(CancellationToken.None));
        await JobTests.AssertBusinessAsync(404, "BRAND_NOT_FOUND", () => foreign.GetAsync(brandId, CancellationToken.None));
        await JobTests.AssertBusinessAsync(404, "BRAND_NOT_FOUND", () => foreign.UpdateAsync(brandId, new BrandBody { Name = "x" }, CancellationToken.None));
        // Otra organización puede tener una marca con el mismo nombre.
        Assert.True(await foreign.CreateAsync(new BrandBody { Name = "Uñas Ya" }, CancellationToken.None) > 0);
    }

    [Fact]
    public async Task Un_brief_pertenece_a_una_marca_y_no_puede_contradecirla()
    {
        int org = await fixture.NewOrganizationAsync();
        await using AsyncServiceScope scope = fixture.Scope(org);
        BrandService brands = scope.ServiceProvider.GetRequiredService<BrandService>();
        BriefService briefs = scope.ServiceProvider.GetRequiredService<BriefService>();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        int brandId = await brands.CreateAsync(new BrandBody { Name = "Uñas Ya" }, CancellationToken.None);
        int otherBrand = await brands.CreateAsync(new BrandBody { Name = "Otra" }, CancellationToken.None);
        int product = await brands.AddItemAsync(brandId, new BrandItemBody { Kind = BrandItemKind.Product, Text = "Kit inicial" }, CancellationToken.None);
        int foreignProduct = await brands.AddItemAsync(otherBrand, new BrandItemBody { Kind = BrandItemKind.Product, Text = "Otro kit" }, CancellationToken.None);
        int benefit = await brands.AddItemAsync(brandId, new BrandItemBody { Kind = BrandItemKind.Benefit, Text = "Seca rápido" }, CancellationToken.None);
        await brands.AddItemAsync(brandId, new BrandItemBody { Kind = BrandItemKind.ForbiddenMessage, Text = "Cura garantizada" }, CancellationToken.None);
        BriefBody body = new()
        {
            BrandId = brandId,
            Name = "  Navidad 2026 ",
            OriginalText = "Quiero 20 anuncios de Navidad",
            ProductId = product,
            Occasion = "Navidad",
            Audience = "Mujeres que buscan regalos prácticos",
            Objective = "Ventas",
            Message = "El regalo que sí se usa",
            Approaches = "Emocional, demostración",
            DurationSeconds = 15,
            VideoCount = 20,
            Platform = BriefPlatform.MetaReels,
            UseExternalResearch = true,
            MaxGenerationBudgetUsd = 25,
            Autonomy = AutonomyLevel.Assisted,
        };

        long briefId = await briefs.CreateAsync(body, CancellationToken.None);

        BriefDetail detail = await briefs.GetAsync(briefId, CancellationToken.None);
        Assert.Equal(("Navidad 2026", "Uñas Ya", "Kit inicial", 20, 15), (detail.Name, detail.BrandName, detail.ProductName, detail.VideoCount, detail.DurationSeconds));
        Assert.Equal((BriefPlatform.MetaReels, true, 25m, AutonomyLevel.Assisted), (detail.Platform, detail.UseExternalResearch, detail.MaxGenerationBudgetUsd, detail.Autonomy));
        Assert.Equal("Quiero 20 anuncios de Navidad", detail.OriginalText);
        BriefListItem listed = Assert.Single(await briefs.ListAsync(CancellationToken.None));
        Assert.Equal(("Navidad 2026", "Uñas Ya", "Navidad", 20), (listed.Name, listed.BrandName, listed.Occasion, listed.VideoCount));
        Assert.Equal(1, (await brands.ListAsync(CancellationToken.None)).Single(b => b.Id == brandId).Briefs);

        // El producto tiene que ser un producto de esa marca.
        body.ProductId = foreignProduct;
        await JobTests.AssertBusinessAsync(400, "PRODUCT_INVALID", () => briefs.CreateAsync(body, CancellationToken.None));
        body.ProductId = benefit;
        await JobTests.AssertBusinessAsync(400, "PRODUCT_INVALID", () => briefs.UpdateAsync(briefId, body, CancellationToken.None));
        body.ProductId = null;

        // Un mensaje que la marca prohíbe no entra, lo escriba donde lo escriba.
        body.Message = "Con nosotras, CURA GARANTIZADA";
        BusinessException forbidden = await Assert.ThrowsAsync<BusinessException>(() => briefs.CreateAsync(body, CancellationToken.None));
        Assert.Equal(("FORBIDDEN_MESSAGE", 409), (forbidden.Code, forbidden.Status));
        Assert.Contains("Cura garantizada", forbidden.Message);
        body.Message = "El regalo que sí se usa";
        body.OriginalText = "prometé una cura   garantizada";
        await JobTests.AssertBusinessAsync(409, "FORBIDDEN_MESSAGE", () => briefs.UpdateAsync(briefId, body, CancellationToken.None));
        Assert.Equal(1, await db.CampaignBriefs.CountAsync());

        body.OriginalText = null;
        body.VideoCount = 10;
        await briefs.UpdateAsync(briefId, body, CancellationToken.None);
        BriefDetail updated = await briefs.GetAsync(briefId, CancellationToken.None);
        Assert.Equal((10, null, null), (updated.VideoCount, updated.ProductName, updated.OriginalText));

        // Un producto que un brief promociona no se puede quitar del perfil.
        body.ProductId = product;
        await briefs.UpdateAsync(briefId, body, CancellationToken.None);
        await JobTests.AssertBusinessAsync(409, "PRODUCT_IN_USE", () => brands.RemoveItemAsync(product, CancellationToken.None));

        body.BrandId = int.MaxValue;
        await JobTests.AssertBusinessAsync(404, "BRAND_NOT_FOUND", () => briefs.CreateAsync(body, CancellationToken.None));
        await using AsyncServiceScope otherScope = fixture.Scope(await fixture.NewOrganizationAsync());
        BriefService foreign = otherScope.ServiceProvider.GetRequiredService<BriefService>();
        Assert.Empty(await foreign.ListAsync(CancellationToken.None));
        await JobTests.AssertBusinessAsync(404, "BRIEF_NOT_FOUND", () => foreign.GetAsync(briefId, CancellationToken.None));
        body.BrandId = brandId;
        await JobTests.AssertBusinessAsync(404, "BRIEF_NOT_FOUND", () => foreign.UpdateAsync(briefId, body, CancellationToken.None));
        await JobTests.AssertBusinessAsync(404, "BRAND_NOT_FOUND", () => foreign.CreateAsync(body, CancellationToken.None));
    }
}

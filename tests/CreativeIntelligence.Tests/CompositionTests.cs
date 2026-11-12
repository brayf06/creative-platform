using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CreativeIntelligence.Tests;

/// <summary>Motor de composición: recetas, render real con FFmpeg, control de calidad, derechos y linaje.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class CompositionTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Renderizar_una_receta_genera_el_video_en_su_formato_pasa_el_control_y_conserva_el_linaje()
    {
        Scenario scenario = await ArrangeAsync(authorized: true);
        await using AsyncServiceScope scope = fixture.Scope(scenario.Organization);
        CompositionService composition = scope.ServiceProvider.GetRequiredService<CompositionService>();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Sólo lo elegido para producción aparece en el editor.
        Assert.Empty(await composition.AvailableAsync(CancellationToken.None));
        SegmentEditService editor = scope.ServiceProvider.GetRequiredService<SegmentEditService>();
        await editor.SetSelectedAsync(scenario.Cuts[2].Id, true, CancellationToken.None);
        await editor.SetSelectedAsync(scenario.Clip.Id, true, CancellationToken.None);
        Assert.Equal(
            new[] { scenario.Cuts[2].Id, scenario.Clip.Id }.Order(),
            (await composition.AvailableAsync(CancellationToken.None)).Select(a => a.SegmentId).Order());

        // Mezcla un original sin audio con uno con audio, y repite un video en dos tramos.
        long recipeId = await composition.CreateAsync(
            new CreateRecipeBody
            {
                Name = "  Hook + demostración  ",
                Concept = "Practicidad",
                Format = "1:1",
                Items =
                [
                    new RecipeItemBody { SegmentId = scenario.Cuts[2].Id, Role = CreativeFunction.Hook },
                    new RecipeItemBody { SegmentId = scenario.Clip.Id, Role = CreativeFunction.Demonstration },
                    new RecipeItemBody { SegmentId = scenario.Cuts[0].Id },
                ],
            },
            CancellationToken.None);
        int expectedMs = scenario.Cuts[2].EndMs - scenario.Cuts[2].StartMs + scenario.Clip.EndMs - scenario.Clip.StartMs
            + scenario.Cuts[0].EndMs - scenario.Cuts[0].StartMs;

        RecipeDetail created = await composition.GetAsync(recipeId, CancellationToken.None);
        Assert.Equal("Hook + demostración", created.Name);
        Assert.Equal((1080, 1080), (created.Width, created.Height));
        Assert.Equal(expectedMs, created.DurationMs);
        Assert.Equal([1, 2, 3], created.Items.Select(i => i.Position));
        Assert.Equal([scenario.CutsAsset, scenario.ClipAsset, scenario.CutsAsset], created.Items.Select(i => i.AssetId));
        Assert.Equal([CreativeFunction.Hook, CreativeFunction.Demonstration, null], created.Items.Select(i => i.Role));
        Assert.Equal(scenario.Cuts[2].StartMs, created.Items[0].StartMs);
        Assert.Empty(created.Variants);

        long variantId = await composition.RenderAsync(recipeId, CancellationToken.None);
        Assert.Equal(MediaProcessingStatus.Pending, (await composition.GetAsync(recipeId, CancellationToken.None)).Variants.Single().Status);
        await JobTests.AssertBusinessAsync(404, "VARIANT_NOT_FOUND", () => composition.OpenVariantAsync(variantId, CancellationToken.None));
        await DrainAsync();

        VariantView variant = (await composition.GetAsync(recipeId, CancellationToken.None)).Variants.Single();
        Assert.Equal(MediaProcessingStatus.Ready, variant.Status);
        Assert.Null(variant.Error);
        Assert.Equal((1080, 1080), (variant.Width, variant.Height));
        Assert.InRange(variant.DurationMs!.Value, expectedMs - 220, expectedMs + 220);
        Assert.Equal(["audio", "black", "duration", "resolution"], variant.Checks.Select(c => c.Rule));
        Assert.All(variant.Checks, c => Assert.True(c.Passed, c.Detail));
        Assert.True(variant.PassedQuality);
        MediaContent content = await composition.OpenVariantAsync(variantId, CancellationToken.None);
        await using (Stream stream = content.Content)
        {
            Assert.Equal("video/mp4", content.ContentType);
            Assert.Equal(variant.SizeBytes, stream.Length);
        }

        // El archivo generado es un video legible, con imagen y audio, del formato pedido.
        string path = scope.ServiceProvider.GetRequiredService<IMediaStorage>()
            .GetLocalPath(await db.CreativeVariants.Where(v => v.Id == variantId).Select(v => v.StoragePath!).SingleAsync());
        MediaProbe probe = await fixture.Services.GetRequiredService<IMediaTools>().ProbeAsync(path, CancellationToken.None);
        Assert.Equal("h264", probe.VideoCodec);
        Assert.Equal("aac", probe.AudioCodec);
        Assert.Equal(30m, probe.FrameRate);
        Assert.Equal(2, await db.AuditEntries.CountAsync(a => a.Action == "recipe.create" || a.Action == "recipe.render"));

        // Editar y volver a detectar las escenas no cambia la receta: conserva el video y el tramo, y pierde sólo
        // el puntero al fragmento que dejó de existir. Volver a renderizar produce el mismo montaje.
        await editor.MoveBoundaryAsync(scenario.Cuts[0].Id, 1500, CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<SceneService>().RequestDetectionAsync(scenario.CutsAsset, true, CancellationToken.None);
        await DrainAsync();
        RecipeDetail after = await composition.GetAsync(recipeId, CancellationToken.None);
        Assert.Equal(created.Items.Select(i => (i.AssetId, i.StartMs, i.EndMs)), after.Items.Select(i => (i.AssetId, i.StartMs, i.EndMs)));
        Assert.Equal([null, scenario.Clip.Id, null], after.Items.Select(i => i.SegmentId));

        long second = await composition.RenderAsync(recipeId, CancellationToken.None);
        await DrainAsync();
        List<VariantView> variants = (await composition.GetAsync(recipeId, CancellationToken.None)).Variants;
        Assert.Equal([second, variantId], variants.Select(v => v.Id));
        Assert.All(variants, v => Assert.True(v.PassedQuality));
        Assert.Equal(variants[1].DurationMs, variants[0].DurationMs);
        Assert.Equal((variants[1].Width, variants[1].Height), (variants[0].Width, variants[0].Height));
        RecipeListItem listed = Assert.Single(await composition.ListAsync(CancellationToken.None));
        Assert.Equal((3, expectedMs, 2, 2), (listed.Items, listed.DurationMs, listed.Variants, listed.ReadyVariants));
    }

    [Fact]
    public async Task Un_video_sin_autorizacion_no_entra_en_una_receta_ni_se_renderiza_si_la_pierde_despues()
    {
        Scenario scenario = await ArrangeAsync(authorized: false);
        await using AsyncServiceScope scope = fixture.Scope(scenario.Organization);
        CompositionService composition = scope.ServiceProvider.GetRequiredService<CompositionService>();
        MediaLibraryService media = scope.ServiceProvider.GetRequiredService<MediaLibraryService>();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        CreateRecipeBody body = new()
        {
            Name = "Receta",
            Items = [new RecipeItemBody { SegmentId = scenario.Cuts[0].Id }, new RecipeItemBody { SegmentId = scenario.Clip.Id }],
        };

        await JobTests.AssertBusinessAsync(409, "REUSE_NOT_AUTHORIZED", () => composition.CreateAsync(body, CancellationToken.None));
        await media.UpdateRightsAsync(scenario.CutsAsset, new MediaRightsBody { ReuseAuthorized = true }, CancellationToken.None);
        await JobTests.AssertBusinessAsync(409, "REUSE_NOT_AUTHORIZED", () => composition.CreateAsync(body, CancellationToken.None));
        await media.UpdateRightsAsync(scenario.ClipAsset, new MediaRightsBody { ReuseAuthorized = true }, CancellationToken.None);
        long recipeId = await composition.CreateAsync(body, CancellationToken.None);

        // Autorización vencida al pedir el render: no se crea la variante.
        await media.UpdateRightsAsync(
            scenario.ClipAsset, new MediaRightsBody { ReuseAuthorized = true, RightsExpireOn = new DateOnly(2020, 1, 1) }, CancellationToken.None);
        await JobTests.AssertBusinessAsync(409, "REUSE_NOT_AUTHORIZED", () => composition.RenderAsync(recipeId, CancellationToken.None));
        Assert.Equal(0, await db.CreativeVariants.CountAsync());
        Assert.False((await composition.GetAsync(recipeId, CancellationToken.None)).Items[1].ReuseAuthorized);

        // Autorización retirada con el render ya en la cola: el trabajo falla sin reintentos y no deja archivo.
        await media.UpdateRightsAsync(scenario.ClipAsset, new MediaRightsBody { ReuseAuthorized = true }, CancellationToken.None);
        long variantId = await composition.RenderAsync(recipeId, CancellationToken.None);
        await media.UpdateRightsAsync(scenario.CutsAsset, new MediaRightsBody { ReuseAuthorized = false }, CancellationToken.None);
        await DrainAsync();

        VariantView variant = (await composition.GetAsync(recipeId, CancellationToken.None)).Variants.Single();
        Assert.Equal(MediaProcessingStatus.Failed, variant.Status);
        Assert.Contains("cortes.mp4", variant.Error);
        Assert.Contains("autorización", variant.Error);
        Assert.False(variant.PassedQuality);
        Assert.Empty(variant.Checks);
        await JobTests.AssertBusinessAsync(404, "VARIANT_NOT_FOUND", () => composition.OpenVariantAsync(variantId, CancellationToken.None));
        JobItem job = (await scope.ServiceProvider.GetRequiredService<JobService>().ListAsync(new PageRequest(), CancellationToken.None))
            .Items.First(j => j.Type == CreativeRenderJobHandler.JobType);
        Assert.Equal(BackgroundJobStatus.Failed, job.Status);
        Assert.Equal(1, job.Attempts);
    }

    [Fact]
    public async Task Una_receta_invalida_se_rechaza_y_otra_organizacion_no_la_ve_ni_usa_sus_fragmentos()
    {
        Scenario scenario = await ArrangeAsync(authorized: true);
        int other = await fixture.NewOrganizationAsync();
        await using AsyncServiceScope scope = fixture.Scope(scenario.Organization);
        CompositionService composition = scope.ServiceProvider.GetRequiredService<CompositionService>();
        RecipeItemBody item = new() { SegmentId = scenario.Clip.Id };

        await JobTests.AssertBusinessAsync(
            400, "UNKNOWN_FORMAT", () => composition.CreateAsync(new CreateRecipeBody { Name = "x", Format = "3:2", Items = [item] }, CancellationToken.None));
        await JobTests.AssertBusinessAsync(
            400, "RECIPE_INVALID", () => composition.CreateAsync(new CreateRecipeBody { Name = "x" }, CancellationToken.None));
        await JobTests.AssertBusinessAsync(
            400, "RECIPE_INVALID",
            () => composition.CreateAsync(
                new CreateRecipeBody { Name = "x", Items = Enumerable.Repeat(item, CompositionRules.MaxItems + 1).ToList() }, CancellationToken.None));
        await JobTests.AssertBusinessAsync(
            404, "SEGMENT_NOT_FOUND",
            () => composition.CreateAsync(
                new CreateRecipeBody { Name = "x", Items = [item, new RecipeItemBody { SegmentId = long.MaxValue }] }, CancellationToken.None));
        long recipeId = await composition.CreateAsync(new CreateRecipeBody { Name = "Propia", Items = [item, item] }, CancellationToken.None);
        long variantId = await composition.RenderAsync(recipeId, CancellationToken.None);
        await DrainAsync();
        Assert.True((await composition.GetAsync(recipeId, CancellationToken.None)).Variants.Single().PassedQuality);

        await using AsyncServiceScope otherScope = fixture.Scope(other);
        CompositionService foreign = otherScope.ServiceProvider.GetRequiredService<CompositionService>();
        Assert.Empty(await foreign.ListAsync(CancellationToken.None));
        Assert.Empty(await foreign.AvailableAsync(CancellationToken.None));
        await JobTests.AssertBusinessAsync(404, "RECIPE_NOT_FOUND", () => foreign.GetAsync(recipeId, CancellationToken.None));
        await JobTests.AssertBusinessAsync(404, "RECIPE_NOT_FOUND", () => foreign.RenderAsync(recipeId, CancellationToken.None));
        await JobTests.AssertBusinessAsync(404, "VARIANT_NOT_FOUND", () => foreign.OpenVariantAsync(variantId, CancellationToken.None));
        await JobTests.AssertBusinessAsync(
            404, "SEGMENT_NOT_FOUND", () => foreign.CreateAsync(new CreateRecipeBody { Name = "Ajena", Items = [item] }, CancellationToken.None));
    }

    [Theory]
    [InlineData(5500, 1080, 1920, "aac", true, true, true)]
    [InlineData(5719, 1080, 1920, "aac", true, true, true)]
    [InlineData(5721, 1080, 1920, "aac", false, true, true)]
    [InlineData(null, 1080, 1920, "aac", false, true, true)]
    [InlineData(5500, 1920, 1080, "aac", true, false, true)]
    [InlineData(5500, 1080, 1920, null, true, true, false)]
    public void El_control_de_calidad_compara_el_archivo_con_lo_que_pedia_la_receta(
        int? durationMs, int width, int height, string? audioCodec, bool durationOk, bool resolutionOk, bool audioOk)
    {
        MediaProbe probe = new(durationMs, width, height, 30m, "h264", audioCodec, "mov,mp4");

        List<QualityResult> results = CompositionRules.Evaluate(5500, 3, 1080, 1920, probe);

        Assert.Equal(["duration", "resolution", "audio"], results.Select(r => r.Rule));
        Assert.Equal([durationOk, resolutionOk, audioOk], results.Select(r => r.Passed));
        Assert.All(results, r => Assert.NotEmpty(r.Detail));
    }


    // Una organización con el video de tres cortes (sin audio) y el clip de una escena (con audio), ya detectados.
    private async Task<Scenario> ArrangeAsync(bool authorized)
    {
        await fixture.ClearJobsAsync();
        int org = await fixture.NewOrganizationAsync();
        await using AsyncServiceScope scope = fixture.Scope(org);
        MediaLibraryService media = scope.ServiceProvider.GetRequiredService<MediaLibraryService>();
        MediaRightsBody rights = new() { ReuseAuthorized = authorized };
        long cuts;
        long clip;
        await using (FileStream stream = File.OpenRead(fixture.SampleCutsVideoPath))
        {
            cuts = (await media.IngestAsync("cortes.mp4", rights, stream, CancellationToken.None)).AssetId;
        }

        await using (FileStream stream = File.OpenRead(fixture.SampleVideoPath))
        {
            clip = (await media.IngestAsync("clip.mp4", rights, stream, CancellationToken.None)).AssetId;
        }

        await DrainAsync();
        SceneService scenes = scope.ServiceProvider.GetRequiredService<SceneService>();
        List<SegmentItem> cutScenes = (await scenes.ListAsync(cuts, CancellationToken.None)).Segments;
        Assert.Equal(3, cutScenes.Count);
        SegmentItem clipScene = Assert.Single((await scenes.ListAsync(clip, CancellationToken.None)).Segments);
        return new Scenario(org, cuts, clip, cutScenes, clipScene);
    }

    private async Task DrainAsync()
    {
        JobRunner runner = fixture.Services.GetRequiredService<JobRunner>();
        while (await runner.ProcessNextAsync(CancellationToken.None))
        {
        }
    }

    private sealed record Scenario(int Organization, long CutsAsset, long ClipAsset, List<SegmentItem> Cuts, SegmentItem Clip);
}

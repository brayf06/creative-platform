using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CreativeIntelligence.Tests;

/// <summary>Recorte de fragmentos dentro de una receta, duplicado de recetas, y cancelación y reintento de lotes.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class RecipeEditingTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Un_fragmento_se_puede_usar_recortado_pero_solo_dentro_de_sus_limites()
    {
        Scenario scenario = await ArrangeAsync();
        await using AsyncServiceScope scope = fixture.Scope(scenario.Organization);
        CompositionService composition = scope.ServiceProvider.GetRequiredService<CompositionService>();
        SegmentItem second = scenario.Cuts[1];

        long recipeId = await composition.CreateAsync(
            new CreateRecipeBody
            {
                Name = "Recortada",
                Format = "1:1",
                Items =
                [
                    new RecipeItemBody { SegmentId = second.Id, StartMs = second.StartMs + 500, EndMs = second.EndMs - 300 },
                    new RecipeItemBody { SegmentId = scenario.Cuts[0].Id, EndMs = 800 },
                ],
            },
            CancellationToken.None);

        RecipeDetail recipe = await composition.GetAsync(recipeId, CancellationToken.None);
        Assert.Equal([(second.StartMs + 500, second.EndMs - 300), (0, 800)], recipe.Items.Select(i => (i.StartMs, i.EndMs)));
        int expected = second.EndMs - second.StartMs - 800 + 800;
        Assert.Equal(expected, recipe.DurationMs);
        await composition.RenderAsync(recipeId, CancellationToken.None);
        await DrainAsync();
        VariantView video = (await composition.GetAsync(recipeId, CancellationToken.None)).Variants.Single();
        Assert.True(video.PassedQuality, video.Error);
        Assert.InRange(video.DurationMs!.Value, expected - 180, expected + 180);

        // Fuera del fragmento (antes, después) o demasiado corto.
        foreach (RecipeItemBody invalid in new[]
                 {
                     new RecipeItemBody { SegmentId = second.Id, StartMs = second.StartMs - 1 },
                     new RecipeItemBody { SegmentId = second.Id, EndMs = second.EndMs + 1 },
                     new RecipeItemBody { SegmentId = second.Id, StartMs = second.StartMs + 100, EndMs = second.StartMs + 100 + CompositionRules.MinTrimMs - 1 },
                     new RecipeItemBody { SegmentId = second.Id, StartMs = second.EndMs - 100, EndMs = second.StartMs + 100 },
                 })
        {
            await JobTests.AssertBusinessAsync(
                400, "RECIPE_INVALID", () => composition.CreateAsync(new CreateRecipeBody { Name = "x", Items = [invalid] }, CancellationToken.None));
        }
    }

    [Fact]
    public async Task Duplicar_una_receta_trae_sus_datos_ajustados_a_como_estan_hoy_los_fragmentos()
    {
        Scenario scenario = await ArrangeAsync();
        await using AsyncServiceScope scope = fixture.Scope(scenario.Organization);
        CompositionService composition = scope.ServiceProvider.GetRequiredService<CompositionService>();
        long recipeId = await composition.CreateAsync(
            new CreateRecipeBody
            {
                Name = "Original",
                Concept = "Practicidad",
                Format = "4:5",
                Transition = RecipeTransition.FadeBlack,
                Subtitles = true,
                MusicVolumePercent = 60,
                Overlays = [new OverlayBody { Text = "Envío gratis", Placement = OverlayPlacement.Bottom, Moment = OverlayMoment.Closing }],
                Items =
                [
                    new RecipeItemBody { SegmentId = scenario.Cuts[0].Id, StartMs = 200, EndMs = 1800, Role = CreativeFunction.Hook },
                    new RecipeItemBody { SegmentId = scenario.Cuts[2].Id },
                ],
            },
            CancellationToken.None);

        RecipeDraft draft = await composition.DraftAsync(recipeId, CancellationToken.None);
        Assert.Equal(("Copia de Original", "Practicidad", "4:5", RecipeTransition.FadeBlack, true, 60), (draft.Name, draft.Concept, draft.Format, draft.Transition, draft.Subtitles, draft.MusicVolumePercent));
        Assert.Equal(new OverlayView("Envío gratis", OverlayPlacement.Bottom, OverlayMoment.Closing), Assert.Single(draft.Overlays));
        Assert.Equal(0, draft.Dropped);
        Assert.Equal([(200, 1800), (scenario.Cuts[2].StartMs, scenario.Cuts[2].EndMs)], draft.Items.Select(i => (i.StartMs, i.EndMs)));
        Assert.Equal((0, 2000, CreativeFunction.Hook), (draft.Items[0].SegmentStartMs, draft.Items[0].SegmentEndMs, draft.Items[0].Role));

        // El borrador se guarda como otra receta; la original no cambia.
        long copyId = await composition.CreateAsync(
            new CreateRecipeBody
            {
                Name = draft.Name,
                Format = draft.Format,
                Items = draft.Items.Select(i => new RecipeItemBody { SegmentId = i.SegmentId!.Value, StartMs = i.StartMs, EndMs = i.EndMs, Role = i.Role }).ToList(),
            },
            CancellationToken.None);
        Assert.NotEqual(recipeId, copyId);
        Assert.Equal(
            (await composition.GetAsync(recipeId, CancellationToken.None)).Items.Select(i => (i.AssetId, i.StartMs, i.EndMs)),
            (await composition.GetAsync(copyId, CancellationToken.None)).Items.Select(i => (i.AssetId, i.StartMs, i.EndMs)));

        // La primera escena ahora termina antes: el tramo se ajusta a lo que hoy abarca.
        await scope.ServiceProvider.GetRequiredService<SegmentEditService>().MoveBoundaryAsync(scenario.Cuts[0].Id, 1500, CancellationToken.None);
        RecipeDraft adjusted = await composition.DraftAsync(recipeId, CancellationToken.None);
        Assert.Equal((200, 1500), (adjusted.Items[0].StartMs, adjusted.Items[0].EndMs));
        Assert.Equal(0, adjusted.Dropped);

        // Al volver a detectar, los fragmentos son otros: no se pueden traer y se informa.
        await scope.ServiceProvider.GetRequiredService<SceneService>().RequestDetectionAsync(scenario.Asset, true, CancellationToken.None);
        await DrainAsync();
        RecipeDraft lost = await composition.DraftAsync(recipeId, CancellationToken.None);
        Assert.Empty(lost.Items);
        Assert.Equal(2, lost.Dropped);

        await using AsyncServiceScope otherScope = fixture.Scope(await fixture.NewOrganizationAsync());
        await JobTests.AssertBusinessAsync(
            404, "RECIPE_NOT_FOUND", () => otherScope.ServiceProvider.GetRequiredService<CompositionService>().DraftAsync(recipeId, CancellationToken.None));
    }

    [Fact]
    public async Task Un_lote_se_puede_cancelar_y_despues_reintentar_lo_que_no_salio()
    {
        Scenario scenario = await ArrangeAsync();
        await using AsyncServiceScope scope = fixture.Scope(scenario.Organization);
        BatchService batches = scope.ServiceProvider.GetRequiredService<BatchService>();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        long batchId = await batches.CreateAsync(
            new CreateBatchBody
            {
                Name = "Lote",
                Format = "1:1",
                Count = 5,
                Items = scenario.Cuts.Select(c => new RecipeItemBody { SegmentId = c.Id }).ToList(),
            },
            CancellationToken.None);
        Assert.Equal(0, await batches.RetryFailedAsync(batchId, CancellationToken.None));

        Assert.Equal(5, await batches.CancelAsync(batchId, CancellationToken.None));

        BatchDetail cancelled = await batches.GetAsync(batchId, CancellationToken.None);
        Assert.Equal((0, 5, 100), (cancelled.Ready, cancelled.Failed, cancelled.Percent));
        Assert.All(cancelled.Variants, v => Assert.Equal(BatchService.CancelledMessage, v.Video!.Error));
        Assert.Equal(5, await db.BackgroundJobs.CountAsync(j => j.Type == CreativeRenderJobHandler.JobType && j.Status == BackgroundJobStatus.Cancelled));
        Assert.Equal(0, await batches.CancelAsync(batchId, CancellationToken.None));
        // Nada cancelado se renderiza después.
        await DrainAsync();
        Assert.Equal(0, (await batches.GetAsync(batchId, CancellationToken.None)).Ready);

        Assert.Equal(5, await batches.RetryFailedAsync(batchId, CancellationToken.None));
        Assert.Equal((0, 0), ((await batches.GetAsync(batchId, CancellationToken.None)).Ready, (await batches.GetAsync(batchId, CancellationToken.None)).Failed));
        await DrainAsync();

        BatchDetail done = await batches.GetAsync(batchId, CancellationToken.None);
        Assert.Equal((5, 0, 5), (done.Ready, done.Failed, done.PassedQuality));
        Assert.Equal(10, await db.CreativeVariants.CountAsync());
        Assert.Equal(1, await db.AuditEntries.CountAsync(a => a.Action == "batch.cancel"));
        Assert.Equal(1, await db.AuditEntries.CountAsync(a => a.Action == "batch.retry"));

        await using AsyncServiceScope otherScope = fixture.Scope(await fixture.NewOrganizationAsync());
        BatchService foreign = otherScope.ServiceProvider.GetRequiredService<BatchService>();
        await JobTests.AssertBusinessAsync(404, "BATCH_NOT_FOUND", () => foreign.CancelAsync(batchId, CancellationToken.None));
        await JobTests.AssertBusinessAsync(404, "BATCH_NOT_FOUND", () => foreign.RetryFailedAsync(batchId, CancellationToken.None));
    }

    // Una organización con el video de tres cortes, autorizado y con sus escenas detectadas.
    private async Task<Scenario> ArrangeAsync()
    {
        await fixture.ClearJobsAsync();
        int org = await fixture.NewOrganizationAsync();
        await using AsyncServiceScope scope = fixture.Scope(org);
        long asset;
        await using (FileStream stream = File.OpenRead(fixture.SampleCutsVideoPath))
        {
            asset = (await scope.ServiceProvider.GetRequiredService<MediaLibraryService>()
                .IngestAsync("cortes.mp4", new MediaRightsBody { ReuseAuthorized = true }, stream, CancellationToken.None)).AssetId;
        }

        await DrainAsync();
        List<SegmentItem> cuts = (await scope.ServiceProvider.GetRequiredService<SceneService>().ListAsync(asset, CancellationToken.None)).Segments;
        Assert.Equal(3, cuts.Count);
        return new Scenario(org, asset, cuts);
    }

    private async Task DrainAsync()
    {
        JobRunner runner = fixture.Services.GetRequiredService<JobRunner>();
        while (await runner.ProcessNextAsync(CancellationToken.None))
        {
        }
    }

    private sealed record Scenario(int Organization, long Asset, List<SegmentItem> Cuts);
}

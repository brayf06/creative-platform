using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CreativeIntelligence.Tests;

/// <summary>Generador de variantes (función pura) y lotes de creatividades con render real.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class BatchTests(DatabaseFixture fixture)
{
    private static readonly PoolFragment A = new(1, 2000, null);
    private static readonly PoolFragment B = new(2, 2000, null);
    private static readonly PoolFragment C = new(3, 1500, null);
    private static readonly PoolFragment D = new(4, 3000, null);

    [Fact]
    public void La_similitud_pesa_el_material_compartido_y_el_orden()
    {
        Assert.Equal(1m, VariantGenerator.Similarity([A, B, C], [A, B, C]));
        Assert.Equal(0m, VariantGenerator.Similarity([A, B], [C, D]));
        // Los mismos fragmentos en otro orden: todo el material en común, ninguna posición igual.
        Assert.Equal(0.7m, VariantGenerator.Similarity([A, B], [B, A]));
        // Comparten A (2 s de 5,5 s) en la misma posición, de dos.
        Assert.Equal((0.7m * 2000 / 5500) + 0.15m, VariantGenerator.Similarity([A, B], [A, C]));
        Assert.Equal(0m, VariantGenerator.Similarity([], [A]));
        Assert.Equal(VariantGenerator.Similarity([A, B, D], [A, C]), VariantGenerator.Similarity([A, C], [A, B, D]));
    }

    [Fact]
    public void El_generador_respeta_la_estructura_la_duracion_y_el_umbral_y_es_reproducible()
    {
        List<PoolFragment> pool =
        [
            new PoolFragment(1, 2000, CreativeFunction.Hook), new PoolFragment(2, 1500, CreativeFunction.Hook),
            new PoolFragment(3, 3000, CreativeFunction.Demonstration), new PoolFragment(4, 2500, CreativeFunction.Benefit),
            new PoolFragment(5, 4000, null), new PoolFragment(6, 2000, CreativeFunction.SocialProof),
            new PoolFragment(7, 1500, CreativeFunction.Cta), new PoolFragment(8, 2000, CreativeFunction.Offer),
        ];

        GeneratedPlan plan = VariantGenerator.Generate(pool, 20, 12_000, 0.8m, 7);

        Assert.Equal(20, plan.Variants.Count);
        Assert.All(plan.Variants, v =>
        {
            Assert.Equal(CreativeFunction.Hook, v[0].Role);
            Assert.Contains(v[^1].Role, new CreativeFunction?[] { CreativeFunction.Cta, CreativeFunction.Offer });
            Assert.InRange(v.Count, 2, 2 + VariantGenerator.MaxBodyFragments);
            Assert.All(v.Skip(1).SkipLast(1), f => Assert.Contains(f.SegmentId, new long[] { 3, 4, 5, 6 }));
            Assert.Equal(v.Count, v.Select(f => f.SegmentId).Distinct().Count());
            Assert.True(v.Sum(f => f.DurationMs) <= 12_000);
        });
        for (int i = 0; i < plan.Variants.Count; i++)
        {
            for (int j = i + 1; j < plan.Variants.Count; j++)
            {
                Assert.True(VariantGenerator.Similarity(plan.Variants[i], plan.Variants[j]) < 0.8m);
            }
        }

        GeneratedPlan again = VariantGenerator.Generate(pool, 20, 12_000, 0.8m, 7);
        Assert.Equal(plan.Variants.Select(Key), again.Variants.Select(Key));
        Assert.NotEqual(plan.Variants.Select(Key), VariantGenerator.Generate(pool, 20, 12_000, 0.8m, 8).Variants.Select(Key));
    }

    [Fact]
    public void Con_pocos_fragmentos_salen_menos_variantes_y_un_umbral_mas_bajo_descarta_mas()
    {
        // Dos fragmentos sin función: sólo existen A→B y B→A.
        GeneratedPlan two = VariantGenerator.Generate([A, B], 10, 60_000, 0.8m, 1);
        Assert.Equal(2, two.Variants.Count);
        Assert.True(two.Discarded > 0);
        // Con el umbral en 0,7 el orden inverso ya cuenta como parecido.
        Assert.Single(VariantGenerator.Generate([A, B], 10, 60_000, 0.7m, 1).Variants);
        // Nada entra en la duración máxima.
        Assert.Empty(VariantGenerator.Generate([A, B], 10, 1000, 0.8m, 1).Variants);
        // Sin hook ni cierre, cada variante lleva al menos dos fragmentos.
        Assert.All(VariantGenerator.Generate([A, B, C, D], 10, 60_000, 0.9m, 1).Variants, v => Assert.True(v.Count >= 2));
    }

    [Fact]
    public async Task Un_lote_genera_sus_variantes_las_renderiza_y_muestra_el_avance()
    {
        Scenario scenario = await ArrangeAsync(authorized: true);
        await using AsyncServiceScope scope = fixture.Scope(scenario.Organization);
        BatchService batches = scope.ServiceProvider.GetRequiredService<BatchService>();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        CreateBatchBody body = new()
        {
            Name = "Lanzamiento",
            Brief = "Probar dos hooks",
            Format = "1:1",
            Count = 5,
            MaxDurationSeconds = 6,
            SimilarityPercent = 90,
            Items =
            [
                new RecipeItemBody { SegmentId = scenario.Segments[0], Role = CreativeFunction.Hook },
                new RecipeItemBody { SegmentId = scenario.Segments[3], Role = CreativeFunction.Hook },
                new RecipeItemBody { SegmentId = scenario.Segments[1] },
                new RecipeItemBody { SegmentId = scenario.Segments[2] },
                new RecipeItemBody { SegmentId = scenario.Segments[2], Role = CreativeFunction.Cta },
            ],
        };

        BatchPreview preview = await batches.PreviewAsync(body, CancellationToken.None);
        Assert.Equal(5, preview.Variants.Count);
        Assert.Equal(0, await db.CreativeBatches.CountAsync());

        long batchId = await batches.CreateAsync(body, CancellationToken.None);

        BatchDetail queued = await batches.GetAsync(batchId, CancellationToken.None);
        Assert.Equal(5, queued.Variants.Count);
        Assert.Equal((0, 0, 0), (queued.Ready, queued.Failed, queued.Percent));
        Assert.All(queued.Variants, v => Assert.Equal(MediaProcessingStatus.Pending, v.Video!.Status));
        Assert.Equal(["Lanzamiento · 01", "Lanzamiento · 02", "Lanzamiento · 03", "Lanzamiento · 04", "Lanzamiento · 05"], queued.Variants.Select(v => v.RecipeName));
        // Lo creado es lo que mostró la vista previa, en el mismo orden.
        List<List<long?>> created = [];
        foreach (BatchVariantView variant in queued.Variants)
        {
            created.Add(await db.RecipeItems.Where(i => i.CreativeRecipeId == variant.RecipeId).OrderBy(i => i.Position).Select(i => i.MediaSegmentId).ToListAsync());
        }

        Assert.Equal(preview.Variants.Select(v => string.Join(",", v.SegmentIds)), created.Select(c => string.Join(",", c)));
        Assert.All(created, c => Assert.Contains(c[0], new long?[] { scenario.Segments[0], scenario.Segments[3] }));
        Assert.Equal(5, created.Select(c => string.Join(",", c)).Distinct().Count());

        await DrainAsync();

        BatchDetail done = await batches.GetAsync(batchId, CancellationToken.None);
        Assert.Equal((5, 0, 5, 100), (done.Ready, done.Failed, done.PassedQuality, done.Percent));
        Assert.All(done.Variants, v => Assert.Equal((1080, 1080), (v.Video!.Width, v.Video.Height)));
        Assert.All(done.Variants, v => Assert.True(v.Video!.DurationMs <= 6200));
        Assert.All(done.Variants, v => Assert.True(v.Video!.RenderMs > 0));
        Assert.True(done.RenderMs > 0);
        BatchListItem listed = Assert.Single(await batches.ListAsync(CancellationToken.None));
        Assert.Equal((5, 5, 5, 0), (listed.RequestedCount, listed.Total, listed.Ready, listed.Failed));
        Assert.Equal(1, await db.AuditEntries.CountAsync(a => a.Action == "batch.create"));
        Assert.Equal(5, await db.CreativeRecipes.CountAsync(r => r.CreativeBatchId == batchId));
    }

    [Fact]
    public async Task Un_lote_sin_variedad_o_sin_autorizacion_no_se_crea_y_otra_organizacion_no_lo_ve()
    {
        Scenario scenario = await ArrangeAsync(authorized: false);
        int other = await fixture.NewOrganizationAsync();
        await using AsyncServiceScope scope = fixture.Scope(scenario.Organization);
        BatchService batches = scope.ServiceProvider.GetRequiredService<BatchService>();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        CreateBatchBody body = new()
        {
            Name = "Lote",
            Count = 5,
            Items = scenario.Segments.Select(id => new RecipeItemBody { SegmentId = id }).ToList(),
        };

        await JobTests.AssertBusinessAsync(409, "REUSE_NOT_AUTHORIZED", () => batches.PreviewAsync(body, CancellationToken.None));
        await JobTests.AssertBusinessAsync(409, "REUSE_NOT_AUTHORIZED", () => batches.CreateAsync(body, CancellationToken.None));
        MediaLibraryService media = scope.ServiceProvider.GetRequiredService<MediaLibraryService>();
        foreach (long asset in scenario.Assets)
        {
            await media.UpdateRightsAsync(asset, new MediaRightsBody { ReuseAuthorized = true }, CancellationToken.None);
        }

        CreateBatchBody tooFew = new() { Name = "Lote", Count = 5, Items = body.Items.Take(2).ToList() };
        Assert.Equal(2, (await batches.PreviewAsync(tooFew, CancellationToken.None)).Variants.Count);
        await JobTests.AssertBusinessAsync(409, "NOT_ENOUGH_VARIETY", () => batches.CreateAsync(tooFew, CancellationToken.None));
        await JobTests.AssertBusinessAsync(
            400, "UNKNOWN_FORMAT",
            () => batches.PreviewAsync(new CreateBatchBody { Name = "Lote", Format = "3:2", Items = body.Items }, CancellationToken.None));
        Assert.Equal(0, await db.CreativeBatches.CountAsync());
        Assert.Equal(0, await db.CreativeRecipes.CountAsync());

        // Si los fragmentos dan para menos que lo pedido pero al menos cinco, el lote se crea con lo que hay.
        CreateBatchBody many = new() { Name = "Lote", Count = 50, MaxDurationSeconds = 4, SimilarityPercent = 60, Items = body.Items };
        int possible = (await batches.PreviewAsync(many, CancellationToken.None)).Variants.Count;
        Assert.InRange(possible, 5, 49);
        long batchId = await batches.CreateAsync(many, CancellationToken.None);
        BatchDetail detail = await batches.GetAsync(batchId, CancellationToken.None);
        Assert.Equal((50, possible), (detail.RequestedCount, detail.Variants.Count));
        Assert.True(detail.DiscardedBySimilarity > 0);
        await fixture.ClearJobsAsync();

        await using AsyncServiceScope otherScope = fixture.Scope(other);
        BatchService foreign = otherScope.ServiceProvider.GetRequiredService<BatchService>();
        Assert.Empty(await foreign.ListAsync(CancellationToken.None));
        await JobTests.AssertBusinessAsync(404, "BATCH_NOT_FOUND", () => foreign.GetAsync(batchId, CancellationToken.None));
        await JobTests.AssertBusinessAsync(404, "SEGMENT_NOT_FOUND", () => foreign.PreviewAsync(body, CancellationToken.None));
    }

    private static string Key(List<PoolFragment> variant)
    {
        return string.Join(",", variant.Select(f => f.SegmentId));
    }

    // Una organización con cuatro fragmentos: las tres escenas del video de cortes y la del clip con audio.
    private async Task<Scenario> ArrangeAsync(bool authorized)
    {
        await fixture.ClearJobsAsync();
        int org = await fixture.NewOrganizationAsync();
        await using AsyncServiceScope scope = fixture.Scope(org);
        MediaLibraryService media = scope.ServiceProvider.GetRequiredService<MediaLibraryService>();
        MediaRightsBody rights = new() { ReuseAuthorized = authorized };
        List<long> assets = [];
        foreach (string path in new[] { fixture.SampleCutsVideoPath, fixture.SampleVideoPath })
        {
            await using FileStream stream = File.OpenRead(path);
            assets.Add((await media.IngestAsync(Path.GetFileName(path), rights, stream, CancellationToken.None)).AssetId);
        }

        await DrainAsync();
        SceneService scenes = scope.ServiceProvider.GetRequiredService<SceneService>();
        List<long> segments = [];
        foreach (long asset in assets)
        {
            segments.AddRange((await scenes.ListAsync(asset, CancellationToken.None)).Segments.Select(s => s.Id));
        }

        Assert.Equal(4, segments.Count);
        return new Scenario(org, assets, segments);
    }

    private async Task DrainAsync()
    {
        JobRunner runner = fixture.Services.GetRequiredService<JobRunner>();
        while (await runner.ProcessNextAsync(CancellationToken.None))
        {
        }
    }

    private sealed record Scenario(int Organization, List<long> Assets, List<long> Segments);
}

using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CreativeIntelligence.Tests;

/// <summary>Planificador falso: propone los conceptos pedidos usando los fragmentos disponibles, cuenta las llamadas
/// y puede fallar a pedido. Los tests automáticos nunca llaman a un proveedor pago.</summary>
public sealed class FakeConceptPlanner : IConceptPlanner
{
    public bool IsConfigured { get; set; } = true;

    public string Model => "planificador-falso";

    public int Calls { get; private set; }

    public decimal CostPerCall { get; set; } = 0.01m;

    public Queue<AiProviderException> Errors { get; } = new();

    public PlanRequest? LastRequest { get; private set; }

    /// <summary>Si se define, es lo que devuelve en lugar de los conceptos por defecto.</summary>
    public List<ConceptProposal>? Proposals { get; set; }

    public void Reset()
    {
        IsConfigured = true;
        Calls = 0;
        CostPerCall = 0.01m;
        Errors.Clear();
        LastRequest = null;
        Proposals = null;
    }

    public AnalysisCost Estimate(int inputCharacters, int concepts)
    {
        return new AnalysisCost(inputCharacters / 3, concepts * 450, CostPerCall);
    }

    public Task<PlanOutcome> PlanAsync(PlanRequest request, CancellationToken cancellationToken)
    {
        Calls++;
        LastRequest = request;
        if (Errors.TryDequeue(out AiProviderException? error))
        {
            throw error;
        }

        int available = request.Fragments.Count;
        List<ConceptProposal> concepts = Proposals ?? Enumerable.Range(1, request.Count)
            .Select(i => Concept(i, available == 0 ? [] : [((i - 1) % available) + 1, (i % available) + 1], available == 0 ? ["Toma del producto en uso"] : []))
            .ToList();
        return Task.FromResult(new PlanOutcome(concepts, "falso", Model, ConceptPlanPrompt.Version, 2000, 3000, CostPerCall, 55));
    }

    public static ConceptProposal Concept(int number, List<int> fragments, List<string> missing, string? script = null)
    {
        return new ConceptProposal(
            $"Concepto {number}", $"Si mostramos el resultado primero, más gente mira hasta el final ({number})", "Demostración",
            $"Hook {number}", script ?? $"Guion del concepto {number}", "Muestra el producto en uso", "Pedilo hoy", "Mujeres 25-45",
            "Tomas del producto", "Coincide con el brief", "Puede parecerse a otros", 3, fragments, missing);
    }
}

[Collection(DatabaseCollection.Name)]
public sealed class PlanTests(DatabaseFixture fixture)
{
    [Fact]
    public void Las_propuestas_del_planificador_se_filtran_antes_de_guardarse()
    {
        List<PlanSource> sources =
        [
            new PlanSource { SegmentId = 11, AssetId = 1, StartMs = 0, EndMs = 2000 },
            new PlanSource { SegmentId = 12, AssetId = 1, StartMs = 2000, EndMs = 4000 },
        ];
        List<ConceptProposal> proposals =
        [
            FakeConceptPlanner.Concept(1, [2, 1, 2, 0, 99, -3], ["  Toma de manos  ", " "]),
            FakeConceptPlanner.Concept(1, [1], []),
            FakeConceptPlanner.Concept(2, [], ["Testimonio"], "Prometemos una CURA  garantizada"),
            FakeConceptPlanner.Concept(3, [1], []) with { Title = "  ", Hook = "otro" },
            FakeConceptPlanner.Concept(4, [1], []) with { Novelty = 9 },
            FakeConceptPlanner.Concept(5, [1], []),
        ];

        List<CreativeConcept> concepts = StrategyPlanJobHandler.BuildConcepts(proposals, sources, ["cura garantizada", "gratis"], 3);

        // Entran tres: el segundo repite el gancho del primero y el cuarto no tiene título.
        Assert.Equal(["Concepto 1", "Concepto 2", "Concepto 4"], concepts.Select(c => c.Title));
        Assert.Equal([1, 2, 3], concepts.Select(c => c.Position));
        // Sólo los números que existen, sin repetir y en el orden propuesto.
        Assert.Equal([(12L, 1), (11L, 2)], concepts[0].Fragments.Select(f => (f.MediaSegmentId!.Value, f.Position)));
        Assert.Equal("Toma de manos", concepts[0].MissingText);
        Assert.Null(concepts[0].ViolationsText);
        Assert.Equal("cura garantizada", concepts[1].ViolationsText);
        Assert.Empty(concepts[1].Fragments);
        Assert.Equal(5, concepts[2].Novelty);
    }

    [Fact]
    public void El_pedido_al_planificador_lleva_la_marca_el_brief_los_fragmentos_y_lo_prohibido()
    {
        BrandDetail brand = new(
            1, "Uñas Ya", "Uñas de salón en casa", "Cercano", null, null, null, null,
            [
                new BrandItemView(1, BrandItemKind.Benefit, "Seca en 60 segundos", "Medido con lámpara", null),
                new BrandItemView(2, BrandItemKind.ForbiddenMessage, "Dura para siempre", null, null),
            ]);
        BriefDetail brief = new() { Name = "Navidad", BrandId = 1, Occasion = "Navidad", DurationSeconds = 15, OriginalText = "Quiero 20 anuncios", Platform = BriefPlatform.MetaReels };
        List<PlanFragment> fragments = [new PlanFragment(1, 2300, [CreativeFunction.Hook, CreativeFunction.Problem], "Una mano muestra uñas rotas")];

        string text = ConceptPlanPrompt.BuildInput(new PlanRequest(brand, brief, fragments, 20));

        Assert.Contains("# Marca: Uñas Ya", text);
        Assert.Contains("Propuesta de valor: Uñas de salón en casa", text);
        Assert.Contains("- Seca en 60 segundos (Medido con lámpara)", text);
        Assert.Contains("MENSAJES PROHIBIDOS (no usar ni reformular):\n- Dura para siempre", text);
        Assert.Contains("Texto original: Quiero 20 anuncios", text);
        Assert.Contains("Duración de cada video: 15 segundos", text);
        Assert.Contains("1. 2.3 s · Hook, Problem · Una mano muestra uñas rotas", text);
        Assert.EndsWith("Proponé exactamente 20 conceptos distintos.", text);
        Assert.DoesNotContain("Identidad visual", text);
        Assert.Contains("No hay fragmentos disponibles", ConceptPlanPrompt.BuildInput(new PlanRequest(brand, brief, [], 5)));
    }

    [Fact]
    public async Task El_cliente_de_Gemini_pide_el_plan_como_texto_y_lee_los_conceptos()
    {
        const string response =
            """
            {
              "status": "completed",
              "steps": [ { "type": "model_output", "content": [ { "type": "text", "text": "{\"concepts\":[{\"title\":\"El regalo que se usa\",\"hypothesis\":\"H\",\"approach\":\"Emocional\",\"hook\":\"¿Otro regalo que queda en el cajón?\",\"script\":\"G\",\"development\":\"D\",\"cta\":\"Pedilo\",\"audience\":\"Mujeres\",\"required_material\":\"M\",\"reason\":\"R\",\"risks\":\"X\",\"novelty\":4,\"fragments\":[2,1],\"missing\":[\"Toma navideña\"]}]}" } ] } ],
              "usage": { "total_input_tokens": 1000, "total_output_tokens": 800, "total_thought_tokens": 200 }
            }
            """;
        PlanStub handler = new(HttpStatusCode.OK, response);
        IOptions<GeminiOptions> options = Options.Create(new GeminiOptions { ApiKey = "clave-de-prueba" });
        GeminiConceptPlanner planner = new(new GeminiInteractions(new HttpClient(handler), options), options);
        PlanRequest request = new(new BrandDetail(1, "Marca", null, null, null, null, null, null, []), new BriefDetail { Name = "Brief" }, [], 1);

        PlanOutcome outcome = await planner.PlanAsync(request, CancellationToken.None);

        using JsonDocument body = JsonDocument.Parse(handler.Body);
        JsonElement root = body.RootElement;
        Assert.Equal("gemini-3.5-flash-lite", root.GetProperty("model").GetString());
        Assert.Equal(ConceptPlanPrompt.SystemInstruction, root.GetProperty("system_instruction").GetString());
        Assert.Equal("medium", root.GetProperty("generation_config").GetProperty("thinking_level").GetString());
        Assert.False(root.GetProperty("store").GetBoolean());
        JsonElement part = Assert.Single(root.GetProperty("input").EnumerateArray().ToArray());
        Assert.Equal("text", part.GetProperty("type").GetString());
        Assert.Contains("# Marca: Marca", part.GetProperty("text").GetString());
        Assert.True(root.GetProperty("response_format").GetProperty("schema").GetProperty("properties").TryGetProperty("concepts", out _));
        Assert.DoesNotContain("clave-de-prueba", handler.Body);

        ConceptProposal concept = Assert.Single(outcome.Concepts);
        Assert.Equal(("El regalo que se usa", "¿Otro regalo que queda en el cajón?", 4), (concept.Title, concept.Hook, concept.Novelty));
        Assert.Equal([2, 1], concept.Fragments);
        Assert.Equal(["Toma navideña"], concept.Missing);
        Assert.Equal(("gemini", ConceptPlanPrompt.Version, 1000, 1000), (outcome.Provider, outcome.PromptVersion, outcome.InputTokens, outcome.OutputTokens));
        Assert.Equal(0.0028m, outcome.CostUsd);
        AnalysisCost estimate = planner.Estimate(3000, 20);
        Assert.Equal((1700, 10_500), (estimate.InputTokens, estimate.OutputTokens));

        // Un plan vacío o ilegible es un error definitivo.
        GeminiConceptPlanner broken = new(
            new GeminiInteractions(new HttpClient(new PlanStub(HttpStatusCode.OK, """{"status":"completed","steps":[]}""")), options), options);
        Assert.False((await Assert.ThrowsAsync<AiProviderException>(() => broken.PlanAsync(request, CancellationToken.None))).Retryable);
    }

    [Fact]
    public async Task Un_plan_se_estima_se_genera_y_sus_conceptos_se_convierten_en_recetas()
    {
        Scenario scenario = await ArrangeAsync(AutonomyLevel.Propose, BriefPlatform.MetaFeed, 4);
        await using AsyncServiceScope scope = fixture.Scope(scenario.Organization);
        StrategyService strategy = scope.ServiceProvider.GetRequiredService<StrategyService>();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        PlanEstimate estimate = await strategy.EstimateAsync(scenario.Brief, CancellationToken.None);
        Assert.Equal((true, 4, 3, 0.01m, 0.50m), (estimate.Configured, estimate.Concepts, estimate.Fragments, estimate.CostUsd, estimate.MaxCostUsd));
        fixture.Planner.IsConfigured = false;
        await JobTests.AssertBusinessAsync(409, "AI_NOT_CONFIGURED", () => strategy.RequestAsync(scenario.Brief, CancellationToken.None));
        fixture.Planner.IsConfigured = true;
        fixture.Planner.CostPerCall = 0.60m;
        await JobTests.AssertBusinessAsync(409, "BUDGET_EXCEEDED", () => strategy.RequestAsync(scenario.Brief, CancellationToken.None));
        fixture.Planner.CostPerCall = 0.01m;

        long planId = await strategy.RequestAsync(scenario.Brief, CancellationToken.None);
        Assert.Equal(MediaProcessingStatus.Pending, (await strategy.GetAsync(planId, CancellationToken.None)).Status);
        await JobTests.AssertBusinessAsync(409, "PLAN_IN_PROGRESS", () => strategy.RequestAsync(scenario.Brief, CancellationToken.None));
        await DrainAsync();

        PlanDetail plan = await strategy.GetAsync(planId, CancellationToken.None);
        Assert.Equal((MediaProcessingStatus.Ready, 4, "planificador-falso", ConceptPlanPrompt.Version, 0.01m), (plan.Status, plan.Concepts.Count, plan.Model, plan.PromptVersion, plan.CostUsd));
        Assert.All(plan.Concepts, c => Assert.True(c.Realizable));
        Assert.All(plan.Concepts, c => Assert.Null(c.RecipeId));
        Assert.Equal([scenario.Segments[0], scenario.Segments[1]], plan.Concepts[0].Fragments.Select(f => f.SegmentId!.Value));
        Assert.Equal(1, fixture.Planner.Calls);
        Assert.Equal(3, fixture.Planner.LastRequest!.Fragments.Count);
        Assert.Contains("Sin analizar", fixture.Planner.LastRequest.Fragments[0].Description);
        Assert.Equal("Uñas Ya", fixture.Planner.LastRequest.Brand.Name);
        AiUsageRecord usage = await db.AiUsageRecords.AsNoTracking().SingleAsync(u => u.Task == "strategy.plan");
        Assert.Equal((0.01m, 2000, 3000), (usage.CostUsd, usage.InputTokens, usage.OutputTokens));
        // Con autonomía "sólo propone" no se arma nada solo.
        Assert.Equal(0, await db.CreativeRecipes.CountAsync());

        long recipeId = await strategy.CreateRecipeAsync(plan.Concepts[0].Id, CancellationToken.None);
        RecipeDetail recipe = await scope.ServiceProvider.GetRequiredService<CompositionService>().GetAsync(recipeId, CancellationToken.None);
        Assert.Equal(("Concepto 1", "Hook 1", 1080, 1350, 2), (recipe.Name, recipe.Concept, recipe.Width, recipe.Height, recipe.Items.Count));
        Assert.Contains("más gente mira hasta el final", recipe.Hypothesis);
        await JobTests.AssertBusinessAsync(409, "CONCEPT_HAS_RECIPE", () => strategy.CreateRecipeAsync(plan.Concepts[0].Id, CancellationToken.None));
        Assert.Equal(3, await strategy.CreateRecipesAsync(planId, false, CancellationToken.None));
        Assert.Equal(0, await strategy.CreateRecipesAsync(planId, false, CancellationToken.None));
        PlanListItem listed = Assert.Single(await strategy.ListAsync(scenario.Brief, CancellationToken.None));
        Assert.Equal((4, 4, 0.01m), (listed.Concepts, listed.Recipes, listed.CostUsd));
        Assert.Equal(0, await db.CreativeVariants.CountAsync());

        // El presupuesto de generación del brief, si es menor que el tope por plan, manda.
        BriefService briefs = scope.ServiceProvider.GetRequiredService<BriefService>();
        long tight = await briefs.CreateAsync(new BriefBody { BrandId = scenario.Brand, Name = "Ajustado", VideoCount = 2, MaxGenerationBudgetUsd = 0.02m }, CancellationToken.None);
        Assert.Equal(0.02m, (await strategy.EstimateAsync(tight, CancellationToken.None)).MaxCostUsd);
        fixture.Planner.CostPerCall = 0.03m;
        await JobTests.AssertBusinessAsync(409, "BUDGET_EXCEEDED", () => strategy.RequestAsync(tight, CancellationToken.None));
        fixture.Planner.CostPerCall = 0.01m;

        await using AsyncServiceScope otherScope = fixture.Scope(await fixture.NewOrganizationAsync());
        StrategyService foreign = otherScope.ServiceProvider.GetRequiredService<StrategyService>();
        await JobTests.AssertBusinessAsync(404, "PLAN_NOT_FOUND", () => foreign.GetAsync(planId, CancellationToken.None));
        await JobTests.AssertBusinessAsync(404, "BRIEF_NOT_FOUND", () => foreign.EstimateAsync(scenario.Brief, CancellationToken.None));
        await JobTests.AssertBusinessAsync(404, "BRIEF_NOT_FOUND", () => foreign.RequestAsync(scenario.Brief, CancellationToken.None));
        await JobTests.AssertBusinessAsync(404, "CONCEPT_NOT_FOUND", () => foreign.CreateRecipeAsync(plan.Concepts[1].Id, CancellationToken.None));
        Assert.Empty(await foreign.ListAsync(scenario.Brief, CancellationToken.None));
    }

    [Fact]
    public async Task Con_autonomia_automatica_el_plan_se_arma_y_se_renderiza_solo_salvo_lo_que_no_se_puede()
    {
        Scenario scenario = await ArrangeAsync(AutonomyLevel.Automatic, BriefPlatform.MetaReels, 4);
        fixture.Planner.Proposals =
        [
            FakeConceptPlanner.Concept(1, [1, 3], []),
            FakeConceptPlanner.Concept(2, [2], [], "Te prometemos una cura garantizada"),
            FakeConceptPlanner.Concept(3, [], ["Testimonio de una clienta", "Toma del empaque"]),
            FakeConceptPlanner.Concept(4, [3, 2, 1], []),
        ];
        await using AsyncServiceScope scope = fixture.Scope(scenario.Organization);
        StrategyService strategy = scope.ServiceProvider.GetRequiredService<StrategyService>();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        long planId = await strategy.RequestAsync(scenario.Brief, CancellationToken.None);
        await DrainAsync();

        PlanDetail plan = await strategy.GetAsync(planId, CancellationToken.None);
        Assert.Equal(MediaProcessingStatus.Ready, plan.Status);
        Assert.Equal([true, false, false, true], plan.Concepts.Select(c => c.Realizable));
        Assert.Equal([true, false, false, true], plan.Concepts.Select(c => c.RecipeId is not null));
        Assert.Equal(["cura garantizada"], plan.Concepts[1].Violations);
        Assert.Equal(["Testimonio de una clienta", "Toma del empaque"], plan.Concepts[2].Missing);
        await JobTests.AssertBusinessAsync(409, "CONCEPT_NOT_REALIZABLE", () => strategy.CreateRecipeAsync(plan.Concepts[1].Id, CancellationToken.None));
        await JobTests.AssertBusinessAsync(409, "CONCEPT_NOT_REALIZABLE", () => strategy.CreateRecipeAsync(plan.Concepts[2].Id, CancellationToken.None));

        // Los dos realizables quedaron renderizados, sin edición manual, en el formato de la plataforma.
        List<CreativeVariant> videos = await db.CreativeVariants.AsNoTracking().ToListAsync();
        Assert.Equal(2, videos.Count);
        Assert.All(videos, v => Assert.Equal((MediaProcessingStatus.Ready, 1080, 1920), (v.Status, v.Width, v.Height)));
        Assert.Equal(2, await db.QualityChecks.Where(c => c.Rule == "duration" && c.Passed).CountAsync());
    }

    [Fact]
    public async Task Un_error_transitorio_del_planificador_se_reintenta_y_uno_definitivo_deja_el_plan_fallido()
    {
        Scenario scenario = await ArrangeAsync(AutonomyLevel.Propose, BriefPlatform.MetaReels, 2);
        await using AsyncServiceScope scope = fixture.Scope(scenario.Organization);
        StrategyService strategy = scope.ServiceProvider.GetRequiredService<StrategyService>();

        fixture.Planner.Errors.Enqueue(new AiProviderException("Gemini respondió 429.", true));
        long first = await strategy.RequestAsync(scenario.Brief, CancellationToken.None);
        await DrainAsync();
        Assert.Equal(MediaProcessingStatus.Ready, (await strategy.GetAsync(first, CancellationToken.None)).Status);
        Assert.Equal(2, fixture.Planner.Calls);

        fixture.Planner.Errors.Enqueue(new AiProviderException("Gemini respondió 400. Pedido inválido.", false));
        long second = await strategy.RequestAsync(scenario.Brief, CancellationToken.None);
        await DrainAsync();

        PlanDetail failed = await strategy.GetAsync(second, CancellationToken.None);
        Assert.Equal((MediaProcessingStatus.Failed, "Gemini respondió 400. Pedido inválido."), (failed.Status, failed.Error));
        Assert.Empty(failed.Concepts);
        Assert.Equal(3, fixture.Planner.Calls);
        // Un plan fallido no bloquea pedir otro.
        Assert.True(await strategy.RequestAsync(scenario.Brief, CancellationToken.None) > second);
        await fixture.ClearJobsAsync();
    }

    // Una organización con el video de tres cortes autorizado y sus escenas elegidas para producción, una marca con
    // un mensaje prohibido y un brief.
    private async Task<Scenario> ArrangeAsync(AutonomyLevel autonomy, BriefPlatform platform, int videos)
    {
        await fixture.ClearJobsAsync();
        fixture.Planner.Reset();
        int org = await fixture.NewOrganizationAsync();
        await using AsyncServiceScope scope = fixture.Scope(org);
        long asset;
        await using (FileStream stream = File.OpenRead(fixture.SampleCutsVideoPath))
        {
            asset = (await scope.ServiceProvider.GetRequiredService<MediaLibraryService>()
                .IngestAsync("cortes.mp4", new MediaRightsBody { ReuseAuthorized = true }, stream, CancellationToken.None)).AssetId;
        }

        await DrainAsync();
        List<long> segments = (await scope.ServiceProvider.GetRequiredService<SceneService>().ListAsync(asset, CancellationToken.None))
            .Segments.Select(s => s.Id).ToList();
        SegmentEditService editor = scope.ServiceProvider.GetRequiredService<SegmentEditService>();
        foreach (long segment in segments)
        {
            await editor.SetSelectedAsync(segment, true, CancellationToken.None);
        }

        BrandService brands = scope.ServiceProvider.GetRequiredService<BrandService>();
        int brand = await brands.CreateAsync(new BrandBody { Name = "Uñas Ya" }, CancellationToken.None);
        await brands.AddItemAsync(brand, new BrandItemBody { Kind = BrandItemKind.ForbiddenMessage, Text = "cura garantizada" }, CancellationToken.None);
        long brief = await scope.ServiceProvider.GetRequiredService<BriefService>().CreateAsync(
            new BriefBody { BrandId = brand, Name = "Navidad", VideoCount = videos, Platform = platform, Autonomy = autonomy }, CancellationToken.None);
        return new Scenario(org, brand, brief, segments);
    }

    private async Task DrainAsync()
    {
        JobRunner runner = fixture.Services.GetRequiredService<JobRunner>();
        while (await runner.ProcessNextAsync(CancellationToken.None))
        {
        }
    }

    private sealed record Scenario(int Organization, int Brand, long Brief, List<long> Segments);

    private sealed class PlanStub(HttpStatusCode status, string response) : HttpMessageHandler
    {
        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }
}

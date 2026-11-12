using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CreativeIntelligence.Tests;

/// <summary>Agente investigador falso: devuelve las referencias que se le definan, cuenta las llamadas y puede
/// fallar a pedido. Los tests automáticos nunca buscan en la web.</summary>
public sealed class FakeResearchAgent : IResearchAgent
{
    public bool IsConfigured { get; set; } = true;

    public string Model => "investigador-falso";

    public int Calls { get; private set; }

    public decimal CostPerCall { get; set; } = 0.05m;

    public Queue<AiProviderException> Errors { get; } = new();

    public ResearchRequest? LastRequest { get; private set; }

    public List<ReferenceProposal> Proposals { get; set; } = [];

    public void Reset()
    {
        IsConfigured = true;
        Calls = 0;
        CostPerCall = 0.05m;
        Errors.Clear();
        LastRequest = null;
        Proposals = [];
    }

    public AnalysisCost Estimate(int inputCharacters, int maxReferences)
    {
        return new AnalysisCost(inputCharacters / 3, maxReferences * 120, CostPerCall);
    }

    public Task<ResearchOutcome> ResearchAsync(ResearchRequest request, CancellationToken cancellationToken)
    {
        Calls++;
        LastRequest = request;
        if (Errors.TryDequeue(out AiProviderException? error))
        {
            throw error;
        }

        return Task.FromResult(new ResearchOutcome(Proposals, 6, "falso", Model, ResearchPrompt.Version, 9000, 1200, CostPerCall, 70));
    }
}

[Collection(DatabaseCollection.Name)]
public sealed class ResearchTests(DatabaseFixture fixture)
{
    [Theory]
    [InlineData("ejemplo.com", "ejemplo.com")]
    [InlineData("  HTTPS://www.Ejemplo.com/anuncios?x=1 ", "ejemplo.com")]
    [InlineData("http://blog.ejemplo.com.uy", "blog.ejemplo.com.uy")]
    [InlineData("ejemplo", null)]
    [InlineData("192.168.0.1", null)]
    [InlineData("", null)]
    [InlineData("no es un sitio", null)]
    public void Un_dominio_se_normaliza_sin_protocolo_ni_www_ni_ruta(string input, string? expected)
    {
        Assert.Equal(expected, ResearchRules.NormalizeDomain(input));
    }

    [Theory]
    [InlineData("https://ejemplo.com/anuncio/1", "ejemplo.com")]
    [InlineData("http://BLOG.Ejemplo.com/nota", "ejemplo.com")]
    [InlineData("https://otro.net/x", "otro.net")]
    [InlineData("https://miejemplo.com/x", null)]
    [InlineData("https://ejemplo.com.atacante.net/x", null)]
    [InlineData("https://atacante.net/?u=ejemplo.com", null)]
    [InlineData("ftp://ejemplo.com/archivo", null)]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("ejemplo.com/sin-protocolo", null)]
    [InlineData(null, null)]
    public void Una_direccion_solo_pertenece_a_un_dominio_autorizado_o_a_un_subdominio_suyo(string? url, string? expected)
    {
        Assert.Equal(expected, ResearchRules.MatchDomain(url, ["ejemplo.com", "otro.net"]));
    }

    [Fact]
    public void De_lo_que_propone_el_agente_solo_se_guarda_lo_de_fuentes_autorizadas_y_sin_repetir()
    {
        List<ResearchSource> sources =
        [
            new ResearchSource { Id = 1, Name = "Ejemplo", Domain = "ejemplo.com", DefaultUsage = ReferenceUsage.ReferenceOnly },
            new ResearchSource { Id = 2, Name = "Propio", Domain = "propio.com", DefaultUsage = ReferenceUsage.Usable },
        ];
        List<ReferenceProposal> proposals =
        [
            new ReferenceProposal(" https://ejemplo.com/a ", "  Hook de regalo  ", ReferenceKind.Hook, " Abre con una pregunta "),
            new ReferenceProposal("https://ejemplo.com/a", "Repetida", ReferenceKind.Hook, "x"),
            new ReferenceProposal("https://no-autorizado.com/b", "Ajena", ReferenceKind.Ad, "x"),
            new ReferenceProposal("https://propio.com/c", "Propia", ReferenceKind.Ad, "Testimonio"),
            new ReferenceProposal("https://propio.com/ya-guardada", "Conocida", ReferenceKind.Trend, "x"),
            new ReferenceProposal("https://ejemplo.com/sin-titulo", " ", ReferenceKind.Trend, "x"),
        ];

        List<ExternalReference> kept = ResearchJobHandler.Keep(
            proposals, sources, ["https://PROPIO.com/ya-guardada"], new ResearchRun { Id = 9, CampaignBriefId = 4 });

        Assert.Equal(["https://ejemplo.com/a", "https://propio.com/c"], kept.Select(r => r.Url));
        Assert.Equal(("Hook de regalo", "Abre con una pregunta", ReferenceUsage.ReferenceOnly, 1), (kept[0].Title, kept[0].Summary, kept[0].Usage, kept[0].ResearchSourceId));
        Assert.Equal((ReferenceUsage.Usable, 2), (kept[1].Usage, kept[1].ResearchSourceId));
        Assert.All(kept, r => Assert.Equal((ReferenceOrigin.Researched, 9L, 4L), (r.Origin, r.ResearchRunId!.Value, r.CampaignBriefId!.Value)));
    }

    [Fact]
    public async Task El_cliente_de_Gemini_investiga_con_busqueda_web_y_cobra_las_consultas()
    {
        const string response =
            """
            {
              "status": "completed",
              "steps": [
                { "type": "google_search_call", "arguments": { "queries": ["anuncios navidad uñas", "hooks regalos"] } },
                { "type": "google_search_result", "call_id": "search_001" },
                { "type": "google_search_call", "arguments": { "queries": ["tendencias reels navidad"] } },
                { "type": "model_output", "content": [ { "type": "text", "annotations": [ { "type": "url_citation", "url": "https://ejemplo.com/a", "title": "ejemplo.com", "start_index": 0, "end_index": 5 } ], "text": "{\"references\":[{\"url\":\"https://ejemplo.com/a\",\"title\":\"Regalos que se usan\",\"kind\":\"hook\",\"summary\":\"Abre con una pregunta\"},{\"url\":\"https://ejemplo.com/b\",\"title\":\"x\",\"kind\":\"inventada\",\"summary\":\"x\"}]}" } ] }
              ],
              "usage": { "total_input_tokens": 10000, "total_output_tokens": 900, "total_thought_tokens": 100 }
            }
            """;
        ResearchStub handler = new(response);
        IOptions<GeminiOptions> options = Options.Create(new GeminiOptions { ApiKey = "clave-de-prueba" });
        GeminiResearchAgent agent = new(new GeminiInteractions(new HttpClient(handler), options), options);
        ResearchRequest request = new(
            new BrandDetail(1, "Uñas Ya", null, null, null, null, null, null, []), new BriefDetail { Name = "Navidad", Occasion = "Navidad" },
            ["ejemplo.com", "otro.net"], 15);

        ResearchOutcome outcome = await agent.ResearchAsync(request, CancellationToken.None);

        using JsonDocument body = JsonDocument.Parse(handler.Body);
        JsonElement root = body.RootElement;
        Assert.Equal("google_search", Assert.Single(root.GetProperty("tools").EnumerateArray().ToArray()).GetProperty("type").GetString());
        Assert.Equal(ResearchPrompt.SystemInstruction, root.GetProperty("system_instruction").GetString());
        Assert.False(root.GetProperty("store").GetBoolean());
        string input = Assert.Single(root.GetProperty("input").EnumerateArray().ToArray()).GetProperty("text").GetString()!;
        Assert.Contains("- ejemplo.com\n- otro.net", input);
        Assert.Contains("Ocasión: Navidad", input);
        Assert.EndsWith("Devolvé hasta 15 referencias, las más útiles primero.", input);
        Assert.True(root.GetProperty("response_format").GetProperty("schema").GetProperty("properties").TryGetProperty("references", out _));

        // La de clase desconocida se descarta; las tres consultas de búsqueda se cobran aparte de los tokens.
        Assert.Equal(new ReferenceProposal("https://ejemplo.com/a", "Regalos que se usan", ReferenceKind.Hook, "Abre con una pregunta"), Assert.Single(outcome.References));
        Assert.Equal((3, 10000, 1000), (outcome.SearchQueries, outcome.InputTokens, outcome.OutputTokens));
        Assert.Equal(Math.Round((((10000 * 0.30m) + (1000 * 2.50m)) / 1_000_000m) + (3 * 14m / 1000m), 6), outcome.CostUsd);
        Assert.Equal(0.0475m, outcome.CostUsd);
        // La estimación supone ocho consultas.
        Assert.True(agent.Estimate(600, 15).CostUsd > 8 * 0.014m);
    }

    [Fact]
    public async Task Sin_la_herramienta_de_busqueda_el_pedido_no_lleva_tools()
    {
        ResearchStub handler = new("""{"status":"completed","steps":[{"type":"model_output","content":[{"type":"text","text":"{\"concepts\":[{\"title\":\"t\",\"hook\":\"h\"}]}"}]}]}""");
        IOptions<GeminiOptions> options = Options.Create(new GeminiOptions { ApiKey = "clave-de-prueba" });
        GeminiConceptPlanner planner = new(new GeminiInteractions(new HttpClient(handler), options), options);

        await planner.PlanAsync(new PlanRequest(new BrandDetail(1, "M", null, null, null, null, null, null, []), new BriefDetail { Name = "B" }, [], 1), CancellationToken.None);

        using JsonDocument body = JsonDocument.Parse(handler.Body);
        Assert.False(body.RootElement.TryGetProperty("tools", out _));
    }

    [Fact]
    public async Task Las_referencias_solo_se_guardan_de_fuentes_autorizadas_y_respetan_su_permiso()
    {
        Scenario scenario = await ArrangeAsync(useResearch: true);
        await using AsyncServiceScope scope = fixture.Scope(scenario.Organization);
        ResearchService research = scope.ServiceProvider.GetRequiredService<ResearchService>();

        int reference = await research.AddSourceAsync(
            new ResearchSourceBody { Name = "Biblioteca de anuncios", Domain = "https://www.Ejemplo.com/ads", ConditionsOfUse = "Sólo consulta" }, CancellationToken.None);
        int own = await research.AddSourceAsync(
            new ResearchSourceBody { Name = "Canal propio", Domain = "propio.com", DefaultUsage = ReferenceUsage.Usable }, CancellationToken.None);
        await JobTests.AssertBusinessAsync(409, "SOURCE_EXISTS", () => research.AddSourceAsync(new ResearchSourceBody { Name = "Otra vez", Domain = "ejemplo.com" }, CancellationToken.None));
        await JobTests.AssertBusinessAsync(400, "DOMAIN_INVALID", () => research.AddSourceAsync(new ResearchSourceBody { Name = "Mal", Domain = "no es un sitio" }, CancellationToken.None));

        long first = await research.AddReferenceAsync(
            new ReferenceBody { Url = " https://blog.ejemplo.com/hooks ", Title = " Hooks de regalo ", Kind = ReferenceKind.Hook, Summary = "Abre con una pregunta", BriefId = scenario.Brief },
            CancellationToken.None);
        long second = await research.AddReferenceAsync(new ReferenceBody { Url = "https://propio.com/testimonio", Title = "Testimonio", Kind = ReferenceKind.Ad }, CancellationToken.None);
        await JobTests.AssertBusinessAsync(
            400, "SOURCE_NOT_AUTHORIZED", () => research.AddReferenceAsync(new ReferenceBody { Url = "https://otro-sitio.com/x", Title = "x" }, CancellationToken.None));
        await JobTests.AssertBusinessAsync(
            409, "REFERENCE_EXISTS", () => research.AddReferenceAsync(new ReferenceBody { Url = "https://propio.com/testimonio", Title = "x" }, CancellationToken.None));
        await JobTests.AssertBusinessAsync(
            404, "BRIEF_NOT_FOUND", () => research.AddReferenceAsync(new ReferenceBody { Url = "https://propio.com/y", Title = "x", BriefId = long.MaxValue }, CancellationToken.None));

        ResearchOverview overview = await research.OverviewAsync(CancellationToken.None);
        Assert.Equal([("ejemplo.com", ReferenceUsage.ReferenceOnly, 1), ("propio.com", ReferenceUsage.Usable, 1)], overview.Sources.OrderBy(s => s.Domain).Select(s => (s.Domain, s.DefaultUsage, s.References)));
        ReferenceItem hooks = overview.References.Single(r => r.Id == first);
        Assert.Equal(("https://blog.ejemplo.com/hooks", "Hooks de regalo", ReferenceUsage.ReferenceOnly, ReferenceOrigin.Manual, "Navidad", false),
            (hooks.Url, hooks.Title, hooks.Usage, hooks.Origin, hooks.BriefName, hooks.SourceAllowsUse));
        Assert.Equal(ReferenceUsage.Usable, overview.References.Single(r => r.Id == second).Usage);

        // Una referencia de una fuente de "sólo referencia" no se puede marcar utilizable.
        await JobTests.AssertBusinessAsync(409, "SOURCE_FORBIDS_USE", () => research.SetUsageAsync(first, ReferenceUsage.Usable, CancellationToken.None));
        await research.SetUsageAsync(second, ReferenceUsage.ReferenceOnly, CancellationToken.None);
        await research.SetUsageAsync(second, ReferenceUsage.Usable, CancellationToken.None);

        // Una fuente desactivada no acepta referencias nuevas; las que tiene quedan.
        await research.SetSourceActiveAsync(reference, false, CancellationToken.None);
        await JobTests.AssertBusinessAsync(
            400, "SOURCE_NOT_AUTHORIZED", () => research.AddReferenceAsync(new ReferenceBody { Url = "https://ejemplo.com/otra", Title = "x" }, CancellationToken.None));
        Assert.Equal(2, (await research.OverviewAsync(CancellationToken.None)).References.Count);
        await research.RemoveReferenceAsync(second, CancellationToken.None);
        await JobTests.AssertBusinessAsync(404, "REFERENCE_NOT_FOUND", () => research.RemoveReferenceAsync(second, CancellationToken.None));
        Assert.True(own > 0);

        await using AsyncServiceScope otherScope = fixture.Scope(await fixture.NewOrganizationAsync());
        ResearchService foreign = otherScope.ServiceProvider.GetRequiredService<ResearchService>();
        ResearchOverview empty = await foreign.OverviewAsync(CancellationToken.None);
        Assert.Empty(empty.Sources);
        Assert.Empty(empty.References);
        await JobTests.AssertBusinessAsync(404, "SOURCE_NOT_FOUND", () => foreign.SetSourceActiveAsync(reference, true, CancellationToken.None));
        await JobTests.AssertBusinessAsync(404, "REFERENCE_NOT_FOUND", () => foreign.SetUsageAsync(first, ReferenceUsage.ReferenceOnly, CancellationToken.None));
        await JobTests.AssertBusinessAsync(
            400, "SOURCE_NOT_AUTHORIZED", () => foreign.AddReferenceAsync(new ReferenceBody { Url = "https://propio.com/z", Title = "x" }, CancellationToken.None));
    }

    [Fact]
    public async Task El_agente_investiga_para_un_brief_guarda_lo_autorizado_y_el_plan_lo_recibe()
    {
        Scenario scenario = await ArrangeAsync(useResearch: true);
        await using AsyncServiceScope scope = fixture.Scope(scenario.Organization);
        ResearchService research = scope.ServiceProvider.GetRequiredService<ResearchService>();
        StrategyService strategy = scope.ServiceProvider.GetRequiredService<StrategyService>();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await JobTests.AssertBusinessAsync(409, "NO_SOURCES", () => research.RequestAsync(scenario.Brief, CancellationToken.None));
        await research.AddSourceAsync(new ResearchSourceBody { Name = "Biblioteca", Domain = "ejemplo.com" }, CancellationToken.None);
        int inactive = await research.AddSourceAsync(new ResearchSourceBody { Name = "Pausada", Domain = "pausada.com" }, CancellationToken.None);
        await research.SetSourceActiveAsync(inactive, false, CancellationToken.None);

        ResearchEstimate estimate = await research.EstimateAsync(scenario.Brief, CancellationToken.None);
        Assert.Equal((true, 1, 15, 0.05m, 0.30m), (estimate.Configured, estimate.Sources, estimate.MaxReferences, estimate.CostUsd, estimate.MaxCostUsd));
        fixture.Research.IsConfigured = false;
        await JobTests.AssertBusinessAsync(409, "AI_NOT_CONFIGURED", () => research.RequestAsync(scenario.Brief, CancellationToken.None));
        fixture.Research.IsConfigured = true;
        fixture.Research.CostPerCall = 0.31m;
        await JobTests.AssertBusinessAsync(409, "BUDGET_EXCEEDED", () => research.RequestAsync(scenario.Brief, CancellationToken.None));
        fixture.Research.CostPerCall = 0.05m;

        fixture.Research.Proposals =
        [
            new ReferenceProposal("https://ejemplo.com/navidad-1", "Regalos que se usan", ReferenceKind.Hook, "Abre con una pregunta incómoda"),
            new ReferenceProposal("https://www.ejemplo.com/navidad-2", "Estructura problema-solución", ReferenceKind.Structure, "Tres tiempos"),
            new ReferenceProposal("https://pausada.com/x", "De una fuente desactivada", ReferenceKind.Trend, "x"),
            new ReferenceProposal("https://sitio-cualquiera.com/y", "De un sitio no autorizado", ReferenceKind.Ad, "x"),
        ];
        long runId = await research.RequestAsync(scenario.Brief, CancellationToken.None);
        await JobTests.AssertBusinessAsync(409, "RESEARCH_IN_PROGRESS", () => research.RequestAsync(scenario.Brief, CancellationToken.None));
        await DrainAsync();

        BriefResearch result = await research.ForBriefAsync(scenario.Brief, CancellationToken.None);
        ResearchRunItem run = Assert.Single(result.Runs);
        Assert.Equal((runId, MediaProcessingStatus.Ready, 6, 4, 2, 0.05m), (run.Id, run.Status, run.SearchQueries, run.Found, run.Kept, run.CostUsd));
        Assert.Equal(["https://ejemplo.com/navidad-1", "https://www.ejemplo.com/navidad-2"], result.References.OrderBy(r => r.Url).Select(r => r.Url));
        Assert.All(result.References, r => Assert.Equal((ReferenceOrigin.Researched, ReferenceUsage.ReferenceOnly, "Biblioteca"), (r.Origin, r.Usage, r.SourceName)));
        Assert.Equal(["ejemplo.com"], fixture.Research.LastRequest!.Domains);
        Assert.Equal("Uñas Ya", fixture.Research.LastRequest.Brand.Name);
        AiUsageRecord usage = await db.AiUsageRecords.AsNoTracking().SingleAsync(u => u.Task == "strategy.research");
        Assert.Equal((0.05m, 9000), (usage.CostUsd, usage.InputTokens));

        // Otra corrida no vuelve a guardar lo que ya está.
        await research.RequestAsync(scenario.Brief, CancellationToken.None);
        await DrainAsync();
        Assert.Equal(0, (await research.ForBriefAsync(scenario.Brief, CancellationToken.None)).Runs[0].Kept);
        Assert.Equal(2, await db.ExternalReferences.CountAsync());

        // El plan recibe las referencias del brief como inspiración, con su permiso.
        PlanRequest plan = await strategy.BuildRequestAsync(scenario.Brief, CancellationToken.None);
        Assert.Equal(2, plan.References.Count);
        string text = ConceptPlanPrompt.BuildInput(plan);
        Assert.Contains("# Referencias externas (inspiración; no copiar)", text);
        Assert.Contains("- [Hook, sólo referencia] Regalos que se usan: Abre con una pregunta incómoda", text);

        // Un error definitivo deja la corrida fallida.
        fixture.Research.Errors.Enqueue(new AiProviderException("Gemini respondió 400.", false));
        long failed = await research.RequestAsync(scenario.Brief, CancellationToken.None);
        await DrainAsync();
        ResearchRunItem failedRun = (await research.ForBriefAsync(scenario.Brief, CancellationToken.None)).Runs.Single(r => r.Id == failed);
        Assert.Equal((MediaProcessingStatus.Failed, "Gemini respondió 400."), (failedRun.Status, failedRun.Error));
    }

    [Fact]
    public async Task Un_brief_que_no_pide_investigacion_no_le_pasa_referencias_al_plan()
    {
        Scenario scenario = await ArrangeAsync(useResearch: false);
        await using AsyncServiceScope scope = fixture.Scope(scenario.Organization);
        ResearchService research = scope.ServiceProvider.GetRequiredService<ResearchService>();
        await research.AddSourceAsync(new ResearchSourceBody { Name = "Biblioteca", Domain = "ejemplo.com" }, CancellationToken.None);
        await research.AddReferenceAsync(new ReferenceBody { Url = "https://ejemplo.com/a", Title = "Idea", BriefId = scenario.Brief }, CancellationToken.None);

        PlanRequest plan = await scope.ServiceProvider.GetRequiredService<StrategyService>().BuildRequestAsync(scenario.Brief, CancellationToken.None);

        Assert.Empty(plan.References);
        Assert.DoesNotContain("Referencias externas", ConceptPlanPrompt.BuildInput(plan));
    }

    // Una organización con una marca y un brief.
    private async Task<Scenario> ArrangeAsync(bool useResearch)
    {
        await fixture.ClearJobsAsync();
        fixture.Research.Reset();
        int org = await fixture.NewOrganizationAsync();
        await using AsyncServiceScope scope = fixture.Scope(org);
        int brand = await scope.ServiceProvider.GetRequiredService<BrandService>().CreateAsync(new BrandBody { Name = "Uñas Ya" }, CancellationToken.None);
        long brief = await scope.ServiceProvider.GetRequiredService<BriefService>().CreateAsync(
            new BriefBody { BrandId = brand, Name = "Navidad", Occasion = "Navidad", VideoCount = 5, UseExternalResearch = useResearch }, CancellationToken.None);
        return new Scenario(org, brief);
    }

    private async Task DrainAsync()
    {
        JobRunner runner = fixture.Services.GetRequiredService<JobRunner>();
        while (await runner.ProcessNextAsync(CancellationToken.None))
        {
        }
    }

    private sealed record Scenario(int Organization, long Brief);

    private sealed class ResearchStub(string response) : HttpMessageHandler
    {
        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }
}

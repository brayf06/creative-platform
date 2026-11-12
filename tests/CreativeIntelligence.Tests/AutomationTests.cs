using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CreativeIntelligence.Tests;

/// <summary>Publicador de Meta falso: registra cada llamada de escritura. Por defecto se declara sin conexión.</summary>
public sealed class FakeMetaPublisher : IMetaPublisher
{
    public bool IsConfigured { get; set; }

    /// <summary>Pedidos de publicación recibidos.</summary>
    public List<MetaPublishRequest> Calls { get; } = [];

    /// <summary>Errores a lanzar en las próximas llamadas, en orden.</summary>
    public Queue<MetaPublishException> Errors { get; } = new();

    public void Reset()
    {
        IsConfigured = false;
        Calls.Clear();
        Errors.Clear();
    }

    public Task<string> PublishAsync(MetaPublishRequest request, CancellationToken cancellationToken)
    {
        Calls.Add(request);
        return Errors.TryDequeue(out MetaPublishException? error) ? throw error : Task.FromResult($"meta-ad-{Calls.Count}");
    }
}

/// <summary>Automatización: cada guarda, los tres modos, la simulación sin escrituras y la ejecución real con un
/// publicador falso.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class AutomationTests(DatabaseFixture fixture)
{
    private static readonly AutomationLimits Semi = new(false, AutomationMode.SemiAutomatic, 50m, 100m, 3);
    private static readonly DailyUsage Nothing = new(0, 0m);

    [Fact]
    public void Cada_guarda_bloquea_con_su_motivo()
    {
        // Kill switch: frena todo, antes que cualquier otra cosa.
        GuardVerdict killed = AutomationRules.Evaluate(Semi with { KillSwitchActive = true, MaxBudgetPerAction = 0 }, 999m, Nothing, null, "video.mp4");
        Assert.Equal((true, true, "El kill switch está activo: no se acepta ni se ejecuta ninguna acción."), (killed.Blocked, killed.CancelledByKillSwitch, killed.Reason));

        // Sin los tres límites definidos no se acepta nada.
        Assert.All(
            new[] { Semi with { MaxBudgetPerAction = 0 }, Semi with { MaxDailyBudget = 0 }, Semi with { MaxActionsPerDay = 0 } },
            limits =>
            {
                GuardVerdict verdict = AutomationRules.Evaluate(limits, 10m, Nothing, null, null);
                Assert.True(verdict.Blocked);
                Assert.StartsWith("Faltan límites", verdict.Reason);
            });

        // Presupuesto por acción.
        Assert.Equal(
            (true, false, "El presupuesto diario de 50.01 supera el máximo por acción (50.00)."),
            Blocked(AutomationRules.Evaluate(Semi, 50.01m, Nothing, null, null)));
        Assert.False(AutomationRules.Evaluate(Semi, 50m, Nothing, null, null).Blocked);

        // Presupuesto del día: cuenta lo ya comprometido.
        Assert.Equal(
            (true, false, "Con esta acción el presupuesto comprometido hoy sería 110.00 y el máximo diario es 100.00."),
            Blocked(AutomationRules.Evaluate(Semi, 30m, new DailyUsage(2, 80m), null, null)));
        Assert.False(AutomationRules.Evaluate(Semi, 20m, new DailyUsage(2, 80m), null, null).Blocked);

        // Acciones por día.
        Assert.Equal(
            (true, false, "Ya hay 3 acciones hoy y el límite diario es 3."),
            Blocked(AutomationRules.Evaluate(Semi, 10m, new DailyUsage(3, 30m), null, null)));

        // Derechos del video.
        Assert.Equal(
            (true, false, "\"toma.mp4\" ya no tiene autorización de reutilización vigente: el video no se puede publicar."),
            Blocked(AutomationRules.Evaluate(Semi, 10m, Nothing, LearningConfidence.High, "toma.mp4")));
    }

    [Fact]
    public void El_modo_decide_quien_aprueba_y_sin_datos_suficientes_decide_una_persona()
    {
        // Manual y semiautomático: publicar siempre lo aprueba una persona, tenga el respaldo que tenga.
        GuardVerdict manual = AutomationRules.Evaluate(Semi with { Mode = AutomationMode.Manual }, 10m, Nothing, LearningConfidence.High, null);
        Assert.Equal((false, true, "Modo manual: toda acción la decide una persona."), (manual.Blocked, manual.NeedsApproval, manual.Reason));
        GuardVerdict semi = AutomationRules.Evaluate(Semi, 10m, Nothing, LearningConfidence.High, null);
        Assert.Equal((false, true), (semi.Blocked, semi.NeedsApproval));
        Assert.StartsWith("Modo semiautomático: publicar es una acción crítica", semi.Reason);

        // Automático: sola si la respalda un aprendizaje de confianza media o alta; si no, datos insuficientes.
        AutomationLimits automatic = Semi with { Mode = AutomationMode.Automatic };
        Assert.Equal(new GuardVerdict(false, false, false, string.Empty), AutomationRules.Evaluate(automatic, 10m, Nothing, LearningConfidence.High, null));
        Assert.False(AutomationRules.Evaluate(automatic, 10m, Nothing, LearningConfidence.Medium, null).NeedsApproval);
        Assert.All(
            new LearningConfidence?[] { LearningConfidence.Low, null },
            evidence =>
            {
                GuardVerdict verdict = AutomationRules.Evaluate(automatic, 10m, Nothing, evidence, null);
                Assert.Equal((false, true), (verdict.Blocked, verdict.NeedsApproval));
                Assert.StartsWith("Datos insuficientes", verdict.Reason);
            });
        // El modo automático no salta los límites.
        Assert.True(AutomationRules.Evaluate(automatic, 60m, Nothing, LearningConfidence.High, null).Blocked);

        // Con anomalías sin revisar, ni el mejor respaldo alcanza para ejecutar sola.
        GuardVerdict uneasy = AutomationRules.Evaluate(automatic, 10m, Nothing, LearningConfidence.High, null, openAnomalies: 2);
        Assert.Equal(
            (false, true, "Hay 2 anomalía(s) sin revisar en las métricas: hasta revisarlas, toda acción la decide una persona."),
            (uneasy.Blocked, uneasy.NeedsApproval, uneasy.Reason));
    }

    [Fact]
    public void Las_anomalias_se_buscan_en_el_ultimo_dia_contra_los_anteriores()
    {
        // Salto de gasto: 3,5 veces el promedio de cuatro días.
        AnomalyFinding spike = Assert.Single(AnomalyRules.Detect(Days([10m, 10m, 10m, 10m, 35m])));
        Assert.Equal(
            new AnomalyFinding(
                AnomalyKind.SpendSpike, new DateOnly(2020, 3, 5),
                "El 2020-03-05 gastó 35.00, 3.5 veces el promedio de los 4 días anteriores (10.00). Puede ser un cambio de presupuesto hecho a propósito."),
            spike);
        // Por debajo de tres veces, con poca historia o con montos chicos no se señala nada.
        Assert.Empty(AnomalyRules.Detect(Days([10m, 10m, 10m, 10m, 29m])));
        Assert.Empty(AnomalyRules.Detect(Days([10m, 10m, 100m])));
        Assert.Empty(AnomalyRules.Detect(Days([1m, 1m, 1m, 4m])));
        Assert.Empty(AnomalyRules.Detect([]));

        // Caída del CTR: de 2,00 % a 0,50 %, con volumen.
        AnomalyFinding drop = Assert.Single(AnomalyRules.Detect(Days([10m, 10m, 10m, 10m], clicks: [40, 40, 40, 10])));
        Assert.Equal(
            (AnomalyKind.CtrDrop, "El 2020-03-04 el CTR fue 0.50 %, una caída del 75 % frente al 2.00 % de los 3 días anteriores. Puede deberse a un cambio de audiencia o de ubicación."),
            (drop.Kind, drop.Description));
        // Una caída menor a la mitad, o un último día con pocas impresiones, no alcanza.
        Assert.Empty(AnomalyRules.Detect(Days([10m, 10m, 10m, 10m], clicks: [40, 40, 40, 24])));
        Assert.Empty(AnomalyRules.Detect(Days([10m, 10m, 10m, 10m], clicks: [40, 40, 40, 0], lastImpressions: 500)));

        // Gasto sin impresiones: se ve aunque no haya historia.
        AnomalyFinding empty = Assert.Single(AnomalyRules.Detect(Days([5m], lastImpressions: 0)));
        Assert.Equal((AnomalyKind.SpendWithoutImpressions, "El 2020-03-01 gastó 5.00 sin ninguna impresión. Puede ser un error del informe."), (empty.Kind, empty.Description));
    }

    [Fact]
    public void Una_receta_aplica_una_hipotesis_si_usa_el_fragmento_en_su_lugar()
    {
        ComponentRef a = new(1, "uno.mp4", 0, 2000, 11, null);
        ComponentRef b = new(1, "uno.mp4", 2000, 4000, 12, null);
        ComponentRef c = new(2, "dos.mp4", 0, 3000, 21, null);
        AdComponents recipe = new() { Items = [a, b, c] };

        Assert.True(AutomationRules.Uses(recipe, ExperimentVariable.Opening, 1, 0, 2000));
        Assert.False(AutomationRules.Uses(recipe, ExperimentVariable.Opening, 1, 2000, 4000));
        Assert.True(AutomationRules.Uses(recipe, ExperimentVariable.Closing, 2, 0, 3000));
        // Un video de un solo fragmento no tiene cierre.
        Assert.False(AutomationRules.Uses(new AdComponents { Items = [c] }, ExperimentVariable.Closing, 2, 0, 3000));
        Assert.True(AutomationRules.Uses(recipe, ExperimentVariable.Development, 1, 2000, 4000));
        Assert.False(AutomationRules.Uses(recipe, ExperimentVariable.Development, 1, 0, 1999));
        // De la música o los subtítulos el sistema no sabe elegir un video por fragmento.
        Assert.False(AutomationRules.Uses(recipe, ExperimentVariable.Music, 1, 0, 2000));
    }

    [Fact]
    public async Task El_ciclo_completo_corre_en_simulacion_y_una_anomalia_le_quita_la_autonomia()
    {
        await fixture.ClearJobsAsync();
        fixture.Publisher.Reset();
        int org = await fixture.NewOrganizationAsync();
        await using AsyncServiceScope scope = fixture.Scope(org, null);
        AutomationService automation = scope.ServiceProvider.GetRequiredService<AutomationService>();
        AnomalyService anomalies = scope.ServiceProvider.GetRequiredService<AnomalyService>();
        CompositionService composition = scope.ServiceProvider.GetRequiredService<CompositionService>();
        MetaService meta = scope.ServiceProvider.GetRequiredService<MetaService>();
        HypothesisService hypotheses = scope.ServiceProvider.GetRequiredService<HypothesisService>();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // 1. Biblioteca y composición: cuatro videos; tres abren con la escena 1 y el cuarto queda sin publicar.
        long asset;
        await using (FileStream stream = File.OpenRead(fixture.SampleCutsVideoPath))
        {
            asset = (await scope.ServiceProvider.GetRequiredService<MediaLibraryService>()
                .IngestAsync("cortes.mp4", new MediaRightsBody { ReuseAuthorized = true }, stream, CancellationToken.None)).AssetId;
        }

        await DrainAsync();
        List<SegmentItem> scenes = (await scope.ServiceProvider.GetRequiredService<SceneService>().ListAsync(asset, CancellationToken.None)).Segments;
        long[][] montages = [[scenes[0].Id, scenes[1].Id], [scenes[0].Id, scenes[2].Id], [scenes[2].Id], [scenes[0].Id]];
        List<long> videos = [];
        foreach (long[] montage in montages)
        {
            long recipe = await composition.CreateAsync(
                new CreateRecipeBody { Name = $"Receta {videos.Count + 1}", Format = "1:1", Items = montage.Select(id => new RecipeItemBody { SegmentId = id }).ToList() },
                CancellationToken.None);
            videos.Add(await composition.RenderAsync(recipe, CancellationToken.None));
        }

        // 2. Métricas importadas y anuncios vinculados a sus videos.
        await UploadAsync(meta, "101,A,2020-03-01,10,10000,150,c1,Navidad\n102,B,2020-03-01,10,10000,150,c1,Navidad\n103,C,2020-03-01,10,10000,60,c2,Reyes\n104,D,2020-03-01,10,1000,10,c3,Pascua\n");
        Dictionary<string, long> ads = (await meta.OverviewAsync(new MetaFilter(), CancellationToken.None)).Ads.ToDictionary(a => a.ExternalId, a => a.Id);
        await meta.LinkAsync(ads["101"], videos[0], CancellationToken.None);
        await meta.LinkAsync(ads["102"], videos[1], CancellationToken.None);
        await meta.LinkAsync(ads["103"], videos[2], CancellationToken.None);
        Assert.Empty((await automation.OverviewAsync(CancellationToken.None)).Anomalies);

        // 3. Del análisis a una hipótesis, y de ahí a un aprendizaje (cargado directo: el camino con experimento
        //    está cubierto en LearningTests). Otra hipótesis, escrita a mano, también con respaldo.
        ComponentGroup opening = (await scope.ServiceProvider.GetRequiredService<PerformanceService>().AnalyzeAsync(new PerformanceFilter(), CancellationToken.None))
            .Dimensions.Single(d => d.Name == "opening").Groups.Single(g => g.Fragment!.SegmentId == scenes[0].Id);
        long fromPattern = await hypotheses.FromPatternAsync(new PatternBody { Dimension = "opening", Key = opening.Key }, CancellationToken.None);
        long manual = await hypotheses.CreateAsync(new CreateHypothesisBody { Statement = "El tono cercano funciona", Variable = ExperimentVariable.Other }, CancellationToken.None);
        foreach (long hypothesis in new[] { fromPattern, manual })
        {
            db.Learnings.Add(new Learning
            {
                HypothesisId = hypothesis, Statement = "x", DataUsed = "x", Result = "x", Confidence = LearningConfidence.High, ConfidenceReason = "x",
                Recommendation = "x", Applicability = "x", CriteriaVersion = LearningRules.CriteriaVersion, Status = LearningStatus.Active,
            });
        }

        await db.SaveChangesAsync();

        // 4. Prioridades y plan de producción: las dos se proponen aplicar.
        long plan = await scope.ServiceProvider.GetRequiredService<ProductionPlanService>()
            .CreateAsync(new CreateProductionPlanBody { Name = "Tanda", Count = 5 }, CancellationToken.None);
        ProductionPlanDetail planned = await scope.ServiceProvider.GetRequiredService<ProductionPlanService>().GetAsync(plan, CancellationToken.None);
        Assert.Equal([(fromPattern, PlanAction.Apply), (manual, PlanAction.Apply)], planned.Items.Select(i => (i.HypothesisId, i.Action)));

        // 5. El sistema propone las publicaciones del plan, con presupuesto por lote.
        Dictionary<string, long> adSets = (await automation.OverviewAsync(CancellationToken.None)).AdSets.ToDictionary(s => s.Label.Split(' ')[0], s => s.Id);
        AutomationPolicyBody policy = new() { Mode = AutomationMode.Automatic, MaxBudgetPerAction = 50m, MaxDailyBudget = 1000m, MaxActionsPerDay = 10 };
        await automation.SavePolicyAsync(policy, CancellationToken.None);
        ProposeFromPlanBody propose = new() { PlanId = plan, AdSetId = adSets["Pascua"], DailyBudget = 40m };
        await JobTests.AssertBusinessAsync(409, "BATCH_LIMIT_REQUIRED", () => automation.ProposeFromPlanAsync(propose, CancellationToken.None));
        policy.MaxBatchBudget = 30m;
        await automation.SavePolicyAsync(policy, CancellationToken.None);
        // El lote que excede su presupuesto se rechaza entero.
        await JobTests.AssertBusinessAsync(409, "BATCH_BUDGET_EXCEEDED", () => automation.ProposeFromPlanAsync(propose, CancellationToken.None));
        Assert.Empty((await automation.OverviewAsync(CancellationToken.None)).Actions);

        propose.DailyBudget = 20m;
        ProposalResult proposal = await automation.ProposeFromPlanAsync(propose, CancellationToken.None);

        // Una acción: el único video listo y sin publicar que abre con ese fragmento. La hipótesis escrita a mano queda afuera.
        long proposed = Assert.Single(proposal.ActionIds);
        Assert.Equal("«El tono cercano funciona»: no trata de un fragmento, así que el sistema no sabe qué video la aplica.", Assert.Single(proposal.Skipped));
        AutomationActionItem action = await ActionAsync(automation, proposed);
        // Modo automático, con respaldo y sin anomalías: no espera a nadie.
        Assert.Equal((videos[3], "Tanda · 1", 20m, plan, AutomationActionStatus.Approved, true), (action.VariantId, action.AdName, action.DailyBudget, action.PlanId!.Value, action.Status, action.IsSimulation));
        await DrainAsync();
        Assert.Equal(AutomationActionStatus.Simulated, (await ActionAsync(automation, proposed)).Status);
        // Proponer de nuevo no duplica.
        ProposalResult again = await automation.ProposeFromPlanAsync(propose, CancellationToken.None);
        Assert.Equal((0, 2), (again.ActionIds.Count, again.Skipped.Count));
        Assert.Contains("ya tiene una acción en pie en ese conjunto", again.Skipped[0]);

        // 6. Llegan métricas nuevas con un salto de gasto: la importación lo detecta.
        await UploadAsync(meta, "104,D,2020-03-02,10,1000,10,c3,Pascua\n104,D,2020-03-03,10,1000,10,c3,Pascua\n104,D,2020-03-04,10,1000,10,c3,Pascua\n104,D,2020-03-05,100,1000,10,c3,Pascua\n");
        AnomalyItem anomaly = Assert.Single((await automation.OverviewAsync(CancellationToken.None)).Anomalies);
        Assert.Equal((ads["104"], "D", AnomalyKind.SpendSpike, new DateOnly(2020, 3, 5), false), (anomaly.AdId, anomaly.AdName, anomaly.Kind, anomaly.Date, anomaly.Reviewed));
        Assert.StartsWith("El 2020-03-05 gastó 100.00, 10.0 veces el promedio de los 4 días anteriores (10.00).", anomaly.Description);
        Assert.Equal(0, await anomalies.ScanAsync(CancellationToken.None));

        // Con la anomalía abierta, la misma acción respaldada ya no se ejecuta sola.
        long held = await automation.RequestAsync(
            new PublishRequestBody { VariantId = videos[3], AdSetId = adSets["Navidad"], AdName = "Otra", DailyBudget = 20m, HypothesisId = fromPattern }, CancellationToken.None);
        AutomationActionItem waiting = await ActionAsync(automation, held);
        Assert.Equal(
            (AutomationActionStatus.PendingApproval, "Hay 1 anomalía(s) sin revisar en las métricas: hasta revisarlas, toda acción la decide una persona."),
            (waiting.Status, waiting.ApprovalReason));

        // 7. Una persona la revisa y el sistema recupera la autonomía.
        await anomalies.ReviewAsync(anomaly.Id, "  Subimos el presupuesto a propósito  ", CancellationToken.None);
        AnomalyItem reviewed = Assert.Single((await automation.OverviewAsync(CancellationToken.None)).Anomalies);
        Assert.Equal((true, "Subimos el presupuesto a propósito"), (reviewed.Reviewed, reviewed.ReviewNote));
        await JobTests.AssertBusinessAsync(404, "ANOMALY_NOT_FOUND", () => anomalies.ReviewAsync(anomaly.Id, "otra vez", CancellationToken.None));
        long free = await automation.RequestAsync(
            new PublishRequestBody { VariantId = videos[3], AdSetId = adSets["Reyes"], AdName = "Tercera", DailyBudget = 20m, HypothesisId = fromPattern }, CancellationToken.None);
        Assert.Equal(AutomationActionStatus.Approved, (await ActionAsync(automation, free)).Status);
        await DrainAsync();
        Assert.Equal(AutomationActionStatus.Simulated, (await ActionAsync(automation, free)).Status);

        // Todo el ciclo corrió sin una sola escritura en Meta.
        Assert.Empty(fixture.Publisher.Calls);
        Assert.Equal((1, 1), (await db.AuditEntries.CountAsync(a => a.Action == "automation.propose" && a.Summary.StartsWith("Propuso 1")), await db.AuditEntries.CountAsync(a => a.Action == "anomaly.review")));

        await using AsyncServiceScope otherScope = fixture.Scope(await fixture.NewOrganizationAsync());
        Assert.Empty(await otherScope.ServiceProvider.GetRequiredService<AnomalyService>().ListAsync(CancellationToken.None));
        await JobTests.AssertBusinessAsync(
            404, "PRODUCTION_PLAN_NOT_FOUND", () => otherScope.ServiceProvider.GetRequiredService<AutomationService>().ProposeFromPlanAsync(propose, CancellationToken.None));
    }

    // Días consecutivos desde el 2020-03-01, con 2.000 impresiones cada uno salvo que se indique otra cosa para el último.
    private static List<AnomalyDay> Days(decimal[] spend, long[]? clicks = null, long lastImpressions = 2_000)
    {
        return spend
            .Select((amount, index) => new AnomalyDay(
                new DateOnly(2020, 3, 1).AddDays(index), amount, index == spend.Length - 1 ? lastImpressions : 2_000, clicks?[index] ?? 20))
            .ToList();
    }

    private async Task UploadAsync(MetaService meta, string rows)
    {
        const string header =
            "Identificador del anuncio,Nombre del anuncio,Día,Importe gastado (USD),Impresiones,Clics en el enlace,Identificador de la campaña,Nombre de la campaña\n";
        await using (MemoryStream stream = new(Encoding.UTF8.GetBytes(header + rows)))
        {
            await meta.UploadAsync("informe.csv", "USD", stream, CancellationToken.None);
        }

        await DrainAsync();
    }

    [Fact]
    public void Aprobar_cambiar_la_politica_y_accionar_el_kill_switch_exigen_ser_administrador()
    {
        foreach (string name in new[] { "Approve", "Reject", "Policy", "RealExecution", "KillSwitch", "ReviewAnomaly" })
        {
            MethodInfo method = typeof(CreativeIntelligence.Web.Controllers.AutomationController).GetMethod(name)!;
            Assert.Equal(Policies.OrgAdmin, method.GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        }

        Assert.Equal(Policies.OrgEditor, typeof(CreativeIntelligence.Web.Controllers.AutomationController).GetMethod("RequestAction")!.GetCustomAttribute<AuthorizeAttribute>()!.Policy);
    }

    [Fact]
    public async Task Sin_habilitacion_explicita_todo_es_simulacion_y_con_ella_cada_guarda_sigue_valiendo()
    {
        await fixture.ClearJobsAsync();
        fixture.Publisher.Reset();
        int org = await fixture.NewOrganizationAsync();
        await using AsyncServiceScope scope = fixture.Scope(org, null);
        AutomationService automation = scope.ServiceProvider.GetRequiredService<AutomationService>();
        MediaLibraryService media = scope.ServiceProvider.GetRequiredService<MediaLibraryService>();
        CompositionService composition = scope.ServiceProvider.GetRequiredService<CompositionService>();
        MetaService meta = scope.ServiceProvider.GetRequiredService<MetaService>();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Un video generado y tres conjuntos de anuncios donde publicarlo.
        long asset;
        await using (FileStream stream = File.OpenRead(fixture.SampleCutsVideoPath))
        {
            asset = (await media.IngestAsync("cortes.mp4", new MediaRightsBody { ReuseAuthorized = true }, stream, CancellationToken.None)).AssetId;
        }

        await DrainAsync();
        List<SegmentItem> scenes = (await scope.ServiceProvider.GetRequiredService<SceneService>().ListAsync(asset, CancellationToken.None)).Segments;
        long recipe = await composition.CreateAsync(
            new CreateRecipeBody { Name = "Receta", Format = "1:1", Items = [new RecipeItemBody { SegmentId = scenes[0].Id }] }, CancellationToken.None);
        long video = await composition.RenderAsync(recipe, CancellationToken.None);
        const string csv =
            "Identificador del anuncio,Nombre del anuncio,Día,Importe gastado (USD),Impresiones,Clics en el enlace,Identificador de la campaña,Nombre de la campaña\n"
            + "1,Uno,2020-03-01,10,1000,10,c1,Alfa\n2,Dos,2020-03-01,10,1000,10,c2,Beta\n3,Tres,2020-03-01,10,1000,10,c3,Gama\n";
        await using (MemoryStream stream = new(Encoding.UTF8.GetBytes(csv)))
        {
            await meta.UploadAsync("informe.csv", "USD", stream, CancellationToken.None);
        }

        await DrainAsync();

        // --- Lo que rige sin tocar nada: manual, sin límites, simulación, y sin conexión no se puede habilitar ---
        AutomationOverview initial = await automation.OverviewAsync(CancellationToken.None);
        Assert.Equal(new AutomationPolicyView(AutomationMode.Manual, false, false, 0m, 0m, 0, 0m), initial.Policy);
        Assert.Equal((false, 1, 3), (initial.MetaConnected, initial.Variants.Count, initial.AdSets.Count));
        List<long> adSets = initial.AdSets.Select(s => s.Id).ToList();
        await JobTests.AssertBusinessAsync(409, "META_NOT_CONNECTED", () => automation.SetRealExecutionAsync(true, CancellationToken.None));

        long noLimits = await automation.RequestAsync(Publish(video, adSets[0], 10m), CancellationToken.None);
        AutomationActionItem blocked = await ActionAsync(automation, noLimits);
        Assert.Equal((AutomationActionStatus.Blocked, true), (blocked.Status, blocked.IsSimulation));
        Assert.StartsWith("Faltan límites", blocked.Result);

        // --- Modo manual con límites: 50 por acción, 100 por día, 3 acciones por día ---
        await automation.SavePolicyAsync(new AutomationPolicyBody { Mode = AutomationMode.Manual, MaxBudgetPerAction = 50m, MaxDailyBudget = 100m, MaxActionsPerDay = 3 }, CancellationToken.None);
        Assert.StartsWith("El presupuesto diario de 60.00 supera", (await ActionAsync(automation, await automation.RequestAsync(Publish(video, adSets[0], 60m), CancellationToken.None))).Result);

        long first = await automation.RequestAsync(Publish(video, adSets[0], 40m), CancellationToken.None);
        AutomationActionItem pending = await ActionAsync(automation, first);
        Assert.Equal((AutomationActionStatus.PendingApproval, "Modo manual: toda acción la decide una persona.", "Receta", "Alfa · Sin conjunto"), (pending.Status, pending.ApprovalReason, pending.RecipeName, pending.AdSetName));
        // Mientras espera no hay ningún trabajo: nada se ejecuta sin la aprobación.
        Assert.Equal(0, await db.BackgroundJobs.CountAsync(j => j.Type == AutomationExecuteJobHandler.JobType));
        // Protección contra duplicados: el mismo video en el mismo conjunto no se pide dos veces.
        await JobTests.AssertBusinessAsync(409, "DUPLICATE_ACTION", () => automation.RequestAsync(Publish(video, adSets[0], 5m), CancellationToken.None));

        await automation.ApproveAsync(first, "  Adelante  ", CancellationToken.None);
        await DrainAsync();
        AutomationActionItem simulated = await ActionAsync(automation, first);
        Assert.Equal((AutomationActionStatus.Simulated, "Adelante"), (simulated.Status, simulated.DecisionNote));
        Assert.Equal(
            "Simulación: se habría creado el anuncio \"Anuncio\" en el conjunto \"Alfa · Sin conjunto\" con un presupuesto diario de 40.00. No se escribió nada en Meta.",
            simulated.Result);
        await JobTests.AssertBusinessAsync(409, "ACTION_NOT_PENDING", () => automation.ApproveAsync(first, null, CancellationToken.None));
        await JobTests.AssertBusinessAsync(409, "DUPLICATE_ACTION", () => automation.RequestAsync(Publish(video, adSets[0], 5m), CancellationToken.None));

        // Presupuesto del día: 40 simulados + 40 pendientes; otros 40 pasarían de 100.
        long second = await automation.RequestAsync(Publish(video, adSets[1], 40m), CancellationToken.None);
        AutomationActionItem overDay = await ActionAsync(automation, await automation.RequestAsync(Publish(video, adSets[2], 40m), CancellationToken.None));
        Assert.Equal((AutomationActionStatus.Blocked, "Con esta acción el presupuesto comprometido hoy sería 120.00 y el máximo diario es 100.00."), (overDay.Status, overDay.Result));
        Assert.Equal(new DailyUsage(2, 80m), (await automation.OverviewAsync(CancellationToken.None)).Today);

        // Rechazar libera lo comprometido.
        await automation.RejectAsync(second, "No va", CancellationToken.None);
        Assert.Equal(AutomationActionStatus.Rejected, (await ActionAsync(automation, second)).Status);
        long third = await automation.RequestAsync(Publish(video, adSets[2], 40m), CancellationToken.None);
        Assert.Equal(AutomationActionStatus.PendingApproval, (await ActionAsync(automation, third)).Status);

        // --- Kill switch: cancela lo pendiente y lo que está en cola, y no acepta nada nuevo ---
        long queued = await automation.RequestAsync(Publish(video, adSets[1], 10m), CancellationToken.None);
        await automation.ApproveAsync(queued, null, CancellationToken.None);
        Assert.Equal(AutomationActionStatus.Approved, (await ActionAsync(automation, queued)).Status);
        Assert.Equal(2, await automation.KillSwitchAsync(true, "  Algo anda mal  ", CancellationToken.None));
        Assert.All(new[] { third, queued }, id => Assert.Equal(AutomationActionStatus.Cancelled, ActionAsync(automation, id).Result.Status));
        // El trabajo que ya estaba en cola corre y no hace nada.
        await DrainAsync();
        Assert.Equal((AutomationActionStatus.Cancelled, "Cancelada por el kill switch."), ((await ActionAsync(automation, queued)).Status, (await ActionAsync(automation, queued)).Result));
        AutomationActionItem refused = await ActionAsync(automation, await automation.RequestAsync(Publish(video, adSets[1], 10m), CancellationToken.None));
        Assert.Equal((AutomationActionStatus.Blocked, "El kill switch está activo: no se acepta ni se ejecuta ninguna acción."), (refused.Status, refused.Result));
        fixture.Publisher.IsConfigured = true;
        await JobTests.AssertBusinessAsync(409, "KILL_SWITCH_ACTIVE", () => automation.SetRealExecutionAsync(true, CancellationToken.None));
        Assert.Equal(0, await automation.KillSwitchAsync(false, "Resuelto", CancellationToken.None));
        Assert.Equal(
            [(false, "Resuelto", 0), (true, "Algo anda mal", 2)],
            (await automation.OverviewAsync(CancellationToken.None)).KillSwitch.Select(k => (k.Activated, k.Reason, k.CancelledActions)));
        // Desactivarlo no revive nada.
        Assert.Equal(AutomationActionStatus.Cancelled, (await ActionAsync(automation, third)).Status);

        // --- Límite de acciones por día y derechos ---
        await automation.SavePolicyAsync(new AutomationPolicyBody { Mode = AutomationMode.SemiAutomatic, MaxBudgetPerAction = 50m, MaxDailyBudget = 1000m, MaxActionsPerDay = 2 }, CancellationToken.None);
        long fourth = await automation.RequestAsync(Publish(video, adSets[1], 10m), CancellationToken.None);
        Assert.StartsWith("Modo semiautomático", (await ActionAsync(automation, fourth)).ApprovalReason);
        Assert.Equal("Ya hay 2 acciones hoy y el límite diario es 2.", (await ActionAsync(automation, await automation.RequestAsync(Publish(video, adSets[2], 10m), CancellationToken.None))).Result);
        await automation.RejectAsync(fourth, null, CancellationToken.None);
        await media.UpdateRightsAsync(asset, new MediaRightsBody { ReuseAuthorized = false }, CancellationToken.None);
        Assert.Equal(
            "\"cortes.mp4\" ya no tiene autorización de reutilización vigente: el video no se puede publicar.",
            (await ActionAsync(automation, await automation.RequestAsync(Publish(video, adSets[1], 10m), CancellationToken.None))).Result);
        await media.UpdateRightsAsync(asset, new MediaRightsBody { ReuseAuthorized = true }, CancellationToken.None);

        // Hasta acá todo fue simulación: el publicador no recibió ni una llamada.
        Assert.Empty(fixture.Publisher.Calls);

        // --- Ejecución real, habilitada a propósito, en modo automático ---
        await automation.SavePolicyAsync(new AutomationPolicyBody { Mode = AutomationMode.Automatic, MaxBudgetPerAction = 50m, MaxDailyBudget = 1000m, MaxActionsPerDay = 10 }, CancellationToken.None);
        await automation.SetRealExecutionAsync(true, CancellationToken.None);
        Assert.True((await automation.OverviewAsync(CancellationToken.None)).Policy.RealExecutionEnabled);

        // Sin respaldo en datos, ni el modo automático publica solo.
        long unsupported = await automation.RequestAsync(Publish(video, adSets[0], 20m), CancellationToken.None);
        AutomationActionItem waiting = await ActionAsync(automation, unsupported);
        Assert.Equal((AutomationActionStatus.PendingApproval, false), (waiting.Status, waiting.IsSimulation));
        Assert.StartsWith("Datos insuficientes", waiting.ApprovalReason);
        await DrainAsync();
        Assert.Empty(fixture.Publisher.Calls);

        await automation.ApproveAsync(unsupported, null, CancellationToken.None);
        await DrainAsync();
        AutomationActionItem executed = await ActionAsync(automation, unsupported);
        Assert.Equal(AutomationActionStatus.Executed, executed.Status);
        Assert.Equal("Se creó el anuncio \"Anuncio\" en Meta (identificador meta-ad-1) con un presupuesto diario de 20.00.", executed.Result);
        MetaPublishRequest call = Assert.Single(fixture.Publisher.Calls);
        Assert.Equal(($"ci-action-{unsupported}", "Anuncio", 20m), (call.IdempotencyKey, call.AdName, call.DailyBudget));
        // El anuncio queda registrado y vinculado a su video sin que nadie lo vincule a mano.
        MetaAd ad = await db.MetaAds.AsNoTracking().SingleAsync(a => a.ExternalId == "meta-ad-1");
        Assert.Equal((video, AdLinkOrigin.Automatic, adSets[0]), (ad.CreativeVariantId!.Value, ad.LinkOrigin!.Value, ad.MetaAdSetId));
        // Pedirlo otra vez no publica dos veces.
        await JobTests.AssertBusinessAsync(409, "DUPLICATE_ACTION", () => automation.RequestAsync(Publish(video, adSets[0], 20m), CancellationToken.None));
        Assert.Single(fixture.Publisher.Calls);

        // Con un aprendizaje vigente de confianza alta detrás, el modo automático ejecuta sin aprobación.
        long hypothesis = await scope.ServiceProvider.GetRequiredService<HypothesisService>()
            .CreateAsync(new CreateHypothesisBody { Statement = "Abrir así funciona", Variable = ExperimentVariable.Opening }, CancellationToken.None);
        // Preparación directa en la base: registrar un aprendizaje de confianza alta por el servicio exige un
        // experimento completo, que ya está cubierto en LearningTests.
        Learning learning = new()
        {
            HypothesisId = hypothesis, Statement = "x", DataUsed = "x", Result = "x", Confidence = LearningConfidence.High, ConfidenceReason = "x",
            Recommendation = "x", Applicability = "x", CriteriaVersion = LearningRules.CriteriaVersion, Status = LearningStatus.Active,
        };
        db.Learnings.Add(learning);
        await db.SaveChangesAsync();
        long supported = await automation.RequestAsync(Publish(video, adSets[1], 20m, hypothesis), CancellationToken.None);
        Assert.Equal((AutomationActionStatus.Approved, null), ((await ActionAsync(automation, supported)).Status, (await ActionAsync(automation, supported)).ApprovalReason));
        await DrainAsync();
        Assert.Equal(AutomationActionStatus.Executed, (await ActionAsync(automation, supported)).Status);
        Assert.Equal(2, fixture.Publisher.Calls.Count);

        // Si ese aprendizaje se invalida, la siguiente acción vuelve a necesitar a una persona.
        await db.Learnings.Where(l => l.Id == learning.Id).ExecuteUpdateAsync(s => s.SetProperty(l => l.Status, LearningStatus.Invalidated));
        long afterwards = await automation.RequestAsync(Publish(video, adSets[2], 20m, hypothesis), CancellationToken.None);
        Assert.Equal(AutomationActionStatus.PendingApproval, (await ActionAsync(automation, afterwards)).Status);

        // Deshabilitar la ejecución real después de aprobar frena la acción antes de escribir.
        await automation.ApproveAsync(afterwards, null, CancellationToken.None);
        await automation.SetRealExecutionAsync(false, CancellationToken.None);
        await DrainAsync();
        Assert.Equal(
            (AutomationActionStatus.Blocked, "La ejecución real se deshabilitó después de aprobarla: no se publicó."),
            ((await ActionAsync(automation, afterwards)).Status, (await ActionAsync(automation, afterwards)).Result));
        Assert.Equal(2, fixture.Publisher.Calls.Count);

        // Un rechazo de Meta deja la acción fallida, con el motivo.
        await automation.SetRealExecutionAsync(true, CancellationToken.None);
        fixture.Publisher.Errors.Enqueue(new MetaPublishException("El conjunto está archivado.", false));
        long rejected = await automation.RequestAsync(Publish(video, adSets[2], 20m), CancellationToken.None);
        await automation.ApproveAsync(rejected, null, CancellationToken.None);
        await DrainAsync();
        Assert.Equal(
            (AutomationActionStatus.Failed, "Meta rechazó la publicación: El conjunto está archivado."),
            ((await ActionAsync(automation, rejected)).Status, (await ActionAsync(automation, rejected)).Result));
        Assert.Equal(2, await db.MetaAds.CountAsync(a => a.LinkOrigin == AdLinkOrigin.Automatic));

        Assert.Equal(
            (2, 2, 3),
            (await db.AuditEntries.CountAsync(a => a.Action == "automation.kill-switch"), await db.AuditEntries.CountAsync(a => a.Action == "automation.reject"),
                await db.AuditEntries.CountAsync(a => a.Action == "automation.real-execution")));
        Assert.True(await db.AuditEntries.AnyAsync(a => a.Action == "automation.execute" && a.Summary.StartsWith("Simulated: Simulación")));

        // --- Otra organización: su propia política restrictiva y ninguna acción ajena ---
        await using AsyncServiceScope otherScope = fixture.Scope(await fixture.NewOrganizationAsync());
        AutomationService foreign = otherScope.ServiceProvider.GetRequiredService<AutomationService>();
        AutomationOverview other = await foreign.OverviewAsync(CancellationToken.None);
        Assert.Equal((false, AutomationMode.Manual, 0, 0), (other.Policy.RealExecutionEnabled, other.Policy.Mode, other.Actions.Count, other.AdSets.Count));
        await JobTests.AssertBusinessAsync(404, "ACTION_NOT_FOUND", () => foreign.ApproveAsync(first, null, CancellationToken.None));
        await JobTests.AssertBusinessAsync(404, "VARIANT_NOT_FOUND", () => foreign.RequestAsync(Publish(video, adSets[0], 10m), CancellationToken.None));
        await JobTests.AssertBusinessAsync(409, "LIMITS_REQUIRED", () => foreign.SetRealExecutionAsync(true, CancellationToken.None));
        fixture.Publisher.Reset();
    }

    private static (bool Blocked, bool Cancelled, string Reason) Blocked(GuardVerdict verdict)
    {
        return (verdict.Blocked, verdict.CancelledByKillSwitch, verdict.Reason);
    }

    private static PublishRequestBody Publish(long video, long adSet, decimal budget, long? hypothesis = null)
    {
        return new PublishRequestBody { VariantId = video, AdSetId = adSet, AdName = "  Anuncio  ", DailyBudget = budget, HypothesisId = hypothesis };
    }

    private static async Task<AutomationActionItem> ActionAsync(AutomationService automation, long id)
    {
        return (await automation.OverviewAsync(CancellationToken.None)).Actions.Single(a => a.Id == id);
    }

    private async Task DrainAsync()
    {
        JobRunner runner = fixture.Services.GetRequiredService<JobRunner>();
        while (await runner.ProcessNextAsync(CancellationToken.None))
        {
        }
    }
}

using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CreativeIntelligence.Tests;

/// <summary>Memoria de aprendizaje: confianza calculada, hipótesis desde patrones y ciclo de un aprendizaje
/// (registrar, revisar con datos nuevos, corregir, invalidar).</summary>
[Collection(DatabaseCollection.Name)]
public sealed class LearningTests(DatabaseFixture fixture)
{
    private const string Header =
        "Identificador del anuncio,Nombre del anuncio,Día,Importe gastado (USD),Impresiones,Clics en el enlace,Identificador de la campaña,Nombre de la campaña\n";

    private static readonly ComponentRef A = new(1, "uno.mp4", 0, 2000, 11, null);
    private static readonly ComponentRef B = new(1, "uno.mp4", 2000, 4000, 12, null);
    private static readonly ComponentRef C = new(2, "dos.mp4", 0, 3000, 21, null);

    [Fact]
    public void La_confianza_de_un_experimento_solo_es_alta_si_esta_limpio_terminado_y_la_diferencia_es_clara()
    {
        Assert.Equal(LearningConfidence.High, LearningRules.FromExperiment(Experiment(150))!.Confidence);
        Assert.Equal(LearningConfidence.Medium, LearningRules.FromExperiment(Experiment(132))!.Confidence);
        // No ver una diferencia es saber poco.
        Assert.Equal(LearningConfidence.Low, LearningRules.FromExperiment(Experiment(101))!.Confidence);
        // La misma diferencia clara, sin terminar o con problemas de diseño, baja a confianza baja.
        LearningBasis running = LearningRules.FromExperiment(Experiment(150, finished: false))!;
        Assert.Equal(LearningConfidence.Low, running.Confidence);
        Assert.StartsWith("Baja: el experimento no terminó", running.Reason);
        LearningBasis messy = LearningRules.FromExperiment(Experiment(150, campaign: 9))!;
        Assert.Equal(LearningConfidence.Low, messy.Confidence);
        Assert.StartsWith("Baja: el experimento tiene problemas de diseño", messy.Reason);
        // Sin el mínimo de impresiones no hay nada que aprender.
        Assert.Null(LearningRules.FromExperiment(Experiment(150, impressions: 4_000)));
    }

    [Fact]
    public void Un_patron_es_una_correlacion_y_su_confianza_nunca_llega_a_alta()
    {
        Assert.Null(LearningRules.FromPattern(Pattern(EvidenceLevel.Insufficient, SignalStrength.None)));
        // Con evidencia pero sin con qué comparar tampoco hay nada que afirmar.
        Assert.Null(LearningRules.FromPattern(Pattern(EvidenceLevel.Moderate, SignalStrength.None)));
        Assert.Equal(LearningConfidence.Medium, LearningRules.FromPattern(Pattern(EvidenceLevel.Moderate, SignalStrength.Clear))!.Confidence);
        Assert.Equal(LearningConfidence.Low, LearningRules.FromPattern(Pattern(EvidenceLevel.Preliminary, SignalStrength.Clear))!.Confidence);
        Assert.Equal(LearningConfidence.Low, LearningRules.FromPattern(Pattern(EvidenceLevel.Moderate, SignalStrength.Probable))!.Confidence);
        Assert.Equal(
            "Baja: es una correlación, con evidencia moderada y una diferencia que no se distingue del azar. No prueba una causa: falta un experimento.",
            LearningRules.FromPattern(Pattern(EvidenceLevel.Moderate, SignalStrength.NotDistinguishable))!.Reason);

        // Ningún motivo declara un ganador ni promete un resultado.
        List<string> reasons =
        [
            .. new[] { Experiment(150), Experiment(132), Experiment(101), Experiment(150, finished: false), Experiment(150, campaign: 9) }
                .Select(r => LearningRules.FromExperiment(r)!.Reason),
            LearningRules.FromPattern(Pattern(EvidenceLevel.Moderate, SignalStrength.Clear))!.Reason,
            LearningRules.FromPattern(Pattern(EvidenceLevel.Preliminary, SignalStrength.Probable))!.Reason,
        ];
        Assert.All(reasons, r => Assert.DoesNotContain("ganador", r, StringComparison.OrdinalIgnoreCase));
        Assert.All(reasons, r => Assert.DoesNotContain("ROAS", r, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Un_patron_se_enuncia_como_conjetura_y_el_estado_de_una_hipotesis_se_deduce()
    {
        Assert.Equal("Abrir con «uno.mp4 0:00,0–0:02,0» mejora el CTR frente a otras aperturas.", LearningRules.PatternStatement("opening", "uno.mp4 0:00,0–0:02,0", true));
        Assert.Equal("Cerrar con «x» empeora el CTR frente a otros cierres.", LearningRules.PatternStatement("closing", "x", false));
        Assert.Equal("Incluir «x» mejora el CTR.", LearningRules.PatternStatement("fragment", "x", true));
        Assert.Equal("La estructura «Hook → Cta» mejora el CTR frente a otras estructuras.", LearningRules.PatternStatement("structure", "Hook → Cta", true));
        Assert.Equal("«Con música» empeora el CTR frente al resto.", LearningRules.PatternStatement("music", "Con música", false));

        Assert.Equal(
            [ExperimentVariable.Opening, ExperimentVariable.Closing, ExperimentVariable.Development, ExperimentVariable.Subtitles, ExperimentVariable.Music, ExperimentVariable.Transition, ExperimentVariable.Other],
            new[] { "opening", "closing", "fragment", "subtitles", "music", "transition", "structure" }.Select(LearningRules.VariableOf));

        Assert.Equal(HypothesisState.Open, LearningRules.State(false, 0, 0, 0));
        Assert.Equal(HypothesisState.Testing, LearningRules.State(false, 2, 1, 0));
        Assert.Equal(HypothesisState.Tested, LearningRules.State(false, 2, 0, 0));
        Assert.Equal(HypothesisState.Learned, LearningRules.State(false, 2, 1, 1));
        Assert.Equal(HypothesisState.Discarded, LearningRules.State(true, 2, 1, 1));
    }

    [Fact]
    public async Task Un_aprendizaje_se_registra_con_sus_datos_se_revisa_con_datos_nuevos_se_corrige_y_se_invalida()
    {
        await fixture.ClearJobsAsync();
        int org = await fixture.NewOrganizationAsync();
        await using AsyncServiceScope scope = fixture.Scope(org);
        CompositionService composition = scope.ServiceProvider.GetRequiredService<CompositionService>();
        MetaService meta = scope.ServiceProvider.GetRequiredService<MetaService>();
        PerformanceService performance = scope.ServiceProvider.GetRequiredService<PerformanceService>();
        ExperimentService experiments = scope.ServiceProvider.GetRequiredService<ExperimentService>();
        HypothesisService hypotheses = scope.ServiceProvider.GetRequiredService<HypothesisService>();
        LearningService learnings = scope.ServiceProvider.GetRequiredService<LearningService>();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        long asset;
        await using (FileStream stream = File.OpenRead(fixture.SampleCutsVideoPath))
        {
            asset = (await scope.ServiceProvider.GetRequiredService<MediaLibraryService>()
                .IngestAsync("cortes.mp4", new MediaRightsBody { ReuseAuthorized = true }, stream, CancellationToken.None)).AssetId;
        }

        await DrainAsync();
        List<SegmentItem> scenes = (await scope.ServiceProvider.GetRequiredService<SceneService>().ListAsync(asset, CancellationToken.None)).Segments;

        // Tres creatividades: las dos primeras abren igual y sólo difieren en el cierre.
        long[][] montages = [[scenes[0].Id, scenes[1].Id], [scenes[0].Id, scenes[2].Id], [scenes[2].Id]];
        List<long> videos = [];
        foreach (long[] montage in montages)
        {
            long recipe = await composition.CreateAsync(
                new CreateRecipeBody { Name = $"Receta {videos.Count + 1}", Format = "1:1", Items = montage.Select(id => new RecipeItemBody { SegmentId = id }).ToList() },
                CancellationToken.None);
            videos.Add(await composition.RenderAsync(recipe, CancellationToken.None));
        }

        await UploadAsync(meta, "101,Cierra con escena 2,2020-03-01,10,10000,100,c1,Navidad\n102,Cierra con escena 3,2020-03-01,10,10000,150,c1,Navidad\n103,Sólo escena 3,2020-03-01,10,10000,60,c2,Reyes\n");
        Dictionary<string, long> ads = (await meta.OverviewAsync(new MetaFilter(), CancellationToken.None)).Ads.ToDictionary(a => a.ExternalId, a => a.Id);
        await meta.LinkAsync(ads["101"], videos[0], CancellationToken.None);
        await meta.LinkAsync(ads["102"], videos[1], CancellationToken.None);
        await meta.LinkAsync(ads["103"], videos[2], CancellationToken.None);

        // --- De un patrón a una hipótesis ---
        ComponentDimension opening = (await performance.AnalyzeAsync(new PerformanceFilter(), CancellationToken.None)).Dimensions.Single(d => d.Name == "opening");
        ComponentGroup sceneOne = opening.Groups.Single(g => g.Fragment!.SegmentId == scenes[0].Id);
        Assert.Equal([ads["101"], ads["102"]], sceneOne.AdIds);
        PatternBody pattern = new() { Dimension = "opening", Key = sceneOne.Key };

        long fromPattern = await hypotheses.FromPatternAsync(pattern, CancellationToken.None);

        // El mismo patrón no crea otra hipótesis, y uno sin evidencia no crea ninguna.
        Assert.Equal(fromPattern, await hypotheses.FromPatternAsync(pattern, CancellationToken.None));
        await JobTests.AssertBusinessAsync(
            409, "PATTERN_NO_EVIDENCE",
            () => hypotheses.FromPatternAsync(new PatternBody { Dimension = "opening", Key = opening.Groups.Single(g => g.Fragment!.SegmentId == scenes[2].Id).Key }, CancellationToken.None));
        await JobTests.AssertBusinessAsync(
            409, "PATTERN_NO_EVIDENCE", () => hypotheses.FromPatternAsync(new PatternBody { Dimension = "opening", Key = "no-existe" }, CancellationToken.None));
        HypothesisDetail suggested = await hypotheses.GetAsync(fromPattern, CancellationToken.None);
        Assert.Equal((HypothesisOrigin.Pattern, ExperimentVariable.Opening, HypothesisState.Open, true), (suggested.Origin, suggested.Variable, suggested.State, suggested.CanLearnFromPattern));
        Assert.StartsWith("Abrir con «cortes.mp4 0:00,0–", suggested.Statement);
        Assert.EndsWith("» mejora el CTR frente a otras aperturas.", suggested.Statement);
        Assert.Equal((sceneOne.Reading, sceneOne.Reading, EvidenceLevel.Preliminary, null), (suggested.Pattern!.ReadingThen, suggested.Pattern.ReadingNow, suggested.Pattern.EvidenceNow, suggested.Pattern.CampaignName));

        // --- De una hipótesis a un experimento limpio y a un aprendizaje de confianza alta ---
        long manual = await hypotheses.CreateAsync(new CreateHypothesisBody { Statement = "  Cerrar con la escena 3 sube el CTR  ", Variable = ExperimentVariable.Closing }, CancellationToken.None);
        ExperimentForm form = await experiments.FormAsync(manual, CancellationToken.None);
        Assert.Equal((manual, "Cerrar con la escena 3 sube el CTR", ExperimentVariable.Closing, 3), (form.HypothesisId, form.Hypothesis, form.Variable, form.Ads.Count));
        CreateExperimentBody body = new()
        {
            Name = "Cierre",
            Hypothesis = form.Hypothesis!,
            Variable = ExperimentVariable.Closing,
            Metric = ExperimentMetric.Ctr,
            StartDate = new DateOnly(2020, 3, 1),
            EndDate = new DateOnly(2020, 3, 2),
            MinImpressionsPerArm = 5000,
            HypothesisId = manual,
            Arms = [new ExperimentArmBody { AdId = ads["101"] }, new ExperimentArmBody { AdId = ads["102"] }],
        };
        long experiment = await experiments.CreateAsync(body, CancellationToken.None);
        Assert.Equal(manual, (await experiments.GetAsync(experiment, CancellationToken.None)).HypothesisId);
        Assert.Equal(HypothesisState.Testing, (await hypotheses.GetAsync(manual, CancellationToken.None)).State);

        LearningDraft draft = await learnings.DraftAsync(experiment, null, CancellationToken.None);
        Assert.Equal((experiment, manual, "Cierre", "Cerrar con la escena 3 sube el CTR", LearningConfidence.High), (draft.ExperimentId, draft.HypothesisId, draft.SourceName, draft.SuggestedStatement, draft.Confidence));
        Assert.Equal(
            "Experimento «Cierre»: 2 brazos (Cierra con escena 2, Cierra con escena 3), 20000 impresiones en total, con un mínimo de 5000 por brazo.",
            draft.DataUsed);
        long tested = await learnings.CreateAsync(
            new CreateLearningBody { ExperimentId = experiment, Statement = " Cerrar con la escena 3 subió el CTR ", Recommendation = " Usarla de cierre ", Applicability = " Navidad " },
            CancellationToken.None);

        LearningDetail detail = await learnings.GetAsync(tested, CancellationToken.None);
        Assert.Equal(
            ("Cerrar con la escena 3 subió el CTR", "Usarla de cierre", "Navidad", LearningConfidence.High, LearningStatus.Active, LearningRules.CriteriaVersion),
            (detail.Statement, detail.Recommendation, detail.Applicability, detail.Confidence, detail.Status, detail.CriteriaVersion));
        Assert.Equal((new DateOnly(2020, 3, 1), new DateOnly(2020, 3, 2), manual, experiment, "Cierre"), (detail.PeriodStart, detail.PeriodEnd, detail.HypothesisId, detail.ExperimentId, detail.ExperimentName));
        Assert.Contains("una diferencia clara a favor (+50 %)", detail.Result);
        Assert.Equal(
            [(ads["101"], "Cierra con escena 2", videos[0]), (ads["102"], "Cierra con escena 3", videos[1])],
            detail.Evidence.Select(e => (e.AdId!.Value, e.AdName!, e.VariantId!.Value)));
        // Los datos no cambiaron: nada que revisar.
        Assert.Equal(new LearningReview(LearningConfidence.High, detail.Result, false), detail.Review);
        Assert.Equal(HypothesisState.Learned, (await hypotheses.GetAsync(manual, CancellationToken.None)).State);

        // --- Un aprendizaje desde el patrón, sin experimento: confianza baja y el fragmento como evidencia ---
        LearningDraft correlational = await learnings.DraftAsync(null, fromPattern, CancellationToken.None);
        Assert.Equal((null, fromPattern, LearningConfidence.Low, sceneOne.Reading), (correlational.ExperimentId, correlational.HypothesisId, correlational.Confidence, correlational.Result));
        Assert.StartsWith("Patrón del análisis por componente sobre todas las campañas: 2 anuncios de 1 campaña(s) con «cortes.mp4 0:00,0–", correlational.DataUsed);
        Assert.EndsWith("» (20000 impresiones), comparados con el resto (10000 impresiones).", correlational.DataUsed);
        long observed = await learnings.CreateAsync(
            new CreateLearningBody { HypothesisId = fromPattern, Statement = "Abrir con la escena 1 acompaña un CTR mayor", Recommendation = "Probarla", Applicability = "Sin confirmar" },
            CancellationToken.None);
        LearningDetail observedDetail = await learnings.GetAsync(observed, CancellationToken.None);
        Assert.Equal(3, observedDetail.Evidence.Count);
        Assert.Equal((asset, "cortes.mp4", scenes[0].StartMs, scenes[0].EndMs), (observedDetail.Evidence[2].AssetId!.Value, observedDetail.Evidence[2].FileName!, observedDetail.Evidence[2].StartMs!.Value, observedDetail.Evidence[2].EndMs!.Value));
        Assert.False(observedDetail.Review!.Changed);
        Assert.Equal([observed, tested], (await learnings.ListAsync(LearningStatus.Active, CancellationToken.None)).Select(l => l.Id));
        Assert.Equal([false, true], (await learnings.ListAsync(null, CancellationToken.None)).Select(l => l.FromExperiment));

        // --- Priorización con datos reales: cada factor sale de lo que hay en la base ---
        PriorityBoard board = await scope.ServiceProvider.GetRequiredService<PriorityService>().BoardAsync(CancellationToken.None);
        Assert.Equal([fromPattern, manual], board.Items.Select(i => i.HypothesisId));
        // Del patrón: CTR muy superior (acotado), aprendizaje de confianza baja, 2 de 3 recetas ya abren así, 2 anuncios
        // publicados, original autorizado y ningún anuncio con fatiga. Lo que valora una persona, sin dato.
        Assert.Equal(
            [1m, 0.33m, 0.33m, 0.33m, null, 1m, null, null, 1m, null],
            board.Items[0].Factors.Select(f => f.Value is null ? (decimal?)null : Math.Round(f.Value.Value, 2)));
        Assert.Equal("2 de 3 recetas ya lo usan; cuanto más repetido, menos suma.", board.Items[0].Factors[2].Explanation);
        // La escrita a mano: aprendizaje de confianza alta y un experimento; del resto el sistema no sabe.
        Assert.Equal([null, 1m, null, 0.5m, null, null, null, null, null, null], board.Items[1].Factors.Select(f => f.Value));
        Assert.Equal((HypothesisState.Learned, HypothesisState.Learned, 43.9m, 20.2m), (board.Items[0].State, board.Items[1].State, board.Items[0].Score, board.Items[1].Score));

        // --- Plan de producción: la respaldada por un experimento se aplica; la del patrón, se prueba ---
        ProductionPlanService plans = scope.ServiceProvider.GetRequiredService<ProductionPlanService>();
        long firstPlan = await plans.CreateAsync(new CreateProductionPlanBody { Name = "Con evidencia", Count = 5 }, CancellationToken.None);
        ProductionPlanDetail planned = await plans.GetAsync(firstPlan, CancellationToken.None);
        Assert.Equal(
            [(fromPattern, PlanAction.Test, 43.9m, observed, "Probarla", LearningStatus.Active), (manual, PlanAction.Apply, 20.2m, tested, "Usarla de cierre", LearningStatus.Active)],
            planned.Items.Select(i => (i.HypothesisId, i.Action, i.Score, i.LearningId!.Value, i.Recommendation!, i.LearningStatus!.Value)));
        // La elección del hook se explica con su fragmento y su evidencia.
        Assert.Equal((ExperimentVariable.Opening, asset, scenes[0].StartMs, scenes[0].EndMs), (planned.Items[0].Variable, planned.Items[0].AssetId!.Value, planned.Items[0].StartMs!.Value, planned.Items[0].EndMs!.Value));
        Assert.StartsWith("Apertura (hook): «cortes.mp4 0:00,0–", planned.Items[0].Justification);
        Assert.Contains("Se propone probarlo con un experimento antes de producir en cantidad: el aprendizaje vigente es de confianza baja («Abrir con la escena 1 acompaña un CTR mayor»).", planned.Items[0].Justification);
        Assert.Contains("rendimiento histórico — CTR 1.25 % en 2 anuncios que lo usan frente a 0.60 % del resto. Es una correlación.", planned.Items[0].Justification);
        Assert.StartsWith("Cierre. Se propone producir aplicándolo: lo respalda un aprendizaje vigente de confianza alta («Cerrar con la escena 3 subió el CTR»).", planned.Items[1].Justification);
        // Y el planificador de conceptos recibe esos mismos aprendizajes vigentes.
        Assert.Equal(
            [("Abrir con la escena 1 acompaña un CTR mayor", LearningConfidence.Low), ("Cerrar con la escena 3 subió el CTR", LearningConfidence.High)],
            (await learnings.ForPlanAsync(CancellationToken.None)).Select(l => (l.Statement, l.Confidence)));

        // --- Llegan datos nuevos: el experimento ya no muestra diferencia ---
        await UploadAsync(meta, "102,Cierra con escena 3,2020-03-01,10,10000,100,c1,Navidad\n");
        LearningReview review = (await learnings.GetAsync(tested, CancellationToken.None)).Review!;
        Assert.Equal((LearningConfidence.Low, true), (review.Confidence, review.Changed));
        Assert.Contains("ningún brazo se distingue del control", review.Result);

        long corrected = await learnings.CorrectAsync(
            tested,
            new CorrectLearningBody { Statement = "El cierre no cambió el CTR", Recommendation = "No priorizar el cierre", Applicability = "Navidad", Reason = " Se reimportó el informe " },
            CancellationToken.None);

        LearningDetail previous = await learnings.GetAsync(tested, CancellationToken.None);
        Assert.Equal((LearningStatus.Corrected, "Se reimportó el informe", corrected, null), (previous.Status, previous.StatusReason, previous.ReplacedById, previous.Review));
        Assert.NotNull(previous.StatusChangedAt);
        // La versión anterior conserva lo que decía cuando se registró.
        Assert.Equal((LearningConfidence.High, "Cerrar con la escena 3 subió el CTR"), (previous.Confidence, previous.Statement));
        LearningDetail current = await learnings.GetAsync(corrected, CancellationToken.None);
        Assert.Equal((LearningStatus.Active, LearningConfidence.Low, tested, false, 2), (current.Status, current.Confidence, current.ReplacesId, current.Review!.Changed, current.Evidence.Count));
        Assert.Equal([corrected, observed], (await learnings.ListAsync(LearningStatus.Active, CancellationToken.None)).Select(l => l.Id));
        await JobTests.AssertBusinessAsync(409, "LEARNING_NOT_ACTIVE", () => learnings.CorrectAsync(tested, new CorrectLearningBody { Statement = "x", Recommendation = "x", Applicability = "x", Reason = "x" }, CancellationToken.None));

        // --- El patrón pierde su sustento: un anuncio se desvincula y ya no hay evidencia ---
        await meta.LinkAsync(ads["102"], null, CancellationToken.None);
        Assert.Equal(new LearningReview(null, null, true), (await learnings.GetAsync(observed, CancellationToken.None)).Review);
        Assert.False((await hypotheses.GetAsync(fromPattern, CancellationToken.None)).CanLearnFromPattern);
        // Sin datos no se puede corregir: corresponde invalidar.
        await JobTests.AssertBusinessAsync(409, "LEARNING_NO_EVIDENCE", () => learnings.CorrectAsync(observed, new CorrectLearningBody { Statement = "x", Recommendation = "x", Applicability = "x", Reason = "x" }, CancellationToken.None));
        await JobTests.AssertBusinessAsync(409, "LEARNING_NO_EVIDENCE", () => learnings.DraftAsync(null, fromPattern, CancellationToken.None));

        await learnings.InvalidateAsync(observed, "  El anuncio se desvinculó  ", CancellationToken.None);

        LearningDetail invalid = await learnings.GetAsync(observed, CancellationToken.None);
        Assert.Equal((LearningStatus.Invalidated, "El anuncio se desvinculó", null), (invalid.Status, invalid.StatusReason, invalid.Review));
        // Un aprendizaje invalidado deja de estar entre los vigentes, y su hipótesis vuelve a estar sin probar.
        Assert.Equal([corrected], (await learnings.ListAsync(LearningStatus.Active, CancellationToken.None)).Select(l => l.Id));
        Assert.Equal([observed], (await learnings.ListAsync(LearningStatus.Invalidated, CancellationToken.None)).Select(l => l.Id));
        Assert.Equal(3, (await learnings.ListAsync(null, CancellationToken.None)).Count);
        Assert.Equal(HypothesisState.Open, (await hypotheses.GetAsync(fromPattern, CancellationToken.None)).State);
        await JobTests.AssertBusinessAsync(409, "LEARNING_NOT_ACTIVE", () => learnings.InvalidateAsync(observed, "otra vez", CancellationToken.None));

        // --- Estados de la hipótesis probada, descarte y fuentes inválidas ---
        await experiments.ConcludeAsync(experiment, "Sin diferencia.", CancellationToken.None);
        await learnings.InvalidateAsync(corrected, "Se repite el experimento", CancellationToken.None);
        HypothesisDetail after = await hypotheses.GetAsync(manual, CancellationToken.None);
        Assert.Equal((HypothesisState.Tested, 1, 2, false), (after.State, after.Experiments.Count, after.Learnings.Count, after.CanLearnFromPattern));
        // --- Los aprendizajes invalidados dejan de influir en el plan siguiente ---
        Assert.Empty(await learnings.ForPlanAsync(CancellationToken.None));
        long secondPlan = await plans.CreateAsync(new CreateProductionPlanBody { Name = "Sin respaldo", Count = 5 }, CancellationToken.None);
        ProductionPlanDetail unsupported = await plans.GetAsync(secondPlan, CancellationToken.None);
        // Ya no se propone aplicar ni probar con respaldo: las dos quedan como exploración, sin aprendizaje y con menos puntaje.
        Assert.Equal(
            [(fromPattern, PlanAction.Explore, 26.6m, null), (manual, PlanAction.Explore, 3.2m, null)],
            unsupported.Items.Select(i => (i.HypothesisId, i.Action, i.Score, i.LearningId)));
        Assert.All(unsupported.Items, i => Assert.DoesNotContain("aprendizaje vigente", i.Justification));
        // El plan anterior conserva lo que decía, y avisa que sus aprendizajes ya no están vigentes.
        ProductionPlanDetail stale = await plans.GetAsync(firstPlan, CancellationToken.None);
        Assert.Equal(
            [(PlanAction.Test, 43.9m, LearningStatus.Invalidated), (PlanAction.Apply, 20.2m, LearningStatus.Corrected)],
            stale.Items.Select(i => (i.Action, i.Score, i.LearningStatus!.Value)));

        // Una hipótesis escrita a mano no tiene datos propios: sin experimento no deja un aprendizaje.
        await JobTests.AssertBusinessAsync(409, "LEARNING_NO_EVIDENCE", () => learnings.DraftAsync(null, manual, CancellationToken.None));
        await JobTests.AssertBusinessAsync(400, "LEARNING_SOURCE", () => learnings.DraftAsync(null, null, CancellationToken.None));
        await hypotheses.DiscardAsync(fromPattern, " Ya no aplica ", CancellationToken.None);
        HypothesisDetail discarded = await hypotheses.GetAsync(fromPattern, CancellationToken.None);
        Assert.Equal((HypothesisState.Discarded, "Ya no aplica"), (discarded.State, discarded.DiscardReason));
        Assert.Equal(
            [(manual, HypothesisState.Tested, 1, 0), (fromPattern, HypothesisState.Discarded, 0, 0)],
            (await hypotheses.ListAsync(CancellationToken.None)).Select(h => (h.Id, h.State, h.Experiments, h.ActiveLearnings)));
        body.HypothesisId = long.MaxValue;
        await JobTests.AssertBusinessAsync(404, "HYPOTHESIS_NOT_FOUND", () => experiments.CreateAsync(body, CancellationToken.None));
        Assert.Equal(
            (2, 2, 1, 2),
            (await db.AuditEntries.CountAsync(a => a.Action == "hypothesis.create"), await db.AuditEntries.CountAsync(a => a.Action == "learning.create"),
                await db.AuditEntries.CountAsync(a => a.Action == "learning.correct"), await db.AuditEntries.CountAsync(a => a.Action == "learning.invalidate")));

        // --- Otra organización no ve nada ---
        await using AsyncServiceScope otherScope = fixture.Scope(await fixture.NewOrganizationAsync());
        HypothesisService foreignHypotheses = otherScope.ServiceProvider.GetRequiredService<HypothesisService>();
        LearningService foreignLearnings = otherScope.ServiceProvider.GetRequiredService<LearningService>();
        Assert.Empty(await foreignHypotheses.ListAsync(CancellationToken.None));
        Assert.Empty(await foreignLearnings.ListAsync(null, CancellationToken.None));
        await JobTests.AssertBusinessAsync(404, "HYPOTHESIS_NOT_FOUND", () => foreignHypotheses.GetAsync(manual, CancellationToken.None));
        await JobTests.AssertBusinessAsync(404, "HYPOTHESIS_NOT_FOUND", () => foreignHypotheses.DiscardAsync(manual, "x", CancellationToken.None));
        await JobTests.AssertBusinessAsync(409, "PATTERN_NO_EVIDENCE", () => foreignHypotheses.FromPatternAsync(pattern, CancellationToken.None));
        await JobTests.AssertBusinessAsync(404, "LEARNING_NOT_FOUND", () => foreignLearnings.GetAsync(corrected, CancellationToken.None));
        await JobTests.AssertBusinessAsync(404, "LEARNING_NOT_FOUND", () => foreignLearnings.InvalidateAsync(corrected, "x", CancellationToken.None));
        await JobTests.AssertBusinessAsync(404, "EXPERIMENT_NOT_FOUND", () => foreignLearnings.DraftAsync(experiment, null, CancellationToken.None));
        await JobTests.AssertBusinessAsync(404, "HYPOTHESIS_NOT_FOUND", () => foreignLearnings.DraftAsync(null, fromPattern, CancellationToken.None));
    }

    // Un experimento de dos brazos sobre la apertura: el control con 1,00 % de CTR y la variante con los clics dados.
    private static ExperimentResult Experiment(long variantClicks, bool finished = true, long campaign = 1, long impressions = 10_000)
    {
        return ExperimentRules.Evaluate(
            ExperimentMetric.Ctr, ExperimentVariable.Opening,
            [
                new ArmData("Control", 1, "a", 1, new AdMetrics(50m, 10_000, 100, null, null, null, null, 4), new AdComponents { Items = [A, B] }),
                new ArmData("Variante", 2, "b", campaign, new AdMetrics(50m, impressions, variantClicks, null, null, null, null, 4), new AdComponents { Items = [C, B] }),
            ],
            5_000, finished);
    }

    private static ComponentGroup Pattern(EvidenceLevel evidence, SignalStrength signal)
    {
        AdMetrics metrics = new(100m, 60_000, 900, null, null, null, null, 5);
        return new ComponentGroup("k", "Con música", null, 5, 2, metrics, metrics, evidence, signal, true, "lectura", [1, 2, 3, 4, 5]);
    }

    private async Task UploadAsync(MetaService meta, string rows)
    {
        await using (MemoryStream stream = new(Encoding.UTF8.GetBytes(Header + rows)))
        {
            await meta.UploadAsync("informe.csv", "USD", stream, CancellationToken.None);
        }

        await DrainAsync();
    }

    private async Task DrainAsync()
    {
        JobRunner runner = fixture.Services.GetRequiredService<JobRunner>();
        while (await runner.ProcessNextAsync(CancellationToken.None))
        {
        }
    }
}

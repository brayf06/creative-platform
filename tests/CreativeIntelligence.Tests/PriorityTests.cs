using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CreativeIntelligence.Tests;

/// <summary>Motor de priorización: valor de cada factor, puntaje determinista y ponderaciones versionadas.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class PriorityTests(DatabaseFixture fixture)
{
    [Fact]
    public void El_puntaje_es_determinista_para_unos_factores_y_unas_ponderaciones_dadas()
    {
        OpportunityInput input = new()
        {
            // CTR 0,90 % contra 0,60 % del resto: +50 %.
            Pattern = Pattern(EvidenceLevel.Preliminary, SignalStrength.Clear, ads: 2, clicks: 180, restClicks: 60),
            Experiments = 1,
            RecipesUsing = 1,
            Recipes = 4,
            MaterialAvailable = true,
            FatiguedAds = 1,
            Assessment = new HypothesisAssessment(AssessmentLevel.High, AssessmentLevel.High, null, AssessmentLevel.Medium),
        };

        List<FactorValue> factors = PriorityRules.Factors(input);

        Assert.Equal(PriorityRules.All, factors.Select(f => f.Factor));
        // Rendimiento, evidencia, similitud, novedad, público, material, costo (alto resta), riesgo (sin valorar), fatiga, margen.
        Assert.Equal([0.75m, 0.15m, 0.75m, 0.25m, 1m, 1m, 0m, null, 0.5m, 0.5m], factors.Select(f => f.Value));
        Assert.Equal("CTR 0.90 % en 2 anuncios que lo usan frente a 0.60 % del resto. Es una correlación.", factors[0].Explanation);
        Assert.Equal("1 de 4 recetas ya lo usan; cuanto más repetido, menos suma.", factors[2].Explanation);
        Assert.Equal("Costo de producción alto, según la valoración de una persona.", factors[6].Explanation);
        Assert.Equal("Sin dato: nadie lo valoró en la hipótesis.", factors[7].Explanation);

        // (6×0,75 + 8×0,15 + 3×0,75 + 3×0,25 + 5×1 + 6×1 + 3×0 + 5×0 + 4×0,5 + 4×0,5) / 47 = 23,7 / 47.
        Assert.Equal(50.4m, PriorityRules.Score(PriorityRules.DefaultWeights, factors));
        Assert.Equal(50.4m, PriorityRules.Score(PriorityRules.DefaultWeights, PriorityRules.Factors(input)));
        // Otras ponderaciones, otro puntaje: sólo la evidencia.
        Assert.Equal(15m, PriorityRules.Score(new PriorityWeights(7, 0, 10, 0, 0, 0, 0, 0, 0, 0, 0), factors));
        Assert.Equal(0m, PriorityRules.Score(new PriorityWeights(8, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0), factors));
    }

    [Fact]
    public void Un_factor_sin_dato_no_suma_y_ninguna_explicacion_promete_resultados()
    {
        // Una conjetura escrita a mano: sólo se sabe que no tiene evidencia y que nadie la probó.
        List<FactorValue> conjecture = PriorityRules.Factors(new OpportunityInput());
        Assert.Equal([null, 0m, null, 1m, null, null, null, null, null, null], conjecture.Select(f => f.Value));
        Assert.Equal(6.4m, PriorityRules.Score(PriorityRules.DefaultWeights, conjecture));

        // La calidad de la evidencia sale del mejor aprendizaje vigente.
        Assert.Equal(1m, PriorityRules.Factors(new OpportunityInput { BestConfidence = LearningConfidence.High })[1].Value);
        Assert.Equal(0.66m, PriorityRules.Factors(new OpportunityInput { BestConfidence = LearningConfidence.Medium })[1].Value);
        Assert.Equal(0.33m, PriorityRules.Factors(new OpportunityInput { BestConfidence = LearningConfidence.Low })[1].Value);
        Assert.Equal(0.25m, PriorityRules.Factors(new OpportunityInput { Pattern = Pattern(EvidenceLevel.Moderate, SignalStrength.Clear) })[1].Value);

        // Un patrón sin evidencia no da rendimiento histórico; uno con CTR muy inferior queda en cero.
        List<FactorValue> weak = PriorityRules.Factors(new OpportunityInput { Pattern = Pattern(EvidenceLevel.Insufficient, SignalStrength.None), MaterialAvailable = false });
        Assert.Equal((null, 0m, 0m), (weak[0].Value, weak[1].Value, weak[5].Value));
        Assert.Equal(0m, PriorityRules.Factors(new OpportunityInput { Pattern = Pattern(EvidenceLevel.Preliminary, SignalStrength.Clear, clicks: 0) })[0].Value);
        // Una diferencia enorme no vale más que el doble: se acota.
        Assert.Equal(1m, PriorityRules.Factors(new OpportunityInput { Pattern = Pattern(EvidenceLevel.Preliminary, SignalStrength.Clear, clicks: 2000) })[0].Value);
        // Todos los anuncios con fatiga: el factor queda en cero.
        Assert.Equal(0m, PriorityRules.Factors(new OpportunityInput { Pattern = Pattern(EvidenceLevel.Preliminary, SignalStrength.Clear, ads: 2), FatiguedAds = 2 })[8].Value);
        // Costo y riesgo bajos suman; relevancia y margen bajos, no.
        List<FactorValue> low = PriorityRules.Factors(new OpportunityInput { Assessment = new HypothesisAssessment(AssessmentLevel.Low, AssessmentLevel.Low, AssessmentLevel.Low, AssessmentLevel.Low) });
        Assert.Equal((0m, 1m, 1m, 0m), (low[4].Value, low[6].Value, low[7].Value, low[9].Value));

        List<string> texts = [.. conjecture.Select(f => f.Explanation), .. weak.Select(f => f.Explanation), .. low.Select(f => f.Explanation)];
        Assert.All(texts, t => Assert.DoesNotContain("ganador", t, StringComparison.OrdinalIgnoreCase));
        Assert.All(texts, t => Assert.DoesNotContain("ROAS", t, StringComparison.OrdinalIgnoreCase));
        Assert.All(texts, t => Assert.DoesNotContain("predic", t, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Cambiar_las_ponderaciones_crea_una_version_nueva_y_reordena_las_hipotesis()
    {
        int org = await fixture.NewOrganizationAsync();
        await using AsyncServiceScope scope = fixture.Scope(org);
        HypothesisService hypotheses = scope.ServiceProvider.GetRequiredService<HypothesisService>();
        PriorityService priorities = scope.ServiceProvider.GetRequiredService<PriorityService>();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Sin nada guardado rigen las ponderaciones de fábrica.
        PriorityWeightsView initial = await priorities.WeightsAsync(CancellationToken.None);
        Assert.Equal((PriorityRules.DefaultWeights, 0), (initial.Current, initial.History.Count));
        Assert.Empty((await priorities.BoardAsync(CancellationToken.None)).Items);

        long plain = await hypotheses.CreateAsync(new CreateHypothesisBody { Statement = "Sin valorar", Variable = ExperimentVariable.Other }, CancellationToken.None);
        long assessed = await hypotheses.CreateAsync(new CreateHypothesisBody { Statement = "Valorada", Variable = ExperimentVariable.Other }, CancellationToken.None);
        long dropped = await hypotheses.CreateAsync(new CreateHypothesisBody { Statement = "Descartada", Variable = ExperimentVariable.Other }, CancellationToken.None);
        await hypotheses.DiscardAsync(dropped, "No aplica", CancellationToken.None);
        AssessHypothesisBody assessment = new()
        {
            AudienceRelevance = AssessmentLevel.High, ProductionCost = AssessmentLevel.Low, AdRisk = AssessmentLevel.Low, PotentialMargin = AssessmentLevel.High,
        };
        await hypotheses.AssessAsync(assessed, assessment, CancellationToken.None);
        Assert.Equal(
            new HypothesisAssessment(AssessmentLevel.High, AssessmentLevel.Low, AssessmentLevel.Low, AssessmentLevel.High),
            (await hypotheses.GetAsync(assessed, CancellationToken.None)).Assessment);

        PriorityBoard board = await priorities.BoardAsync(CancellationToken.None);

        // La descartada no entra. La valorada: novedad 3 + público 5 + costo 3 + riesgo 5 + margen 4 = 20 de 47.
        Assert.Equal(
            [(assessed, 42.6m, 6, HypothesisState.Open), (plain, 6.4m, 2, HypothesisState.Open)],
            board.Items.Select(i => (i.HypothesisId, i.Score, i.FactorsWithData, i.State)));
        Assert.Equal(0, board.Weights.Version);

        // Pesos nuevos: sólo cuenta la novedad, y las dos empatan (gana la más vieja).
        PriorityWeightsBody weights = new() { Novelty = 10, Note = "  Probar sólo novedad  " };
        Assert.Equal(1, await priorities.SaveWeightsAsync(weights, CancellationToken.None));
        PriorityBoard reordered = await priorities.BoardAsync(CancellationToken.None);
        Assert.Equal(1, reordered.Weights.Version);
        Assert.Equal([(plain, 100m), (assessed, 100m)], reordered.Items.Select(i => (i.HypothesisId, i.Score)));

        // Otro cambio crea otra versión; la anterior queda como estaba.
        weights.Novelty = 0;
        weights.AdRisk = 4;
        weights.Note = "Sólo riesgo";
        Assert.Equal(2, await priorities.SaveWeightsAsync(weights, CancellationToken.None));
        PriorityWeightsView view = await priorities.WeightsAsync(CancellationToken.None);
        Assert.Equal(new PriorityWeights(2, 0, 0, 0, 0, 0, 0, 0, 4, 0, 0), view.Current);
        Assert.Equal(
            [(new PriorityWeights(2, 0, 0, 0, 0, 0, 0, 0, 4, 0, 0), "Sólo riesgo"), (new PriorityWeights(1, 0, 0, 0, 10, 0, 0, 0, 0, 0, 0), "Probar sólo novedad")],
            view.History.Select(h => (h.Weights, h.Note)));
        Assert.Equal([(assessed, 100m), (plain, 0m)], (await priorities.BoardAsync(CancellationToken.None)).Items.Select(i => (i.HypothesisId, i.Score)));

        // Quitar una valoración la deja sin dato.
        assessment.AdRisk = null;
        await hypotheses.AssessAsync(assessed, assessment, CancellationToken.None);
        Assert.All((await priorities.BoardAsync(CancellationToken.None)).Items, i => Assert.Equal(0m, i.Score));

        weights.AdRisk = 0;
        await JobTests.AssertBusinessAsync(400, "WEIGHTS_INVALID", () => priorities.SaveWeightsAsync(weights, CancellationToken.None));
        Assert.Equal(
            (2, 2),
            (await db.AuditEntries.CountAsync(a => a.Action == "priority.weights"), await db.AuditEntries.CountAsync(a => a.Action == "hypothesis.assess")));

        // Otra organización sigue con las de fábrica y no ve nada.
        await using AsyncServiceScope otherScope = fixture.Scope(await fixture.NewOrganizationAsync());
        PriorityService foreign = otherScope.ServiceProvider.GetRequiredService<PriorityService>();
        Assert.Equal((0, 0), ((await foreign.WeightsAsync(CancellationToken.None)).Current.Version, (await foreign.BoardAsync(CancellationToken.None)).Items.Count));
        Assert.Equal(1, await foreign.SaveWeightsAsync(new PriorityWeightsBody { Fatigue = 1, Note = "Propia" }, CancellationToken.None));
        await JobTests.AssertBusinessAsync(
            404, "HYPOTHESIS_NOT_FOUND",
            () => otherScope.ServiceProvider.GetRequiredService<HypothesisService>().AssessAsync(assessed, assessment, CancellationToken.None));
    }

    // Un grupo de anuncios con 20.000 impresiones contra un resto de 10.000.
    private static ComponentGroup Pattern(EvidenceLevel evidence, SignalStrength signal, int ads = 2, long clicks = 180, long restClicks = 60)
    {
        return new ComponentGroup(
            "k", "Componente", null, ads, 1, new AdMetrics(100m, 20_000, clicks, null, null, null, null, 5),
            new AdMetrics(100m, 10_000, restClicks, null, null, null, null, 5), evidence, signal, clicks * 10_000 > restClicks * 20_000, "lectura",
            Enumerable.Range(1, ads).Select(i => (long)i).ToList());
    }
}

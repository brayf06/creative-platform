using Microsoft.Extensions.DependencyInjection;

namespace CreativeIntelligence.Tests;

/// <summary>Plan de producción: qué se propone según la evidencia, cómo se explica y qué recibe el planificador de
/// conceptos. El ciclo con datos reales (incluido que un aprendizaje invalidado deja de influir) está en
/// <see cref="LearningTests"/>.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class ProductionPlanTests(DatabaseFixture fixture)
{
    [Fact]
    public void Producir_en_cantidad_solo_se_propone_con_un_aprendizaje_de_confianza_media_o_alta()
    {
        Assert.Equal(PlanAction.Apply, ProductionPlanRules.Action(LearningConfidence.High, false));
        Assert.Equal(PlanAction.Apply, ProductionPlanRules.Action(LearningConfidence.Medium, true));
        Assert.Equal(PlanAction.Test, ProductionPlanRules.Action(LearningConfidence.Low, true));
        // Sin aprendizaje: con un patrón detrás se prueba; sin nada, se explora.
        Assert.Equal(PlanAction.Test, ProductionPlanRules.Action(null, true));
        Assert.Equal(PlanAction.Explore, ProductionPlanRules.Action(null, false));

        Assert.Equal(
            ["Apertura (hook)", "Cierre", "Desarrollo", "Terminación", "Terminación", "Terminación", "Enfoque"],
            Enum.GetValues<ExperimentVariable>().Select(ProductionPlanRules.Role));
    }

    [Fact]
    public void La_explicacion_dice_que_se_propone_con_que_evidencia_y_que_factores_pesaron()
    {
        // Una conjetura: sólo suma la novedad.
        Opportunity conjecture = Scored(new OpportunityInput(), ExperimentVariable.Other, null, false);
        Assert.Equal(
            "Enfoque. Se propone explorarlo en chico: es una conjetura, todavía sin datos que la respalden. "
            + "Puntaje 6.4 de 100 con las ponderaciones versión 0. "
            + "Lo que más sumó: novedad — 0 anuncio(s) publicado(s) con esto y 0 experimento(s); cuanto menos probado, más suma. "
            + "Sin dato, y por eso sin sumar: rendimiento histórico, similitud con lo existente, relevancia para el público, "
            + "disponibilidad de material, costo de producción, riesgo publicitario, fatiga, margen potencial.",
            ProductionPlanRules.Justify(conjecture, null, PriorityRules.DefaultWeights));

        // Con un aprendizaje de confianza alta se propone aplicarla, y la evidencia es lo que más pesa.
        OpportunityInput proven = new()
        {
            BestConfidence = LearningConfidence.High,
            MaterialAvailable = true,
            Assessment = new HypothesisAssessment(AssessmentLevel.High, AssessmentLevel.Low, AssessmentLevel.Low, AssessmentLevel.High),
        };
        string applied = ProductionPlanRules.Justify(
            Scored(proven, ExperimentVariable.Opening, "uno.mp4 0:00,0–0:02,0", false),
            new SupportingLearning(7, "Abrir con la pregunta subió el CTR", "Usarla de apertura", LearningConfidence.High),
            PriorityRules.DefaultWeights);
        Assert.StartsWith(
            "Apertura (hook): «uno.mp4 0:00,0–0:02,0». Se propone producir aplicándolo: lo respalda un aprendizaje vigente de confianza alta "
            + "(«Abrir con la pregunta subió el CTR»). Vale para lo que ese aprendizaje dice que vale, no como regla general. Puntaje ",
            applied);
        Assert.Contains("Lo que más sumó: calidad de la evidencia — Tiene un aprendizaje vigente de confianza alta. disponibilidad de material — ", applied);
        Assert.EndsWith("Sin dato, y por eso sin sumar: rendimiento histórico, similitud con lo existente, fatiga.", applied);

        // Con un aprendizaje de confianza baja, o con sólo un patrón, lo que se propone es probar.
        string weak = ProductionPlanRules.Justify(
            Scored(new OpportunityInput { BestConfidence = LearningConfidence.Low }, ExperimentVariable.Music, "Con música", true),
            new SupportingLearning(8, "Con música hubo más clics", "Repetir", LearningConfidence.Low), PriorityRules.DefaultWeights);
        Assert.StartsWith("Terminación: «Con música». Se propone probarlo con un experimento antes de producir en cantidad: el aprendizaje vigente es de confianza baja", weak);
        string correlational = ProductionPlanRules.Justify(Scored(new OpportunityInput(), ExperimentVariable.Music, "Con música", true), null, PriorityRules.DefaultWeights);
        Assert.Contains("sólo hay un patrón, que es una correlación y no una causa", correlational);

        Assert.All(
            new[] { applied, weak, correlational },
            text =>
            {
                Assert.DoesNotContain("ganador", text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("ROAS", text, StringComparison.OrdinalIgnoreCase);
            });
    }

    [Fact]
    public void El_planificador_de_conceptos_recibe_los_aprendizajes_vigentes_con_su_confianza()
    {
        BrandDetail brand = new(1, "Marca", null, null, null, null, null, null, []);
        BriefDetail brief = new() { Name = "Brief" };

        string with = ConceptPlanPrompt.BuildInput(new PlanRequest(brand, brief, [], 3)
        {
            Learnings =
            [
                new PlanLearning("Abrir con una pregunta subió el CTR.", "Usarla de apertura.", "Campañas de regalo.", LearningConfidence.High),
                new PlanLearning("Con música hubo más clics.", "Repetir la prueba.", "Sin confirmar.", LearningConfidence.Low),
            ],
        });

        Assert.Contains("# Aprendizajes vigentes (de resultados reales de la marca)", with);
        Assert.Contains("- [confianza alta] Abrir con una pregunta subió el CTR. Recomendación: Usarla de apertura. Vale para: Campañas de regalo.", with);
        Assert.Contains("- [confianza baja] Con música hubo más clics. Recomendación: Repetir la prueba. Vale para: Sin confirmar.", with);
        Assert.DoesNotContain("# Aprendizajes vigentes", ConceptPlanPrompt.BuildInput(new PlanRequest(brand, brief, [], 3)));
        Assert.Contains("Los de confianza baja son indicios", ConceptPlanPrompt.SystemInstruction);
        Assert.Equal("concept-plan-v3", ConceptPlanPrompt.Version);
    }

    [Fact]
    public async Task Un_plan_toma_las_hipotesis_con_puntaje_y_queda_como_una_foto()
    {
        int org = await fixture.NewOrganizationAsync();
        await using AsyncServiceScope scope = fixture.Scope(org);
        HypothesisService hypotheses = scope.ServiceProvider.GetRequiredService<HypothesisService>();
        PriorityService priorities = scope.ServiceProvider.GetRequiredService<PriorityService>();
        ProductionPlanService plans = scope.ServiceProvider.GetRequiredService<ProductionPlanService>();

        // Sin hipótesis no hay plan.
        CreateProductionPlanBody body = new() { Name = "  Próxima tanda  ", Count = 5 };
        await JobTests.AssertBusinessAsync(409, "PLAN_NO_CANDIDATES", () => plans.CreateAsync(body, CancellationToken.None));

        long plain = await hypotheses.CreateAsync(new CreateHypothesisBody { Statement = "Sin valorar", Variable = ExperimentVariable.Other }, CancellationToken.None);
        long assessed = await hypotheses.CreateAsync(new CreateHypothesisBody { Statement = "Valorada", Variable = ExperimentVariable.Opening }, CancellationToken.None);
        await hypotheses.AssessAsync(
            assessed, new AssessHypothesisBody { AudienceRelevance = AssessmentLevel.High, AdRisk = AssessmentLevel.Low }, CancellationToken.None);

        long id = await plans.CreateAsync(body, CancellationToken.None);

        ProductionPlanDetail plan = await plans.GetAsync(id, CancellationToken.None);
        Assert.Equal(("Próxima tanda", 5, 0, 0), (plan.Name, plan.RequestedItems, plan.WeightsVersion, plan.CurrentWeightsVersion));
        // Novedad 3 + público 5 + riesgo 5 = 13 de 47; la otra, sólo novedad.
        Assert.Equal(
            [(1, assessed, 27.7m, PlanAction.Explore, ExperimentVariable.Opening), (2, plain, 6.4m, PlanAction.Explore, ExperimentVariable.Other)],
            plan.Items.Select(i => (i.Position, i.HypothesisId, i.Score, i.Action, i.Variable)));
        Assert.All(plan.Items, i => Assert.Equal((10, null, null, null), (i.Factors.Count, i.LearningId, i.LearningStatus, i.AssetId)));
        Assert.Equal(1m, plan.Items[0].Factors.Single(f => f.Factor == PriorityFactor.AudienceRelevance).Value);
        Assert.StartsWith("Apertura (hook). Se propone explorarlo en chico", plan.Items[0].Justification);

        // El plan no cambia aunque cambien las ponderaciones o las valoraciones; uno nuevo sí.
        await priorities.SaveWeightsAsync(new PriorityWeightsBody { Novelty = 10, Note = "Sólo novedad" }, CancellationToken.None);
        await hypotheses.AssessAsync(assessed, new AssessHypothesisBody(), CancellationToken.None);
        ProductionPlanDetail same = await plans.GetAsync(id, CancellationToken.None);
        Assert.Equal((0, 1), (same.WeightsVersion, same.CurrentWeightsVersion));
        Assert.Equal([27.7m, 6.4m], same.Items.Select(i => i.Score));
        Assert.Equal(plan.Items[0].Justification, same.Items[0].Justification);

        body.Count = 1;
        long next = await plans.CreateAsync(body, CancellationToken.None);
        ProductionPlanDetail newer = await plans.GetAsync(next, CancellationToken.None);
        // Empatan en 100 y entra una sola: la más vieja.
        Assert.Equal(1, newer.WeightsVersion);
        Assert.Equal([(plain, 100m)], newer.Items.Select(i => (i.HypothesisId, i.Score)));
        Assert.Contains("con las ponderaciones versión 1", newer.Items[0].Justification);
        Assert.Equal([(next, 1, 1), (id, 2, 0)], (await plans.ListAsync(CancellationToken.None)).Select(p => (p.Id, p.Items, p.WeightsVersion)));

        // Una hipótesis que no suma nada no entra: con todo el peso en el riesgo, ninguna tiene puntaje.
        await priorities.SaveWeightsAsync(new PriorityWeightsBody { AdRisk = 10, Note = "Sólo riesgo" }, CancellationToken.None);
        await JobTests.AssertBusinessAsync(409, "PLAN_NO_CANDIDATES", () => plans.CreateAsync(body, CancellationToken.None));

        await using AsyncServiceScope otherScope = fixture.Scope(await fixture.NewOrganizationAsync());
        ProductionPlanService foreign = otherScope.ServiceProvider.GetRequiredService<ProductionPlanService>();
        Assert.Empty(await foreign.ListAsync(CancellationToken.None));
        await JobTests.AssertBusinessAsync(404, "PRODUCTION_PLAN_NOT_FOUND", () => foreign.GetAsync(id, CancellationToken.None));
    }

    private static Opportunity Scored(OpportunityInput input, ExperimentVariable variable, string? component, bool hasPattern)
    {
        List<FactorValue> factors = PriorityRules.Factors(input);
        return new Opportunity(
            1, "Hipótesis", HypothesisOrigin.Manual, HypothesisState.Open, PriorityRules.Score(PriorityRules.DefaultWeights, factors),
            factors.Count(f => f.Value is not null), factors, variable, component, null, hasPattern);
    }
}

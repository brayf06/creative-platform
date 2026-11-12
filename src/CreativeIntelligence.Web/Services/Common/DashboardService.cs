using Microsoft.EntityFrameworkCore;

namespace CreativeIntelligence.Web.Services.Common;

/// <summary>Reglas de la pantalla de inicio que no dependen de la base.</summary>
public static class DashboardRules
{
    /// <summary>
    /// Decide el paso que conviene dar ahora. Primero lo que espera a una persona (anomalías y aprobaciones);
    /// después, el primer eslabón del recorrido que todavía no tiene nada. Función pura.
    /// </summary>
    /// <param name="overview">Cuánto hay hecho en cada etapa.</param>
    /// <returns>El paso siguiente.</returns>
    public static NextStep Next(DashboardOverview overview)
    {
        if (overview.OpenAnomalies > 0)
        {
            return new NextStep($"Hay {overview.OpenAnomalies} anomalía(s) en las métricas sin revisar.", "Automation", "Index", true);
        }

        if (overview.PendingApprovals > 0)
        {
            return new NextStep($"Hay {overview.PendingApprovals} acción(es) esperando aprobación.", "Automation", "Index", true);
        }

        if (overview.MediaFiles == 0)
        {
            return new NextStep("Cargá los primeros videos en la biblioteca.", "Media", "Index", false);
        }

        if (overview.ProductionSegments == 0)
        {
            return new NextStep("Abrí un video de la biblioteca y elegí fragmentos para producción.", "Media", "Index", false);
        }

        if (overview.ReadyVideos == 0)
        {
            return new NextStep("Armá una receta con los fragmentos elegidos y generá un video.", "Recipes", "Index", false);
        }

        if (overview.Ads == 0)
        {
            return new NextStep("Importá un informe de Ads Manager para ver cómo rinden los anuncios.", "Meta", "Index", false);
        }

        if (overview.LinkedAds == 0)
        {
            return new NextStep("Vinculá cada anuncio con el video que publicó: sin eso no se puede analizar.", "Meta", "Index", false);
        }

        if (overview.Hypotheses == 0)
        {
            return new NextStep("Mirá el análisis por componente y guardá una hipótesis.", "Meta", "Analysis", false);
        }

        if (overview.ActiveLearnings == 0)
        {
            return new NextStep("Probá una hipótesis con un experimento y registrá lo aprendido.", "Hypotheses", "Index", false);
        }

        return new NextStep("Armá un plan de producción con las prioridades de hoy.", "ProductionPlans", "Index", false);
    }
}

/// <summary>Resumen de la organización activa para la pantalla de inicio.</summary>
/// <param name="db">Contexto de datos.</param>
public sealed class DashboardService(AppDbContext db)
{
    /// <summary>Cuenta lo que hay en cada etapa del trabajo. No escribe.</summary>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Los conteos.</returns>
    public async Task<DashboardOverview> OverviewAsync(CancellationToken cancellationToken)
    {
        return new DashboardOverview(
            await db.MediaAssets.CountAsync(cancellationToken),
            await db.MediaSegments.CountAsync(s => s.SelectedForProduction, cancellationToken),
            await db.CreativeRecipes.CountAsync(cancellationToken),
            await db.CreativeVariants.CountAsync(v => v.Status == MediaProcessingStatus.Ready, cancellationToken),
            await db.Brands.CountAsync(cancellationToken),
            await db.CampaignBriefs.CountAsync(cancellationToken),
            await db.MetaAds.CountAsync(cancellationToken),
            await db.MetaAds.CountAsync(a => a.CreativeVariantId != null, cancellationToken),
            await db.Hypotheses.CountAsync(h => h.DiscardedAt == null, cancellationToken),
            await db.Learnings.CountAsync(l => l.Status == LearningStatus.Active, cancellationToken),
            await db.AutomationActions.CountAsync(a => a.Status == AutomationActionStatus.PendingApproval, cancellationToken),
            await db.AnomalyEvents.CountAsync(e => e.ReviewedAt == null, cancellationToken));
    }
}

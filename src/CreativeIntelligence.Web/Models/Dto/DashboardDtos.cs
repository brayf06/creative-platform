namespace CreativeIntelligence.Web.Models.Dto;

/// <summary>Cuánto hay hecho en cada etapa del trabajo, para orientar en la pantalla de inicio.</summary>
/// <param name="MediaFiles">Archivos en la biblioteca.</param>
/// <param name="ProductionSegments">Fragmentos elegidos para producción.</param>
/// <param name="Recipes">Recetas.</param>
/// <param name="ReadyVideos">Videos generados listos.</param>
/// <param name="Brands">Marcas.</param>
/// <param name="Briefs">Briefs.</param>
/// <param name="Ads">Anuncios con métricas importadas.</param>
/// <param name="LinkedAds">De esos, los vinculados a un video generado.</param>
/// <param name="Hypotheses">Hipótesis en pie.</param>
/// <param name="ActiveLearnings">Aprendizajes vigentes.</param>
/// <param name="PendingApprovals">Acciones de automatización que esperan aprobación.</param>
/// <param name="OpenAnomalies">Anomalías sin revisar.</param>
public sealed record DashboardOverview(
    int MediaFiles, int ProductionSegments, int Recipes, int ReadyVideos, int Brands, int Briefs, int Ads, int LinkedAds, int Hypotheses,
    int ActiveLearnings, int PendingApprovals, int OpenAnomalies);

/// <summary>El paso que conviene dar ahora.</summary>
/// <param name="Text">Qué hacer.</param>
/// <param name="Controller">Controller de la pantalla donde se hace.</param>
/// <param name="Action">Acción de esa pantalla.</param>
/// <param name="Urgent">Si es algo que espera a una persona (aprobaciones, anomalías) y no un paso del recorrido.</param>
public sealed record NextStep(string Text, string Controller, string Action, bool Urgent);

namespace CreativeIntelligence.Web.Services.Ai;

/// <summary>Configuración general del uso de IA (sección <c>Ai</c>).</summary>
public sealed class AiOptions
{
    /// <summary>Costo estimado máximo, en dólares, de analizar un video. Un pedido que lo supera se rechaza.</summary>
    public decimal MaxCostPerVideoUsd { get; set; } = 0.05m;

    /// <summary>Costo estimado máximo, en dólares, de generar un plan de conceptos. Un pedido que lo supera se
    /// rechaza. El presupuesto de generación del brief, si es menor, manda.</summary>
    public decimal MaxCostPerPlanUsd { get; set; } = 0.50m;

    /// <summary>Costo estimado máximo, en dólares, de una corrida de investigación externa.</summary>
    public decimal MaxCostPerResearchUsd { get; set; } = 0.30m;

    /// <summary>Cantidad máxima de referencias que se piden en una corrida de investigación.</summary>
    public int MaxReferencesPerResearch { get; set; } = 15;

    /// <summary>Cantidad máxima de fragmentos que se le describen al planificador.</summary>
    public int MaxFragmentsPerPlan { get; set; } = 120;

    /// <summary>Presupuesto mensual de IA por organización, en dólares. La pantalla de costos avisa al 50 %, 80 %
    /// y 100 %. 0 = sin presupuesto. Es un aviso: no bloquea nuevos análisis.</summary>
    public decimal MonthlyBudgetUsd { get; set; } = 20m;

    /// <summary>Costo estimado máximo, en dólares, de una tanda de análisis en lote (manual o automática). Los
    /// videos que no entran quedan pendientes para la siguiente.</summary>
    public decimal MaxCostPerBatchUsd { get; set; } = 5m;

    /// <summary>Cada cuántos minutos se revisa si hay videos nuevos para analizar en las organizaciones que
    /// activaron el análisis automático.</summary>
    public int AutoAnalyzeIntervalMinutes { get; set; } = 5;

    /// <summary>Cantidad máxima de cuadros que se envían al modelo por video.</summary>
    public int MaxFramesPerVideo { get; set; } = 48;

    /// <summary>Lado más largo, en píxeles, de los cuadros que se envían. 384 es el tamaño que el proveedor cobra
    /// como una sola unidad de imagen.</summary>
    public int FrameMaxSide { get; set; } = 384;
}

/// <summary>Falla de un proveedor de IA. El mensaje es apto para mostrar al usuario; nunca incluye la clave.</summary>
/// <param name="message">Qué pasó.</param>
/// <param name="retryable">Verdadero si reintentar puede servir (límite de cuota, error transitorio, red).</param>
public sealed class AiProviderException(string message, bool retryable) : Exception(message)
{
    /// <summary>Si reintentar puede servir.</summary>
    public bool Retryable { get; } = retryable;
}

/// <summary>
/// Análisis multimodal de escenas por un modelo de IA. Es un límite externo: hoy lo implementa Gemini. Sólo se
/// usa desde trabajos en segundo plano.
/// </summary>
public interface ISegmentAnalyzer
{
    /// <summary>Falso si falta la clave del proveedor: entonces no se puede analizar.</summary>
    bool IsConfigured { get; }

    /// <summary>Modelo que se usaría, para mostrarlo y para la clave de cache.</summary>
    string Model { get; }

    /// <summary>Estima el consumo de un análisis sin llamar al proveedor.</summary>
    /// <param name="scenes">Cantidad de escenas.</param>
    /// <param name="frames">Cantidad total de cuadros a enviar.</param>
    /// <param name="transcriptCharacters">Largo total del texto transcripto a enviar.</param>
    /// <returns>Tokens y costo estimados con las tarifas configuradas.</returns>
    AnalysisCost Estimate(int scenes, int frames, int transcriptCharacters);

    /// <summary>Analiza las escenas de un video en una sola llamada al modelo.</summary>
    /// <param name="request">Escenas con sus cuadros y su texto.</param>
    /// <param name="cancellationToken">Cancelación.</param>
    /// <returns>Análisis por escena y consumo real.</returns>
    /// <exception cref="AiProviderException">Si el proveedor rechaza el pedido, falla o devuelve algo ilegible.</exception>
    Task<AnalysisOutcome> AnalyzeAsync(VideoAnalysisRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Prompt del análisis de escenas. Cambiar cualquier texto exige cambiar <see cref="Version"/>: la versión se
/// guarda con cada análisis y forma parte de la clave de cache.
/// </summary>
public static class SceneAnalysisPrompt
{
    /// <summary>Versión del prompt.</summary>
    public const string Version = "scene-analysis-v2";

    /// <summary>Instrucción de sistema.</summary>
    public const string SystemInstruction =
        "Sos un analista de creatividades publicitarias en video. Recibís las escenas de un anuncio en orden: "
        + "para cada una, uno o más cuadros y lo que se dice en ella. Describí únicamente lo que se ve y se oye. "
        + "No inventes productos, marcas, precios ni afirmaciones. Si un dato no se puede determinar, dejá el campo "
        + "vacío. No evalúes si el anuncio va a rendir bien. Respondé en español.";

    /// <summary>Texto inicial del pedido: qué devolver por escena y la taxonomía de funciones.</summary>
    /// <param name="fileName">Nombre del archivo.</param>
    /// <param name="sceneCount">Cantidad de escenas.</param>
    /// <returns>El texto de la consigna.</returns>
    public static string Instructions(string fileName, int sceneCount)
    {
        return $"Video: {fileName}. Tiene {sceneCount} escenas. Devolvé un elemento por escena, con el mismo número "
            + "en 'index'.\n"
            + "- visual_description: qué se ve, en una o dos oraciones.\n"
            + "- on_screen_text: texto sobreimpreso tal como aparece; vacío si no hay.\n"
            + "- product: producto que aparece; vacío si no se identifica.\n"
            + "- actions: acciones que se observan.\n"
            + "- commercial_angle: enfoque comercial (practicidad, ahorro, emoción, resultado, urgencia…).\n"
            + "- functions: una o más de estas funciones dentro del anuncio: hook (gancho inicial), problem "
            + "(plantea un problema), presentation (presenta el producto), demonstration (lo muestra en uso), "
            + "benefit (beneficio o resultado), social_proof (testimonio o reseña), objection (responde una "
            + "objeción), comparison (compara o muestra antes y después), offer (precio o promoción), cta (llamado "
            + "a la acción), broll (imagen de apoyo), closing (cierre, logo o placa final).\n"
            + "- main_message: el mensaje principal de la escena.\n"
            + "- audience: a quién parece dirigirse.\n"
            + "- visual_quality: de 1 (mala) a 5 (excelente): nitidez, iluminación y encuadre.\n"
            + "- tags: de 3 a 8 etiquetas cortas en minúscula para encontrar la escena.\n"
            + "- confidence: de 0 a 1, qué tan seguro estás de la clasificación.\n"
            + "Además devolvé 'narrative_segments': los tramos narrativos del anuncio. Un tramo es una o más escenas "
            + "consecutivas que cumplen juntas una misma función (por ejemplo, un hook hecho con tres planos rápidos). "
            + "Cada tramo lleva from_scene y to_scene (números de escena, inclusive), function (una sola, de la lista "
            + "de arriba) y summary (qué pasa en el tramo, en una oración). Los tramos van en orden, no se superponen "
            + "y entre todos cubren el anuncio completo.";
    }

    /// <summary>Texto que introduce una escena antes de sus cuadros.</summary>
    /// <param name="scene">Escena.</param>
    /// <returns>Encabezado con número, tiempos y lo que se dice.</returns>
    public static string SceneHeader(SceneInput scene)
    {
        string said = scene.Transcript.Length > 0 ? $"Se dice: \"{scene.Transcript}\"" : "No se dice nada.";
        return $"Escena {scene.Index} (de {scene.StartMs / 1000.0:0.0} a {scene.EndMs / 1000.0:0.0} s). {said}";
    }
}

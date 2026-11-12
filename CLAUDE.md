# Creative Intelligence Platform

Plataforma propia de inteligencia creativa publicitaria: convierte una biblioteca de videos en un catálogo de escenas
reutilizables, compone nuevas creatividades, investiga y planifica desde un brief, mide el rendimiento real en Meta Ads
y aprende de él para priorizar la siguiente producción.

Recharm, raw2ads y Creatify son **referencias funcionales**. No son dependencias y no se copia su código, diseño ni
propiedad intelectual.

## Estado actual

- **Fase 0 — Fundamentos: cerrada el 2026-11-11.**
- **Fase 1 — Biblioteca inteligente: construida, puerta sin medir.** Todo lo que no depende de la clave está hecho
  y verificado: carga, duplicados exactos y visuales, derechos, propiedades técnicas, escenas, timeline con edición
  (incluido arrastrar cortes), transcripción local, calidad de audio, carpetas vigiladas, buscador por palabras y
  filtros (con interpretación de la consulta), costos de IA. El análisis con IA (por escena y por tramos
  narrativos, individual, en lote y automático) y la búsqueda por significado (Gemini) están **implementados pero
  sin probar contra el servicio real: falta la clave**. La puerta de la fase (precisión sobre videos reales
  anotados) sigue sin medirse.
- **Fase 2 — Motor de composición: construida, puerta sin medir.** Hecho y verificado con material de prueba:
  recetas (con recorte, duplicado y vista previa), render con FFmpeg, control de calidad, linaje, variantes con
  control de similitud, lotes de 5 a 50 (con cancelar y reintentar) y terminación (subtítulos, textos, música con
  derechos, fundidos). La puerta pide un lote de 10 o más con **material real y revisión humana**; los pasos están
  en el roadmap. Por decisión del usuario del 2026-11-11 se construye todo y la clave de IA va al final; no se
  produce en masa con la biblioteca real hasta medir las puertas. La suite tarda unos 4 minutos: corre FFmpeg,
  PySceneDetect y Whisper de verdad.
- **Fase 3 — Investigación y estrategia: en curso.** Hecho y verificado el paso 1: perfil de marca, brief de
  campaña con interpretación del texto libre y mensajes prohibidos. Paso 2, **implementado pero sin probar contra
  el servicio real**: motor estratégico (del brief a un plan de conceptos, y de cada concepto realizable a su
  receta). Paso 3: fuentes autorizadas y referencias (verificado) y agente investigador con búsqueda web (sin
  probar contra el servicio real). **No construida: la generación audiovisual opcional**, hasta que el usuario
  elija proveedor. La puerta de la fase (plan de 20 conceptos desde un brief estacional real) no está medida.
- **Fase 4 — Inteligencia de Meta: en curso.** Hecho y verificado el paso 1: importación de métricas desde un CSV
  de Ads Manager, métricas por anuncio y por día, vínculo anuncio ↔ video y linaje completo. Paso 2, hecho y
  verificado: análisis por componente con niveles de evidencia, señal estadística, lecturas y fatiga. Paso 3,
  hecho y verificado: experimentos controlados con revisión del diseño. **No construida: la API de Meta**, hasta
  tener app y token. La puerta de la fase (conciliar con Ads Manager, datos reales) no está medida.
- **Fase 5 — Aprendizaje: construida, puerta sin medir.** Hecho y verificado con material de prueba: hipótesis
  (desde un patrón o a mano), memoria de aprendizaje (registrar, revisar contra los datos de hoy, corregir,
  invalidar), priorización con diez factores y ponderaciones versionadas, y plan de producción que explica cada
  elección. El motor estratégico recibe los aprendizajes vigentes (sin probar contra el servicio real). La puerta
  pide un plan a partir de resultados de campañas reales.
- **Fase 6 — Automatización publicitaria: construida en simulación.** Hecho y verificado: política (modo y
  límites), guardas, aprobaciones, kill switch, ejecución simulada, detección de anomalías, acciones propuestas
  desde el plan de producción y presupuesto por lote. El ciclo completo corre en simulación (hay un test). **No
  construido: el cliente de la Marketing API** (faltan la app y el token), así que no hay ninguna escritura real
  posible y esa parte de la puerta no se puede medir.
- **Todas las fases están construidas hasta donde se puede sin credenciales.** Lo pendiente depende del usuario:
  clave de Gemini, app y token de Meta, proveedor de generación de video, y material y datos reales para medir las
  puertas.
- Actualizá esta sección y [docs/02-roadmap.md](docs/02-roadmap.md) al cerrar cada fase (fecha + evidencia).
- La única especificación de origen es [docs/00-directiva-producto.md](docs/00-directiva-producto.md). No existe un
  "prompt maestro" anterior, aunque la directiva lo mencione.

## Documentación (leer antes de trabajar en un área)

| Documento | Para qué |
|---|---|
| [docs/00-directiva-producto.md](docs/00-directiva-producto.md) | Texto original de la directiva; manda ante cualquier diferencia |
| [docs/01-matriz-requisitos.md](docs/01-matriz-requisitos.md) | Qué hay que construir, con ID y forma de verificación |
| [docs/02-roadmap.md](docs/02-roadmap.md) | Fases, entregables y puertas de salida |
| [docs/03-arquitectura-funcional.md](docs/03-arquitectura-funcional.md) | Capas, módulos, dependencias, cola de trabajos, IA |
| [docs/04-modelo-datos.md](docs/04-modelo-datos.md) | Entidades por fase y Creative Lineage |
| [docs/05-plan-pruebas.md](docs/05-plan-pruebas.md) | Casos de prueba por fase |
| [docs/06-riesgos-tecnicos.md](docs/06-riesgos-tecnicos.md) | Riesgos y supuestos a confirmar |
| [docs/07-control-costos.md](docs/07-control-costos.md) | Reglas de costo |

## Reglas de producto

1. **Orden de prioridad:** biblioteca inteligente → motor de composición → investigación y estrategia → inteligencia de
   Meta → aprendizaje → automatización.
2. **No avanzar de fase sin verificar la actual.** No se construyen módulos en paralelo. Cada paso entrega un flujo
   vertical ejecutable y probado.
3. **Sin pantallas ficticias.** Una vista o un enlace del menú existe sólo si está conectado a un servicio funcional.
4. **No eliminar funcionalidad existente** ni reemplazar una implementación que funciona sin justificarlo.
5. **Derechos primero.** Un fragmento sin autorización de reutilización no entra en una receta. El material externo de
   referencia no se descarga, reproduce ni publica.
6. **Rendimiento con honestidad.** Separar observación, correlación e inferencia causal. No declarar "ganador" por un
   resultado aislado. Un puntaje de IA no es una predicción de ROAS.
7. **Automatización apagada por defecto.** Ninguna escritura real en Meta sin habilitación explícita, límites,
   aprobaciones y kill switch. Existe modo simulación.
8. **Costo por creatividad utilizable** como indicador. Analizar una sola vez, cache por hash + versión, estimar antes
   de ejecutar, presupuesto por lote.

## Arquitectura obligatoria

- ASP.NET Core MVC + Razor Views + Bootstrap 5 + JavaScript/jQuery/AJAX + SignalR. **Sin React, sin SPA, sin
  microservicios innecesarios.**
- **Un solo proyecto MVC** (`src/CreativeIntelligence.Web`), por decisión del usuario del 2026-11-11, que reemplaza
  a los cinco proyectos por capas que pedía la directiva. Las responsabilidades siguen separadas, pero por
  carpetas: `Controllers` (finos) → `Services` (por área) → `Data` (EF Core) y `Models` (entidades, DTOs, view
  models). **No volver a partir en proyectos sin que el usuario lo pida.**
- **Los trabajos en segundo plano corren dentro del proceso de la Web** (servicios alojados), también por decisión
  del usuario: no hay Worker aparte.
- SQL Server + Entity Framework Core + migraciones.
- Python (PySceneDetect, Whisper) y FFmpeg como herramientas de línea de comandos, con salida JSON; las lanza la cola
  de trabajos como procesos externos.
- Proveedores de IA configurables por tarea, detrás de contratos propios.
- Todo trabajo intensivo va a la cola; nunca dentro de una solicitud HTTP. Los trabajos son idempotentes, reanudables,
  cancelables y auditados.
- Multiempresa: toda entidad de negocio lleva `OrganizationId` y pasa por el filtro global.
- Trazabilidad: toda creatividad generada conserva la relación con su receta, concepto, brief, fragmentos y assets.
- Prompts y modelos versionados; la versión se guarda en cada resultado de IA.

## Cómo está armado el código

Todo el código de la aplicación está en `src/CreativeIntelligence.Web`:

| Carpeta | Contenido | Espacio de nombres |
|---|---|---|
| `Controllers/` | Controllers finos | `…Web.Controllers` |
| `Models/Entidades/` | Entidades y enumeraciones (`Entities.cs`) | `…Web.Models.Entidades` |
| `Models/Dto/` | DTOs, bodies, filtros y proyecciones, un archivo por área (`MediaDtos.cs`, `JobDtos.cs`…) | `…Web.Models.Dto` |
| `Models/ViewModels/` | View models y formato para las vistas | `…Web.Models.ViewModels` |
| `Data/` | `AppDbContext`, configuraciones EF, `DatabaseInitializer`, registro de SQL Server y `Migrations/` | `…Web.Data` |
| `Services/<Área>/` | Servicios, manejadores de trabajos, contratos e implementaciones (`Jobs`, `Media`, `Composition`, `Strategy`, `Meta`, `Learnings`, `Automation`, `Ai`, `Auditing`, `Organizations`, `Common`) | `…Web.Services.<Área>` |
| `Security/`, `Filters/`, `Hubs/`, `ViewComponents/` | Claims y políticas, filtros globales, SignalR, componentes de vista | `…Web.<Carpeta>` |
| `Views/`, `wwwroot/` | Vistas Razor, JavaScript, CSS | — |

`tests/CreativeIntelligence.Tests` tiene los tests de integración contra LocalDB y los de arquitectura.

`GlobalUsings.cs` importa todos los espacios de nombres propios del proyecto: **no se escriben `using
CreativeIntelligence…` en cada archivo** (ni `@using` en las vistas). Una carpeta nueva con su propio espacio de
nombres se agrega ahí y en el `GlobalUsings.cs` de los tests. Un DTO nuevo va en `Models/Dto/<Área>Dtos.cs`, no
junto al servicio. El respaldo de la estructura anterior está en `respaldo-antes-de-mvc/` (fuera de git).

Decisiones que conviene conocer antes de tocar:

- **Sin repositorios**: los servicios consultan `AppDbContext` con EF Core directo; EF es la abstracción de
  persistencia.
- **Multiempresa**: `CurrentContext` (por ámbito) dice la organización activa; una solicitud web lo carga de los claims y
  la cola de trabajos, del trabajo que tomó. Entidad nueva de negocio = implementar `IOrganizationScoped` **y** agregar su
  `HasQueryFilter` en `AppDbContext` (un test falla si falta). Cruzar organizaciones exige `IgnoreQueryFilters()` a la
  vista.
- **"Ahora" es la hora de la base**: `AppDbContext.UtcNow()` dentro de la consulta (`SYSUTCDATETIME()`).
- **Trabajos**: los usuarios operan por `JobService` (filtrado por organización); la cola, por `JobQueue` (todas las
  organizaciones). Cuatro servicios alojados corren dentro de la Web, registrados en `Program.cs`: `JobProcessor`
  (ejecuta la cola), `FolderScanScheduler` (carpetas vigiladas) y `JobProgressNotifier` (avisa el progreso por
  SignalR consultando la base cada segundo). Un tipo de trabajo nuevo = una clase `IJobHandler` registrada en
  `AddApplication`. Como todo corre en un proceso, **reiniciar la Web corta los trabajos en curso**: quedan
  `Running` hasta que vence su arriendo (60 s) y se retoman. La cola sigue soportando varios procesos (arriendo y
  guarda de concurrencia), por si alguna vez se vuelve a separar.
- **Biblioteca**: `MediaLibraryService` (Application) sobre `IMediaStorage` (implementación local en Infrastructure).
  Los originales van a `storage/org-{id}/originals/{2 del hash}/{sha256}`, sin extensión, y **no existe operación para
  modificarlos ni borrarlos**. Cargar el mismo contenido dos veces devuelve el archivo existente como duplicado. El
  tipo de archivo se deduce de la extensión (`MediaRules`), no del tipo MIME que envía el navegador. La carga masiva
  es una llamada por archivo desde `media.js`.
- **Análisis técnico**: al ingresar un archivo se encola `media.probe` (`MediaProbeJobHandler`) en la misma
  transacción; el manejador lee propiedades y genera la miniatura con `IMediaTools` (`FfmpegMediaTools`, procesos
  externos de `ffprobe`/`ffmpeg`). FFmpeg **sólo corre desde un trabajo**, nunca dentro de una solicitud web. Un archivo
  ilegible queda `Failed` con el motivo; "Reanalizar" lo vuelve a encolar.
- **Escenas**: cuando el análisis técnico de un video termina bien por primera vez encola `media.scenes`
  (`MediaScenesJobHandler`). Corre `tools/media/detect_scenes.py` con el Python de `tools/media/.venv`, normaliza
  los cortes a tramos contiguos que cubren todo el video (`Normalize`, función pura con tests) y genera una miniatura
  por tramo. Volver a detectar reemplaza sólo los segmentos de origen `Detected`. `SceneService` atiende la pantalla;
  `scenes.js` dibuja la franja y reproduce por fragmento. Umbral y duración mínima en `MediaTools`.
- **Transcripción**: si el video tiene audio, el análisis técnico encola `media.transcribe`
  (`MediaTranscribeJobHandler`, en `TranscriptService.cs`). Corre `tools/media/transcribe.py`: decodifica el audio
  con FFmpeg y lo pasa a faster-whisper en memoria (el decodificador propio de faster-whisper no es compatible con
  la versión actual de PyAV), y corta en frases cortas usando los tiempos por palabra. Las frases se guardan por
  video (`MediaTranscriptLine`); el texto de cada escena **se deriva al consultar** con
  `TranscriptService.TextBetween` (frases cuyo punto medio cae en el tramo), no se guarda por escena. Cada
  transcripción registra un `AiUsageRecord` con costo 0 y su duración. Modelo e idioma en `MediaTools`
  (`WhisperModel`, `WhisperLanguage`); los tests usan `tiny`.
- **Análisis con IA**: `AnalysisService` + `MediaAnalyzeJobHandler` (en `AnalysisService.cs`) sobre el contrato
  `ISegmentAnalyzer`; la implementación es `GeminiSegmentAnalyzer` (Infrastructure), que usa la **API Interactions**
  de Gemini (`POST /v1beta/interactions`, encabezados `x-goog-api-key` y `Api-Revision`, salida JSON por
  `response_format.schema`, `store: false`). **No es automático porque cuesta**: una persona pide la estimación y
  confirma. Una sola llamada por video con 1 a 3 cuadros de 384 px por escena (258 tokens cada uno) más lo que se
  dice. Antes de llamar se busca en `AiResultCache`; después se registra `AiUsageRecord` con tokens y costo. El
  prompt está en `SceneAnalysisPrompt` (Application): **cambiar su texto obliga a cambiar `Version`**. Los tests
  usan `FakeSegmentAnalyzer`; ningún test llama a Gemini. Modelos, tarifas y forma de la API se consultaron en la
  documentación oficial el 2026-11-11: **antes de tocar el cliente, volver a consultarla**, cambia seguido.
- **Búsqueda**: todo en `SearchService.cs`. `SegmentIndexService` arma el texto de cada fragmento (análisis + lo
  que se dice + etiquetas) y encola `media.embed` cada vez que algo de eso cambia (análisis, transcripción,
  etiquetas, cortes), sólo si hay proveedor configurado; el trabajo compara un hash del texto y vuelve a pedir el
  embedding únicamente de lo que cambió. `SearchService` filtra en la base y ordena en memoria por producto punto
  (los vectores se guardan normalizados). Sin proveedor cae a búsqueda por palabras. El vector de cada consulta se
  guarda en `AiResultCache`. Contrato `IEmbeddingProvider`; implementación `GeminiEmbeddingProvider`
  (`batchEmbedContents`; la tarea va como prefijo del texto: `title: none | text: …` para documentos y
  `task: search result | query: …` para consultas). **Cambiar de modelo o de dimensiones invalida el índice.**
- **Costos de IA**: todo uso de un modelo se registra con `AiUsageService.RecordAsync` (los modelos que corren en
  el servidor, con proveedor `AiUsageService.LocalProvider` y costo 0, para medir su tiempo de cómputo).
  `SummaryAsync` alimenta la pantalla `/AiCosts`. El presupuesto mensual (`Ai:MonthlyBudgetUsd`) sólo avisa; lo que
  bloquea un gasto es `Ai:MaxCostPerVideoUsd`. Una tarea nueva de IA = registrar su uso y sumar su nombre legible en
  la vista `AiCosts/Index.cshtml`.
- **Clave de Gemini**: `Ai:Gemini:ApiKey`, vacía en `appsettings.json`. Se define con la variable de entorno
  `Ai__Gemini__ApiKey`; no se escribe en el repositorio.
- **Latido de la cola**: `JobRunner.HeartbeatLoopAsync` filtra el fin normal por la cancelación y no por el tipo
  de excepción, porque SQL Server cancelado en medio de un comando lanza `SqlException`.
- **Edición de escenas**: `SegmentEditService`. Invariante: los fragmentos visuales de un video son contiguos y lo
  cubren entero. Toda operación que cambia límites lee `MediaAsset.SegmentsVersion` antes de leer los fragmentos y
  la incrementa con esa versión en la condición del `UPDATE` dentro de la transacción (`CommitAsync`): la segunda
  de dos ediciones simultáneas recibe 409 `SEGMENTS_CHANGED`. Un fragmento editado pasa a origen `Manual` y pierde
  la miniatura, que regenera el trabajo `media.segment-thumbnails`. Redetectar con fragmentos manuales exige
  confirmación explícita (`HAS_MANUAL_EDITS`). Elegir para producción exige autorización de reutilización vigente
  a la fecha de la base.
- **Carpetas vigiladas**: `WatchedFolderService` + `MediaFolderScanJobHandler` (en `WatchedFolderService.cs`). Una
  ruta la escribe una persona en el navegador, así que **sólo se acepta dentro de `Ingest:AllowedRoots`** (en
  desarrollo, `watch/` en la raíz, fuera de git); la comparación es por carpetas completas y después de resolver
  `..`. El trabajo `media.folder-scan` guarda en `WatchedFile` tamaño y fecha de cada archivo visto para no
  releerlo, e ingresa con `MediaLibraryService.IngestAsync` (procedencia `WatchedFolder`).
  `FolderScanScheduler` (servicio alojado) encola cada `Ingest:ScanIntervalMinutes` una revisión por carpeta
  activa, con el intervalo
  en la clave de idempotencia y con la organización de la carpeta activada.
- **Calidad de audio**: `IMediaTools.MeasureAudioAsync` (FFmpeg `volumedetect`) mide volumen medio y pico de un
  tramo; se guarda por escena en `MediaSegment.AudioMeanDb`/`AudioPeakDb` al detectar escenas y, tras una edición,
  en el trabajo `media.segment-thumbnails` (que regenera todo lo que depende del tramo: miniatura y audio). La
  clasificación (`MediaRules.ClassifyAudio`, función pura con tests y umbrales en `MediaRules`) se calcula al
  consultar, no se guarda. Sin pista de audio no hay dato: no es lo mismo que "sin sonido".
- **Duplicados visuales**: el análisis técnico calcula la huella (`IMediaTools.FrameHashAsync`: cuadro reducido a
  9×8 en gris, un bit por comparación de brillo entre vecinos) en las posiciones de `MediaRules.HashSamplePositions`
  y busca, entre los archivos anteriores de la organización que no son duplicados, uno del mismo tipo, de duración
  parecida y con distancia ≤ `MediaRules.DuplicateMaxDistance`. Sólo marca `DuplicateOfAssetId`; no bloquea la carga.
  Al pedir el análisis de un duplicado cuyo original ya está analizado, `MediaAnalyzeJobHandler` llama a
  `AnalysisService.InheritAsync` en lugar de al proveedor: copia el análisis escena por escena según
  `AnalysisService.MatchRanges` (función pura con tests) y lo marca con `InheritedFromAnalysisId`.
- **Tramos narrativos**: el análisis devuelve además `narrative_segments`; `MediaAnalyzeJobHandler` los guarda
  como `MediaSegment` de tipo `Narrative` (uno por tramo, con su `SegmentAnalysis`) y reemplaza los anteriores.
  **Los fragmentos `Visual` son los únicos contiguos y editables**: todo lo que arma la franja, edita o planifica
  el análisis filtra por `Kind == Visual`; el buscador y el índice incluyen los dos tipos.
- **Análisis en lote y automático**: `AnalysisService.EstimateBatchAsync`/`RequestBatchAsync` recorren los videos
  con escenas y `AnalysisStatus` nulo y respetan tres topes: por video, por tanda (`Ai:MaxCostPerBatchUsd`) y lo
  que queda del presupuesto mensual (acá sí bloquea). `AutoAnalysisScheduler` (cuarto servicio alojado) pide una
  tanda en cada organización con `Organization.AutoAnalyzeNewVideos`; apagado por defecto.
- **Estrategia (Fase 3)**: `Services/Strategy`. `BrandService` (perfil y sus listas, todas en `BrandItem` con
  `Kind`) y `BriefService` (un brief es de una marca: su producto debe ser de ella y ningún texto puede contener
  un mensaje prohibido). Reglas puras con tests en `StrategyRules.cs`: `MessageRules.Violations` (frases literales,
  sin mayúsculas, acentos ni espacios) y `BriefTextParser.Parse` (sólo lo explícito; una ocasión nueva se agrega
  a su lista con su caso de test).
- **Motor estratégico**: `StrategyService` + `StrategyPlanJobHandler` (trabajo `strategy.plan`) sobre el contrato
  `IConceptPlanner` (`Services/Ai/ConceptPlanning.cs`); la implementación es `GeminiConceptPlanner` y los tests usan
  `FakeConceptPlanner`. La consigna está en `ConceptPlanPrompt`: **cambiar su texto obliga a cambiar `Version`**.
  `StrategyPlanJobHandler.BuildConcepts` (función pura con tests) filtra lo que devuelve el modelo. El planificador
  nombra los fragmentos por número: el orden de `StrategyService.SourcesAsync` es el mismo al armar el pedido y
  al guardar. La autonomía del brief decide si el trabajo además arma las recetas y las renderiza.
- **Rendimiento (Fase 4)**: `Services/Meta`. `MetaCsvParser` (funciones puras con tests) lee el informe; un nombre de
  columna nuevo se agrega a `MetaCsvParser.Columns` con su caso de test. `MetaImportJobHandler` (trabajo
  `meta.import-csv`) guarda una fila por anuncio y día y **reemplaza** al reimportar. `MetaInsightDaily` sólo guarda
  cantidades sumables: **las tasas se calculan en `AdMetrics` sobre las sumas del período, nunca se guardan ni se
  promedian**. El vínculo con la creatividad es `MetaAd.CreativeVariantId`; `MetaService.LineageAsync` reconstruye
  el linaje. Regla de producto 6: las pantallas de rendimiento muestran observaciones, no declaran ganadores.
- **Análisis por componente**: `PerformanceService` arma los datos y `PerformanceRules` (funciones puras con tests)
  decide: nivel de evidencia, señal estadística (z de dos proporciones sobre el CTR), fatiga, agrupación y el texto
  de cada lectura. **Una lectura nunca dice "ganador" ni afirma una causa** (hay un test que lo fija) y con
  evidencia insuficiente no se calcula señal. Los umbrales son constantes con nombre, sin calibrar con datos reales.
  Una dimensión nueva se agrega en `PerformanceService.Dimensions`.
- **Experimentos**: `ExperimentService` + `ExperimentRules` (funciones puras con tests). Todo se define antes de mirar
  resultados; **el resultado no se guarda**, se calcula del período con `ExperimentRules.Evaluate`. Reglas que no se
  negocian: sin el mínimo de impresiones por brazo no se informa diferencia; con más de dos brazos el umbral sube;
  los problemas de diseño (campañas distintas, reparto desparejo, creatividades que difieren en más de una cosa)
  se avisan y matizan el veredicto. La conclusión la escribe una persona.
- **Aprendizaje (Fase 5)**: `Services/Learnings` (en plural: `Learning` es la entidad y un espacio de nombres igual
  la taparía). `HypothesisService` y `LearningService`, con las reglas puras en `LearningRules`. **Un aprendizaje
  siempre tiene datos detrás** (un experimento o el patrón de una hipótesis) y **la confianza la calcula el sistema**
  (`LearningRules.FromExperiment`/`FromPattern`): una correlación no pasa de media y sólo un experimento limpio,
  terminado y con diferencia clara llega a alta. Cambiar esas reglas obliga a cambiar `LearningRules.CriteriaVersion`.
  Datos, resultado y confianza quedan fijos como estaban al registrar; el detalle los recalcula y avisa si
  cambiaron. No se edita ni se borra: corregir crea una versión nueva (`ReplacesLearningId`) e invalidar lo saca de
  los vigentes, que son los únicos que cuentan para planificar. El patrón de una hipótesis se vuelve a calcular en
  el servidor (`HypothesisService.PatternAsync`): no se confía en lo que mande el navegador. El estado de una
  hipótesis se deduce (`LearningRules.State`), no se guarda.
- **Priorización**: `PriorityService` + `PriorityRules` (funciones puras con tests), en `Services/Learnings`. Ordena
  las hipótesis en pie con diez factores de 0 a 1: seis los calcula el sistema y cuatro los valora una persona en
  la hipótesis (`AssessmentLevel`). **Un factor sin dato no suma** y **no hay puntajes de IA**: el puntaje ordena,
  no predice (regla de producto 6; un test fija los textos). El puntaje no se guarda. Las ponderaciones
  (`PriorityWeightSet`) **no se editan**: guardar crea una versión y la vigente es la de número más alto; sin
  ninguna rigen `PriorityRules.DefaultWeights` (versión 0). Un factor nuevo = valor en `PriorityFactor`, su cálculo
  en `PriorityRules.Factors`, su columna en `PriorityWeightSet` y su nombre en `Formatting.Factor`.
- **Plan de producción**: `ProductionPlanService` + `ProductionPlanRules` (funciones puras con tests), en
  `Services/Learnings`. Toma las hipótesis de mayor puntaje y guarda una **foto**: puntaje, desglose
  (`FactorsJson`), versión de las ponderaciones, propuesta y justificación; no se edita. **"Producir aplicándola"
  sólo con un aprendizaje vigente de confianza media o alta**; con menos se propone probar o explorar
  (`ProductionPlanRules.Action`). No usa IA. Es distinto del plan de conceptos de la Fase 3 (`PlansController`):
  ése sale de un brief y de un modelo. El único puente es `LearningService.ForPlanAsync`, que le pasa los
  aprendizajes vigentes al planificador de conceptos (`PlanRequest.Learnings`).
- **Automatización (Fase 6)**: `Services/Automation`. `AutomationService` + `AutomationRules` (guardas puras con
  tests) + `AutomationExecuteJobHandler` (trabajo `automation.execute`). **Regla de producto 7, en código**: la
  ejecución real está desactivada por defecto y una acción creada así es una simulación (`IsSimulation`) que nunca
  llama a `IMetaPublisher`; hay un test que lo comprueba con `FakeMetaPublisher`. Las guardas se evalúan tres veces
  (pedir, aprobar, ejecutar) con `AutomationService.EvaluateAsync`. Una guarda nueva = un caso en
  `AutomationRules.Evaluate` con su test. `IMetaPublisher` es el único lugar por donde se escribe en Meta; la
  implementación registrada es `NotConnectedMetaPublisher` (no configurada), y **el cliente real no existe**:
  antes de escribirlo, consultar la documentación oficial de la Marketing API. Los permisos (aprobar, política y
  kill switch son de administrador) están en `AutomationController`, con un test que los fija.
- **Anomalías y propuestas**: `AnomalyService` + `AnomalyRules` (funciones puras con tests) buscan anomalías en el
  último día de cada anuncio; `MetaImportJobHandler` las busca al terminar cada importación. **Una anomalía sin
  revisar pasa todo a aprobación en modo automático** (`AutomationRules.Evaluate`, parámetro `openAnomalies`).
  `AutomationService.ProposeFromPlanAsync` arma acciones desde los renglones `Apply` de un plan de producción que
  tienen fragmento, eligiendo un video con `AutomationRules.Uses`; el lote entero se rechaza si supera
  `AutomationPolicy.MaxBatchBudget`. Una anomalía nueva = un caso en `AnomalyRules.Detect`, su valor en
  `AnomalyKind` y su nombre en la vista.
- **Navegación**: el menú (`Views/Shared/_Layout.cshtml`) agrupa por etapa del trabajo: Inicio, Biblioteca,
  Producción, Estrategia, Resultados, Automatización y Configuración. **Una pantalla nueva se agrega a su grupo y
  al `switch` que decide la sección activa**; no se suma una entrada de primer nivel: con siete el menú entra en
  1366 px, y por debajo de 1200 px pasa a menú desplegable. El inicio (`Home/Index`) muestra qué hay en cada etapa
  y el paso siguiente, que decide `DashboardRules.Next` (función pura con tests). Los nombres legibles de los
  tipos de trabajo están en `jobs.js` (`typeNames`): un tipo de trabajo nuevo se agrega ahí. El plan que sale de un
  brief se llama siempre "plan de conceptos"; "plan de producción" es el de la Fase 5. Las introducciones de las
  pantallas son de una o dos frases; el detalle va en un desplegable "Cómo se lee esto", y lo que no puede
  esconderse (que un puntaje no predice resultados, que una lectura es una correlación) queda a la vista.
- **Archivos con acentos**: no reescribir un archivo con `Get-Content | Set-Content` de PowerShell 5.1: lee sin BOM
  como ANSI y rompe los acentos. Usar `[IO.File]::ReadAllText`/`WriteAllText` con UTF-8 o la herramienta de edición.
- **Investigación externa**: `ResearchService` + `ResearchJobHandler` (trabajo `strategy.research`) sobre el contrato
  `IResearchAgent` (`Services/Ai/ResearchAgent.cs`; `GeminiResearchAgent` usa la herramienta `google_search`; los
  tests, `FakeResearchAgent`). **Regla que no se negocia**: una referencia sólo se guarda si su dirección pertenece
  a una fuente autorizada y activa (`ResearchRules.MatchDomain`, función pura con tests de engaños) y sólo se
  guarda dirección y resumen, nunca material. Lo que devuelve el agente pasa por `ResearchJobHandler.Keep` antes
  de guardarse. Antes de producción: cumplir los términos del proveedor sobre mostrar las sugerencias de búsqueda.
- **Protocolo de Gemini**: `GeminiInteractions` es el único lugar que conoce la forma de la API Interactions;
  `GeminiSegmentAnalyzer` y `GeminiConceptPlanner` sólo aportan consigna y esquema. Una tarea nueva de IA con
  Gemini = otro cliente chico sobre `GeminiInteractions`, su contrato y su doble de prueba.
- **Cultura**: `Program.cs` fija `CultureInfo.InvariantCulture`. Los números se leen y se escriben con punto en
  cualquier servidor; un `<input type="number">` siempre envía punto. En las vistas, un decimal en un `value` va
  con `InvariantCulture`.
- **Composición (Fase 2)**: `Services/Composition/CompositionService.cs` tiene las reglas puras
  (`CompositionRules`: formatos, límites, control de calidad), el servicio y `CreativeRenderJobHandler` (trabajo
  `creative.render`). Una receta es **inmutable** y cada `RecipeItem` guarda el video y el tramo copiados, no sólo
  el fragmento: los fragmentos se borran al redetectar y `MediaSegmentId` queda nulo. La autorización se comprueba
  tres veces (crear, pedir render, renderizar). El render es un solo comando de FFmpeg
  (`IMediaTools.RenderAsync`); los generados van a `storage/org-{id}/variants/{id}.mp4`. Un fallo de FFmpeg o de
  derechos deja la variante `Failed` y lanza `PermanentJobException`.
- **Terminación**: `SubtitleBuilder` (funciones puras con tests) arma los subtítulos desde la transcripción y los textos
  de la receta, y escribe un archivo ASS que FFmpeg quema con el filtro `subtitles`. **FFmpeg corre en la carpeta
  temporal de ese archivo y lo nombra sin ruta**: una ruta de Windows dentro de un filtro exige escapar `:` y `\`.
  `SubtitleBuilder.Escape` impide que un texto de usuario escriba órdenes de formato ASS. La música
  (`MusicService`, `MusicTrack`, trabajo `music.probe`) tiene sus propios derechos y entra en
  `CompositionService.UnauthorizedSourceAsync` junto con los originales. El fundido a negro no cambia la duración
  (por eso el control de duración no lo conoce). Un control de calidad nuevo = una regla en `CompositionRules` y
  su etiqueta en `recipes.js`.
- **Editor de recetas**: el recorte viaja en `RecipeItemBody.StartMs`/`EndMs` y se valida contra el fragmento en
  `CreateAsync`. Duplicar es `GET /Recipes/Create?from=ID`: `CompositionService.DraftAsync` arma el borrador y la
  vista lo entrega como JSON al editor. La vista previa es sólo del navegador (reproduce los originales por tramos):
  no usa FFmpeg ni genera archivos. Cancelar un lote (`BatchService.CancelAsync`) busca los trabajos por su clave
  de idempotencia `creative.render:{variante}`.
- **Variantes y lotes**: `VariantGenerator` (función pura con tests) arma montajes por función (hook → desarrollo →
  cierre) y descarta los parecidos (`Similarity`: 70 % material compartido + 30 % orden); es determinista por
  semilla, y por eso la vista previa coincide con lo que se crea. **Cambiar el algoritmo cambia lo que una semilla
  guardada reproduce.** `BatchService` crea una receta por variante con `CompositionService.CreateAsync(body,
  batchId, …)` y encola sus renders, todo en una transacción. El avance de un lote no se guarda: se deduce de sus
  videos.
- **Consulta interpretada**: `SearchQueryParser.Parse` (función pura con tests) separa de la consulta la función
  (sólo si es su primera palabra), la duración máxima, "autorizados" y "para producción". Lo elegido en el
  formulario manda. Una palabra nueva se agrega ahí con su caso de test.
- **Fallos de trabajos**: lanzar `PermanentJobException` cuando reintentar no arregla nada (el trabajo falla sin
  gastar intentos); cualquier otra excepción se reintenta.
- **Transacciones**: `AppDbContext.InTransactionAsync` es reentrante; un servicio que llama a otro comparte la
  transacción.
- **Almacenamiento**: `Storage:RootPath` es relativo al directorio de trabajo; con `dotnet run` apunta a
  `storage/` en la raíz (ignorada por git).
- **Auditoría**: explícita. Cada operación de escritura de un servicio llama a `AuditService.Record` y guarda en la
  misma transacción.
- **Errores**: `BusinessException(status, mensaje, código)`; el filtro global devuelve JSON a AJAX y un aviso al
  volver de un formulario. Los controllers no validan `ModelState` ni atrapan excepciones.
- **Idioma**: identificadores en inglés (como los nombra la directiva: `MediaSegment`, `CreativeRecipe`…);
  documentación XML, comentarios, mensajes e interfaz en español.

## Cómo correrlo

```bash
dotnet run --project src/CreativeIntelligence.Web --launch-profile http
```

- Es un solo proceso: sirve la Web en `http://localhost:5021` y ejecuta los trabajos en segundo plano. Al arrancar
  crea y migra la base. Usuarios en [CREDENCIALES.md](CREDENCIALES.md).
- Migración nueva: `dotnet ef migrations add <Nombre> -p src/CreativeIntelligence.Web -o Data/Migrations`.
- **LocalDB**: si la Web se mata a la fuerza, la instancia puede quedar viva pero "detenida" para `sqllocaldb`, y
  los tests fallan con "SQL Server process failed to start". No matar `sqlservr.exe` (la instancia tiene bases de
  otros proyectos): esperar unos 5 minutos sin conexiones a que se apague sola y correr `sqllocaldb start MSSQLLocalDB`.
  Si otra sesión la mantiene ocupada (la Web corriendo desde Visual Studio), los tests pueden entrar por su
  canalización: variable `CI_TEST_SQL_SERVER=np:\\.\pipe\LOCALDB#XXXXXXXX\tsql\query` (el nombre sale del argumento
  `-s` del proceso `sqlservr.exe`). Con la Web abierta en Visual Studio, compilar y probar con `-c Release`.
- Tests: `dotnet test` (recrean la base `CreativeIntelligence_Test` en LocalDB).

## Estándares de código

Se aplica el skill `backend-dotnet-estandares` a todo el C#:

- Tipos explícitos, sin `var`. Sin tipos anónimos, `object`, `dynamic` ni diccionarios genéricos como contrato.
- Sólo EF Core con LINQ: nada de `FromSql*`, `ExecuteSql*`, Dapper ni SQL en strings.
- Lecturas con `AsNoTracking()` y proyección al DTO; sin N+1.
- Controller fino: permiso, una llamada al servicio, mapeo. Sin `DbContext`, sin reglas, sin `try/catch`.
- Errores de negocio con excepción tipada, status y código estable; un filtro global los traduce.
- Entidades separadas de DTOs y de ViewModels. Validación declarativa. `CancellationToken` en toda la cadena.
- Documentación XML en todo tipo y miembro; comentarios sólo para el porqué.
- Sin código innecesario: interfaces sólo donde hay más de una implementación o un límite externo (IA, storage, Meta,
  herramientas de medios).

Particularidades de este proyecto frente al skill:

- La base es **SQL Server**, no PostgreSQL: los scripts generadores del skill no se usan tal cual; las funciones de
  fecha y los bloqueos se mapean a sus equivalentes de SQL Server.
- La aplicación es **MVC con vistas Razor**: además de DTOs hay ViewModels, y las acciones AJAX devuelven records con
  nombre.

## Configuración y seguridad

- Valores de desarrollo en `appsettings.json`; producción los sobreescribe fuera del repositorio o por variables de
  entorno. La aplicación valida la configuración al arrancar.
- `CREDENCIALES.md` en la raíz con los valores **de desarrollo**; se actualiza en el mismo cambio que crea un usuario,
  una base o una clave.
- Credenciales reales, tokens de Meta y claves de proveedores nunca se commitean ni se registran en logs.
- Tests sólo contra bases de prueba.
- Commit, push, merge y borrados sólo con confirmación del usuario.

## Verificación antes de dar algo por terminado

1. `dotnet build -warnaserror` en 0.
2. `dotnet test` en verde; consulta nueva → test de traducción; regla nueva → test que la fije.
3. Búsqueda de `var `, `new {`, `FromSql`, `ExecuteSql`, `SqlQuery`, `dynamic`, `catch { }` en lo escrito: 0.
4. Reporte fiel de qué se compiló, qué se corrió y qué no se pudo verificar.
5. Documentación y matriz de requisitos actualizadas en el mismo cambio.

## Prerrequisitos del entorno

- .NET SDK 10 y Python 3 instalados. Los paquetes Microsoft van fijados a `10.0.*` (ya existe .NET 11).
- SQL Server LocalDB (`(localdb)\MSSQLLocalDB`) para desarrollo y tests.
- **FFmpeg 9.0.2** instalado con winget (`Gyan.FFmpeg`); `ffmpeg` y `ffprobe` deben estar en el PATH de la Web y de
  `dotnet test`. Si no lo están, se configura la ruta en la sección `MediaTools` (`FfmpegPath`, `FfprobePath`).
- **Modelos de Whisper** en `tools/media/models` (fuera de git): `tiny` (75 MB, tests) y `small` (460 MB, la aplicación).
  Se descargan solos de Hugging Face la primera vez que se usan, así que esa primera transcripción necesita red.
- **Python**: entorno propio en `tools/media/.venv` (fuera de git) con `scenedetect==0.7.3`,
  `opencv-python==5.0.0.93` y `faster-whisper==1.2.1`. Para recrearlo: `python -m venv tools/media/.venv` y luego
  `tools/media/.venv/Scripts/python -m pip install -r tools/media/requirements.txt`. La aplicación lo encuentra por
  `MediaTools:PythonPath` y `MediaTools:ScriptsPath` de `appsettings.json`.
- La carpeta todavía no es un repositorio git.

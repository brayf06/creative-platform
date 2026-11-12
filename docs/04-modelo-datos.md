# Modelo de datos evolutivo

Cada fase **agrega** tablas y relaciones; no reescribe las anteriores. Todas las entidades de negocio llevan
`OrganizationId`, fechas de creación/modificación y usuario. Los nombres son orientativos y se fijan al implementar.

## Fase 0 — Fundamentos

| Entidad | Contenido |
|---|---|
| `Organization` | Empresa. Raíz del aislamiento. `AutoAnalyzeNewVideos` (migración `AddAutoAnalyze`): si el análisis con IA de los videos nuevos se pide solo, dentro de los topes |
| `ApplicationUser`, roles (Identity) | Usuarios y permisos |
| `OrganizationMember` | Usuario ↔ organización, rol dentro de ella |
| `BackgroundJob` | Tipo, clave de idempotencia (única), estado, progreso, intentos, arriendo, cancelación, error, carga útil |
| `AuditEntry` | Usuario, acción, entidad, identificador, resumen del cambio, fecha |
| `AiUsageRecord` | Tarea, proveedor, modelo, versión de prompt, tokens, costo, duración, trabajo |
| `AiResultCache` | Clave (hash de entrada + modelo + versión de prompt), resultado. **Se crea al inicio de la Fase 1**, con el primer proveedor real |

Implementado el 2026-11-11 (migración `InitialCreate`): todas las anteriores salvo `AiResultCache`. Los nombres reales
son `Organizations`, `OrganizationMembers`, `BackgroundJobs`, `AuditEntries`, `AiUsageRecords` y las tablas de Identity.

## Fase 1 — Biblioteca inteligente

| Entidad | Contenido |
|---|---|
| `MediaAsset` | Archivo original: tipo, SHA-256, ruta, tamaño, **procedencia** (tipo y referencia) y **derechos** (titular, licencia y restricciones, autorización de reutilización, vigencia). Único por organización y hash. **Propiedades técnicas** (migración `AddMediaTechnicalProperties`): estado y error del análisis, duración en ms, ancho y alto como se ven, fps, códecs de video y audio, contenedor y ruta de la miniatura. **Duplicados visuales** (migración `AddVisualDuplicates`): `PerceptualHash` (16 hexadecimales por cuadro muestreado) y `DuplicateOfAssetId` (archivo anterior con la misma imagen) |

Implementado el 2026-11-11 (migración `AddMediaAssets`): `MediaAssets` con procedencia y derechos **en la misma
tabla**. Se descartaron las tablas separadas `MediaSource` y `RightsRecord` previstas: hoy cada archivo tiene una sola
procedencia y un solo juego de derechos, y separarlas sería estructura sin caso. Si la Fase 2 necesita derechos
compartidos (por ejemplo, una licencia que cubre muchas pistas de música), se extraen entonces.
| `WatchedFolder`, `WatchedFile` | Carpeta vigilada (ruta, activa, derechos por defecto, última revisión y su resumen) y archivos ya vistos en ella (ruta relativa, tamaño, fecha de modificación, resultado y archivo de la biblioteca). **Implementados** (migración `AddWatchedFolders`). `MediaAsset.SourceKind` suma el valor `WatchedFolder`, con la ruta en `SourceReference` |
| `MediaSegment` | `MediaAssetId`, clase (**Visual** / **Narrativo**), inicio y fin en milisegundos, miniatura, origen (detectado / manual). **Implementado** (migración `AddMediaSegments`), con restricción `EndMs > StartMs`. Los visuales son contiguos y cubren el video; los narrativos (migración no necesaria: misma tabla) los crea el análisis con IA, uno por tramo, con su propio `SegmentAnalysis`, y se reemplazan en cada reanálisis. El origen manual es lo que protege las correcciones frente a un reanálisis. `AudioMeanDb` y `AudioPeakDb` (migración `AddSegmentAudioLevels`) guardan el volumen del tramo; la calidad de audio no se guarda, se deduce de ellos. `MediaAsset` suma el estado y el error de la detección de escenas |
| `MediaTranscriptLine` | Frase de la transcripción de un video: inicio, fin y texto. **Implementado** (migración `AddTranscripts`). Pertenece al **video**, no a la escena: el texto de cada escena se deriva por tiempo al consultarlo, así sigue siendo correcto cuando los cortes cambian. `MediaAsset` suma estado, error, idioma y modelo de la transcripción |
| `SegmentAnalysis`, `SegmentAnalysisFunction`, `AiResultCache` | **Implementados** (migración `AddSegmentAnalysis`). El análisis guarda el tramo analizado (para saber si quedó desactualizado), proveedor, modelo y versión de prompt, y se versiona con `IsCurrent`. Las funciones narrativas (taxonomía de 12, enum `CreativeFunction`) van en filas aparte para poder filtrarlas. `SegmentAnalysis.InheritedFromAnalysisId` (migración `AddAnalysisInheritance`) indica que el análisis se copió del de un duplicado visual; es una referencia sin clave foránea, porque el análisis de origen puede desaparecer. El cache es por organización, con clave = hash de modelo + prompt + contenido + tramos + cuadros + texto. `MediaAsset` suma estado y error del análisis |
| `SegmentAnalysis` (diseño original) | Versionado por segmento: descripción visual, transcripción, texto en pantalla, producto, acciones, enfoque comercial, mensaje principal, público, calidad visual y de audio, confianza, modelo, versión de prompt, `EsVigente` |
| `SegmentFunction` | Segmento ↔ función narrativa (muchos a muchos) con confianza |
| `MediaSegmentTag` | Etiqueta de un fragmento: texto y origen (manual / detectado), única por fragmento. **Implementado** (migración `AddSegmentEditing`), junto con `MediaSegment.SelectedForProduction` y `MediaAsset.SegmentsVersion` (guarda de concurrencia de la edición). Sin tabla `Tag` aparte: no hay todavía un vocabulario controlado que la justifique |
| `SegmentEmbedding` | Vector normalizado (float32 en `varbinary`), modelo, dimensiones y hash del texto indexado; uno por fragmento y modelo. **Implementado** (migración `AddSegmentEmbeddings`). No se usa el tipo vectorial de SQL Server: LocalDB no lo trae, y la similitud en memoria alcanza para miles de fragmentos |
| `SegmentRepeat` | Pares de fragmentos repetidos entre videos |
| ~~`SegmentSelection`~~ | Reemplazada por la columna `MediaSegment.SelectedForProduction`: la selección es un atributo del fragmento, no una lista aparte. Si la Fase 2 necesita varias selecciones con nombre, se agrega entonces |

Decisiones:
- **Visual y narrativo en la misma tabla**: un narrativo (p. ej. un hook) abarca varios visuales; la contención se
  deriva del rango de tiempo dentro del mismo asset.
- **El análisis se versiona** en lugar de sobrescribirse: permite reanalizar con otro modelo sin perder historia ni
  correcciones manuales.
- Tiempos en milisegundos enteros, no decimales de segundo.

## Fase 2 — Motor de composición

| Entidad | Contenido |
|---|---|
| `CreativeBatch` | Brief (texto en esta fase), cantidad pedida, estado, progreso, presupuesto, costo estimado y real |
| `CreativeRecipe` | Concepto comercial, hipótesis, duración objetivo, formato, resolución, música, firma de similitud, versión |
| `RecipeItem` | Receta ↔ segmento: orden, rol (hook, desarrollo, CTA, complementaria…), recorte, transición |
| `RecipeOverlay`, `RecipeSubtitle` | Textos, tiempos y estilo |
| `MusicTrack` | Pista con su `RightsRecord` |
| `CreativeVariant` | Video generado: receta, lote, archivo, propiedades, estado, costo |
| `RenderAttempt` | Intento de render: comando, duración, error |
| `QualityCheck` | Regla, resultado, detalle por variante |

`RecipeItem` es el corazón del linaje: une cada video generado con **todos** los fragmentos usados y el rol que cumplió
cada uno.

**Implementado en el paso 1 (migración `AddComposition`):** `CreativeRecipe` (nombre, concepto, hipótesis, ancho y
alto de salida; inmutable), `RecipeItem` (posición, `MediaAssetId`, tramo copiado en milisegundos, función, y
`MediaSegmentId` opcional que pasa a nulo si el fragmento deja de existir), `CreativeVariant` (estado, error, archivo,
duración, resolución, tamaño) y `QualityCheck` (regla, resultado, detalle; una fila por regla y video). Decisión: el
item guarda el video y el tramo, no sólo el fragmento, porque los fragmentos se editan y se vuelven a detectar y la
receta tiene que seguir diciendo lo mismo. **Paso 2 (migración `AddBatches`):** `CreativeBatch` (nombre, brief, formato,
cantidad pedida, duración máxima, umbral de similitud, semilla y candidatos descartados; el avance se deduce de sus
videos), `CreativeRecipe.CreativeBatchId` y `CreativeVariant.RenderMs`. **Paso 3 (migración `AddFinishing`):** en
`CreativeRecipe`, `Subtitles`, `Transition` (corte o fundido a negro), `MusicTrackId` y `MusicVolumePercent`;
`RecipeOverlay` (texto, posición y momento relativo: todo, apertura o cierre) y `MusicTrack` (archivo por hash, duración,
estado del análisis y sus propios derechos, en la misma tabla en lugar de un `RightsRecord` aparte). No hay
`RecipeSubtitle`: los subtítulos no se guardan, se derivan de la transcripción al renderizar. Todavía no existen
`RenderAttempt` (el intento se ve en el trabajo de la cola).

## Fase 3 — Investigación y estrategia

| Entidad | Contenido |
|---|---|
| `Brand`, `Product` | Marca y productos, con márgenes |
| `BrandBenefit`, `BrandObjection`, `BrandAudience`, `BrandMessageRule` | Beneficios verificables, objeciones, públicos, mensajes permitidos y prohibidos |
| `BrandStyle` | Estilo de comunicación e identidad visual |
| `CampaignBrief` | Producto, ocasión, público, objetivo, mensaje, enfoques, duración, cantidad, plataforma, investigación externa, presupuesto, autonomía, texto original |
| `ResearchSource` | Fuente autorizada y sus condiciones de uso |
| `ResearchRun` | Corrida del agente: consultas, costo |
| `ExternalReference` | Referencia: procedencia, permisos, **utilizable / sólo referencia**, resumen |
| `CreativeConcept` | Hipótesis, enfoque, hook, guion, desarrollo, CTA, público, motivo, riesgos, novedad |
| `ConceptRequirement` | Material requerido: existente (segmento) o faltante |
| `GeneratedAsset` | Recurso creado por IA: proveedor, prompt, costo, restricciones aplicadas |

**Implementado en el paso 1 (migración `AddBrandsAndBriefs`):** `Brand` (datos generales y presupuesto mensual),
`BrandItem` y `CampaignBrief`. Decisión: las seis listas del perfil (productos, beneficios, públicos, objeciones,
mensajes permitidos y prohibidos) van en **una sola tabla** `BrandItem` con su clase en `Kind`, en lugar de
`Product`, `BrandBenefit`, `BrandObjection`, `BrandAudience`, `BrandMessageRule` y `BrandStyle`: todas tienen la misma
forma (texto + detalle; el producto suma el margen) y el estilo y la identidad son campos de `Brand`. Si un producto
necesita más datos (precio, variantes), se separa entonces. `CampaignBrief.ProductId` apunta a un `BrandItem` de clase
producto de la misma marca.

**Paso 2 (migración `AddCreativePlans`):** `CreativePlan` (brief, estado, cantidad pedida, proveedor, modelo, versión
de la consigna y costo), `CreativeConcept` (los campos del concepto, lo que falta y los mensajes prohibidos que usa
como texto de un renglón por elemento, y `CreativeRecipeId` cuando se arma su receta) y `ConceptFragment` (material
existente propuesto: video, tramo y fragmento opcional, igual que `RecipeItem`). `ConceptRequirement` no existe como
tabla: lo existente es `ConceptFragment` y lo faltante es texto.

**Paso 3 (migración `AddResearch`):** `ResearchSource` (dominio único por organización, condiciones de uso, permiso por
defecto, activa), `ExternalReference` (fuente, brief y corrida opcionales, clase, dirección única por organización,
título, resumen, permiso y origen; **no guarda material**) y `ResearchRun` (estado, consultas de búsqueda, propuestas,
guardadas, modelo, versión de la consigna y costo). `GeneratedAsset` no existe: la generación no está construida.

Cambios sobre lo anterior: `CreativeBatch` y `CreativeRecipe` ganan `CampaignBriefId` y `CreativeConceptId`
(opcionales, para no romper lotes de la Fase 2).

## Fase 4 — Inteligencia de Meta

| Entidad | Contenido |
|---|---|
| `MetaConnection` | Token cifrado, permisos, vencimiento |
| `MetaAdAccount`, `MetaCampaign`, `MetaAdSet`, `MetaAd`, `MetaCreative` | Espejo de la estructura de Meta con identificador externo, estado y fechas |
| `MetaInsightDaily` | Por anuncio y día: gasto, impresiones, clics, CTR, CPC, CPM, reproducciones, retención, compras, valor, CPA, ROAS, ventana de atribución |
| `MetaSyncState` | Cursor y última sincronización por cuenta y recurso |
| `AdCreativeLink` | `MetaAd` ↔ `CreativeVariant` (o `MediaAsset` para históricos), origen (automático / manual / inferido) y confianza |
| `Experiment`, `ExperimentArm` | Hipótesis, variable, brazos, período, resultado |

Si se anticipa la importación básica, se crean sólo las cinco tablas espejo, `MetaConnection` y `MetaSyncState`.

**Implementado en el paso 1 (migración `AddMetaPerformance`):** `MetaAdAccount`, `MetaCampaign`, `MetaAdSet`, `MetaAd`,
`MetaInsightDaily` (tabla `MetaInsightsDaily`) y `MetaImport` (registro de cada archivo cargado). Diferencias con lo
previsto: `MetaInsightDaily` **no guarda tasas** (CTR, CPC, CPM, CPA, ROAS se calculan al consultar) ni ventana de
atribución; no hay `MetaCreative` (el CSV no la trae); el vínculo con la creatividad es `MetaAd.CreativeVariantId`
con su origen (manual o automático) en lugar de una tabla `AdCreativeLink`, porque un anuncio publica un solo video.
Si hace falta vincular históricos a un `MediaAsset` o guardar una confianza, se separa entonces. `MetaConnection` y
`MetaSyncState` llegan con la sincronización por API. **Paso 3 (migración `AddExperiments`):** `Experiment` (hipótesis,
variable, métrica, período, mínimo de impresiones por brazo, conclusión y fecha de cierre; **el resultado no se guarda**,
se calcula) y `ExperimentArm` (posición, nombre y un anuncio; el de posición 1 es el control).

## Fase 5 — Aprendizaje

| Entidad | Contenido |
|---|---|
| `Hypothesis` | Enunciado, origen, estado |
| `Learning` | Hipótesis, experimentos, datos usados, resultado, confianza, período, recomendación, aplicabilidad, versión de criterio, estado (vigente / corregido / invalidado) |
| `LearningEvidence` | Aprendizaje ↔ anuncios, variantes y segmentos que lo sustentan |
| `PriorityWeightSet` | Ponderaciones versionadas de los 10 factores |
| `OpportunityScore` | Puntaje por concepto con el desglose por factor y el conjunto de ponderaciones usado |
| `ProductionPlan`, `ProductionPlanItem` | Plan propuesto y su justificación |

**Implementado en el paso 1 (migración `AddLearning`):** `Hypothesis` (tabla `Hypotheses`: enunciado, origen manual o
patrón, variable, y del patrón su dimensión, clave, nombre, lectura al guardarla, campaña y período; **su estado no
se guarda**, se deduce de sus experimentos y aprendizajes, salvo el descarte), `Learning` (hipótesis y experimento
opcionales, enunciado, datos usados, resultado, confianza con su motivo, período, recomendación, aplicabilidad,
versión del criterio, estado con motivo y fecha, y `ReplacesLearningId` para las correcciones) y `LearningEvidence`
(un anuncio con el video que publicó, o un tramo de un original). `Experiment` gana `HypothesisId` (opcional). No
existen todavía `OpportunityScore` ni `ProductionPlan`.

**Paso 2 (migración `AddPriorities`):** `PriorityWeightSet` (número de versión único por organización, un peso de 0 a
10 por cada uno de los diez factores y el motivo del cambio; no se edita) y cuatro valoraciones opcionales en
`Hypothesis` (`AudienceRelevance`, `ProductionCost`, `AdRisk`, `PotentialMargin`: bajo, medio o alto).
**`OpportunityScore` no existe**: el puntaje se calcula al consultar.

**Paso 3 (migración `AddProductionPlans`):** `ProductionPlan` (nombre, cantidad pedida y versión de las ponderaciones)
y `ProductionPlanItem` (posición, hipótesis, y copiados: enunciado, variable, componente y tramo, propuesta
—aplicar, probar o explorar—, puntaje, justificación, desglose por factor como JSON, y el aprendizaje que la
respaldaba con su recomendación). El desglose guardado en cada renglón cumple el papel que el diseño le daba a
`OpportunityScore`.

## Fase 6 — Automatización

| Entidad | Contenido |
|---|---|
| `AutomationPolicy` | Modo, presupuestos máximos, límites diarios, reglas de aprobación, simulación, habilitación de ejecución real (falso por defecto) |
| `ApprovalRequest` | Acción pendiente, solicitante, aprobador, decisión |
| `AutomationAction` | Acción ejecutada o simulada, clave de idempotencia, resultado |
| `KillSwitchEvent`, `AnomalyEvent` | Activaciones y anomalías detectadas |

**Implementado en el paso 1 (migración `AddAutomation`):** `AutomationPolicy` (una por organización: modo, ejecución
real, kill switch y los tres límites; sin fila rige lo más restrictivo), `AutomationAction` (video, conjunto,
nombre, presupuesto diario, hipótesis de respaldo, si es simulación, estado, motivo de la aprobación, resultado,
identificador en Meta, quién la pidió y quién la decidió) y `KillSwitchEvent`. Diferencias con lo previsto: no hay
`ApprovalRequest` (la decisión se guarda en la acción: una acción tiene una sola decisión) ni clave de idempotencia
guardada (se deriva del identificador de la acción). **Paso 2 (migración `AddAnomalies`):** `AnomalyEvent` (anuncio,
tipo, día, descripción, y quién y cuándo la revisó; única por anuncio, tipo y día), `AutomationPolicy.MaxBatchBudget`
y `AutomationAction.ProductionPlanId` (el plan del que el sistema la propuso).

## Creative Lineage

```
MetaAd ─ AdCreativeLink ─► CreativeVariant ─► CreativeRecipe ─► RecipeItem ─► MediaSegment ─► MediaAsset
                                 │                  │              (rol: hook, desarrollo, CTA…)
                                 ▼                  ▼
                           CreativeBatch      CreativeConcept ─► CampaignBrief ─► Brand
                                                    │
                                                    └─► Hypothesis ─► Experiment ─► Learning
```

Con esta cadena, desde un anuncio se reconstruye cómo se generó, y desde un fragmento se obtienen todos los anuncios
donde participó y con qué rol. Por eso `RecipeItem` y `AdCreativeLink` se diseñan temprano aunque el análisis llegue en
la Fase 4.

## Reglas de evolución

- Una migración de EF por cambio de esquema; nunca se edita una migración ya aplicada.
- Columnas nuevas sobre tablas existentes: anulables o con valor por defecto.
- Nada se borra físicamente si participa del linaje: se desactiva.
- Enumeraciones persistidas como texto estable.

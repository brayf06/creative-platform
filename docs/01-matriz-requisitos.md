# Matriz de requisitos

Estado al 2026-11-11: **Fase 0 implementada y verificada** (ARQ-01 a ARQ-10; evidencia en
[02-roadmap.md](02-roadmap.md)). ARQ-08 es progresivo: hoy el menú tiene Dashboard, Trabajos y Configuración. ARQ-11 y
ARQ-12 se hicieron con el proveedor de IA; COS-02 (registro y pantalla de costos) hecho y verificado. **Fase 1, hecho y
verificado:** LIB-01 (carga individual y masiva), LIB-02 (carpetas vigiladas dentro de fuentes autorizadas), LIB-03 (propiedades técnicas), LIB-06 (originales inmutables,
procedencia y derechos), LIB-04 completo (duplicados exactos y visuales), LIB-05 (miniaturas por archivo y por segmento) y LIB-07
(cortes; falta medirlo sobre material real anotado). LIB-13 completo salvo "consultar análisis" (no hay análisis
todavía). De LIB-14, las correcciones manuales no se pierden al redetectar salvo confirmación explícita. De LIB-10,
la transcripción por fragmento; COS-04 (transcripción local) hecho, con su uso registrado en `AiUsageRecords`.
La calidad de audio de LIB-10 está hecha y verificada (medición con FFmpeg, sin IA).
**Implementados, sin verificar contra el proveedor real:** el resto de LIB-10,
ARQ-11 (proveedor de IA detrás de un contrato), ARQ-12 (versión de prompt y modelo en cada análisis), COS-01 (cache
por hash y versión), COS-03 (modelo económico para clasificación) y la estimación y el tope por video de COS-05.
LIB-11 y LIB-12 (buscador con filtros, miniatura, tiempos y acceso al original): verificados por palabras y filtros;
la parte semántica, sólo con proveedor falso. La interpretación de la consulta en lenguaje natural y los filtros por
calidad de audio y por tipo de fragmento: hechos y verificados (2026-11-11). LIB-09 (tramos narrativos que abarcan
varias escenas), el análisis en lote y el automático con sus topes (COS-05): implementados y cubiertos por tests, sin
verificar contra el proveedor real. LIB-13: arrastrar cortes hecho y verificado en el navegador. **La puerta de la
Fase 1 sigue sin medirse**; el usuario decidió construir la Fase 2 antes (ver roadmap). **Fase 2, paso 1, hecho y
verificado (2026-11-11):** REM-06 (sólo fragmentos autorizados; se comprueba al crear, al pedir el render y al
renderizar), REM-07 (linaje receta → tramo → original), REM-09 (re-render con el mismo montaje); parciales: REM-02
(receta sin transiciones, subtítulos, overlays ni música), REM-05 (render sin subtítulos ni overlays) y REM-10
(duración, resolución y audio; faltan cuadros negros y subtítulos). **Paso 2 (2026-11-11):** REM-04 (control de
similitud) hecho y verificado; REM-03 (variantes por hook, desarrollo, cierre, orden y duración; no por mensaje ni
formato) y REM-08 (lotes de 5 a 50 con estado, progreso, errores y videos; sin conceptos ni presupuesto) parciales.
REM-01: la taxonomía de 12 funciones existe desde la Fase 1 y un fragmento admite varias. **Paso 3 (2026-11-11):**
REM-02 completo, con recorte dentro de la receta desde el paso 4 (transiciones, subtítulos, textos y música autorizada), REM-05
completo (los nueve pasos del render) y REM-10 completo (duración, resolución, audio, cuadros negros y subtítulos),
todos verificados con tests y en el navegador. **Paso 4 (2026-11-11):** editor con recorte, duplicado y vista previa;
REM-08 suma cancelar y reintentar un lote. **Fase 3, paso 1 (2026-11-11), hecho y verificado:** STR-01 (perfil de marca), STR-02 (brief con sus 12 campos;
la interpretación del texto libre es determinista y sólo deduce lo explícito) y STR-09 (mensajes prohibidos, aplicado
a los briefs; todavía no a los textos de las recetas). **Paso 2 (2026-11-11), implementado y cubierto con proveedor
falso, sin verificar contra el proveedor real:** STR-05 (motor estratégico; todavía sin históricos, referencias ni
resultados), STR-06 (estructura del concepto) y STR-07 (plan con receta realizable o faltantes; el plan de 20 con
un brief estacional real no está probado). **Paso 3 (2026-11-11):** STR-04 hecho y verificado (referencias con
procedencia y permisos, sólo de fuentes autorizadas, sin descargar material); STR-03 implementado y cubierto con
agente falso, sin verificar contra el proveedor real. **STR-08 (generación audiovisual) no construido: falta elegir
proveedor.** **Fase 4, paso 1 (2026-11-11), hecho y verificado:** MET-05 (linaje anuncio → video → receta →
fragmentos → concepto → brief → marca, con vínculo manual; falta el experimento); MET-02 y MET-03 parciales, por
importación de CSV (sin API, sin estados ni fechas de campaña, sin retención detallada; sin conciliar contra Ads
Manager). **Paso 2 (2026-11-11), hecho y verificado:** MET-06 (análisis por fragmento de apertura, de cierre, fragmento,
estructura y terminación, y fatiga) y MET-08 (niveles de evidencia y lecturas que no declaran ganadores); parciales:
MET-07 (sólo filtro por campaña y período: faltan objetivo, audiencia, ubicaciones, oferta y atribución, que el CSV
no trae) y MET-10 (lecturas con limitaciones y nivel de evidencia, sin recomendaciones accionables). **Paso 3 (2026-11-11),
hecho y verificado:** MET-09 (experimentos definidos de antemano, con revisión del diseño; sin asignación aleatoria,
que da la herramienta de Meta) y el experimento dentro del linaje de MET-05. **MET-01 y MET-04 (API de Meta) no
construidos: faltan la app y el token.** **Fase 5, paso 1 (2026-11-11), hecho y verificado:** LRN-04 (memoria de
aprendizaje con todos sus campos; la confianza y la versión del criterio las fija el sistema) y LRN-05 (un
aprendizaje se revisa contra los datos de hoy, se corrige con una versión nueva o se invalida; test de invalidación);
de LRN-01, los pasos 3 y 4 del ciclo (de un patrón a una hipótesis) y el 11 (actualizar el conocimiento). **Paso 2
(2026-11-12), hecho y verificado:** LRN-02 (puntaje con los diez factores, seis calculados y cuatro valorados por una
persona; ponderaciones configurables, versionadas y auditadas; test determinista) y LRN-03 (ningún puntaje de IA; la
pantalla y un test fijan que no se presenta como predicción); de LRN-01, el paso 5 (priorizar oportunidades).
**Paso 3 (2026-11-12), hecho y verificado con material de prueba:** LRN-06 (plan de producción que prioriza
hipótesis por evidencia y explica cada elección; un aprendizaje invalidado deja de influir, con test) y, de LRN-01,
el paso 6 en su parte de decidir qué producir; el motor estratégico recibe los aprendizajes vigentes (sin probar
contra el proveedor real). **La puerta de la Fase 5 no está medida con campañas reales.** **Fase 6, paso 1 (2026-11-12), hecho y verificado
en simulación:** LRN-07 (tres modos, con tests), LRN-09 (límite por acción, por día y de cantidad; el de lote falta),
LRN-11 (simulación por defecto; un test comprueba que sin habilitación explícita no hay llamada de escritura) y, de
LRN-10, kill switch, datos insuficientes y duplicados (falta la detección de anomalías). LRN-08 parcial: las
aprobaciones están; **la publicación real en Meta no está construida: faltan la app y el token.** **Paso 2
(2026-11-12), hecho y verificado en simulación:** lo que faltaba de LRN-10 (detección de anomalías, que además le
quita la autonomía al modo automático) y de LRN-09 (presupuesto por lote), y LRN-01 como ciclo completo en
simulación (el sistema propone las publicaciones de un plan; evaluar resultados de lo publicado no aplica en
simulación). **La puerta de la Fase 2 sigue sin medirse. Todo lo
demás: "No iniciado".**
Al cerrar cada requisito, agregar la fecha y la evidencia (test, pantalla, muestra) en la columna *Verificación*.

Columnas: **F** = fase del roadmap. **Módulo** = módulo de [03-arquitectura-funcional.md](03-arquitectura-funcional.md).

## Arquitectura y fundamentos (ARQ)

| ID | Requisito | F | Módulo | Verificación |
|---|---|---|---|---|
| ARQ-01 | Solución ASP.NET Core MVC por capas (Web, Application, Domain, Infrastructure, Worker) | 0 | Plataforma | Build `-warnaserror` en 0 |
| ARQ-02 | SQL Server + EF Core con migraciones; sin SQL embebido | 0 | Plataforma | Test de arquitectura |
| ARQ-03 | Identity, roles y permisos por acción | 0 | Seguridad | Tests de autorización |
| ARQ-04 | Organizaciones y aislamiento multiempresa en toda consulta | 0 | Seguridad | Test: la org A no ve datos de la org B |
| ARQ-05 | Cola de trabajos en segundo plano con estado, progreso, reintentos, cancelación e idempotencia | 0 | Trabajos | Tests de cola (doble encolado, reintento, cancelación) |
| ARQ-06 | Progreso en tiempo real por SignalR | 0 | Trabajos | Prueba manual + test del hub |
| ARQ-07 | Auditoría de operaciones (quién, qué, cuándo, sobre qué) | 0 | Auditoría | Test: cada escritura deja registro |
| ARQ-08 | Layout unificado con las 17 secciones; sólo se enlazan las que tienen servicio funcional | 0+ | Web | Revisión de navegación por fase |
| ARQ-09 | Logging estructurado y manejo global de errores de negocio | 0 | Plataforma | Test del filtro de errores |
| ARQ-10 | Secretos fuera del repositorio en producción; validación de configuración al arrancar | 0 | Plataforma | Arranque falla si falta una clave |
| ARQ-11 | Proveedores de IA configurables detrás de contratos propios | 0–1 | IA | Test con proveedor falso |
| ARQ-12 | Versionado de prompts y modelos en cada resultado de IA | 1 | IA | Columnas de versión pobladas |

## Prioridad 1 — Biblioteca inteligente (LIB)

| ID | Requisito | F | Módulo | Verificación |
|---|---|---|---|---|
| LIB-01 | Carga individual y masiva de videos e imágenes | 1 | Ingesta | Carga de 50 archivos en un lote |
| LIB-02 | Detección de archivos nuevos en carpetas vigiladas / fuentes autorizadas | 1 | Ingesta | Test con carpeta de prueba |
| LIB-03 | Extracción de propiedades técnicas (ffprobe) | 1 | Ingesta | Test con videos de muestra |
| LIB-04 | Duplicados exactos (SHA-256) y visuales (hash perceptual) | 1 | Ingesta | Test: mismo archivo y recodificación detectados |
| LIB-05 | Miniaturas por archivo y por segmento | 1 | Ingesta | Verificación visual + test de existencia |
| LIB-06 | Originales inmutables; registro de procedencia y derechos | 1 | Ingesta | Test: ninguna operación modifica el original |
| LIB-07 | Detección de cortes, cambios de plano y transiciones (PySceneDetect + FFmpeg) | 1 | Escenas | Precisión sobre muestra anotada |
| LIB-08 | Detección de fragmentos repetidos entre videos | 1 | Escenas | Test con clips reutilizados |
| LIB-09 | Segmentos narrativos (hook, demostración, testimonio, problema, beneficio, CTA) distintos de los visuales; un narrativo puede abarcar varios visuales | 1 | Análisis | Muestra anotada |
| LIB-10 | Documentación automática por fragmento: descripción visual, transcripción, texto en pantalla, producto, acciones, enfoque comercial, función narrativa, mensaje, público, calidad visual/audio, etiquetas, embeddings, confianza, autorización, versión de análisis y modelo | 1 | Análisis | Todos los campos persistidos y visibles |
| LIB-11 | Búsqueda semántica combinada con filtros tradicionales (duración, función, producto, calidad, derechos) | 1 | Búsqueda | Conjunto de consultas de referencia |
| LIB-12 | Resultado de búsqueda con miniatura, timestamps y acceso al original | 1 | Búsqueda | Prueba de UI |
| LIB-13 | Timeline: ver cortes, reproducir fragmento, corregir límites, fusionar, dividir, editar etiquetas, consultar análisis, seleccionar para producción | 1 | Timeline | Pruebas de servicio + UI |
| LIB-14 | Las correcciones manuales no se pierden al reanalizar | 1 | Análisis | Test de reanálisis |

## Prioridad 2 — Motor de remix (REM)

| ID | Requisito | F | Módulo | Verificación |
|---|---|---|---|---|
| REM-01 | Taxonomía de 12 componentes; un fragmento puede cumplir varias funciones | 2 | Composición | Test de dominio |
| REM-02 | Receta estructurada: concepto, hipótesis, hook, desarrollo, complementarias, CTA, orden, duración, transiciones, subtítulos, overlays, música autorizada, formato y resolución | 2 | Composición | Validación de receta |
| REM-03 | Variantes cambiando hook, desarrollo, CTA, escenas, narrativa, duración, orden, mensaje o formato | 2 | Composición | Test de generador |
| REM-04 | Control de similitud: se rechazan variantes casi idénticas | 2 | Composición | Test con umbral |
| REM-05 | Render FFmpeg en 9 pasos (validar, recuperar autorizados, cortar, normalizar, combinar, subtítulos/overlays, renderizar, validar archivo, almacenar) | 2 | Render | Test de integración con clips reales |
| REM-06 | Sólo se usan fragmentos con autorización de reutilización | 2 | Render | Test: receta con fragmento no autorizado falla |
| REM-07 | Relación persistida entre cada video generado y todos sus fragmentos | 2 | Trazabilidad | Consulta de linaje |
| REM-08 | Lotes de 5 a 50 videos con brief, conceptos, variantes, recetas, estado, progreso, errores, costo, videos y validaciones | 2 | Lotes | Lote de 5 de punta a punta |
| REM-09 | Reproducibilidad: volver a renderizar una receta almacenada produce el mismo montaje | 2 | Render | Test de re-render |
| REM-10 | Control de calidad automático (duración, resolución, audio, cuadros negros, subtítulos) | 2 | Calidad | Tests con videos defectuosos |

## Prioridad 3 — Investigación y estrategia (STR)

| ID | Requisito | F | Módulo | Verificación |
|---|---|---|---|---|
| STR-01 | Perfil de marca: productos, beneficios verificables, público, propuesta de valor, objeciones, estilo, identidad visual, objetivos, restricciones, márgenes, presupuesto, mensajes permitidos y prohibidos | 3 | Marca | ABM completo |
| STR-02 | Brief de campaña con sus 12 campos, incluidos presupuesto máximo y nivel de autonomía | 3 | Briefs | ABM + interpretación de texto libre |
| STR-03 | Agente investigador sobre fuentes autorizadas (anuncios, tendencias, hooks, estructuras, estilos, formatos, estacionalidad) | 3 | Investigación | Corrida con fuentes configuradas |
| STR-04 | Referencias con procedencia y permisos; separación "utilizable" / "sólo referencia"; no se descarga ni reproduce material ajeno sin autorización | 3 | Investigación | Test: referencia no utilizable no entra en una receta |
| STR-05 | Motor estratégico: combina brief, marca, fragmentos, históricos, referencias y resultados | 3 | Estrategia | Plan generado |
| STR-06 | Cada concepto: hipótesis, enfoque, hook, guion, desarrollo, CTA, público, material requerido/existente, motivo, riesgos, novedad | 3 | Estrategia | Validación de estructura |
| STR-07 | Plan de 20 conceptos distintos; cada uno con receta realizable o lista explícita de faltantes | 3 | Estrategia | Brief estacional de prueba |
| STR-08 | Generación audiovisual nueva, opcional, con restricciones de personas, voces, licencias, música y producto | 3 | Generación | Desactivada por defecto; test de política |
| STR-09 | Validación de mensajes contra permitidos/prohibidos de la marca | 3 | Calidad | Test de reglas |

## Prioridad 4 — Inteligencia de Meta (MET)

| ID | Requisito | F | Módulo | Verificación |
|---|---|---|---|---|
| MET-01 | Conexión por Marketing API con permisos oficiales; token cifrado | 4 (básico antes si hace falta) | Meta | Conexión en cuenta de prueba |
| MET-02 | Importar cuentas, campañas, conjuntos, anuncios, creatividades, estados y fechas | 4 | Meta | Importación histórica |
| MET-03 | Insights: gasto, impresiones, CTR, CPC, CPM, reproducciones, retención, compras, CPA, ROAS | 4 | Meta | Conciliación contra Ads Manager |
| MET-04 | Sincronización incremental, paginación, control de cuotas y manejo de errores | 4 | Meta | Tests con API simulada |
| MET-05 | Creative Lineage: anuncio ↔ variante, receta, hook, desarrollo, CTA, segmentos, assets, brief, concepto, experimento | 1–4 | Trazabilidad | Pantalla de linaje completa |
| MET-06 | Análisis por componente (hooks, estructuras, desarrollos, CTA, fragmentos, fatiga) | 4 | Rendimiento | Consultas con datos importados |
| MET-07 | Control de factores: presupuesto, objetivo, audiencia, tiempo, ubicaciones, estacionalidad, oferta, volumen, atribución | 4 | Rendimiento | Segmentación visible en el análisis |
| MET-08 | Separar observación, correlación e inferencia causal; no declarar ganadores por un resultado aislado | 4 | Rendimiento | Umbrales de evidencia + etiquetas |
| MET-09 | Experimentos controlados | 4 | Experimentos | Experimento de prueba |
| MET-10 | Recomendaciones con limitaciones y nivel de confianza | 4 | Rendimiento | Revisión de salida |

## Prioridad 5 — Aprendizaje y automatización (LRN)

| ID | Requisito | F | Módulo | Verificación |
|---|---|---|---|---|
| LRN-01 | Ciclo de aprendizaje de 11 pasos | 5–6 | Aprendizaje | Ciclo completo en simulación |
| LRN-02 | Priorización con 10 factores; ponderaciones configurables, versionadas y auditables | 5 | Priorización | Test determinista de puntaje |
| LRN-03 | Los puntajes de IA no se presentan como predicción de ROAS | 5 | Priorización | Revisión de UI y textos |
| LRN-04 | Memoria de aprendizaje: hipótesis, experimentos, datos, resultados, confianza, período, recomendación, aplicabilidad, versión de criterio | 5 | Aprendizaje | Persistencia + pantalla |
| LRN-05 | Aprendizajes revisables, corregibles e invalidables | 5 | Aprendizaje | Test de invalidación |
| LRN-06 | Plan de producción nuevo justificado por evidencia y explicable | 5 | Aprendizaje | Plan a partir de datos importados |
| LRN-07 | Modos manual, semiautomático y automático controlado | 6 | Automatización | Tests por modo |
| LRN-08 | Publicación en Meta con aprobaciones | 6 | Automatización | Cuenta de prueba |
| LRN-09 | Límites: presupuesto por lote, presupuesto publicitario, límites diarios | 6 | Automatización | Test: acción que excede se bloquea |
| LRN-10 | Kill switch, detección de anomalías, control de datos insuficientes, protección contra duplicados | 6 | Automatización | Tests de cada guarda |
| LRN-11 | Modo simulación; ejecución real desactivada por defecto | 6 | Automatización | Test: sin habilitación explícita no hay llamada de escritura a Meta |

## Costos (COS) y calidad global (CAL)

| ID | Requisito | F | Módulo | Verificación |
|---|---|---|---|---|
| COS-01 | Análisis una sola vez; cache por hash de contenido + versión de modelo y prompt | 1 | IA | Test: segundo análisis no llama al proveedor |
| COS-02 | Registro de tokens y costo por llamada, trabajo, lote y organización | 1 | Costos | Pantalla "Costos de IA" |
| COS-03 | Modelos económicos para tareas simples, capaces para razonamiento complejo | 1 | IA | Configuración por tarea |
| COS-04 | Transcripción local opcional | 1 | Análisis | Configuración |
| COS-05 | Estimación previa, presupuesto por lote y alertas de consumo | 2 | Costos | Test: lote que excede no arranca |
| COS-06 | Medición de CPU/GPU, almacenamiento, render, investigación, reintentos, calidad e intervención humana | 2–5 | Costos | Indicador "costo por creatividad utilizable" |
| CAL-01 | Controladores delgados, servicios cohesionados, DTOs y ViewModels, validaciones | todas | — | Revisión + tests de arquitectura |
| CAL-02 | Pruebas unitarias e integración por fase | todas | — | Suite en verde |
| CAL-03 | Documentación actualizada en el mismo cambio | todas | — | Revisión |

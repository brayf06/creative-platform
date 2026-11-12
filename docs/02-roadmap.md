# Roadmap

Orden de prioridad definitivo:

1. Comprensión y reutilización del material existente.
2. Composición y generación automatizada de videos.
3. Investigación y estrategia creativa.
4. Análisis del rendimiento real en Meta.
5. Aprendizaje y optimización.
6. Automatización controlada de campañas.

**Regla de avance:** una fase se cierra sólo cuando su puerta de salida está verificada y registrada en este documento
(fecha + evidencia). Hasta entonces no se escribe código de la fase siguiente, salvo las excepciones marcadas como
"anticipado".

## Cambio de estructura (2026-11-11)

Por decisión del usuario, el código pasó de cinco proyectos por capas a **un solo proyecto MVC**, y los trabajos en
segundo plano pasaron a correr **dentro de la Web**. No cambió ninguna funcionalidad ni el esquema de la base (mismas
migraciones). Evidencia: `dotnet build -warnaserror` en 0, los mismos 116 tests en verde, y en el navegador un video
subido quedó analizado y dividido en escenas con un único proceso corriendo. Donde este documento dice "Worker", hoy
es la cola de trabajos dentro de la Web. El código anterior está en `respaldo-antes-de-mvc/`.

## Estado

| Fase | Nombre | Estado | Cierre |
|---|---|---|---|
| 0 | Fundamentos | **Cerrada** (conformidad del usuario) | 2026-11-11 |
| 1 | Biblioteca inteligente | **Construida, puerta sin medir** — el usuario decidió el 2026-11-11 seguir con la Fase 2 y medir la puerta al final, cuando configure la clave de IA | — |
| 2 | Motor de composición | **En curso** — **Construida, puerta sin medir** — pasos 1 a 4 hechos; la puerta necesita un lote de 10 o más con material real y revisión humana | — |
| 3 | Investigación y estrategia | **Construida salvo la generación opcional; puerta sin medir** — pasos 1 a 3 hechos; el motor y el agente sin probar contra el proveedor real | — |
| 4 | Inteligencia de Meta | **Construida salvo la API de Meta; puerta sin medir** — pasos 1 a 3 hechos (importación por CSV, linaje, análisis por componente, experimentos) | — |
| 5 | Aprendizaje | **Construida, puerta sin medir** — pasos 1 a 3 hechos (hipótesis, memoria de aprendizaje, priorización y plan de producción); la puerta pide resultados de campañas reales | — |
| 6 | Automatización publicitaria | **Construida en simulación; puerta a medias** — pasos 1 y 2 hechos (política, guardas, aprobaciones, kill switch, anomalías, acciones propuestas desde el plan); falta la publicación real, que necesita la app y el token de Meta | — |

## Fase 0 — Fundamentos

**Entregables**
- Solución por capas, SQL Server, EF Core y migración inicial.
- Identity, roles, organizaciones y filtro multiempresa.
- Layout Bootstrap con navegación (sólo secciones funcionales visibles).
- Cola de trabajos persistente, Worker, progreso por SignalR, cancelación.
- Auditoría, logging estructurado, manejo global de errores.
- Contratos de proveedores de IA y registro de uso (sin proveedor real todavía).
- `CREDENCIALES.md` de desarrollo y documentación.

**Puerta de salida**
- Un usuario inicia sesión, crea una organización y lanza un trabajo de prueba que muestra progreso en vivo, puede
  cancelarse, reintenta ante fallo y deja auditoría.
- Un usuario de otra organización no ve nada de lo anterior.
- Build sin advertencias y suite en verde.

**Verificación del 2026-11-11**

| Punto de la puerta | Evidencia |
|---|---|
| Build | `dotnet build -warnaserror`: 0 advertencias, 0 errores |
| Suite | `dotnet test`: 21 de 21 contra LocalDB (cola, reintentos, cancelación, arriendo vencido, dos Workers en paralelo, aislamiento, auditoría, organizaciones, arquitectura) |
| Sesión | En el navegador: credenciales incorrectas → aviso; correctas → entra y vuelve a la página pedida |
| Trabajo con progreso en vivo | Trabajo de diagnóstico: la fila pasó de 30 % a 100 % sin recargar, con Web y Worker como procesos separados |
| Cancelación | Trabajo de 40 pasos cancelado en el paso 4: quedó "Cancelado" en 17 % |
| Reintento ante fallo | Trabajo con un fallo simulado: volvió a la cola con el error y terminó bien en el intento 2 de 3 |
| Auditoría | Pantalla de auditoría con cada encolado, cancelación y alta de organización |
| Aislamiento | Organización nueva: lista de trabajos vacía y `404` al cancelar un trabajo de la otra; auditoría separada |
| Permisos | Usuario con rol Lector: sin formulario ni botones, y `403` al lanzar, cancelar o abrir Miembros, Auditoría y Organizaciones |
| Errores | AJAX: `400` validación, `404`, `409` con código estable; POST sin antiforgery: `400` |

**Diferencias con lo planificado**

- Los **contratos de proveedores de IA y el cache de resultados** pasan al inicio de la Fase 1: sin un primer uso real
  serían abstracciones sin caso. El **registro de uso de IA** sí quedó (entidad, servicio y test).
- **Organizaciones**: las crea el administrador de plataforma y los usuarios los da de alta el administrador de cada
  organización; no hay registro abierto.
- La **cola de renderización** y **Costos de IA** no aparecen en el menú: todavía no tienen servicio detrás.

**No verificado**

- Reconexión de SignalR ante caída de la Web y entrega de avisos sólo al grupo de la organización: revisado en el
  código (`JobsHub`), sin test automático.
- Un cambio de rol o una baja de miembro se refleja recién en el próximo inicio de sesión (todavía no hay pantalla
  para cambiar roles).
- Comportamiento con más de un Worker real en simultáneo: cubierto sólo por el test de toma en paralelo.

## Fase 1 — Biblioteca inteligente (primer gran hito)

**Entregables, en este orden** (cada paso es un flujo vertical):
1. Ingesta: carga individual y masiva, almacenamiento, hash, ffprobe, miniatura, procedencia y derechos.
2. Duplicados exactos y visuales.
3. Detección de escenas (PySceneDetect) y persistencia de segmentos visuales.
4. Timeline: ver, reproducir, corregir, fusionar, dividir.
5. Transcripción y texto en pantalla.
6. Análisis multimodal: documentación por fragmento y segmentos narrativos.
7. Embeddings y búsqueda semántica con filtros.
8. Selección de fragmentos para producción.

**Avance**

| Paso | Estado | Evidencia |
|---|---|---|
| 1a. Carga individual y masiva, almacenamiento inmutable por hash, duplicados exactos, procedencia y derechos, listado con filtros, detalle con reproductor | **Hecho y verificado, 2026-11-11** | 6 tests de biblioteca (27 en total, en verde). En el navegador: carga de 5 archivos en una tanda → 2 creados, 1 reconocido como duplicado aunque tenía otro nombre, 1 rechazado por formato, 1 rechazado por vacío; un solo archivo en disco por contenido; video reproducible con rangos (`206`); filtros conservados al paginar; cambio de derechos auditado |
| 1b. Propiedades técnicas (ffprobe) y miniaturas, como trabajo en segundo plano al ingresar | **Hecho y verificado, 2026-11-11** (FFmpeg 9.0.2 instalado con winget) | 4 tests con FFmpeg real sobre clips generados (31 en total, en verde): video H.264+AAC con duración, resolución, fps, códecs y miniatura; imagen sin duración; archivo ilegible → "Failed" con motivo y sin reintentos; reanálisis. En el navegador, con Web y Worker separados: los dos archivos ya cargados pasaron a "Ready" con miniatura y la lista se refrescó sola |
| 1c. Carpetas vigiladas: el administrador de la organización indica carpetas del servidor, dentro de las raíces permitidas por configuración; el Worker las revisa cada 5 minutos (y a pedido) e ingresa los archivos nuevos con los derechos de la carpeta, sin modificar ni mover nada | **Hecho y verificado, 2026-11-11** | 4 tests (93 en total, en verde) sobre carpetas reales: ingresa lo admitido incluidas subcarpetas, ignora otros formatos, registra el rechazado, no reingresa en la segunda revisión, ingresa sólo el archivo nuevo en la tercera, deja los archivos intactos; rechaza rutas fuera de la raíz, con `..` o inexistentes; el programador encola una revisión por carpeta activa y no la repite en el mismo intervalo; carpeta borrada → falla sin reintentos. En el navegador: una carpeta con 2 videos y un `.txt` → 2 ingresados y analizados con sus derechos; `C:\Windows` rechazada |
| 2. Duplicados visuales: huella de 64 bits por cuadro (cinco cuadros por video, uno por imagen), calculada con FFmpeg durante el análisis técnico; un archivo con huella casi igual a la de uno anterior queda marcado como su duplicado | **Hecho y verificado, 2026-11-11** | 9 tests (89 en total, en verde) con FFmpeg real: video recodificado a 240×426 con mucha compresión → duplicado del original; video distinto → no; imagen PNG convertida a JPEG de la mitad de tamaño → duplicado; imagen distinta → no; no se cruzan organizaciones; 6 casos de distancia entre huellas. En el navegador: una imagen reducida a la mitad y pasada a JPEG quedó "Duplicado visual de #1" en la lista y en el detalle |
| 3. Detección de escenas (PySceneDetect 0.7.3) y persistencia de segmentos visuales, con miniatura por escena | **Hecho y verificado, 2026-11-11** | 10 tests (41 en total, en verde) con PySceneDetect y FFmpeg reales: video con cortes a 2 s y 4 s → 3 escenas contiguas dentro de ±100 ms que cubren todo el video; video sin cortes → una escena; volver a detectar reemplaza sin duplicar; imágenes y videos sin analizar rechazados; otra organización recibe `404`; 6 casos de normalización de cortes. En el navegador: un video de 3 escenas subido desde la página quedó dividido en 3, con miniaturas |
| 4. Timeline: ver cortes, reproducir fragmento, mover un corte, dividir, fusionar, editar etiquetas | **Hecho y verificado, 2026-11-11**. "Consultar análisis" llega con el paso 6 | 7 tests (48 en total, en verde): mover, dividir y fusionar conservan fragmentos contiguos; límites de 100 ms; etiquetas sin repetidos; dos ediciones simultáneas no se aplican las dos; otra organización recibe `404`; con el pipeline real, una edición regenera la miniatura y redetectar exige confirmar. En el navegador: dividir en la posición del video, etiquetar, mover el corte, fusionar y redetectar con confirmación |
| 8. Selección de fragmentos para producción | **Hecho y verificado, 2026-11-11** (adelantado: comparte pantalla con el timeline) | Test y navegador: sin autorización de reutilización vigente → `409` y casilla deshabilitada; con autorización → seleccionado; autorización vencida → `409` |
| 5. Transcripción local (faster-whisper 1.2.1, CPU) con frases y tiempos; el texto de cada escena se deriva por tiempo | **Hecho y verificado, 2026-11-11**. **Falta: texto en pantalla (OCR)**, que se hará con el análisis multimodal | 7 tests (55 en total, en verde) con Whisper real (modelo `tiny`) sobre un video con voz sintetizada: frases ordenadas con tiempos, texto en su escena, uso registrado con costo 0 y duración; volver a transcribir reemplaza; sin audio no se transcribe; otra organización recibe `404`; 4 casos de asignación de frases a tramos. En el navegador, con el modelo `small`: transcripción exacta de la frase, en 3 frases con tiempos, visible en la escena; clic en una frase salta a ese momento |
| 6. Análisis multimodal por escena con Gemini (`gemini-3.5-flash-lite`, API Interactions): descripción visual, texto en pantalla, producto, acciones, enfoque, funciones narrativas, mensaje, público, calidad visual, etiquetas y confianza; versionado, con cache, estimación previa y tope por video | **Implementado; verificado sólo con proveedor falso, 2026-11-11. Falta la prueba contra Gemini real: no hay clave configurada** | 17 tests (72 en total, en verde). Flujo con proveedor falso: documenta cada escena, registra tokens y costo, el segundo análisis sale del cache sin llamar al proveedor, error transitorio se reintenta, error definitivo deja "Failed", escena editada queda "desactualizada", etiquetas manuales sobreviven. Cliente de Gemini contra un servidor HTTP falso con la forma documentada de la API: URL, encabezados, cuerpo, `store: false`, lectura de la respuesta, costo con razonamiento incluido, errores 429/503 reintentables y 400/403 no. En el navegador: estimación (3 escenas, 6 cuadros, USD 0,0035) y rechazo `AI_NOT_CONFIGURED` |
| 7. Buscador: por significado (embeddings `gemini-embedding-2`, 768 dimensiones, similitud en memoria), por palabras cuando no hay proveedor, y filtros por función, duración máxima, autorización y selección; resultado con miniatura, tiempos y enlace al video en la escena | **Implementado. Búsqueda por palabras y filtros verificados de punta a punta; la búsqueda por significado, sólo con proveedor falso: falta la clave** | 7 tests (79 en total, en verde): orden por similitud con embeddings falsos deterministas, filtros combinados ("hooks de menos de 4 s"), autorización y selección según el estado actual del video, modo palabras con todas las palabras exigidas, reindexado sólo de lo que cambió, consulta repetida sin volver a pagar, aislamiento entre organizaciones, contrato del cliente de embeddings. En el navegador (sin clave): búsqueda por palabras sobre lo que se dice, filtros, `400` con filtro fuera de rango, y el enlace abre el video posicionado en la escena |

No verificado en el paso 7: **la búsqueda por significado contra Gemini real** (la forma de la respuesta de
`batchEmbedContents` no figura en la documentación consultada: se asumió `embeddings[].values`), la pertinencia de los
resultados sobre la biblioteca real (el criterio de la puerta: 15 consultas de referencia con un resultado pertinente
entre los 5 primeros en el 80 %) y el rendimiento con miles de fragmentos.

**Costos de IA (transversal, COS-02): hecho y verificado, 2026-11-11.** Pantalla en Configuración, para el
administrador de la organización: costo, llamadas, tokens y cómputo local del período (7, 30, 90 o 365 días), desglose
por tarea y modelo, últimas 50 llamadas con su trabajo, y gasto del mes contra un presupuesto mensual configurable
(`Ai:MonthlyBudgetUsd`, USD 20 por defecto) con avisos al 50 %, 80 % y 100 %. Evidencia: 1 test (94 en total, en
verde) con sumas por grupo, período, mes en curso y aislamiento entre organizaciones; en el navegador muestra la
transcripción real registrada (34,3 s de cómputo local, costo 0). **Límites:** el presupuesto mensual sólo avisa, no
bloquea (el tope que sí bloquea es el de cada video); es un único valor para todas las organizaciones; y todavía no
existe el indicador "costo por creatividad utilizable", porque no hay creatividades hasta la Fase 2.

**Interpretación de la consulta (hecho y verificado, 2026-11-11).** "hooks de menos de 4 segundos que muestren uñas
terminadas" se convierte en función = Hook, duración ≤ 4 s y texto = "muestren uñas terminadas"
(`SearchQueryParser`, determinista, sin IA ni costo). Reconoce la función sólo si es la primera palabra de la
consulta, la duración máxima ("menos de", "hasta", "como máximo"…), "autorizados" y "para producción"; la pantalla
muestra qué entendió y lo elegido en el formulario manda sobre lo deducido. Evidencia: 15 casos de test y prueba en
el navegador. **Límites:** son giros concretos del español; lo que no reconoce queda como texto. No deduce producto,
emoción ni calidad.

**Filtros nuevos del buscador (hecho y verificado, 2026-11-11):** por calidad de audio (traducido a SQL con los
mismos umbrales que la clasificación; un test los compara en los bordes) y por tipo de fragmento (escena o tramo
narrativo).

**Análisis en lote y automático (hecho, 2026-11-11; verificado con tests y, sin clave, en el navegador).** La
biblioteca muestra cuántos videos con escenas siguen sin analizar, cuántos entran en una tanda y cuánto costaría;
nada se gasta sin confirmar. Topes: por video (`Ai:MaxCostPerVideoUsd`), por tanda (`Ai:MaxCostPerBatchUsd`, 5 USD)
y lo que queda del presupuesto mensual, que acá sí bloquea. El administrador de la organización puede activar el
**análisis automático de videos nuevos**: `AutoAnalysisScheduler` pide una tanda cada `Ai:AutoAnalyzeIntervalMinutes`
con los mismos topes. Está apagado por defecto. Evidencia: tests de tope por video, falta de clave, presupuesto casi
agotado (entra uno, queda uno) y modo automático (sólo la organización que lo activó, sin repetir). En el navegador,
sin clave: "5 videos sin analizar, 5 no se pueden analizar: falta la clave". **No verificado:** una tanda real contra
Gemini. Límite: un video cuyo análisis falló no vuelve a entrar solo en una tanda; se pide a mano.

No verificado en el paso 6: **nada contra el servicio real**. La forma del pedido y de la respuesta sale de la
documentación oficial consultada el 2026-11-11; puede haber diferencias que sólo aparecen al llamar de verdad. Tampoco
la calidad de las clasificaciones (es la otra mitad de la puerta de la fase: 80 % de funciones narrativas correctas
sobre videos anotados) ni la exactitud de la estimación de costo.

**Tramos narrativos (LIB-09; hecho, 2026-11-11; verificado con tests, no contra Gemini ni en el navegador).** El
mismo pedido de análisis devuelve además los tramos del anuncio: una o más escenas contiguas que cumplen una función
(un hook de tres planos es un solo tramo). Se guardan como fragmentos de tipo `Narrative` con su propio análisis, se
reemplazan en cada reanálisis, entran al buscador y se reproducen desde el detalle del video. No cuesta una llamada
más: va en la misma respuesta (el prompt pasó a `scene-analysis-v2`, lo que invalida el cache del v1, que no tenía
ninguna entrada real). Evidencia: tests de persistencia, descarte de tramos superpuestos o fuera de rango, reemplazo
al reanalizar, búsqueda y lectura de la respuesta del proveedor. **Límites:** si se editan los cortes, los tramos
quedan con sus tiempos anteriores hasta reanalizar; un duplicado que hereda el análisis no hereda los tramos; no se
pueden crear ni corregir a mano.

**Calidad de audio (LIB-10): hecho y verificado, 2026-11-11.** No usa IA: FFmpeg mide el volumen medio y el pico de
cada escena (filtro `volumedetect`) al detectarla y al editarla, y de ahí sale una clasificación: sin sonido (medio
bajo −50 dB), bajo (medio bajo −30 dB), saturado (pico desde −0,5 dB) o correcto. Evidencia: 17 tests (116 en total,
en verde) con FFmpeg real sobre tonos de volumen conocido (normal, bajo, casi mudo, saturado y silencio digital), el
pipeline completo (video con audio → niveles por escena; sin pista de audio → sin dato; dividir una escena → se
vuelven a medir) y 11 casos de clasificación en los bordes. En el navegador: el video con voz quedó "Audio correcto"
(−16,2 dB medio, −3,4 dB de pico). **Límites:** mide nivel, no contenido: no distingue voz de música ni detecta ruido
de fondo o eco; los umbrales son los que elegí, sin validar con material real; las escenas detectadas antes de este
cambio quedan sin dato hasta volver a detectarlas o editarlas.

No verificado en el paso 5: **audio en español** (la única voz que trae FFmpeg es en inglés), voz real con música
de fondo o ruido, varios hablantes, y el tiempo de transcripción de videos largos en CPU (el clip de 5 s tardó
pocos segundos; el tope es de 30 minutos por video).

**Arrastrar los cortes (hecho y verificado en el navegador, 2026-11-11):** cada corte de la franja se arrastra; el
video muestra el cuadro de la posición mientras se mueve y al soltar se guarda con la misma operación que el campo de
tiempo. Se probó con eventos de puntero generados por script, no con un mouse físico ni en pantalla táctil.

No verificado en el paso 4: el uso con muchas escenas (decenas) y la edición desde dos navegadores a la vez (cubierta
sólo por test).
Pendiente conocido: si a un video se le quita la autorización, sus fragmentos ya elegidos siguen marcados; la
validación de recetas de la Fase 2 debe volver a comprobar los derechos al usarlos.

No verificado en el paso 3: videos reales (los cortes de prueba son cambios bruscos de color; falta medir precisión y
cobertura sobre material anotado, que es la puerta de la fase), transiciones suaves (fundidos), videos largos frente al
tope de 15 minutos de detección, y el umbral por defecto (27) sobre contenido UGC.

No verificado en 1c: la revisión periódica esperando los 5 minutos reales (se probó el encolado del programador,
no el reloj), carpetas de red o con miles de archivos, y un archivo que se está copiando mientras se revisa (la
espera de 10 segundos y el salto de archivos bloqueados están escritos, sin test). Límite conocido: si un archivo se
borra de la carpeta, su registro y su copia en la biblioteca quedan; no hay sincronización de bajas.

No verificado en el paso 2: recortes, videos con subtítulos o marcos agregados, y versiones con otro ritmo o
duración (la comparación exige duración parecida, ±3 %); tampoco el umbral (10 % de bits distintos) sobre material
real, donde puede dar falsos positivos entre tomas muy parecidas.

**Herencia del análisis (hecho, 2026-11-11; verificado sólo con tests).** Pedir el análisis de un duplicado visual
cuyo original ya está analizado copia ese análisis, sin llamar al proveedor ni necesitar la clave: la estimación lo
avisa con costo 0. Cada escena de la copia hereda de la escena del original con la que más se superpone en posición
relativa (al menos la mitad de su tramo); las que no corresponden quedan sin análisis. El análisis heredado queda
marcado como tal y guarda de qué análisis salió. Evidencia: 6 tests (100 en total, en verde): con el video de cortes
y su copia recodificada, la copia hereda las 3 escenas con el proveedor desconfigurado y sin llamadas ni costo
nuevos; las etiquetas del original llegan como automáticas sin duplicar las manuales; el heredado entra al
buscador; 5 casos de emparejamiento de tramos. **No verificado en el navegador**: hace falta un original analizado,
y sin clave no hay ninguno en la base de desarrollo. Límites: sólo hereda al pedirlo (no se propaga solo si el
original se analiza después); si el original se reanaliza, la copia no se actualiza; un falso positivo de duplicado
heredaría un análisis ajeno; y la transcripción no se hereda, porque es local y gratis.

No verificado en 1b: videos reales de la biblioteca (sólo clips sintéticos), videos con rotación de teléfono (la
lógica está escrita pero no hay clip de prueba), archivos largos frente al tope de 120 s por invocación de FFmpeg.

No verificado en 1a: carga de archivos grandes (cientos de MB; el límite es 2 GB por archivo) y la carga con un
usuario de rol Lector (la política es la misma que ya se probó en Trabajos).

**Anticipado desde esta fase:** las tablas de trazabilidad (asset → segmento) se diseñan pensando en el linaje completo.

**Puerta de salida (criterio de aceptación)**
- Se procesa una **muestra real** (≥ 30 videos de la biblioteca de ~500) de punta a punta sin intervención.
- Sobre un subconjunto anotado a mano (≥ 10 videos): cortes visuales con precisión y cobertura ≥ 90 %; función
  narrativa correcta en ≥ 80 % de los segmentos (umbrales iniciales, a revisar con el usuario).
- Un conjunto de ≥ 15 consultas de referencia (p. ej. "hooks de menos de 4 segundos que muestren uñas terminadas y
  transmitan practicidad") devuelve al menos un fragmento pertinente entre los 5 primeros en ≥ 80 % de los casos.
- Costo de análisis por video medido y registrado.
- **No se inicia la generación masiva hasta cumplir esta puerta.**

**Decisión del usuario (2026-11-11):** construir la Fase 2 antes de medir esta puerta y configurar la clave de IA al
final ("quiero armar todo y luego poner la key"). La puerta **sigue sin medirse**: nada del análisis con IA ni de la
búsqueda por significado se probó contra el servicio real, y no hay videos reales anotados. La regla de arriba se
mantiene en su sentido práctico: la Fase 2 se construye y se prueba con material de prueba, pero no se produce en
masa con la biblioteca real hasta medir esta puerta. Suite al tomar la decisión: 134 tests en verde.

## Fase 2 — Motor de composición (segundo hito)

**Entregables**
1. Taxonomía creativa y asignación de funciones a fragmentos.
2. Receta estructurada, validación y editor de composiciones.
3. Render FFmpeg de una receta (corte, normalización, concatenación, subtítulos, overlays, música).
4. Control de calidad automático del archivo.
5. Generador de variantes con control de similitud.
6. Lotes de 5 a 50 con progreso, errores, costo y cola de render.

**Avance**

**Paso 1 — Receta, render, control de calidad y linaje: hecho y verificado, 2026-11-11.** Menú *Composición*.
- **Receta** (`CreativeRecipe` + `RecipeItem`): nombre, concepto, hipótesis, formato de salida (9:16, 4:5, 1:1 o
  16:9) y de 1 a 30 fragmentos en orden, cada uno con su función opcional. El editor ofrece los fragmentos elegidos
  para producción de videos con autorización vigente; se agregan, se ordenan y se quitan. La receta **copia el video
  y el tramo** de cada fragmento y **no se modifica** después: editar o volver a detectar las escenas no la cambia.
- **Render** (trabajo `creative.render`, un solo comando de FFmpeg): corta cada tramo, lo escala hasta cubrir el
  cuadro y recorta el sobrante, lo lleva a 30 cuadros por segundo y audio estéreo de 48 kHz (silencio si el original
  no tiene audio) y une todo en un MP4 H.264 + AAC. Los originales no se tocan.
- **Derechos (REM-06)**: se comprueban al crear la receta, al pedir el render y cuando el render corre.
- **Control de calidad (REM-10, parcial)**: duración contra la suma de los tramos, resolución y presencia de audio;
  queda guardado por video (`QualityCheck`).
- **Linaje (REM-07)** y **reproducibilidad (REM-09)**: cada video generado (`CreativeVariant`) apunta a su receta y
  la receta a cada original con su tramo; renderizar otra vez da una variante nueva con el mismo montaje.

Evidencia: 9 tests nuevos (145 en total, en verde) con FFmpeg real: receta que mezcla un original sin audio con uno con audio
→ MP4 1080×1080 a 30 cps con H.264 y AAC, duración correcta y los tres controles aprobados; segundo render con la
misma duración y resolución después de editar y redetectar las escenas; receta rechazada por falta de autorización;
render rechazado por autorización vencida; render ya encolado que falla sin reintentos al retirarse la autorización;
validaciones; aislamiento entre organizaciones; 6 casos del control de calidad. En el navegador: receta de 3 tramos
de dos videos (uno WebM) armada en el editor → render 1080×1920 de 5,8 s que pasa el control y se reproduce.

**Límites de este paso:** sin subtítulos, overlays, música ni transiciones (los cortes son directos); el recorte
para cubrir el cuadro es siempre centrado; el control de calidad no detecta cuadros negros ni audio mudo; no se
puede recortar un fragmento dentro de la receta (se usa entero); no hay "duplicar receta" para partir de una
existente; un render no se puede borrar. La duración máxima (180 s) no tiene test. Se probó con clips sintéticos
de pocos segundos: falta un render largo con material real y medir cuánto tarda.

**Paso 2 — Variantes con control de similitud y lotes: hecho y verificado, 2026-11-11.** *Composición → Lotes*.
- **Generador de variantes (REM-03, REM-04)** (`VariantGenerator`, función pura, sin IA ni costo): de un conjunto de
  fragmentos con su función arma montajes con un hook al principio (si hay hooks), de uno a cuatro fragmentos de
  desarrollo y un CTA, cierre u oferta al final (si hay). Varía el hook, el desarrollo, el cierre, el orden y la
  duración. Descarta todo candidato cuya **similitud** con uno ya aceptado llegue al umbral: 70 % material
  compartido (por duración) + 30 % posiciones iguales. Con la misma semilla da siempre lo mismo.
- **Lote (REM-08)** (`CreativeBatch`): nombre, brief, formato, de 5 a 50 variantes, duración máxima, similitud máxima
  y semilla. La vista previa muestra qué variantes saldrían antes de crear nada; "probar otra combinación" cambia la
  semilla. Crear el lote genera una receta por variante y encola sus renders. Si los fragmentos no dan para lo
  pedido se crean las que hay (mínimo 5; con menos, se rechaza). El detalle muestra avance, videos, control de
  calidad, errores y el tiempo de render (el costo es cómputo local: no hay IA en este paso).

Evidencia: 5 tests nuevos (150 en total, en verde): similitud en casos conocidos; 20 variantes de 8 fragmentos con
estructura, duración máxima y **ningún par por encima del umbral**, reproducibles por semilla; conjuntos chicos que
dan menos variantes; lote de 5 con render real (5 videos 1080×1080 que pasan el control, lo creado coincide con la
vista previa); rechazo por falta de autorización y por falta de variedad; aislamiento. En el navegador: lote de 5
variantes desde 3 fragmentos, las 5 renderizadas y aprobadas (5,8 s de render en total).

**Límites de este paso:** el generador combina, no entiende: la coherencia narrativa depende de que las funciones
de los fragmentos estén bien asignadas (a mano o por el análisis con IA, todavía sin probar), y la revisión humana
que pide la puerta no está hecha. La similitud mide fragmentos y orden, no parecido visual. No se puede cancelar un
lote entero ni reintentar sólo los fallidos (se vuelve a renderizar cada receta desde su detalle). El lote más
grande probado es de 5 con clips sintéticos: **falta un lote de 10 o más con material real**, que es la puerta.
No hay brief estructurado ni conceptos (Fase 3) ni presupuesto por lote (no hay costo que limitar todavía).

**Paso 3 — Terminación: subtítulos, textos, música, transiciones y cuadros negros: hecho y verificado, 2026-11-11.**
Se elige al armar una receta o un lote (en el lote vale para todas sus variantes).
- **Subtítulos**: se queman en el video con lo que se dice en cada tramo, tomado de la transcripción del original y
  llevado a los tiempos del montaje (misma regla que la biblioteca: entra la frase cuyo punto medio cae en el tramo).
- **Textos sobre el video** (hasta 5): arriba, centro o abajo; durante todo el video, en los primeros 3 s o en los
  últimos 3 s. El momento es relativo para que sirva igual en variantes de distinta duración.
- **Música** (*Composición → Música*): biblioteca de pistas (`MusicTrack`: mp3, m4a, aac, wav, ogg; hasta 100 MB) con
  sus propios derechos. Un trabajo (`music.probe`) comprueba que el archivo tenga audio y guarda la duración. Sólo
  una pista analizada y con autorización vigente entra en una receta, y se vuelve a comprobar al renderizar. Se
  mezcla al volumen elegido, se repite si es más corta que el video y se apaga en el último segundo.
- **Transición**: corte directo o fundido a negro (200 ms al final de cada tramo y al inicio del siguiente); no
  cambia la duración del montaje.
- **Control de calidad (REM-10)**: se suman *cuadros negros* (falla con un tramo en negro de 500 ms o más) y
  *subtítulos* (si la receta los pidió y ningún tramo tenía voz transcripta, el video sale pero no pasa el control).

Evidencia: 13 tests nuevos (163 en total, en verde) con FFmpeg y Whisper reales: tiempos de los subtítulos, momentos
de los textos, formato del archivo de textos (y que un texto de usuario no pueda inyectar órdenes de formato), reglas
del control; pista de música analizada, duplicada, de formato no admitido e ilegible; receta rechazada por música sin
autorización y render rechazado si la pierde después; **el texto quemado cambia la imagen** (se compara el mismo
instante con y sin texto), **la música se oye** en un montaje que sin ella es silencio, el fundido no cambia la
duración; subtítulos quemados desde la transcripción de un video con voz; detección de 1,2 s de negro; lote con la
misma terminación en sus 5 recetas. En el navegador: pista cargada y analizada, receta con fundido, música al 40 % y
dos textos → video 1080×1920 que pasa los cuatro controles; revisé un cuadro del archivo y los textos están en su
lugar, con acentos.

**Límites de este paso:** los subtítulos son tan buenos como la transcripción (Whisper, sin probar en español) y no
se pueden corregir a mano; un solo estilo de texto (Arial blanca con borde, sin elegir tipografía, color ni tamaño);
la fuente depende de las que tenga instaladas el servidor; los textos de apertura y cierre duran siempre 3 s; el
fundido es a negro, sin fundido cruzado; la música no baja sola cuando alguien habla; no se puede oír una pista
desde la pantalla de música ni cambiarle la vigencia después de cargarla (sólo autorizar o desautorizar); el control
no verifica que los subtítulos se lean ni detecta audio mudo. El cuadro negro se probó con negro puro.

**Paso 4 — Editor: recorte, duplicado, vista previa y control de lotes: hecho y verificado, 2026-11-11.**
- **Recorte**: en el editor cada fragmento se puede usar desde y hasta donde se quiera, siempre dentro de sus
  límites y con un mínimo de 200 ms. El servidor valida el rango.
- **Duplicar y cambiar**: como una receta no se modifica, cambiarla es abrir el editor con sus datos (fragmentos,
  recortes, funciones, formato y terminación) y guardar otra. Si las escenas se editaron, cada tramo se ajusta a lo
  que hoy abarca su fragmento; si se volvieron a detectar, se avisa cuántos tramos no se pudieron traer.
- **Vista previa**: el editor reproduce los tramos en orden directamente de los originales, con el recorte del
  formato y los textos encima, sin generar ningún archivo. No muestra subtítulos, música ni fundidos (se avisa).
- **Lote**: *cancelar lo que falta* (cancela los renders en cola, pide parar a los que corren y deja sus videos
  como cancelados) y *reintentar los fallidos* (un video nuevo para cada variante que falló o se canceló).

Evidencia: 3 tests nuevos (166 en total, en verde): receta con dos fragmentos recortados que se renderiza con la
duración esperada y cuatro recortes inválidos rechazados; borrador idéntico a la receta, guardado como otra, ajustado
tras mover un corte y vacío (con aviso) tras redetectar; lote de 5 cancelado antes de renderizar (5 trabajos
cancelados, nada se renderiza después) y reintentado hasta 5 videos aprobados; aislamiento. En el navegador: receta
duplicada con su terminación, recorte con el valor fuera de rango corregido solo, vista previa que recorre los tres
tramos y muestra el texto de cierre en los últimos 3 s, y un lote cancelado y reintentado.

**Hallado al probar en el navegador y corregido:** al cancelar un lote con un render en curso, ese render terminó
igual y el video quedó listo pero con el mensaje de cancelado. Ahora un video que termina nunca conserva un error
anterior. Esa carrera no tiene test automático (depende del instante); el aviso "N cancelados" puede contar un video
que termina de todos modos.

**Límites de este paso:** el recorte se escribe en segundos, no se arrastra sobre una línea de tiempo; la vista
previa no es el render (el salto entre tramos tiene una pausa de carga y no refleja el fundido); no se puede
duplicar un lote; un lote cancelado no distingue en el listado "cancelado" de "fallido".

### Cómo medir la puerta de la Fase 2

La construcción de la fase está completa; la puerta **no está medida** y no se puede medir con el material de prueba.
Hace falta:
1. Videos reales de la biblioteca, con autorización de reutilización, con sus escenas detectadas y revisadas.
2. Asignar la función de cada fragmento (a mano, o con el análisis de IA cuando esté la clave).
3. Crear un lote de 10 o más variantes y esperar a que termine: todas deben pasar el control de calidad.
4. Que una persona mire los videos y diga si la estructura narrativa es coherente.
5. Volver a renderizar una receta del lote y comprobar que el montaje es el mismo.
Lo que ya está cubierto por tests: ningún par de variantes por encima del umbral de similitud, re-render con el mismo
montaje y linaje de cada video hasta sus originales.

**Puerta de salida**
- Desde una selección de escenas se genera un lote de ≥ 10 videos técnicamente correctos (pasan el control de
  calidad), con estructura narrativa coherente (revisión humana) y sin pares por encima del umbral de similitud.
- Re-renderizar una receta almacenada produce el mismo montaje.
- Cada video muestra todos los fragmentos que lo componen.

## Fase 3 — Investigación y estrategia (tercer hito)

**Entregables**
1. Perfiles de marca.
2. Briefs de campaña e interpretación de texto libre.
3. Fuentes externas autorizadas, referencias con procedencia y permisos.
4. Agente investigador.
5. Motor estratégico y agente de planificación: conceptos → recetas.
6. Generación audiovisual opcional (desactivada por defecto).

**Avance**

**Paso 1 — Perfil de marca, brief de campaña y mensajes prohibidos: hecho y verificado, 2026-11-11.** Menú
*Estrategia*.
- **Perfil de marca (STR-01)**: nombre, propuesta de valor, estilo de comunicación, identidad visual, objetivos,
  restricciones comerciales y presupuesto mensual, más seis listas: productos (con margen), beneficios verificables
  (con cómo se verifican), públicos, objeciones (con cómo se responden) y mensajes permitidos y prohibidos.
- **Brief de campaña (STR-02)**: sus 12 campos (producto, ocasión, público, objetivo, mensaje, enfoques, duración,
  cantidad, plataforma, investigación externa, presupuesto máximo de generación y nivel de autonomía), para una
  marca, conservando el texto original.
- **Interpretación del texto libre** (`BriefTextParser`, determinista, sin IA ni costo): del texto deduce sólo lo
  explícito (cantidad de videos, duración, plataforma, ocasión entre 16 conocidas, y si pide investigación) y
  completa los campos vacíos sin pisar lo ya escrito.
- **Mensajes prohibidos (STR-09)**: un brief cuyo texto, mensaje, enfoques, objetivo o nombre contiene una frase
  que la marca prohíbe no se guarda; la comparación ignora mayúsculas, acentos y espacios.

Evidencia: 13 tests nuevos (179 en total, en verde): 4 casos de mensajes prohibidos, 7 de interpretación (incluido el
brief de Navidad de la directiva), perfil de marca (nombres y elementos sin repetir, margen sólo en productos,
aislamiento) y brief (producto de otra marca rechazado, mensaje prohibido rechazado en tres campos, producto en uso
que no se puede quitar, aislamiento). En el navegador: marca con su perfil y sus listas, brief completado desde el
texto ("Entendí: 20 videos · de 15 segundos · plataforma: Reels · ocasión: Navidad · con investigación externa"),
rechazo por "dura para siempre" sin perder lo escrito, y guardado.

**Hallado al probar en el navegador y corregido:** el servidor corría con la cultura del sistema (español, coma
decimal) y los campos numéricos de un navegador envían punto: un margen "42.5" se rechazaba y un presupuesto
"1500.50" se habría leído como 150050. La aplicación ahora fija la cultura invariante al arrancar (`Program.cs`):
los números se leen y se muestran con punto en cualquier servidor. No tiene test automático (los tests no arrancan
la Web); quedó verificado en el navegador.

**Límites de este paso:** la interpretación no entiende el público, el mensaje ni los enfoques: eso lo hará el
motor estratégico leyendo el texto original con IA. Los mensajes prohibidos se comparan como frases literales: una
reformulación ("no se sale nunca") no se detecta. Los mensajes *permitidos* se guardan pero todavía nada los usa.
El nivel de autonomía y el presupuesto de generación se guardan pero todavía no gobiernan nada (no hay motor que
gobernar). Un brief o una marca no se pueden borrar. Las listas de la marca no se editan: se quita y se vuelve a
agregar. La validación de mensajes todavía no se aplica a los textos sobre el video de una receta.

**Paso 2 — Motor estratégico: del brief a un plan de conceptos y a sus recetas. Hecho el 2026-11-11; verificado con
tests y proveedor falso, NO contra Gemini.** En el detalle de un brief, *Plan creativo*.
- **Plan (STR-05, STR-07)**: se estima el costo, se confirma y un trabajo (`strategy.plan`) le envía al planificador
  el perfil de la marca, el brief y los fragmentos disponibles (los elegidos para producción de videos autorizados,
  con lo que su análisis dice de cada uno; hasta 120). Devuelve tantos conceptos como videos pide el brief (10 si
  no dice). Topes: `Ai:MaxCostPerPlanUsd` (0,50 USD) y el presupuesto de generación del brief si es menor.
- **Concepto (STR-06)**: hipótesis, enfoque, hook, guion, desarrollo, CTA, público, material requerido, material
  existente (fragmentos en orden), material faltante, motivo, riesgos y novedad de 1 a 5.
- **Filtros al guardar**: se descartan los conceptos que repiten el gancho de otro, los números de fragmento que no
  existen y los que exceden lo pedido; un concepto que usa un **mensaje prohibido** de la marca queda marcado y no
  se puede convertir en receta (STR-09).
- **De concepto a receta**: un concepto *realizable* (tiene fragmentos, todos existen y no usa nada prohibido) se
  convierte en una receta del motor de composición, con su título, su hook y su hipótesis, en el formato de la
  plataforma del brief (Feed → 4:5; el resto → 9:16). Uno a uno o todos juntos.
- **Autonomía del brief**: *sólo propone* no arma nada; *asistido* arma las recetas; *automático* las arma y las
  renderiza. Nunca publica.
- **Cliente de Gemini**: el protocolo de la API Interactions pasó a `GeminiInteractions`, compartido por el análisis
  de escenas y el plan: cuando esté la clave, un ajuste de la forma de la API se hace en un solo lugar. Los 11 tests
  del cliente de análisis siguen pasando sin cambios de expectativa. La forma de la API es la consultada en la
  documentación oficial el 2026-11-11 para el análisis; no se volvió a consultar.

Evidencia: 6 tests nuevos (185 en total, en verde): filtros de las propuestas; texto del pedido; cliente de Gemini
con servidor simulado (pedido de sólo texto, esquema, lectura de conceptos, costo); plan estimado, pedido, generado
y convertido en 4 recetas 1080×1350, con su costo registrado, rechazos por falta de clave, por tope, por presupuesto
del brief y por plan en curso, y aislamiento; **autonomía automática: 2 conceptos realizables renderizados solos en
1080×1920, uno con mensaje prohibido y uno sin material que quedan sin receta**; reintento ante un error transitorio y
plan fallido ante uno definitivo. En el navegador, sin clave: la estimación ("20 conceptos · 3 fragmentos · USD
0.0266 · falta la clave") y el pedido bloqueado.

**No verificado:** nada contra Gemini: ni la forma del pedido y la respuesta, ni la calidad, la variedad o la
coherencia de los conceptos (que es la puerta de la fase), ni la exactitud de la estimación. **La pantalla del plan
con sus conceptos no se vio en el navegador** (sin clave no hay ningún plan en desarrollo); sólo compila.

**Límites de este paso:** el plan no usa todavía creatividades históricas, referencias externas ni resultados
publicitarios (no existen aún). "Conceptos distintos" se controla sólo por el gancho repetido; la variedad real
depende del modelo. El modelo de planificación es por defecto el mismo modelo económico del análisis (se cambia en
`Ai:Gemini:PlanningModel`); conviene elegir uno más capaz con la clave en la mano. Los fragmentos sin analizar se le
describen sólo con el nombre del archivo, así que sin análisis previo el plan elige material a ciegas. La receta de
un concepto usa los fragmentos enteros y sin terminación (subtítulos, textos, música): se agregan duplicándola. Un
concepto no se edita ni se descarta. Un mensaje prohibido reformulado no se detecta.

**Paso 3 — Fuentes autorizadas, referencias y agente investigador. Hecho el 2026-11-11; el registro de fuentes y
referencias verificado en el navegador, el agente sólo con proveedor falso.** *Estrategia → Investigación* y la
tarjeta *Investigación externa* de cada brief.
- **Fuentes autorizadas (STR-03)**: un sitio se autoriza por su dominio, con sus condiciones de uso y el permiso que
  reciben sus referencias (*sólo referencia* o *utilizable*). Sólo el administrador de la organización las autoriza.
- **Referencias (STR-04)**: de cada una se guarda la dirección, un título, qué aporta (anuncio, tendencia, concepto,
  hook, estructura, estilo, formato o estacionalidad) y un resumen en palabras propias. **Nunca el material**: no se
  descarga ni se reproduce. Sólo se guarda si su dirección es del dominio (o de un subdominio) de una fuente
  autorizada y activa. Lleva su procedencia (cargada a mano o encontrada por el agente, y en qué corrida) y su
  permiso; sólo se puede marcar *utilizable* si la fuente lo permite.
- **Agente investigador (STR-03)** (trabajo `strategy.research`): para un brief que pide investigación externa, un
  modelo con búsqueda web busca en los sitios autorizados y propone referencias. **El servidor descarta todo lo que
  no venga de una fuente autorizada y activa**, lo ya guardado y lo repetido. Se estima y se confirma antes: cuesta
  tokens más cada consulta de búsqueda (14 USD cada 1000). Tope: `Ai:MaxCostPerResearchUsd` (0,30 USD) o el
  presupuesto del brief si es menor.
- **Uso en el plan (STR-05)**: si el brief pide investigación, sus referencias se le pasan al planificador como
  inspiración, cada una con su permiso, con la orden de no copiar ni citar (la consigna pasó a `concept-plan-v2`).
- **Documentación del proveedor consultada el 2026-11-11** antes de escribir el cliente: la búsqueda se activa con
  `"tools": [{"type": "google_search"}]`, las consultas vienen en los pasos `google_search_call` y se puede combinar
  con salida JSON estructurada en los modelos de las series 3 y 4 (en vista previa).

Evidencia: 23 tests nuevos (208 en total, en verde): 7 casos de normalización de dominio y 10 de pertenencia a un
dominio (incluidos los engaños `ejemplo.com.atacante.net`, `miejemplo.com` y `javascript:`); filtro de lo que propone
el agente; cliente de Gemini con servidor simulado (pide la herramienta de búsqueda, cuenta 3 consultas y las cobra);
el plan no lleva `tools`; fuentes y referencias (repetidas, no autorizadas, permiso, fuente desactivada,
aislamiento); corrida completa (4 propuestas, 2 guardadas, las de la fuente desactivada y del sitio ajeno
descartadas, costo registrado, segunda corrida que no duplica, plan que recibe las referencias, corrida fallida) y
brief que no pide investigación. En el navegador: fuente autorizada, referencia guardada y asociada al brief, rechazo
de un sitio no autorizado y del intento de marcarla utilizable, y la estimación ("1 fuente · hasta 15 referencias ·
USD 0.1223, búsquedas incluidas · falta la clave").

**No verificado:** nada contra Gemini. En particular, si el modelo respeta la lista de sitios (por eso el servidor
filtra igual), si las direcciones que devuelve existen, y la exactitud del costo (la estimación supone 8 consultas y
no descuenta las 5000 gratis del mes).

**Pendiente antes de usarlo en producción:** los términos del proveedor para la búsqueda web piden mostrar las
"sugerencias de búsqueda" que devuelve; hoy no se guardan ni se muestran. Hay que leer esos términos y cumplirlos.

**Límites de este paso:** el modelo busca en la web abierta; "sólo en los sitios autorizados" es una instrucción más
un filtro posterior, no una restricción técnica de la búsqueda, así que una corrida puede gastar y no guardar nada.
No hay conexión con la Biblioteca de Anuncios de Meta ni con ninguna API de una fuente: es búsqueda general. El
resumen lo escribe el modelo: puede ser impreciso. *Utilizable* hoy sólo cambia cómo se le describe la referencia al
planificador; no habilita a traer su material a la biblioteca (eso se hace cargando el archivo con sus derechos).
El requisito "una referencia no utilizable no entra en una receta" se cumple por construcción: una referencia nunca
es material, y una receta sólo admite fragmentos de la biblioteca con autorización.

**No construido: generación audiovisual opcional (STR-08).** Queda sin hacer a propósito. Generar escenas nuevas
exige elegir un proveedor de generación de video, con su costo y sus condiciones sobre personas, voces y marcas; sin
esa decisión sólo se podría construir un interruptor apagado que no gobierna nada, y el proyecto no admite pantallas
ni piezas sin función. Cuando se elija el proveedor: contrato propio, desactivada por defecto por organización,
política de restricciones con sus tests, y el material generado entra a la biblioteca con su procedencia.

**Puerta de salida**
- A partir de un brief estacional se obtiene un plan de 20 conceptos distintos, cada uno con hipótesis y receta
  realizable con material autorizado, o con los recursos faltantes identificados.
- El plan se renderiza con el motor de la Fase 2 sin edición manual.

## Fase 4 — Inteligencia de Meta

**Anticipado:** una importación básica de sólo lectura (cuentas, campañas, anuncios, creatividades) puede adelantarse
durante las fases 1–3 si hace falta para validar las relaciones del modelo de datos. No incluye análisis.

**Entregables**
1. Conexión, importación histórica y sincronización incremental.
2. Insights diarios por anuncio.
3. Asociación anuncio ↔ variante (automática para lo publicado por la plataforma, asistida para históricos).
4. Pantalla de linaje y dashboard.
5. Análisis por componente con control de factores y niveles de evidencia.
6. Experimentos.

**Avance**

**Paso 1 — Importación de métricas por archivo, vínculo anuncio ↔ video y linaje: hecho y verificado, 2026-11-11.**
Menú *Rendimiento*.
- **Importación (MET-02 y MET-03, por archivo)**: se carga un informe de Ads Manager en CSV, por anuncio y por día.
  Un trabajo (`meta.import-csv`) crea la cuenta, la campaña, el conjunto y el anuncio que falten y guarda una fila
  por anuncio y día (`MetaInsightDaily`). Reconoce las columnas por nombre, en español o en inglés; detecta el
  separador; entiende punto o coma decimal; descarta la fila de totales y las que no se entienden, y lo informa.
  **Reimportar un día reemplaza sus valores**: un informe más nuevo corrige al anterior, no lo suma.
- **Métricas**: se guardan sólo cantidades sumables (gasto, impresiones, clics, reproducciones de 3 s, ThruPlays,
  compras y su valor). CTR, CPC, CPM, CPA, ROAS y las tasas de video se calculan al consultar, sobre las sumas del
  período que se mire: una tasa no se promedia. Sin denominador, la tasa queda sin dato.
- **Vínculo y linaje (MET-05)**: un anuncio se vincula con el video generado que se publicó en él. Desde ahí se ve
  la receta, cada fragmento con su original y su tramo, y, si la creatividad nació de un brief, el concepto, el
  plan, el brief y la marca; si salió de un lote, el lote.
- **Honestidad (MET-08, en lo que hay)**: la pantalla dice que son observaciones, no causas, y muestra los días con
  datos. Nada declara ganadores ni ordena por "mejor".

Evidencia: 18 tests nuevos (226 en total, en verde): 13 casos de lectura de números; informe en español con
comillas, miles con punto, fila de totales y fecha inválida; informe en inglés con punto y coma; columnas
obligatorias faltantes; tasas y sus casos sin denominador; importación, **reimportación que reemplaza sin duplicar**,
consulta por período, archivo roto que queda fallido y aislamiento; **linaje completo marca → brief → plan →
concepto → receta → fragmentos → video → anuncio**, linaje de una receta hecha a mano (sin brief), video sin
terminar que no se puede vincular y desvínculo. En el navegador: informe de 4 filas importado (3 guardadas, la de
totales descartada), sumas y tasas correctas (gasto 100, CTR 1,50 %, CPA 14,29, ROAS 4,00), filtro por día y
vínculo con un video existente mostrando su receta y sus tres fragmentos.

**No verificado:** un informe real exportado de Ads Manager. Los nombres de columna aceptados son los que conozco de
sus exportaciones en español y en inglés, pero no los comprobé contra un archivo real; si una exportación usa otro
nombre, la importación lo dice (columna faltante) y se agrega el alias. Tampoco la conciliación de cifras contra Ads
Manager, que es parte de la puerta.

**Límites de este paso:** no hay conexión con la API de Meta: la carga es manual (MET-01 y MET-04 no están). No se
importan estados, fechas ni objetivo de las campañas, ni ubicaciones ni audiencias: el CSV básico no los trae. No
hay ventana de atribución: las compras son las que diga el informe. Si el informe no trae el identificador de la
campaña o del conjunto, se los identifica por nombre (dos con el mismo nombre se confunden). Todas las métricas de
una cuenta se asumen en una moneda y no se convierten entre cuentas. El vínculo es manual, anuncio por anuncio. Un
número con un solo separador y exactamente tres cifras después (`1,234`) se lee como miles. No hay todavía análisis
por componente, niveles de evidencia, experimentos ni recomendaciones.

**Paso 2 — Análisis por componente con niveles de evidencia: hecho y verificado, 2026-11-11.** *Rendimiento →
Análisis por componente*.
- **Agrupaciones (MET-06)**: los anuncios vinculados se agrupan por el fragmento con que abren, el fragmento con que
  cierran, cada fragmento (esté donde esté), la estructura (secuencia de funciones), y la terminación (subtítulos,
  música, transición). Cada grupo muestra sus métricas sumadas y las del resto.
- **Nivel de evidencia (MET-08)**: *insuficiente* (menos de 2 anuncios o de 5.000 impresiones), *preliminar*, o
  *moderada* (al menos 5 anuncios, de 2 o más campañas, y 50.000 impresiones). Con evidencia insuficiente no se
  calcula ninguna señal.
- **Señal estadística**: compara el CTR del grupo con el del resto (prueba de dos proporciones) y dice si la
  diferencia es compatible con el azar, probable o clara, y hacia dónde.
- **Lectura (MET-10, parcial)**: cada grupo trae una frase con lo observado, qué tan firme es y qué no se puede
  concluir. Siempre dice que es una correlación y si todos los anuncios son de una misma campaña.
- **Control de factores (MET-07, parcial)**: el análisis se puede limitar a una campaña y a un período, que es la
  forma de sacar de en medio objetivo, presupuesto y audiencia con los datos que hay.
- **Fatiga (MET-06)**: señala los anuncios con 6 o más días cuyo CTR cayó un 20 % o más entre la primera y la
  segunda mitad, con volumen suficiente y una caída que difícilmente sea azar.
- **Cobertura a la vista**: la pantalla dice cuántos anuncios con datos están vinculados a una creatividad; los
  demás no entran en el análisis.

Evidencia: 12 tests nuevos (238 en total, en verde): 7 casos de nivel de evidencia; señal estadística con valores
conocidos (1,5 % contra 1,0 % es clara; 1,2 % contra 1,0 % no se distingue del azar); fatiga con sus cinco casos sin
señal; agrupación de tres anuncios con todas sus lecturas (y que ninguna dice "ganador"); suma de métricas con datos
faltantes; y el análisis completo sobre un informe importado y tres videos renderizados (cobertura 3 de 4, grupo de
apertura con diferencia clara y evidencia preliminar, fragmento que cuenta en dos creatividades, fatiga del anuncio
que cae, filtro por campaña y por período, aislamiento). En el navegador, con los dos anuncios de prueba: cobertura
"2 de 2" y todas las lecturas en "datos insuficientes" o "no hay con qué comparar", como corresponde.

**Límites que importan para no leer de más:**
- **Los umbrales los elegí yo** al construir el módulo; no están calibrados con datos reales. Están con nombre en
  `PerformanceRules` para revisarlos.
- **La señal estadística es aproximada**: trata cada impresión como independiente y no lo son, así que exagera algo
  la certeza. Y se mira sobre muchas agrupaciones a la vez: entre veinte comparaciones, alguna "probable" aparece
  por azar. No hay corrección por comparaciones múltiples.
- **Sólo compara CTR.** CPA y ROAS se muestran pero sin señal: con pocas compras la prueba no sirve.
- **Es correlación.** Un fragmento aparece en ciertos anuncios porque alguien lo eligió para ellos, no al azar. El
  objetivo de la campaña, la audiencia, las ubicaciones, la estacionalidad y la oferta no se importan todavía y no
  se pueden controlar. Para afirmar una causa hace falta un experimento, que no está construido.
- Un anuncio cuenta en todos los fragmentos que usa: los grupos se superponen y no se pueden sumar entre sí.
- No hay recomendaciones accionables ("usá este fragmento"): sólo lecturas. Eso es la Fase 5.

**Paso 3 — Experimentos controlados: hecho y verificado, 2026-11-11.** *Rendimiento → Experimentos*.
- **Definición previa (MET-09)**: nombre, hipótesis, qué cambia (apertura, cierre, desarrollo, subtítulos, música,
  transición u otra cosa), con qué se mide (CTR, reproducciones de 3 s sobre impresiones, o compras sobre clics),
  período, impresiones mínimas por brazo y de 2 a 4 brazos, un anuncio cada uno. El primero es el control.
- **Resultado calculado, no guardado**: sobre las métricas importadas del período, cada vez que se consulta. No
  informa ninguna diferencia si algún brazo no llegó al mínimo de impresiones o no tiene el dato de la métrica. Con
  más de dos brazos el umbral sube (corrección de Bonferroni). Si el período no terminó, el resultado se marca
  provisional y la pantalla explica por qué no conviene frenarlo antes.
- **Revisión del diseño**: avisa si los brazos están en campañas distintas, si el reparto fue desparejo (un brazo
  con más del doble de impresiones que otro) y, comparando las creatividades vinculadas, si un brazo **no difiere
  del control en lo que se dice probar** o **difiere en más de una cosa**. Con avisos, el veredicto se muestra
  apagado, con la etiqueta "Diseño con problemas", y el resumen dice que la diferencia puede deberse a ellos.
- **Cierre**: la conclusión la escribe una persona; el sistema calcula, no decide.
- **Linaje (MET-05)**: el detalle de un anuncio muestra los experimentos en los que participa.

Evidencia: 7 tests nuevos (245 en total, en verde): diferencias entre creatividades; experimento limpio con su
resumen exacto; falta de volumen o de dato; **la misma diferencia que es "probable" con dos brazos deja de serlo con
tres**; compras sobre clics con diferencia en contra; todos los avisos de diseño y el matiz en el resumen; y el flujo
completo (período que deja afuera un día posterior, experimento sin fecha de fin que queda provisional, cierre con
conclusión, validaciones y aislamiento). En el navegador: experimento creado con los dos anuncios de prueba; dio
"diferencia clara" y a la vez avisó reparto desparejo y tres diferencias de más entre las creatividades. El ajuste
posterior de la pantalla (veredicto apagado y etiqueta de diseño) quedó cubierto por tests en su parte de reglas; la
vista no se volvió a abrir en el navegador.

**Límites que importan:**
- **No asigna personas al azar.** Compara dos anuncios; si Meta le mostró cada uno a gente distinta, eso no se ve.
  La asignación aleatoria la da la herramienta de pruebas A/B de Meta: lo correcto es correr la prueba ahí y leerla
  acá. Por eso "controlado" llega hasta donde el sistema puede comprobar.
- La prueba estadística es la misma aproximación del análisis: trata cada impresión como independiente.
- Un anuncio por brazo; no hay brazos con varios anuncios ni reparto de presupuesto planificado.
- No calcula de antemano cuántas impresiones hacen falta para detectar una diferencia dada: el mínimo lo pone la
  persona.
- Sólo métricas de proporción. CPA y ROAS no se comparan.
- Un experimento no se edita ni se borra; la conclusión no se corrige.

**No construido: conexión y sincronización por la API de Meta (MET-01, MET-04).** Necesita una app de Meta
aprobada, un token con permisos de lectura de anuncios y decidir dónde se guarda cifrado. Sin eso sólo se podría
escribir un cliente sin probar contra nada. Lo que la API agregaría sobre el CSV: carga automática e incremental,
estados y fechas, objetivo, audiencia y ubicaciones (para controlar factores de verdad), y el vínculo automático de
lo que la plataforma publique. Todo el modelo de datos ya está: la API llenaría las mismas tablas.

**Puerta de salida**
- Para una creatividad se ve su trazabilidad completa y las métricas de sus componentes.
- Las cifras importadas concilian con Ads Manager en una muestra.
- Las recomendaciones indican datos usados, limitaciones y confianza.

## Fase 5 — Aprendizaje

**Entregables:** patrones, hipótesis, memoria de aprendizaje, motor de priorización con ponderaciones versionadas,
recomendaciones, plan de producción basado en resultados.

**Paso 1 — Hipótesis y memoria de aprendizaje: hecho y verificado, 2026-11-11.** Menú *Aprendizaje*.
- **Hipótesis (pasos 3 y 4 del ciclo)**: una hipótesis sale de un patrón del análisis por componente (botón "Guardar
  como hipótesis" en cada lectura con evidencia; el patrón se recalcula en el servidor y el enunciado se escribe
  como conjetura) o la escribe una persona. Desde la hipótesis se arma el experimento que la prueba, con su
  enunciado y su variable ya propuestos. Su estado (sin probar, en prueba, probada, con aprendizaje) se deduce.
- **Memoria (LRN-04)**: un aprendizaje se registra desde un experimento con resultado o desde el patrón de una
  hipótesis. El sistema fija los datos usados, el resultado, el período, la evidencia (anuncios, videos y, si
  corresponde, el fragmento), la confianza con su motivo y la versión del criterio; la persona escribe qué se
  aprendió, la recomendación y la aplicabilidad.
- **Confianza calculada, no opinada**: baja para una correlación o un experimento sin terminar, con problemas de
  diseño o sin diferencia; media para una correlación clara con evidencia moderada o un experimento limpio con
  diferencia probable; alta sólo para un experimento limpio, terminado y con diferencia clara.
- **Revisar, corregir, invalidar (LRN-05, paso 11 del ciclo)**: el detalle de un aprendizaje vigente recalcula su
  fuente con los datos de hoy y avisa si cambió o si ya no alcanza. Corregir crea una versión nueva con los datos
  de hoy y deja la anterior como corregida, intacta. Invalidar lo saca de los vigentes y guarda el motivo.

Evidencia: 4 tests nuevos (249 en total, en verde): la confianza de un experimento en sus seis casos; la de un
patrón, que nunca llega a alta, y que ningún motivo dice "ganador" ni menciona ROAS; enunciados, variables y estados;
y el ciclo completo con videos renderizados e informes importados (hipótesis desde un patrón sin duplicar, experimento
limpio que da confianza alta, aprendizaje desde el patrón con el fragmento como evidencia, reimportación que cambia
el resultado y lo avisa, corrección que conserva la versión anterior, anuncio desvinculado que deja al patrón sin
sustento, **invalidación que lo saca de los vigentes**, y aislamiento entre organizaciones). En el navegador: desde
el experimento "Prueba de música" se registró un aprendizaje (confianza baja por el diseño), se corrigió, y el
anterior quedó como corregido y sin acciones; se creó una hipótesis a mano y el formulario de experimento la
propuso; pedir un aprendizaje de una hipótesis sin datos responde que no alcanzan. **No se vio en el navegador** el
botón "Guardar como hipótesis" del análisis: los datos de desarrollo no tienen ningún patrón con evidencia.

**Límites que importan:**
- **Los aprendizajes todavía no influyen en nada**: el plan de producción que los usa es el paso 3. Hoy son memoria
  consultable.
- La confianza hereda las aproximaciones del análisis y de los experimentos (impresiones tratadas como
  independientes, sin asignación al azar comprobable) y sus umbrales sin calibrar.
- Los patrones sólo miran el CTR, así que una hipótesis surgida de un patrón habla de CTR.
- El aviso de "los datos cambiaron" compara el resultado en palabras y la confianza: cualquier cambio en las cifras
  lo dispara, aunque la conclusión sea la misma.
- Una hipótesis no se edita; un aprendizaje no se reactiva después de invalidarlo (se registra otro).
- No hay detección automática de patrones ni generación de hipótesis con IA: las elige una persona desde el
  análisis.

**Paso 2 — Priorización con ponderaciones versionadas: hecho y verificado, 2026-11-12.** *Aprendizaje → Prioridades*.
- **Qué se prioriza**: las hipótesis que no se descartaron. El puntaje, de 0 a 100, es la suma de peso por valor de
  diez factores sobre la suma de los pesos. **Un factor sin dato no suma**: lo que no se sabe no da prioridad.
- **Seis factores los calcula el sistema** con los datos de hoy: rendimiento histórico (CTR del patrón frente al
  resto, acotado a ±100 %), calidad de la evidencia (confianza del mejor aprendizaje vigente; si no hay, la
  evidencia del patrón), similitud (cuántas recetas ya usan el componente), novedad (anuncios y experimentos que
  ya lo probaron), disponibilidad de material (derechos vigentes del original o de la música) y fatiga (anuncios
  del patrón con señal de fatiga).
- **Cuatro los valora una persona** en la hipótesis (bajo, medio, alto), porque el sistema no tiene de dónde
  medirlos: relevancia para el público, costo de producción, riesgo publicitario y margen potencial.
- **Explicable**: cada hipótesis muestra, factor por factor, el peso, el valor y de qué dato sale.
- **Ponderaciones versionadas y auditadas (LRN-02)**: de 0 a 10 por factor. Guardar crea una versión nueva y nunca
  pisa la anterior; el historial queda a la vista y cada cambio va a la auditoría con su motivo. Sólo las cambia un
  administrador de la organización. Sin ninguna guardada rigen las de fábrica (versión 0).
- **Sin puntajes de IA (LRN-03)**: no interviene ningún modelo, y la pantalla dice que el puntaje ordena qué
  producir primero y no predice el ROAS ni ningún resultado. Un test fija que ninguna explicación lo prometa.

Evidencia: 3 tests nuevos y un bloque más en el de aprendizaje (252 en total, en verde): puntaje exacto para
unos factores dados y otro con otras ponderaciones; factores sin dato, escalas y topes; versiones de ponderaciones
(crear la 1 y la 2, la 1 intacta, todos los pesos en cero rechazado, hipótesis descartada fuera, otra organización
con las de fábrica); y, con videos renderizados e informe importado, los diez factores de una hipótesis surgida de un
patrón y de una escrita a mano. En el navegador, con la hipótesis de prueba: 6,4 puntos sin valorar, 28,7 después de
valorarla y 22,8 al guardar la versión 1 de las ponderaciones, que quedó en el historial. **No se vio en el navegador**
una hipótesis surgida de un patrón (los datos de desarrollo no tienen ninguna) ni el aspecto de las pantallas: el
panel estaba oculto y sólo se leyó su contenido.

**Límites que importan:**
- **Las escalas de cada factor y los pesos de fábrica los elegí yo**, sin calibrar con datos reales. Están en
  `PriorityRules`.
- **El puntaje no se guarda** (no existe `OpportunityScore`): se calcula con los datos y las ponderaciones de hoy.
  Queda fijo recién cuando un plan lo use, en el paso 3.
- Una hipótesis escrita a mano tiene como mucho seis factores con dato: rendimiento, similitud, material y fatiga
  necesitan un patrón. Queda por debajo de una surgida de un patrón aunque tenga un experimento de confianza alta.
- El rendimiento histórico es el CTR de una correlación; no mira compras ni ROAS.
- Similitud y novedad se solapan en parte: las dos bajan cuando algo ya se usó mucho.
- La disponibilidad de material mira derechos, no si el fragmento está elegido para producción.
- Lo que valora una persona es una opinión: el motor la usa tal cual y lo dice en el desglose.

**Paso 3 — Plan de producción basado en resultados: hecho y verificado, 2026-11-12.** *Aprendizaje → Plan de
producción*.
- **Qué arma (LRN-06)**: toma las hipótesis de mayor puntaje de hoy (las de puntaje cero no entran) y guarda, de
  cada una, qué se propone hacer y por qué. No interviene ninguna IA.
- **Qué propone, según la evidencia**: *producir aplicándola* sólo con un aprendizaje vigente de confianza media o
  alta; *probar con un experimento* si el aprendizaje es de confianza baja o sólo hay un patrón; *explorar en chico*
  si es una conjetura sin datos.
- **Explica la elección de hooks, desarrollos y enfoques**: cada renglón dice qué parte del anuncio toca (apertura,
  desarrollo, cierre, terminación o enfoque) y con qué fragmento, qué evidencia lo respalda (el aprendizaje, citado,
  con su recomendación), qué factores sumaron más al puntaje y cuáles no tenían dato.
- **Es una foto**: guarda el puntaje, el desglose y la versión de las ponderaciones del día en que se armó, y no
  cambia. Si después cambian las ponderaciones o un aprendizaje deja de estar vigente, el plan lo avisa.
- **Un aprendizaje invalidado deja de influir**: el plan siguiente ya no lo cita, la calidad de la evidencia de su
  hipótesis baja y la propuesta pasa de aplicar o probar a explorar.
- **El motor estratégico de la Fase 3 recibe los aprendizajes vigentes** (paso 5 del flujo de la directiva,
  "consultar aprendizajes previos"): van en el pedido al planificador de conceptos con su confianza, y la consigna
  (`concept-plan-v3`) le pide aplicar los de confianza media o alta y tratar los de baja como indicios. **Sin probar
  contra el servicio real**, como el resto del motor.

Evidencia: 4 tests nuevos y dos bloques más en el de aprendizaje (256 en total, en verde): qué se propone en
cada caso; la explicación exacta de una conjetura y las partes de las demás (sin "ganador" ni "ROAS"); el texto que
recibe el planificador con y sin aprendizajes; un plan como foto (no cambia al cambiar ponderaciones ni
valoraciones, uno nuevo sí, sin candidatas no se arma, aislamiento); y con datos reales, **un plan que propone
aplicar la hipótesis respaldada por un experimento y probar la del patrón, explicando el hook con su fragmento, y
el plan siguiente a la invalidación, que ya no cita esos aprendizajes y deja las dos como exploración**. En el
navegador: se armó "Plan de prueba" con la única hipótesis de desarrollo, como exploración, con su explicación y su
desglose. Ahí apareció un defecto: los acentos del texto fijo de la pantalla estaban rotos por un error mío de
codificación al editar la vista; se corrigió y se volvió a abrir. **No se vio en el navegador** un plan con
aprendizajes, con fragmento ni con el aviso de aprendizaje no vigente: los datos de desarrollo no tienen ninguna
hipótesis surgida de un patrón ni con aprendizaje propio.

**Límites que importan:**
- **El plan dice qué hipótesis trabajar, no arma las recetas.** Desde cada renglón se va al editor de recetas o a
  crear el experimento, pero el fragmento no viaja precargado.
- **No está unido al brief ni al plan de conceptos de la Fase 3**: son dos planes distintos. El de conceptos recibe
  los aprendizajes, pero no las prioridades ni este plan.
- Hereda todos los límites de la priorización: escalas y pesos sin calibrar, CTR como única medida de rendimiento,
  desventaja de las hipótesis escritas a mano.
- "Aplicar" se decide sólo por la confianza del aprendizaje; no mira si su aplicabilidad coincide con la campaña
  que se va a producir. Eso lo lee una persona.
- No hay detección automática de patrones ni hipótesis generadas por IA: el ciclo empieza cuando una persona guarda
  una hipótesis.
- Del ciclo de 11 pasos de la directiva quedan para la Fase 6 preparar pruebas automáticamente y publicar.

**Puerta de salida**
- A partir de resultados de campañas anteriores se genera un plan que prioriza hipótesis respaldadas por evidencia y
  explica la elección de hooks, desarrollos y enfoques.
- Un aprendizaje puede invalidarse y deja de influir en el siguiente plan.

**Estado de la puerta:** el segundo punto está demostrado con un test. El primero está demostrado con material de
prueba (tres videos y un informe armado a mano); **falta medirlo con resultados de campañas reales**, que dependen
de importar informes reales de Ads Manager y vincular sus anuncios.

## Fase 6 — Automatización publicitaria

**Entregables:** publicación por Marketing API, reglas, límites, aprobaciones, modos de autonomía, simulación,
protección presupuestaria, kill switch, detección de anomalías, auditoría.

**Paso 1 — Política, guardas, aprobaciones, kill switch y simulación: hecho y verificado, 2026-11-12.** Menú
*Automatización*.
- **Una acción**: publicar un video generado como anuncio en un conjunto conocido, con un nombre y un presupuesto
  diario. La pide un editor; pasa por las guardas al pedirla, al aprobarla y otra vez al ejecutarla.
- **Simulación por defecto (LRN-11)**: la ejecución real está desactivada. Mientras lo esté, toda acción es una
  simulación que registra lo que se habría hecho y **nunca llama al publicador**. Habilitarla exige conexión con
  Meta, los tres límites definidos y el kill switch apagado; hoy no hay conexión, así que en la aplicación no se
  puede habilitar.
- **Límites (LRN-09)**: presupuesto diario máximo por acción, presupuesto máximo comprometido por día y acciones
  máximas por día. Sin los tres definidos toda acción queda bloqueada. Una acción que excede cualquiera queda
  bloqueada con su motivo.
- **Modos (LRN-07)**: en *manual* y *semiautomático* publicar siempre lo aprueba una persona; en *automático
  controlado* se ejecuta sola sólo si entra en los límites y la respalda un aprendizaje vigente de confianza media
  o alta. Sin ese respaldo ("datos insuficientes") la decide una persona.
- **Aprobaciones (LRN-08)**: aprobar, rechazar, cambiar la política y accionar el kill switch son de un
  administrador de la organización. Al aprobar se vuelven a evaluar las guardas.
- **Kill switch (LRN-10)**: al activarlo se cancelan las acciones que esperan aprobación o están en cola, no se
  acepta ninguna nueva y no se puede habilitar la ejecución real. Desactivarlo no revive nada. Queda el historial.
- **Duplicados (LRN-10)**: el mismo video no se pide dos veces para el mismo conjunto mientras haya una acción en
  pie, y el publicador recibe una clave propia de la acción.
- **Derechos**: un video con un original o una pista sin autorización vigente no se publica.
- **Ejecución real** (probada sólo con un publicador falso): crea el anuncio, lo registra y lo vincula a su video
  de forma automática, de modo que el linaje no depende de un vínculo manual. Un rechazo de Meta deja la acción
  fallida con su motivo. Si la ejecución real se deshabilita después de aprobar, la acción no se publica.
- **Auditoría**: cada pedido, decisión, cambio de política, kill switch y ejecución queda auditado.

Evidencia: 4 tests nuevos (260 en total, en verde): **cada guarda con su motivo** (kill switch, límites sin
definir, presupuesto por acción, presupuesto del día, acciones por día, derechos); los tres modos y el control de
datos insuficientes; que aprobar, cambiar la política y el kill switch exigen ser administrador; y el recorrido
completo con un video renderizado: **en simulación el publicador no recibe ni una llamada**, sin conexión no se
puede habilitar la ejecución real, duplicados rechazados, kill switch que cancela lo pendiente y lo que está en
cola, y con la ejecución real habilitada a propósito y un publicador falso: aprobación obligatoria sin respaldo,
ejecución automática con un aprendizaje de confianza alta, anuncio vinculado solo, sin doble publicación, freno al
deshabilitar la ejecución real y rechazo de Meta. En el navegador, en simulación: se fijaron límites, una acción
quedó a la espera, se aprobó y quedó simulada; otra quedó bloqueada por exceder el presupuesto por acción; el kill
switch canceló una pendiente y se desactivó.

**Límites que importan:**
- **No existe el cliente de la Marketing API**: no hay ninguna escritura real posible desde la aplicación. Lo que
  se llama "ejecución real" está probado contra un publicador falso; la puerta pide una cuenta de prueba de Meta.
- Hay un solo tipo de acción (publicar). Pausar un anuncio o cambiar un presupuesto no existen.
- El presupuesto es el que la acción declara comprometer por día; no se lee el gasto real de Meta para los
  límites. "Presupuesto por lote" todavía no existe porque las acciones se piden de a una.
- "Datos suficientes" se mide por la confianza del aprendizaje de la hipótesis que se elige como respaldo; el
  sistema no comprueba que ese video aplique de verdad esa hipótesis.
- Los límites cuentan por día en hora UTC de la base.
- El kill switch frena lo que todavía no empezó; una acción que ya superó la última comprobación termina.
- **No hay detección de anomalías** ni acciones propuestas por el sistema (desde el plan de producción): todas las
  pide una persona. Son el paso 2.

**Paso 2 — Anomalías, acciones propuestas por el sistema y presupuesto por lote: hecho y verificado, 2026-11-12.**
- **Detección de anomalías (LRN-10)**: al terminar cada importación de métricas, y a pedido, se mira el último día
  con datos de cada anuncio contra los anteriores: salto de gasto (3 veces o más el promedio de al menos 3 días),
  caída del CTR (a la mitad o menos, con volumen y sin que parezca azar) y gasto sin impresiones. Cada anomalía
  queda abierta hasta que un administrador la revisa y anota qué concluyó.
- **Una anomalía abierta le quita la autonomía al sistema**: en modo automático, mientras haya alguna sin revisar,
  toda acción pasa a aprobación, tenga el respaldo que tenga.
- **Acciones propuestas por el sistema (LRN-01, pasos 8 y 9 en simulación)**: desde un plan de producción, para
  cada hipótesis que el plan dice aplicar y que trata de un fragmento, el sistema busca el video listo más nuevo,
  sin publicar, que lo use en ese lugar (apertura, cierre o cualquier parte) y pide publicarlo con esa hipótesis
  como respaldo. Cada acción pasa por las mismas guardas; proponer dos veces no duplica; lo que no da una acción
  se informa con su motivo.
- **Presupuesto por lote (LRN-09)**: un lote propuesto de una vez no puede comprometer más que el máximo por lote.
  Si lo supera se rechaza entero; sin ese límite definido no se propone nada.

Evidencia: 3 tests nuevos (263 en total, en verde): las tres anomalías con sus cifras exactas y los casos en
que no se señala nada; cuándo una receta aplica una hipótesis; y **el ciclo completo en simulación** con cuatro
videos renderizados: métricas importadas → análisis → hipótesis → aprendizaje → prioridades → plan → publicaciones
propuestas (lote sin límite y lote excedido rechazados; una acción creada, ejecutada sola y simulada; la hipótesis
sin fragmento afuera; sin duplicar) → métricas nuevas con un salto de gasto detectado por la importación → la misma
acción respaldada pasa a aprobación → revisión de la anomalía → el sistema vuelve a ejecutar sola, **sin una sola
llamada al publicador**. En el navegador: la búsqueda de anomalías corre, proponer sin presupuesto por lote se
rechaza, y con el límite guardado el plan de desarrollo responde que no tiene hipótesis para aplicar. **No se vio
en el navegador** una anomalía ni una acción propuesta: los datos de desarrollo no tienen ninguna de las dos.

**Límites que importan:**
- Los umbrales de las anomalías los elegí yo, sin calibrar. Sólo miran el último día de cada anuncio: una anomalía
  en el medio de un período importado de una vez no se ve.
- Todas las anomalías abiertas cuentan, aunque sean de métricas viejas: hay que revisarlas para recuperar la
  autonomía. Una anomalía no activa el kill switch ni frena lo ya aprobado.
- El sistema propone sólo lo que trata de un fragmento. Una hipótesis sobre música, subtítulos o enfoque queda
  afuera, y lo dice.
- La propuesta la dispara una persona eligiendo el plan, el conjunto y el presupuesto: **no hay un proceso que
  proponga solo cada tanto**. Con el modo automático y respaldo, lo propuesto sí se ejecuta sin aprobación.
- El ciclo cubre hasta "publicar" en simulación. Evaluar los resultados de lo publicado depende de importar
  después las métricas de esos anuncios, que en simulación no existen.

**Paso 3 — Publicación real por la Marketing API: pendiente; necesita la app y el token de Meta.**

**Estado de la puerta:** los dos primeros puntos están demostrados con tests (el ciclo completo en simulación, sin
ninguna llamada al publicador, y un test por guarda). **El tercero no se puede medir**: no existe el cliente de la
Marketing API ni una cuenta de prueba.

**Puerta de salida**
- Ciclo completo en **modo simulación** sin ninguna escritura real en Meta.
- Cada guarda (límites, kill switch, datos insuficientes, duplicados) tiene un test que la demuestra.
- Publicación real probada en una cuenta de prueba, habilitada explícitamente por organización.

## Revisión de uso — 2026-11-12

A pedido del usuario se revisó que la aplicación sea fácil de recorrer. **Defecto encontrado y corregido:** el menú
tenía 10 entradas y necesitaba 1513 px; en 1366 px y en 1024 px aparecía barra horizontal y "Salir" quedaba fuera de
vista. Cambios:
- **Menú agrupado por etapa**, con siete entradas (Inicio, Biblioteca, Producción, Estrategia, Resultados,
  Automatización, Configuración), que marca la sección y la pantalla activas. Entra en 1366 px; entre 1200 y 1300 px
  oculta el nombre del usuario, y por debajo de 1200 px pasa a menú desplegable.
- **Todo está en el menú**: Lotes, Música, Experimentos y Análisis por componente antes sólo se alcanzaban por un
  botón dentro de otra pantalla.
- **Inicio como guía**: qué hay en cada una de las cinco etapas y el paso siguiente (primero lo que espera a una
  persona: anomalías y aprobaciones).
- **Pantalla "Videos generados"** (pedida por la directiva como "Creatividades generadas"): todos los videos, con
  su receta, su lote, su estado y si se publicaron.
- **"Plan de conceptos"** en lugar de "plan creativo" para el plan que sale de un brief, con la aclaración de en
  qué se diferencia del plan de producción en las dos pantallas.
- **Trabajos**: explicación de qué son, nombres legibles en lugar de identificadores internos, y la herramienta de
  diagnóstico sólo para administradores de la plataforma, plegada.
- **Automatización en tres pestañas** (Acciones, Política y límites, Anomalías), que recuerdan cuál estaba abierta.
- **Introducciones cortas** con el detalle en un desplegable, en Aprendizajes, Prioridades, Análisis, Plan de
  producción y Automatización.

Evidencia: 2 tests nuevos (265 en total, en verde) para el paso siguiente del inicio y los conteos; en el
navegador, a 1366, 1300, 1200 y 1024 px sin barra horizontal, sección activa correcta en cinco pantallas, inicio,
videos generados (12), trabajos con nombres legibles y pestañas de automatización.

**No revisado:** el uso en celular, la accesibilidad (teclado, contraste, lectores de pantalla) y cómo se ve cada
pantalla con muchos datos. **No cambiado:** las fechas siguen mostrando "UTC" y varias pantallas de detalle siguen
siendo largas.
## Dependencias entre fases

```
F0 ──► F1 ──► F2 ──► F3
        │      │      │
        │      └──────┼──► F4 (linaje completo requiere variantes de F2)
        └─────────────┘     │
   (importación básica      ▼
    de Meta: anticipable)   F5 ──► F6
```

- F2 depende de F1: sin fragmentos confiables no hay recetas.
- F3 depende de F2 (las recetas del plan se ejecutan con el motor) y de F1 (disponibilidad de material).
- F4 analítica depende de F2 para el linaje; la importación sólo depende de F0.
- F5 depende de F4 (evidencia) y de F3 (planificación).
- F6 depende de F5 y de F4 (permisos de escritura sobre la misma integración).

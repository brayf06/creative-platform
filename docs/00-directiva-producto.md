# Creative Intelligence Platform — Directiva de producto, prioridades y arquitectura funcional

> **Desvío acordado el 2026-11-11:** la sección 2 pide capas separadas (Application Services, Domain, Infrastructure)
> y Background Workers. El usuario decidió después unir todo en **un solo proyecto MVC** y correr los trabajos en
> segundo plano **dentro de la Web**. En ese punto manda esa decisión, no el texto de abajo.
>
> Texto original de la directiva recibida el 2026-11-11. Es la **única especificación de origen** del proyecto: no
> existe un prompt maestro anterior. Los demás documentos de `docs/` derivan de éste; ante una diferencia, manda éste.

Quiero actualizar y consolidar las especificaciones de Creative Intelligence Platform.

Esta directiva complementa el prompt maestro de desarrollo existente y establece las prioridades definitivas del
producto.

El objetivo es construir una plataforma propia que reúna las principales capacidades funcionales de Recharm, raw2ads y
Creatify, agregando un motor de aprendizaje basado en resultados reales de Meta Ads.

Estas herramientas son referencias competitivas y funcionales. No deben incorporarse como dependencias obligatorias, ni
copiar su código, diseño propietario o propiedad intelectual.

La plataforma debe ser independiente y controlar sus propios datos, algoritmos de composición, servicios de IA y lógica
de negocio.

## 1. Visión del producto

Construir una plataforma de inteligencia creativa publicitaria capaz de transformar bibliotecas de videos existentes en
nuevas creatividades optimizadas mediante inteligencia artificial.

El sistema deberá permitir:

1. Cargar cientos o miles de videos e imágenes.
2. Detectar automáticamente sus escenas y fragmentos narrativos.
3. Comprender, clasificar y documentar cada fragmento.
4. Construir una biblioteca multimedia con búsqueda semántica.
5. Recuperar información histórica de campañas publicitarias.
6. Identificar estructuras y enfoques creativos asociados a buenos resultados.
7. Investigar tendencias, anuncios y referencias externas mediante fuentes autorizadas.
8. Interpretar briefs comerciales del usuario.
9. Proponer nuevos conceptos y guiones publicitarios.
10. Seleccionar automáticamente escenas apropiadas.
11. Generar lotes de videos mediante recombinación de fragmentos.
12. Generar nuevos recursos audiovisuales mediante IA cuando sea necesario.
13. Validar la calidad, coherencia y legalidad de las creatividades.
14. Publicar anuncios en Meta mediante la Marketing API, con controles y permisos.
15. Analizar sus resultados.
16. Utilizar los aprendizajes para priorizar próximas producciones.

El sistema deberá ofrecer funcionamiento manual, semiautomático y automático.

El objetivo final es que el usuario pueda introducir un brief, como:

> "Generar 20 anuncios de Navidad dirigidos a mujeres interesadas en regalos prácticos, utilizando nuestra biblioteca y
> aprendizajes históricos".

Y que el sistema pueda investigar, planificar, seleccionar material, editar, renderizar, evaluar y preparar esas 20
creatividades con mínima intervención humana.

## 2. Arquitectura obligatoria

Mantener la arquitectura ASP.NET Core MVC acordada.

Stack principal:

- ASP.NET Core MVC.
- Razor Views.
- Controllers.
- Application Services.
- Domain Services y reglas de negocio cuando corresponda.
- Infrastructure.
- Entity Framework Core.
- SQL Server.
- Bootstrap 5.
- JavaScript, jQuery y AJAX.
- SignalR para progreso.
- Background Workers.
- Python, PySceneDetect y FFmpeg para procesamiento audiovisual.
- Proveedores de IA configurables.

No utilizar React ni desarrollar una SPA como aplicación principal.

No introducir microservicios innecesarios.

La aplicación web debe actuar como centro de administración y orquestación.

Los trabajos intensivos deben ejecutarse de manera asíncrona, fuera del ciclo de solicitudes HTTP.

Las operaciones deben ser auditables, recuperables e idempotentes.

Diseñar los módulos para que puedan evolucionar sin rehacer todo el sistema.

## 3. Prioridad 1 — Intelligent Media Library

Referencia funcional: Recharm.

Esta es la primera capacidad central que debe desarrollarse y validarse.

### Objetivo

Transformar una biblioteca inicial de aproximadamente 500 videos en un catálogo de escenas reutilizables, clasificadas
y documentadas automáticamente.

### Funcionalidades

**A. Ingesta de contenido**

- Carga individual y masiva.
- Detección de archivos nuevos.
- Importación desde fuentes autorizadas.
- Extracción de propiedades técnicas.
- Identificación de duplicados exactos y visuales.
- Generación de miniaturas.
- Conservación de los archivos originales.
- Registro de procedencia y derechos.

**B. Detección automática de escenas**

Utilizar PySceneDetect, FFmpeg y análisis multimodal.

Identificar:

- Cortes directos.
- Cambios de plano.
- Transiciones.
- Fragmentos repetidos.
- Segmentos narrativos.
- Hooks.
- Demostraciones.
- Testimonios.
- Problemas.
- Beneficios.
- CTA.

Distinguir entre segmentos visuales y segmentos narrativos.

Un hook puede contener varias escenas visuales.

**C. Documentación automática**

Cada fragmento debe guardar:

- ID del video original.
- Timestamp inicial y final.
- Descripción visual.
- Transcripción.
- Texto en pantalla.
- Producto identificado.
- Acciones observadas.
- Enfoque comercial.
- Función narrativa.
- Hook o mensaje principal.
- Tipo de público previsto.
- Calidad visual.
- Calidad de audio.
- Etiquetas.
- Embeddings.
- Confianza de clasificación.
- Autorización de reutilización.
- Versiones del análisis y modelo utilizado.

**D. Buscador inteligente**

Implementar búsquedas semánticas y filtros tradicionales.

Ejemplo:

> "Buscar hooks de menos de 4 segundos que muestren resultados de uñas terminadas y transmitan practicidad".

El motor debe devolver fragmentos relevantes, con miniatura, timestamps y acceso al video original.

**E. Timeline de escenas**

Desarrollar una interfaz visual mediante MVC, JavaScript y componentes reutilizables.

Permitir:

- Visualizar los cortes detectados.
- Reproducir cada fragmento.
- Corregir límites.
- Fusionar segmentos.
- Dividir segmentos.
- Editar etiquetas.
- Consultar análisis.
- Seleccionar fragmentos para producción.

### Criterio de aceptación

El sistema debe poder recibir una muestra real de videos, detectar sus escenas, catalogarlas, persistirlas y permitir
su recuperación por búsqueda semántica.

No avanzar al motor de generación masiva hasta que la identificación y selección de fragmentos sea suficientemente
confiable.

## 4. Prioridad 2 — Automated Creative Remix Engine

Referencia funcional: raw2ads.

### Objetivo

Generar automáticamente nuevos videos publicitarios reutilizando fragmentos de la biblioteca.

### Clasificación de componentes

Definir una taxonomía de componentes creativos:

- Hook.
- Problema.
- Presentación.
- Demostración.
- Beneficio.
- Prueba social.
- Objeción.
- Comparación.
- Oferta.
- CTA.
- B-roll.
- Cierre.

Un fragmento puede cumplir varias funciones.

### Motor de composición

Crear un servicio que permita generar videos mediante recetas estructuradas.

Cada receta debe definir:

- Concepto comercial.
- Hipótesis.
- Hook seleccionado.
- Desarrollo.
- Escenas complementarias.
- CTA.
- Orden de fragmentos.
- Duración objetivo.
- Transiciones.
- Subtítulos.
- Overlays.
- Música autorizada.
- Formato y resolución.

### Variaciones automáticas

Permitir generar variantes cambiando:

- Hook.
- Desarrollo.
- CTA.
- Escenas.
- Narrativa.
- Duración.
- Orden.
- Mensaje.
- Formato.

Implementar controles para evitar variantes prácticamente idénticas.

### Renderización

Utilizar FFmpeg como motor inicial.

El sistema debe:

1. Validar la receta.
2. Recuperar los fragmentos autorizados.
3. Cortar los clips.
4. Normalizar formatos y audio.
5. Combinar escenas.
6. Generar subtítulos y overlays.
7. Renderizar el resultado.
8. Validar propiedades del archivo.
9. Almacenar el video final.

No bloquear la solicitud web durante el procesamiento.

Registrar la relación entre cada nuevo video y todos los fragmentos utilizados.

### Generación por lotes

Permitir solicitudes de 5 a 50 videos inicialmente.

Cada lote tendrá:

- Brief.
- Conceptos.
- Variantes.
- Recetas.
- Estado.
- Progreso.
- Errores.
- Costo.
- Videos generados.
- Resultados de validación.

### Criterio de aceptación

A partir de una selección de escenas reutilizables, el sistema debe producir automáticamente un lote de videos
técnicamente correctos, con estructuras narrativas coherentes y diversidad real.

La generación debe ser reproducible desde las recetas almacenadas.

## 5. Prioridad 3 — Creative Research & Strategy Engine

Referencia funcional: Creatify.

### Objetivo

Convertir instrucciones comerciales en estrategias creativas, incorporando investigación de mercado y referencias
externas.

**A. Brand Intelligence**

Cada empresa debe configurar:

- Productos.
- Beneficios verificables.
- Público objetivo.
- Propuesta de valor.
- Objeciones.
- Estilo de comunicación.
- Identidad visual.
- Objetivos.
- Restricciones comerciales.
- Márgenes.
- Presupuesto.
- Mensajes permitidos y prohibidos.

Toda la información debe poder reutilizarse en campañas futuras.

**B. Brief de campaña**

Permitir definir:

- Producto.
- Ocasión.
- Público.
- Objetivo.
- Mensaje.
- Enfoques.
- Duración.
- Cantidad de videos.
- Plataforma de destino.
- Investigación externa.
- Presupuesto máximo de generación.
- Nivel de autonomía.

**C. Investigación externa**

Crear un agente investigador que consulte fuentes autorizadas.

Investigar:

- Anuncios comparables.
- Tendencias.
- Conceptos comerciales.
- Hooks.
- Estructuras narrativas.
- Estilos.
- Formatos.
- Estacionalidad.

Guardar las referencias con su procedencia y permisos.

Separar material utilizable de material disponible únicamente como referencia.

No descargar, reproducir ni publicar videos ajenos sin autorización.

**D. Motor estratégico**

Combinar:

- Brief.
- Conocimiento de marca.
- Fragmentos disponibles.
- Creatividades históricas.
- Referencias externas.
- Resultados publicitarios, cuando existan.

Generar conceptos comerciales distintos.

Cada concepto debe incluir:

- Hipótesis.
- Enfoque.
- Hook.
- Guion.
- Desarrollo.
- CTA.
- Público previsto.
- Material requerido.
- Material existente.
- Motivo de selección.
- Riesgos.
- Nivel de novedad.

**E. Generación audiovisual adicional**

Permitir generar escenas nuevas cuando la biblioteca no disponga de material adecuado.

Esta funcionalidad debe ser opcional.

Priorizar la reutilización de videos existentes para controlar costos.

Respetar restricciones sobre personas, voces, licencias, música y representación de productos.

### Criterio de aceptación

El usuario debe poder generar un plan de 20 creatividades diferentes a partir de un brief estacional.

Cada concepto debe tener una hipótesis clara y una receta realizable mediante material autorizado, o identificar
explícitamente los recursos faltantes.

## 6. Prioridad 4 — Meta Performance Intelligence

Este es nuestro principal módulo de diferenciación.

Aunque su implementación analítica completa corresponde a esta etapa, la importación de datos y el modelo de
trazabilidad deben prepararse desde las primeras fases.

### Objetivo

Relacionar los anuncios publicitarios con sus componentes creativos para identificar patrones de rendimiento.

### Integración

Implementar Meta Marketing API utilizando permisos oficiales.

Recuperar:

- Cuentas.
- Campañas.
- Conjuntos.
- Anuncios.
- Creatividades.
- Estados.
- Fechas.
- Gasto.
- Impresiones.
- CTR.
- CPC.
- CPM.
- Reproducciones.
- Retención disponible.
- Compras.
- CPA.
- ROAS.

Diseñar sincronización incremental, paginación, controles de cuotas y gestión de errores.

### Creative Lineage

Cada anuncio debe poder relacionarse con:

- CreativeVariant.
- CreativeRecipe.
- Hook.
- Development.
- CTA.
- MediaSegments.
- MediaAssets.
- CampaignBrief.
- CreativeConcept.
- Experimento.

Esto permitirá reconstruir cómo se generó cada anuncio.

### Análisis del rendimiento

El sistema debe responder preguntas como:

- ¿Qué tipos de hooks están asociados con mejores resultados?
- ¿Qué estructuras narrativas presentan menor CPA?
- ¿Qué desarrollos mantienen mejor la atención?
- ¿Qué CTA funcionan mejor según el contexto?
- ¿Qué fragmentos aparecen repetidamente en anuncios ganadores?
- ¿Qué enfoques están mostrando señales de fatiga?

No atribuir automáticamente todo el rendimiento al hook.

Considerar:

- Presupuesto.
- Objetivo de campaña.
- Audiencia.
- Tiempo de publicación.
- Ubicaciones.
- Estacionalidad.
- Oferta.
- Volumen de datos.
- Diferencias de atribución.

Separar observaciones, correlaciones e inferencias causales.

Implementar experimentos controlados cuando sea posible.

No afirmar que una creatividad es ganadora por un resultado aislado.

### Criterio de aceptación

El sistema debe mostrar la trazabilidad completa de una creatividad y relacionar sus componentes con las métricas
observadas.

Debe proporcionar recomendaciones fundamentadas en datos, con indicación de limitaciones y confianza.

## 7. Prioridad 5 — Creative Learning & Automation Engine

### Objetivo

Hacer que el sistema aprenda de los resultados y utilice ese conocimiento para priorizar futuras producciones.

### Ciclo de aprendizaje

1. Recuperar métricas.
2. Analizar rendimiento.
3. Identificar patrones.
4. Generar hipótesis.
5. Priorizar oportunidades.
6. Crear nuevas recetas.
7. Renderizar variantes.
8. Preparar pruebas.
9. Publicar cuando esté autorizado.
10. Evaluar nuevos resultados.
11. Actualizar el conocimiento.

### Priorización inteligente

Crear un motor que considere:

- Rendimiento histórico.
- Calidad de evidencia.
- Similitud con creatividades existentes.
- Novedad del concepto.
- Relevancia del público.
- Disponibilidad de material.
- Costos de producción.
- Riesgo publicitario.
- Fatiga de creatividad.
- Margen potencial.

Las ponderaciones deben ser configurables y auditables.

No utilizar puntuaciones subjetivas de IA como si fueran predicciones fiables de ROAS.

### Memoria de aprendizaje

Guardar:

- Hipótesis.
- Experimentos asociados.
- Datos utilizados.
- Resultados.
- Nivel de confianza.
- Período.
- Recomendación.
- Aplicabilidad.
- Versión del criterio de análisis.

Los aprendizajes deben poder revisarse, corregirse o invalidarse cuando aparezcan nuevos datos.

### Modos operativos

**Manual.** La IA analiza y recomienda. El usuario decide todas las acciones.

**Semiautomático.** La IA investiga, planifica, genera y renderiza. El usuario aprueba las creatividades y las acciones
publicitarias críticas.

**Automático controlado.** La IA genera y ejecuta acciones previamente autorizadas.

Debe existir:

- Presupuesto máximo por lote.
- Presupuesto máximo publicitario.
- Límites diarios.
- Reglas de aprobación.
- Kill switch.
- Auditoría.
- Detección de anomalías.
- Control de datos insuficientes.
- Protección contra duplicaciones.
- Modo simulación.

La ejecución real debe permanecer desactivada por defecto hasta que existan permisos, límites y pruebas suficientes.

### Criterio de aceptación

A partir de los resultados de campañas anteriores, el sistema debe generar un nuevo plan de producción que priorice
hipótesis justificadas por evidencia.

Debe poder explicar por qué seleccionó determinados hooks, desarrollos y enfoques.

## 8. Experiencia de usuario unificada

No quiero tres herramientas independientes con interfaces diferentes.

Quiero una única plataforma con un flujo de trabajo integrado.

Secciones principales:

1. Dashboard.
2. Biblioteca multimedia.
3. Explorador de escenas.
4. Buscador inteligente.
5. Investigación creativa.
6. Perfiles de marca.
7. Campañas y briefs.
8. Generador de lotes.
9. Editor de composiciones.
10. Cola de renderización.
11. Creatividades generadas.
12. Meta Ads.
13. Inteligencia de rendimiento.
14. Aprendizajes.
15. Automatizaciones.
16. Costos de IA.
17. Configuración.

Cada módulo debe utilizar servicios desacoplados, pero compartir datos y trazabilidad.

La interfaz deberá implementarse con Razor Views, Bootstrap y JavaScript, utilizando AJAX y SignalR cuando corresponda.

## 9. Flujo objetivo: campaña de Navidad

El flujo final esperado será:

El usuario selecciona una marca y escribe:

> "Quiero generar 20 anuncios de Navidad orientados a mujeres que buscan regalos originales y prácticos. Priorizá
> enfoques emocionales, demostración del producto y facilidad de uso. Utilizá nuestros videos históricos y referencias
> actuales del mercado".

El sistema deberá:

1. Interpretar el brief.
2. Recuperar el perfil de marca.
3. Consultar su biblioteca de escenas.
4. Identificar creatividades históricas relevantes.
5. Consultar aprendizajes previos.
6. Investigar fuentes externas autorizadas.
7. Proponer 20 conceptos diferentes.
8. Seleccionar hooks, desarrollos y CTA.
9. Crear 20 recetas audiovisuales.
10. Evaluar disponibilidad y derechos de los recursos.
11. Renderizar los videos.
12. Ejecutar controles automáticos de calidad.
13. Mostrar los resultados y sus hipótesis.
14. Permitir su aprobación o publicación controlada.
15. Recuperar resultados de campañas.
16. Generar recomendaciones para el siguiente lote.

El sistema debe poder ejecutar los pasos de producción con mínima intervención humana.

## 10. Control de costos

La plataforma debe priorizar el costo por creatividad utilizable.

Implementar:

- Análisis de videos una sola vez cuando sea posible.
- Cache por hash y versión de modelo.
- Deduplicación audiovisual.
- Transcripción local opcional.
- Modelos económicos para tareas simples.
- Modelos más capaces para razonamiento complejo.
- Preprocesamiento con FFmpeg.
- Reutilización de escenas.
- Generación audiovisual nueva solamente cuando aporte valor.
- Presupuesto por lote.
- Registro de tokens y costos.
- Alertas de consumo.
- Estimación antes de ejecutar trabajos.

Medir no solo el costo de API, sino también:

- Tiempo de CPU y GPU.
- Almacenamiento.
- Renderización.
- Investigación externa.
- Reintentos.
- Calidad de resultados.
- Intervención humana requerida.

El objetivo es reducir el costo y tiempo de producción sin sacrificar coherencia ni calidad comercial.

## 11. Orden de implementación requerido

Organizar el desarrollo en entregas funcionales.

**Fase 0 — Fundamentos**

- Arquitectura MVC.
- Modelo de dominio.
- SQL Server.
- Identity y permisos.
- Organizaciones.
- Layout.
- Servicios.
- Background jobs.
- Auditoría.
- Documentación.

**Fase 1 — Biblioteca inteligente**

- Ingesta.
- Metadatos.
- Almacenamiento.
- Duplicados.
- PySceneDetect.
- FFmpeg.
- Transcripción.
- Clasificación IA.
- Timeline.
- Búsqueda semántica.

Este será el primer gran hito funcional.

**Fase 2 — Motor de composición**

- Taxonomía creativa.
- Selección de escenas.
- Guiones.
- Recetas.
- Combinaciones.
- Renderización.
- Generación por lotes.
- Control de calidad.

Segundo hito: generación automática de variantes usando material propio.

**Fase 3 — Investigación y estrategia**

- Brand Intelligence.
- Briefs.
- Fuentes externas.
- Investigación.
- Concepts.
- Planning Agent.
- Generación estratégica de 20 creatividades.
- Generación audiovisual opcional.

Tercer hito: producción desde un brief comercial.

**Fase 4 — Inteligencia de Meta**

- Importación histórica.
- Sincronización.
- Insights.
- Asociación de anuncios con componentes creativos.
- Dashboard.
- Análisis de rendimiento.
- Experimentos.

Implementar primero una importación básica de Meta si resulta necesario para validar las relaciones del modelo de datos
durante fases anteriores.

**Fase 5 — Aprendizaje**

- Patrones.
- Hipótesis.
- Priorización.
- Recomendaciones.
- Aprendizaje continuo.
- Nuevas generaciones basadas en resultados.

**Fase 6 — Automatización publicitaria**

- Publicación mediante Meta.
- Reglas.
- Límites.
- Aprobaciones.
- Modos de autonomía.
- Simulación.
- Protección presupuestaria.
- Auditoría.

## 12. Criterios globales de calidad

El desarrollo debe respetar:

- SOLID, DRY, KISS y YAGNI.
- Controladores delgados.
- Servicios cohesionados.
- Dependency Injection.
- DTOs y ViewModels.
- Validaciones.
- EF Core.
- Migraciones.
- Pruebas unitarias e integración.
- Idempotencia.
- Logging estructurado.
- Manejo seguro de secretos.
- Control de costos.
- Versionado de prompts y modelos.
- Control de permisos y derechos.
- Trazabilidad.
- Aislamiento multiempresa.
- Gestión de errores.
- Cancelación de trabajos.
- Documentación.

No crear interfaces visuales ficticias que no estén conectadas a servicios funcionales.

No construir todos los módulos simultáneamente.

Cada etapa debe entregar un flujo vertical ejecutable y probado.

## 13. Instrucción final para Claude Code

Tomá estas prioridades como parte de la especificación oficial de Creative Intelligence Platform.

No elimines funcionalidades previas del proyecto.

Reorganizá el roadmap para dar prioridad a:

1. Comprensión y reutilización del material existente.
2. Composición y generación automatizada de videos.
3. Investigación y estrategia creativa.
4. Análisis del rendimiento real en Meta.
5. Aprendizaje y optimización.
6. Automatización controlada de campañas.

Mantené el objetivo de una aplicación unificada desarrollada en ASP.NET Core MVC con servicios.

Antes de modificar código, inspeccioná el repositorio, identificá el estado actual y compará estas especificaciones con
lo ya implementado.

Generá:

- Matriz de requisitos.
- Roadmap actualizado.
- Dependencias entre módulos.
- Arquitectura funcional.
- Modelo de datos evolutivo.
- Criterios de aceptación.
- Plan de pruebas.
- Riesgos técnicos.
- Estrategia de control de costos.

Actualizá CLAUDE.md y la documentación correspondiente.

No reemplaces implementaciones funcionales sin justificación.

No avances a la siguiente fase hasta verificar la actual.

El objetivo final no es solamente generar publicidad. Es construir un sistema que comprenda el contenido disponible,
investigue oportunidades, produzca nuevas creatividades, aprenda de su rendimiento y mejore sus siguientes decisiones
de producción con intervención humana mínima.

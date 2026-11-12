# Estrategia de control de costos

Indicador rector: **costo por creatividad utilizable** = costo total del lote (IA + cómputo + almacenamiento +
investigación + reintentos + tiempo humano) ÷ creatividades aprobadas.

## Principios

1. **Pagar una vez.** Cada análisis se guarda con la clave `hash de contenido + modelo + versión de prompt`. La misma
   clave nunca vuelve a llamar al proveedor.
2. **Lo barato primero.** FFmpeg y Python locales antes que cualquier modelo: cortes, cuadros clave, reducción de
   resolución, silencio, hash perceptual.
3. **El modelo justo para la tarea.** Modelo económico para clasificación y etiquetado; modelo capaz sólo para
   estrategia, planificación e interpretación de briefs. Se configura por tarea.
4. **Reutilizar antes que generar.** La generación audiovisual nueva es opcional, está desactivada por defecto y sólo se
   propone cuando el concepto declara un faltante.
5. **Estimar antes de ejecutar.** Ningún trabajo costoso arranca sin estimación; ningún lote arranca si supera su
   presupuesto.

## Medidas por fase

| Fase | Medida |
|---|---|
| 0 | Registro de uso de IA y de costo por trabajo desde el primer día, aunque esté en cero |
| 1 | Deduplicación antes de analizar (un duplicado hereda el análisis); cuadros clave por escena en lugar del video completo; transcripción local opcional; análisis por lotes; embeddings sólo sobre texto ya generado; piloto de 30 videos para fijar el costo por video antes de procesar los 500 |
| 2 | Clips normalizados reutilizables entre variantes del mismo lote; render a resolución final una sola vez; estimación por lote (duración × cantidad); presupuesto y corte por lote; reintentos con tope |
| 3 | Investigación externa con tope de consultas y cache de referencias por vigencia; el plan se genera en una pasada estructurada, no concepto por concepto; generación nueva sujeta a aprobación de costo |
| 4 | Sincronización incremental (sólo días abiertos por la ventana de atribución); respeto de cuotas |
| 5 | El análisis de patrones corre sobre datos ya agregados; la IA redacta la explicación, no calcula las métricas |
| 6 | Presupuesto publicitario máximo y límites diarios como guardas duras, separados del presupuesto de generación |

## Qué se mide

| Concepto | Cómo |
|---|---|
| Tokens y costo de API | Por llamada, con tarea, modelo y versión de prompt |
| Tiempo de CPU / GPU | Duración por trabajo y tipo; tarifa configurable por hora |
| Renderización | Duración y reintentos por variante |
| Almacenamiento | Bytes por organización: originales, derivados, renders |
| Investigación externa | Consultas y costo por corrida |
| Reintentos | Intentos por trabajo y costo desperdiciado |
| Calidad | Porcentaje de variantes que pasan el control automático y de las aprobadas por el usuario |
| Intervención humana | Correcciones manuales en el timeline, ediciones de receta y rechazos por lote |

## Controles

- **Presupuesto por lote**: obligatorio; el lote se detiene al alcanzarlo y lo informa.
- **Presupuesto mensual por organización** con alertas al 50 %, 80 % y 100 %.
- **Estimación previa** visible antes de confirmar: análisis masivo, lote, investigación, generación.
- **Pantalla "Costos de IA"**: por período, tarea, modelo, lote y costo por creatividad utilizable.
- **Tarifas en configuración**, versionadas: un cambio de precio no altera los costos ya registrados.

## Lo que no se hace

- Reanalizar toda la biblioteca por un cambio menor de prompt: se reanaliza por muestra y se compara antes.
- Enviar videos completos a un modelo cuando alcanzan cuadros clave y transcripción.
- Usar el modelo más capaz "por las dudas".
- Presentar un puntaje de IA como predicción de ROAS para justificar gasto.

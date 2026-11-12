# Plan de pruebas

## Niveles

| Nivel | Qué cubre | Con qué |
|---|---|---|
| Unitarias | Reglas de dominio puras: validación de receta, similitud, puntaje de priorización, guardas de automatización | xUnit |
| Arquitectura | Sin SQL embebido, sin `var`, sin tipos anónimos en contratos, toda entidad con `OrganizationId`, controllers sin `DbContext` | Tests sobre el ensamblado y el código fuente |
| Traducción | Cada consulta LINQ nueva se traduce en SQL Server | Base de prueba |
| Integración | Servicios contra base de prueba; cola de trabajos; pipeline de medios con clips reales cortos | Base de prueba + FFmpeg |
| Contrato externo | IA y Meta contra implementaciones falsas con respuestas grabadas | Sin red |
| Aceptación | Puerta de salida de cada fase, sobre muestra real | Manual asistida, con evidencia |

Reglas:
- Sólo bases **de prueba**. Nunca producción, ni para leer.
- Los tests automáticos no llaman a proveedores pagos ni a Meta. Las corridas con proveedor real son de aceptación,
  explícitas y con costo registrado.
- Regla de negocio nueva → test que la fije. Error corregido → test que lo reproduzca.

## Datos de prueba

- `tests/media/`: 5–8 clips cortos propios (≤ 10 s) con cortes conocidos, uno sin audio, uno vertical, uno duplicado
  recodificado, uno dañado.
- Muestra real de aceptación: ≥ 30 videos de la biblioteca, fuera del repositorio.
- Anotación manual de ≥ 10 videos (cortes y función narrativa) para medir precisión.
- ≥ 15 consultas de búsqueda con sus fragmentos esperados.
- Respuestas grabadas de la Marketing API (paginación, límite de cuota, error transitorio, token vencido).

## Casos por fase

### Fase 0
- Usuario de la organización A no accede a recursos de B (lista, detalle, descarga, hub SignalR).
- Encolar dos veces el mismo trabajo crea uno solo.
- Dos Workers no toman el mismo trabajo.
- Trabajo con Worker caído vuelve a la cola al vencer el arriendo.
- Reintentos con tope; al agotarse queda fallido con error.
- Cancelación en curso detiene el trabajo y lo deja en estado consistente.
- Toda escritura deja auditoría.
- La aplicación no arranca si falta configuración obligatoria.

### Fase 1
- Carga de archivo válido, inválido, dañado y demasiado grande.
- Mismo archivo dos veces → duplicado exacto; recodificado → duplicado visual.
- El original no cambia tras todo el pipeline (hash igual).
- Escenas detectadas en el clip de cortes conocidos, dentro de la tolerancia.
- Dividir, fusionar y mover límites mantienen segmentos contiguos y sin solapamiento; no cruzan assets.
- Reanalizar conserva correcciones manuales y crea una versión nueva de análisis.
- Segundo análisis del mismo contenido con el mismo modelo no llama al proveedor.
- Búsqueda: filtros solos, semántica sola y combinados; respeta organización.
- Fragmento sin autorización aparece en la búsqueda pero no puede seleccionarse para producción.

### Fase 2
- Receta inválida (sin hook, fragmento inexistente, sin derechos, duración imposible) se rechaza con código estable.
- Render de receta mínima: duración, resolución, códec y audio esperados.
- Mezcla de orígenes (vertical + horizontal, con y sin audio, distintos fps) se normaliza.
- Variante casi idéntica a otra se rechaza por similitud.
- Re-render de una receta produce el mismo montaje.
- Lote: uno de N falla y los demás terminan; el lote informa el error.
- Lote cuya estimación supera el presupuesto no arranca.
- Control de calidad detecta video negro, sin audio y duración fuera de rango.

### Fase 3
- Mensaje prohibido por la marca bloquea el concepto.
- Referencia "sólo referencia" no puede entrar en una receta.
- Plan de 20: sin conceptos duplicados; cada uno con receta válida o con faltantes listados.
- Generación audiovisual desactivada por defecto; activada, respeta restricciones de personas y voces.

### Fase 4
- Importación paginada completa; reanudación tras corte.
- Sincronización incremental no duplica ni pierde días; reprocesar un día actualiza, no agrega.
- Límite de cuota → espera y reintento; token vencido → estado visible, sin bucle.
- Métricas derivadas (CTR, CPC, CPM, CPA, ROAS) calculadas desde las bases, con división por cero controlada.
- Linaje completo desde un anuncio hasta los assets.
- Con datos por debajo del umbral, el análisis informa "evidencia insuficiente" en lugar de un ganador.

### Fase 5
- Puntaje de priorización determinista para ponderaciones dadas; cambiar ponderaciones crea versión nueva.
- Aprendizaje invalidado deja de influir en el plan siguiente.
- El plan explica cada elección con la evidencia usada.

### Fase 6
- Sin habilitación explícita no existe llamada de escritura a Meta (test con cliente falso que falla si se la invoca).
- Simulación registra las acciones sin ejecutarlas.
- Acción que excede presupuesto o límite diario se bloquea.
- Kill switch detiene acciones pendientes y en curso.
- Repetir una acción con la misma clave no publica dos veces.
- En modo semiautomático, publicar requiere aprobación de un usuario con permiso.

## Verificación antes de cerrar cualquier cambio

1. `dotnet build -warnaserror` en 0.
2. `dotnet test` en verde.
3. Búsqueda de `var `, `new {`, `FromSql`, `ExecuteSql`, `SqlQuery`, `dynamic`, `catch { }` en lo escrito: 0.
4. Reporte fiel: qué se compiló, qué se corrió y qué no se pudo verificar.

# Riesgos técnicos

Impacto y probabilidad: Alto / Medio / Bajo.

| # | Riesgo | Prob. | Imp. | Mitigación |
|---|---|---|---|---|
| R1 | La clasificación narrativa (hook, beneficio, CTA) no alcanza la confiabilidad necesaria | Media | Alto | Muestra anotada y umbrales antes de la Fase 2; confianza por etiqueta; corrección manual en el timeline que no se pierde al reanalizar; análisis versionado para comparar modelos |
| R2 | PySceneDetect sobre-segmenta o no detecta transiciones suaves en material UGC | Media | Medio | Detectores y umbrales configurables por tipo de contenido; duración mínima de escena; fusión manual; medir sobre la muestra |
| R3 | Costo de análisis multimodal de ~500 videos mayor al esperado | Media | Alto | Preprocesar con FFmpeg (cuadros clave, baja resolución); cache por hash + versión; transcripción local; modelo económico por defecto; estimación y presupuesto previos; piloto de 30 videos con costo medido |
| R4 | Búsqueda semántica poco pertinente | Media | Alto | Conjunto de consultas de referencia; texto del embedding cuidado; combinación con filtros estructurados; reindexado barato por versión |
| R5 | Soporte vectorial de SQL Server / EF Core no disponible en la versión instalada | Media | Bajo | Alternativa en memoria ya prevista; se decide al iniciar la Fase 1 |
| R6 | Render FFmpeg frágil con fuentes heterogéneas (fps, relación de aspecto, audio ausente) | Alta | Medio | Normalizar cada clip a un formato intermedio común antes de concatenar; tests con mezcla de orígenes; validación del archivo final |
| R7 | Variantes "distintas" que en la práctica son iguales | Media | Medio | Firma de similitud por receta (fragmentos, orden, mensaje) y umbral; revisión humana en la puerta de la Fase 2 |
| R8 | Carga de trabajo del Worker satura la máquina (CPU/GPU, disco) | Media | Medio | Concurrencia configurable por tipo de trabajo; límites de disco; derivados regenerables y purgables |
| R9 | Almacenamiento local insuficiente o sin respaldo | Media | Alto | Contrato de storage que permite cambiar a almacenamiento en la nube; respaldo de originales desde el inicio |
| R10 | Permisos y revisión de app de Meta demoran o se deniegan | Media | Alto | Iniciar el trámite temprano; importación de sólo lectura primero; cuenta de prueba |
| R11 | Cuotas y cambios de versión de la Marketing API | Alta | Medio | Cliente aislado en Infrastructure, versión configurable, control de cuota y espera, sincronización incremental reanudable |
| R12 | Anuncios históricos sin vínculo con archivos de la biblioteca | Alta | Medio | Asociación asistida por hash perceptual y revisión manual; origen y confianza registrados en el vínculo |
| R13 | Conclusiones de rendimiento estadísticamente débiles | Alta | Alto | Umbrales mínimos de datos; niveles observación / correlación / causal; segmentar por factores; experimentos; nunca "ganador" por un resultado aislado |
| R14 | Automatización que gasta presupuesto real por error | Baja | Muy alto | Ejecución real desactivada por defecto; simulación; límites duros; aprobaciones; kill switch; idempotencia de acciones; auditoría |
| R15 | Uso de material sin derechos (personas, voces, música, terceros) | Media | Alto | Derechos como dato obligatorio; bloqueo en la validación de receta; referencias externas separadas del material utilizable |
| R16 | Fuga de datos entre organizaciones | Baja | Muy alto | Filtro global, test de arquitectura sobre el modelo, tests de acceso cruzado, grupos de SignalR por organización |
| R17 | Dependencia de un proveedor de IA (precio, disponibilidad, cambios de modelo) | Media | Medio | Contratos por capacidad; modelo por tarea en configuración; versión registrada en cada resultado |
| R18 | Fuentes externas de investigación con términos de uso restrictivos | Media | Medio | Sólo fuentes autorizadas y configuradas; guardar procedencia y permisos; no descargar ni reproducir |
| R19 | Alcance demasiado amplio: construir módulos en paralelo | Media | Alto | Puertas de fase; flujo vertical por paso; sin pantallas sin servicio |
| R20 | Integración Python ↔ .NET frágil (entorno, versiones, rutas) | Media | Medio | Scripts con contrato JSON y versión fijada de dependencias; verificación de herramientas al arrancar el Worker; errores de subproceso capturados |

## Supuestos a confirmar

- SQL Server disponible para desarrollo (instancia, versión, autenticación).
- Dónde se alojan los ~500 videos y su tamaño total.
- Proveedores de IA a usar y presupuesto mensual.
- Si habrá GPU para transcripción local.
- Umbrales de aceptación de la Fase 1 (propuestos en el roadmap).

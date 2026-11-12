# Creative Intelligence Platform — Documentación

Especificación oficial derivada de la **Directiva de producto, prioridades y arquitectura funcional** (2026-11-11).

## Estado del repositorio al 2026-11-11

Inspección previa a cualquier cambio:

- La carpeta del proyecto estaba **vacía**: sin solución, sin código, sin `CLAUDE.md`, sin repositorio git.
- La directiva menciona un "prompt maestro de desarrollo" previo; el usuario confirmó que **no existe**: la directiva
  es la única especificación de origen.
- No hay funcionalidades previas que preservar ni implementaciones que comparar: **todos los requisitos están en estado
  "No iniciado"** y la directiva es la línea base.
- Entorno: .NET SDK 10.0.400 y Python 3.14 instalados. **FFmpeg no está instalado** (prerrequisito de la Fase 1).

Desde ese mismo día existe la Fase 0 (fundamentos) implementada y verificada; el detalle está en el roadmap.

## Índice

| Documento | Contenido |
|---|---|
| [00-directiva-producto.md](00-directiva-producto.md) | Texto original de la directiva; manda ante cualquier diferencia |
| [01-matriz-requisitos.md](01-matriz-requisitos.md) | Requisitos con ID, fase, módulo y forma de verificación |
| [02-roadmap.md](02-roadmap.md) | Fases 0–6, entregables, criterios de aceptación y puertas de avance |
| [03-arquitectura-funcional.md](03-arquitectura-funcional.md) | Capas, módulos, dependencias entre módulos, trabajos en segundo plano |
| [04-modelo-datos.md](04-modelo-datos.md) | Modelo de datos evolutivo por fase y trazabilidad (Creative Lineage) |
| [05-plan-pruebas.md](05-plan-pruebas.md) | Niveles de prueba, casos por fase y datos de prueba |
| [06-riesgos-tecnicos.md](06-riesgos-tecnicos.md) | Riesgos, impacto y mitigación |
| [07-control-costos.md](07-control-costos.md) | Estrategia de control y medición de costos |

## Principios que no se negocian

1. Plataforma propia e independiente. Recharm, raw2ads y Creatify son **referencias funcionales**: no son dependencias y
   no se copia su código, diseño ni propiedad intelectual.
2. Una sola aplicación ASP.NET Core MVC (Razor + Bootstrap 5 + jQuery/AJAX + SignalR), en **un solo proyecto**
   desde el 2026-11-11. Sin React, sin SPA, sin microservicios innecesarios.
3. Todo trabajo intensivo corre fuera del ciclo HTTP, y es auditable, recuperable e idempotente.
4. Cada fase entrega un flujo vertical ejecutable y probado. **No se avanza de fase sin verificar la actual.**
5. Sin interfaces visuales ficticias: una pantalla existe sólo si está conectada a un servicio funcional.
6. La ejecución publicitaria real está **desactivada por defecto**.

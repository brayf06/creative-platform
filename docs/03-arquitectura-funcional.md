# Arquitectura funcional

> **Actualización del 2026-11-11 — un solo proyecto MVC.** Por decisión del usuario, los cinco proyectos por capas
> que describe este documento (Domain, Application, Infrastructure, Web, Worker) se unieron en uno:
> `src/CreativeIntelligence.Web`, con las mismas responsabilidades separadas por carpetas (`Controllers`,
> `Models/Entidades`, `Models/Dto`, `Models/ViewModels`, `Data`, `Services/<Área>`). Además, **los trabajos en
> segundo plano corren dentro del proceso de la Web** como servicios alojados; ya no hay un Worker aparte.
>
> Sigue vigente todo lo demás: los módulos y sus dependencias, la cola de trabajos (que conserva el arriendo y la
> guarda de concurrencia), los contratos de IA, la búsqueda, el almacenamiento y la seguridad. Donde el texto dice
> "Worker", hoy es la cola de trabajos dentro de la Web; donde dice un proyecto, hoy es una carpeta. La estructura
> al día está en `CLAUDE.md`.
>
> Qué se ganó y qué se cedió: se arranca y se despliega una sola cosa, y desaparece el problema de orden de
> arranque entre procesos. A cambio, un trabajo pesado (FFmpeg, Whisper) compite por CPU con el sitio, y reiniciar
> la Web corta los trabajos en curso hasta que vence su arriendo y se retoman.

## Vista general

Una sola aplicación, dos procesos que comparten código y base de datos:

```
Navegador (Razor + Bootstrap 5 + jQuery/AJAX + SignalR)
        │
        ▼
┌──────────────────────────┐        ┌───────────────────────────┐
│ Web (ASP.NET Core MVC)   │        │ Worker (BackgroundService)│
│ Controllers finos, Views │        │ toma trabajos de la cola  │
│ Hub SignalR de progreso  │        │ FFmpeg / Python / IA      │
└──────────┬───────────────┘        └───────────┬───────────────┘
           │        Application (servicios por área)            │
           │        Domain (entidades, reglas)                  │
           │        Infrastructure (EF Core, storage, IA, Meta) │
           └───────────────────┬────────────────────────────────┘
                               ▼
                 SQL Server            Almacenamiento de medios
```

- **Web** administra y orquesta: valida, persiste y **encola**. Nunca procesa video ni llama a modelos dentro de una
  solicitud HTTP.
- **Worker** es un proceso aparte (no un microservicio: mismo código, misma base, mismo despliegue) para que FFmpeg y
  el análisis no compitan con el sitio.
- **Python** no es un servicio: son scripts de línea de comandos (`tools/media/`) que el Worker ejecuta como
  subproceso, con entrada por argumentos y salida JSON.

## Solución propuesta

| Proyecto | Responsabilidad |
|---|---|
| `CreativeIntelligence.Domain` | Entidades, enumeraciones, reglas puras (validación de receta, similitud, puntaje de priorización) |
| `CreativeIntelligence.Application` | `AppDbContext` y configuraciones EF, servicios por área, DTOs, contratos de infraestructura (storage, IA, Meta, herramientas de medios) |
| `CreativeIntelligence.Infrastructure` | Proveedor SQL Server, migraciones, inicialización de la base, storage, FFmpeg/Python, proveedores de IA, cliente de Meta |
| `CreativeIntelligence.Web` | MVC, ViewModels, Razor, JS, hub SignalR, Identity |
| `CreativeIntelligence.Worker` | Bucle de la cola y manejadores de trabajos |
| `CreativeIntelligence.Tests` | Unitarias, arquitectura, integración |
| `tools/media/` | Scripts Python (PySceneDetect, hash perceptual, transcripción local) |

El `AppDbContext` está en Application porque los servicios consultan con EF Core directamente, sin repositorios: EF es
la abstracción de persistencia. Infrastructure sólo aporta el proveedor concreto y las migraciones.

Las interfaces se reservan para lo que **realmente** tiene más de una implementación o es un límite externo (IA,
storage, Meta, herramientas de medios). Los servicios de aplicación se inyectan como clases concretas.

## Módulos

| Módulo | Fase | Qué hace | Secciones de UI |
|---|---|---|---|
| Plataforma / Seguridad | 0 | Identity, organizaciones, permisos, configuración | Configuración |
| Trabajos | 0 | Cola, progreso, reintentos, cancelación | (transversal), Cola de renderización |
| Auditoría | 0 | Registro de operaciones | Configuración |
| IA | 0–1 | Contratos de proveedores, cache, versiones de prompt/modelo, uso | Costos de IA |
| Ingesta | 1 | Carga, hash, metadatos, duplicados, miniaturas, derechos | Biblioteca multimedia |
| Escenas | 1 | Segmentos visuales, repetidos | Explorador de escenas |
| Análisis | 1 | Transcripción, documentación, segmentos narrativos, embeddings | Explorador de escenas |
| Búsqueda | 1 | Semántica + filtros | Buscador inteligente |
| Timeline | 1 | Edición de segmentos y selección | Explorador de escenas |
| Composición | 2 | Taxonomía, recetas, variantes, similitud | Editor de composiciones |
| Render | 2 | Pipeline FFmpeg | Cola de renderización |
| Calidad | 2–3 | Validación técnica, de mensajes y de derechos | Creatividades generadas |
| Lotes | 2 | Lotes de 5–50 | Generador de lotes |
| Marca | 3 | Brand Intelligence | Perfiles de marca |
| Briefs | 3 | Briefs de campaña | Campañas y briefs |
| Investigación | 3 | Fuentes y referencias externas | Investigación creativa |
| Estrategia | 3 | Conceptos y planificación | Campañas y briefs |
| Generación | 3 | Recursos audiovisuales nuevos (opcional) | Editor de composiciones |
| Meta | 4 | Importación y sincronización | Meta Ads |
| Trazabilidad | 1–4 | Creative Lineage | Creatividades generadas, Meta Ads |
| Rendimiento / Experimentos | 4 | Análisis por componente | Inteligencia de rendimiento |
| Aprendizaje / Priorización | 5 | Memoria, hipótesis, puntajes | Aprendizajes |
| Automatización | 6 | Modos, límites, publicación | Automatizaciones |
| Costos | 1–5 | Medición y presupuestos | Costos de IA, Dashboard |

El **Dashboard** agrega lo que exista en cada fase; no muestra indicadores sin datos reales.

## Dependencias entre módulos

```
Plataforma ─► Trabajos ─► Ingesta ─► Escenas ─► Análisis ─► Búsqueda
     │           │                       │          │           │
     └► IA ──────┴───────────────────────┴──────────┘           │
                                         Timeline ◄─────────────┤
                                            │                   │
                                            ▼                   ▼
                              Composición ─► Render ─► Calidad ─► Lotes
                                   ▲                              │
        Marca ─► Briefs ─► Estrategia ◄─ Investigación            │
                               ▲                                  ▼
                               │                          Trazabilidad ◄─ Meta
                               │                                  │
                     Aprendizaje / Priorización ◄─ Rendimiento ◄──┘
                               │
                               ▼
                         Automatización ─► Meta (escritura)
```

Reglas:
- Las dependencias van en un solo sentido. Composición no conoce Meta; Rendimiento lee Trazabilidad, no Render.
- Costos y Auditoría son transversales: todos escriben en ellos, ninguno depende de su lectura.
- Un módulo posterior usa los **servicios** del anterior, no sus tablas directamente.

## Trabajos en segundo plano

Cola persistida en SQL Server (tabla propia, sin dependencias adicionales):

- **Toma de trabajo** con guarda de concurrencia: se actualiza el estado `Pendiente → EnCurso` con el estado de origen
  en la condición; si no afectó filas, lo tomó otro Worker.
- **Idempotencia**: cada trabajo tiene una clave única (`tipo + recurso + versión`); encolar dos veces no duplica.
  Los manejadores verifican si el resultado ya existe antes de trabajar.
- **Recuperación**: arriendo con vencimiento; un trabajo cuyo Worker murió vuelve a la cola. Reintentos con espera
  creciente y tope; al agotarlos queda `Fallido` con el error registrado.
- **Cancelación**: bandera en la fila, consultada entre pasos y propagada como `CancellationToken` a los subprocesos.
- **Progreso**: el manejador persiste porcentaje y paso. Como el Worker es otro proceso, la Web tiene un servicio
  (`JobProgressNotifier`) que cada segundo consulta qué trabajos cambiaron desde la última marca de agua (hora de la
  base) y lo publica por SignalR al grupo de la organización. No hay canal directo Worker → Web que mantener.
- **Orden de arranque**: la Web crea y migra la base; el Worker no migra. La primera vez hay que arrancar la Web antes.
- **Encadenamiento**: un pipeline es una secuencia de trabajos pequeños (probar → miniaturas → escenas → transcribir →
  analizar → embeddings), no un trabajo monolítico; cada paso es reanudable.

## Proveedores de IA

Contratos por capacidad, no por proveedor: análisis multimodal de fragmentos, razonamiento/planificación en texto,
embeddings, transcripción, generación audiovisual. La configuración asigna proveedor y modelo **por tarea**, lo que
permite modelos económicos para clasificación simple y más capaces para estrategia.

Cada llamada registra: tarea, proveedor, modelo, versión de prompt, tokens, costo, duración, trabajo y organización. El
resultado se guarda con una clave `hash de entrada + modelo + versión de prompt`; la misma clave no se vuelve a pagar.

## Búsqueda semántica

- Un embedding por fragmento a partir de su documentación (descripción, transcripción, texto en pantalla, etiquetas).
- Opción principal: tipo vectorial nativo de SQL Server 2025 con EF Core 10. **A verificar al iniciar la Fase 1**
  contra la versión de SQL Server disponible.
- Alternativa sin cambios de infraestructura: similitud coseno en memoria. Con ~500 videos (del orden de miles de
  fragmentos por organización) es suficiente y simple.
- Los filtros tradicionales se aplican en la consulta; la consulta en lenguaje natural puede descomponerse en filtros
  (duración < 4 s, función = hook) más el texto semántico restante.

## Almacenamiento de medios

Contrato único con implementación inicial en sistema de archivos local. Originales direccionados por SHA-256 e
**inmutables**; derivados (miniaturas, proxies, clips, renders) en rutas separadas y regenerables.

## Seguridad y multiempresa

- Toda entidad de negocio lleva `OrganizationId`; filtro global de EF, más un test que recorre el modelo y falla si una
  entidad no lo tiene.
- Permisos por acción (ver, editar, aprobar, publicar, administrar automatización).
- Tokens de Meta y claves de proveedores cifrados en reposo; nunca en logs ni en respuestas.
- Derechos: un fragmento sin autorización de reutilización puede verse y buscarse, pero no entrar en una receta.

# Credenciales de desarrollo

Sólo valores **de desarrollo**. Los de producción no van acá ni en el repositorio: se configuran con
`appsettings.Production.json` fuera del repositorio o con variables de entorno (`Seccion__Clave`).

Este archivo se actualiza en el mismo cambio que crea un usuario, una base o una clave.

## Base de datos

| Uso | Instancia | Base | Autenticación | Dónde se configura |
|---|---|---|---|---|
| Desarrollo | `(localdb)\MSSQLLocalDB` | `CreativeIntelligence_Dev` | Windows (sin usuario ni contraseña) | `ConnectionStrings:Default` en `src/CreativeIntelligence.Web/appsettings.json` |
| Tests | `(localdb)\MSSQLLocalDB` | `CreativeIntelligence_Test` | Windows | Fija en `tests/CreativeIntelligence.Tests/DatabaseFixture.cs`; se borra y recrea en cada corrida |

La instancia principal de SQL Server de la máquina (`MSSQLSERVER`) **no se usa**.

## Usuarios de la aplicación

| Email | Contraseña | Rol | Organización | De dónde sale |
|---|---|---|---|---|
| `admin@ciplatform.local` | `Desarrollo.2026` | Administrador de plataforma + Admin | Organización Demo, Segunda Org | Arranque de la Web: sección `InitialUser` de `appsettings.json` (`DatabaseInitializer`) |
| `lector@ciplatform.local` | `Lector.2026` | Viewer | Organización Demo | Alta manual desde Configuración → Miembros, durante la verificación de la Fase 0 |

La organización "Segunda Org" se creó a mano durante la verificación de la Fase 0, para probar el aislamiento.

Los usuarios que crean los tests (`…@test.local`, contraseña `Prueba.2026`) viven sólo en la base de tests.

## Claves

| Clave | Para qué | Dónde se configura | Estado |
|---|---|---|---|
| Gemini API | Análisis con IA de las escenas y búsqueda por significado | Variable de entorno `Ai__Gemini__ApiKey`. **No se escribe en este archivo ni en `appsettings.json`**: es una clave real que factura | Sin configurar |

Todavía no hay claves de Meta.

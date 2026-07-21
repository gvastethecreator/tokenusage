# Preparación de M1 en este equipo

Fecha: 2026-07-21

Estado: lista para ejecutar después de aprobar el tracker y los tickets

## Pregunta

¿Este equipo puede crear, compilar y lanzar el primer scaffold WinUI 3 empaquetado sin instalar o cambiar herramientas antes de M1?

## Respuesta

Sí. Están presentes .NET 10, las plantillas WinUI, `winapp`, Developer Mode y Visual Studio con MSBuild. M1 puede comenzar con `winui-mvvm`, una build `x64` y lanzamiento con identidad de paquete.

## Fuentes

- [Inicio de WinUI 3 por CLI](https://learn.microsoft.com/en-us/windows/apps/winui/winui3/create-your-first-winui3-app)
- [Rutas de inicio de WinUI](https://learn.microsoft.com/en-us/windows/apps/get-started/winui-get-started-overview)
- [Windows App SDK y sus canales](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/)
- [Versiones de Windows App SDK](https://learn.microsoft.com/en-us/windows/apps/get-started/versioning-overview)
- [Uso de `winapp` con .NET](https://learn.microsoft.com/en-us/windows/apps/dev-tools/winapp-cli/guides/dotnet)

## Evidencia local

| Comprobación | Resultado observado |
|---|---|
| `.NET SDK` | `10.0.301` |
| plantilla | `winui-mvvm` disponible |
| `winapp` | `0.4.0` |
| Developer Mode | habilitado |
| Visual Studio | Community 18 con MSBuild detectado |
| arquitectura inicial | `x64` |

No se instaló ni actualizó ninguna herramienta durante la comprobación.

## Decisiones para M1

- Crear la app con `dotnet new winui-mvvm -n WOpenUsage.App`.
- Conservar `Package.appxmanifest` y la identidad de paquete.
- Usar el canal estable de Windows App SDK que resuelva la plantilla; no fijar Preview o Experimental.
- Construir y lanzar con `BuildAndRun.ps1` o la ruta de paquete de `winapp`; nunca abrir el ejecutable empaquetado de forma directa.
- Compilar por arquitectura concreta. La primera prueba es `x64`; `ARM64` se valida antes de estable.
- Agregar paquetes sin versión manual y comprobar restore al añadir cada dependencia.

## Incertidumbre

- La presencia de las herramientas no prueba que un scaffold nuevo compile; ese es el primer criterio de aceptación de M1.
- La firma y el Publisher ID de producción siguen siendo decisiones humanas. Una identidad de desarrollo basta para el smoke local.
- La plantilla puede resolver una versión estable más nueva que la citada hoy. El `.csproj` creado quedará como baseline reproducible del repo.

## Cambio en el plan

M1 ya no necesita una tarea de instalación. Empieza con scaffold, build y launch. Si cualquiera de esos pasos falla, se registra el error exacto y se detiene antes de agregar arquitectura o proveedores.

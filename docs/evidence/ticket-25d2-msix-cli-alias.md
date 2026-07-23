# Ticket 25D2: alias CLI dentro del MSIX

Fecha: 2026-07-23

## Resultado

TokenUsage usa un Windows Application Packaging Project que incluye dos
ejecutables de confianza plena:

- `WOpenUsage.App\WOpenUsage.App.exe`, entrada WinUI;
- `WOpenUsage.Cli\wusage.exe`, consola registrada como alias `wusage.exe`.

El paquete conserva identidad, App ID, recursos `en-US`/`es-ES`, assets,
`runFullTrust` y dependencia de Windows App SDK. La app dejó de ser dueña del
MSIX de proyecto único. El CLI conserva namespace y contratos JSON; solo cambia
el nombre de su binario.

## Proyecto y manifest

`WOpenUsage.Package.wapproj` referencia App y CLI, admite solo x64/ARM64 y usa
el proyecto App como entry point. El manifest declara
`windows.appExecutionAlias` con `Windows.FullTrustApplication` y apunta a la
ruta real del payload `WOpenUsage.Cli\wusage.exe`.

`Directory.Build.props` declara los RID `win-x64` y `win-arm64` para que WAP
pueda publicar el cierre completo de proyectos. Release desactiva trimming: el
publish WAP encontró límites JSON y WinRT sin anotaciones seguras. ReadyToRun se
mantiene activo.

`BuildAndRun.ps1` redirige la ruta histórica del proyecto App al packaging
project. `scripts/check.ps1` usa Visual Studio MSBuild para la solución y el
paquete; `dotnet` sigue ejecutando las suites.

## Prueba de paquete

- Debug x64: paquete generado.
- Debug ARM64: paquete generado.
- Release x64: paquete generado sin avisos ni errores.
- Ambos paquetes contienen `WOpenUsage.App.exe`, `wusage.exe`, `resources.pri`
  y `AppxManifest.xml`.
- El manifest generado conserva identidad `D6C94EDD-...`, arquitectura correcta,
  App ID `App` y el alias hacia `WOpenUsage.Cli\wusage.exe`.
- `BuildAndRun.ps1 ...WOpenUsage.App.csproj -SkipRun` construye mediante WAP.

El registro dev Debug no pudo iniciarse porque el host no tiene
`Microsoft.VCLibs.140.00.Debug.UWPDesktop`; no se instaló la dependencia. El
layout Release se registró con `winapp run`, inició la app por AUMID y el proceso
siguió vivo tras cinco segundos. Windows resolvió
`Microsoft\WindowsApps\wusage.exe`; `wusage providers --format json` devolvió
código 0 y esquema `wusage.providers.v1`. La prueba cerró el proceso creado y
retiró el registro dev.

## Gate final x64 Release

`scripts/check.ps1 -Platform x64 -Configuration Release`:

- Architecture: 62/62;
- Core: 71/71;
- CLI: 82/82;
- Providers: 170/170;
- Platform Windows: 58/58;
- solución y paquete: correctos.

## Revisión

Grok Build produjo el plan aceptado del corte; coste informado: USD 0.097612.
Su revisión amplia agotó 10 turnos y mezcló `ACCEPT` con hallazgos
contradictorios; se descartó como veredicto, coste informado: USD 0.4147012.

La revisión independiente detectó que `AppPackages/` podía entrar en un commit.
El ignore raíz ahora cubre carpetas y extensiones de paquete. Tras la corrección,
el revisor devolvió `ACCEPT`, sin P0-P2. La CLI publicada incluye .NET 10 en su
payload; no depende de un `hostfxr` externo.

## Límite pendiente

Este corte prueba un registro de desarrollo Release. Firma de producción,
timestamp, Store, upgrade y máquina limpia pertenecen al gate de publicación.

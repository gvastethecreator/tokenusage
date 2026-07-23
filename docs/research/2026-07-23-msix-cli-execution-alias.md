# Alias CLI dentro del paquete MSIX

Fecha de corte: 2026-07-23

## Pregunta

¿Puede el MSIX actual de proyecto único publicar `wusage.exe` como una CLI real
sin convertir la app WinUI en un proceso de consola?

## Respuesta

No con el modelo actual. El MSIX de proyecto único admite un solo ejecutable.
TokenUsage necesita dos: `WOpenUsage.App.exe` para WinUI y `wusage.exe` para
stdout, stderr y códigos de salida. El corte debe migrar el empaquetado a un
Windows Application Packaging Project y mantener ambos proyectos de aplicación
como referencias x64/ARM64.

El alias debe usar `windows.appExecutionAlias`, apuntar de forma explícita a
`wusage.exe`, declarar `Windows.FullTrustApplication` y registrar
`wusage.exe`. No se debe dirigir el alias al ejecutable WinUI: un `WinExe` no
cumple el contrato de consola.

## Fuentes primarias

| Fuente | Hecho usado |
|---|---|
| [Single-project MSIX](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/single-project-msix) | El modelo de proyecto único admite un solo ejecutable; varios ejecutables requieren Windows Application Packaging Project. |
| [Packaging extensions](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/desktop-to-uwp-extensions) | `windows.appExecutionAlias` acepta `Executable`, `EntryPoint="Windows.FullTrustApplication"` y un alias terminado en `.exe`. |
| [Packaging project](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-packaging-dot-net) | Un Windows Application Packaging Project puede incluir varias apps de escritorio; las plataformas de las referencias deben coincidir. |
| [Windows App SDK packaged deployment](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/deploy-packaged-apps) | Un `.wapproj` separado debe declarar la referencia de paquete de Windows App SDK que genera la dependencia del framework. |

## Estado local

- `Package.appxmanifest` actual pertenece a `WOpenUsage.App` y no declara alias.
- `WOpenUsage.App` usa single-project MSIX.
- `WOpenUsage.Cli` es un `Exe` separado, pero su ensamblado aún se llama
  `WOpenUsage.Cli`.
- Visual Studio Community 18 está instalado y contiene
  `Microsoft.DesktopBridge.props` y MSBuild x64. El gate técnico para crear un
  `.wapproj` está disponible.

## Decisión de implementación

1. `25D1`: probar lecturas app/CLI concurrentes sobre caché y SQLite, incluidos
   writer activo, cancelación y archivos intactos.
2. `25D2`: crear el packaging project, mover allí el manifest, incluir app y CLI,
   y registrar `wusage.exe`.
3. Validar el manifest y construir x64/ARM64. Instalar y ejecutar el alias solo
   mediante paquete firmado o identidad de desarrollo; nunca iniciar el
   ejecutable empaquetado por ruta directa.

## Riesgo pendiente

La migración cambia el dueño del manifest y del package graph. Debe conservar
identidad, recursos, `runFullTrust`, localización, StartupTask y el flujo de
`BuildAndRun.ps1`. Un build de la solución por sí solo no prueba que Windows
registró el alias; el smoke empaquetado sigue siendo un gate separado.

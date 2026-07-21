# Ticket 03: WinUI scaffold evidence

Fecha: 2026-07-21

Estado: verificado

## Entrega

El repo contiene un scaffold WinUI 3 empaquetado en `src/WOpenUsage.App`, generado con:

```powershell
dotnet new winui-mvvm -n WOpenUsage.App -o src/WOpenUsage.App
```

La plantilla resolvió `net10.0-windows10.0.26100.0`, Windows App SDK `2.3.1`, BuildTools `10.0.28000.2270`, BuildTools.WinApp `0.5.0` y CommunityToolkit.Mvvm `8.4.2`.

## Delegación Grok Build

Grok Build inspeccionó el scaffold y propuso documentación. El worker no pudo escribir: el wrapper declaró globs relativos, mientras las tool calls de Windows pidieron rutas absolutas y `dontAsk` falló cerrado. Los resultados durables están bajo `.scratch/agent-cli-delegation/grok-build/runs/`.

Reconciliación del review:

- Aceptado con correcciones: documentar ubicación, versiones y comandos.
- Rechazado: cambiar `.gitignore` para perfiles `pubxml`; la plantilla no generó esos perfiles.
- Corregido por el parent review: capability `systemAIModels` sin uso, targets `x86`/`Windows.Universal`, namespace `WOpenUsage_App` y botones sin nombre estable de automatización.

El CLI reportó USD 0.3401052 para los turnos de Grok usados en este ticket. Es un valor del resultado local, no una conciliación de facturación.

## Revisión WinUI

- El manifiesto conserva identidad empaquetada y `runFullTrust`.
- El manifiesto apunta solo a `Windows.Desktop` y elimina `systemAIModels`.
- El proyecto admite `x64` y `ARM64`; no contiene `AnyCPU` ni `WindowsPackageType=None`.
- Los namespaces usan `WOpenUsage.App` de forma uniforme.
- Los dos botones de muestra tienen `AutomationProperties.Name` y `AutomationId`; sus iconos quedan fuera del árbol de accesibilidad.
- `BuildAndRun.ps1` coincide línea por línea con el script de `winui-dev-workflow`. Su hash difiere por finales de línea del checkout.
- La build con `Microsoft.WindowsAppSDK.Analyzers` habilitado pasó sin diagnósticos.

## Prueba local

Build empaquetada:

```powershell
.\BuildAndRun.ps1 src\WOpenUsage.App\WOpenUsage.App.csproj -SkipRun /p:Platform=x64
```

Resultado: `BUILD SUCCEEDED`; salida `bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64/WOpenUsage.App.dll`.

Build y launch:

```powershell
.\BuildAndRun.ps1 src\WOpenUsage.App\WOpenUsage.App.csproj -Detach /p:Platform=x64
```

Resultado: `BUILD SUCCEEDED`; `winapp` lanzó `D6C94EDD-3747-465C-9A81-05DF5A4108C5_1z32rh13vfry6!App` con PID `22300`. El proceso `WOpenUsage.App` respondió y luego se cerró al terminar la prueba.

## Límites

- La identidad y los assets siguen siendo valores de desarrollo hasta cerrar el Ticket 02.
- La UI visible sigue siendo la pantalla de muestra de la plantilla. Ticket 01 bloquea su dirección visual y Ticket 05 abre el flyout real.
- Esta prueba cubre `x64` en el equipo actual. ARM64 tiene un gate posterior.

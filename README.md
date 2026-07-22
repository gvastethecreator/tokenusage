# WOpenUsage

`WOpenUsage` es el nombre interno de una app Windows que muestra cuotas, reinicios, tokens y gasto local de herramientas de IA desde las sesiones que ya existen en el equipo.

El producto final tendrá nombre, logo y paquete propios. OpenUsage permite reutilizar su código bajo MIT, pero reserva su nombre, logo e identidad visual. Este repo no representa al proyecto OpenUsage.

## Estado

Investigación, plan, scaffold WinUI y límites de arquitectura listos. La app empaquetada vive en `src/WOpenUsage.App`; la solución completa compila para `x64` y `ARM64`.

- Upstream estudiado: `robinebers/openusage` en `9d2bf09f10e21f769494a525a9d65c84d7aeb1df`.
- Referencias de gasto local: `getagentseal/codeburn@6e3c57a9ff95a624f1d9affa7384d32a67f359b7` y `kenn-io/agentsview@1ee2de88e2dae54326d8b47aeb2de2f58b5944f9`.
- Clones locales ignorados: `.reference/openusage`, `.reference/codeburn` y `.reference/agentsview`.
- Plataforma elegida: C#, WinUI 3 y Windows App SDK, con paquete MSIX de confianza plena.
- Primer proveedor: Codex mediante su `app-server` oficial por `stdio`.
- Claude: uso de logs locales viable; cuota restante bloqueada para distribución hasta contar con un contrato público o permiso del proveedor.
- Grok Build y OpenCode: tokens y gasto local dentro de la beta; la cuota Grok queda sujeta a una interfaz pública o permiso.
- Antigravity CLI: solo lectura pasiva de datos locales en fase experimental; la app no usa su login ni consulta servicios privados.

## Scaffold WinUI

El proyecto se generó con:

```powershell
dotnet new winui-mvvm -n WOpenUsage.App -o src/WOpenUsage.App
```

Versiones resueltas por la plantilla:

| Componente | Versión |
|---|---|
| Target framework | `net10.0-windows10.0.26100.0` |
| Microsoft.WindowsAppSDK | `2.3.1` |
| Microsoft.Windows.SDK.BuildTools | `10.0.28000.2270` |
| Microsoft.Windows.SDK.BuildTools.WinApp | `0.5.0` |
| CommunityToolkit.Mvvm | `8.4.2` |

Build `x64` desde la raíz:

```powershell
.\BuildAndRun.ps1 src\WOpenUsage.App\WOpenUsage.App.csproj -SkipRun /p:Platform=x64
```

Build y launch con identidad de paquete:

```powershell
.\BuildAndRun.ps1 src\WOpenUsage.App\WOpenUsage.App.csproj -Detach /p:Platform=x64
```

El script usa `winapp` para abrir la app empaquetada. El ejecutable generado no se inicia de forma directa. La evidencia está en [Ticket 03](docs/evidence/ticket-03-winui-scaffold.md).

## Solución y arquitectura

`WOpenUsage.slnx` contiene seis proyectos:

- `WOpenUsage.Core`: contratos y dominio portables; no depende de Windows, UI ni providers.
- `WOpenUsage.Providers`: adaptadores de proveedores; depende solo de Core.
- `WOpenUsage.Platform.Windows`: servicios del sistema; depende solo de Core.
- `WOpenUsage.App`: composición y UI WinUI; depende de Core, Providers y Platform.Windows.
- `WOpenUsage.Cli`: composición de consola; depende de Core, Providers y Platform.Windows.
- `WOpenUsage.Architecture.Tests`: comprueba el grafo desde los archivos de proyecto.

Control completo para `x64`:

```powershell
.\scripts\check.ps1 -Platform x64
```

Compilación cruzada para `ARM64`, con las reglas de arquitectura ejecutadas en el host `x64`:

```powershell
.\scripts\check.ps1 -Platform ARM64
```

El control rechaza `AnyCPU` y `x86`. La evidencia está en [Ticket 04](docs/evidence/ticket-04-architecture.md).

## Documentos

- [Investigación de viabilidad](docs/research/2026-07-21-openusage-windows-feasibility.md)
- [Investigación de Grok, Antigravity, OpenCode y gasto local](docs/research/2026-07-21-agent-costs-and-quotas.md)
- [Gate de cuota Z.ai](docs/research/2026-07-21-zai-gate.md)
- [Preparación del primer scaffold WinUI](docs/research/2026-07-21-winui-m1-readiness.md)
- [Evidencia del Ticket 03](docs/evidence/ticket-03-winui-scaffold.md)
- [Evidencia del Ticket 04](docs/evidence/ticket-04-architecture.md)
- [Especificación de producto](docs/PRODUCT-SPEC.md)
- [Arquitectura base](docs/architecture/ADR-0001-windows-native-baseline.md)
- [Matriz de proveedores](docs/PROVIDER-MATRIX.md)
- [Plan de implementación](docs/IMPLEMENTATION-PLAN.md)

## Principios

- La app no tendrá una cuenta propia.
- Cada proveedor debe tener una fuente local o pública comprobada.
- La app no copiará tokens a su propio almacén.
- El motor de gasto será propio y pequeño: no indexará transcripciones, herramientas ni comandos.
- Las claves que el usuario agregue de forma manual irán a Windows Credential Locker.
- La API local y la telemetría estarán apagadas al instalar.
- La UI mostrará cuándo un dato es remoto, local, estimado, incompleto o vencido.

## Inicio de la implementación

Los Tickets 03 y 04 dejaron una app empaquetada y una solución con límites probados. El Ticket 05 queda bloqueado por la elección visual del Ticket 01. El orden, las pruebas y los criterios de salida están en el [plan](docs/IMPLEMENTATION-PLAN.md).

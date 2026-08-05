# Ticket 04: architecture boundary evidence

Fecha: 2026-07-21

Estado: verificado

## Entrega

`TokenUsage.slnx` agrupa App, Core, Providers, Platform.Windows, CLI y los tests de arquitectura. `Directory.Build.props` exige `x64` o `ARM64`, activa los analizadores de .NET y trata cada aviso como error.

El grafo permitido queda codificado en `ArchitectureRules`:

- Core no tiene referencias de proyecto ni paquetes de UI o Windows App SDK.
- Providers y Platform.Windows dependen solo de Core.
- App y CLI componen Core, Providers y Platform.Windows.
- Un proyecto nuevo, un proyecto esperado ausente o una arista fuera de la lista produce un fallo.

## Delegación Grok Build

Grok Build produjo un diseño de parche sin editar el checkout. El resultado durable está en `.scratch/agent-cli-delegation/grok-build/runs/2026-07-21T23-55-46-682Z-plan-2a103ef7/result.json`.

El run terminó con `EndTurn`, sesión `14fe689f-50a1-4d18-b957-a099f583dd01`, seis turnos y un coste reportado de USD 0.2121948. El valor proviene del CLI local y no prueba la factura del proveedor.

Reconciliación del review:

- Aceptado: separación de proyectos, grafo de referencias, test por lectura de `.csproj`, prueba negativa en memoria y script fail-fast.
- Endurecido: análisis limitado a `src`, avisos como errores, plataformas concretas, mapa explícito de solución y detección de proyectos esperados ausentes.
- Rechazado: convertir `AnyCPU` a `x64`, agregar coverlet, una librería de arquitectura o tipos de dominio antes de sus tickets.
- Adaptado al host: las reglas se ejecutan como `x64`; la solución pedida luego compila para `x64` o `ARM64`.

## Pruebas locales

Suite enfocada:

```powershell
dotnet test tests\TokenUsage.Architecture.Tests\TokenUsage.Architecture.Tests.csproj -p:Platform=x64 --no-restore
```

Resultado: 3 tests superados. Cubren el repo real, una arista invertida `Core -> Providers` y la ausencia de un proyecto esperado.

Control `x64`:

```powershell
.\scripts\check.ps1 -Platform x64
```

Resultado: 6 proyectos compilados, 3 tests superados, 0 avisos y 0 errores.

Control `ARM64`:

```powershell
.\scripts\check.ps1 -Platform ARM64
```

Resultado: reglas ejecutadas en `x64`; 6 proyectos compilados para `ARM64`, 0 avisos y 0 errores.

Empaquetado WinUI después de agregar las referencias:

```powershell
.\BuildAndRun.ps1 src\TokenUsage.App\TokenUsage.App.csproj -SkipRun /p:Platform=x64
```

Resultado: `BUILD SUCCEEDED` con Core, Providers, Platform.Windows y App.

Prueba negativa del control de plataforma:

```powershell
dotnet build src\TokenUsage.Core\TokenUsage.Core.csproj -p:Platform=AnyCPU --no-restore
```

Resultado esperado y observado: fallo con `TokenUsage requires Platform=x64 or Platform=ARM64`.

## Límites

- ARM64 tiene prueba de compilación cruzada. Este equipo no ejecuta un host .NET ARM64.
- Core, Providers y Platform.Windows están vacíos a propósito; sus modelos y adaptadores llegan en tickets posteriores.
- La CLI conserva un punto de entrada mínimo hasta el Ticket 25.
- La UI visible sigue bloqueada por la elección del Ticket 01.

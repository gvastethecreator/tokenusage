# Ticket 12B2B — identidad y proyección de métricas

Fecha: 2026-07-23

Estado: implementado y verificado en x64.

## Resultado

- Cada cuota, métrica principal y métrica bajo demanda tiene un ID de layout estable y sin texto localizado.
- El proyector reconcilia el catálogo actual con el layout guardado y conserva preferencias de métricas ausentes.
- Orden, sección, visibilidad y destacado se aplican sin cambiar el snapshot fuente.
- La salida visual usa una secuencia única por sección. El orden guardado se conserva al mezclar cuotas y valores escalares.
- Las cuotas movidas a Bajo demanda se muestran dentro del expander.
- Los IDs vacíos o repetidos fallan antes de publicar un dashboard parcial.

## Fuentes cubiertas

- fixtures de Codex, Claude, Grok Build, OpenCode y Antigravity;
- Codex real, con IDs de sus métricas normalizadas;
- Vercel AI Gateway, con IDs separados para cuota y estado textual.

`LocalUsageCard` queda fuera del layout por provider aunque comparta el tipo `SampleMetric`.

## Pruebas

Comando focal:

```powershell
dotnet test tests/WOpenUsage.Providers.Tests/WOpenUsage.Providers.Tests.csproj `
  -c Release --no-restore `
  --filter "FullyQualifiedName~DashboardLayoutProjectorTests|FullyQualifiedName~SampleDashboardProjectorTests|FullyQualifiedName~CodexDashboardProjectorTests"
```

Resultado: 25/25.

Vercel:

```powershell
dotnet test tests/WOpenUsage.Platform.Windows.Tests/WOpenUsage.Platform.Windows.Tests.csproj `
  -c Release --no-restore `
  --filter FullyQualifiedName~VercelGatewayCardProjectorTests
```

Resultado: 7/7.

Build empaquetada:

```powershell
.\BuildAndRun.ps1 src\WOpenUsage.App\WOpenUsage.App.csproj `
  -SkipRun /p:Platform=x64 /p:Configuration=Release
```

Resultado: `BUILD SUCCEEDED` y MSIX x64 generado.

Gate x64 del lote 12B2A+B:

- Architecture: 62/62.
- Core: 134/134.
- CLI: 82/82.
- Providers: 268/268.
- Platform Windows: 98/98.

La revisión independiente halló que el primer corte separaba cuotas y valores en listas visuales y perdía el orden mixto. La reparación añadió secuencias unificadas por sección, una prueba escalar → cuota y un segundo build. La revisión final fue `ACCEPT`, sin P0–P2.

## Pendiente

12B2C añadirá los controles WinUI para editar estas preferencias y una prueba empaquetada con reinicio. ARM64 queda para el cierre del lote de Ticket 12.

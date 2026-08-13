# Actualización de dependencias

Fecha: 2026-08-13

## Estado

`tokenusage` es un proyecto nativo .NET/WinUI; no usa Bun ni pnpm. Las
dependencias activas quedan en la última versión estable.

| Paquete o acción | Versión | Motivo |
| --- | --- | --- |
| Microsoft.Windows.SDK.BuildTools.WinApp | 0.6.0 | único paquete NuGet estable desactualizado |
| actions/checkout | v7.0.1 | runtime Node 24 |
| actions/setup-dotnet | v6.0.0 | runtime Node 24 |
| microsoft/setup-msbuild | v3 | runtime Node 24 |
| actions/upload-artifact | v7.0.1 | runtime Node 24 |

Se revisaron también Windows App SDK 2.3.1, CommunityToolkit.Mvvm 8.4.2,
Microsoft.Data.Sqlite 10.0.11, SQLitePCLRaw 3.0.5, Windows SDK Build Tools
10.0.28000.2526, Microsoft.NET.Test.Sdk 18.8.1, xunit 2.9.3 y
xunit.runner.visualstudio 3.1.5. Esas piezas ya estaban en la última estable.

No se tomaron previews (Windows App SDK 2.3.2-experimental, EF 11, xUnit 4).
Tampoco se migró a `xunit.v3`: v2.9.3 es la última de ese paquete, y v3 exige
pasar `TestContext.Current.CancellationToken` en cientos de tests bajo
`TreatWarningsAsErrors`.

WinApp 0.6.0 pide `PrivateAssets="all"` en la app de cabecera para que la
herramienta de `dotnet run` no fluya como dependencia de producto.

Fuentes oficiales: [WinApp 0.6.0](https://www.nuget.org/packages/Microsoft.Windows.SDK.BuildTools.WinApp/0.6.0),
[checkout v7.0.1](https://github.com/actions/checkout/releases/tag/v7.0.1),
[setup-dotnet v6.0.0](https://github.com/actions/setup-dotnet/releases/tag/v6.0.0),
[setup-msbuild v3](https://github.com/microsoft/setup-msbuild/releases/tag/v3) y
[upload-artifact v7.0.1](https://github.com/actions/upload-artifact/releases/tag/v7.0.1).

## Comandos

```powershell
dotnet restore src\TokenUsage.App\TokenUsage.App.csproj
.\scripts\deps-check.ps1
.\scripts\audit.ps1
```

Los proyectos de `.scratch`, `.reference` y `.snapshots` son probes históricos;
se mantienen fuera del grafo operativo y no se actualizan automáticamente.

# Actualización de dependencias

Fecha: 2026-08-12

## Estado

`tokenusage` es un proyecto nativo .NET/WinUI; no usa Bun ni pnpm. Las
dependencias activas quedan actualizadas:

| Paquete | Versión | Motivo |
| --- | --- | --- |
| Microsoft.Windows.SDK.BuildTools | 10.0.28000.2526 | SDK Windows más reciente publicado |
| Microsoft.Data.Sqlite | 10.0.11 | parche de mantenimiento .NET 10 |
| SQLitePCLRaw.bundle_e_sqlite3 | 3.0.5 | bundle SQLite actual; se verificó la API `Batteries_V2` |
| Microsoft.NET.Test.Sdk | 18.8.1 | runner/testhost actual |
| xunit.runner.visualstudio | 3.1.5 | adapter actual de xUnit |

También se revisaron los changelogs/NuGet de Windows SDK Build Tools, SQLitePCLRaw,
Microsoft.NET.Test.Sdk y xUnit. La actualización de SQLitePCLRaw es mayor, pero
la suite activa y el build conservan la inicialización y el almacenamiento sin
cambios de contrato.

Fuentes oficiales: [BuildTools 10.0.28000.2526](https://www.nuget.org/packages/Microsoft.Windows.SDK.BuildTools/10.0.28000.2526),
[SQLitePCLRaw 3.0.5](https://www.nuget.org/packages/SQLitePCLRaw.bundle_e_sqlite3/3.0.5),
[Test SDK 18.8.1](https://www.nuget.org/packages/microsoft.net.test.sdk/18.8.1) y
[xUnit adapter 3.1.5](https://xunit.net/releases/visualstudio/3.1.5).

## Comandos

```powershell
dotnet restore src\TokenUsage.App\TokenUsage.App.csproj
.\scripts\deps-check.ps1
.\scripts\audit.ps1
```

Los proyectos de `.scratch`, `.reference` y `.snapshots` son probes históricos;
se mantienen fuera del grafo operativo y no se actualizan automáticamente.

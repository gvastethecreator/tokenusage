# Dependency maintenance

Last reviewed: 2026-08-14

## Current state

TokenUsage is a native .NET and WinUI project. It does not use Bun or pnpm.
The active dependency graph is on the latest stable releases available from
the configured NuGet sources.

| Package or action | Version | Notes |
| --- | --- | --- |
| Microsoft.WindowsAppSDK | 2.4.0 | Latest stable Windows App SDK |
| Microsoft.Windows.SDK.BuildTools.WinApp | 0.6.0 | Kept private to the app project |
| Microsoft.NET.Test.Sdk | 18.9.0 | Current stable test host |
| actions/checkout | v7.0.1 | Node 24 action runtime |
| actions/setup-dotnet | v6.0.0 | Node 24 action runtime |
| microsoft/setup-msbuild | v3 | Current stable MSBuild setup action |
| actions/upload-artifact | v7.0.1 | Node 24 action runtime |

The review also covered CommunityToolkit.Mvvm 8.4.2, Microsoft.Data.Sqlite
10.0.11, SQLitePCLRaw 3.0.5, Windows SDK Build Tools 10.0.28000.2526,
xUnit 2.9.3, and xunit.runner.visualstudio 4.0.0. No preview packages were
selected.

The app's WinApp helper remains `PrivateAssets="all"` so a development launch
tool does not flow into the product dependency graph.

Official package versions were checked against the NuGet V3 catalog. The
dependency check and vulnerability audit were then rerun with an isolated
package cache because the machine-wide CommunityToolkit.Mvvm cache was
incomplete.

## Commands

```powershell
.\scripts\deps-check.ps1
.\scripts\audit.ps1
```

Projects under `.scratch`, `.reference`, and `.snapshots` are historical probes.
They stay outside the active graph and are not updated automatically.

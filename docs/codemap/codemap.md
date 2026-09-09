# Code map: tokenusage

Generated: 2026-09-09T15:24:46Z | Commit: `dc0dd45a992c` | Schema: 2
Generation: `bef2ea748a85f4177d425a773f58d4ea6914b084198eb489010e118d3d5e163b`
Scope: BuildAndRun.ps1, Directory.Build.props, TokenUsage.slnx, scripts, src, tests | Inventory: working-tree
Nodes: 512 | Edges: 27 | Flows: 0

## Coverage

- Analysis: **partial**; 460 analyzed of 512 included files.
- Configuration files: 0; omitted untracked files: 0.
- Unresolved references and analysis limits: 1965.
- Static references and call paths do not prove runtime execution or test coverage.

## Modules

- `BuildAndRun.ps1` | module | Repository | callers: none | callees: none | tests: 0 | entry: none
- `scripts/audit.ps1` | module | Repository | callers: none | callees: none | tests: 0 | entry: none
- `scripts/check.ps1` | module | Repository | callers: none | callees: none | tests: 0 | entry: none
- `scripts/deps-check.ps1` | module | Repository | callers: none | callees: none | tests: 0 | entry: none
- `scripts/measure-ingest.cs` | module | Repository | callers: none | callees: none | tests: 0 | entry: none
- `scripts/release.ps1` | module | Repository | callers: none | callees: none | tests: 0 | entry: none
- `scripts/store/Build-StoreUpload.ps1` | module | Repository | callers: none | callees: none | tests: 0 | entry: none
- `scripts/store/Test-StoreReadiness.ps1` | module | Repository | callers: none | callees: none | tests: 0 | entry: none
- `src/TokenUsage.App/App.xaml` | module | Repository | callers: none | callees: none | tests: 0 | entry: none
- `src/TokenUsage.App/App.xaml.cs` | module | Repository | callers: none | callees: none | tests: 0 | entry: none
- `src/TokenUsage.App/Composition/AppComposition.cs` | module | Repository | callers: none | callees: none | tests: 0 | entry: none
- `src/TokenUsage.App/Composition/DebugVercelGatewayFakes.cs` | module | Repository | callers: none | callees: none | tests: 0 | entry: none
- `src/TokenUsage.App/Controls/AnimatedProgressBar.xaml` | module | Repository | callers: none | callees: none | tests: 0 | entry: none
- `src/TokenUsage.App/Controls/AnimatedProgressBar.xaml.cs` | module | Repository | callers: none | callees: none | tests: 0 | entry: none
- `src/TokenUsage.App/Controls/MotionSettings.cs` | module | Repository | callers: none | callees: none | tests: 0 | entry: none
- `src/TokenUsage.App/Controls/ProviderColorPalette.cs` | module | Repository | callers: none | callees: none | tests: 0 | entry: none
- `src/TokenUsage.App/Controls/ProviderColorSwatch.xaml` | module | Repository | callers: none | callees: none | tests: 0 | entry: none
- `src/TokenUsage.App/Controls/ProviderColorSwatch.xaml.cs` | module | Repository | callers: none | callees: none | tests: 0 | entry: none
- `src/TokenUsage.App/Controls/ProviderMarkImage.xaml` | module | Repository | callers: none | callees: none | tests: 0 | entry: none
- `src/TokenUsage.App/Controls/ProviderMarkImage.xaml.cs` | module | Repository | callers: none | callees: none | tests: 0 | entry: none
- Showing 20 of 512 nodes. Query `impact --module <path>` or open the HTML hierarchy for the rest.

## Edges

- `src/TokenUsage.App/TokenUsage.App.csproj` -> `src/TokenUsage.Core/TokenUsage.Core.csproj` | imports
- `src/TokenUsage.App/TokenUsage.App.csproj` -> `src/TokenUsage.Platform.Windows/TokenUsage.Platform.Windows.csproj` | imports
- `src/TokenUsage.App/TokenUsage.App.csproj` -> `src/TokenUsage.Presentation/TokenUsage.Presentation.csproj` | imports
- `src/TokenUsage.App/TokenUsage.App.csproj` -> `src/TokenUsage.Providers/TokenUsage.Providers.csproj` | imports
- `src/TokenUsage.App/TokenUsage.App.csproj` -> `src/TokenUsage.Runtime.Windows/TokenUsage.Runtime.Windows.csproj` | imports
- `src/TokenUsage.Cli/TokenUsage.Cli.csproj` -> `src/TokenUsage.Core/TokenUsage.Core.csproj` | imports
- `src/TokenUsage.Cli/TokenUsage.Cli.csproj` -> `src/TokenUsage.Providers/TokenUsage.Providers.csproj` | imports
- `src/TokenUsage.Cli/TokenUsage.Cli.csproj` -> `src/TokenUsage.Runtime.Windows/TokenUsage.Runtime.Windows.csproj` | imports
- `src/TokenUsage.Platform.Windows/TokenUsage.Platform.Windows.csproj` -> `src/TokenUsage.Core/TokenUsage.Core.csproj` | imports
- `src/TokenUsage.Presentation/TokenUsage.Presentation.csproj` -> `src/TokenUsage.Core/TokenUsage.Core.csproj` | imports
- `src/TokenUsage.Presentation/TokenUsage.Presentation.csproj` -> `src/TokenUsage.Providers/TokenUsage.Providers.csproj` | imports
- `src/TokenUsage.Providers/TokenUsage.Providers.csproj` -> `src/TokenUsage.Core/TokenUsage.Core.csproj` | imports
- `src/TokenUsage.Runtime.Windows/TokenUsage.Runtime.Windows.csproj` -> `src/TokenUsage.Core/TokenUsage.Core.csproj` | imports
- `src/TokenUsage.Runtime.Windows/TokenUsage.Runtime.Windows.csproj` -> `src/TokenUsage.Platform.Windows/TokenUsage.Platform.Windows.csproj` | imports
- `src/TokenUsage.Runtime.Windows/TokenUsage.Runtime.Windows.csproj` -> `src/TokenUsage.Providers/TokenUsage.Providers.csproj` | imports
- `tests/TokenUsage.Architecture.Tests/TokenUsage.Architecture.Tests.csproj` -> `src/TokenUsage.Presentation/TokenUsage.Presentation.csproj` | imports
- `tests/TokenUsage.Cli.Tests/TokenUsage.Cli.Tests.csproj` -> `src/TokenUsage.Cli/TokenUsage.Cli.csproj` | imports
- `tests/TokenUsage.Cli.Tests/TokenUsage.Cli.Tests.csproj` -> `tests/TokenUsage.TestSupport.FakeCodex/TokenUsage.TestSupport.FakeCodex.csproj` | imports
- `tests/TokenUsage.Core.Tests/TokenUsage.Core.Tests.csproj` -> `src/TokenUsage.Core/TokenUsage.Core.csproj` | imports
- `tests/TokenUsage.Platform.Windows.Tests/TokenUsage.Platform.Windows.Tests.csproj` -> `src/TokenUsage.Platform.Windows/TokenUsage.Platform.Windows.csproj` | imports
- `tests/TokenUsage.Platform.Windows.Tests/TokenUsage.Platform.Windows.Tests.csproj` -> `src/TokenUsage.Presentation/TokenUsage.Presentation.csproj` | imports
- `tests/TokenUsage.Platform.Windows.Tests/TokenUsage.Platform.Windows.Tests.csproj` -> `src/TokenUsage.Providers/TokenUsage.Providers.csproj` | imports
- `tests/TokenUsage.Platform.Windows.Tests/TokenUsage.Platform.Windows.Tests.csproj` -> `src/TokenUsage.Runtime.Windows/TokenUsage.Runtime.Windows.csproj` | imports
- `tests/TokenUsage.Platform.Windows.Tests/TokenUsage.Platform.Windows.Tests.csproj` -> `tests/TokenUsage.TestSupport.FakeCodex/TokenUsage.TestSupport.FakeCodex.csproj` | imports
- `tests/TokenUsage.Providers.Tests/TokenUsage.Providers.Tests.csproj` -> `src/TokenUsage.Core/TokenUsage.Core.csproj` | imports
- `tests/TokenUsage.Providers.Tests/TokenUsage.Providers.Tests.csproj` -> `src/TokenUsage.Presentation/TokenUsage.Presentation.csproj` | imports
- `tests/TokenUsage.Providers.Tests/TokenUsage.Providers.Tests.csproj` -> `src/TokenUsage.Providers/TokenUsage.Providers.csproj` | imports

## Unknown

- `BuildAndRun.ps1:1`: unsupported-language (.ps1)
- `scripts/audit.ps1:1`: unsupported-language (.ps1)
- `scripts/check.ps1:1`: unsupported-language (.ps1)
- `scripts/deps-check.ps1:1`: unsupported-language (.ps1)
- `scripts/measure-ingest.cs:1`: syntax-error (#:property)
- `scripts/release.ps1:1`: unsupported-language (.ps1)
- `scripts/store/Build-StoreUpload.ps1:1`: unsupported-language (.ps1)
- `scripts/store/Test-StoreReadiness.ps1:1`: unsupported-language (.ps1)
- `src/TokenUsage.App/App.xaml:1`: unsupported-language (.xaml)
- `src/TokenUsage.App/App.xaml.cs:1`: call-resolution-not-supported (c_sharp)
- `src/TokenUsage.App/App.xaml.cs:1`: csharp-namespace-resolution-not-supported (Microsoft.UI.Xaml)
- `src/TokenUsage.App/App.xaml.cs:2`: csharp-namespace-resolution-not-supported (Microsoft.Windows.AppLifecycle)

## Flows

- no source-backed call path from a recognized trigger

## Architecture changes

- Nodes: +0 / -0; edges: +0 / -0.
- Boundary changes: 0; new cycles: 0.

## Read next

- Use `status` before relying on this generation.
- Use `impact --changed` for possible impact and related test evidence.
- Use `diff --before <model> --after <model>` for architecture changes.

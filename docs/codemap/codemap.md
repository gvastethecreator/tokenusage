# Code map: tokenusage

Generated: 2026-09-28T06:45:49Z | Commit: `f46cb38d3b45` | Schema: 2
Generation: `a659de137956277a2fbe2b2e0f63c64a07530deea94f69e8ecfe608c08fe3628`
Scope: BuildAndRun.ps1, Directory.Build.props, TokenUsage.slnx, scripts, src, tests | Inventory: working-tree
Nodes: 577 | Edges: 4174 | Flows: 0

## Coverage

- Analysis: **partial**; 539 analyzed of 570 included files.
- Configuration files: 0; omitted untracked files: 0.
- Unresolved references and analysis limits: 2257.
- Static references and call paths do not prove runtime execution or test coverage.

## Modules

- `BuildAndRun.ps1` | module | Repository | callers: none | callees: none | tests: 0 | entry: none
- `external:csharp:CommunityToolkit` | external | External | callers: src/TokenUsage.App/ViewModels/FlyoutViewModel.cs, src/TokenUsage.App/ViewModels/Reports/UsageReportViewModel.cs, src/TokenUsage.App/ViewModels/Surfaces/UpdateOptionsViewModel.cs, src/TokenUsage.Presentation/ViewModels/Surfaces/AppearanceSurfaceViewModel.cs | callees: none | tests: 0 | entry: none
- `external:csharp:Microsoft` | external | External | callers: src/TokenUsage.App/App.xaml.cs, src/TokenUsage.App/Controls/AnimatedProgressBar.xaml.cs, src/TokenUsage.App/Controls/ProviderColorPalette.cs, src/TokenUsage.App/Controls/ProviderColorSwatch.xaml.cs | callees: none | tests: 12 | entry: none
- `external:csharp:System` | external | External | callers: src/TokenUsage.App/App.xaml.cs, src/TokenUsage.App/Controls/ReportShareBar.cs, src/TokenUsage.App/Controls/ThemeSwitchTransition.cs, src/TokenUsage.App/Controls/UsageHeatmap.xaml.cs | callees: none | tests: 75 | entry: none
- `external:csharp:TokenUsage` | external | External | callers: src/TokenUsage.Cli/GlobalUsings.cs, tests/TokenUsage.Cli.Tests/GlobalUsings.cs | callees: none | tests: 1 | entry: none
- `external:csharp:WinRT` | external | External | callers: src/TokenUsage.App/MainWindow.xaml.cs, src/TokenUsage.App/Services/ShareCaptureService.cs, src/TokenUsage.App/TraySummaryWindow.xaml.cs | callees: none | tests: 0 | entry: none
- `external:csharp:Windows` | external | External | callers: src/TokenUsage.App/Controls/MotionSettings.cs, src/TokenUsage.App/Controls/ProviderColorPalette.cs, src/TokenUsage.App/Controls/ProviderColorSwatch.xaml.cs, src/TokenUsage.App/Controls/ReportCaptureStackLayout.cs | callees: none | tests: 0 | entry: none
- `external:csharp:Xunit` | external | External | callers: tests/TokenUsage.Platform.Windows.Tests/AssemblyInfo.cs | callees: none | tests: 1 | entry: none
- `scripts/audit.ps1` | module | Repository | callers: none | callees: none | tests: 0 | entry: none
- `scripts/check.ps1` | module | Repository | callers: none | callees: none | tests: 0 | entry: none
- `scripts/deps-check.ps1` | module | Repository | callers: none | callees: none | tests: 0 | entry: none
- `scripts/release.ps1` | module | Repository | callers: none | callees: none | tests: 0 | entry: none
- `scripts/store/Build-StoreUpload.ps1` | module | Repository | callers: none | callees: none | tests: 0 | entry: none
- `scripts/store/Test-StoreReadiness.ps1` | module | Repository | callers: none | callees: none | tests: 0 | entry: none
- `src/TokenUsage.App/App.xaml` | module | Repository | callers: none | callees: none | tests: 0 | entry: none
- `src/TokenUsage.App/App.xaml.cs` | module | Repository | callers: src/TokenUsage.App/MainPage.xaml.cs, src/TokenUsage.App/Program.cs, src/TokenUsage.App/Program.cs, src/TokenUsage.App/Views/Options/GeneralOptionsView.xaml.cs | callees: external:csharp:Microsoft, external:csharp:System, src/TokenUsage.App/Localization/AppLanguageRuntime.cs, src/TokenUsage.App/Localization/AppLanguageRuntime.cs | tests: 0 | entry: none
- `src/TokenUsage.App/Composition/AppComposition.cs` | module | Repository | callers: none | callees: none | tests: 0 | entry: none
- `src/TokenUsage.App/Composition/DebugVercelGatewayFakes.cs` | module | Repository | callers: none | callees: src/TokenUsage.Providers/VercelAiGateway/VercelGatewayCreditsContracts.cs, src/TokenUsage.Providers/VercelAiGateway/VercelGatewayProviderRuntime.cs, src/TokenUsage.Providers/VercelAiGateway/VercelGatewayQuotaContracts.cs, src/TokenUsage.Providers/VercelAiGateway/VercelGatewayReportContracts.cs | tests: 0 | entry: none
- `src/TokenUsage.App/Controls/AnimatedProgressBar.xaml` | module | Repository | callers: none | callees: none | tests: 0 | entry: none
- `src/TokenUsage.App/Controls/AnimatedProgressBar.xaml.cs` | module | Repository | callers: none | callees: external:csharp:Microsoft, src/TokenUsage.App/Controls/MotionSettings.cs, src/TokenUsage.App/Controls/MotionSettings.cs, src/TokenUsage.Core/Usage/QuotaUsageLevel.cs | tests: 0 | entry: none
- Showing 20 of 577 nodes. Query `impact --module <path>` or open the HTML hierarchy for the rest.

## Edges

- `src/TokenUsage.App/App.xaml.cs` -> `external:csharp:Microsoft` | imports (type only)
- `src/TokenUsage.App/App.xaml.cs` -> `external:csharp:System` | imports (type only)
- `src/TokenUsage.App/App.xaml.cs` -> `src/TokenUsage.App/Localization/AppLanguageRuntime.cs` | calls
- `src/TokenUsage.App/App.xaml.cs` -> `src/TokenUsage.App/Localization/AppLanguageRuntime.cs` | imports (type only)
- `src/TokenUsage.App/App.xaml.cs` -> `src/TokenUsage.App/MainWindow.Resize.cs` | imports (type only)
- `src/TokenUsage.App/App.xaml.cs` -> `src/TokenUsage.App/MainWindow.TrayOptions.cs` | imports (type only)
- `src/TokenUsage.App/App.xaml.cs` -> `src/TokenUsage.App/MainWindow.Updates.cs` | imports (type only)
- `src/TokenUsage.App/App.xaml.cs` -> `src/TokenUsage.App/MainWindow.xaml.cs` | imports (type only)
- `src/TokenUsage.App/App.xaml.cs` -> `src/TokenUsage.App/Services/WindowsAlertNotificationService.cs` | calls
- `src/TokenUsage.App/App.xaml.cs` -> `src/TokenUsage.App/Services/WindowsAlertNotificationService.cs` | imports (type only)
- `src/TokenUsage.App/App.xaml.cs` -> `src/TokenUsage.Core/Alerts/AlertNotification.cs` | imports (type only)
- `src/TokenUsage.App/Composition/DebugVercelGatewayFakes.cs` -> `src/TokenUsage.Providers/VercelAiGateway/VercelGatewayCreditsContracts.cs` | imports (type only)
- `src/TokenUsage.App/Composition/DebugVercelGatewayFakes.cs` -> `src/TokenUsage.Providers/VercelAiGateway/VercelGatewayProviderRuntime.cs` | imports (type only)
- `src/TokenUsage.App/Composition/DebugVercelGatewayFakes.cs` -> `src/TokenUsage.Providers/VercelAiGateway/VercelGatewayQuotaContracts.cs` | imports (type only)
- `src/TokenUsage.App/Composition/DebugVercelGatewayFakes.cs` -> `src/TokenUsage.Providers/VercelAiGateway/VercelGatewayReportContracts.cs` | imports (type only)
- `src/TokenUsage.App/Composition/DebugVercelGatewayFakes.cs` -> `src/TokenUsage.Runtime.Windows/VercelAiGateway/VercelGatewayCredentialStore.cs` | imports (type only)
- `src/TokenUsage.App/Controls/AnimatedProgressBar.xaml.cs` -> `external:csharp:Microsoft` | imports (type only)
- `src/TokenUsage.App/Controls/AnimatedProgressBar.xaml.cs` -> `src/TokenUsage.App/Controls/MotionSettings.cs` | calls
- `src/TokenUsage.App/Controls/AnimatedProgressBar.xaml.cs` -> `src/TokenUsage.App/Controls/MotionSettings.cs` | imports (type only)
- `src/TokenUsage.App/Controls/AnimatedProgressBar.xaml.cs` -> `src/TokenUsage.Core/Usage/QuotaUsageLevel.cs` | calls
- `src/TokenUsage.App/Controls/AnimatedProgressBar.xaml.cs` -> `src/TokenUsage.Core/Usage/QuotaUsageLevel.cs` | imports (type only)
- `src/TokenUsage.App/Controls/AnimatedProgressBar.xaml.cs` -> `src/TokenUsage.Presentation/Controls/SpendDonutGeometry.cs` | calls
- `src/TokenUsage.App/Controls/AnimatedProgressBar.xaml.cs` -> `src/TokenUsage.Presentation/Controls/SpendDonutGeometry.cs` | imports (type only)
- `src/TokenUsage.App/Controls/MotionSettings.cs` -> `external:csharp:Windows` | imports (type only)
- `src/TokenUsage.App/Controls/ProviderColorPalette.cs` -> `external:csharp:Microsoft` | imports (type only)
- `src/TokenUsage.App/Controls/ProviderColorPalette.cs` -> `external:csharp:Windows` | imports (type only)
- `src/TokenUsage.App/Controls/ProviderColorPalette.cs` -> `src/TokenUsage.Core/Layout/ProviderColorPreference.cs` | calls
- `src/TokenUsage.App/Controls/ProviderColorPalette.cs` -> `src/TokenUsage.Core/Layout/ProviderColorPreference.cs` | imports (type only)
- `src/TokenUsage.App/Controls/ProviderColorSwatch.xaml.cs` -> `external:csharp:Microsoft` | imports (type only)
- `src/TokenUsage.App/Controls/ProviderColorSwatch.xaml.cs` -> `external:csharp:Windows` | imports (type only)
- `src/TokenUsage.App/Controls/ProviderColorSwatch.xaml.cs` -> `src/TokenUsage.App/Controls/ProviderColorPalette.cs` | calls
- `src/TokenUsage.App/Controls/ProviderColorSwatch.xaml.cs` -> `src/TokenUsage.App/Controls/ProviderColorPalette.cs` | imports (type only)
- `src/TokenUsage.App/Controls/ProviderMarkImage.xaml.cs` -> `external:csharp:Microsoft` | imports (type only)
- `src/TokenUsage.App/Controls/ProviderMarkImage.xaml.cs` -> `src/TokenUsage.Providers/Catalog/ProviderPresentationCatalog.cs` | calls
- `src/TokenUsage.App/Controls/ProviderMarkImage.xaml.cs` -> `src/TokenUsage.Providers/Catalog/ProviderPresentationCatalog.cs` | imports (type only)
- `src/TokenUsage.App/Controls/ReportCaptureStackLayout.cs` -> `external:csharp:Microsoft` | imports (type only)
- `src/TokenUsage.App/Controls/ReportCaptureStackLayout.cs` -> `external:csharp:Windows` | imports (type only)
- `src/TokenUsage.App/Controls/ReportShareBar.cs` -> `external:csharp:Microsoft` | imports (type only)
- `src/TokenUsage.App/Controls/ReportShareBar.cs` -> `external:csharp:System` | imports (type only)
- `src/TokenUsage.App/Controls/ReportShareBar.cs` -> `src/TokenUsage.App/Controls/MotionSettings.cs` | calls
- `src/TokenUsage.App/Controls/ReportShareBar.cs` -> `src/TokenUsage.App/Controls/MotionSettings.cs` | imports (type only)
- `src/TokenUsage.App/Controls/ReportShareBar.cs` -> `src/TokenUsage.App/Controls/ProviderColorPalette.cs` | calls
- `src/TokenUsage.App/Controls/ReportShareBar.cs` -> `src/TokenUsage.App/Controls/ProviderColorPalette.cs` | imports (type only)
- `src/TokenUsage.App/Controls/ReportToneIcon.cs` -> `external:csharp:Microsoft` | imports (type only)
- `src/TokenUsage.App/Controls/SpendDonutChart.xaml.cs` -> `external:csharp:Microsoft` | imports (type only)
- `src/TokenUsage.App/Controls/SpendDonutChart.xaml.cs` -> `external:csharp:Windows` | imports (type only)
- `src/TokenUsage.App/Controls/SpendDonutChart.xaml.cs` -> `src/TokenUsage.App/Controls/MotionSettings.cs` | calls
- `src/TokenUsage.App/Controls/SpendDonutChart.xaml.cs` -> `src/TokenUsage.App/Controls/MotionSettings.cs` | imports (type only)
- `src/TokenUsage.App/Controls/SpendDonutChart.xaml.cs` -> `src/TokenUsage.App/Controls/ProviderColorPalette.cs` | calls
- `src/TokenUsage.App/Controls/SpendDonutChart.xaml.cs` -> `src/TokenUsage.App/Controls/ProviderColorPalette.cs` | imports (type only)
- Showing 50 of 4174 edges; JSON contains every edge and its evidence.

## Unknown

- `BuildAndRun.ps1:1`: unsupported-language (.ps1)
- `scripts/audit.ps1:1`: unsupported-language (.ps1)
- `scripts/check.ps1:1`: unsupported-language (.ps1)
- `scripts/deps-check.ps1:1`: unsupported-language (.ps1)
- `scripts/release.ps1:1`: unsupported-language (.ps1)
- `scripts/store/Build-StoreUpload.ps1:1`: unsupported-language (.ps1)
- `scripts/store/Test-StoreReadiness.ps1:1`: unsupported-language (.ps1)
- `src/TokenUsage.App/App.xaml:1`: unsupported-language (.xaml)
- `src/TokenUsage.App/App.xaml.cs:31`: call-target-symbol-not-resolved (InitializeComponent)
- `src/TokenUsage.App/Composition/AppComposition.cs:152`: syntax-error (,)
- `src/TokenUsage.App/Controls/AnimatedProgressBar.xaml:1`: unsupported-language (.xaml)
- `src/TokenUsage.App/Controls/AnimatedProgressBar.xaml.cs:43`: call-target-symbol-not-resolved (InitializeComponent)

## Flows

- no source-backed call path from a recognized trigger

## Architecture changes

- Nodes: +17 / -6; edges: +161 / -140.
- Boundary changes: 0; new cycles: 4.

## Read next

- Use `status` before relying on this generation.
- Use `impact --changed` for possible impact and related test evidence.
- Use `diff --before <model> --after <model>` for architecture changes.

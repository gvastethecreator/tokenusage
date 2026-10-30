# Code map: tokenusage

Generated: 2026-10-30T08:37:22Z | Commit: `4552ec282520` | Schema: 2
Generation: `ede73183ee4c9530bbe30c1c23e0210bd8be873b17d02b583eb0158cc84e20b5`
Scope: BuildAndRun.ps1, Directory.Build.props, TokenUsage.slnx, scripts, src, tests | Inventory: working-tree
Nodes: 473 | Edges: 2902 | Flows: 0

## Coverage

- Analysis: **partial**; 435 analyzed of 466 included files.
- Configuration files: 0; omitted untracked files: 0.
- Unresolved references and analysis limits: 1268.
- Static references and call paths do not prove runtime execution or test coverage.

## Modules

- `BuildAndRun.ps1` | module | Repository | callers: none | callees: none | tests: 0 | entry: none
- `external:csharp:CommunityToolkit` | external | External | callers: src/TokenUsage.App/ViewModels/FlyoutViewModel.cs, src/TokenUsage.App/ViewModels/Reports/ReportViewModel.cs, src/TokenUsage.App/ViewModels/Surfaces/UpdateOptionsViewModel.cs, src/TokenUsage.Presentation/ViewModels/Surfaces/AppearanceSurfaceViewModel.cs | callees: none | tests: 0 | entry: none
- `external:csharp:Microsoft` | external | External | callers: src/TokenUsage.App/App.xaml.cs, src/TokenUsage.App/Controls/AccountQuotaList.xaml.cs, src/TokenUsage.App/Controls/ProviderColorPalette.cs, src/TokenUsage.App/Controls/ProviderColorSwatch.xaml.cs | callees: none | tests: 10 | entry: none
- `external:csharp:System` | external | External | callers: src/TokenUsage.App/App.xaml.cs, src/TokenUsage.App/Controls/ReportShareBar.cs, src/TokenUsage.App/Controls/ThemeSwitchTransition.cs, src/TokenUsage.App/Controls/UsageHeatmap.xaml.cs | callees: none | tests: 59 | entry: none
- `external:csharp:TokenUsage` | external | External | callers: src/TokenUsage.App/MainPage.xaml.cs, src/TokenUsage.App/MainWindow.xaml.cs | callees: none | tests: 0 | entry: none
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
- `src/TokenUsage.App/App.xaml.cs` | module | Repository | callers: src/TokenUsage.App/MainPage.xaml.cs, src/TokenUsage.App/Program.cs, src/TokenUsage.App/Program.cs, src/TokenUsage.App/Views/Options/AdvancedOptionsView.xaml.cs | callees: external:csharp:Microsoft, external:csharp:System, src/TokenUsage.App/Localization/AppLanguageRuntime.cs, src/TokenUsage.App/Localization/AppLanguageRuntime.cs | tests: 0 | entry: none
- `src/TokenUsage.App/Composition/AppComposition.cs` | module | Repository | callers: none | callees: none | tests: 0 | entry: none
- `src/TokenUsage.App/Controls/AccountQuotaList.xaml` | module | Repository | callers: none | callees: none | tests: 0 | entry: none
- `src/TokenUsage.App/Controls/AccountQuotaList.xaml.cs` | module | Repository | callers: none | callees: external:csharp:Microsoft, src/TokenUsage.Presentation/ViewModels/AccountQuota.cs | tests: 0 | entry: none
- `src/TokenUsage.App/Controls/MotionSettings.cs` | module | Repository | callers: src/TokenUsage.App/Controls/ReportShareBar.cs, src/TokenUsage.App/Controls/ReportShareBar.cs, src/TokenUsage.App/Controls/SpendDonutChart.xaml.cs, src/TokenUsage.App/Controls/SpendDonutChart.xaml.cs | callees: external:csharp:Windows | tests: 0 | entry: none
- Showing 20 of 473 nodes. Query `impact --module <path>` or open the HTML hierarchy for the rest.

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
- `src/TokenUsage.App/Controls/AccountQuotaList.xaml.cs` -> `external:csharp:Microsoft` | imports (type only)
- `src/TokenUsage.App/Controls/AccountQuotaList.xaml.cs` -> `src/TokenUsage.Presentation/ViewModels/AccountQuota.cs` | imports (type only)
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
- `src/TokenUsage.App/Controls/QuotaMeter.xaml.cs` -> `external:csharp:Microsoft` | imports (type only)
- `src/TokenUsage.App/Controls/QuotaMeter.xaml.cs` -> `src/TokenUsage.Core/Usage/QuotaUsageLevel.cs` | calls
- `src/TokenUsage.App/Controls/QuotaMeter.xaml.cs` -> `src/TokenUsage.Core/Usage/QuotaUsageLevel.cs` | imports (type only)
- `src/TokenUsage.App/Controls/QuotaMeter.xaml.cs` -> `src/TokenUsage.Presentation/Controls/SpendDonutGeometry.cs` | calls
- `src/TokenUsage.App/Controls/QuotaMeter.xaml.cs` -> `src/TokenUsage.Presentation/Controls/SpendDonutGeometry.cs` | imports (type only)
- `src/TokenUsage.App/Controls/ReportCaptureStackLayout.cs` -> `external:csharp:Microsoft` | imports (type only)
- `src/TokenUsage.App/Controls/ReportCaptureStackLayout.cs` -> `external:csharp:Windows` | imports (type only)
- `src/TokenUsage.App/Controls/ReportCharts.cs` -> `external:csharp:Microsoft` | imports (type only)
- `src/TokenUsage.App/Controls/ReportCharts.cs` -> `external:csharp:Windows` | imports (type only)
- `src/TokenUsage.App/Controls/ReportCharts.cs` -> `src/TokenUsage.Presentation/ViewModels/Reports/ReportProjection.cs` | imports (type only)
- `src/TokenUsage.App/Controls/ReportCharts.cs` -> `src/TokenUsage.Presentation/ViewModels/UsageValueFormatter.cs` | calls
- `src/TokenUsage.App/Controls/ReportCharts.cs` -> `src/TokenUsage.Presentation/ViewModels/UsageValueFormatter.cs` | imports (type only)
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
- Showing 50 of 2902 edges; JSON contains every edge and its evidence.

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
- `src/TokenUsage.App/Composition/AppComposition.cs:89`: syntax-error (,)
- `src/TokenUsage.App/Controls/AccountQuotaList.xaml:1`: unsupported-language (.xaml)
- `src/TokenUsage.App/Controls/AccountQuotaList.xaml.cs:14`: call-target-symbol-not-resolved (InitializeComponent)

## Flows

- no source-backed call path from a recognized trigger

## Architecture changes

- Nodes: +0 / -0; edges: +0 / -0.
- Boundary changes: 0; new cycles: 0.

## Read next

- Use `status` before relying on this generation.
- Use `impact --changed` for possible impact and related test evidence.
- Use `diff --before <model> --after <model>` for architecture changes.

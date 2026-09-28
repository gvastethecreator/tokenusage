# Code map: tokenusage

Generated: 2026-09-28T03:46:26Z | Commit: `b7d2866ab1d6` | Schema: 2
Generation: `e7a65462dc1ce8c96a9d32b9fbae69abc5f20a008fc91d9c63bfd124d1b05791`
Scope: BuildAndRun.ps1, Directory.Build.props, TokenUsage.slnx, scripts, src, tests | Inventory: working-tree
Nodes: 599 | Edges: 4243 | Flows: 0

## Coverage

- Analysis: **partial**; 539 analyzed of 592 included files.
- Configuration files: 0; omitted untracked files: 0.
- Unresolved references and analysis limits: 2318.
- Static references and call paths do not prove runtime execution or test coverage.

## Modules

- `BuildAndRun.ps1` | module | Repository | callers: none | callees: none | tests: 0 | entry: none
- `external:csharp:CommunityToolkit` | external | External | callers: src/TokenUsage.App/ViewModels/FlyoutViewModel.cs, src/TokenUsage.App/ViewModels/Reports/UsageReportViewModel.cs, src/TokenUsage.App/ViewModels/Surfaces/UpdateOptionsViewModel.cs, src/TokenUsage.App/ViewModels/VercelGatewaySettingsViewModel.cs | callees: none | tests: 0 | entry: none
- `external:csharp:Microsoft` | external | External | callers: src/TokenUsage.App/App.xaml.cs, src/TokenUsage.App/Composition/AppComposition.cs, src/TokenUsage.App/Controls/AnimatedProgressBar.xaml.cs, src/TokenUsage.App/Controls/ProviderColorPalette.cs | callees: none | tests: 12 | entry: none
- `external:csharp:System` | external | External | callers: src/TokenUsage.App/App.xaml.cs, src/TokenUsage.App/Controls/ReportShareBar.cs, src/TokenUsage.App/Controls/ThemeSwitchTransition.cs, src/TokenUsage.App/Controls/UsageHeatmap.xaml.cs | callees: none | tests: 74 | entry: none
- `external:csharp:TokenUsage` | external | External | callers: src/TokenUsage.Cli/GlobalUsings.cs, tests/TokenUsage.Cli.Tests/GlobalUsings.cs | callees: none | tests: 1 | entry: none
- `external:csharp:WinRT` | external | External | callers: src/TokenUsage.App/MainWindow.xaml.cs, src/TokenUsage.App/Services/ShareCaptureService.cs, src/TokenUsage.App/TraySummaryWindow.xaml.cs | callees: none | tests: 0 | entry: none
- `external:csharp:Windows` | external | External | callers: src/TokenUsage.App/Controls/MotionSettings.cs, src/TokenUsage.App/Controls/ProviderColorPalette.cs, src/TokenUsage.App/Controls/ProviderColorSwatch.xaml.cs, src/TokenUsage.App/Controls/ReportCaptureStackLayout.cs | callees: none | tests: 0 | entry: none
- `external:csharp:Xunit` | external | External | callers: tests/TokenUsage.Platform.Windows.Tests/AssemblyInfo.cs | callees: none | tests: 1 | entry: none
- `scripts/audit.ps1` | module | Repository | callers: none | callees: none | tests: 0 | entry: none
- `scripts/check.ps1` | module | Repository | callers: none | callees: none | tests: 0 | entry: none
- `scripts/deps-check.ps1` | module | Repository | callers: none | callees: none | tests: 0 | entry: none
- `scripts/measure-ingest.cs` | module | Repository | callers: none | callees: none | tests: 0 | entry: none
- `scripts/release.ps1` | module | Repository | callers: none | callees: none | tests: 0 | entry: none
- `scripts/store/Build-StoreUpload.ps1` | module | Repository | callers: none | callees: none | tests: 0 | entry: none
- `scripts/store/Test-StoreReadiness.ps1` | module | Repository | callers: none | callees: none | tests: 0 | entry: none
- `src/TokenUsage.App/App.xaml` | module | Repository | callers: none | callees: none | tests: 0 | entry: none
- `src/TokenUsage.App/App.xaml.cs` | module | Repository | callers: src/TokenUsage.App/MainPage.xaml.cs, src/TokenUsage.App/Program.cs, src/TokenUsage.App/Program.cs, src/TokenUsage.App/Views/Options/GeneralOptionsView.xaml.cs | callees: external:csharp:Microsoft, external:csharp:System, src/TokenUsage.App/Localization/AppLanguageRuntime.cs, src/TokenUsage.App/Localization/AppLanguageRuntime.cs | tests: 0 | entry: none
- `src/TokenUsage.App/Composition/AppComposition.cs` | module | Repository | callers: src/TokenUsage.App/MainPage.xaml.cs, src/TokenUsage.App/MainPage.xaml.cs, src/TokenUsage.App/MainWindow.xaml.cs, src/TokenUsage.App/MainWindow.xaml.cs | callees: external:csharp:Microsoft, src/TokenUsage.App/Composition/DebugVercelGatewayFakes.cs, src/TokenUsage.App/ViewModels/FlyoutViewModel.Dashboard.cs, src/TokenUsage.App/ViewModels/FlyoutViewModel.cs | tests: 0 | entry: none
- `src/TokenUsage.App/Composition/DebugVercelGatewayFakes.cs` | module | Repository | callers: src/TokenUsage.App/Composition/AppComposition.cs | callees: src/TokenUsage.Providers/VercelAiGateway/VercelGatewayProviderRuntime.cs, src/TokenUsage.Providers/VercelAiGateway/VercelGatewayQuotaContracts.cs, src/TokenUsage.Providers/VercelAiGateway/VercelGatewayReportContracts.cs, src/TokenUsage.Runtime.Windows/VercelAiGateway/VercelGatewayCredentialStore.cs | tests: 0 | entry: none
- `src/TokenUsage.App/Controls/AnimatedProgressBar.xaml` | module | Repository | callers: none | callees: none | tests: 0 | entry: none
- Showing 20 of 599 nodes. Query `impact --module <path>` or open the HTML hierarchy for the rest.

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
- `src/TokenUsage.App/Composition/AppComposition.cs` -> `external:csharp:Microsoft` | imports (type only)
- `src/TokenUsage.App/Composition/AppComposition.cs` -> `src/TokenUsage.App/Composition/DebugVercelGatewayFakes.cs` | imports (type only)
- `src/TokenUsage.App/Composition/AppComposition.cs` -> `src/TokenUsage.App/ViewModels/FlyoutViewModel.Dashboard.cs` | imports (type only)
- `src/TokenUsage.App/Composition/AppComposition.cs` -> `src/TokenUsage.App/ViewModels/FlyoutViewModel.cs` | imports (type only)
- `src/TokenUsage.App/Composition/AppComposition.cs` -> `src/TokenUsage.App/ViewModels/Surfaces/UpdateOptionsViewModel.cs` | imports (type only)
- `src/TokenUsage.App/Composition/AppComposition.cs` -> `src/TokenUsage.Core/Alerts/AlertDecisionState.cs` | imports (type only)
- `src/TokenUsage.App/Composition/AppComposition.cs` -> `src/TokenUsage.Core/Alerts/AlertHost.cs` | imports (type only)
- `src/TokenUsage.App/Composition/AppComposition.cs` -> `src/TokenUsage.Core/Alerts/AlertNotification.cs` | imports (type only)
- `src/TokenUsage.App/Composition/AppComposition.cs` -> `src/TokenUsage.Core/Appearance/AppearanceSettingsStore.cs` | imports (type only)
- `src/TokenUsage.App/Composition/AppComposition.cs` -> `src/TokenUsage.Core/Cache/SnapshotCacheResults.cs` | imports (type only)
- `src/TokenUsage.App/Composition/AppComposition.cs` -> `src/TokenUsage.Core/Cache/SnapshotStore.cs` | imports (type only)
- `src/TokenUsage.App/Composition/AppComposition.cs` -> `src/TokenUsage.Core/Layout/DashboardLayoutStore.cs` | imports (type only)
- `src/TokenUsage.App/Composition/AppComposition.cs` -> `src/TokenUsage.Core/Session/AppSessionHost.cs` | imports (type only)
- `src/TokenUsage.App/Composition/AppComposition.cs` -> `src/TokenUsage.Core/Updates/UpdateSettingsStore.cs` | imports (type only)
- `src/TokenUsage.App/Composition/AppComposition.cs` -> `src/TokenUsage.Core/Usage/AttributionAliasStore.cs` | calls
- `src/TokenUsage.App/Composition/AppComposition.cs` -> `src/TokenUsage.Core/Usage/AttributionAliasStore.cs` | imports (type only)
- `src/TokenUsage.App/Composition/AppComposition.cs` -> `src/TokenUsage.Core/Usage/AttributionConsent.cs` | imports (type only)
- `src/TokenUsage.App/Composition/AppComposition.cs` -> `src/TokenUsage.Core/Usage/AttributionConsentStore.cs` | calls
- `src/TokenUsage.App/Composition/AppComposition.cs` -> `src/TokenUsage.Core/Usage/AttributionConsentStore.cs` | imports (type only)
- `src/TokenUsage.App/Composition/AppComposition.cs` -> `src/TokenUsage.Core/Usage/DataCollectionSettingsStore.cs` | calls
- `src/TokenUsage.App/Composition/AppComposition.cs` -> `src/TokenUsage.Core/Usage/DataCollectionSettingsStore.cs` | imports (type only)
- `src/TokenUsage.App/Composition/AppComposition.cs` -> `src/TokenUsage.Core/Usage/OpaqueAttributionKeys.cs` | imports (type only)
- `src/TokenUsage.App/Composition/AppComposition.cs` -> `src/TokenUsage.Core/Usage/QuotaResetHistoryStore.cs` | imports (type only)
- `src/TokenUsage.App/Composition/AppComposition.cs` -> `src/TokenUsage.Core/Usage/SavedUsageComparison.cs` | imports (type only)
- `src/TokenUsage.App/Composition/AppComposition.cs` -> `src/TokenUsage.Core/Usage/UsageRepository.Attribution.cs` | calls
- `src/TokenUsage.App/Composition/AppComposition.cs` -> `src/TokenUsage.Core/Usage/UsageRepository.Attribution.cs` | imports (type only)
- `src/TokenUsage.App/Composition/AppComposition.cs` -> `src/TokenUsage.Core/Usage/UsageRepository.Backup.cs` | imports (type only)
- `src/TokenUsage.App/Composition/AppComposition.cs` -> `src/TokenUsage.Core/Usage/UsageRepository.Detail.cs` | imports (type only)
- `src/TokenUsage.App/Composition/AppComposition.cs` -> `src/TokenUsage.Core/Usage/UsageRepository.Distributions.cs` | imports (type only)
- `src/TokenUsage.App/Composition/AppComposition.cs` -> `src/TokenUsage.Core/Usage/UsageRepository.Measurement.cs` | imports (type only)
- `src/TokenUsage.App/Composition/AppComposition.cs` -> `src/TokenUsage.Core/Usage/UsageRepository.Operations.cs` | calls
- `src/TokenUsage.App/Composition/AppComposition.cs` -> `src/TokenUsage.Core/Usage/UsageRepository.Operations.cs` | imports (type only)
- `src/TokenUsage.App/Composition/AppComposition.cs` -> `src/TokenUsage.Core/Usage/UsageRepository.Overview.cs` | imports (type only)
- `src/TokenUsage.App/Composition/AppComposition.cs` -> `src/TokenUsage.Core/Usage/UsageRepository.Paging.cs` | imports (type only)
- `src/TokenUsage.App/Composition/AppComposition.cs` -> `src/TokenUsage.Core/Usage/UsageRepository.Project.cs` | calls
- `src/TokenUsage.App/Composition/AppComposition.cs` -> `src/TokenUsage.Core/Usage/UsageRepository.Project.cs` | imports (type only)
- `src/TokenUsage.App/Composition/AppComposition.cs` -> `src/TokenUsage.Core/Usage/UsageRepository.Revision.cs` | imports (type only)
- `src/TokenUsage.App/Composition/AppComposition.cs` -> `src/TokenUsage.Core/Usage/UsageRepository.SourceScope.cs` | imports (type only)
- `src/TokenUsage.App/Composition/AppComposition.cs` -> `src/TokenUsage.Core/Usage/UsageRepository.cs` | calls
- Showing 50 of 4243 edges; JSON contains every edge and its evidence.

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
- `src/TokenUsage.App/App.xaml.cs:31`: call-target-symbol-not-resolved (InitializeComponent)
- `src/TokenUsage.App/Composition/AppComposition.cs:393`: call-target-symbol-not-resolved (purge)
- `src/TokenUsage.App/Controls/AnimatedProgressBar.xaml:1`: unsupported-language (.xaml)

## Flows

- no source-backed call path from a recognized trigger

## Architecture changes

- Nodes: +22 / -0; edges: +4216 / -0.
- Boundary changes: 0; new cycles: 23.

## Read next

- Use `status` before relying on this generation.
- Use `impact --changed` for possible impact and related test evidence.
- Use `diff --before <model> --after <model>` for architecture changes.

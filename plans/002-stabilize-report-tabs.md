# 002 — Stabilize the report tabs

- **Status**: DONE
- **Commit**: 43e54ce
- **Severity**: HIGH
- **Category**: Purpose and frequency
- **Estimated scope**: 2 files, medium change

## Problem

The shared report tab style animates every visual-state change for 180 ms.

```xml
<!-- src/TokenUsage.App/Views/Reports/UsageReportPage.xaml:47 — current -->
<VisualStateGroup x:Name="CommonStates">
    <VisualStateGroup.Transitions>
        <VisualTransition GeneratedDuration="0:0:0.18" />
    </VisualStateGroup.Transitions>
```

The generated transition fades the old active indicator while the new indicator appears. Rapid selection can show two active tabs.

The controls are independent `ToggleButton` elements. A user can temporarily toggle the selected item before the view model commits the next state.

## Target

Use `RadioButton` controls with one group name for each exclusive set. Change the active underline without an animation.

Use these exact groups:

- `UsageReportScopeTabs`: Global and Provider.
- `UsageReportPeriodTabs`: 7 days, 30 days, 90 days, and reset cycle.
- `UsageReportMetricTabs`: Cost and Tokens.
- `UsageReportValueModeTabs`: Absolute and Share.
- `UsageReportChartLayoutTabs`: Combined and Split.
- `UsageReportBreakdownTabs`: Model, Sources, and Day.

The selected foreground and underline must change in the same frame. Hover and pressed fills can use the existing visual states without generated transitions.

## Repo conventions to follow

`src/TokenUsage.App/Views/Dashboard/CompactUsageDashboard.xaml:22-95` defines `CompactProviderTabStyle` for `RadioButton`.

That style has no `VisualStateGroup.Transitions`. Its active indicator uses an immediate opacity setter.

```xml
<VisualState x:Name="Checked">
    <VisualState.Setters>
        <Setter Target="TabContent.Foreground" Value="{ThemeResource TextFillColorPrimaryBrush}" />
        <Setter Target="ActiveIndicator.Opacity" Value="1" />
    </VisualState.Setters>
</VisualState>
```

## Steps

1. Change `ReportToolbarToggleButtonStyle` to target `RadioButton`.
2. Change its `ControlTemplate` target to `RadioButton`.
3. Delete `VisualStateGroup.Transitions` from the style.
4. Replace each report tab `ToggleButton` with a `RadioButton`.
5. Add the exact `GroupName` values from the Target section.
6. Preserve every `x:Uid`, automation ID, tag, binding, click handler, and visible label.
7. Keep refresh, capture, and reset-cycle arrow controls as `Button`.
8. Keep each active indicator at `Height="2"` and `CornerRadius="1"`.
9. Extend `ArchitectureRulesTests` to reject `GeneratedDuration` inside `ReportToolbarToggleButtonStyle`.
10. Extend the same test to require all six report group names.

## Boundaries

- Do not animate the active underline.
- Do not move the toolbar controls.
- Do not change tab text or localization resources.
- Do not change automation IDs.
- Do not convert action buttons to radio buttons.
- If a tab group is no longer exclusive, stop and report the drift.

## Verification

- **Mechanical**: Run `dotnet test tests\TokenUsage.Architecture.Tests\TokenUsage.Architecture.Tests.csproj -c Release -p:Platform=x64 --no-restore`.
- **Mechanical**: Build the packaged app with `powershell -ExecutionPolicy Bypass -File .\BuildAndRun.ps1 src\TokenUsage.App\TokenUsage.App.csproj -SkipRun /p:Platform=x64 /p:Configuration=Release`.
- **Feel check**: Click rapidly across every tab group.
- **Feel check**: Make sure that one underline is visible in each group at all times.
- **Feel check**: Make sure that the tab row does not move or resize.
- **Feel check**: Use keyboard navigation and make sure that focus remains visible.
- **Done when**: Rapid changes never show two active indicators or an empty selection.

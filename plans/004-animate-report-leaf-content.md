# 004 — Animate only report leaf content

- **Status**: DONE
- **Commit**: 43e54ce
- **Severity**: HIGH
- **Category**: Purpose and cohesion
- **Estimated scope**: 3 files, large change

## Problem

Every major filter targets `ReportDataContent`. This element contains the full report body.

```xml
<!-- src/TokenUsage.App/Views/Reports/UsageReportPage.xaml:351 — current -->
<StackPanel x:Name="ReportDataContent" Grid.Row="2" Spacing="12"
            Visibility="{x:Bind ViewModel.HasData, Mode=OneWay}">
```

```csharp
// src/TokenUsage.App/Views/Reports/UsageReportPage.xaml.cs:104 — current
PlayViewTransition(
    ReportDataContent,
    ReportDataTransitionTransform,
    () => ViewModel.SetMetric(metric));
```

The animation fades cards, borders, titles, tabs, and data together. Stable structure appears to blink.

## Target

Keep these elements static:

- `ReportDataContent` and its spacing.
- Every `ReportCardStyle` border.
- The report toolbar and provider selector shell.
- Section titles and breakdown tabs.
- Table card borders and column layout.

Add these exact leaf roots:

- `ReportSummaryValuesRoot`: the inner summary grid at XAML line 390.
- `ReportCompositionLegendRoot`: the composition legend grid at line 424.
- Keep `ReportCompositionBar` as an existing leaf target.
- Keep `GlobalChartTransitionRoot` as the global chart plot target.
- `ProviderChartContentRoot`: the stack inside the provider chart card at line 526.
- `ReportCacheValuesRoot`: the inner cache grid at line 538.
- `ReportProviderLimitsContentRoot`: the inner limits grid at line 567.
- Keep `ModelBreakdownRows`, `SourceBreakdownRows`, and `DayBreakdownRows` as row targets.

The report-data transition must fade only the visible leaf roots. It must get a new visible-target list after the state commit.

Use opacity only for filter-driven data changes. Do not translate these targets.

The Combined and Split transition can use a 12 px horizontal offset. Keep its card, title, and layout tabs static.

The breakdown transition must animate the destination header and rows together. Keep the table card and breakdown tabs static.

## Repo conventions to follow

`src/TokenUsage.App/Views/Dashboard/CompactUsageDashboard.xaml:416-500` keeps provider cards mounted.

`CompactUsageDashboard.xaml.cs:330` returns only content targets that change. Its provider tabs and card shells do not animate.

Use the interruptible coordinator from plan 003 for report data targets.

## Steps

1. Remove `ReportDataTransitionTransform` from `ReportDataContent`.
2. Add the exact leaf-root names from the Target section.
3. Add `GetVisibleReportDataTargets()` to return visible leaf roots only.
4. Return summary, composition, chart, cache, limits, and current table data when each element is visible.
5. Exclude all card borders, tabs, titles, and toolbar elements from this method.
6. Route period, reset-cycle, metric, scope, value-mode, and provider changes through the plan 003 coordinator.
7. Get exit targets before the view-model commit.
8. Get entry targets after the view-model commit and `UpdateLayout()`.
9. Keep the provider selector width logic unchanged.
10. Wrap each breakdown header and row repeater in one named content root.
11. Update `GetBreakdownTransitionTarget` to return each named content root.
12. Keep `GlobalChartTransitionRoot`, but remove the chart card shell from its animated subtree.
13. Add an architecture test that rejects `PlayViewTransition(ReportDataContent`.
14. Add an architecture test that requires all leaf-root names.

## Boundaries

- Do not change report data calculations.
- Do not change chart data or tooltips.
- Do not change card sizes, padding, or grid columns.
- Do not animate report toolbar controls.
- Do not animate a `ScrollViewer` or the full report stack.
- Do not add a stagger longer than 20 ms between leaf targets.
- If the named XAML structures moved, stop and report the drift.

## Verification

- **Mechanical**: Run `dotnet test tests\TokenUsage.Architecture.Tests\TokenUsage.Architecture.Tests.csproj -c Release -p:Platform=x64 --no-restore`.
- **Mechanical**: Build the packaged app with `powershell -ExecutionPolicy Bypass -File .\BuildAndRun.ps1 src\TokenUsage.App\TokenUsage.App.csproj -SkipRun /p:Platform=x64 /p:Configuration=Release`.
- **Feel check**: Record the report while each filter changes.
- **Feel check**: Review the recording at 10% speed.
- **Feel check**: Make sure that card borders, tabs, titles, and layout positions do not fade or move.
- **Feel check**: Make sure that only values, plot content, bars, limits, headers, and rows change.
- **Feel check**: Enable reduced motion and make sure that data updates remain clear without movement.
- **Done when**: Every stable container remains visually fixed during each report interaction.

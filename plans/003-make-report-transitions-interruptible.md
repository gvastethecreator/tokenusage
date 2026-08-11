# 003 — Make report transitions interruptible

- **Status**: DONE
- **Commit**: 43e54ce
- **Severity**: HIGH
- **Category**: Interruptibility
- **Estimated scope**: 2 files, medium change

## Problem

The current helper stops an active storyboard before it starts a replacement.

```csharp
// src/TokenUsage.App/Views/Reports/UsageReportPage.xaml.cs:405 — current
FrameworkElement destinationElement = entryElement ?? element;
CompositeTransform destinationTransform = entryTransform ?? transform;
StopTransition(element);
```

`StartTransition` stops the same target again. It then applies explicit start values.

```csharp
// src/TokenUsage.App/Views/Reports/UsageReportPage.xaml.cs:511 — current
StopTransition(element);
element.Opacity = fromOpacity;
transform.TranslateX = fromOffset;
```

A rapid second action can restart from a base value. An old completion callback can also compete with newer state.

## Target

Create one interruptible transition channel for report data changes. Use these fields:

```csharp
private int _reportDataTransitionToken;
private Storyboard? _reportDataTransitionStoryboard;
```

Before stopping an active storyboard, capture the current opacity of every target. Stop the storyboard. Then restore the captured values as base values.

Each new transition must increment `_reportDataTransitionToken`. Every completion callback must compare its captured token and storyboard reference.

Only the newest transition can run its commit action. A stale callback must return without a commit or reset.

Use opacity only for report data refreshes. Use translation only for direct spatial swaps, such as Combined to Split.

Use 140 ms exit and 240 ms entry with `CubicEase { EasingMode = EasingMode.EaseOut }`.

If animations are disabled, run the commit immediately. Reset each target to opacity `1` and translation `0`.

## Repo conventions to follow

`src/TokenUsage.App/Views/Dashboard/CompactUsageDashboard.xaml.cs:236-328` is the primary exemplar.

It increments `_providerTransitionToken`. It also checks the token and storyboard identity in both completion callbacks.

Improve the exemplar for this report path. Capture animated values before `Storyboard.Stop()`.

## Steps

1. Add `_reportDataTransitionToken` and `_reportDataTransitionStoryboard` to `UsageReportPage`.
2. Add a report-data transition method that accepts exit targets, a commit action, and an entry-target factory.
3. Capture current target opacity values before the method stops an active storyboard.
4. Reapply the captured values after the storyboard stops.
5. Disable hit testing only on targets that contain interactive content.
6. Add the 140 ms opacity exit with `EaseOut`.
7. Check the token and storyboard reference before the commit.
8. Recompute entry targets after the commit and after `UpdateLayout()`.
9. Add the 240 ms opacity entry with `EaseOut`.
10. Restore opacity and hit testing after the current entry completes.
11. Keep separate transition tokens for chart-layout swaps and table-row swaps.
12. Cancel all transition channels in `OnUnloaded`.
13. Delete the old `_activeViewTransitions` dictionary after all call sites move to explicit channels.
14. Add an architecture regression test for token checks in both completion phases.

## Boundaries

- Do not delay the visual selection of a tab.
- Do not commit an obsolete selection.
- Do not animate width, height, margin, padding, or grid definitions.
- Do not add a new dependency.
- Do not change view-model behavior.
- If the current compact transition no longer uses tokens, stop and report the drift.

## Verification

- **Mechanical**: Run `dotnet test tests\TokenUsage.Architecture.Tests\TokenUsage.Architecture.Tests.csproj -c Release -p:Platform=x64 --no-restore`.
- **Mechanical**: Build the packaged app with `powershell -ExecutionPolicy Bypass -File .\BuildAndRun.ps1 src\TokenUsage.App\TokenUsage.App.csproj -SkipRun /p:Platform=x64 /p:Configuration=Release`.
- **Feel check**: Click Global, Provider, Global, and Provider before each transition ends.
- **Feel check**: Repeat this test for period, metric, chart layout, and table tabs.
- **Feel check**: Make sure that opacity continues from its current value.
- **Feel check**: Make sure that stale content never returns after the final click.
- **Done when**: Rapid input never causes a restart flash, stale commit, or disabled content.

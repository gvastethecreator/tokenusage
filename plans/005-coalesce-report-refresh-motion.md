# 005 — Coalesce report refresh motion

- **Status**: DONE
- **Commit**: 43e54ce
- **Severity**: HIGH
- **Category**: Interruptibility and performance
- **Estimated scope**: 2 files, medium change

## Problem

The report starts an entry when loading ends. A later `Trend` notification can start the same entry again.

```csharp
// src/TokenUsage.App/Views/Reports/UsageReportPage.xaml.cs:354 — current
if (string.Equals(e.PropertyName, nameof(UsageReportViewModel.IsLoading), StringComparison.Ordinal))
{
    if (ViewModel.IsLoading)
    {
        PrepareViewTransitionExit(ReportDataContent, ReportDataTransitionTransform);
    }
    else if (ViewModel.HasData)
    {
        PlayViewTransitionEntry(ReportDataContent, ReportDataTransitionTransform);
    }
    return;
}

if (string.Equals(e.PropertyName, nameof(UsageReportViewModel.Trend), StringComparison.Ordinal)
    && !ViewModel.IsLoading)
{
    PlayViewTransitionEntry(ReportDataContent, ReportDataTransitionTransform);
}
```

One data load can restart the full-report fade. The second animation makes the report flicker after the data already appeared.

## Target

Use one refresh generation for each `IsLoading` true-to-false cycle.

```csharp
private int _reportRefreshGeneration;
private int _lastCompletedRefreshGeneration;
private bool _reportRefreshInProgress;
```

When `IsLoading` becomes true, increment the generation once. Start one leaf-target exit and set `_reportRefreshInProgress`.

When `IsLoading` becomes false, start one leaf-target entry for the current generation. Set `_lastCompletedRefreshGeneration` after the entry completes.

A `Trend` notification during this cycle must update the binding only. It must not start another report entry.

A `Trend` notification outside a loading cycle can fade only the visible chart plot. It must not fade summary, cache, limits, or table content.

## Repo conventions to follow

`UsageReportPage` already uses `_shareStatusToken` to reject stale delayed work.

Plan 003 adds tokens for interruptible report transitions. Use the same identity checks.

Plan 004 defines the visible leaf targets and chart plot roots.

## Steps

1. Add the refresh-generation fields from the Target section.
2. Increment the generation only when loading changes from false to true.
3. Start one exit for the visible leaf targets.
4. Do not run a second exit for repeated `IsLoading=true` notifications.
5. Start one entry when loading changes to false and data exists.
6. Ignore entry requests for a generation that already completed.
7. Remove the full-report `Trend` entry call.
8. Route an isolated `Trend` change to the visible chart plot only.
9. Reset refresh state in `OnUnloaded`.
10. Add an architecture regression test that rejects `PlayViewTransitionEntry(ReportDataContent`.
11. Add a focused test at an existing public seam if refresh generation logic moves into a testable helper.
12. Do not create a new helper project or test runner.

## Boundaries

- Do not suppress a real data update.
- Do not add a timer or debounce delay.
- Do not animate the progress bar.
- Do not animate the full report body.
- Do not change view-model notification order.
- If the view model no longer exposes `IsLoading` and `Trend`, stop and report the drift.

## Verification

- **Mechanical**: Run `dotnet test tests\TokenUsage.Architecture.Tests\TokenUsage.Architecture.Tests.csproj -c Release -p:Platform=x64 --no-restore`.
- **Mechanical**: Run `powershell -ExecutionPolicy Bypass -File .\scripts\check.ps1 -Platform x64 -Configuration Release` at the final integration boundary.
- **Feel check**: Press refresh repeatedly during a slow load.
- **Feel check**: Make sure that one exit and one entry occur for the final refresh.
- **Feel check**: Change a filter while refresh is active.
- **Feel check**: Make sure that stale data never flashes after the final result.
- **Feel check**: Make sure that a chart-only update does not fade other report sections.
- **Done when**: Each load generation produces at most one exit and one entry.

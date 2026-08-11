# 001 — Tighten the report motion tokens

- **Status**: DONE
- **Commit**: 43e54ce
- **Severity**: MEDIUM
- **Category**: Easing and duration
- **Estimated scope**: 2 files, small change

## Problem

The report uses a 320 ms entry. This duration exceeds the 300 ms budget for frequent UI actions.

```csharp
// src/TokenUsage.App/Controls/MotionSettings.cs:15 — current
public static readonly TimeSpan ReportSwitchExitDuration = TimeSpan.FromMilliseconds(180);
public static readonly TimeSpan ReportSwitchDuration = TimeSpan.FromMilliseconds(320);
public const double ReportSwitchMinimumOpacity = 0.08;
public const double ReportRefreshMinimumOpacity = 0.58;
public const double ReportSwitchOffset = 12;
```

The report exit also uses `EaseInOut`. Entry and exit transitions must start fast.

```csharp
// src/TokenUsage.App/Views/Reports/UsageReportPage.xaml.cs:423 — current
StartTransition(
    element,
    transform,
    Math.Clamp(element.Opacity, 0, 1),
    minimumOpacity,
    transform.TranslateX,
    -exitOffset,
    MotionSettings.ReportSwitchExitDuration,
    EasingMode.EaseInOut,
    resetOnComplete: false,
```

## Target

Use the same crisp timing as the compact provider view.

```csharp
public static readonly TimeSpan ReportSwitchExitDuration = TimeSpan.FromMilliseconds(140);
public static readonly TimeSpan ReportSwitchDuration = TimeSpan.FromMilliseconds(240);
public const double ReportSwitchMinimumOpacity = 0;
public const double ReportSwitchOffset = 12;
```

Use `EasingMode.EaseOut` for report entry and exit. Keep `ReportSwitchOffset` only for a spatial content swap.

Delete `ReportRefreshMinimumOpacity` after plan 004 removes the full-report refresh fade.

## Repo conventions to follow

The compact provider transition defines 140 ms exit and 240 ms entry tokens in `MotionSettings.cs:25-29`.

`CompactUsageDashboard.xaml.cs:248-303` uses `CubicEase` with `EasingMode.EaseOut` for both phases.

## Steps

1. Set `ReportSwitchExitDuration` to 140 ms in `MotionSettings.cs`.
2. Set `ReportSwitchDuration` to 240 ms in `MotionSettings.cs`.
3. Set `ReportSwitchMinimumOpacity` to `0` in `MotionSettings.cs`.
4. Change report exit easing from `EaseInOut` to `EaseOut` in `UsageReportPage.xaml.cs`.
5. Keep translation disabled for refresh fades in plans 003 and 004.
6. Delete `ReportRefreshMinimumOpacity` after its last call site is gone.

## Boundaries

- Do not change compact dashboard timing.
- Do not change quota or donut reveal timing.
- Do not add a motion library.
- Do not animate layout properties.
- If these token names no longer exist, stop and report the drift.

## Verification

- **Mechanical**: Run `dotnet test tests\TokenUsage.Architecture.Tests\TokenUsage.Architecture.Tests.csproj -c Release -p:Platform=x64 --no-restore`.
- **Mechanical**: Run `powershell -ExecutionPolicy Bypass -File .\BuildAndRun.ps1 src\TokenUsage.App\TokenUsage.App.csproj -SkipRun /p:Platform=x64 /p:Configuration=Release`.
- **Feel check**: Open the packaged report and switch each tab ten times.
- **Feel check**: Make sure that entry and exit respond without a slow start.
- **Feel check**: Enable reduced motion and make sure that positional movement is absent.
- **Done when**: Every frequent report transition completes in less than 300 ms.

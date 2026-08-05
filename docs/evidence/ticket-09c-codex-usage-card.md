# Ticket 09C: Codex usage card

Date: 2026-07-22

Status: implemented and verified with synthetic data.

## Result

The compact Codex card now shows:

- quota remaining, reset time, and animated progress;
- pace for each window when reset and duration can support it;
- today, yesterday, 7-day, and 30-day local token totals;
- `No data` / `Sin datos` for missing periods and `0 tokens` for an observed zero;
- a partial-use notice while keeping fresh quota visible.

Pace stays out of the snapshot cache. The projector computes it from the cached
quota window and the current clock. Shared Codex metric IDs keep the provider
writer and UI reader aligned.

## UI and accessibility

Daily rows and pace lines have stable AutomationIds. Long values trim or wrap
inside the 320-DIP flyout. State text carries the meaning without depending on
color. The synthetic catalog includes normal, near-limit, and partial data.

`tests/ui/ticket-09c-codex-usage.ps1` passed 4/4:

- usage and pace UIA;
- near-limit values and ETA;
- observed zero versus missing data;
- AutomationIds on interactive controls.

Reviewed captures:

- `artifacts/ticket-09c/01-usage-surface.png`;
- `artifacts/ticket-09c/02-near-limit.png`;
- `artifacts/ticket-09c/03-partial.png`.

## Automated proof

```text
scripts/check.ps1 -Platform x64 -Configuration Debug
  Architecture 22/22
  Core 41/41
  Providers 112/112
  Platform.Windows 48/48
  build 0 warnings/errors

scripts/check.ps1 -Platform ARM64 -Configuration Debug
  same test counts on the x64 host
  ARM64 build 0 warnings/errors

dotnet format TokenUsage.slnx --verify-no-changes --no-restore
git diff --check
tests/ui/ticket-09c-codex-usage.ps1
  4/4
```

The process-to-cache-to-card test also proves the four usage periods and both
pace severities with the fake Codex app-server.

## Review

Six read-only Grok Build reviews ran in parallel. The XAML, test, and
maintainability reviews completed. The projector, accessibility, and copy runs
hit their 12-turn cap, so their unfinished output did not count as acceptance.
Total reported Grok cost was US$2.336491.

Accepted findings added large-text fit, missing pace cases, full pace-state
mapping, live-composition resource coverage, synthetic 09C states, stable UIA,
and shared metric IDs. Parent tests and visual inspection provide the final
proof.

## Claim boundary

This slice does not call a real customer account. It does not prove a native
ARM64 run, a real-account local-day boundary, or screen-reader output. Those
remain opt-in or later release gates.

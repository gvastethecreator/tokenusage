# Ticket 07A — Sample-data UI evidence

Date: 2026-07-21
Baseline: `b33e65b`

## Accepted surface

- Sample mode starts off, lasts only for the process session, and returns to the honest empty state when disabled.
- The sample dashboard labels itself with a header badge, an explanatory InfoBar, a scenario caption, and a sample-data footer.
- Three deterministic scenarios cover normal use, near-limit quota, and partial or stale coverage.
- Each scenario includes Codex, Claude, Grok Build, OpenCode, and Antigravity CLI without claiming that a provider is connected.
- The option-1 hierarchy stays intact: total spend, provider headers outside their cards, quota or local-usage details, and a fixed footer.
- Native WinUI progress bars replace the reference donut in this slice. The 320-DIP flyout scrolls when the content exceeds the work-area clamp.

## Fixture consistency

| Scenario | Total | Provider spend sum |
|---|---:|---:|
| Normal | `$48.12` | `$48.12` |
| Near limit | `$96.40` | `$96.40` |
| Partial or stale | `$31.05` | `$31.05` |

Quota percentages describe remaining quota. Local tools without a quota source show tokens and sample spend or an explicit coverage note.

## Grok Build cycles

### Plan

- Session: `23f622fb-c4f0-45cc-9c7b-78edf4c4feb1`
- Stop reason: `EndTurn`
- Turns: 5
- Cost: USD 0.2281104
- Receipt: `.scratch/agent-cli-delegation/grok-build/runs/2026-07-22T02-32-34-217Z-plan-f1052f4a/result.json`

The parent accepted the App-only fixture boundary, three scenarios, a fixed footer, and native scroll. The parent corrected totals that did not add up and rejected a simulated donut or any live-provider implication.

### Review

- Session: `14a98a5a-b2d4-41d7-9b33-3baa108a94b0`
- Stop reason: `EndTurn`
- Turns: 7
- Cost: USD 0.249214
- Receipt: `.scratch/agent-cli-delegation/grok-build/runs/2026-07-22T02-57-36-206Z-review-db9f8887/result.json`

The review found no blocker and one high honesty issue: sample refresh temporarily used live-scan copy. The parent fixed it with sample-aware loading chrome and copy. The parent also closed the review gaps for deactivate-to-hide, locale-neutral UI assertions, near-limit emphasis, and provider-qualified automation names.

## Automated proof

- `tests/ui/ticket-07a-sample.ps1`: 10/10 passed.
- Ticket 05 tray regression: 12/12 passed.
- Platform tests: 21/21 passed.
- Architecture tests: 3/3 passed.
- WinUI analyzer x64 build through `BuildAndRun.ps1 -SkipRun`: passed.
- App ARM64 build: 0 warnings, 0 errors.
- Full x64 solution build: 0 warnings, 0 errors.
- Sample resource parity: 39/39 keys in `en-US` and `es-ES`.
- `git diff --check`: passed.

Machine-readable UI receipts:

- `artifacts/ticket-07a/ui-results.json`
- `artifacts/ticket-07a/regression-05-ui-results.json`

## Visual proof

- `artifacts/ticket-07a/01-options-sample.png`
- `artifacts/ticket-07a/02-normal.png`
- `artifacts/ticket-07a/03-near-limit.png`
- `artifacts/ticket-07a/04-partial-stale.png`

The parent compared `02-normal.png` with `docs/design/selected-flyout-option-1.png` in one visual review. The accepted mock keeps the reference order and card grammar. It adds clear sample disclosure and uses compact spend rows in place of the donut.

## Claim limits

- These fixtures prove UI behavior only. They do not prove provider discovery, quota retrieval, local spend parsing, cache behavior, or persistence.
- Current runtime proof covers the local machine in dark mode at 125% scale.
- Light mode, high contrast, 200% scale, alternate taskbar edges, and the wider release matrix remain Ticket 39 work.

## Revisión posterior 11A

El corte 11A reemplazó el InfoBar y las filas de gasto por un encabezado de
muestra más compacto, un donut real, marcas vectoriales y barras animadas. La
divulgación sigue visible en el badge y el footer. La evidencia vigente de esa
revisión está en `docs/evidence/ticket-11a-compact-animated-dashboard.md`; esta
sección no cambia el baseline histórico de 07A.

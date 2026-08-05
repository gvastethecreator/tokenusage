# Ticket 05 — Tray flyout evidence

Date: 2026-07-21

## Accepted surface

- The packaged WinUI app starts with no visible top-level window and keeps a tray icon alive.
- Mouse and keyboard tray activation open a 320-DIP flyout based on visual option 1.
- The flyout has real empty, loading, and options states. `Escape`, deactivate-to-hide, update, options, and exit are wired.
- Placement uses the tray icon bounds when Windows exposes them, the cursor as fallback, the selected monitor work area, and the window DPI after the first physical placement.
- English and Spanish resources cover the visible shell and native tray menu.

## Delegated review

Grok Build ran in read-only plan mode because prior Windows edit sessions stopped before producing a safe patch.

- Session: `410d8820-7211-4c99-97f9-35c03eab2fb0`
- Stop reason: `EndTurn`
- Turns: 8
- Cost: USD 0.3383356
- Receipt: `.scratch/agent-cli-delegation/grok-build/runs/2026-07-22T01-49-54-580Z-plan-64d21353/result.json`

Parent review rejected two parts of the proposal before implementation:

- `GetDpiForMonitor` was replaced with `GetDpiForWindow` after the first placement, which follows the DPI context owned by the real WinUI window.
- A fixed `(0, 0)` monitor fallback was replaced with the current cursor position. This avoids choosing the wrong monitor when the tray rectangle is unavailable.

## Automated proof

- `dotnet test tests/TokenUsage.Platform.Windows.Tests/TokenUsage.Platform.Windows.Tests.csproj -p:Platform=x64 --nologo`: 21/21 passed.
- `dotnet test tests/TokenUsage.Architecture.Tests/TokenUsage.Architecture.Tests.csproj -p:Platform=x64 --nologo`: 3/3 passed.
- `dotnet build src/TokenUsage.App/TokenUsage.App.csproj -p:Platform=ARM64 --nologo`: 0 warnings, 0 errors.
- `BuildAndRun.ps1 -SkipRun /p:Platform=x64`: 0 warnings, 0 errors, including Windows App SDK analyzers.
- `tests/ui/ticket-05-flyout.ps1`: 12/12 checks passed.

The UI check covers hidden launch, tray discovery, native context commands, options, empty and loading states, automation IDs, `Escape`, keyboard focus, mouse toggle, update, and clean exit. Machine-readable results live at `artifacts/ticket-05/ui-results.json`.

## Visual proof

- `artifacts/ticket-05/01-empty.png`
- `artifacts/ticket-05/02-loading.png`
- `artifacts/ticket-05/03-options.png`

The checked runtime used Windows dark mode at 125% display scale. The screenshots match the chosen option-1 structure: 320-DIP surface, section heading outside the card, one main card, 14-DIP outer margin, and compact footer.

## Claim limits

- This gate proves the current machine, monitor, dark theme, and 125% scale.
- Light theme, high contrast, 200% scale, alternate taskbar edges, Explorer restart recovery, and multi-monitor movement remain later matrix work.
- The empty state contains no quota or spend claim. Mock data belongs to Ticket 07 and must carry a visible sample-data label.

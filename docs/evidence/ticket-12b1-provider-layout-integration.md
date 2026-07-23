# Ticket 12B1: provider layout integration

Status: implemented and verified in the packaged x64 app with synthetic data.
Metric-level layout remains in Ticket 12B2. Undo, reset and the full keyboard
recovery flow remain in Ticket 12C.

## Delivered behavior

- The app loads the versioned layout from its local data folder before edits.
- A fixture-only argument can redirect that file for packaged UI tests.
- The dashboard reconciles saved entries with the providers in each snapshot.
- Users can move, hide and highlight provider cards from a compact native
  Expander in Options.
- Each change saves before the projected dashboard changes.
- Corrupt input is quarantined by the existing store. Future schemas and file
  access failures keep the controls read-only and show localized status.
- English and Spanish resources cover controls, state and recovery copy.
- Hidden cards stay absent after restart. Spend totals and the spend legend keep
  reporting the full source snapshot in this provider-card slice.

## Delegation and parent review

Grok Build received a bounded projector task in a repo-local snapshot. Two
broader attempts failed on the Windows file-read tool. The reduced retry wrote
one projector in two turns and reported USD 0.0589816. The parent added hostile
null checks, read-only result lists and focused tests before integration. Run
records remain under `.scratch/agent-cli-delegation/grok-build/runs/`; temporary
snapshot worktrees were removed from `.snapshots` after collection.

## Automated proof

Focused Release/x64 checks:

- `DashboardLayoutProjectorTests`: 8/8;
- `LocalizationContractTests`: 5/5;
- app build: zero warnings and zero errors;
- fixture solution and x64 MSIX build: passed.

Packaged UI Automation used `winapp run` and a layout file under
`.scratch/ui/ticket-12b1/`. It never ran the packaged executable directly.

- Configure phase: 3/3;
- second-process Verify phase: 2/2.

The checks prove dynamic AutomationIds and provider-specific accessible names,
move, hide, highlight, schema output, card projection, persisted toggle state
and reload after process restart. The layout list stays disabled while its file
loads, so user actions cannot race initialization.

Reviewed captures:

- `.scratch/ui/ticket-12b1/final-configure/01-layout-options.png`;
- `.scratch/ui/ticket-12b1/final-configure/02-layout-dashboard.png`;
- `.scratch/ui/ticket-12b1/final-verify/03-layout-restart.png`.

The 560 by 1260 capture shows the 320-DIP flyout without horizontal clipping.
The highlighted Grok card uses a star, localized accessible text and its provider
color. The Options rows keep each action at 28 DIPs and expose localized names.

Final `scripts/check.ps1 -Platform x64 -Configuration Release`:

- Architecture: 62/62;
- Core: 128/128;
- CLI: 82/82;
- Providers: 262/262;
- Platform Windows: 98/98;
- solution and x64 MSIX package build: passed.

An independent review first requested two repairs: disable edits during load and
name every action with its provider. After those changes and the second packaged
run, the review returned `ACCEPT` with no P0-P2 findings.

## Boundary

This cut does not reorder, hide or highlight metrics. It does not add drag,
undo or reset. Ticket 12B2 owns metrics and Ticket 12C owns recovery and the
complete keyboard alternative. ARM64 builds remain part of the final Ticket 12
gate; this UI proof ran on x64 hardware.

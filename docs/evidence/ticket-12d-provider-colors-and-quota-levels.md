# Ticket 12D — Provider colors and quota levels

## Result

- Quota bars now map the remaining allowance to four states: healthy above
  25%, caution from 15.01% through 25%, warning from 5.01% through 15%, and
  critical at 5% or less.
- Used-percent display keeps the same state because the bar stores the true
  remaining value apart from the displayed value.
- Each provider can save an optional `#RRGGBB` color. The dashboard donut and
  legend use its dark-to-base gradient.
- High contrast ignores custom colors and uses system brushes.
- The provider color picker has a stable automation id and an accessible name.
- Layout schema 2 writes provider colors and loads schema 1 documents without
  losing the old layout.

## Proof

- Core layout, store, migration, and quota policy: 60 passed.
- Dashboard and appearance projection: 16 passed.
- Provider palette architecture checks: 8 passed.
- Packaged WinUI Release x64 build with UI fixtures: passed.
- Runtime captures:
  - `.scratch/ui/provider-colors/dashboard.png`
  - `.scratch/ui/provider-colors/picker.png`

## Remaining checks

- Capture all four quota states in one runtime proof.
- Reopen the packaged app and confirm a changed provider color survives a new
  process.

# Ticket 13E — Internal option sections

## Result

- Options now opens on a compact four-entry home: General, Appearance,
  Dashboard, and Providers.
- Appearance exposes its controls directly instead of placing them inside an
  otherwise empty collapsed card.
- Dashboard owns provider order, visibility, metrics, and colors.
- Providers opens a second level for the Vercel connection and source health.
- Back navigation returns through the current level before closing Options.
- The window remeasures after every section change. Runtime heights were 386 px
  for Providers, 514 for the home, 574 for Appearance, 635 for General, and 875
  for Vercel. Short views showed no scrollbar.
- Existing control automation ids remain stable. UI scripts now select the
  required section before opening appearance or dashboard layout controls.
- Each navigation row has a localized accessible name and a Fluent glyph.

## Proof

- Packaged WinUI Release x64 build with UI fixtures: passed.
- Architecture tests: 62 passed.
- Focused flyout size tests: 9 passed.
- All 19 UI PowerShell scripts parse after their navigation updates.
- Packaged runtime navigation and dynamic height: passed.
- Nested keyboard Escape path Vercel → Providers → Home → dashboard: passed.
- Read-only review after the keyboard, focus, scroll, and sizing fixes: accepted
  with no P0–P2 findings.
- Captures:
  - `.scratch/ui/options-improve-ui/proof/after-home.png`
  - `.scratch/ui/options-improve-ui/proof/after-general.png`
  - `.scratch/ui/options-improve-ui/proof/after-appearance.png`
  - `.scratch/ui/options-improve-ui/proof/after-providers.png`
  - `.scratch/ui/options-improve-ui/proof/after-vercel.png`
- Improve UI records:
  - `.scratch/ui/options-improve-ui/context-card.md`
  - `.scratch/ui/options-improve-ui/finish-ledger.md`

## Runtime correction

The first packaged run exposed an initialization fault in the provider color
legend. Empty WinUI string bindings reached the gradient parser before the
control loaded. The swatch now waits for its visual tree and treats empty color
bindings as the provider default. The packaged app then opened and completed
the navigation proof.

The first section split only recalculated flyout size when Options opened. This
left a short view at the prior height and could force a scrollbar in General.
`MainWindow` now schedules the same two-pass measure and tray placement when
`ActiveOptionsSection` changes.

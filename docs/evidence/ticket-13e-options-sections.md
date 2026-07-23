# Ticket 13E — Internal option sections

## Result

- Options now opens on a compact General view.
- Appearance and Providers have separate internal views selected from a fixed
  three-button bar.
- Provider layout, provider colors, Vercel connection, and provider health stay
  inside Providers.
- Existing control automation ids remain stable. UI scripts now select the
  required section before opening appearance or dashboard layout controls.
- The active section uses an accent underline and each navigation button has a
  localized accessible name.

## Proof

- Packaged WinUI Release x64 build with UI fixtures: passed.
- Packaged runtime navigation through all three sections: passed.
- Captures:
  - `.scratch/ui/options-sections/general.png`
  - `.scratch/ui/options-sections/appearance.png`
  - `.scratch/ui/options-sections/providers.png`

## Runtime correction

The first packaged run exposed an initialization fault in the provider color
legend. Empty WinUI string bindings reached the gradient parser before the
control loaded. The swatch now waits for its visual tree and treats empty color
bindings as the provider default. The packaged app then opened and completed
the three-view navigation proof.

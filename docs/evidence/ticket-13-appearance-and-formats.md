# Ticket 13 — Appearance and display formats

Date: 2026-07-23

## Delivered

- Persisted theme: system, light, or dark.
- Persisted regular or compact density.
- Optional Acrylic transparency, disabled when high contrast or advanced effects require an opaque surface.
- Remaining or used quota display.
- Relative or exact reset display.
- Atomic schema-v1 storage with legacy migration, corrupt-document quarantine, and future-version refusal.
- A 440-DIP flyout with a stacked Appearance form below 360 DIPs.
- English and Spanish copy for settings, states, percentages, and reset values.

When exact time is selected but a provider has no reset timestamp, the UI says that the exact reset is unavailable. It does not retain a relative value under the exact setting.

## Automated proof

Focused tests:

- appearance model and store: 15/15;
- appearance dashboard projection: 4/4;
- flyout size policy: 9/9.

Packaged UI Automation uses `tests/ui/ticket-13-appearance.ps1`. The parent launched both processes with `winapp run`; it did not execute the packaged binary directly.

- Configure: 4/4.
- Verify after a fresh packaged process with the same JSON: 4/4.

The flow proves dark theme, compact density, transparency enabled, used quota, exact resets, the persisted schema-v1 document, restored control values, and dashboard projection. It also rejects relative reset text while exact mode is active.

Artifacts live under `.scratch/ui/ticket-13/run-v5/` and layout captures under `.scratch/ui/ticket-13-layout/`.

## Full gate

- Architecture: 62/62.
- Core: 149/149.
- CLI: 82/82.
- Providers: 278/278.
- Platform Windows: 101/101.
- Packaged Release x64: passed.
- Packaged Release ARM64: passed.
- `git diff --check`: passed.

The first ARM64 attempt exposed an invalid nullable `const` hidden by the fixture branch. The declaration now uses a normal nullable local, and both production architectures pass.

## Accessibility and review

The UI uses semantic theme resources. Provider colors have a HighContrast dictionary, the opaque surface uses `ApplicationPageBackgroundThemeBrush`, and the app keeps WinUI's default automatic high-contrast adjustment. Acrylic checks both `AccessibilitySettings.HighContrast` and `UISettings.AdvancedEffectsEnabled`.

The final independent review returned ACCEPT with no P0–P2 findings. Earlier review cycles found and closed:

- narrow Appearance labels that could clip;
- a wide-layout row overlap;
- an incorrect adaptive-state selection;
- a global high-contrast override that disabled WinUI's automatic adjustment.

Runtime visual proof covers 440 and 300 DIPs. High contrast has source and review proof; this ticket did not change the operating-system high-contrast setting during automation.

## Delegation record

Grok Build produced the isolated appearance model that the parent reviewed and integrated. A later Grok implementation run for UIA reached its turn cap without output and was discarded. The parent wrote and ran the test. A read-only Grok review also reached its turn cap, but its partial artifact exposed an English exact-reset false positive. Tightening that assertion found the product fallback bug described above.

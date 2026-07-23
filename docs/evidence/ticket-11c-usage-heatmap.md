# Ticket 11C — Daily usage heatmap

## Result

- Usage details now include a compact 35-day activity heatmap.
- Local usage uses stored daily rollups. Missing dates render as empty cells.
- Sample providers use deterministic fixtures so the UI remains testable without customer data.
- Intensity uses token volume on a square-root scale. Activity with no token count remains visible.
- Each cell exposes its date, token count, event count, and cost through UI Automation and a tooltip.
- High contrast uses system brushes and keeps empty and active cells distinct.
- The reveal animation respects reduced-motion settings.

## Proof

- Focused provider tests: 16 passed.
- Packaged WinUI Release x64 build: passed with 0 errors.
- `git diff --check`: passed; only existing line-ending notices were reported.

## Remaining visual proof

- Capture the expanded heatmap in light, dark, and high-contrast themes.
- Exercise keyboard focus and inspect its UI Automation tree in the packaged app.

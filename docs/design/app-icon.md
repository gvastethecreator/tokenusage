# TokenUsage app icon

## Concept

The icon combines a three-part quota ring with a central token. The open gaps
keep the remaining-usage idea visible at small sizes. Indigo, violet, and cyan
match the dashboard without using a provider brand.

## Source and generation

- Generated with the built-in image generator on 2026-07-23.
- Use case: `logo-brand`.
- The prompt requested an original geometric quota ring, no text, no letters,
  no provider marks, and a flat magenta background.
- The background was removed locally with the imagegen chroma-key helper. The
  no-despill result was selected because magenta despill desaturated the violet
  segment.
- Editable source: `docs/design/assets/TokenUsageIcon.source.png`.

## Packaged assets

The transparent source generates the existing MSIX asset names:

- `AppIcon.ico`: 16, 20, 24, 32, 40, 48, 64, 128, and 256 px.
- Square app marks: 24, 48, 88, and 300 px.
- Store and lock-screen marks.
- Wide tile and splash canvases with a centered mark.

The icon keeps transparent padding so Windows can apply its own tile and taskbar
treatments.

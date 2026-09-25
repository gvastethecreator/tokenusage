# Changelog

User-facing changes for TokenUsage. Release notes include installation details and known limits.

## Unreleased

### Added

- Claude Code subscription limits (5-hour and weekly, plus a gateway spend limit). They come from the documented status line reading and are opt-in in Settings. They appear in the Claude card, the global limits strip, the tray, provider status, `tokenusage limits`, and report reset cycles. Any existing Claude Code status line keeps running.
- A Claude Code `Stop` hook that refreshes TokenUsage after each task, managed like the Grok, Cursor, and ZCode hooks.
- `tokenusage claude <install-hook|uninstall-hook|install-statusline|uninstall-statusline|status|statusline>`.

## 0.0.1 Preview 2 — Unsigned portable

The first Windows x64 preview uses an unsigned portable ZIP. No MSIX installer is included. Stable releases remain subject to signing and install checks.

### Added

- Opt-in GitHub updates every 24 hours, with manual checks, progress, cancellation, retry, and installation after exit.
- Verified downloads, portable recovery backups, unchanged-file skipping, and trusted MSIX deployment.
- Local token and cost dashboards, a compact tray view, and CLI reports.
- Week, model, and two-to-four-cycle comparisons with provider labels and separate cost and coverage states.
- Six report chart styles, provider/model grouping, optional small-value scaling, and recorded-reset markers.
- Sortable full-result tables with active-day counts and clean report captures.

### Improved

- Portable startup includes the required Windows App SDK resource and third-party license notices. The first unpublished candidate was rejected during native startup checks.

- Report headers, action placement, chart controls, and cached-token breakdown placement.
- Options tabs, label-and-switch rows, animated panel sizing, and compact two-column quota cards.
- Quieter chart motion, clearer hover states, and keyboard access to report details.
- Usage accounting for idle periods and Grok records with an unknown model.

### Release checks

- Version, package identity, signature, asset size, and SHA-256 checks in the release pipeline.
- Stable publication remains gated on signing and install verification. Unsigned portable previews are labeled as pre-releases and are excluded from automatic delivery. No unsigned MSIX is published.

Read the [complete 0.0.1 release notes](docs/releases/0.0.1.md).

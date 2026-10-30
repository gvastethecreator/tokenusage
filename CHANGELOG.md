# Changelog

User-facing changes for TokenUsage. Release notes include installation details and known limits.

## Unreleased

### Fixed

- Correct Gemini 3.8 Flash output estimates in Cursor to $3.75 per million tokens.
- Show Codex quota windows by their reported duration; a 30-day window is no longer labeled Weekly.
- Show Claude's actual quota refresh error, with session renewal steps, and distinguish rate limiting from sign-in failures.
- Read Cursor's measured turn counters even when newer turns contain zero counters.
- Correct Sonnet 5.5 cache-read estimates to $0.10 per million tokens.
- Refresh Anthropic and Kimi prices from their official Markdown documents within the existing download limit.
- Stop packaged builds when Visual Studio MSBuild is missing, instead of reporting success from a skipped build.

- Build the portable resource index independently of the MSIX index so WinUI's theme resources are included at startup.

- Keep Codex's 5-hour reading when its keyed limits contain more information than the legacy summary. Refresh Claude's local quota reading every 30 seconds, including percentage changes with the same observation timestamp.
- Keep sample dashboards separate from previously collected usage.
- Close reports safely when Windows detaches the window's theme resources.
- Count Claude usage in reset-cycle totals. Claude records always held each message's time but marked it unknown; the schema 13 upgrade marks stored records, and new ones arrive marked. No logs are read again.
- Place reset cycles on the provider's schedule, so a reset that happened while TokenUsage was closed no longer stretches the next cycle backwards.
- Stop `tokenusage claude statusline` and provider hooks from waiting forever when their host is killed and leaves the input pipe open; they now read at most 2 seconds of input. Orphaned status line processes had piled up for hours.
- Run one hook refresh at a time. A Stop hook that fires during a refresh asks for one more pass and exits, so many agents finishing together no longer stack refresh processes.

### Removed

- The 44 catalog providers without a reader (33 prepared, 9 policy-blocked) and the OpenRouter and Vercel AI Gateway key connections. The catalog now lists only the 12 tools with a local reader.
- CLI commands `usage`, `providers`, `cursor`, `grok`, the `zcode` management subcommands, and `claude install-hook|uninstall-hook|enable-limits|disable-limits|recover-history|status`. Use `report`, `doctor`, and Settings instead; the app installs refresh hooks itself.
- The 7-day tile, the composition strip, and the separate cost-split line from the dashboard. The reported and estimated split is in the coverage hint.
- Optional session, project, tool, MCP, command, and file attribution, with its backfill, aliases, explorer, and operation views. Upgrading to usage schema 13 deletes the stored links, operation records, and consent; numeric usage is unchanged.
- Report views for saved comparisons, linear price scenarios, measurement details, activity windows, change explanations, and per-section toggles.
- `tokenusage report --format json-v2|csv|html` and `--selection-file`. `report` keeps `human` and `json` (`tokenusage.report.v1`).

### Changed

- Options has an Advanced tab for screenshots, Claude history recovery, and the dashboard layout.
- The report is rebuilt around three questions: will each quota last to its reset, why usage changed from the previous period, and how many tokens each quota point covered in recent resets. It keeps 7, 30, and 90-day periods, a daily chart by agent, and a detail table by agent, model, or day.
- Update Microsoft.Data.Sqlite to 10.0.13 and Microsoft.NET.Test.Sdk to 18.11.0.
- Keep unchanged usage refreshes from rewriting events, rebuilding daily totals, and invalidating reports.

### Added

- Price Gemini 4 Argon from Google and Cursor, including Google's published December 1 transition, and Muse Wonder from Cursor.
- Claude Code subscription limits (5-hour and weekly, plus a gateway spend limit). They come from the documented status line reading and are opt-in in Settings. They appear in the Claude card, the global limits strip, the tray, provider status, `tokenusage limits`, and report reset cycles. Any existing Claude Code status line keeps running.
- A Claude Code `Stop` hook that refreshes TokenUsage after each task, managed like the Grok, Cursor, and ZCode hooks.
- `tokenusage claude <install-statusline|uninstall-statusline|statusline>`.
- Separate Claude quotas for claude-swap profiles, next to code-swap profiles for Codex, in Settings → Providers → Accounts. TokenUsage reads the usage claude-swap already cached and sends no request of its own.
- GitHub Copilot usage from VS Code chat sessions (also Insiders and VSCodium). Output tokens and Copilot credits are measured; input is a lower bound unless per-model totals exist. Cost is the credit value before the plan allowance. Only numeric fields are read; messages and paths are not.

### Removed

- Upgrades from files that only builds older than Preview 1 wrote: appearance settings before schema 5, `quota-resets.v1.json`, and Codex scan checkpoints at schema 2. Such appearance or reset-history files are set aside unchanged and TokenUsage starts from defaults; such a Codex checkpoint is reported as unreadable and kept unchanged. Files written by Preview 1 or later are not affected.

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

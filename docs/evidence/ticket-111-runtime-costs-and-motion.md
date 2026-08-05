# Ticket 111: tray activation, local costs, usage details and motion

Status: implemented and verified in the packaged x64 app on 2026-07-23.

## Delivered behavior

- Primary tray selection accepts current and legacy notification packing. It
  collapses the related mouse messages into one action.
- The flyout asks Windows for foreground activation after it becomes visible.
  This keeps the focus-loss rule from hiding it at once.
- Claude JSONL reads use the final counters for each streaming message and the
  first timestamp. Advisor iterations become separate usage events.
- A complete Claude read replaces the exact recent 35-day event window. A
  partial read only updates events that were read, so it keeps the last sound
  total for unread files.
- The 30-day total and spend chart use the same inclusive civil-date window.
- Usage details place the 35-day heatmap at the top of the expanded section.
- Dashboard and Options navigation use a 200 ms ease-out transition. The
  system animation setting disables it.

## Cost source review

The implementation was checked against the local Codeburn reference:

- `.reference/codeburn/src/parser.ts` keeps the last usage counters for a
  streaming message and handles `advisor_message` iterations;
- `.reference/codeburn/src/day-aggregator.ts` groups local civil days;
- `.reference/codeburn/src/usage-aggregator.ts` combines stored history with
  the current day.

Before reconciliation, TokenUsage kept a Claude v1 parser event and reported
39.004111 USD. Codeburn 0.9.15 reported zero Claude cost for the same local
source. After the parser migration and exact range replacement, TokenUsage
reported 37.754111 USD across 14 events:

- Grok Build: 0.13 USD;
- OpenCode: 37.63 USD;
- Claude: 0 USD.

The dashboard showed the same rounded total, 37.75 USD, and the same two
provider slices.

## Runtime proof

The app launched through `winapp run` from the Release x64 package. It started
with no visible window. Invoking the `TokenUsage` notification icon selection
opened a 600 by 741 flyout, kept it visible after 900 ms and made it the
foreground window. `HeaderRefreshButton` was available.

The expanded local-usage card exposed `UsageProductCard.Heatmap` as a visible
538 by 147 group with 35 cells and the summary `9 días activos · últimos 35`.
The reviewed capture is:

- `.scratch/tokenusage/evidence/ticket-111-heatmap-final.png`.

Options measured 750 by 504 at the test display scale. General measured 750 by
605. Both views fit without a page scroll. Reviewed captures are:

- `.scratch/tokenusage/evidence/ticket-111-options-final.png`;
- `.scratch/tokenusage/evidence/ticket-111-general-final.png`.

The tray message contract follows the current Microsoft notification-area
rules for `NIN_SELECT`, mouse messages and icon ID packing:

- <https://learn.microsoft.com/windows/win32/api/shellapi/nf-shellapi-shell_notifyicona>
- <https://learn.microsoft.com/windows/win32/api/shellapi/ns-shellapi-notifyicondataa>

## Automated proof

`scripts/check.ps1 -Platform x64 -Configuration Release` passed:

- Architecture: 64/64;
- Core: 174/174;
- CLI: 82/82;
- Providers: 334/334;
- Platform Windows: 112/112;
- solution and x64 MSIX package build: passed.

Focused tray, repository, Claude parser, coordinator, heatmap and transition
checks also passed. `git diff --check` found no whitespace errors.

## Review boundary

The packaged runtime and local data path passed. The reduced-motion branch has
source and architecture coverage; this run did not change the user's Windows
animation setting. No independent agent review ran in this cycle.

# T3 Code usage dashboard study

Date: 2026-08-08

## Question

Which parts of the T3 Code usage dashboard fit TokenUsage without replacing its local data contracts?

## Primary sources

- T3 Code added the full page in [`UsagePage.tsx`](https://github.com/pingdotgg/t3code/blob/a20923ce/apps/web/src/components/usage/UsagePage.tsx). The page has 7, 30, and 90-day windows, cost and token modes, provider totals, five summary metrics, model and day tables, and cost-quality facts.
- The chart implementation is in [`UsageProviderChart.tsx`](https://github.com/pingdotgg/t3code/blob/a20923ce/apps/web/src/components/usage/UsageProviderChart.tsx). It fills missing dates, rounds the vertical scale up, uses shape-preserving cubic curves, layers providers from a shared zero line, and uses the plotted day columns for the hover values.
- The first transcript-backed implementation is commit [`8101cd04`](https://github.com/pingdotgg/t3code/commit/8101cd04). Its server reads local provider transcripts and returns one usage snapshot per environment.
- T3 Code merges environments in [`usageMerge.ts`](https://github.com/pingdotgg/t3code/blob/a20923ce/apps/web/src/usage/usageMerge.ts). It degrades failed environments separately and avoids counting a shared transcript source twice.
- Its pricing and transcript scanner remain server-side in [`usagePricing.ts`](https://github.com/pingdotgg/t3code/blob/a20923ce/apps/server/src/usage/usagePricing.ts) and [`usageTranscripts.ts`](https://github.com/pingdotgg/t3code/blob/a20923ce/apps/server/src/usage/usageTranscripts.ts).

## Findings

The valuable part is the information structure, not the React implementation. The compact provider view answers quota questions. A separate report window answers historical usage and cost questions without making the flyout larger.

The chart has three useful correctness rules:

1. Add quiet days to the selected period before plotting.
2. Use the largest provider-day for the vertical scale when series share a zero line.
3. Derive the line and hover values from the same daily collection.

TokenUsage already has a stronger durable source for this product. `usage.v1.db` stores daily rollups with agent, model, five token classes, reported cost, estimated cost, unpriced tokens, event count, and coverage. The report must query those rollups. It must not rescan Codex, Grok Build, Antigravity, or OpenCode inside the chart window.

## TokenUsage adaptation

- Open one resizable WinUI report window from the compact flyout.
- Keep 7, 30, and 90-day filters plus cost and token modes.
- Plot one daily series per installed agent from `UsageReportQuery`.
- Keep reported and estimated costs separate in the quality panel. The headline can show their explicit sum.
- Replace T3 Code's cache-savings estimate with price coverage. TokenUsage does not have enough stored price inputs to calculate a trustworthy full-input counterfactual.
- Keep partial coverage as a compact information hint with a tooltip.
- Use the existing provider refresh only when the user requests an update. Normal report opening remains a read-only SQLite query.

This keeps the implementation small: one query extension for daily agent totals, one report view model, one chart control, and one secondary window.

# CodeBurn CLI report study

Date: 2026-08-04

## Question

Which CodeBurn report ideas fit TokenUsage without expanding local collection into customer content?

## Sources checked

- CodeBurn's current [command reference](https://github.com/getagentseal/codeburn/blob/2c3319b2861142269cc44d71c2b711e118ce4bed/README.md) documents `report`, `overview`, named periods, exact date ranges, provider filters, and JSON output.
- Its current [overview renderer](https://github.com/getagentseal/codeburn/blob/2c3319b2861142269cc44d71c2b711e118ce4bed/src/overview.ts) renders totals, token types, providers, models, high-value days, projects, daily history, activities, and tools.
- The [0.9.19 changelog](https://github.com/getagentseal/codeburn/blob/main/CHANGELOG.md) explains that durable daily totals are shared across CLI, TUI, desktop, and web views.

The local reference clone was also inspected at commit `6e3c57a9ff95a624f1d9affa7384d32a67f359b7`. The upstream `main` commit checked for this note was `2c3319b2861142269cc44d71c2b711e118ce4bed`.

## Findings

CodeBurn has two useful report surfaces. Its interactive `report` is broad and its `overview` is a copyable text summary. Both support fixed or exact ranges. JSON is a separate stable automation surface.

The strongest design choice is not the terminal styling. It is that historical totals come from durable daily data. Detail that needs session content is allowed to be a smaller live subset.

TokenUsage already has the right durable source: `DailyUsageRollup`. It supports date, agent, model, five token classes, reported cost, estimated cost, unpriced tokens, unavailable-cost event count, and source coverage.

## Decision

Add `tokenusage report` with human and `tokenusage.report.v1` JSON output. It supports either `--days` or an inclusive `--from` and `--to` range, plus an optional `--agent` filter.

The report includes:

- totals and token-type shares;
- reported, estimated, and combined cost;
- price coverage and unpriced tokens;
- breakdowns by agent and model;
- highest-cost days and daily history.

Do not copy CodeBurn's project, session, task, prompt, tool, shell command, skill, or subagent sections. TokenUsage does not store that content. Adding those sections would break its privacy boundary and make old detail less durable than the headline totals.

## Resulting contract

The CLI reads the same SQLite rollups as the app. Human output is deterministic and has no color dependency. JSON keeps reported and estimated cost separate, even though it also provides their sum. Empty data returns exit code `4`, invalid options return `2`, and reader failures are redacted.

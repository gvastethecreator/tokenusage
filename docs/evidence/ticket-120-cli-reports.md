# Ticket 120: CLI reports

Date: 2026-08-04

## Delivered

- `tokenusage report` reads the shared `daily_usage_rollup` store.
- Human output includes totals, token types, agents, top models, high-cost days, daily history, and price coverage.
- `tokenusage.report.v1` exposes the same data as versioned JSON.
- `--days`, exact `--from` and `--to`, `--agent`, and `--format` are validated before storage is opened.
- Reported and estimated costs remain separate. Unpriced tokens and incomplete source coverage remain visible.

## Proof

- Roslyn parsed 277 active C# files without syntax errors.
- A focused semantic compilation of the report domain and CLI completed against the installed .NET 10 runtime reference assemblies.
- The compiled report command ran with synthetic daily rollups. Its JSON matched `tests/TokenUsage.Cli.Tests/Golden/tokenusage.report.v1.json` exactly. Human output, exact range, agent filter, invalid-input redaction, and exit codes also passed.
- The new golden JSON parsed successfully and `git diff --check` found no whitespace errors.

## Blocker

The canonical CLI test project did not run. The machine has .NET SDK 9.0.316 while the repository targets .NET 10.0, so restore stops with `NETSDK1045`. No SDK or tool was installed. The packaged x64 build remains pending for the same reason.

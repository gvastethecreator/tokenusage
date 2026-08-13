# Architecture and performance batch 2026-08-13

Approved contract. Do not reopen F9-F13. Do not write to Cursor or OpenCode databases. Report Refresh stays a full live refresh.

STE terms in this file: **comprobar**, **configuración**.

## Compact contract

```text
Artifact and user outcome: TokenUsage source with ARCH-01..10 and PERF-01..12 landed, proofs attached
Purpose and enabling outcome: smaller module seams; faster ingest, refresh, and scans; same cost, quota, and privacy meaning
Mission mode: change / complete approved batch
In scope: ARCH-01..10, PERF-01..12 as listed below
Out of scope: F9-F13 redo, xunit v3, .NET 11, HTTP API, Cursor DB writes, unrelated dependency commit
Baseline: ingest 10k median 1218 ms; Compile Include seam; per-event SQLite writes
Gates: scope, regression, structural, runtime on touched seams, one integration check, adversarial autopsy
Stop condition: every ticket finished or honestly blocked; no in-scope P1
```

## Tracker

Local workplan is the live queue. GitHub Issues were not created for this batch (implementation was approved in chat).

Frontier: batch complete. Issues 6–25 y tickets locales 128–147 están cerrados; Project 4 los muestra en Done. Release check, paquete, UI real y code-map pasaron. No hacer commit salvo pedido explícito.

## Architecture

| ID | Type | Blocked by | Outcome | Proof |
|---|---|---|---|---|
| ARCH-01 | AFK | None | `TokenUsage.Presentation` (`net10.0`, no WinUI). Tests project-reference it. No `Compile Include` of App sources. | Architecture.Tests 97/97; package Release x64 |
| ARCH-02 | AFK | ARCH-01 | Split `DashboardSurfaceViewModel`. | `CompactDashboardProjector` + focused projector test |
| ARCH-03 | AFK | ARCH-01 | Split report view model and page code-behind. Keep automation IDs if XAML changes. | `UsageReportRows.cs`; `UsageReportPage.ProviderTabs.cs` / `.Transitions.cs` |
| ARCH-04 | AFK | ARCH-01 | Split `CompactUsageDashboard` code-behind. Keep automation IDs. | `CompactUsageDashboard.ProviderTabs.cs` / `.Transitions.cs` |
| ARCH-05 | AFK | None | Codex and Vercel coordinators only `CreateRegistration()`. Tests use `RunProviderAsync`. | Platform.Windows.Tests coordinators 40 |
| ARCH-06 | AFK | None | One PasswordVault adapter in Platform.Windows. | Platform.Windows.Tests credentials |
| ARCH-07 | AFK | None | Order, display name, and mark come from the provider catalog. | ProviderPresentation catalog tests |
| ARCH-08 | AFK | None | Los wire types JSON estables permanecen en CLI; Core conserva consultas de dominio. | CLI 104/104; regla `CliOwnsStableJsonWireTypes` |
| ARCH-09 | AFK | None | Split Codex usage scanner into paths, scan, and map. Codex only. | CodexUsageEventSourceTests 19; Paths/Scan/Map files |
| ARCH-10 | AFK | None | Remove nested debug Vercel fakes from `MainPage`. Keep `AppComposition` as root. | Architecture composition test |

## Performance

| ID | Type | Blocked by | Outcome | Proof |
|---|---|---|---|---|
| PERF-01 | AFK | None | Batch ingest. Beat 1218 ms / 10k on this machine. Tombstone and `event_key` rules stay. | ingest 10k median 385 ms (`scripts/measure-ingest.cs`) |
| PERF-02 | AFK | None | Schema v4 indexes. Forward-only. Same rollups. `EXPLAIN QUERY PLAN` proves index use. | UsageRepositoryTests |
| PERF-03 | AFK | PERF-01 | Batch replace and reconcile. Complete snapshot still wipes the agent. Partial does not. Historical rollups and cross-day moves stay correct. | UsageRepositoryTests 28/28 |
| PERF-04 | AFK | None | Retention does not run on every `LocalUsageRefresh` pass. Same 400-day policy. | `RetentionIfDueSkipsWithinTheIntervalAndRunsAfterIt` |
| PERF-05 | AFK | None | Claude skips old files before JSONL parse. PartialScan. No prompts stored. | Claude skip-old-jsonl test |
| PERF-08 | AFK | None | Cursor read-only query narrowing. No WRITE to `state.vscdb`; composer/bubble y formatos ISO/numeric-text/real. | 16/16; 123→22 ms y 10100→100 filas |
| PERF-09 | AFK | None | OpenCode drops the per-message correlated subquery. Read-only. | 16/16; 5504→38 ms, checksum idéntico |
| PERF-10 | AFK | None | One forced live refresh per process. Cache-first. Codex limits if present. | `HasRequestedForcedRefresh`; SessionModuleTests |
| PERF-11 | AFK | ARCH-02 | Fewer dashboard rebuilds per refresh. Tray equals panel. Cost states stay distinct. | `InOnePass`; `CompactDashboardProjectorTests` |
| PERF-12 | AFK | None | Report Refresh stays full live. Speed the pipeline. Cost and scope unchanged. | `UsageReportQuery.FilterByAgent` equals agent query |

## Verification

- Comprobar each ticket with the narrowest test that can fail the claim.
- `scripts/check.ps1 -Platform x64 -Configuration Release` pasó sobre el código final: Architecture 97, Core 233, CLI 104, Providers 490, Platform 157; total 1081/1081 y paquete correcto.
- Revisión independiente: `ACCEPT` después de reparar composer timestamps, oversized y rollups cross-day.
- UI empaquetada: datos reales de cinco proveedores; Global/Provider comprobado con teclado.
- Do not run the packaged executable directly.
- Do not mix the unrelated WinApp / Actions dependency edits into this work unless asked.
- Reports: `docs/architecture/architecture-review-2026-08-13.md`, `docs/performance/performance-review-2026-08-13.md`.

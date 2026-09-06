# Competitor ideas and official-contract recheck

Reviewed public documentation on 2026-09-06. These are feature/contract observations, not performance benchmarks, security endorsements or an assertion that the same access is available to every account. External projects remain read-only research references. No source code is copied into TokenUsage by this PR.

| Reference | Useful documented idea | TokenUsage adaptation | Boundary |
|---|---|---|---|
| [ccusage blocks](https://ccusage.com/guide/blocks-reports) | Session blocks, burn rate, gaps, JSON and cost calculation modes. | Equal exposure from cycle start, gap disclosure, replayable export. | A reconstructed block or historic maximum is not an authoritative allowance. The docs now say `blocks --live` was removed in v18; use the current statusline reference rather than suggesting the removed command. |
| [CodexBar](https://github.com/steipete/CodexBar) | Lightweight quota/reset visibility and provider status, with credits/spend where sources support it. | Keep the tray focused and disclose freshness; open evidence on demand. | Do not copy browser-cookie or private-session access paths that conflict with TokenUsage policy. |
| [OpenUsage](https://github.com/janekbaraniewski/openusage) | Unified usage/spend views and a separated collection/interface architecture. | Shared Core interpretations for UI and headless/CLI workflows; explicit collection gaps. | A mandatory daemon, remote backend or more providers is not needed for the first milestone. |
| [CodeBurn](https://github.com/getagentseal/codeburn#compare-models) | Model comparison includes retries, cache hit rate, per-call/per-edit cost and fast-mode signals. | Configuration cohorts and resource per accepted task; show confounders. | Edit success and timestamp correlation with commits do not establish overall quality or causal attribution. Do not ingest tool/command content to copy its full workflow. |
| [Tokscale](https://github.com/junhoyeo/tokscale) | Local multi-agent token analysis and temporal exploration. | Efficient drill-down and consistent model/source breakdowns. | Token volume is not productivity; leaderboards are not the measurement objective. |
| [Langfuse experiments](https://langfuse.com/docs/evaluation/experiments/experiments-via-sdk) | Dataset-based runs, experiments and evaluation results. | Optional versioned task suites and reproducible outcome metadata. | This is adjacent evaluation infrastructure, not subscription-quota telemetry; do not add its cloud/SDK as a dependency merely to adopt the idea. |

## Official contracts that materially constrain implementation

[Codex app-server](https://learn.chatgpt.com/docs/app-server), rate-limit and token-usage sections: account usage exposes optional daily buckets; the rate-limit map identifies metered buckets using limit IDs. Do not transform daily buckets into request-level timestamps or deduplicate independent IDs only because their readings coincide. The documented reset-credit detail list can be capped and `availableCount` remains authoritative: F17/TU-CMP-022 specifically addresses a mismatch with the current mapper. Read-only monitoring must not call credit-consuming or notification endpoints.

[Claude monitoring](https://code.claude.com/docs/en/monitoring-usage): OpenTelemetry is opt-in, but standard attributes can include emails, account/organization identifiers and custom resource attributes. A collector must allowlist before persistence, handle temporality and choose one accounting representation when metrics/events overlap. This interface is not proof of subscription quota consumption. Keep it optional and policy-gated.

## Product differentiation

Prioritize reproducibility and valid comparisons over the number of dashboards. A user should be able to inspect the precise supporting interval, source scope, pool membership, missing data, parser/pricing revision and reasons a metric is withheld. Do not claim competitors lack a capability unless it has been tested; the table identifies ideas, not a complete competitive gap matrix.

Choose sequence: preserve evidence → align sources and pools → compare like-for-like exposure → explain recorded factors → optional evaluated tasks and calibrated drift detection. Copying more surfaces before those steps would make existing ambiguity more persuasive, not more correct.

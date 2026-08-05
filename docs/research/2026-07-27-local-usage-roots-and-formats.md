# Local usage roots and formats

Date: 2026-07-27

## Question

Which local sources can TokenUsage use for Codex, Grok Build, and OpenCode, and why do they fail to reach the spend donut today?

## Answer

- OpenCode still stores session token and cost totals under `%USERPROFILE%\.local\share\opencode`. The installed database matches the aggregate session schema that the Windows reader supports.
- Grok Build keeps a compact append-only log at `~/.grok/logs/unified.jsonl`. OpenUsage reads that log before any session format. TokenUsage now follows that path, tracks model changes per process, reads `shell.turn.inference_done` counters, and prices known models. Current `_x.ai/session/update` snapshots remain a bounded compatibility path.
- Codex exposes daily account token totals through `account/usage/read`. Its state DB maps each session file to a model, while the last 64 KB of each JSONL file usually contains a recent content-free token counter. TokenUsage uses the official daily total, samples the local model and token mix from those bounded tails, then applies public API rates. This keeps the token total authoritative and marks the cost as an estimate.
- The donut only includes providers with a reported or estimated cost. Token-only Codex data cannot appear until the local reader prices known models.

## Primary sources

- The [Codex app-server reference](https://github.com/openai/codex/blob/main/codex-rs/app-server/README.md) defines `account/usage/read` as an account token-activity summary with daily buckets. It also states that ephemeral threads have no file path, so a disk scan cannot cover them.
- [Codex issue 23340](https://github.com/openai/codex/issues/23340) documents inflated `threads.tokens_used` values when nested agent spans share a process. TokenUsage does not use that column.
- The [OpenCode troubleshooting guide](https://opencode.ai/docs/troubleshooting/) lists `%USERPROFILE%\.local\share\opencode` as the Windows data root. The [OpenCode CLI reference](https://github.com/anomalyco/opencode/blob/dev/packages/web/src/content/docs/cli.mdx) states that `opencode stats` reports session tokens and cost.
- The [Grok Build settings guide](https://docs.x.ai/build/settings) defines `~/.grok` and `GROK_HOME`. xAI does not publish a stable schema for the session usage JSONL files.
- The pinned OpenUsage [Grok scanner](https://github.com/robinebers/openusage/blob/9d2bf09f10e21f769494a525a9d65c84d7aeb1df/Sources/OpenUsage/Providers/Grok/GrokLogUsageScanner.swift) reads `logs/unified.jsonl`, maps model changes by process, and prices each inference row.
- The pinned OpenUsage [OpenCode scanner](https://github.com/robinebers/openusage/blob/9d2bf09f10e21f769494a525a9d65c84d7aeb1df/Sources/OpenUsage/Providers/OpenCode/OpenCodeUsageScanner.swift) queries local SQLite rows by date and uses OpenCode's stored cost.
- The pinned OpenUsage [Codex scanner](https://github.com/robinebers/openusage/blob/9d2bf09f10e21f769494a525a9d65c84d7aeb1df/Sources/OpenUsage/Providers/Codex/CodexLogUsageScanner.swift) parses rollout JSONL and uses a persistent cache keyed by path, size, and modification time.
- The [OpenAI model comparison](https://developers.openai.com/api/docs/models/compare) lists the per-million-token prices for GPT-5.6 Sol, Terra, and Luna. The [GPT-5.4 mini model page](https://developers.openai.com/api/docs/models/gpt-5.4-mini) provides its input, cached-input, and output rates. TokenUsage labels values derived from these prices as estimates because plan charges can differ.

## Local inspection

The inspection used installed CLI versions and read only paths, table columns, JSON object keys, model IDs, timestamps, and numeric counters. It did not read auth files, prompts, responses, tool calls, commands, or project content.

- `codex-cli 0.145.0`: `state_5.sqlite` maps indexed sessions to their rollout path and current model. A 64 KB tail found token records in nearly all indexed files. The official local API total was materially lower than the raw sum of per-session cumulative counters, which confirms that those counters cannot serve as an account total.
- `grok 0.2.112`: `logs/unified.jsonl` contains the model-change and inference rows used by OpenUsage. Current session updates also keep `_x.ai/session/update.usage`, including token counts, `costUsdTicks`, and per-model totals.
- `opencode 1.18.7`: `opencode.db` contains session rows with model, cost, input, output, reasoning, and cache counters. The existing reader returns real rows from that schema.

## Limits and safety

- Codex token totals come from its official local API. Disk sampling only estimates the model and token-category mix; ephemeral threads can reduce that sample coverage. Unknown model IDs stay unpriced.
- Codex cost uses public API rates and can differ from subscription or workspace charges.
- Grok unified rows use pinned OpenUsage rates and remain clearly marked as estimated. The session compatibility path keeps provider-reported `costUsdTicks` when present.
- Readers keep file-count, file-size, and line-size limits and open files with shared-read access.
- Parsers select only counters, model IDs, and times. They must never persist surrounding JSON content.

## Implementation decision

Use the OpenUsage source shape where it matches the installed Windows data: Grok reads `unified.jsonl` first and OpenCode queries SQLite by date. Codex keeps `account/usage/read` as the total because the local corpus is large, child rollouts can replay counters, and ephemeral threads may lack files; bounded session tails only provide model mix. Persist normalized rollups in TokenUsage SQLite and publish that cache before starting a fresh scan. Register all three as local usage sources. Remove Vercel AI Gateway from active composition and visible options while retaining its code for later work.

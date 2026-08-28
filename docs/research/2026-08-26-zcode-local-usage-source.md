# ZCode local usage database gate

Date: 2026-08-26

Decision: `allow` with conditions

## Executive result

ZCode now stores per-request usage counters in a local SQLite database. The counters have no conversation content. TokenUsage can read them through a strict column allowlist.

This gate reopens the ZCode provider for local usage. The remote Coding Plan quota stays blocked under the Z.ai provider.

The local observation found ZCode `3.8.1` with agent database schema migrations up to version 18. The observation read only the `model_usage` table. It did not read credentials, transcripts, prompts, responses, tool calls, or workspace content.

## What changed since the 2026-08-11 gate

The earlier gate found no safe local source in ZCode `3.7.6`. The hooks carried no tokens, and the published paths held support data or content.

The current build keeps a usage ledger at `%USERPROFILE%\.zcode\cli\db\db.sqlite`. The `model_usage` table stores one row per model request:

- `id`, `started_at` (epoch milliseconds, UTC), `model_id`, `provider_id`
- `input_tokens`, `output_tokens`, `reasoning_tokens`
- `cache_creation_input_tokens`, `cache_read_input_tokens`
- status and retry metadata

Counter semantics, checked on 3,047 live rows:

- `input_tokens` includes the cached input. `input_tokens` is never below `cache_read_input_tokens`.
- The database total equals `input_tokens + output_tokens`. Reasoning sits inside output.
- No row on the observed machine carries a money cost. Plan credits are not stored as a billed value.

## Reader contract

The reader implements the rules below. A future review must reject the reader if any rule stops holding.

1. Read the database in read-only mode with `PRAGMA query_only=ON`.
2. Select only the eight counter columns listed above from `model_usage`. Never use `SELECT *`.
3. Never open these surfaces: `v2\credentials.json`, the `part`, `message`, `input_history`, and `session` tables, and `cli\rollout\*.jsonl`. They hold credentials, transcripts, prompts, or workspace paths.
4. Store no raw session IDs and no workspace paths. Event keys are SHA-256 hashes of the row id.
5. Make sure that the required columns exist before you read. A changed schema degrades to `UnsupportedSchema`. The parser never invents numbers.
6. Stamp events with a parser version. A parser change reconciles and replaces stored rows.
7. Make no network call. Detection only checks that the root or the database file exists.

## Terms re-evaluation

The current terms took effect on 2026-06-15. The service provider is JINGSHENG HENGXING TECHNOLOGY PTE.LTD.

The terms bar access to data, content, or accounts through the service without permission. They also bar unauthorized servers or accounts.

TokenUsage reads a usage-metrics table on the user's own disk. It sends nothing, reads no account data, and opens no service resource. This is the same boundary TokenUsage uses for the Cursor `state.vscdb` allowlist projection.

Risk kept on record: Z.ai does not publish the database schema. The schema can change without notice. The column check and the parser version keep the reader fail-closed when that happens.

If Z.ai publishes an official usage API or export, TokenUsage must move to it and retire this reader.

## Refresh trigger (opt-in Stop hook)

ZCode documents a local hook protocol. Its `Stop` event fires when the main
model finishes a task. TokenUsage can register one async `command` hook in
`~/.zcode/cli/config.json` through `tokenusage zcode install-hook`.

The hook is a trigger only:

- The event payload can carry conversation content. TokenUsage drains stdin
  and discards it. Nothing is logged, printed, or stored.
- The hook runs `tokenusage zcode stop-hook`, which refreshes TokenUsage's own
  usage database through the allowlist readers above.
- The entry is async and non-blocking, with a 60-second timeout.
- Uninstall (`tokenusage zcode uninstall-hook`) removes only the TokenUsage
  entry and keeps every other user hook intact.

A hook collector that stores content fields remains out of scope.

## Cost handling

ZCode records no money cost. Billing runs through GLM Coding Plan credits.

TokenUsage shows catalog-estimated cost from the public Z.ai API rates for GLM models. The rates come from the official pricing page and carry a dated catalog version. Models without a published rate stay `Unpriced`. TokenUsage never converts plan credits into an invoice estimate.

## Scope

Allowed now:

- Local per-request tokens by model through the allowlist reader.
- Catalog-estimated API-rate cost, labeled as an estimate.

Still blocked:

- Coding Plan quota, five-hour and weekly pools. These need an authorized public contract.
- The Z.ai monitor endpoints used by the official plugin.
- Any hook collector that stores content fields.

## Primary sources

- [ZCode Terms of Service](https://zcode.z.ai/en/terms)
- [Z.ai API pricing](https://docs.z.ai/guides/overview/pricing)
- Previous gate: [ZCode usage source research](2026-08-11-z-code-usage-source.md)
- Original gate: [ZCode source gate](2026-07-22-zcode-source-gate.md)

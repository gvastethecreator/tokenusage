# OpenUsage local usage gap

Date: 2026-08-04

## Question

Why can local token use and cost fail to reach the TokenUsage spend ring? Which OpenUsage behavior can close the gap for Codex, Grok Build, OpenCode, and Antigravity?

## Answer

OpenUsage has local spend readers for Codex, Grok Build, and part of OpenCode. It does not read local tokens or spend for Antigravity. Its Antigravity integration reports quota fractions through private, reverse-engineered interfaces. TokenUsage must not copy that credential path.

TokenUsage already finds and reads real Codex, Grok Build, and OpenCode data on this Windows machine. The remaining shared risks are source isolation, fallback behavior, and price coverage. Antigravity needs a separate passive SQLite reader.

## Primary sources

- The pinned OpenUsage [Codex scanner](https://github.com/robinebers/openusage/blob/9d2bf09f10e21f769494a525a9d65c84d7aeb1df/Sources/OpenUsage/Providers/Codex/CodexLogUsageScanner.swift) reads rollout JSONL. It derives deltas, removes copied parent counters, resolves model prices, and keeps an incremental file cache.
- The pinned OpenUsage [Grok scanner](https://github.com/robinebers/openusage/blob/9d2bf09f10e21f769494a525a9d65c84d7aeb1df/Sources/OpenUsage/Providers/Grok/GrokLogUsageScanner.swift) reads `logs/unified.jsonl`. It maps model changes by process and prices inference rows.
- The pinned OpenUsage [OpenCode scanner](https://github.com/robinebers/openusage/blob/9d2bf09f10e21f769494a525a9d65c84d7aeb1df/Sources/OpenUsage/Providers/OpenCode/OpenCodeUsageScanner.swift) queries local SQLite rows and uses the cost that OpenCode stores.
- OpenUsage [documents Antigravity](https://github.com/robinebers/openusage/blob/9d2bf09f10e21f769494a525a9d65c84d7aeb1df/docs/providers/antigravity.md) as quota-only. It states that token and dollar tiles are not available.
- OpenUsage uses a shared offline-first [pricing store](https://github.com/robinebers/openusage/blob/9d2bf09f10e21f769494a525a9d65c84d7aeb1df/Sources/OpenUsage/Pricing/ModelPricingStore.swift) and a bounded [model resolver](https://github.com/robinebers/openusage/blob/9d2bf09f10e21f769494a525a9d65c84d7aeb1df/Sources/OpenUsage/Pricing/ModelPricing.swift).
- The independent AgentsView parser documents passive Antigravity `gen_metadata` extraction in [`internal/parser/antigravity.go`](https://github.com/kenn-io/agentsview/blob/1ee2de88e2dae54326d8b47aeb2de2f58b5944f9/internal/parser/antigravity.go). It maps protobuf fields to uncached input, output, cache-read tokens, and model name.
- Google's official [Gemini pricing](https://ai.google.dev/gemini-api/docs/pricing?hl=en) lists Gemini 3.6 Flash at $1.50 input, $0.15 cached input, and $7.50 output per million tokens.
- Anthropic's official [Claude pricing](https://platform.claude.com/docs/en/about-claude/pricing) lists Claude Sonnet 4.6 at $3 input, $0.30 cache read, and $15 output per million tokens.

## Current Windows inspection

The inspection read only roots, SQLite schemas, model IDs, timestamps, and numeric counters. It did not read prompts, responses, commands, credentials, or transcript text.

- The production composition now contains five sources: Claude plus the requested Codex, Grok Build, OpenCode, and Antigravity. See `src/TokenUsage.Runtime.Windows/Providers/WindowsProviderCatalog.cs`.
- Grok returned 1,366 priced events from its real root. OpenCode returned four priced aggregate events from its real root.
- Codex returned daily account totals, but only 12 of 100 daily model events had a priced local model sample. The other events remained unpriced.
- The Antigravity roots contain 24 conversation databases. They share `gen_metadata(idx INTEGER, data BLOB, size INTEGER)` and contain 1,049 metadata rows.
- The same Antigravity databases contain `steps(idx, step_type, step_payload, ...)`. This permits a passive, bounded reader without Credential Manager, network, RPC, or process automation.
- The implemented reader recovered 1,043 real generations: 85,484,532 tokens and $21.589027 in API-rate estimates. It produced one Antigravity spend arc and two model rows through scanner -> SQLite rollup -> card -> spend ring. Six undecodable rows keep the source explicitly partial.

## Local causes

1. `LocalUsageRefresh.RefreshAsync` waits for every source with one `Task.WhenAll`. An unexpected source exception aborts all valid provider results.
2. Grok returns early when `unified.jsonl` is partial, even when it produced no events and session snapshots are valid.
3. OpenCode can let an unreadable database prevent its legacy JSON fallback.
4. The spend ring intentionally excludes a provider when its combined reported and estimated cost is zero. Token-only data still appears in details but not as an arc.
5. Before this change, no production Antigravity source existed, so it could not create a rollup or ring arc.

## Decision

- Keep Codex, Grok Build, OpenCode, and Antigravity as independent sources. One failure must not remove another provider's data.
- Keep reported cost ahead of estimates. Preserve unpriced tokens instead of hiding them.
- Repair the Grok and OpenCode fallbacks.
- Add a passive Antigravity SQLite reader. It can read only `gen_metadata` and the matching step timestamp. It must use bounded protobuf decoding and exact model pricing.
- Do not call private Antigravity quota interfaces. Do not read its credentials.
- Keep pricing offline and versioned for this change. A larger shared catalog remains separate unless current model IDs prove that it is required.
- Treat a partial Grok `unified.jsonl` that yielded events as a lower bound. Do not merge session snapshots because they can overlap and double count the same inference.

## Uncertainty

- Antigravity does not publish the `gen_metadata` protobuf schema. The reader must mark undecodable rows as partial and retain the last reliable snapshot.
- API-rate estimates do not equal subscription charges. The UI must keep the estimate label.
- Local formats can change. Parser versions and focused fixtures must make those changes visible.
- This checkout exposes .NET SDK 9 while the solution targets .NET 10. The changed production sources and focused tests compile through the installed .NET 10 PowerShell runtime, and the real scanner-to-ring path ran successfully, but a current packaged x64 build remains unverified until SDK 10 is available.

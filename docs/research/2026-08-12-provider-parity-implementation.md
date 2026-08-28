# Provider parity implementation

Date: 2026-08-12

## Result

TokenUsage registers the inspected union of providers from:

- [OpenUsage](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab)
- [CodexBar](https://github.com/steipete/CodexBar/tree/26ebaf9d5b0949e3b57fafcde0ed54aa3b27b3d2)
- [CodeBurn](https://github.com/getagentseal/codeburn/tree/d78bdb21f86025702376778fb27035cd3938956b)

The catalog has 55 identities: 10 active, 1 opt-in, 35 prepared, and 9 blocked
by policy. A prepared module supplies identity, capabilities, status, and an
icon only. It does not open files or invent data.

## Active sources

| Provider | Source TokenUsage reads | Data | Cost | Limits |
|---|---|---|---|---|
| Codex | `codex app-server` and approved local logs | tokens, models, daily usage, resets | reported or catalog | official local interface |
| Claude | JSONL under the Claude Code root | tokens, models, and date | reported; if missing, catalog | unavailable without a public API |
| Cursor | `state.vscdb` allowlist projection | real counters per turn; estimated context for old records | estimated API value for known models | unavailable without a public API or the user's own Admin key |
| Grok Build | `unified.jsonl` or a compatible local summary | tokens, models, and date | reported or catalog | unavailable without a public API |
| OpenCode | SQLite or local JSON storage | tokens, models, and date | reported or catalog | no common quota across providers |
| Antigravity | local numeric metadata | tokens, models, and date | catalog | blocked by policy |
| Amp | `ledger.jsonl` | tokens, model, and date | credits are not USD; estimated API value when a price exists | unavailable |
| Mux | `session-usage.json` | tokens aggregated by model and date | reported; if missing, catalog | unavailable |
| Goose | read-only numeric projection on `sessions.db` | accumulated tokens, model, provider, and date | reported if the schema includes it; if missing, catalog | unavailable |
| Hermes | read-only numeric projection on `state.db` | aggregated tokens, model, provider, and date | reported if the schema includes it; if missing, catalog | unavailable |

The Amp, Mux, Goose, and Hermes readers do not open threads, transcripts,
messages, commands, or tool calls. Session or message IDs become hashes before
usage events are created.

## How costs are calculated

TokenUsage keeps three separate states:

1. `ProviderReported`: the source stores an explicit USD amount. This value
   has priority.
2. `CatalogEstimated`: real tokens exist and match a catalog model exactly.
   This is an estimated API value, not a subscription charge.
3. `Unavailable`: the amount is missing, or the model has no confirmed price.
   Tokens stay visible as `unpriced`.

The estimate uses prices per million tokens:

`input × inputRate + output × outputRate + reasoning × outputRate + cacheRead × cacheReadRate + cacheWrite × cacheWriteRate`

The result is divided by `1,000,000`. Catalogs are versioned. Unknown models
never inherit the price of a similar model. Amp stores its own credits.
TokenUsage does not label them as USD.

## How limits are obtained

Codex is the only active integration that delivers session limits through an
official local interface. TokenUsage also keeps Vercel AI Gateway as opt-in
because it can use a key the user supplies directly.

Some upstreams obtain more quotas because they reuse cookies, OAuth tokens,
editor bearer tokens, or private endpoints. TokenUsage does not adopt those
paths. Claude, Cursor, Copilot, Grok, and others show observed local usage, but
not a fictitious remaining quota. A new limit needs a public API with a
read-only scope or a key that the user supplies to TokenUsage.

## Modules without an active reader

Prepared: `alibaba-cloud`, `anthropic`, `azure-openai`, `codebuff`,
`codewhale`, `copilot`, `crush`, `cursor-agent`, `deepseek`, `devin`, `droid`,
`forge`, `gemini-api`, `gemini-cli`, `groq`, `ibm-bob`, `kiro`,
`lingtai-tui`, `mistral`, `mistral-vibe`, `moonshot`, `ollama`, `omp`,
`open-design`, `openai`, `openclaude`, `openclaw`, `openrouter`, `pi`,
`quickdesk`, `qwen-cli`, `roo-code`, `warp`, `xai`, and `zerostack`.

Opt-in: `vercel-ai-gateway`.

Blocked: `cline`, `cline-cli`, `kilo-code`, `kimi-cli`, `kimi-code`,
`perplexity`, `zai`, `zcode`, and `zed`.

## Rule to continue

A module becomes active only when one of these exists:

- a numeric local projection without content
- a public API with its own credential
- an official read-only interface

The change must include a bounded reader, a sanitized fixture, runtime
composition, diagnostics, cost coverage, and a package test.

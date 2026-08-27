# OpenUsage provider parity and expanded catalog

Cutoff date: 2026-08-11

## Two projects with the same name

TokenUsage pins historical and product reference to
[`robinebers/openusage`](https://github.com/robinebers/openusage). This review
uses tag
[`v0.7.8`](https://github.com/robinebers/openusage/releases/tag/v0.7.8),
at commit
[`487cc8f19a9a28676f6924aafa48dee79ad7a7f6`](https://github.com/robinebers/openusage/tree/487cc8f19a9a28676f6924aafa48dee79ad7a7f6).
That commit was `HEAD` during the review and records ten cards.

A different project is also named OpenUsage.sh:
[`janekbaraniewski/openusage`](https://github.com/janekbaraniewski/openusage).
Its `main` branch was at
[`ddc05f24b159bfd1a24bbf641dcfb841410a77ab`](https://github.com/janekbaraniewski/openusage/commit/ddc05f24b159bfd1a24bbf641dcfb841410a77ab).
The latest release was
[`v0.24.2`](https://github.com/janekbaraniewski/openusage/releases/tag/v0.24.2),
at commit `89d33d30c48b9a36b343a0ee4105c0b956385763`.

The first project defines the immediate parity close. The second is an
expanded inventory for future modules. Do not mix their IDs and sources.

## Immediate close of the ten-provider parity

The [public list](https://github.com/robinebers/openusage/blob/487cc8f19a9a28676f6924aafa48dee79ad7a7f6/README.md#supported-providers)
and the [executable catalog](https://github.com/robinebers/openusage/blob/487cc8f19a9a28676f6924aafa48dee79ad7a7f6/Sources/OpenUsage/Providers/ProviderCatalog.swift)
agree on ten providers.

| ID | OpenUsage capabilities | Safe state in TokenUsage |
|---|---|---|
| `antigravity` | Five-hour and weekly Gemini and non-Gemini quotas. | **Active, local.** Keep local tokens and spend. Remote quota stays blocked. |
| `claude` | Session, weekly, models, extra usage, and local spend. | **Implementable now, local.** The reader already exists. Do not reuse OAuth. |
| `codex` | Session, weekly, Spark, reset credits, extra usage, and local spend. | **Active.** Keep `codex app-server` and the local readers. |
| `copilot` | AI credits, extra usage, organization, chat, and completions. | **Deferred.** Add the module and icon. Implement later with the Billing REST API and a manual token. |
| `cursor` | Total usage, Auto, API, extra usage, credits, and spend. | **Active, partial local.** Keep the allowlist projection. |
| `devin` | Daily and weekly quotas, plus extra-usage balance. | **Deferred.** Add the module and icon. The future path uses API v3 with a manual service user. |
| `grok` | Weekly pool, pay-as-you-go, and local spend. | **Active, local.** Remote quota stays blocked. |
| `opencode` | Go caps and local spend for Go and Zen. | **Active, local.** Do not present observed caps as an official balance. |
| `openrouter` | Credits, balance, period spend, and key limit. | **Manual, implementable now.** The client already exists. Credential Locker, composition, UI, and smoke are still missing. |
| `zai` | Token windows and web searches. | **Blocked.** Add identity and status. Do not ask for a key or create a client for internal endpoints. |

Five surfaces still lack a close: `claude`, `copilot`, `devin`, `openrouter`,
and `zai`. Claude already has a deferred entry, a reader, and a local icon.
OpenRouter already has an HTTP client. New identity work concentrates on
Copilot, Devin, OpenRouter, and Z.ai.

Reference resources:

- [`copilot.svg`](https://github.com/robinebers/openusage/blob/487cc8f19a9a28676f6924aafa48dee79ad7a7f6/Sources/OpenUsage/Resources/ProviderIcons/copilot.svg)
- [`devin.svg`](https://github.com/robinebers/openusage/blob/487cc8f19a9a28676f6924aafa48dee79ad7a7f6/Sources/OpenUsage/Resources/ProviderIcons/devin.svg)
- [`openrouter.svg`](https://github.com/robinebers/openusage/blob/487cc8f19a9a28676f6924aafa48dee79ad7a7f6/Sources/OpenUsage/Resources/ProviderIcons/openrouter.svg)
- [`zai.svg`](https://github.com/robinebers/openusage/blob/487cc8f19a9a28676f6924aafa48dee79ad7a7f6/Sources/OpenUsage/Resources/ProviderIcons/zai.svg)

The repository contains each provider source under
[`Sources/OpenUsage/Providers`](https://github.com/robinebers/openusage/tree/487cc8f19a9a28676f6924aafa48dee79ad7a7f6/Sources/OpenUsage/Providers).
That code confirms an important boundary: several quotas use existing
credentials and private endpoints. TokenUsage must not copy those paths.

## Optional expanded base of 35

The [OpenUsage.sh executable registry](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/registry.go#L45-L82)
contains 35 providers. `docs/providers.md` lists 17 and is outdated. The
registry and the modules take priority over that page.

The expanded base can register the 35 identities without enabling unsafe
readers, but it is not part of the immediate close of ten. If a later phase
adopts it, the catalog must separate four states:

- `active`: an approved real source exists.
- `manual`: a public source exists that needs a credential the user supplies.
- `deferred`: identity and capabilities exist, but no approved reader exists.
- `blocked`: the only observed path uses cookies, another product's credentials, or a private contract.

Local agent sources can contain prompts, responses, commands, and paths. Their
presence in OpenUsage.sh does not authorize a TokenUsage read.

Identity reconciliation must follow these rules:

- The current ID `claude` can declare `claude_code` as a reference alias. It must not create two cards.
- The current ID `grok` means Grok Build. It is not `xai`.
- Antigravity is not `gemini_cli` or `gemini_api`.
- `moonshot` is the Moonshot API. It is not `kimi_cli`.
- `alibaba_cloud` is not `qwen_cli`.
- Devin belongs to the close of ten, even though OpenUsage.sh does not register it.

## API or account providers

| ID and name | OpenUsage capabilities | Upstream source | Safe state in TokenUsage |
|---|---|---|---|
| `openai` · OpenAI | Limits taken from headers. | [`internal/providers/openai`](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/openai) | **Deferred.** An independent monitor does not receive those headers without making a request. |
| `anthropic` · Anthropic | Limits taken from headers. | [`internal/providers/anthropic`](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/anthropic) | **Deferred.** Do not reuse Claude Code keys or OAuth. |
| `azure_openai` · Azure OpenAI | Limits per resource and deployment, taken from headers. | [`internal/providers/azure_openai`](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/azure_openai) | **Deferred.** Needs manual settings and a scope and cost gate. |
| `alibaba_cloud` · Alibaba Cloud Model Studios | Quota, credits, spend, tokens, rate limits, and models. | [`internal/providers/alibaba_cloud`](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/alibaba_cloud) | **Deferred.** Add the catalog. Confirm the public contract before the client. |
| `openrouter` · OpenRouter | Credits, balance, spend, activity, generations, and models. | [`internal/providers/openrouter`](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/openrouter) | **Manual, implementable now.** The TokenUsage client already exists. Credential Locker, composition, UI, and authorized smoke are still missing. |
| `perplexity` · Perplexity | Billing, analytics, and console tier. | [`internal/providers/perplexity`](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/perplexity) | **Blocked.** Upstream uses a browser cookie. TokenUsage must not copy it. |
| `groq` · Groq | Rate limits and daily limits. | [`internal/providers/groq`](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/groq) | **Deferred.** It is not Grok Build. It needs its own gate. |
| `mistral` · Mistral AI | Headers, subscription, and usage. | [`internal/providers/mistral`](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/mistral) | **Deferred.** Keep it separate from Mistral Vibe. |
| `moonshot` · Moonshot | Balance and account data. | [`internal/providers/moonshot`](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/moonshot) | **Deferred.** It is not Kimi CLI. |
| `deepseek` · DeepSeek | Headers and balance. | [`internal/providers/deepseek`](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/deepseek) | **Deferred.** Add the shell. Confirm the billing API before the client. |
| `xai` · xAI (Grok) | Headers and API-key data. | [`internal/providers/xai`](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/xai) | **Deferred.** It is not local Grok Build. |
| `zai` · Z.AI | Models, quotas, credits, and usage by model or tool. | [`internal/providers/zai`](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/zai) | **Blocked.** The current TokenUsage gate does not authorize the internal quota endpoints. |
| `gemini_api` · Google Gemini API | Headers, per-model limits, and authentication status. | [`internal/providers/gemini_api`](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/gemini_api) | **Deferred.** It is not Gemini CLI or Antigravity. |

## Local or hybrid tools

| ID and name | OpenUsage capabilities | Upstream source | Safe state in TokenUsage |
|---|---|---|---|
| `opencode` · OpenCode | Local telemetry, spend, and models. It can also query the Zen console. | [`internal/providers/opencode`](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/opencode) | **Active, local.** Keep the current SQLite reader. Block console cookies and credentials. |
| `gemini_cli` · Gemini CLI | Local sessions, tokens, cost, and OAuth quota. | [`internal/providers/gemini_cli`](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/gemini_cli) | **Deferred.** Prepare the module. The local reader needs a content and format gate. Block another product's OAuth. |
| `ollama` · Ollama | Local API, SQLite, logs, tokens, models, and optional cloud. | [`internal/providers/ollama`](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/ollama) | **Implementable now, local.** High priority because the local API needs no credentials. Cloud stays deferred. |
| `copilot` · GitHub Copilot | Account, quota, and local per-session telemetry. | [`internal/providers/copilot`](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/copilot) | **Deferred.** Prepare the module. Implement later with the Billing REST API and a manual token. Block the editor token and the private endpoint. |
| `cursor` · Cursor IDE | SQLite, CSV, local telemetry, and remote usage. | [`internal/providers/cursor`](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/cursor) | **Active, partial local.** Keep the current allowlist projection. Block token, RPC, Stripe, and private CSV. |
| `claude_code` · Claude Code CLI | JSONL, stats, tokens, spend, and remote quota. | [`internal/providers/claude_code`](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/claude_code) | **Implementable now, local.** The reader already exists. Keep remote quota blocked. |
| `codex` · OpenAI Codex CLI | JSONL, tokens, spend, limits, and credits. | [`internal/providers/codex`](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/codex) | **Active.** Keep `codex app-server` and the local readers. Do not copy `auth.json` or private endpoints. |

## Agents with local storage

| ID and name | Source and data OpenUsage reads | Safe state in TokenUsage |
|---|---|---|
| `amp` · Amp | [Thread JSON and `ledger.jsonl`](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/amp). Tokens and cost in credits. | **Deferred.** The ledger is a candidate. Threads need a content gate. |
| `goose` · Goose | [`sessions.db`](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/goose). Tokens, cost, and sessions. | **Deferred.** Do not open the database until a projection without conversation is fixed. |
| `hermes` · Hermes Agent | [`state.db`](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/hermes). Tokens, cost, and models. | **Deferred.** Needs a schema and privacy gate. |
| `mux` · Mux | [`session-usage.json`](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/mux). Aggregated usage per session. | **Deferred.** A good candidate if the file contains metrics only. |
| `droid` · Droid | [Session settings and auxiliary JSONL](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/droid). Tokens and cost. | **Deferred.** Exclude transcript and commands before the reader. |
| `crush` · Crush | [`crush.db` per project](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/crush). Tokens and cost. | **Deferred.** A dedicated icon and a table gate are missing. |
| `roocode` · Roo Code | [VS Code global storage and compatible forks](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/roocode). Tokens and cost. | **Deferred.** Do not read tasks or messages. Double-count risk also exists. |
| `kilo_code` · Kilo Code | [Local format compatible with Roo Code](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/kilocode). Tokens and cost. | **Deferred.** Keep a separate ID. The current gate does not authorize the database. |
| `kiro_cli` · Kiro CLI | [`data.sqlite3`](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/kiro). Sessions and tokens when they exist. | **Deferred, experimental.** Upstream marks low confidence and missing tokens in some data. |
| `zed` · Zed | [`threads.db`](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/zed). Tokens per thread. | **Deferred.** The database mixes metrics and conversation. The current gate blocks that read. |
| `codebuff` · Codebuff | [`chat-messages.json`](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/codebuff). Tokens, cost, and models. | **Deferred.** Do not read messages. A dedicated icon is missing. |
| `kimi_cli` · Kimi CLI | [`wire.jsonl`](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/kimi_cli). Tokens and cost. | **Deferred.** It is not the Moonshot API. The current gate blocks sessions. |
| `openclaw` · OpenClaw | [JSONL per agent and aliases](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/openclaw). Tokens and cost. | **Deferred.** Needs deduplication and a projection without content. |
| `pi` · Pi | [Pi and OMP JSONL sessions](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/pi). Tokens, cost, and models. | **Deferred.** Separate agent and model provider before counting. |
| `qwen_cli` · Qwen CLI | [JSONL chats per project](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/providers/qwen_cli). Tokens and cost. | **Deferred.** Do not read chats. Keep it separate from Alibaba Cloud. |

## Icons in the expanded base

The official manifest is
[`internal/tmux/assets/icons.json`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/internal/tmux/assets/icons.json#L1-L40).
It declares 32 icons for 35 providers. The files live in
[`website/public/icons`](https://github.com/janekbaraniewski/openusage/tree/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons).

| Provider | Upstream resource |
|---|---|
| `openai` | [`openai.svg`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons/openai.svg) |
| `anthropic` | [`anthropic.svg`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons/anthropic.svg) |
| `azure_openai` | No dedicated resource. Uses fallback. |
| `alibaba_cloud` | [`alibabacloud.svg`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons/alibabacloud.svg) |
| `openrouter` | [`openrouter.svg`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons/openrouter.svg) |
| `perplexity` | [`perplexity.svg`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons/perplexity.svg) |
| `groq` | [`groq.svg`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons/groq.svg) |
| `mistral` | [`mistral.svg`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons/mistral.svg) |
| `moonshot` | [`moonshot.svg`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons/moonshot.svg) |
| `deepseek` | [`deepseek.svg`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons/deepseek.svg) |
| `xai` | [`xai.svg`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons/xai.svg) |
| `zai` | [`zai.svg`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons/zai.svg) |
| `opencode` | [`opencode.svg`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons/opencode.svg) |
| `gemini_api` | [`gemini.svg`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons/gemini.svg) |
| `gemini_cli` | [`geminicli.svg`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons/geminicli.svg) |
| `ollama` | [`ollama.svg`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons/ollama.svg) |
| `copilot` | [`copilot.svg`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons/copilot.svg) |
| `cursor` | [`cursor.svg`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons/cursor.svg) |
| `claude_code` | [`claude.svg`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons/claude.svg) |
| `codex` | [`codex.svg`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons/codex.svg) |
| `amp` | [`amp.svg`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons/amp.svg) |
| `goose` | [`goose.svg`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons/goose.svg) |
| `hermes` | [`hermes.svg`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons/hermes.svg) |
| `mux` | [`mux.svg`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons/mux.svg) |
| `droid` | [`droid.svg`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons/droid.svg) |
| `crush` | No dedicated resource. Uses fallback. |
| `roocode` | [`roocode.svg`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons/roocode.svg) |
| `kilo_code` | [`kilocode.svg`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons/kilocode.svg) |
| `kiro_cli` | [`kiro.svg`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons/kiro.svg) |
| `zed` | [`zed.svg`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons/zed.svg) |
| `codebuff` | No dedicated resource. Uses fallback. |
| `kimi_cli` | [`kimi.svg`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons/kimi.svg) |
| `openclaw` | [`openclaw.svg`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons/openclaw.svg) |
| `pi` | [`pi.svg`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons/pi.svg) |
| `qwen_cli` | [`qwen.svg`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/website/public/icons/qwen.svg) |

SVG files from both projects confirm identity and geometry. TokenUsage must
store its own provenance and license before it distributes them. The three
fallbacks need an explicit local glyph so icons do not break.

## Recommended base

The first implementation must add catalog entries, not 35 readers.

1. Close the five remaining surfaces of the ten-provider parity.
2. Add Copilot, Devin, OpenRouter, and Z.ai to the icon system.
3. Keep Claude as one identity with alias `claude_code`.
4. Then register the expanded IDs, groups, capabilities, and states.
5. Add the 32 expanded icons, with a fallback for the remaining three.
6. Enable only approved real sources.
7. Keep the rest as `deferred` or `blocked`, with no network or disk factories.
8. Add search and filters so the compact view does not use 35 tabs.

The simulated view must use the full catalog. The upstream demo creates only
seven providers:
[`cmd/demo/provider.go`](https://github.com/janekbaraniewski/openusage/blob/ddc05f24b159bfd1a24bbf641dcfb841410a77ab/cmd/demo/provider.go).
TokenUsage therefore needs its own fixtures for the 35.

Simulated data must follow these rules:

- Show `Simulated data` in the header and on each card.
- Do not write to durable storage or add into real reports.
- Do not appear in captures or exports without the simulation mark.
- Include states with data, with no data, error, deferred, and blocked.
- Hide connection actions for blocked providers.

## Verification performed

The registry, each provider module, the icon manifest, and the demo were read
at the pinned commit. No endpoints were run. No cookies, credentials, or
session data were read.

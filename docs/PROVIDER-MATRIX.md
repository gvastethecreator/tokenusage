# Provider matrix

Cutoff date: 2026-09-02

Temporary status: Vercel AI Gateway is out of the active catalog. Its implementation is kept so it can be reactivated in a later delivery.

Parity upstreams:
`janekbaraniewski/openusage@ddc05f24b159bfd1a24bbf641dcfb841410a77ab`,
`steipete/CodexBar@26ebaf9d5b0949e3b57fafcde0ed54aa3b27b3d2`, and
`getagentseal/codeburn@d78bdb21f86025702376778fb27035cd3938956b`.

Cost catalogs are checked against the official [OpenAI model pages](https://developers.openai.com/api/docs/models), [Anthropic pricing](https://platform.claude.com/docs/en/about-claude/pricing), [Google Gemini pricing](https://ai.google.dev/gemini-api/docs/pricing), [xAI model pages](https://docs.x.ai/developers/models), [Cursor model pricing](https://cursor.com/docs/models-and-pricing), [Z.ai pricing](https://docs.z.ai/guides/overview/pricing), and [Kimi pricing](https://platform.kimi.ai/docs/pricing/chat).

## States

- `MVP`: chosen technical and product path.
- `Local`: a view based only on local data can be published.
- `Gate`: a test, public contract, or permitted-use review is missing.
- `Manual`: requires a key that the user gives to the app.
- `Experimental`: fragile source; no support promise.
- `Blocked`: the known source cannot be used in a public build.

## OpenUsage, CodexBar, and CodeBurn module baseline

The catalog represents 56 identities from the inspected union. A visible module is not the same as an active data source:

- `Active`: Amp, Antigravity, Claude, Codex, Cursor, Goose, Grok Build, Hermes,
  Mux, OpenCode, and ZCode. Each one creates a real, bounded local reader.
- `OptIn`: Vercel AI Gateway. It keeps its public client and requires a key that
  the user gives to TokenUsage.
- `Prepared`: 36 modules with identity, capabilities, brand when it exists, and
  visible status. They do not open files, credentials, or connections.
- `PolicyBlocked`: Cline, Cline CLI, Kilo Code, Kimi CLI, Kimi Code, Perplexity,
  Z.ai, and Zed. They keep the researched contract, but they do not
  activate readers.

TokenUsage takes selected contracts from each upstream, calculates cost locally, and does not copy OAuth sessions, cookies, or private endpoints.

## Summary

| Provider | Live quota | Local tokens and cost | Chosen source | Status | Delivery |
|---|---|---|---|---|
| Codex | Yes, official local interface | Yes, official API and logs | `codex app-server` | MVP | M4; detail in M6 |
| Claude | Blocked without a public interface | Yes, logs and reported or estimated cost | Claude Code sessions | Active local + quota Gate | Active; quota pending |
| OpenCode | No common quota | Yes, reported cost and tokens | `opencode.db` and `storage` | Local | M6A |
| Grok Build | Blocked without a public interface | Yes, reported or estimated cost | `sessions` and `unified.jsonl` | Local + Gate | M6A; quota pending |
| Grok Bot | Blocked without an approved data interface | No | The desktop app coordinates a computer in the cloud; the local profile contains state and credentials | Prepared | Catalog compatibility; sessions and credentials are not read |
| OpenRouter | Yes, API with a key | Depends on the API | user's own manual key | Manual | M9 |
| Z.ai | Blocked outside the official plugin | Only through admitted logs | official plugin limited to Claude Code | Blocked | M9; reopen with a contract or permission |
| Cursor | Not on Individual. Teams/Enterprise: future manual Admin API | Yes, real counters per turn; context fallback for older data | local SQLite with an allowlist projection; future Admin API | Partial active local | Active; estimated API cost when the model matches |
| Amp | No stable public quota | Yes, tokens from the ledger | `ledger.jsonl`; threads are not opened | Partial active local | Active; credits are not shown as USD |
| Mux | No common quota | Yes, tokens and aggregated cost by model | `session-usage.json`; transcripts are not opened | Active local | Active |
| Goose | No common quota | Yes, tokens accumulated per session | read-only numeric query of `sessions.db` | Partial active local | Active; estimated API cost when a price exists |
| Hermes | No common quota | Yes, tokens and cost accumulated per session | `state.db` in `.hermes` or in a profile; an empty `.hermes` folder or one from another tool does not count as an install | Partial active local | Active; reported or estimated API cost |
| GitHub Copilot | No under the current contract | Yes, paid personal and organization | Billing API with a manual token | Partial Manual | M9; smoke pending |
| ZCode | Blocked without a public contract | Yes, counters per request; estimated API cost | local SQLite `model_usage` with an allowlist projection | Partial active local | Reopened in 3.8.1 |
| Kilo Code | No public quota contract | Candidate CLI aggregates, without a machine contract | No suitable source; candidate: `kilo stats` | Gate | M9 |
| Kimi Code | Blocked without a machine contract | Blocked because of session content | Version detection only | Blocked | M9 |
| Command Code | Blocked without a machine contract | Blocked because of sessions and credentials | Version detection only | Blocked | M9 |
| Cline | Manual API pending a contract | Blocked because of task content | Candidate Enterprise API | Blocked | M9 |
| Zed | No public quota contract | Blocked because tokens and transcription are mixed | No suitable source | Blocked | M9 |
| Antigravity IDE/CLI | Blocked by policy | Yes, tokens and estimated cost | local `gen_metadata` | Experimental local + blocked quota | M6B |
| Devin | No for self-serve | Organization ACUs | v3 API with a manual service user | Experimental Manual | M9; smoke pending |

## Prepared candidates

These names appear in the local references, but they are not active providers
yet. Each one starts with a separate gate. A scanner is not added until a
source suitable for Windows is proven.

| Candidate | Reason | Initial limit |
|---|---|---|
| Gemini CLI | Appears in CodeBurn and AgentsView | Do not read chats or credentials; separate local usage from Google quota |
| Kiro | Appears in CodeBurn and AgentsView | Separate CLI, IDE, and account; do not read session content |
| Roo Code | Appears in CodeBurn and AgentsView | Do not reuse VS Code tasks; avoid double counting with the model provider |
| Kimi CLI | CodeBurn separates it from Kimi Code; AgentsView mixes both paths | Settle identity before inheriting Kimi Code storage or claims |
| Cursor Agent | CodeBurn separates it from the Cursor editor | Keep it separate from Cursor Admin API and Individual |
| Forge | Appears in CodeBurn and AgentsView | Validate Windows support and avoid databases that contain content |
| OpenClaw | Appears in CodeBurn and AgentsView | Settle identity and an aggregated source; do not read conversations |
| Pi | Appears in CodeBurn and AgentsView | Distinguish Pi from OMP and settle deduplication |
| Qwen | Appears in CodeBurn and AgentsView | Separate Qwen Code from the Qwen model provider |
| Warp | Appears in CodeBurn and AgentsView | Do not read terminal history or commands |
| Vercel AI Gateway | CodeBurn exposes an aggregated API report | Separate gateway cost from agent usage; manual key with minimum permission |
| Mistral Vibe | Appears in CodeBurn and AgentsView | Do not read messages, tools, or commands; require an aggregated source |
| DeepSeek TUI / CodeWhale | They share inherited paths between AgentsView and CodeBurn | Resolve identity and migration before creating IDs or reading sessions |
| Windsurf | AgentsView declares Windows paths | Require an aggregated source; do not read chat or the IDE global state |
| Trae | AgentsView declares several Windows paths | Separate editor variants and exclude chats, tasks, and credentials |
| Aider | AgentsView treats it as an opt-in root | Settle consent and accept only documented minimum metrics |
| OpenHands CLI | AgentsView records local sessions | Separate CLI, service, and model provider; do not read content |
| Codebuff | CodeBurn records cost from sessions | Look for an official aggregate that does not expose messages or tools |
| Piebald | AgentsView declares its own Windows storage | Confirm product, support, and source before creating a provider ID |
| Crush | CodeBurn records it as a distinct agent | Settle product, publisher, and an aggregated source suitable for Windows |
| Droid | CodeBurn records it as its own identity | Resolve the ambiguous name and separate agent, account, and model provider |
| IBM Bob | CodeBurn includes a dedicated adapter | Confirm the current product, Windows support, and a minimum export |
| LingTai TUI | CodeBurn includes its own sessions | Settle identity and Windows support before evaluating metrics |
| Open Design | CodeBurn includes a dedicated adapter | Confirm that it is a measurable agent and not an auxiliary format |
| Quick Desktop | CodeBurn uses the `quickdesk` identity | Settle canonical name, publisher, and Windows source |
| Zerostack | CodeBurn includes a dedicated adapter | Confirm product, version, and metrics contract |
| Zencoder | AgentsView declares its own sessions | Separate the product from other uses of the name and require a suitable source |
| Qoder | AgentsView declares project paths | Do not read transcripts; look for an official aggregate |
| Cortex Code | AgentsView declares local sessions | Separate agent usage from Snowflake billing |
| gptme | AgentsView declares local logs | Confirm Windows support and an output without content |
| iFlow | AgentsView declares its own sessions | Settle publisher, product, and contract before creating an ID |
| IcodeMate | AgentsView records its own identity | Resolve identity, Windows support, and a minimum source |
| MiMoCode | AgentsView declares local storage | Avoid databases that contain content and separate MiMo models |
| Posit Assistant | AgentsView separates it from Positron | Settle Windows support and a source without conversation |
| Positron Assistant | AgentsView declares paths by platform | Confirm Windows support before evaluating data |
| QClaw | AgentsView separates it from OpenClaw | Resolve relationship, migration, and deduplication |
| QwenPaw | AgentsView separates it from Qwen Code | Resolve identity and the relationship with the Qwen provider |
| Reasonix | AgentsView declares a Windows path | Require aggregated metrics; exclude sessions and sidecars that contain content |
| Shelley | AgentsView declares its own database | Confirm Windows support and tables free of content |
| WorkBuddy | AgentsView declares sessions per project | Settle product, Windows support, and a suitable source |
| OpenClaude | AgentsView separates it from Claude Code | Resolve whether it is a fork, alias, or provider before inheriting contracts |
| Claude Cowork | AgentsView separates it from Claude Code | Keep it in the Claude family until account and source are settled |

Delivery shows order, not a date. No `Gate` status enters stable until all of
its controls are closed.

ZooCode stays inside the Roo Code gate until its identity and the product
migration are settled. VS Code Copilot and Visual Studio Copilot stay under the
GitHub Copilot family. Kiro IDE stays under Kiro. Antigravity IDE and CLI keep
distinct sources inside the same family. OMP is resolved in the Pi gate.
`ChatGPT` and `Claude.ai` appear in AgentsView as history imports, not as local
agents with a measurable quota, and they stay out of the provider inventory
until another contract exists.

The entry term `Zcode` resolves as `ZCode`, the desktop product of ZCode Agent.
Kilo Code, Kimi Code, and Command Code are kept as entry terms. Zed represents
only its native agent: sessions of external agents still belong to their
original provider. Canonical product names must be settled
before IDs, icons, paths, or claims are added to the code.


## Publication gate

Every integration starts with a provider Issue. The PR must follow the
[contributor testing guide](CONTRIBUTOR-TESTING.md), even when the provider is
not available to maintainers.

Each provider needs:

- [ ] documented source and precedence
- [ ] Windows test with default paths and environment variable
- [ ] response contract settled with sanitized fixtures
- [ ] parser with size limits, timeout, and cancellation
- [ ] absent, expired, unsuitable, throttle, and schema-change accounts covered
- [ ] multiple accounts and account change defined
- [ ] credential rotation without a race, or without writing
- [ ] logs and cache without secrets
- [ ] terms, policy, and brand review
- [ ] test inside the signed MSIX
- [ ] regression test against a real supported version
- [ ] UI text that explains source, coverage, and limits
- [ ] reported and estimated cost kept separate, with unpriced models visible
- [ ] total differential against a reference on the same fixture
- [ ] proof that the reader does not open auth, prompt, response, task, or command

## Codex

### Source

- account status: `account/read` with `refreshToken: false`, selecting only
  type, plan, and auth requirement
- quota: `account/rateLimits/read`
- tokens and daily buckets: `account/usage/read`
- optional local detail: `CODEX_HOME/sessions` and `archived_sessions`; the
  `state_5.sqlite` index is merged with the folders so a new session is not
  hidden while the index catches up

The official [`app-server`](https://github.com/openai/codex/blob/a26f219f6788c951dcb3bf435fab4c6d0f4d2f40/codex-rs/app-server/README.md) manages login and renewal. The app does not read `auth.json` in the MVP.

Status reads do not keep or show `email`, `codexHome`, token, raw response, or
account identifier. A ChatGPT session enables quota. API key, Bedrock, local
mode, or future auth remain an unsuitable account until a quota contract says
otherwise.

### Metrics

- primary window
- secondary window
- additional limits named by the official `limitName`; the current
  `base_model_inference` bucket is shown as `GPT reserve`
- next reset
- plan
- credits and spending controls when they exist
- daily tokens and trend
- estimated cost by model in a later phase

### Limits

- requires a compatible Codex binary
- an API key without a ChatGPT account can lack subscription quota
- the method can deliver new additional limits
- multiple accounts require one `CODEX_HOME` and process per instance
- consuming a reset credit stays outside the MVP because it is an irreversible action

### Result

MVP approved after process, contract, package, and unsuitable-account tests.
The local research test completed both read methods.

Upstream comparison source: [Codex provider](https://github.com/robinebers/openusage/blob/9d2bf09f10e21f769494a525a9d65c84d7aeb1df/docs/providers/codex.md).

## Claude

### Local source

- `%USERPROFILE%\.claude\projects`
- equivalent path under `CLAUDE_CONFIG_DIR`
- `pi` logs only in a later phase
- non-persisted sessions stay out of coverage

The reader extracts tokens, model, and date. It extracts recorded cost when
that value exists. Prompts and responses are omitted.

### Quota

Claude Code stores Windows credentials in `%USERPROFILE%\.claude\.credentials.json`,
according to its [authentication documentation](https://code.claude.com/docs/en/authentication).
It does not document a read-only quota command. The upstream implementation
calls a non-public endpoint and can rotate tokens.

The [Claude Code legal guide](https://code.claude.com/docs/en/legal-and-compliance)
limits third-party use of subscription OAuth. Quota stays blocked until a
public interface or permission exists. The app does not write that credential.

### Local metrics

- today, yesterday, and 30 days
- tokens and trend
- measured cost if the log includes it
- estimated cost with price coverage
- omitted models and the reason

### Result

Local view after the scanner and coverage tests. Live quota behind a legal and
technical gate.

Upstream comparison source: [Claude provider](https://github.com/robinebers/openusage/blob/9d2bf09f10e21f769494a525a9d65c84d7aeb1df/docs/providers/claude.md).

## OpenCode

### Source

OpenCode documents `%USERPROFILE%\.local\share\opencode` on Windows and
`~/.local/share/opencode` inside WSL. The reader accepts `opencode.db` and the
JSON `storage`. It omits `auth.json`.

The official [`opencode stats`](https://opencode.ai/docs/cli/) command delivers
human-readable token and cost statistics. Because it does not offer JSON in the
observed version, it is used as a differential oracle and not as the adapter
format.

### Metrics

- usage observed on this computer
- tokens by period, agent, and model
- cost reported by OpenCode when it exists
- estimated cost only for rows without reported cost
- trend
- models and sources with coverage

### Limits

Local data can omit other computers, deleted sessions, and WSL installs. The UI
calls this `Observed local usage`. It does not claim remaining quota because
OpenCode can use many providers and plans.

The database is opened read-only with minimal queries. It is not copied: in the
local test it occupies about 2.5 GB. The first beta covers native OpenCode on
Windows. Each WSL distro needs separate detection and consent.

### Result

Local beta after fixtures for SQLite, legacy JSON, WAL, unpriced model, and
deduplication across formats. The examined install has OpenCode `1.18.4`,
`opencode.db`, `storage`, and `opencode stats`.

Upstream comparison source: [OpenCode provider](https://github.com/robinebers/openusage/blob/9d2bf09f10e21f769494a525a9d65c84d7aeb1df/docs/providers/opencode.md).

## Grok

### Local source

- `GROK_HOME/logs/unified.jsonl` as the primary source
- `GROK_HOME/sessions` with `summary.json` and `updates.jsonl` as compatibility
- `params.update.usage`, per-model breakdown, and `costUsdTicks` when they exist
- catalog estimate only when the source does not report cost

The unified log has priority when its oldest inference reaches the start of the
35-day window, the same as in OpenUsage. If Grok rotated the log and only
recent turns remain, TokenUsage uses the session snapshots. Mixing both sources
would count the same inference twice. The snapshots keep reported
`costUsdTicks`. A `0` is not treated as a free turn.

A turn's model does not travel on the `shell.turn.inference_done` line. The app
takes it from the last model announcement for the same `pid`. When the log no
longer keeps that announcement, the turn is recorded under the `unknown` model,
with tokens counted and cost unavailable. It was discarded before. In the Grok
`1.0.0` test that hid 61 of 1034 turns while the read was declared complete.

### Quota

OpenUsage shows remaining weekly percentage with the `grok login` session: it
reads `auth.json`, calls `GET https://cli-chat-proxy.grok.com/v1/billing`, and
writes rotated tokens. xAI documents that balance for people in Settings →
Usage and in the TUI command `/usage`. There is no `grok usage` subcommand and
no quota JSON for another app. `GET /v1/api-key` publishes key metadata, not
the weekly pool. The Management API prepaid balance is team API credit with a
management key, another product. Its [acceptable use policy](https://x.ai/legal/acceptable-use-policy)
restricts automated access. The public build does not read `auth.json` or call
the private endpoint.

### Result

Local tokens and cost in beta after version fixtures and a differential. Quota
and balance only after a suitable official interface or written permission. The
Windows test detected Grok Build `0.2.112`, sessions, and the unified log
without opening the credential. The check on Grok `1.0.0` keeps the same
format: `msg`, `pid`, `ts` in UTC, and `ctx` with `prompt_tokens`,
`cached_prompt_tokens`, `completion_tokens`, and `reasoning_tokens`.

Update on task completion: when the app opens, the `Stop` hook is registered
automatically if Grok is installed (`~/.grok/hooks/tokenusage.json`). It is
also managed with `tokenusage grok install-hook|status|uninstall-hook`. The
hook discards the event payload and only refreshes TokenUsage's own data.
Uninstall keeps the rest of the user's hooks.

Upstream comparison source: [Grok provider](https://github.com/robinebers/openusage/blob/9d2bf09f10e21f769494a525a9d65c84d7aeb1df/docs/providers/grok.md).

### Grok Bot

Grok Bot remains a prepared provider. Official documentation describes an agent
on a persistent computer in the cloud. The user signs in with a Cursor account.
The desktop app does not publish a usage reader, export, or third-party quota
API.

OpenUsage shows a "Grok Bot" tile inside Cursor: private RPC
`DashboardService/GetSandUsageStatus` with the editor token. That is not a
reader of the Grok Bot desktop app and does not activate TokenUsage's
`grok-bot` module.

TokenUsage detected the Windows package during research, but it does not open
the Electron profile, local storage, session, or credentials. It also does not
attribute Grok Build logs to the Bot, or treat xAI API credits as Bot quota. A
reader can be enabled only with a public data interface or written permission
from xAI or Cursor.

References: [Grok Bot](https://docs.x.ai/grok-bot/overview), [get started](https://docs.x.ai/grok-bot/get-started), [Grok usage and limits](https://docs.x.ai/grok/faq).

## OpenRouter

### Source

A key that the user adds to this app and that is stored in Credential Locker.
A key from another app is not imported without confirmation.

The current official contract separates capabilities. `/api/v1/key` reports
usage and the limit of the active key. `/api/v1/credits` reports account
credits and requires a management key. A `403` on credits does not invalidate
the key-usage result.

### Metrics

- credits and balance
- usage that the public API delivers
- time and status of the response

### Result

Official contract settled. The offline
client is the first cut. The provider list can already store the key in
Credential Locker. Runtime, live reads, and authorized smoke remain pending. It
is marked as a manual configuration.

Upstream comparison source: [OpenRouter provider](https://github.com/robinebers/openusage/blob/9d2bf09f10e21f769494a525a9d65c84d7aeb1df/docs/providers/openrouter.md).

## Z.ai

### Evaluated source

Z.ai publishes `glm-plan-usage`, a quota plugin for the Personal plan that runs
inside Claude Code. Its official repository queries `api.z.ai` for
international accounts and `open.bigmodel.cn` for China. The monitor endpoints
do not appear in the general OpenAPI.

Policy limits the GLM Coding Plan to supported tools. The sources do not grant
a separate Windows app a read-only scope or permission to reuse those
endpoints.

### Result

Blocked. The public build does not ask for a Z.ai key, does not invoke the
plugin, and does not copy the upstream client. Local cost for Z.ai models can
appear through logs of admitted agents, with coverage and provenance.

Upstream comparison source: [Z.ai provider](https://github.com/robinebers/openusage/blob/9d2bf09f10e21f769494a525a9d65c84d7aeb1df/docs/providers/zai.md).

## ZCode

### Source

ZCode `3.8.1` stores a per-request usage record in
`%USERPROFILE%\.zcode\cli\db\db.sqlite`, table `model_usage`. The columns
include input, output, reasoning, read-cache, and write-cache tokens, model,
and a UTC timestamp. Its own total is `entrada + salida`. Input includes the
read cache, and reasoning lives inside output.

The reader opens the database read-only with `PRAGMA query_only=ON` and
selects only eight count columns from `model_usage`. It never opens
`v2\credentials.json`, the `part`, `message`, `input_history`, and `session`
tables, or `cli\rollout\*.jsonl`. It does not store session ids or paths:
event keys are SHA-256 hashes of the row id. A missing column degrades to
`UnsupportedSchema` instead of inventing numbers.

### Evaluated source

The `3.7.6` evaluation found no safe local source: hooks did not carry tokens,
and the published paths were support or content. The current build added the
local usage database, which reopens the provider under the Cursor `state.vscdb`
allowlist-projection precedent.

Terms in force since 2026-06-15 restrict access to other people's data,
content, or accounts through the service. The reader runs on the user's disk,
with no network and no account data. The recorded risk is that Z.ai does not
publish the schema. Column verification and the parser version keep the reader
fail-closed when the schema changes.

### Metrics

- Tokens per request, model, and day; 35-day reconciliation window.
- Estimated cost with public Z.ai API rates for GLM models (dated catalog).
  Plan credits are not converted into invoice cost. Models without a rate stay
  `Unpriced`.

### Limits

Coding Plan quota stays closed: the official plugin's monitoring endpoints are
private and require another tool's credential, and no ZCode hook exposes plan
or real remaining.

A quota estimated from plan credits was delivered briefly (public formula,
multipliers, and tiers published by Z.ai) and was withdrawn by maintainer
decision: it does not show the real remaining side and can mislead. The
credits gate documents the mechanism in case a future contract revives it.

### Result

Partial active local: a card with observed tokens and labeled estimated cost.
If Z.ai publishes an official usage API, the local reader migrates to it.

Update on task completion: when the app opens, `Stop` hooks are registered
automatically for detected local providers (ZCode, Grok, and Cursor). They can
also be managed with `tokenusage zcode install-hook|status|uninstall-hook`.
The hook discards the event payload and only refreshes TokenUsage's own data.
Uninstall keeps the rest of the user's configuration.

No ZCode, Cursor, or Grok hook exposes plan or real remaining.

## Kilo Code

### Evaluated source

Kilo Code publishes the `kilo` CLI, also available on Windows, and documents
`kilo stats` to show token and cost statistics. The command reference only
exposes filters by days, tools, models, and project. It does not publish JSON
output or a versioned contract to automate the table.

The extension keeps a local `kilo.db` with sessions and history. That database
is not a suitable source: TokenUsage cannot open it or infer its tables to
obtain metrics. An isolated test of `kilo 7.4.15` returned an empty statistics
table without login, but it does not prove that the command is read-only,
stable, or safe against format changes.

### Result

Open gate. This phase does not create a database reader, session parser, or
public card. The candidate path stays limited to an official command that emits
a structured, read-only contract with aggregated metrics. Until then the app
can only detect `kilo --version` in a future diagnostic.

## Kimi Code

### Evaluated source

Kimi Code offers the `kimi` CLI, a VS Code extension, `/usage` inside the TUI,
and a Console for quota and Extra Usage. It does not publish a machine output,
metrics export, or read-only API for Kimi Code quota, tokens, or cost.

On Windows it stores configuration, OAuth, sessions, logs, and history under
`%USERPROFILE%\.kimi-code` or `KIMI_CODE_HOME`. Its sessions include
`lastPrompt`, full communication, and request traces. The subscription is
limited to interactive use and prohibits non-interactive automation.

Kimi Platform publishes balance and usage with separate accounts and billing.
It is not mixed with the Kimi Code provider.

### Result

Blocked. The public build can detect `kimi --version` in a diagnostic phase,
but it does not read data, start the TUI, use `kimi web`, take tokens, or call
the Console. The provider reopens with a minimum, documented source that is
authorized for third parties.

## Command Code

### Evaluated source

Command Code offers the `cmd` CLI. On native Windows, `cmdc` is used because
`cmd` belongs to the system. Native Windows is still in alpha, and the
documentation recommends WSL. `/usage` shows credits, plan, and limits inside
an interactive session. Studio shows tokens, cost, and per-request history
after sign-in.

It does not publish a quota subcommand or a metrics export. `--output-format
json` applies only to the `cmd -p` response. It does not turn `/usage` into a
read contract. The Provider API publishes inference and model listing, not a
balance, quota, or cost-history API. It uses the same API key as the CLI, so it
is not a read-only monitor credential.

Documentation places conversations under `~/.commandcode/projects/`, tokens in
`~/.commandcode/auth.json`, and preferences in `.commandcode/taste/`. Those
paths can contain prompts, responses, credentials, rules, or context and are
not a suitable source.

### Result

Blocked. The public build can detect `cmdc --version` in a diagnostic phase,
but it does not read data, sign in, call `/usage`, automate Studio, or reuse an
API key. The provider reopens with a read-only API or export, documented for
third parties, without sessions or credentials, and authorized for automatic
queries.

## Cline

### Evaluated source

Cline publishes an Enterprise API with GET endpoints for profile, balance,
usage, metrics, and organization usage. An API key created by the owning
person would be a possible manual source. The app does not take session tokens
or search for keys in another application.

Current documentation does not publish schemas, units, filters, pagination,
errors, or a read-only permission for balance and usage. The key also serves
the inference API, and the Enterprise API lists mutable operations. The
announced OpenAPI returned HTTP 404 at the gate. Therefore a safe contract to
implement the client does not exist yet.

Local Cline tasks contain full conversations, changes, files, commands, and
tool inputs and outputs. Local cost is an estimate that can differ from the
BYOK invoice. Tasks, history, sessions, logs, `providers.json`, tokens, and
exports are not read.

### Result

No adapter for now. The future manual path requires a schema or sanitized
fixture, explicit permission for the key, Windows smoke with GET and
revocation, and error states before the public build is enabled. ClinePass and
BYOK keep their independent sources and billing rules.

## Zed

### Evaluated source

Zed shows token usage of the active thread in its Agent Panel. That surface
covers the native agent. External agents and terminal threads keep their own
authentication and can expose different metrics.

Official code persists each thread with messages, tool results, model, and
token counters in the same compressed blob in `threads.db`. Decompressing or
querying that store to extract counters would give access to prompts,
responses, and tool data, outside TokenUsage's privacy limit. Documentation
does not publish a CLI, API, or aggregated metrics export for third parties.

### Result

Blocked. The public build does not open the thread database, automate the
panel, or use external-agent threads as Zed data. A future provider requires an
official, minimal, aggregated API or export that is suitable for third-party
queries.

## Cursor

### Chosen source

For Individual accounts, TokenUsage opens `state.vscdb` in read-only SQLite
mode and projects only the model, timestamps, and estimated context total that
Cursor stores in each `composerData:`. The size cap accepts the editor's
current state (tens of GB) because the query does not load the whole file. The
query does not return the full value, prompts, responses, paths, email,
commands, transcript, credentials, or unhashed IDs.

The source is tied to the local schema observed in Cursor `3.15.6`. Cursor
names those counters `estimatedTokens`: they represent the conversation's
current context, not accumulated billed tokens. That is why the app marks the
read as local, partial, and estimated. If the model matches an official
catalog, that context carries estimated API value. Auto and unknown models stay
unpriced. Gemini 3.8 Flash uses Cursor's published $0.75 input, $0.075 cache-read,
and $3.50 output rates for Cursor events. Other providers use Google's $3.75
output rate. The previous hook is out of the active path because the official
`stop` contract does not deliver token counters. That legacy hook was
withdrawn. Today the app automatically registers a `stop` hook at launch that
acts only as a refresh trigger (also with `tokenusage cursor install-hook`): it
discards the payload and updates TokenUsage's own data when each task ends.

When per-turn counters exist in `bubbleId:`, they take priority over the
conversation estimate. In the August 2026 check, the editor wrote `tokenCount`
as zero for input and output, and an install with hundreds of thousands of
bubbles cannot scan them all in one refresh. TokenUsage looks first at a
handful of recent turns: if they are still zero, it uses the conversation
estimate; if any counter is real, it reads turns with a positive value. The row
cap orders from newest to oldest, so a cutoff drops old turns and not today's.

Cursor's public Admin API remains the billable source for Teams and Enterprise.
It requires a separate administrative key and does not reuse the editor login.
The `POST /organizations/pooled-usage` contract publishes `remainingCents` for
the Enterprise organization pool. It stays out of this Individual integration
until a manual connection and authorized smoke are added.

Cost and event endpoints do not replace that pool contract. The team card shows
usage and cost, with provenance and cycle. It does not claim remaining quota
without `remainingCents`. An Individual account has no public remaining-balance
contract.

OpenUsage gets the remaining plan percentage, Extra Usage, and the Grok Bot
tile by reusing the editor login: RPC on `api2.cursor.sh`, private REST,
Stripe, and dashboard CSV. TokenUsage does not call those routes or read the
editor token. Organization `remainingCents` is not Grok Bot's weekly pool.

### Coverage and policy

- Individual: estimate of the current context of Agent conversations retained locally; no Tab, cloud agents, quota, or cost.
- Teams: the local read keeps the same partial coverage; cost and billing require a future Admin API connection.
- Business: legacy name that can appear in events; uses Teams semantics.
- Enterprise: the same contract per configured connection; multiple connections are not mixed by email.

Inside `state.vscdb` only `cursorDiskKV` with `composerData:` and `bubbleId:`
keys is allowed, plus a fixed JSON projection of scalar metadata: model,
timestamp, and counters. All other tables and values, search databases, AI Code
Tracking, another app's Credential Manager, token refresh, cookies created from
JWT, `api2.cursor.sh` RPC, private dashboard routes, Stripe, and private CSV
export stay forbidden.

The local gate is resolved as `integrated-local-estimate`. Tests confirm stable
identity, snapshot replacement, and reads of the allowlist fields. The Admin
API keeps its separate manual gate.



Upstream comparison source: [Cursor provider](https://github.com/robinebers/openusage/blob/9d2bf09f10e21f769494a525a9d65c84d7aeb1df/docs/providers/cursor.md).

## GitHub Copilot

### Chosen source

The public Billing REST API offers dedicated AI credits reports for a paid
personal account and for an organization. TokenUsage uses version `2026-03-10`,
a fine-grained token supplied by the user, and Windows Credential Locker.

The personal account requires `Plan: read`. The organization requires
`Administration: read` and an administrator. Each connection declares its
scope. The client resolves the login with `GET /user` and does not ask for the
username. The result shows used credits, covered discount, and net charge. The
organization view is labeled as the entity total and, if GitHub publishes it,
the Business or Enterprise `plan_type`.

### Coverage and policy

- Paid personal: usage and charge for Pro, Pro+, or Max under AI credits billing.
- Free and Student: `Unsupported` until a useful public response is validated.
- Legacy annual plan: outside the first subset.
- Business or Enterprise: organization total for administrators; an ordinary member receives `InsufficientPermission`.

The API does not return the effective allocation or the balance. The app does
not calculate remaining quota from plan tables because the flex portion changes
and organization pools depend on licenses and budgets.

`/copilot_internal/user`, simulated editor identity, extension files,
`hosts.yml`, another app's Credential Manager, cookies, and `gh auth` stay
forbidden. The provider ignores an existing editor or GitHub CLI session.

The gate is resolved as `implement-subset`. The public build stays off until
an authorized smoke and credential deletion.



Upstream comparison source: [Copilot provider](https://github.com/robinebers/openusage/blob/9d2bf09f10e21f769494a525a9d65c84d7aeb1df/docs/providers/copilot.md).

## Antigravity

### Quota

Antigravity documents [`/usage`](https://antigravity.google/docs/cli/commands/usage) and [`/credits`](https://antigravity.google/docs/cli-credits) inside its TUI, without machine output. Its [FAQ](https://antigravity.google/docs/faq) prohibits using the Antigravity login from third-party software. The app does not read Windows Credential Manager, does not automate the TUI, and does not call Cloud Code, the language server, or a private RPC.

### Permitted local source

- `.db` conversations with `gen_metadata` and tokens per generation
- read-only SQLite open
- a future statusline only if the user installs it explicitly and supplies minimum data

Encrypted `.pb` files, decryption, helper daemon, token, CSRF, and transcript
are excluded. The passive reader accepts the `antigravity`, `antigravity-cli`,
and `antigravity-ide` roots, limits files, rows, and BLOBs, and keeps as
partial any row that does not match the observed schema.

### Result

Active experimental local integration: it delivers tokens and estimated API
cost for known exact aliases, and keeps unknown models unpriced. Quota and
credits stay `Blocked` while the current contract applies.

Upstream comparison source: [Antigravity provider](https://github.com/robinebers/openusage/blob/9d2bf09f10e21f769494a525a9d65c84d7aeb1df/docs/providers/antigravity.md).

## Devin

### Chosen source

The public v3 API returns daily consumption of an organization. TokenUsage
accepts a service user with organization scope, `ManageBilling` permission,
manual ID, and key in Credential Locker. The client pins `api.devin.ai` and
calls only `GET /v3/organizations/{org_id}/consumption/daily`.

The card shows ACUs and a per-product breakdown during an explicit period. It
does not claim remaining quota or dollars.

### Coverage and policy

- Organization: experimental subset with daily ACUs and total.
- Self-serve: `Unsupported`; quota and balance remain only in the dashboard.
- Enterprise: aggregate and ACU limits outside the first subset because of the key's broad scope.
- Dedicated deployment: custom host outside the first subset.

The CLI file, app SQLite, `server.codeium.com` RPC, simulated identity, host
taken from configuration, and Session Insights stay forbidden. Session Insights
returns ACUs, but also session material that the engine does not need.

The gate is resolved as `implement-experimental-subset`. The `ManageBilling`
permission must be scoped to a single organization and pass an authorized smoke
before the public build is enabled.



Upstream comparison source: [Devin provider](https://github.com/robinebers/openusage/blob/9d2bf09f10e21f769494a525a9d65c84d7aeb1df/docs/providers/devin.md).

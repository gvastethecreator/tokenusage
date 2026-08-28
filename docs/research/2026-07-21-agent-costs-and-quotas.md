# Local quotas and spend for Grok, Antigravity, and OpenCode

Date: 2026-07-21

Status: decision ready to add to the plan

## Question

Can we add Grok Build, Antigravity CLI, and OpenCode, show spend when there is no suitable remaining quota, and do it without asking for another login or using someone else's credentials?

## Answer

Yes for a local token and spend engine. Grok Build and OpenCode have useful local Windows paths. Antigravity CLI can enter through passive reads of local databases, but it needs a spike with real data before promising coverage.

Live quota has a different limit. Grok offers `/usage` in its product, but it does not document a quota output that another app can use. Antigravity offers `/usage` and `/credits` inside its TUI, and its FAQ forbids using the Antigravity login from third-party tools. TokenUsage will not automate those private paths.

## Pinned local references

The clones live under `.reference/` and the root repo ignores them:

| Project | SHA | Use in this research |
|---|---|---|
| [OpenUsage](https://github.com/robinebers/openusage) | `9d2bf09f10e21f769494a525a9d65c84d7aeb1df` | quota, card, and provider parity |
| [CodeBurn](https://github.com/getagentseal/codeburn) | `6e3c57a9ff95a624f1d9affa7384d32a67f359b7` | session readers and local aggregation |
| [AgentsView](https://github.com/kenn-io/agentsview) | `1ee2de88e2dae54326d8b47aeb2de2f58b5944f9` | coverage, deduplication, and pricing contracts |

CodeBurn and AgentsView use the MIT license. They serve as comparison and a design corpus. TokenUsage will have its own contracts and code, with a smaller scope: tokens, cost, model, date, and coverage. It will not index transcripts, commands, tools, tasks, or results.

## Chosen common model

Each agent exposes three independent capabilities:

1. `Quota`: used or remaining percentage, limit, and reset when a suitable interface exists.
2. `ObservedUsage`: tokens observed on this machine, grouped by day, agent, and model.
3. `Spend`: cost reported by the source or estimated with a versioned catalog.

A card can show any subset. The UI always states source, age, and coverage. `Estimated spend` is the value at known rates; it does not claim a subscription charge.

The local engine normalizes only:

- agent and model provider when they are known;
- model;
- date and time zone;
- input, output, reasoning, cache-read, and cache-write tokens;
- reported or estimated USD cost;
- event key for deduplication;
- provenance, parser version, and coverage.

The first delivery aggregates by day, agent, and model. Project and session stay out so the app does not store names or create a conversation index.

## Grok Build

### Official sources

[Grok Build](https://docs.x.ai/build/overview) supports Windows, browser login, API key, headless mode, and Agent Client Protocol. Its [CLI reference](https://docs.x.ai/build/cli/reference) documents `grok agent stdio`. The [changelog](https://x.ai/build/changelog) records `/usage`, usage percentages, prepaid credits, and cost/tokens in headless output.

The [Grok FAQ](https://docs.x.ai/grok/faq) describes a shared weekly pool and a usage view with percentage, reset, and credits. It does not offer a JSON quota contract for another app. The [xAI acceptable use policy](https://x.ai/legal/acceptable-use-policy) restricts automated access. For that reason, the private billing endpoint that OpenUsage uses does not turn on in a public build.

### Local path

The references show two generations of local data:

- `GROK_HOME/sessions/.../summary.json`, `signals.json`, and `updates.jsonl`;
- `GROK_HOME/logs/unified.jsonl`.

The session source has priority. Some versions include `params.update.usage`, a per-model breakdown, tokens, and `costUsdTicks`. If reported cost is missing, the engine estimates from tokens and labels the result. The unified log is a fallback and is never added on top of a session already counted.

### Windows test

- binary: `C:\Users\cristian\.grok\bin\grok.exe`;
- observed version: `0.2.106`;
- sessions and `unified.jsonl`: present;
- last 2,000 lines examined for schema only: 1,992 valid JSON records;
- the test did not read or print the contents of `auth.json`, prompts, models, costs, or tokens.

### Decision

- local tokens and spend: beta;
- live quota and balance: `PolicyBlocked` until there is a suitable official output or written permission;
- never start login or read `auth.json` for the local scanner.

## OpenCode

### Official sources

The [OpenCode CLI](https://opencode.ai/docs/cli/) offers `opencode stats` for tokens and costs, filters by days, models, and project, plus session export. The [troubleshooting guide](https://opencode.ai/docs/troubleshooting/) documents `%USERPROFILE%\.local\share\opencode` on Windows. The [Windows and WSL guide](https://opencode.ai/docs/windows-wsl/) pins `~/.local/share/opencode` inside each WSL distro.

`opencode stats` does not offer JSON in the examined version. It will be used as a differential oracle, not as text to parse.

### Local path

OpenCode has used two formats that can coexist:

- `opencode.db`, with session, message, and part;
- `storage/session`, `storage/message`, and `storage/part` as JSON.

`step-finish` messages or parts include model, `cost`, and `tokens` with input, output, reasoning, and cache. Reported cost has priority. The catalog covers only rows without cost.

The reader opens SQLite in read-only mode, queries the minimum columns, and uses a short `busy_timeout`. It does not copy a large database. It processes the WAL without creating or changing files. A lock keeps the last aggregate and marks partial coverage.

### Windows test

- detected binary: `C:\Users\cristian\.bun\bin\opencode.exe`;
- observed version: `1.18.4`;
- `opencode stats --help`: correct, with flags for days, models, and project;
- `opencode.db` and `storage`: present;
- observed database size: about 2.5 GB, a reason to avoid a full copy and a transcript scan;
- the test did not open `auth.json` or run a report with the user's figures.

OpenCode can also run inside WSL. WSL detection needs consent because a Windows app must enumerate distros and open files through `\\wsl$`. The first beta covers the native Windows install; WSL is a separate task.

### Decision

- local tokens and spend: beta, together with Grok Build;
- `opencode stats`: differential total check in fixtures and opt-in smoke;
- common quota: does not apply, because OpenCode can use several providers and plans;
- `auth.json`: out of the reader.

## Antigravity CLI

### Official sources

[Antigravity CLI install](https://antigravity.google/docs/cli-install) supports Windows and stores silent login in Windows Credential Manager. `/usage` shows quota inside the TUI per its [documentation](https://antigravity.google/docs/cli/commands/usage); `/credits` has its own [view](https://antigravity.google/docs/cli-credits). The [statusline](https://antigravity.google/docs/cli-statusline) exposes usage of the active context, but not the global subscription quota.

The [Antigravity FAQ](https://antigravity.google/docs/faq) states that using third-party software to access Antigravity with the Antigravity login violates its terms and can suspend the account. That limit rules out reading Credential Manager, calling Cloud Code, querying a private language server, or automating `/usage`.

### Allowed local path

The references detect `.db` conversations with a `gen_metadata` table. Its rows contain model and token counters. A passive reader can extract those fields without using the login. Only clear local formats without decryption are accepted:

- SQLite `.db` in read-only mode;
- statusline events that the user configures explicitly in the future;
- no encrypted `.pb`, helper daemon, token, CSRF, or private local RPC.

The examined install has `agy.exe` `1.1.5`, but `%USERPROFILE%\.gemini\antigravity-cli` does not exist yet. There is no local corpus to verify the parser.

### Decision

- quota and credits: blocked by policy;
- local tokens and cost: experimental until a real `.db` is tested and fixtures are sanitized;
- a public build must fail closed if it finds only encrypted data or a private source.

## Price catalog

Precedence order:

1. cost reported by the agent;
2. price fixed by provider and model when the contract is clear;
3. dated embedded snapshot of the [LiteLLM catalog](https://github.com/BerriAI/litellm/blob/main/model_prices_and_context_window.json);
4. no cost when the model has no exact match.

The engine does not match by substring. Each estimate stores catalog version and applied rate. Catalog updates are reviewed at build time; the app does not download an unsigned table during normal use.

## Uncertainty

- Local formats are not a stable API and need fixtures per version.
- Grok quota might gain an official output; review it before each beta.
- Antigravity still lacks a real Windows fixture on this machine.
- Native OpenCode and OpenCode in WSL use different roots.
- A reported cost can represent an API rate, a router price, or a promotional zero; the UI keeps provenance.

## Final decision

Build a small local engine of our own. The beta will include Claude, Grok Build, and OpenCode on the same contract. Antigravity CLI comes later as an experimental passive parser. Codex keeps its official source for quota and usage. Each provider can deliver quota, usage, or spend independently, and the card adapts to the available set.

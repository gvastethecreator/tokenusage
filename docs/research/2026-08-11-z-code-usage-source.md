# ZCode usage source research

Date: 2026-08-11

Decision: `block`

## Executive result

ZCode has useful usage data in its desktop interface. It does not publish a safe machine contract for that data.

The local observation found ZCode `3.7.6` installed. The observation did not read credentials, logs, databases, sessions, prompts, responses, code, or other content.

TokenUsage must not add a ZCode reader. The provider stays blocked until ZCode publishes an approved usage export or read-only API.

## Data availability and TokenUsage decision

This table separates data existence from technical capture and product
suitability. A value visible inside ZCode is not automatically safe for a
third-party reader.

| Data | Does it exist? | Technical surface | Stability | TokenUsage decision |
|---|---|---|---|---|
| Installed product and version | Yes | Windows executable metadata exposes the installed name and version. | Stable for discovery, but not a usage source. | Suitable for local product detection only. |
| Total local tokens | Yes | App Usage reads local session records and displays token totals. ZCode publishes no export or reader contract. | Visible in the UI, but the storage contract is private and unsupported. | Blocked. Do not read private local records. |
| Daily token history | Yes | App Usage displays daily trends for all time, 30 days, and 7 days. | UI behavior is documented. Machine-readable history is not documented. | Blocked. |
| Tokens by model | Yes | App Usage displays model totals. Coding Plan also displays remote model use. | The displayed metric is documented, but no stable external schema exists. | Blocked. |
| Sessions, messages, and active days | Yes | App Usage displays aggregate counts. | UI-only. No supported export or API fields are published. | Blocked. |
| Per-call input, output, cached, and total tokens | Yes | The public Z.ai Chat Completion response returns documented JSON counters. | Stable for the individual API request made by that client. It is not ZCode history. | Not suitable for the ZCode provider. A separate Z.ai integration can use a user-supplied key. |
| Five-hour and weekly plan quota | Yes | Coding Plan displays it. A first-party Claude Code plugin can request aggregate JSON with a live credential. | The UI is documented. The plugin endpoints and response schema are not public contracts. | Blocked. Do not reuse credentials or call private endpoints. |
| Monthly limit | Partly | Usage Stats mentions a monthly MCP quota. Current plan docs use credits and separate reset periods. | Meanings differ across plan generations. No stable machine contract exists. | Blocked and unavailable as a normalized monthly token limit. |
| Current context or session allowance | Partly | Coding Plan can display context-window and remaining-pool details. | UI-only. No provider-authoritative session limit field is published. | Unavailable. Do not infer a session limit. |
| Tool or MCP use | Yes | Coding Plan displays tool calls. The first-party plugin requests aggregate tool data. | The private endpoint is unversioned. Plan accounting has changed. | Blocked. |
| Money cost | Not in the documented usage outputs | App Usage and documented API responses expose tokens, not a billed money value. | No supported ZCode billing field or export exists. | Unavailable. Do not convert credits into an invoice estimate. |
| Hook activity | Yes | ZCode provides documented session, prompt, tool, and stop events. | The protocol is documented, but it contains content fields and no token, cost, or quota counters. | Suitable only for an opt-in activity collector with a strict metadata allowlist. |
| Support logs and project data | Yes | ZCode documents these surfaces for support and product features. | They can contain private content and are not usage contracts. | Reject. Never scan them for usage. |

The technical answer has two parts. TokenUsage can count activity through an
opt-in hook, but it cannot obtain tokens or plan limits from that hook. ZCode
itself can read local usage, and its first-party plan plugin can fetch remote
aggregates. Neither usage path is suitable under the current public contracts.

An activity-only collector can persist these allowlisted values:

- receipt time and hashed session identity
- session-start count and model when ZCode supplies it
- submitted prompt count without prompt text
- tool-call count, tool name, and success or failure without tool data
- completed-turn count without the assistant message

This collector must discard paths, prompts, responses, tool input, tool output,
commands, workspace data, and raw session identities before any write.

## Product identity

The official product name is **ZCode**. It is an Agentic Development Environment with the first-party ZCode Agent.

The service provider and data controller is JINGSHENG HENGXING TECHNOLOGY PTE.LTD. Z.ai and BigModel are account and model channels.

This product is not Microsoft Project Z-Code. It is also not the community `simonyos/Z-CODE` project.

Sources:

- [ZCode official site](https://zcode.z.ai/en)
- [ZCode Privacy Policy](https://zcode.z.ai/en/privacy)
- [ZCode Terms of Service](https://zcode.z.ai/en/terms)

## Local safety boundary

The local observation recorded only the installed product and version. It did not open or parse ZCode data.

The observation did not read:

- account tokens, API keys, cookies, or login state
- logs or support archives
- databases or database schemas
- tasks, sessions, conversations, or transcripts
- prompts, responses, tool calls, commands, or workspace content

No local data-path claim in this report comes from inspection. Each path below comes from official ZCode documentation.

## Official command surfaces

ZCode does not document a standalone usage CLI. The install guide only says that Linux can start the app from a command line.

The documentation does not publish `zcode --help`, usage flags, JSON output, or a usage-export command. This absence is a documentation finding.

ZCode Agent has two built-in slash commands:

| Command | Function |
|---|---|
| `/goal` | Manage the goal for the current session. |
| `/compact` | Compact the current conversation context. |

Custom commands store prompts as Markdown files. They are agent features, not an operating-system CLI.

Sources:

- [Install](https://zcode.z.ai/en/docs/install)
- [Command](https://zcode.z.ai/en/docs/commands)

## Official usage interfaces

### App Usage

The App Usage tab reads local ZCode session records. It shows these values:

- tokens, sessions, messages, and active days
- daily token trends and an activity heatmap
- token totals by model
- ranges for all time, 30 days, and 7 days

ZCode does not publish the record path, schema, retention rule, or compatibility policy. It also does not publish an export for these records.

### Coding Plan

The Coding Plan tab reads remote Z.ai or BigModel statistics. It shows these values:

- 5-hour and weekly quota state
- token use by model
- MCP tool calls
- context-window, message, and tool shares
- remaining quota for each visible pool

The Usage Stats page also mentions a monthly MCP quota. It does not define a public API for these values.

Source: [Usage Stats](https://zcode.z.ai/en/docs/usage-stats).

## Machine-readable candidates

### ZCode hooks

ZCode publishes a local JSON protocol for hooks. The protocol includes session, prompt, tool, permission, and stop events.

The documented hook fields do not include token use, cost, plan credits, or quota. The `Stop` event includes the last assistant message.

The hook input can include a temporary `transcript_path`. ZCode deletes that temporary directory after the hook finishes.

This source is not suitable for TokenUsage. It exposes content and does not provide the required numeric usage fields.

Source: [Hooks](https://zcode.z.ai/en/docs/hooks).

### Z.ai response usage

The public Z.ai Chat Completion API returns machine-readable counters for one model call:

- `usage.prompt_tokens`
- `usage.completion_tokens`
- `usage.prompt_tokens_details.cached_tokens`
- `usage.total_tokens`

This response does not provide ZCode history, local session totals, plan quota, or money cost. It covers only calls made through that API client.

The API requires `Authorization: Bearer ZAI_API_KEY`. TokenUsage cannot reuse a key from ZCode or another product.

Sources:

- [Z.ai Chat Completion](https://docs.z.ai/api-reference/llm/chat-completion)
- [Z.ai API introduction](https://docs.z.ai/api-reference/introduction)

### Official GLM plan plugin

Z.ai publishes `glm-plan-usage` in its official Claude Code plugin repository. This plugin is not a ZCode CLI.

Its documented installation and command flow is:

```text
claude plugin marketplace add zai-org/zai-coding-plugins
claude plugin install glm-plan-usage@zai-coding-plugins
/glm-plan-usage:usage-query
```

The bundled script runs with Node.js and prints JSON. It requests three aggregate endpoints:

```text
GET /api/monitor/usage/model-usage
GET /api/monitor/usage/tool-usage
GET /api/monitor/usage/quota/limit
```

The script reads `ANTHROPIC_BASE_URL` and `ANTHROPIC_AUTH_TOKEN`. It sends the token in the `Authorization` header.

The model and tool queries cover an approximate 24-hour window. The quota query returns the current quota state.

The public Z.ai API reference does not document these monitor endpoints. It also does not publish a versioned response schema.

This code is first-party evidence, but it is not a stable public API contract. It also requires a live credential from another tool.

TokenUsage must not run the plugin, read its environment variables, or call these endpoints.

Sources:

- [Official GLM plan usage plugin](https://github.com/zai-org/zai-coding-plugins/tree/main/plugins/glm-plan-usage)
- [Official usage-query script](https://github.com/zai-org/zai-coding-plugins/blob/main/plugins/glm-plan-usage/skills/usage-query-skill/scripts/query-usage.mjs)

## Plans, tokens, and cost

The ZCode application is free. A user still needs a model plan, account, or API key.

Source: [ZCode FAQ](https://zcode.z.ai/en/docs/qa).

### Trial

The documented new-user trial lasts five days. It gives these daily token amounts during the trial:

| Model | Daily trial amount |
|---|---:|
| GLM-5.2 | 3 million tokens |
| GLM-5-Turbo | 2 million tokens |

The token amounts expire after the five-day trial. They are not a permanent daily allowance.

Source: [Connect Models and Plans](https://zcode.z.ai/en/docs/configuration).

### Current individual plans

The current GLM Coding Plan uses credits. Each plan has a 5-hour limit and a weekly limit.

| Plan | 5-hour credits | Weekly credits | Estimated weekly tokens on GLM-5.2 |
|---|---:|---:|---:|
| Lite | 2,000 | 10,000 | 43-87 million |
| Pro | 12,000 | 60,000 | 263-526 million |
| Max | 28,000 | 140,000 | 613-1,226 million |

The token estimates assume a 90.9% cache-hit rate. The range also depends on peak or off-peak use.

The plan calculates model credits with this formula:

```text
(input tokens * input multiplier
 + cached input tokens * cached-input multiplier
 + output tokens * output multiplier) / 10,000
```

The current plan page does not publish a monthly token limit for individual plans. Billing can be monthly, but the usage pools reset every 5 hours and 7 days.

Source: [GLM Coding Plan overview](https://docs.z.ai/devpack/overview).

### Legacy and documentation drift

Z.ai changed new subscriptions to credits on 2026-07-30. Active legacy plans keep their old limits until their billing cycle ends.

The ZCode Usage Stats page still describes a monthly MCP quota. The current plan page charges model and MCP use through credits.

TokenUsage cannot merge these meanings. A future source must identify the plan generation and each quota type.

Source: [Plan Update Announcement](https://docs.z.ai/devpack/notice/usage-revision).

### Money cost

App Usage documents token totals, not money cost. The per-call Z.ai response also returns tokens, not a billed money value.

TokenUsage must mark ZCode money cost as unavailable. It must not convert plan credits into an invoice estimate.

## Documented local paths

ZCode publishes these local paths for other functions:

| Path | Documented function | Usage decision |
|---|---|---|
| `~/.zcode/commands` | User commands | Reject. The files contain prompts. |
| `~/.zcode/cli/config.json` | User hook configuration | Reject. This is configuration, not usage. |
| `<workspace>/.zcode/config.json` | Workspace hook configuration | Reject. This is project data. |
| `~/.zcode/skills/<skill-name>/SKILL.md` | User skills | Reject. The files contain instructions. |
| `~/.zcode/cli/memories/projects/<project>/memory/` | Project memory | Reject. The files contain project context. |
| `%USERPROFILE%\.zcode\logs` | Windows support logs | Reject. The logs are support data. |

None of these paths is a documented usage ledger. ZCode does not publish a safe path for the App Usage records.

Sources:

- [Command](https://zcode.z.ai/en/docs/commands)
- [Hooks](https://zcode.z.ai/en/docs/hooks)
- [Skill](https://zcode.z.ai/en/docs/skill)
- [Memory](https://zcode.z.ai/en/docs/memory)
- [Feedback and Support](https://zcode.z.ai/en/docs/feedback)

## Authentication boundaries

ZCode supports these model connections:

- authorization with a Z.ai account
- authorization with a BigModel account
- a user-supplied API key
- compatible OpenAI or Anthropic providers

The desktop configuration and terminal environment variables are separate. ZCode does not synchronize them automatically.

The Coding Plan endpoint is `/api/coding/paas/v4`. The general prepaid API endpoint is `/api/paas/v4`.

These endpoints are not interchangeable. Account authorization lets ZCode select the correct route automatically.

Source: [Connect Models and Plans](https://zcode.z.ai/en/docs/configuration).

The GLM Coding Plan only supports listed coding tools. The policy prohibits account sharing and unsupported use.

Source: [GLM Coding Plan Usage Policy](https://docs.z.ai/devpack/usage-policy).

## Policy result

The ZCode terms prohibit automated scraping, reverse engineering, and attempts to extract data from ZCode. This rule closes the undocumented local-reader path.

Source: [ZCode Terms of Service](https://zcode.z.ai/en/terms).

| Candidate | Data | Result |
|---|---|---|
| App Usage records | Local tokens and session counts | Blocked. No path, schema, export, or reader permission. |
| Coding Plan UI | Remote quota and token totals | Blocked. No public read-only contract. |
| ZCode hooks | Activity events | Activity-only candidate. No tokens, cost, or quota. |
| Z.ai per-call response | Tokens for one API request | Partial API data only. It is not ZCode history. |
| Official GLM plugin | Aggregate JSON and quota | Blocked. It needs another tool's credential and uses undocumented endpoints. |
| Support logs | Diagnostic data | Blocked. They are not a usage ledger. |

## Fail-closed decision

Keep ZCode token usage and quota at `policy-blocked`. Do not add a local usage
scanner, plugin runner, or remote client.

An opt-in hook can support a separate `Activity` view. It must not appear as
token usage, cost, quota, or billing coverage.

TokenUsage must not read `.zcode`, support logs, databases, sessions, configuration, environment credentials, prompts, or content.

Reopen the provider only if ZCode publishes one of these contracts:

1. A versioned local export with usage-only fields and no session content.
2. A documented read-only database contract with permission for third-party readers.
3. A public usage API with minimum-scope authentication and stable response schemas.

The contract must define token fields, timestamps, retries, retention, quota types, reset rules, cost meaning, and compatibility rules.

## Primary sources

- [ZCode official site](https://zcode.z.ai/en)
- [ZCode Privacy Policy](https://zcode.z.ai/en/privacy)
- [ZCode Terms of Service](https://zcode.z.ai/en/terms)
- [ZCode Install](https://zcode.z.ai/en/docs/install)
- [ZCode Command](https://zcode.z.ai/en/docs/commands)
- [ZCode Usage Stats](https://zcode.z.ai/en/docs/usage-stats)
- [ZCode Hooks](https://zcode.z.ai/en/docs/hooks)
- [ZCode Connect Models and Plans](https://zcode.z.ai/en/docs/configuration)
- [ZCode Feedback and Support](https://zcode.z.ai/en/docs/feedback)
- [GLM Coding Plan overview](https://docs.z.ai/devpack/overview)
- [GLM Coding Plan Usage Policy](https://docs.z.ai/devpack/usage-policy)
- [GLM Plan Update Announcement](https://docs.z.ai/devpack/notice/usage-revision)
- [Z.ai API introduction](https://docs.z.ai/api-reference/introduction)
- [Z.ai Chat Completion API](https://docs.z.ai/api-reference/llm/chat-completion)
- [Official Z.ai coding plugins](https://github.com/zai-org/zai-coding-plugins)
- [Official usage-query script](https://github.com/zai-org/zai-coding-plugins/blob/main/plugins/glm-plan-usage/skills/usage-query-skill/scripts/query-usage.mjs)

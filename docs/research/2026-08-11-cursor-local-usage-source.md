# Cursor local usage source

Date: 2026-08-11

Decision: `research-only`, with an opt-in hook candidate

## Question

Can TokenUsage collect real Cursor usage from an installed and authenticated Cursor client without reading secrets or customer content?

## Answer

Cursor 3.15.6 does not expose a safe passive local usage ledger on this machine. The local databases store application state, conversations, and AI-code attribution. Their public table columns do not include billable token or cost totals.

The installed client has a better candidate. Cursor supports user-level command hooks through JSON over standard input and output. The installed 3.15.6 bundle passes turn token counters to the `stop` hook. These token fields are not part of the public hook schema. This path is useful for a version-bound prototype, but it is not a stable provider contract.

The Cursor Admin API remains the published source for detailed team usage and charges. It requires a separate administrator key. It does not reuse the authenticated desktop session.

## Safety boundary

The local inspection read only these facts:

- executable metadata and package metadata.
- directory and file names, sizes, and dates.
- SQLite table definitions, indexes, journal modes, and row counts.
- field names in the installed first-party JavaScript bundle.

The inspection did not read database values, hook payloads, credentials, prompts, responses, commands, transcripts, or customer code. Project names and full user paths are not in this report.

## Installed client

| Item | Observation |
|---|---|
| Editor executable | `%LOCALAPPDATA%\Programs\cursor\Cursor.exe` |
| Product and file version | `3.15.6` |
| Bundled command | `%LOCALAPPDATA%\Programs\cursor\resources\app\bin\cursor.cmd` |
| Command result | Version `3.15.6`, commit `a1f686545fd0ce8917bbd2449f733551a9bce420`, architecture `x64` |
| Process | The same `Cursor.exe` version was running during inspection. |
| PATH | The `cursor` command was not on the current PATH. The visible `agent.exe` belongs to another installed product. |
| Authentication | The task states that Cursor is authenticated. Authentication data was not opened or validated. |

## Local roots and formats

### Roaming application data

The main root is `%APPDATA%\Cursor`. The relevant user data is under `%APPDATA%\Cursor\User`.

`%APPDATA%\Cursor\User\globalStorage\state.vscdb` is SQLite with WAL enabled. Its observed tables are:

| Table | Public schema | Use for TokenUsage |
|---|---|---|
| `ItemTable` | `key`, `value`, with unique replacement by `key` | Reject. Values mix application state and authentication state. |
| `cursorDiskKV` | `key`, `value`, with unique replacement by `key` | Reject. Values can contain conversation state. |
| `composerHeaders` | `composerId`, `workspaceId`, `createdAt`, `lastUpdatedAt`, archive, subagent, recency, checkpoint, `value` | Reject. It is conversation metadata with a content-bearing value. |

The table schemas contain timestamps and conversation identifiers. They do not contain token or cost columns. The database had active `-wal` and `-shm` files during inspection.

`%APPDATA%\Cursor\User\globalStorage\conversation-search.db` is also SQLite with WAL enabled. It contains FTS tables and conversation index tables. This database is a search corpus, not a usage ledger. TokenUsage must not open it.

`%APPDATA%\Cursor\User\workspaceStorage` contained one opaque workspace directory. The inspection did not open its project metadata or content.

### Cursor home

The root `%USERPROFILE%\.cursor` contained agent, plugin, project, extension, skill, and AI-tracking directories.

`%USERPROFILE%\.cursor\ai-tracking\ai-code-tracking.db` is SQLite with delete journaling. Its observed tables include:

| Table | Relevant columns | Decision |
|---|---|---|
| `ai_code_hashes` | `hash`, `source`, `requestId`, `conversationId`, `timestamp`, `model`, `createdAt` | Has model and time, but no tokens or cost. |
| `ai_deleted_files` | file identity, conversation identifiers, `model`, `deletedAt` | Reject because it is code attribution data. |
| `conversation_summaries` | conversation identifier, summary text, `model`, `mode`, `updatedAt` | Reject because it contains conversation text. |
| `scored_commits` | commit identity, line counters, dates, AI percentages | Reject because it contains repository and commit data. |
| `tracked_file_content` | project path, file content, conversation identifier, `model`, `createdAt` | Reject because it contains customer code. |
| `tracking_state` | `key`, `value` | Not a usage schema. |

The database had no observed token or cost column. It cannot provide Cursor spend or model-token totals.

### Logs and hooks

`%APPDATA%\Cursor\logs` contained one timestamped session directory. No file name included `usage`, `token`, `cost`, or `billing`. One session is insufficient to establish retention or rotation behavior.

Neither `%USERPROFILE%\.cursor\hooks.json` nor `C:\ProgramData\Cursor\hooks.json` existed. No hook was installed or executed during this research.

Cursor documents user, project, team, and enterprise hooks. Command hooks receive JSON through standard input. User hooks live under `%USERPROFILE%\.cursor`. See the [Cursor Hooks reference](https://cursor.com/docs/hooks).

## Field availability

| Field | Local passive databases | Public hook contract | Cursor 3.15.6 installed bundle | Admin API |
|---|---|---|---|---|
| Stable conversation identity | Present in content-bearing stores | `conversation_id` is stable across turns. | Passed to `stop` and `afterAgentResponse`. | `conversationId` can be present. |
| Stable turn identity | Not a usage column | `generation_id` changes with each user message. | Passed to `stop` and `afterAgentResponse`. | No documented event identifier. |
| Model | Present in AI-code tracking and composer state | `model` and optional `model_id` | Passed to the candidate hooks. | `model` is documented. |
| Exact usage time | No safe usage record | No turn timestamp | The candidate hook has no provider timestamp. | `timestamp` is documented. |
| Input tokens | No safe usage record | Not documented | `input_tokens` is passed to the candidate hooks. | `tokenUsage.inputTokens` is documented for token-based calls. |
| Output tokens | No safe usage record | Not documented | `output_tokens` is passed to the candidate hooks. | `tokenUsage.outputTokens` is documented for token-based calls. |
| Cache read tokens | No safe usage record | Not documented | `cache_read_tokens` is passed to the candidate hooks. | `tokenUsage.cacheReadTokens` is documented for token-based calls. |
| Cache write tokens | No safe usage record | Not documented | `cache_write_tokens` is passed to the candidate hooks. | `tokenUsage.cacheWriteTokens` is documented for token-based calls. |
| Reasoning tokens | No safe usage record | Not documented | An internal `turnEnded` message has `reasoningTokens`. The candidate hooks do not pass it. | Not documented in usage events. |
| Cost | No safe usage record | Not documented | No cost is passed to the candidate hooks. | `totalCents`, `chargedCents`, and optional `cursorTokenFee` are documented. |

The public hook base schema also includes workspace roots, email, and transcript paths. Some hook events include prompt, response, command, or file content. TokenUsage must never register for those events.

The `preCompact` hook exposes context-window counters. Those counters describe compaction state. They are not billable turn usage and must not enter spend reports.

The installed bundle is first-party local evidence, but it is not a public compatibility promise. The undocumented token fields need a parser version tied to Cursor 3.15.6.

## Official remote source

`POST /teams/filtered-usage-events` returns hourly aggregated usage events. Documented fields include model, timestamp, token counters, model cost, charged cost, and an optional Cursor token fee. Cursor recommends polling at most once per hour. See the [Cursor Admin API](https://cursor.com/docs/account/teams/admin-api#Get-Usage-Events-Data).

The Admin API has no documented unique event identifier. Its pagination can move while an hour receives new usage. A collector must reconcile a rolling time window instead of appending every returned row.

`chargedCents` is the charge field for reconciliation. `totalCents` is the model cost inside `tokenUsage`. Cursor pricing can also include a separate token fee. TokenUsage must keep these values separate. See [Models and Pricing](https://cursor.com/docs/models-and-pricing).

The Admin API requires Basic authentication with an administrator-created key. This source does not authorize reading the desktop session or credential store.

## Rotation and deduplication

### Existing local stores

`state.vscdb` uses SQLite WAL files. `ItemTable` and `cursorDiskKV` replace rows by key. `composerHeaders` replaces a row by `composerId`. These rules describe application-state updates, not usage-event rotation.

No usage-specific local log was present. The observed logs cannot establish retention, rotation, or a stable usage schema.

### Candidate hook spool

An opt-in helper can write only allowlisted numeric fields to a TokenUsage-owned spool. The helper must discard all other input fields without logging them.

Use a SHA-256 event key over these values:

```text
cursor | hook-v1 | cursor_version | conversation_id | generation_id
```

Hash the identifiers before persistence. Do not store either raw identifier. If the same key arrives again, replace its counters instead of adding them. This rule handles retries and hook replays.

Use the hook receipt time as `OccurredAtUtc`. Mark it as derived because Cursor does not provide the exact turn time. Record `cursor_version` in the parser version.

The spool needs bounded size, atomic replacement, and explicit retention. TokenUsage must own these rules because Cursor exposes no rotation contract for this candidate.

### Admin API reconciliation

Treat each recent API period as a windowed snapshot. Re-read the current and prior hour. Replace matching normalized rows inside that window. Do not assume that page order or row position is stable.

## Coverage limits

The candidate hook has these known gaps:

- It observes only activity after the user enables the hook.
- The token fields are undocumented and can disappear in a Cursor update.
- A user-level hook does not run for cloud agents.
- The public subagent hook schema has no token counters.
- Tab completions have separate hooks and no documented token counters.
- The hook model can be the selected model. Auto routing can make the actual provider model unclear.
- The hook has no reported cost, charged cost, quota balance, or billing-cycle total.
- The hook has no exact provider timestamp.
- Aborted, failed, restarted, and multi-step turns need real runtime proof.

As a result, hook data is partial local usage. It is not a Cursor invoice and it is not quota data.

Cursor states that its monthly usage pools are visible in editor settings and the dashboard. It does not document a local programmatic pool feed. See [Models and Pricing](https://cursor.com/docs/models-and-pricing#usage-pools).

## TokenUsage architecture mapping

No product code changes are part of this research. A future implementation can use the existing local-first flow:

```text
Cursor stop hook
  -> content-dropping helper
  -> TokenUsage-owned numeric spool
  -> CursorHookUsageEventSource
  -> LocalUsageRefresh
  -> UsageRepository and daily_usage_rollup
  -> shared CLI and dashboard projections
```

The provider boundary belongs in `TokenUsage.Providers`. Windows hook discovery and opt-in configuration belong in `TokenUsage.Runtime.Windows` or `TokenUsage.Platform.Windows`. Core contracts need no new content fields.

Suggested normalization:

| TokenUsage field | Candidate hook source |
|---|---|
| `AgentId` | `cursor` |
| `ModelId` | normalized `model_id`, then `model` |
| `ModelProviderId` | absent unless an exact mapping exists |
| `OccurredAtUtc` | local receipt time, marked as derived |
| input, output, cache read, cache write | corresponding numeric hook fields |
| reasoning | zero with an explicit omitted-field note |
| cost | `Unavailable` |
| coverage | `Unpriced` with a partial-source diagnostic |
| parser version | hook contract plus Cursor version |

The source can implement `IWindowedSnapshotUsageEventSource`. A short reconciliation window can replace repeated observations. `LocalUsageRefresh` already isolates source failures and writes durable daily rollups.

The UI and CLI must label this source as `Local, partial, unpriced`. They must not show quota remaining or reported spend from hook data.

The Admin API remains a separate manual-key provider. Its `chargedCents` maps to provider-reported cost. Its model cost and Cursor fee must remain separate provenance values until the domain supports both.

## Decision and gates

1. Keep passive Cursor database scanning blocked.
2. Do not read `state.vscdb`, conversation search, transcripts, AI-code tracking, logs, or the client credential store for usage.
3. Permit a small opt-in `stop` hook prototype only after explicit user approval.
4. Bind the prototype parser to Cursor 3.15.6. Emit no event when the numeric fields are absent.
5. Persist only normalized numbers, a model identifier, a derived time, and hashed event identity.
6. Prove completed, aborted, failed, retried, subagent, Auto, Tab, and client-upgrade behavior before product activation.
7. Keep the source disabled by default until two supported Cursor versions pass the same sanitized runtime probe.
8. Use the Admin API with a manual administrator key for billing-grade Teams or Enterprise usage.

The current readiness result is `blocked` for passive local collection and `candidate` for an explicit hook integration.

## Primary sources

- [Cursor Hooks reference](https://cursor.com/docs/hooks)
- [Cursor Admin API](https://cursor.com/docs/account/teams/admin-api)
- [Cursor Models and Pricing](https://cursor.com/docs/models-and-pricing)
- [Cursor Privacy and Security](https://cursor.com/docs/account/privacy)

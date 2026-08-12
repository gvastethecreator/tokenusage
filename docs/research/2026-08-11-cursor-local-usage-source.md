# Cursor local usage source

Date: 2026-08-11

Decision: `integrated-local-estimate`

## Result

Cursor 3.15.6 stores an allowlisted usage-like summary for each local Agent
conversation in `%APPDATA%\Cursor\User\globalStorage\state.vscdb`.

The usable fields are:

| Field | Cursor local field | TokenUsage mapping |
|---|---|---|
| Stable conversation snapshot | `cursorDiskKV.key`, prefix `composerData:` | SHA-256 event identity; the raw ID is not persisted |
| Last observed activity | `conversationCheckpointLastUpdatedAt`, with `lastUpdatedAt` and `createdAt` fallbacks | `OccurredAtUtc` |
| Selected model | `modelConfig.modelName` | normalized `ModelId` and exact provider mapping when known |
| Estimated current context | `promptTokenBreakdown.totalUsedTokens`, with `contextTokensUsed` fallback | input tokens, output/cache/reasoning set to zero |

Cursor names each category inside `promptTokenBreakdown` as `estimatedTokens`.
These values describe the current context retained for a conversation. They are
not cumulative billable request tokens. TokenUsage therefore labels the source
as local, partial, estimated, unpriced, and without quota.

## Why the hook was replaced

Cursor's public hook schema gives every hook a conversation ID, generation ID,
model, client version, workspace roots, account email, and optional transcript
path. The `stop` hook adds only `status` and `loop_count`. It does not expose
input, output, cache, reasoning, or cost counters.

The first prototype expected undocumented `input_tokens` and `output_tokens`
properties. A real local run proved that those fields are absent, so the hook
could never create the TokenUsage spool. The active provider no longer depends
on a hook. `tokenusage cursor install-hook` is retained as a compatibility
no-op, while `uninstall-hook` removes only the old TokenUsage registration.

Primary reference: [Cursor Hooks](https://cursor.com/docs/hooks).

## Privacy boundary

The provider opens only `state.vscdb` in SQLite read-only and query-only mode.
It checks the exact `cursorDiskKV(key, value)` schema and uses SQLite JSON
projections to return only the seven allowlisted scalar metadata fields listed
above plus the stored value length. The application does not select or
materialize the full conversation value.

The provider does not read:

- `ItemTable`, including Cursor access or refresh tokens;
- conversation text, prompts, responses, tool input, file paths, or workspace IDs;
- `conversation-search.db`, transcripts, AI Code Tracking, logs, or credentials;
- private Cursor RPC, dashboard, Stripe, or export endpoints.

Rows, database size, and individual value size are bounded. Reparse points are
rejected. A locked database, unsupported schema, malformed JSON, oversize row,
or exceeded row budget becomes a contained partial or no-data result.

## Reconciliation

`cursorDiskKV` replaces `composerData:<id>` snapshots in place. TokenUsage uses
a stable SHA-256 key over the source kind and raw local key. A later read updates
the same normalized event rather than adding the full context again.

The snapshot time prefers the conversation checkpoint because it advances with
local Agent work. The source participates in the existing 35-day rolling
reconciliation and durable daily rollups.

## Coverage limits

- The total is Cursor's own local context estimate, not an API invoice.
- Repeated requests and discarded context are not reconstructed.
- Output, cache, and reasoning counters are unavailable.
- Tab completions and cloud agents are not covered by this local conversation store.
- Cost, plan balance, session limits, and monthly limits are unavailable locally.
- Auto routing can make the selected model differ from the final provider model.
- The database key and JSON shape are undocumented and version-bound.

## Billing-grade source

The public Cursor Admin API remains the billing-grade source for Teams and
Enterprise. It requires a separate administrator-created key and does not reuse
the desktop login. It remains a future manual-key integration.

References:

- [Cursor Admin API](https://cursor.com/docs/account/teams/admin-api)
- [Cursor Models and Pricing](https://cursor.com/docs/models-and-pricing)
- [Cursor Privacy and Security](https://cursor.com/docs/account/privacy)

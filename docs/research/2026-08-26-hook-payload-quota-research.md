# Hook payload quota research

Date: 2026-08-26

Decision: `block` for plan detection and real remaining usage through hooks

## Question

Can the Stop hooks TokenUsage installs detect the user's plan tier or the
real remaining quota for ZCode, Cursor, or Grok?

## Method

Three sources, checked on 2026-08-26:

1. The official ZCode hooks documentation (payload and environment).
2. The Grok hooks user guide installed with Grok (`~/.grok/docs/user-guide/10-hooks.md`).
3. A live ZCode session environment (variable names only; no values read).

## Findings

### ZCode

- Hook payloads carry session identity, working directory, permission mode,
  and content fields (`prompt`, `last_assistant_message`, tool input). No
  token, cost, credit, plan, or quota field exists on any event.
- The hook process environment carries `ZCODE_APP_VERSION`, `ZCODE_BASE_URL`,
  `ZCODE_ENV`, and `ZAI_*` OAuth client configuration. No plan or quota
  variable exists.
- Local state (`v2\bot-state.v2.json`, `v2\coding-plan-cache.json`,
  `v2\config.json`, `v2\setting.json`) contains no plan tier. The coding plan
  cache stores status strings only.

### Grok

- The documented common payload fields are `hookEventName`, `sessionId`,
  `cwd`, `workspaceRoot`, `timestamp`, `permissionMode`, and `promptId`, plus
  event-specific fields such as tool names and error classifications. No
  usage or quota fields exist on any event, including `Stop`.
- The runner injects `GROK_HOOK_EVENT`, `GROK_HOOK_NAME`, `GROK_SESSION_ID`,
  and `GROK_WORKSPACE_ROOT`. No plan or quota variable exists.

### Cursor

- The repo research already closed this path: the `stop` contract delivers no
  token counters, and remaining plan data requires the editor session
  (`api2.cursor.sh`, Stripe, dashboard exports), which TokenUsage must not
  reuse.

## Conclusion

Hooks are triggers, not data sources. The plan tier and the real remaining
quota never pass through any documented hook channel on any of the three
clients, and none of them stores either value locally.

The ZCode credit-pool estimate with the user-picked tier stays the deepest
in-policy view. Bars for Cursor and Grok have no legitimate denominator:
neither provider publishes plan limits, so no bar can be computed without
inventing numbers.

## Reopen conditions

- A provider publishes plan limits: an estimated bar becomes possible.
- A provider adds usage or quota fields to hook payloads: a reported bar
  becomes possible.
- A provider publishes an authorized quota API: reported remaining replaces
  every estimate.

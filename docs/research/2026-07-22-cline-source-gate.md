# Cline source gate

Cutoff date: 2026-07-22

Decision: `block`

## Question

Can TokenUsage integrate Cline on Windows to show quota, tokens, or
spend without reusing login, credentials, task content, or interface
automation?

## Answer

There is still no approved adapter. Cline publishes an Enterprise API that
lists reads for balance, usage history, organization metrics, and aggregated
usage. It is a manual remote candidate: a person can create their own API
key and give it to TokenUsage explicitly. The app must never take the Cline
session token or search for the key in existing variables or files.

The documentation does not publish the schema, filters, pagination, units,
period semantics, or error responses of the balance and usage endpoints.
The OpenAPI link announced by the documentation returned HTTP 404 on
2026-07-22. It also does not document a read-only key: the same API key
serves the Enterprise API and the inference API, which also exposes account
and billing operations. Without a contract and without a permission test,
TokenUsage cannot build a safe parser or promise correct quota or spend.

Local tasks stay out. Cline documents that each task keeps the full
conversation, code changes, commands, decisions, tokens, and costs. The
local conversation file contains tool inputs and outputs. Reading it would
break the product's privacy limit.

## Identity and support

- Proposed ID for a future descriptor: `cline`.
- Visible name: `Cline`.
- Observed client: CLI `cline`, npm package `cline`.
- Observed version: `3.0.46`.
- Windows: documented Node.js CLI; no provider or account was evaluated.
- Billing modalities that must stay separate:
  - `Cline (usage-billing)`: pay-as-you-go Cline credits;
  - `ClinePass`: its own subscription and quota;
  - BYOK: billing of the model provider, not of Cline;
  - local models: no API cost.

## Primary sources

Consulted on 2026-07-22:

| Source | Supporting fact |
|---|---|
| [Enterprise API](https://docs.cline.bot/enterprise-solutions/api-reference) | Base `api.cline.bot`, Bearer auth, and GET endpoints for profile, balance, usage, metrics, and organization usage. It also lists mutable and billing operations. |
| [API authentication](https://docs.cline.bot/api/authentication) | Manual API key and account token managed by IDE/CLI; the key can also be managed by API. |
| [Cline usage-billing](https://docs.cline.bot/getting-started/cline-provider) | Separates Cline credits, ClinePass, the dashboard, and View Usage. |
| [Provider authorization](https://docs.cline.bot/getting-started/authorizing-with-cline) | Separates usage-billing, ClinePass, BYOK, and local models. |
| [Tasks](https://docs.cline.bot/core-workflows/task-management) | Each task keeps conversation, changes, commands, decisions, tokens, costs, and time. The shown cost can differ from the final BYOK invoice. |
| [Prompt Storage](https://docs.cline.bot/enterprise-solutions/monitoring/prompt-storage) | Pins `~/.cline/data/tasks/<taskId>/api_conversation_history.json` and confirms that conversations contain tool inputs and outputs. |
| [CLI reference](https://docs.cline.bot/cli/cli-reference) | Pins `--data-dir`, `history`, `export`, configuration paths, and `providers.json`; it does not publish a subcommand for quota, balance, spend, or metric export. |
| [OpenTelemetry](https://docs.cline.bot/enterprise-solutions/monitoring/opentelemetry) | Optional export that the organization configures in its dashboard and collector; TokenUsage does not install, configure, or read it. |
| [Announced OpenAPI](https://docs.cline.bot/api-reference/openapi.json) | The link returned HTTP 404 in the direct check of this research. |

## Isolated Windows test

The machine had no global `cline`. The work created
`.snapshots/cline-t54-smoke`, isolated `HOME`, `USERPROFILE`, `APPDATA`,
`LOCALAPPDATA`, and `CLINE_DATA_DIR`, and installed `cline@latest` without
lifecycle scripts. It did not use login, API key, `cline auth`, user data,
or a task.

The install exceeded the command's 120-second limit, but the local package
and its executable were available. `cline --version --data-dir ...`
returned `3.0.46`. `cline --help --data-dir ...` confirmed `--data-dir`,
`--config`, `--key`, `auth`, and `history`; it did not show a command for
quota, balance, spend, or metric export. `cline history --help --data-dir ...`
confirmed that the CLI can list, delete, update, and export sessions. That
surface is not a suitable source because sessions contain task content.

The test validates only the help surface and isolation. It does not validate
a complete install, an account, permissions, remote data, or API schemas.

## Source classification

| Data | Observed source | TokenUsage status | Decision |
|---|---|---|---|
| Remaining Cline credits | `GET /api/v1/users/{id}/balance` and organization balance | Documented endpoint without schema, units, scope, or authorized smoke | Pending |
| Remote Cline usage | `GET /api/v1/users/{id}/usages` and organization usage | Documented endpoint without schema, filters, pagination, or authorized smoke | Pending |
| ClinePass quota | Settings and View Usage | No documented public endpoint | Blocked |
| Task tokens and cost | Local tasks and UI | Include conversation, files, commands, and estimated cost | Blocked |
| BYOK | Cost shown per task | Belongs to the underlying provider and can differ from its invoice | Out of this provider |
| Local data | `~/.cline/data`, tasks, sessions, logs, and `providers.json` | Mix history, task content, settings, and keys | Blocked |
| OpenTelemetry | Enterprise configuration and external collector | Requires organization infrastructure and activation | Out of scope |

A future Cline card could represent only the approved remote response and
its active account. It must not add BYOK costs as Cline spend, infer
ClinePass quota, or combine personal and organization accounts.

## Privacy and security limit

TokenUsage can detect `cline --version` in a future diagnostic. It must not
open, copy, or use:

- `%USERPROFILE%\.cline`, `CLINE_DATA_DIR`, tasks, SQLite sessions, logs,
  exports, history, checkpoints, prompt storage, or team directories;
- `api_conversation_history.json`, prompts, responses, files, code,
  commands, tool results, plans, rules, skills, MCP, or task state;
- `providers.json`, `CLINE_API_KEY` variables, account tokens, cookies,
  OAuth, Credential Manager, or `cline auth` data;
- `history`, `export`, the TUI, dashboard, Settings, View Usage, observed
  traffic, inferred endpoints, or interface automation;
- someone else's OpenTelemetry configuration, collector, logs, or metrics.

If the remote client is approved, the person must create their own API key
and enter it in the app. TokenUsage will store it only in Windows Credential
Locker, send it only to `https://api.cline.bot`, limit its calls to the
approved `GET`s, and allow deleting it together with its cache. The risk
stays open: current documentation does not offer a monitor key with
read-only permission.

## Product decision

- Do not create a local scanner, task parser, remote client, or Cline card
  in this phase.
- Do not read or reuse the Cline login, its account token, or an API key
  found outside TokenUsage.
- Do not ask for a Cline key while the response contract and an explicit
  test by the account owner are missing.
- Keep Ticket 55 in `needs-info`.
- Reopen the adapter only when all of these points are met:
  1. a versioned public schema, or a sanitized fixture obtained with an
     authorized test account, for minimum profile, balance, and usage;
  2. documented semantics for Cline credits, periods, currency, units,
     pagination, errors, and organization accounts;
  3. permission confirmation: a published read-only key, or explicit
     approval of the risk of a broad key created for TokenUsage;
  4. Windows smoke with a disposable key, GET only, revocation, and
     deletion of credential and cache;
  5. fixtures, and states for missing, expired, no-permission, throttle,
     and schema-change accounts before the public build is turned on.

## Independent review

Grok Build reviewed the gate documents in an isolated snapshot with `Read`
and `Grep` only and issued `accept`. It confirmed that Cline credits,
ClinePass, BYOK, and local metrics keep separate limits, and that the text
does not certify live API behavior. The local review incorporated a
consistent date and matrix status. This review does not replace the primary
sources or an authorized smoke.

## Remaining uncertainty

Cline can restore the OpenAPI, publish monitor schemas and permissions, or
document the balance and usage endpoints better. Before announcing support,
repeat the gate with a current CLI version and an authorized test account.

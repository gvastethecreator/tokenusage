# Provider support and data sources

TokenUsage separates observed local usage, remote account readings, and quota.
A catalog entry does not mean a reader is available. Missing data stays missing;
the app does not create sample activity for prepared or blocked providers.

The [provider catalog](../src/TokenUsage.Providers/Catalog/ProviderModuleCatalog.cs)
defines the current stages:

| Stage | Count | Behavior |
|---|---:|---|
| Active | 12 | A bounded reader collects approved local usage. |
| Opt-in | 2 | A remote reader runs after you save your own API key. |
| Prepared | 33 | The catalog describes the provider, but no reader runs. Saving a supported credential does not enable a reader. |
| Policy blocked | 9 | The available source does not meet the repository's source or privacy requirements. |

## Local readers

| Provider | Source | Cost and coverage | Quota |
|---|---|---|---|
| Codex | Official local `app-server` and bounded session logs | Reported or estimated; account-day totals remain separate | Official account limits for a suitable ChatGPT account |
| Claude | Claude Code project logs | Reported or estimated; excludes sessions that were not saved | Opt-in documented status line reading |
| Cursor | Read-only numeric projection from `state.vscdb` | Per-turn counters when present; context estimate otherwise | Unavailable through this local source |
| GitHub Copilot | VS Code chat session snapshots and operation logs | Partial tokens and recorded credits before plan allowances | Unavailable |
| Grok Build | Unified log and session usage snapshots | Reported or estimated; source precedence prevents double counting | Blocked without an approved interface |
| OpenCode | Read-only `opencode.db` and legacy JSON storage | Reported cost when present, estimated otherwise | No common source across providers and plans |
| ZCode | Read-only `model_usage` table | Per-request counters and estimated API cost | Blocked without an approved interface |
| Antigravity | Read-only generation metadata in local databases | Experimental, partial tokens and estimated cost | Blocked under the current source policy |
| Amp | `ledger.jsonl` | Partial tokens; credits are not USD | No stable public source |
| Mux | `session-usage.json` | Tokens and reported aggregate cost by model | No common source |
| Goose | Read-only numeric query of `sessions.db` | Cumulative session tokens; estimated cost when priced | No common source |
| Hermes | `state.db` in `.hermes` or a profile | Cumulative session tokens and reported or estimated cost | No common source |

Local coverage excludes activity that the source never recorded. Unknown models
stay unpriced. API-rate estimates are not subscription invoices. See
[pricing evidence](PRICING.md) and [storage and retention](USAGE-STORAGE.md).

## Codex

The official local `app-server` handles login and renewal. TokenUsage uses
`account/read` with `refreshToken: false`, `account/rateLimits/read`, and
`account/usage/read`. It keeps only the admitted account type, plan, auth status,
limits, and numeric usage. It does not retain email, account identifiers,
tokens, raw responses, or `codexHome`, and does not open `auth.json`.

Optional local detail comes from `CODEX_HOME/sessions` and `archived_sessions`.
The `state_5.sqlite` index is merged with those folders so index lag does not
hide a new session. Multiple accounts need separate `CODEX_HOME` instances and
processes. A compatible Codex binary is required. API-key, Bedrock, local, or
unknown auth modes do not imply ChatGPT subscription quota. TokenUsage never
consumes a reset credit.

Limits can include primary and secondary windows, named additional pools,
resets, plan, and available spending controls. The `base_model_inference` pool
is shown as `GPT reserve`. Account-day totals have no proved matching local
scope or timezone, so they are not added to local model totals.

Session, parent, and project links require separate consent and use opaque
identities. Enabling consent does not backfill existing observations; Settings
can admit a retained date range. Revoke removes links while keeping numeric
usage. See [attribution rules](USAGE-STORAGE.md#session-attribution).

Codex `token_count` is not a proved finalized request. Request distributions
remain unavailable. Session outliers can still use at least 30 comparable
attributed sessions when the relevant consent is on.

## Claude

The local reader uses `%USERPROFILE%\.claude\projects`, or the equivalent under
`CLAUDE_CONFIG_DIR`. It projects tokens, model, date, and recorded cost when
present. It omits prompt and response content. Sessions that were not saved
are outside coverage.

Quota comes only from the documented Claude Code status line `rate_limits`
object. It is opt-in through Settings or `tokenusage claude install-statusline`.
The wrapper stores percentages, reset times, and observation time, and forwards
stdin to the user's previous status line command. The previous command is kept
in `tokenusage-statusline.json` next to `settings.json` and has a five-second
execution limit. Without one, the wrapper prints the available quota windows.
Uninstall restores that command and deletes the stored reading.

The reading advances only while Claude Code runs with the wrapper enabled.
TokenUsage checks the local reading every 30 seconds while the app runs.
An omitted five-hour window stays absent until Claude Code sends it; it is not
shown as zero use or a full allowance.
Available windows depend on the account and can include five-hour, weekly,
and gateway spending limits. Readings older than 30 minutes are marked stale;
windows past their reset time are dropped. TokenUsage does not read
`.credentials.json` or call the private subscription usage endpoint.

Session, parent, project, and operational attribution stay blocked for Claude.
`message.id` is not a session ID, and `isSidechain` is not a proved parent edge.

## Cursor

The reader opens `state.vscdb` read-only and selects a fixed scalar projection
from `cursorDiskKV`: `composerData:` and `bubbleId:` metadata only. It does not
return full JSON values, prompts, responses, paths, commands, transcript,
credentials, or unhashed IDs. Other tables, search databases, AI Code Tracking,
private dashboard routes, RPCs, cookies, and credential stores are excluded.

Per-turn counters take precedence over conversation context estimates. The
fallback `estimatedTokens` value describes current context, not cumulative
billed usage. Auto and unknown models remain unpriced. Host-specific rates are
used only when the pricing catalog identifies them. Local coverage excludes
Tab, cloud agents, account quota, and billing. Teams and Enterprise billing
still require a separate future Admin API connection.

Optional session links require consent. Their opaque identities distinguish
the same composer fragment in different databases. Parent and workspace
inference remain blocked; a session link does not turn a context estimate into
a request. Already-scanned rows require explicit backfill to gain links.
Revoking Cursor links keeps Codex links and numeric totals.

## GitHub Copilot

The active local reader supports VS Code, Insiders, and VSCodium. It reads
`User\workspaceStorage\<hash>\chatSessions\` and
`User\globalStorage\emptyWindowChatSessions\`, including `.json` snapshots
and `.jsonl` operation logs. Linked directories are excluded. It projects only
the approved request IDs, times, model IDs, counters, and credits; message text,
tool data, workspace descriptors, extension storage, and logs are not admitted.
Raw files are never copied into usage storage.

Output tokens and credits are measured. Input is a lower bound unless the
request supplies per-model totals. One recorded AI credit is valued at $0.01
before the plan allowance; that is not the net account charge. Remaining quota
is unavailable. The [VS Code source gate](source-gates/COPILOT-VSCODE.md)
defines the allowlist, format limits, and privacy boundary.

The Billing REST client is a separate manual connection. Its contract uses a
user-supplied fine-grained token in Credential Locker, `Plan: read` for a paid
personal account, or `Administration: read` and administrator access for an
organization. It reports used credits, discounts, and net charges. Organization
results are entity totals. Free, Student, legacy annual, and ordinary-member
cases are outside the validated subset. The client has no effective allocation
or balance and does not invent remaining quota from plan tables. Public billing
activation still requires an authorized account smoke check and credential
deletion check.

Copilot CLI telemetry remains blocked: its exporter mixes metrics with other
signals and identity. It requires a documented metrics-only export that filters
content and identity before writing. See the separate
[Copilot CLI gate](source-gates/COPILOT-CLI.md). TokenUsage never borrows editor
or GitHub CLI authentication, `hosts.yml`, or private Copilot endpoints.

## Other local sources

**Grok Build** reads `GROK_HOME/logs/unified.jsonl` first and session
`updates.jsonl` snapshots when needed. A summary is optional if the snapshot
already provides model and time. Reported cost takes precedence over estimates.
Grok Bot is a separate prepared provider: Build logs and xAI API credits are
not Bot usage or quota. Its desktop profile and cloud-computer session are
outside the approved sources.

**OpenCode** uses `%USERPROFILE%\.local\share\opencode`, accepts SQLite and
legacy JSON, and excludes `auth.json`. It deduplicates across formats without
copying the database. Native Windows is the supported local path; each WSL
distro needs separate detection and consent. `opencode stats` can serve as a
comparison reference, but its human-readable output is not the adapter format.

**ZCode** reads only numeric usage columns from
`%USERPROFILE%\.zcode\cli\db\db.sqlite`. Input includes read-cache tokens;
reasoning is part of output. Event keys hash the row ID. Missing required
columns produce `UnsupportedSchema`. Credentials, message/session tables,
history, and rollout files are excluded. Reconciliation covers 35 days. Plan
credits do not become invoice cost or an estimate of remaining quota.

**Antigravity** accepts bounded generation metadata from local `.db` files in
the `antigravity`, `antigravity-cli`, and `antigravity-ide` roots. Unknown rows
stay partial and unknown models stay unpriced. Encrypted `.pb` files, transcript,
decryption, helper daemons, tokens, CSRF values, private RPCs, and automated TUI
access are excluded. Local tokens do not establish quota or credits.

**Amp** uses the ledger without opening threads. **Mux** uses aggregate
`session-usage.json` without transcripts. **Goose** and **Hermes** select numeric
session totals from their databases. An empty `.hermes` directory, or one owned
by another application, does not establish an installed Hermes source.

When background collection is on, detected Claude, Cursor, Grok, and ZCode
clients can use task-completion hooks. The hook discards its payload and only
requests a refresh of TokenUsage's own data. The CLI exposes
`tokenusage <provider> install-hook|status|uninstall-hook`; uninstall preserves
the rest of the client's configuration. Hooks do not supply token counters or
remaining quota.

## Opt-in remote connections

Keys belong to the user and are stored in Windows Credential Locker. Removing
a key disables its connection. These readings remain separate from observed
local totals to avoid counting the same activity twice.

### OpenRouter

The active path calls only `GET /api/v1/key` with the saved key. It reports that
key's limit, remaining budget, and spending today, this week, and this month
using UTC boundaries. It does not report tokens. Account credit and activity
endpoints that require a management key are outside this path.

Ordinary refresh reuses a reading for ten minutes. Throttling preserves the
last available reading and honors `Retry-After`. Missing or rejected credentials
require setup; unsupported responses do not become zero usage.

### Vercel AI Gateway

The connection accepts an API key and optional key ID. Requests use a fixed
gateway origin without redirects. The usage report covers the saved key's last
30 UTC days and requires an eligible Pro or Enterprise plan; report requests
can incur provider charges. Team credits are a separate balance. Key quota is
best effort: an unavailable quota endpoint does not imply an unlimited budget.

Ordinary refresh reuses a reading for at least one hour. Forced refresh can
request another paid report. Real-account validation remains separate from
fixture and local test evidence.

## Prepared and blocked providers

Prepared providers have no active reader. The catalog is the source of truth
for their names, capabilities, aliases, and accepted credential types. Saving
a credential for a prepared provider does not prove that its API is integrated.
Devin's retained experimental client, for example, remains outside active
composition until its organization-scoped permission and live-account checks
pass. It reports ACUs, not dollars or remaining quota.

The nine blocked catalog entries are Perplexity, Z.ai, Gemini CLI, Kilo Code,
Zed, Kimi CLI, Kimi Code, Cline, and Cline CLI. Their source gate must change
before a reader can be enabled. Quota can also be blocked for an active local
provider; a safe token source does not grant permission to read credentials
or call private account endpoints.

Gemini CLI's current chat and telemetry files mix counters with content or
identity. Its interactive `/stats` command is not a passive history export.
The [Gemini CLI gate](source-gates/GEMINI-CLI.md) requires a documented
metrics-only source. Do not infer Gemini API cost or Google quota from local
CLI observations.

## Measurement limits

Reports describe observed usage, not model quality or all activity on the PC.
Quota observations are sampled levels, not a complete consumption ledger.
Current sources do not prove model-to-quota-pool attribution, so normalized
quota ratios remain unavailable. Reset-cycle comparisons use Codex and the
opt-in Claude reading, expose gaps, and compare a shared elapsed duration.

Exact-time charts exclude daily or unknown timing. Optional operational facts
and attribution have independent consent, bounded backfill, and revocation
rules. Raw retention, immutable snapshots, source authority, derived report
methods, and recovery limits are documented in
[usage storage](USAGE-STORAGE.md). A new collection cannot reconstruct quota
history that was never observed.

## Publication gate

Every integration starts with a provider issue and follows the
[contributor testing guide](CONTRIBUTOR-TESTING.md), even when maintainers cannot
access the provider. Before publication, provide:

- A documented source, precedence rules, and terms, policy, and brand review.
- A Windows check of default paths and environment overrides, plus a check
  inside the signed MSIX and against a real supported provider version.
- Sanitized fixtures, a settled response contract, size limits, timeout, and
  cancellation handling.
- Missing, expired, unsuitable, throttled, and changed-schema cases; defined
  account switching and safe credential rotation.
- Proof that only admitted fields reach storage. Readers of mixed files must
  skip prompt, response, task, and command content; credential files remain
  excluded.
- Logs and cache without secrets, and a differential total against a reference
  on the same fixture.
- UI text that states source, coverage, and limits, separates reported and
  estimated cost, and keeps unpriced models visible.

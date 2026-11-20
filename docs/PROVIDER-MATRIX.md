# Provider support and data sources

TokenUsage separates observed local usage, account readings, and quota.
Missing data stays missing; the app does not create sample activity.

The [provider catalog](../src/TokenUsage.Providers/Catalog/ProviderModuleCatalog.cs)
lists the twelve tools TokenUsage reads. Each one has a bounded local reader.
A provider without a working reader is not listed.

## Compatibility review

On 2026-11-14, local version commands reported Codex CLI `0.167.0`, Claude Code
`2.1.325`, ZCode app CLI `3.11.2-25` / runtime `0.16.5`, and VS Code `1.146.0`
on Windows x64. The installed OpenCode launcher last failed on November 9 because
its Bun package mapping was broken; that failure was not retried.
These checks did not query accounts or provider data.

The official Codex account-limit schema is identical in releases
[`0.165.0`](https://github.com/openai/codex/blob/rust-v0.165.0/codex-rs/app-server-protocol/schema/json/v2/GetAccountRateLimitsResponse.json)
and [`0.169.0`](https://github.com/openai/codex/blob/rust-v0.169.0/codex-rs/app-server-protocol/schema/json/v2/GetAccountRateLimitsResponse.json).
The account-usage schema and local `TokenCountEvent` structures also remain
unchanged from `0.167.2`, and the state index remains `state_5.sqlite`.
The [Claude status line documentation](https://code.claude.com/docs/en/statusline)
still specifies independently absent `rate_limits.five_hour` and
`rate_limits.seven_day` windows. The
[VS Code serializer at the installed commit](https://github.com/microsoft/vscode/blob/69047191fc427f2f92828279aae1d90d8eeda7ab/src/vs/workbench/contrib/chat/common/model/chatSessionOperationLog.ts)
matches the `1.146.0` source checked on November 12. Its change to when it writes
`lastCallCompletionTokens` affects a field this reader does not use; the admitted
token, credit, and per-model fields remain unchanged. These are source and documentation
checks, not real-total comparisons or installed-package proof.
No provider changed support stage during this review. Private-format limits for
Cursor, ZCode, Antigravity, and Amp remain open.
Codex windows use their reported duration. Seven-day windows keep the Weekly
label; other windows of at least one day show their length in days.

On 2026-11-17, the release review covered
[Claude Code 2.1.326](https://github.com/anthropics/claude-code/releases/tag/v2.1.326),
[Goose 1.59.2](https://github.com/aaif-goose/goose/compare/v1.59.1...v1.59.2),
[OpenCode 1.19.6](https://github.com/anomalyco/opencode/compare/v1.19.5...v1.19.6),
and [Hermes 0.23.2](https://github.com/NousResearch/hermes-agent/compare/v0.23.1...v0.23.2).
Claude's release notes describe session recovery and usage UI fixes without
announcing changes to the fields TokenUsage reads. The Goose session manager
matches 1.59.1 byte for byte. OpenCode 1.19.6 limits images sent to the model
and offers a separate upgrade to 2.0. The 2.0 storage format remains unverified.
Hermes changes database write coordination and adds prompt cache metadata.
This release review did not use live accounts or compare real usage totals.

On 2026-11-18, the published [Codex 0.170.0](https://github.com/openai/codex/releases/tag/rust-v0.170.0)
account, usage and rate-limit schemas matched 0.169.0. The
[Claude Code 2.1.327 notes](https://github.com/anthropics/claude-code/releases/tag/v2.1.327)
add billing-group attribution without documenting a change to the numeric fields
TokenUsage reads. [Goose 1.60.1](https://github.com/aaif-goose/goose/compare/v1.59.2...v1.60.1)
widens upstream token counters to 64 bits. The SQLite reader already reads
64-bit integers. The chat serializer and chat model in
[VS Code 1.147.0](https://code.visualstudio.com/updates/v1_147) match 1.146.0.
These checks used published source and release notes.

On 2026-11-19, the [Claude Code 2.1.328 notes](https://github.com/anthropics/claude-code/releases/tag/v2.1.328)
describe token count corrections in the spinner and subagent display. The
notes do not describe a change to the transcript fields TokenUsage reads.
[Xum 0.32.3](https://github.com/coder/xum/releases/tag/v0.32.3) adds GPT-6.2 model
routes and prices. The [session usage writer](https://github.com/coder/xum/blob/v0.32.3/src/node/services/sessionUsageService.ts)
matches 0.32.2, and TokenUsage uses the aggregate cost reported by Xum.
This review did not compare live accounts or real usage totals.

[ZCode 3.18.2](https://github.com/zai-org/ZCode/releases/tag/v3.18.2) removes
its 30-day automatic usage cleanup. The
[`model_usage` table definition](https://github.com/zai-org/ZCode/blob/v3.18.2/apps/zcode-cli/packages/adapters/src/storage/session-store/schema.baseline.sql)
matches 3.17.2. TokenUsage still bounds the date range, rows, and database size.
[Antigravity](https://antigravity.google/blog/gemini-4-neon-in-google-antigravity/)
can pair Neon and Hydrogen in one session. TokenUsage prices the model named
in each readable generation. The new Orchestrator mode has no live validation.

On 2026-11-20, [Codex 0.171.0](https://github.com/openai/codex/releases/tag/rust-v0.171.0)
added an optional reason to the account notification. The numeric account,
rate-limit, and token count contracts reviewed match 0.170.0. The
[rollout policy](https://github.com/openai/codex/blob/rust-v0.171.0/codex-rs/rollout/src/policy.rs)
keeps thread settings. TokenUsage uses a nonstandard setting from the same
thread to withhold a Standard estimate when the turn tier is missing.

[Hermes 0.23.3](https://github.com/NousResearch/hermes-agent/releases/tag/v0.23.3)
moves SQLite code behind a persistence interface. The
[`sessions` and `session_model_usage` definitions](https://github.com/NousResearch/hermes-agent/blob/v0.23.3/persistence/sqlite/_schema_ddl.py)
match 0.23.2. The Goose 1.60.2 and Xum 0.32.4 usage writers match their previous
releases. Claude Code 2.1.329 describes corrections to the usage credits
display. These checks used published source and release notes.

## Local readers

| Provider | Source | Cost and coverage | Quota |
|---|---|---|---|
| Codex | Official local `app-server` and bounded session logs | Reported or estimated; account-day totals remain separate | Official account limits for a suitable ChatGPT account |
| Claude | Claude Code project logs | Reported or estimated; excludes sessions that were not saved | Opt-in account usage; private endpoint used by Claude Code |
| Cursor | Read-only numeric projection from `state.vscdb` | Per-turn counters when present; context estimate otherwise | Unavailable through this local source |
| GitHub Copilot | VS Code chat session snapshots and operation logs | Partial tokens and recorded credits before plan allowances | Unavailable |
| Grok Build | Unified log and session usage snapshots | Reported or estimated; source precedence prevents double counting | Blocked without an approved interface |
| OpenCode | Read-only `opencode.db` and legacy JSON storage | Reported cost when present, estimated otherwise | No common source across providers and plans |
| ZCode | Read-only `model_usage` table | Per-request counters and estimated API cost | Blocked without an approved interface |
| Antigravity | Read-only generation metadata in local databases | Experimental, partial tokens and estimated cost | Blocked under the current source policy |
| Amp | `ledger.jsonl` | Partial tokens; credits are not USD | No stable public source |
| Mux / Xum | `session-usage.json` | Tokens and reported aggregate cost by model | No common source |
| Goose | Read-only numeric query of `sessions.db` | Cumulative session tokens; estimated cost when priced | No common source |
| Hermes | `state.db` in `.hermes` or a profile | Per-model usage plus remaining session totals; reported or estimated cost | No common source |

Local coverage excludes activity that the source never recorded. Unknown models
stay unpriced. API-rate estimates are not subscription invoices. See
[pricing evidence](PRICING.md) and [storage and retention](USAGE-STORAGE.md).

## Codex

Settings → Providers → Accounts can show quotas for selected code-swap
profiles. Use **Find accounts**, select the profiles, then **Save selection**.
New profiles remain unselected. Each account shows its available windows, plan,
reset times, active state, and reading status. Percentages are never added across
accounts. Failed updates retain the last reading and its observation time.

Discovery requires the installed `xswap list --json` schema version 1 and respects
`XSWAP_HOME`. TokenUsage verifies the effective `home`, account identity, and email
before accepting quota data. Ambiguous or changed identities block that reading.
Discovery is checked again after the query. Codex `0.169.0` adds an optional
ChatGPT `userId` to [account/read](https://github.com/openai/codex/blob/rust-v0.169.0/codex-rs/app-server-protocol/schema/json/v2/GetAccountResponse.json); TokenUsage does not admit that identifier or
use it to bind a profile. Code-swap remains responsible for login and switching.

Local tokens, costs, and conversations keep their existing accounting. Extra
profiles add quota observations only. The CLI JSON contract still has one Codex
entry for the active account. Removing a selection stops its queries and retains
its data. After first activation, the previous global Codex quota history remains
available as history without an identified account; it does not resume writing
if code-swap is missing or the selection becomes empty.

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

## Claude

The local reader uses `%USERPROFILE%\.claude\projects`, or the equivalent under
`CLAUDE_CONFIG_DIR`. It projects tokens, model, message timestamp, and recorded
cost when present. It omits prompt and response content. Sessions that were not saved
are outside coverage. Large files are streamed in batches of up to 64 MiB per
file and 256 MiB per iteration, rotating between files. Checkpoints retain
hashed identities, offsets, fingerprints, and numeric streaming merge state;
they advance only after database persistence. Incomplete trailing lines resume
when writing continues. File replacement or truncation resets only that cursor.

Settings → Advanced offers a recovery preview, progress, and cancellation.
Recovery scans all available files, including those outside the normal 35-day
window, and respects 400-day detail retention, retired totals, and tombstones.
A repeat with unchanged sources adds no consumption. Excluded records are reported
by reason. Existing recorded prices are preserved; an ambiguous price change does
not silently revalue stored usage.

Quota is opt-in through Settings.
The versioned TokenUsage consent is independent of `statusLine`; a Ghostex or
other wrapper can remain in place. Only a direct, recognizable TokenUsage
registration migrates automatically. Turning limits off stops OAuth reads and
status line persistence while preserving forwarding. The legacy
`tokenusage claude install-statusline|uninstall-statusline` commands remain available.
With that opt-in, TokenUsage reads the current Claude Code access token from
`.credentials.json` in memory and calls `https://api.anthropic.com/api/oauth/usage`.
This is a private endpoint used by Claude Code, not a documented public API.
The response's named `limits` array supplies consumed percentages and reset
times for the five-hour session, weekly all-model allowance, and model or
surface scopes such as Fable. Inactive limits still appear, including 0% use.
Opaque legacy fields and status line percentages do not replace those limits.

The Windows app and `tokenusage limits claude` share the provider snapshot
cache. Normal background refresh runs every five minutes; manual refresh forces
a request. Failed refreshes keep the last reading with a warning. Readings older
than ten minutes are marked stale. Claude Code owns sign-in renewal; expired
credentials require running `/usage` in Claude Code, then refreshing TokenUsage.
If Claude cannot renew its session, run `/login` in Claude Code. A local
`claude auth status` result alone does not prove that the access token is valid.
TokenUsage shows the refresh failure reason and distinguishes throttling from
sign-in failures. TokenUsage does not copy or write
tokens, use refresh tokens, or send model prompts. Each successful refresh
replaces the reading with the currently signed-in Claude account. Multiple
Claude accounts are not tracked separately.

The existing status line command still forwards stdin to the user's previous
command, stored in `tokenusage-statusline.json` next to `settings.json`, with a
five-second execution limit and an inherited recursion guard. Nonzero exit codes
remain failures. The numeric sidecar requires the same consent as OAuth reads.
Turning off limits in Settings clears quota caches and stops account requests
without changing the command. `CLAUDE_CONFIG_DIR` applies to both settings
and the credential file. Token reads and responses are limited to 64 KiB, and
requests have a 15-second timeout with redirects disabled.

Settings → Providers → Accounts can also show quotas for selected claude-swap
profiles. Discovery runs `cswap list --json` (schema 1) from `PATH` or
`%USERPROFILE%\.local\bin`. TokenUsage reads the usage that claude-swap already
cached for each profile. It sends no request of its own and does not open profile
credentials. Missing usage falls back to claude-swap's last good reading and its
observation time. Identity is the email plus organization ID; a changed identity
blocks that reading. Expired or missing logins ask the user to sign in again
through claude-swap. Profiles share `~/.claude/projects`, so local tokens and
costs stay on the single Claude entry. While Claude accounts are selected, the
dashboard shows the account rows instead of the global Claude quota.

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

Copilot CLI telemetry remains blocked: its exporter mixes metrics with other
signals and identity. It requires a documented metrics-only export that filters
content and identity before writing. TokenUsage never borrows editor
or GitHub CLI authentication, `hosts.yml`, or private Copilot endpoints.

## Other local sources

**Grok Build** reads `GROK_HOME/logs/unified.jsonl` first and session
`updates.jsonl` snapshots when needed. A summary is optional if the snapshot
already provides model and time. Reported cost takes precedence over estimates.
Build logs and xAI API credits are not Grok Bot usage or quota.

**OpenCode** uses `%USERPROFILE%\.local\share\opencode`, accepts SQLite and
legacy JSON, and excludes `auth.json`. It deduplicates across formats without
copying the database. Native Windows is the supported local path; each WSL
distro needs separate detection and consent. `opencode stats` can serve as a
comparison reference, but its human-readable output is not the adapter format.
Published version `1.19.5` stores the session model as structured JSON. Available
message detail supplies the model, provider, and date instead of assigning all
session usage to the last activity day. The bounded full read must complete
before replacing saved session aggregates; a partial read retains the last
accepted snapshot.

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

**Amp** uses the ledger without opening threads. **Mux / Xum** uses aggregate
`session-usage.json` without transcripts. Root discovery follows version `0.32.1`,
including `.xum`, `XUM_ROOT`, `MUX_ROOT`, and the documented legacy-root selection.
The same root contract is present in `0.32.2`. Its
[usage service](https://github.com/coder/xum/blob/v0.32.2/src/node/services/sessionUsageService.ts)
merges removed child usage into the parent's `byModel` and records `rolledUpFrom`.
TokenUsage rejects a parent snapshot with non-empty or malformed rollup metadata
as partial because it can overlap retained child usage without a reliable
per-model split. Previously accepted parent and child observations remain stored.
Valid observations from other sessions continue to update by stable event identity
through the existing windowed snapshot contract, including during a partial scan.
New usage mixed into that parent is not imported, and historical overcounts
already stored are not repaired automatically. Empty or absent rollup metadata
continues through the normal reader.
**Goose** reads session totals with their recorded cache breakdown.
**Hermes** reads model routes, including auxiliary usage, and reconciles remaining
session totals using the upstream accounting rule. Remaining usage has partial
attribution. A zero residual can remain stored to replace a previous aggregate;
it is not additional token usage. Normal empty sessions do not block either
provider's valid observations; malformed rows still make the read partial.
An empty `.hermes` directory, or one owned by another application, does not
establish an installed Hermes source.

The November 7 checks used published OpenCode `1.19.5`, Xum `0.32.1`,
Goose `1.57.0`, and Hermes `0.23.0` schemas with synthetic Windows fixtures.
The November 9 release-metadata check still identified the same OpenCode, Xum,
and Hermes versions. Goose `1.58.0` was also reviewed: its
[session storage source](https://github.com/aaif-goose/goose/blob/v1.58.0/crates/goose/src/session/session_manager.rs)
is byte-identical to `1.57.0`. This does not establish live Goose compatibility.
The November 12 review checked Goose `1.59.0`: schema 18 adds session tags and
changes connection initialization, while the numeric session fields read here
remain unchanged. The November 14 check found the
[`1.59.1` session storage source](https://github.com/aaif-goose/goose/blob/v1.59.1/crates/goose/src/session/session_manager.rs)
byte-identical to `1.59.0`. Hermes `0.23.1` retains the same
[usage-storage implementation](https://github.com/NousResearch/hermes-agent/blob/v0.23.1/hermes_state_usage.py)
as `0.23.0`; OpenCode's release metadata still identifies `1.19.5`.
The Goose reader now marks malformed, non-empty model JSON as a partial scan
while retaining valid session observations, instead of silently omitting the
malformed row from a complete result.
Grok's current official source defines cache creation inside input and reasoning
inside output; the reader separates those buckets before adding totals.
These checks do not establish live-account totals or signed-package compatibility.
Cursor, ZCode, Antigravity, and Amp retain their documented private-format limits.

When background collection is on, detected Claude, Cursor, Grok, and ZCode
clients can use task-completion hooks. The hook discards its payload and only
requests a refresh of TokenUsage's own data. The app installs the hooks when it
opens and removes them when background collection is off; removal preserves the
rest of the client's configuration. Hooks do not supply token counters or
remaining quota.

## Measurement limits

Reports describe observed usage, not model quality or all activity on the PC.
Quota observations are sampled levels, not a complete consumption ledger.
Reset cycles follow the provider schedule. Tokens per quota point divide this
PC's recorded tokens by the points used, so use on other devices or the web lowers
the ratio. Account quotas show no ratio because local tokens cannot be assigned to
one account. Cycle totals exclude daily or unknown timing. Raw retention, source
authority, reset cycles, and recovery limits are documented in
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

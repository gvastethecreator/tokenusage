# Devin source gate

Cutoff date: 2026-07-21

Decision: `implement-experimental-subset`

## Answer

Devin publishes a v3 API for consumption. TokenUsage can show daily and total ACUs for an organization through a service user created for that organization. The app asks for the organization ID and a `cog_` key by hand, and stores the key in Windows Credential Locker.

The first subset uses only `GET /v3/organizations/{org_id}/consumption/daily` on `https://api.devin.ai`. It does not show daily quota, weekly quota, on-demand balance, or dollars. Those self-serve data appear only in the dashboard; the upstream adapter obtains them from a private RPC.

## Primary sources

Consulted on 2026-07-21:

| Source | Supporting fact |
|---|---|
| [API Overview](https://docs.devin.ai/api-reference/overview) | The v3 API separates organization and Enterprise scopes and uses service users. |
| [Authentication](https://docs.devin.ai/api-reference/authentication) | Integrations use `cog_` keys, Bearer, RBAC, and audit. Personal PATs remain in closed beta. |
| [Organization daily consumption](https://docs.devin.ai/api-reference/v3/consumption/organizations-consumption-daily) | The public endpoint returns `total_acus` and a daily and per-product breakdown; it requires `ManageBilling` on the organization. |
| [Permissions and RBAC](https://docs.devin.ai/api-reference/v3/overview) | Organization service users are limited to one organization; Enterprise service users inherit access across organizations. |
| [Usage](https://docs.devin.ai/admin/billing/usage) | Self-serve shows usage, remaining quota, and balance in Settings; Enterprise uses ACUs. |
| [Session Insights endpoint](https://docs.devin.ai/api-reference/v3/sessions/organizations-sessions-insights) | The read-only endpoint includes ACUs per session, but also suggested prompts, analysis, and other data TokenUsage does not need. |
| [API release notes](https://docs.devin.ai/api-reference/release-notes) | Organization consumption reached v3, and ACU limits are exposed on Enterprise endpoints with `ManageBilling`. |

## Chosen contract

| Field | Value |
|---|---|
| Host | `https://api.devin.ai` |
| Method | `GET` |
| Path | `/v3/organizations/{org_id}/consumption/daily` |
| Auth | `Authorization: Bearer` with an organization service user key |
| Permission | `ManageBilling` at organization scope |
| Query | `time_after` and `time_before` as Unix timestamps |
| Response | `total_acus`, `consumption_by_date[].date`, `acus`, and `acus_by_product` |
| Accounting day | midnight PST, `08:00:00 UTC`, per the reference |

The card will say `Organization consumption` and show ACUs in an explicit period, first `Last 30 days`. The API does not return a price per ACU or spend in dollars; each Enterprise contract can have its own terms.

The permission is named `ManageBilling`. Although the endpoint is `GET`, the same permission name covers more billing than a specific read. To reduce risk:

- the key must belong to a service user with scope of a single organization;
- configuration admits only one ID and the organization endpoint; an Enterprise key stays outside the contract;
- the host is pinned;
- the feature flag starts off;
- smoke must confirm the minimum role before a public build is turned on.

If Devin does not allow creating that bounded role on a real account, the provider stays blocked.

## Rejected sources

### OpenUsage

The upstream adapter:

- reads `windsurf_api_key` and `api_server_url` from `~/.local/share/devin/credentials.toml`;
- reads `apiKey` from the app's SQLite state;
- calls `GetUserStatus` on `exa.seat_management_pb.SeatManagementService`;
- sends the key inside metadata and simulates Devin client `1.108.2`;
- converts remaining percentages into used and micros into dollars;
- can present a hidden daily quota as weekly.

That design will not be copied. Also, accepting any HTTPS host from someone else's configuration would let the key be sent to a server chosen by whoever edits the file. TokenUsage does not read that file and does not accept the override.

### Session Insights

`GET /v3/organizations/{org_id}/sessions/insights` has a read-only permission and returns `acus_consumed`, but its response also includes analysis, suggested prompts, identifiers, titles, and URLs. The product does not need that material and will not request it to sum ACUs.

### Enterprise and self-serve

- Enterprise consumption and ACU-limit endpoints require an Enterprise service user with `ManageBilling`. They stay out of the first subset because of scope and management capacity.
- Self-serve shows remaining quota and balance in the dashboard. The public API does not document an equivalent read for a local app. Personal PATs remain in closed beta.
- Dedicated deployments use their own domain. The first subset does not accept custom hosts.

## Windows probe

The probe was limited to commands, paths, processes, and the registry. It did not open files or use the network:

| Test on this machine | Result |
|---|---|
| `devin` command | absent |
| five candidate paths for CLI, credential, and app | 0 exist |
| Devin process | 0 |
| Devin or Cognition uninstall registry entries | 0 |

The implementation does not depend on a local install. Absence of CLI or app must show `Not configured` until the user adds an organization connection.

## Security and errors

- The key lives in Credential Locker and does not enter logs, cache, diagnostics, or CLI.
- The organization ID is validated against the documented `org-...` format and encoded as a segment.
- Timestamps are normalized to the requested period, and their limits are covered with local fixtures.
- The parser keeps only date and ACUs; it discards new fields.
- `401` maps to `AuthRequired`; `403` to `InsufficientPermission`; `404` to `UnsupportedScope`; `429` keeps the last valid value as stale.
- Removing the connection deletes the key and cache.

## Grok Build review

Grok Build did local forensics without the web and ended with `EndTurn`. The receipt is in `.scratch/agent-cli-delegation/grok-build/runs/2026-07-22T00-46-45-418Z-plan-bdbb16b5/result.json`: seven turns and USD 0.2188196 reported.

Grok classified the RPC, credential chain, and host override as unsafe, and proposed `block`. The parent review found the v3 organization consumption API. That contract changes the result to an experimental subset and removes the local install from the architecture.

## Pending evidence

There was no account, organization, service user, key, or authenticated call. Role, date limits, `200`, revocation, and real rate limits were not tested. The provider stays off until an HITL smoke with a temporary organization key and later deletion.

## Product decision

- Implement organization ACUs over an explicit period through the v3 API.
- Admit only an organization service user and `api.devin.ai` in the first version.
- Keep self-serve quota, balance, dollars, aggregated Enterprise, and dedicated hosts as `Unsupported`.
- Reject CLI, app DB, borrowed credentials, private RPC, simulated identity, and Session Insights.
- Do not claim remaining quota or monetary spend.
- Reopen other scopes when a read-only billing permission exists and an authorized account tests it.

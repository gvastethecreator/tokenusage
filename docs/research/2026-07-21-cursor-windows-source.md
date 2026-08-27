# Cursor Windows source gate

Cutoff date: 2026-07-21

Decision: `implement-subset`

## Answer

TokenUsage can show Cursor usage and spend for Teams and Enterprise through the public Admin API. The connection requires a key that a Cursor administrator creates and supplies by hand. The app will store that key in Windows Credential Locker.

We did not find an equivalent public contract for Individual accounts. That variant stays `Unsupported` for live usage, spend, and quota. The local engine can show observations of models used from other admitted agents, with their provenance, without presenting them as Cursor data.

TokenUsage will not read `state.vscdb`, Credential Manager, or profile files to obtain the Cursor session. It also will not call dashboard endpoints, refresh tokens, create cookies from a JWT, or automate the private CSV export.

## Primary sources

Consulted on 2026-07-21:

| Source | Supporting fact |
|---|---|
| [Admin API](https://cursor.com/docs/account/teams/admin-api) | Team administrators can create keys for `https://api.cursor.com`. The API offers members, daily usage, spend, and usage events. |
| [Teams](https://cursor.com/en-US/business/teams) | Teams offers detailed analytics and central billing. Individual does not offer organization visibility. |
| [Plans and pricing](https://cursor.com/pricing) | Cursor separates Individual, Teams, and Enterprise plans. Enterprise adds pooled usage and organization controls. |
| [Teams pricing updates](https://cursor.com/blog/teams-pricing-june-2026) | Since June 2026 the dashboard distinguishes two included-usage pools: first-party models and third-party APIs. |
| [Enterprise organizations](https://cursor.com/blog/organizations) | Enterprise can group several teams and show aggregated usage in its dashboard. |
| [Models and pricing](https://cursor.com/docs/models-and-pricing) | Public pricing source. It does not prove an account's consumption. |

The Admin API documents a key bound to the organization and Basic authentication with the key as the user. The published contract includes:

| Endpoint | Data TokenUsage can admit | Claim limit |
|---|---|---|
| `POST /teams/spend` | spend by member, configured spend limit, and cycle start | month spend; an individual limit does not represent the full included quota |
| `POST /teams/filtered-usage-events` | model, usage class, tokens, cost in cents, user, and pagination | billed or included events; not the balance of the two new pools |
| `POST /teams/daily-usage-data` | included, API, and usage-based requests, plus daily activity | activity metric; not an invoice or remaining quota |
| `GET /teams/members` | members and roles | auxiliary data; not consumption |

We did not find public fields that expose the remaining balance of the two Teams pools announced in June 2026. The first version will say `Team usage and spend`. It will not say `Remaining quota` for Cursor.

## Coverage by plan

| Plan | Approved source | Coverage |
|---|---|---|
| Individual | none found | `Unsupported`; no session or dashboard read |
| Teams | Admin API with a manual admin key | cycle spend, events, tokens, and activity per the published endpoints |
| Business | legacy name that can still appear in the `kind` field | treated as Teams data, without creating a fourth plan |
| Enterprise | Admin API with a manual admin key | the same contract per configured scope; do not infer an organization aggregate that the API does not return |

An install can store several named connections. Each key keeps its scope and provenance. The app will not join organizations or teams by email.

## OpenUsage review

OpenUsage is a comparison, not a contract. Its Cursor adapter:

- reads `cursorAuth/accessToken`, `cursorAuth/refreshToken`, and membership type from local state or Keychain;
- calls private RPCs on `api2.cursor.sh`;
- calls private routes under `cursor.com/api` for usage, summary, Stripe, and CSV export;
- creates a cookie from the JWT;
- refreshes the token and writes the new value;
- estimates spend from CSV tokens and a price catalog.

Those paths are rejected. For this case the local database contains only authentication state; it does not contribute usage facts. The OpenUsage export is an authenticated remote download, not a stable local file.

## Windows probe

The probe was limited to existence, size, date, and counts. It did not open the database or read keys, values, email, or user content.

| Test on this machine | Result |
|---|---|
| executable paths under the user profile, `%LOCALAPPDATA%`, `Program Files`, and `Program Files (x86)` | 4 candidates; 0 installs found |
| uninstall entries | 0 |
| Cursor processes | 0 |
| `%APPDATA%\Cursor\User\globalStorage\state.vscdb` | present; 12,288 bytes; last write `2026-03-06T20:47:06Z` |
| WAL/SHM sidecars | absent |
| CSVs whose name contains `usage` under the two Cursor roots | 0 |

It was not possible to observe a locked database or several real installs because Cursor is not installed or running on this machine. That absence does not block the chosen source: the approved client does not touch the local profile. Adapter tests must prove that a locked database, a missing export, or several installs do not change its result and are not explored.

## Security and data

- The admin key is requested explicitly and stored in Credential Locker.
- Only `https://api.cursor.com` and the routes pinned by the contract are allowed.
- Logs exclude key, header, email, name, and response body.
- The cache normalizes identifiers before persisting and stores minimum aggregates.
- `401` and `403` map to `AuthRequired`; `429` keeps the last valid value with a stale state; a new schema fails closed.
- The user can delete the connection, credential, and its cache from Settings.

## Grok Build review

Grok Build did local forensics without the web. The first run exhausted ten turns and was canceled; the same session resumed and ended with `EndTurn`. The valid receipt is in `.scratch/agent-cli-delegation/grok-build/runs/2026-07-22T00-30-32-392Z-plan-d0d27de4/result.json`. The two invocations reported USD 0.3661820 in total.

Grok correctly classified the database, refresh, RPCs, dashboard, and CSV as private paths, and proposed `block` with the checkout evidence. The parent review found the official Admin API. That contract changes the decision to `implement-subset` and removes all local reading from the design.

## Pending evidence

There was no login, admin key, or authenticated call. A real Teams or Enterprise account was also not tested. Implementation will use sanitized fixtures and will stay off in the public build until an authorized smoke with a test key is complete. A separate HITL ticket controls that test and the later deletion of the credential.

## Product decision

- Implement Teams and Enterprise with the Admin API and a manual key.
- Keep Individual `Unsupported` for Cursor usage, spend, and quota.
- Show spend, events, and coverage; do not promise remaining balance of the included pools.
- Reject local DB, borrowed secrets, refresh, cookies, private RPCs, private dashboard, and private CSV.
- Support several named connections without mixing scopes.
- Review the contract before each beta because the Admin API is published as a first version.

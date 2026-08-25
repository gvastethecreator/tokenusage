# Remaining usage for Grok and Cursor, and Grok Bot as a provider

Date: 2026-08-25

Decision: `keep-policy-blocked`

## Question

How does OpenUsage show remaining usage for Grok and Cursor? Can TokenUsage add Grok Bot as a measurable provider on an approved data path?

## Answer

OpenUsage remaining meters for Grok and Cursor use private session reuse. TokenUsage must not copy those paths.

Grok remaining in OpenUsage comes from `~/.grok/auth.json` plus `GET https://cli-chat-proxy.grok.com/v1/billing`. Cursor remaining comes from the editor login plus private RPC on `api2.cursor.sh`. The Cursor Grok Bot tile uses that same editor token. It is not a Grok Bot desktop reader.

Grok Bot stays `Prepared` and `quotaBlocked`. Official docs describe a cloud VM agent that signs in with a Cursor account. They do not publish a usage export, CLI quota JSON, or third-party remaining API.

## Official remaining surfaces

### Grok subscription pool

xAI documents remaining usage for people, not for other apps:

| Surface | What it shows | Fit for TokenUsage |
|---|---|---|
| Settings → Usage on web and mobile | Weekly pool percent, product breakdown, reset time, Extra Usage credits | Human UI only |
| Grok TUI `/usage` | Credit usage or billing management inside the interactive product | Not a CLI JSON contract; `grok usage` is not a documented subcommand |
| `GET /v1/api-key` | Key name, status, ACLs, team id | No remaining balance in the published schema |
| Management API prepaid balance | API team prepaid credits with a management key | Different product from SuperGrok weekly pool |

The [Grok FAQ](https://docs.x.ai/grok/faq) describes one shared weekly pool across Grok products, with Extra Usage after the included pool is exhausted. The [acceptable use policy](https://x.ai/legal/acceptable-use-policy) restricts automated access.

### Cursor remaining

Cursor documents remaining budget only for organization pooled usage:

| Surface | What it shows | Fit for TokenUsage |
|---|---|---|
| Individual dashboard in the product | Plan percent, included vs on-demand | No public remaining API |
| Team Admin API spend and events | Cycle spend, usage events, members | Manual admin key later; not Individual remaining |
| `POST /organizations/pooled-usage` | `limitCents`, `usedCents`, `remainingCents` | Enterprise org pool; not Individual Grok Bot weekly |
| Local `state.vscdb` composer snapshots | Estimated conversation context | Observed usage only; no remaining |

The Admin API page lists Grok as a first-party Cursor model in event fees. It does not publish a Grok Bot remaining field.

## What OpenUsage does

OpenUsage is a comparison, not a contract. Pinned docs on 2026-08-25:

### Grok ([grok.md](https://github.com/robinebers/openusage/blob/main/docs/providers/grok.md))

- Weekly percent and Extra Usage cap: `GET https://cli-chat-proxy.grok.com/v1/billing?format=credits`.
- Plan name: `…/v1/settings`.
- Login: read `~/.grok/auth.json`, refresh through `auth.x.ai`, write rotated tokens back.
- Spend tiles: local session files. That local spend path is already in TokenUsage. The billing call is not.

### Cursor ([cursor.md](https://github.com/robinebers/openusage/blob/main/docs/providers/cursor.md))

- Plan usage, Cursor-model percent, other-model percent, Extra Usage, credits.
- Grok Bot weekly percent and reset, shown as a Cursor On Demand widget.
- Login: Cursor `state.vscdb` plus keychain; refreshed tokens written back.
- Network: Connect RPC on `api2.cursor.sh` (`DashboardService` usage and `GetSandUsageStatus` for Grok Bot), REST `cursor.com/api/usage` and `usage-summary`, Stripe `cursor.com/api/auth/stripe`, CSV `cursor.com/api/dashboard/export-usage-events-csv`.

OpenUsage says Grok Bot has a weekly allowance separate from Cursor's billing-cycle meter. Signing into Grok CLI is not required because the Cursor access token is reused.

That Cursor Grok Bot widget is not the TokenUsage `grok-bot` module. It does not read the Grok Bot desktop app.

## Grok Bot gate

Official [overview](https://docs.x.ai/grok-bot/overview) and [get started](https://docs.x.ai/grok-bot/get-started):

- A Bot is a named agent on a persistent cloud computer.
- Eligible plans include SuperGrok Plus, SuperGrok Heavy, Cursor Pro+, Cursor Ultra, and Cursor Teams.
- Sign-in is **Sign In with Cursor**. The download page is on `cursor.com`.
- Work, files, and browser sessions live on the shared cloud computer, not in Grok Build `~/.grok` logs.

Rejected as TokenUsage sources:

| Candidate | Why it fails the gate |
|---|---|
| OpenUsage `GetSandUsageStatus` | Private Cursor RPC with the editor session |
| Grok Bot Electron profile | Session and credentials of another app |
| Grok Build `unified.jsonl` / sessions | Different product; would mis-attribute Build spend to Bot |
| xAI `GET /v1/api-key` or prepaid Management API | API key credits, not Bot remaining |
| Cursor Admin API today | No Grok Bot remaining field; org `remainingCents` is pooled spend |

A future `grok-bot` reader needs a public aggregated usage or quota contract, or written permission from xAI or Cursor. Until then the catalog entry stays prepared and quota-blocked.

## TokenUsage mapping

| Product | Remaining / quota | Observed usage | Spend |
|---|---|---|---|
| Grok Build | `PolicyBlocked` | Local unified log or session snapshots | Reported `costUsdTicks` or catalog estimate |
| Cursor Individual | Unavailable | Local composer context estimate | Catalog estimate of that context only |
| Cursor Teams / Enterprise | Later, only `remainingCents` from pooled usage with a manual admin key | Admin events when connected | Admin spend when connected |
| Grok Bot | `PolicyBlocked` | None | None |

Do not present Grok Build local cost as remaining SuperGrok weekly percent. Do not present Cursor context estimates as remaining plan percent. Do not present Cursor org `remainingCents` as Grok Bot weekly remaining.

## Sources consulted on 2026-08-25

| Source | Fact used |
|---|---|
| [Grok FAQ usage](https://docs.x.ai/grok/faq) | Shared weekly pool, Settings → Usage, Extra Usage |
| [Grok TUI commands](https://docs.x.ai/build/modes-and-commands) | `/usage` is in-product |
| [Grok CLI reference](https://docs.x.ai/build/cli/reference) | No `grok usage` JSON subcommand |
| [GET /v1/api-key](https://docs.x.ai/developers/rest-api-reference/inference/other) | Key metadata only |
| [Management billing](https://docs.x.ai/developers/rest-api-reference/management/billing) | Prepaid API team balance needs a management key |
| [Grok Bot overview](https://docs.x.ai/grok-bot/overview) | Cloud VM agent |
| [Grok Bot get started](https://docs.x.ai/grok-bot/get-started) | Cursor sign-in, eligible plans |
| [Cursor Admin API](https://cursor.com/docs/account/teams/admin-api) | Team spend and events; no Bot remaining |
| [Organization Admin API](https://cursor.com/docs/account/organizations/organization-admin-api) | `remainingCents` on org pooled usage |
| [OpenUsage Grok docs](https://github.com/robinebers/openusage/blob/main/docs/providers/grok.md) | Billing proxy + `auth.json` |
| [OpenUsage Cursor docs](https://github.com/robinebers/openusage/blob/main/docs/providers/cursor.md) | Editor token + RPC, including Grok Bot |

This note does not copy request headers, token refresh steps, or RPC payloads.

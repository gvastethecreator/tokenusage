# GitHub Copilot source gate

Cutoff date: 2026-07-21

Decision: `implement-subset`

## Answer

GitHub publishes REST endpoints dedicated to AI credit usage. TokenUsage can show consumption and extra charges for a paid personal account or a managed organization through a fine-grained personal access token that the user supplies. The app stores the token in Windows Credential Locker.

The public API does not return the remaining included allowance. TokenUsage will show `AI credits used` and `Additional charges`. It will not show `Remaining quota` for Copilot until a public contract returns the account's effective limit.

Out of scope: the internal `/copilot_internal/user` endpoint, headers that imitate an editor, extension tokens, `hosts.yml`, GitHub CLI Credential Manager, and cookies.

## Primary sources

Consulted on 2026-07-21:

| Source | Supporting fact |
|---|---|
| [Billing usage REST API](https://docs.github.com/en/rest/billing/usage?apiVersion=2026-03-10) | GitHub publishes AI credit reports for users and organizations, with schemas and permissions. |
| [Usage-based billing for individuals](https://docs.github.com/en/copilot/concepts/billing/usage-based-billing-for-individuals) | Personal plans use AI credits, reset on the first UTC day, and have an allowance per plan. The flex portion can vary. |
| [Usage-based billing for organizations and enterprises](https://docs.github.com/en/copilot/concepts/billing/usage-based-billing-for-organizations-and-enterprises) | Business and Enterprise pool included credits in the paying entity. An organization total does not represent a member's quota. |
| [Copilot usage metrics REST API](https://docs.github.com/en/rest/copilot/copilot-usage-metrics?apiVersion=2026-03-10) | GitHub offers organization and enterprise activity reports with their own permissions; they serve adoption, not balance. |
| [Copilot plans](https://docs.github.com/en/copilot/get-started/plans) | Separates Free, Student, Pro, Pro+, Max, Business, and Enterprise. |
| [GitHub CLI `gh copilot`](https://cli.github.com/manual/gh_copilot) | Runs Copilot CLI; it does not document a quota or billing command. |

## Chosen contract

REST version: `2026-03-10`.

| Scope | Endpoint | Fine-grained permission | Coverage |
|---|---|---|---|
| paid personal account | `GET /users/{username}/settings/billing/ai_credit/usage` | user `Plan: read` | AI credits, model, price, gross usage, included discount, and net charge |
| organization | `GET /organizations/{org}/settings/billing/ai_credit/usage` | organization `Administration: read` | the same fields for the organization total; supports a user filter |

The user endpoint includes only Copilot bought and billed by that personal account. If an organization or enterprise pays for the license, use that entity's endpoint. The organization endpoint requires an administrator.

The admitted fields are usage and billing facts:

- `grossQuantity`: AI credits consumed;
- `grossAmount`: gross value;
- `discountQuantity` and `discountAmount`: covered portion;
- `netQuantity` and `netAmount`: net usage and charge;
- `model`, `product`, `sku`, `unitType`, and period.

There is no field for total allowance, balance, or effective plan. The published allowance is not mixed with the response because the flex portion of personal plans can change, legacy annual plans exist, and organization pools depend on licenses and budget rules. A local balance calculation could be wrong even when usage is correct.

## Coverage by account and role

| Account | Result |
|---|---|
| Free or Student | `Unsupported` until a test proves the public endpoint returns useful data; do not use the internal endpoint for chat or completions |
| Pro, Pro+, or Max paid by the user | usage and charge through the personal endpoint and a token with `Plan: read` |
| annual Pro or Pro+ with legacy billing | out of the first subset; GitHub keeps a separate premium-requests endpoint |
| Business or Enterprise, ordinary member | `InsufficientPermission`; do not present the organization total as the member's own |
| Business or Enterprise, administrator | organization usage and charge; visible text `Organization total` |
| Enterprise with several scopes | named connections per entity; do not join users or organizations by login |

The public Copilot usage metrics reports aggregate daily activity and adoption. They do not enter the first adapter: they expand personal data and permissions without improving the spend goal.

## OpenUsage review

OpenUsage:

- searches for tokens in editor files, `gh hosts.yml`, and Keychain;
- calls `GET /copilot_internal/user` with VS Code and Copilot Chat identity;
- uses that private response for plan and personal percentages;
- lists organizations and calls a general billing summary;
- degrades to organization data when the private endpoint marks a managed seat.

Grok's local classification was correct. The parent review found newer, more specific public contracts than the upstream cutoff:

- the personal AI credits endpoint;
- the organization endpoint under `/organizations/{org}`;
- version `2026-03-10`;
- precise fine-grained permissions.

TokenUsage does not copy the upstream chain. The user enters an account or organization and supplies a credential created for this app. The client calls the dedicated AI credits endpoint without first querying a private endpoint.

## Windows probe

The probe did not open files, run `gh auth status`, or query GitHub:

| Test on this machine | Result |
|---|---|
| GitHub CLI | `gh 2.92.0` installed |
| five candidate files for `gh`, Copilot, and VS Code | two exist |
| `%APPDATA%\GitHub CLI\hosts.yml` | exists; 100 bytes; metadata only was read |
| VS Code global database | exists; 6,496,256 bytes; metadata only was read |
| `github.copilot*` extension directories under the standard VS Code root | 0 |
| VS Code processes | 0 |

The presence of `gh` or editor state does not configure the provider. The app ignores those sources. That avoids sending an Enterprise Server token to `api.github.com` and keeps the permission under the user's control.

## Security and errors

- Each connection declares `Personal` or `Organization` and a visible name.
- The token lives in Credential Locker and never enters TokenUsage logs, cache, diagnostics, or CLI.
- The host is pinned to `https://api.github.com`; GitHub Enterprise Server is not redirected to the public host.
- `401` maps to `AuthRequired`; `403` to `InsufficientPermission`; `404` to `UnsupportedScope`; `429` keeps the last valid value as stale.
- The cache stores minimum aggregates and removes login, model if the user turns off detail, and any remote body.
- Removing a connection deletes the token and cache.

## Grok Build review

Grok Build did local forensics without the web and ended with `EndTurn`. The receipt is in `.scratch/agent-cli-delegation/grok-build/runs/2026-07-22T00-38-11-898Z-plan-e0785714/result.json`: seven turns and USD 0.2205448 reported.

Grok proposed blocking personal quota and enabling organization billing conditionally. The parent review kept the private quota out, found the official user endpoint, and replaced the generic summary with the two dedicated AI credit reports.

## Pending evidence

There was no token, account, organization, or authenticated call. The work did not validate `200`, filters, discounts, revocation, or rate limits with a real account. The adapter will be implemented with sanitized fixtures and will stay off in the public build until an HITL smoke that deletes the credential when it finishes.

## Product decision

- Implement usage and spend for paid personal accounts and managed organizations.
- Use only public AI credit endpoints and a credential supplied for TokenUsage.
- Keep Free, Student, legacy plans, and members without permission in honest states.
- Do not promise remaining quota, even though GitHub publishes allowances by plan.
- Do not read or invoke internal Copilot, editor, or `gh` tokens, sessions, or endpoints.
- Reopen quota when the public REST API returns the effective limit or a stable balance.

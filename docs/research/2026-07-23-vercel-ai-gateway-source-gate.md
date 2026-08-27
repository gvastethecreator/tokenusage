# Vercel AI Gateway source gate for Windows

Date: 2026-07-23
Status: approved with limits
Ticket: 73

The primary API Key Budgets page was checked again on 2026-07-23. Its
frontmatter showed `last_updated: 2026-06-20`. The endpoint, fields, periods,
and no-budget response were unchanged.

## Decision

TokenUsage can integrate Vercel AI Gateway as a manual, experimental provider.
The approved source covers cost and tokens that passed through AI Gateway. It
can also show remaining quota for a key that has a budget, when the user
supplies the public ID of that key.

The integration does not represent all agent usage or the direct invoice of a
model provider. It must not promise read-only access. Vercel does not document
a report-only key.

## Approved contract

### Aggregated spend

- Fixed endpoint: `GET https://ai-gateway.vercel.sh/v1/report`.
- Authentication: `Authorization: Bearer <AI_GATEWAY_API_KEY>`.
- Required parameters: `start_date` and `end_date`, inclusive UTC dates.
- Plans: Pro and Enterprise. Hobby and Pro Trial are out of scope.
- Status: beta.
- Endpoint price: USD 5 per 1,000 queries.
- Scope: the whole account tied to the key.
- Delay: requests can take a few minutes to appear.

The endpoint returns `results`. Each row includes one dimension, chosen with
`group_by`, plus the aggregated metrics. No pagination is documented. Vercel
also does not fix a maximum range, a maximum row count, or a rate limit on this
page. TokenUsage must limit each query to 31 days, use daily aggregation, and
use a local cache to control cost and size.

One query cannot return day and model as dimensions at the same time. The MVP
will run one query with `group_by=day&date_part=day`. A model breakdown needs
another billed query. It stays out of the first cut.

Metrics that TokenUsage can show:

- `total_cost`: amount charged by Vercel AI Gateway, in USD. It is zero for
  BYOK.
- `market_cost`: market value estimated by the gateway for BYOK and non-BYOK
  traffic. It is not an external invoice.
- `gateway_cost` and `surcharge_cost`: parts of the gateway cost.
- input, output, cache, cache-creation, and reasoning tokens.
- request count.

### Key quota

Vercel allows an optional budget per key. The documented minimum is USD 1.
Periods are daily, weekly, monthly, or no reset. The limit is soft. The
request that crosses it still finishes. Spend can go a little over the limit.

TokenUsage can query:

`GET https://ai-gateway.vercel.sh/v1/quotas?quotaEntityId=api_key_id_<key-id>`

The call uses the same manual key. A key with a budget returns limit, current
spend, period, and status. A key without a budget returns `404` with
`Quota not found`. Remaining is `max(0, limitAmount-currentSpend)`. The key ID
cannot be derived from the secret through a documented method, so the user
must also copy that ID from Vercel.

## Security and permissions

An AI Gateway key works for inference and for reports. Vercel does not publish
a read-only scope. The reporting API also declares account scope. This blocks
presenting the connection as a low-privilege credential.

The integration must follow these rules:

1. Require a new key created for TokenUsage.
2. Recommend expiration and a USD 1 budget.
3. Accept the key only through a manual user action.
4. Store it in Windows Credential Locker.
5. Do not read environment variables, files, or keys from other agents.
6. Pin both hosts and reject redirects to another origin.
7. Delete the credential and the account cache on disconnect.
8. Before save, show that the key can run models and that the report covers
   the whole account.
9. Keep the provider experimental until an authorized smoke test.

Vercel allows keys with `projectId`, an expiration date, and a budget. The
report document still states account scope, so TokenUsage must not claim that
`projectId` reduces report data.

## States and errors

The client must type at least these states. Only `401` was observed in this
gate. The others are defensive handling until an authorized smoke test:

- `401`: missing, invalid, or revoked key.
- `403`: possible unsupported plan or permission, with no confirmed error
  contract.
- `404` on quotas: the contract confirms `Quota not found` for a key without a
  budget. A wrong ID can be indistinguishable until smoke.
- `429`: defensive HTTP handling. Vercel does not publish a rate limit on the
  report page.
- network error or timeout.
- invalid JSON or unknown contract.
- valid empty report.
- stored data with a delay warning.

A call without `Authorization` returns `401` and
`authentication_error`. This check ran without credentials on
2026-07-23 against both endpoints.

Metric names and meaning, quota fields, the minimum budget, periods, and the
soft limit come from the cited primary documentation. Fixtures must pin that
shape for the client. Real smoke must confirm that shape before the
experimental mark is removed.

## Claim separation

| Data | Allowed claim |
| --- | --- |
| `total_cost` | Spend charged by Vercel AI Gateway in the period |
| `market_cost` | Market value reported by the gateway |
| report tokens | Tokens processed by AI Gateway |
| key quota | Remaining budget of that AI Gateway key |
| agent activity | Not inferred from the report |
| external BYOK invoice | Not inferred from the report |
| Cursor, Codex, or other agent quota | Not inferred from the report |

## Local comparison

CodeBurn implements `GET /v1/report` with a manual key, but it combines
`group_by=model` with `date_part=day`. Under the current contract, `date_part`
applies only when `group_by=day`. TokenUsage must not copy that query.
CodeBurn also reads environment variables. TokenUsage will ask for its own key
and use Credential Locker.

## Primary sources

- [Custom Reporting](https://vercel.com/docs/ai-gateway/observability-and-spend/custom-reporting)
- [API Keys](https://vercel.com/docs/ai-gateway/authentication-and-byok/api-keys)
- [API Key Budgets](https://vercel.com/docs/ai-gateway/observability-and-spend/api-key-budgets)
- [Authentication and BYOK](https://vercel.com/docs/ai-gateway/authentication-and-byok)
- [AI Gateway pricing](https://vercel.com/docs/ai-gateway/pricing)
- [Coding agents](https://vercel.com/docs/ai-gateway/coding-agents)
- [Custom Reporting changelog](https://vercel.com/changelog/custom-reporting-ai-gateway)

## Implementation gate

Ticket 74 can start with the client, mapping, fixtures, and experimental UI.
Real smoke stays blocked until the user authorizes a test key with a budget.
No automatic test must search for or use local credentials.

The adversarial Grok review accepted the gate with human review. Its main
objection was to separate documented facts, observed responses, and defensive
handling. The parent incorporated that separation. The objection about cost
and quota fields does not block the contract. Those fields appear in the
primary sources.

# Ticket 74E1: Vercel API key quota client

## Outcome

TokenUsage now has a typed, offline-tested client for the documented Vercel AI
Gateway API-key budget endpoint. The client accepts the user-supplied raw key
ID, builds the fixed quota entity ID and returns either a validated budget or a
closed no-budget result.

This slice does not store the key ID, call the client from the runtime or show
quota in the app. Those changes remain in 74E2 and 74E3.

## Contract and safety

- The request is fixed to
  `https://ai-gateway.vercel.sh/v1/quotas?quotaEntityId=api_key_id_<key-id>`.
- The raw key ID accepts 1 to 256 ASCII letters, digits, `_` and `-`. It rejects
  a pre-prefixed entity ID and all other input before network access.
- The final response origin must remain `https://ai-gateway.vercel.sh`.
- Response reads stop at 64 KiB.
- `401`, `403`, `429`, other HTTP failures, network failures, timeouts and
  contract failures have separate typed outcomes. `Retry-After` is preserved.
- A `404` becomes no-budget only when its `error` value is exactly
  `Quota not found`.
- The success mapper checks entity identity, required fields, the USD 1 minimum,
  non-negative spend and the four documented refresh periods.
- Remaining budget is `max(0, limitAmount - currentSpend)`.
- Error text never includes the API key, key ID or response body.
- Unknown top-level response fields remain valid so Vercel can add fields
  without breaking the client.

## Automated proof

Focused quota client gate:

```text
dotnet test tests\WOpenUsage.Providers.Tests\WOpenUsage.Providers.Tests.csproj \
  -c Release --no-restore \
  --filter "FullyQualifiedName~VercelGatewayQuotaClientTests"

Passed: 30 | Failed: 0 | Skipped: 0
```

All Vercel provider tests:

```text
dotnet test tests\WOpenUsage.Providers.Tests\WOpenUsage.Providers.Tests.csproj \
  -c Release --no-restore \
  --filter "FullyQualifiedName~VercelAiGateway"

Passed: 79 | Failed: 0 | Skipped: 0
```

The tests cover the fixed request, bearer header, all refresh periods,
remaining-budget clamp, no-budget, malformed and hostile input, harmless future
fields, status mapping, throttling, cancellation, network failure, timeout,
cross-origin response and size limit.

## Review

The first Grok Build implementation run hit repeated `read_file` tool failures,
returned cancelled and produced no valid files. A second read-only review hit
its external timeout without a result. Both isolated snapshots were discarded;
no Grok code or verdict was accepted.

Local review changed the status path so authentication and other HTTP errors do
not read response bodies. This keeps a large `401` classified as authentication
instead of a response-contract failure. The regression has a direct test.

## Remaining gates

- 74E2: store the non-secret key ID with the Vercel connection and combine the
  quota call with the existing report runtime.
- 74E3: show budget, spend, remaining amount, period and no-budget state in the
  app with English and Spanish copy.
- 74F: run an authorized packaged smoke with a disposable real key and remove
  all stored Vercel data afterward.

# OpenRouter source gate

Date: 2026-07-23

## Decision

TokenUsage can integrate OpenRouter through a key that the user supplies. The
app must not search `OPENROUTER_API_KEY`, other app files, or browser
credentials. A later cut will store the key in Windows Credential Locker.

The current official contract splits two capabilities:

- `GET https://openrouter.ai/api/v1/key` reports usage for the active key,
  including daily, weekly, and monthly periods, an optional limit, and cadence.
- `GET https://openrouter.ai/api/v1/credits` reports purchased credits and
  total usage, but it requires a management key.

As a result, a common key can show usage and a limit without showing the
global balance. The future UI must show `Insufficient permission` for credits.
It must not hide a valid `/key` result.

Primary sources:

- [remaining credits](https://openrouter.ai/docs/api/api-reference/credits/get-credits)
- [current key](https://openrouter.ai/docs/api/api-reference/api-keys/get-current-key)
- [reference and OpenAPI](https://openrouter.ai/docs/api/reference/overview)

## Fixed contract

Both calls use `GET`, the fixed host `openrouter.ai`, and
`Authorization: Bearer <manual key>`. The client sends no bodies and does not
accept configurable hosts.

`/credits`:

```json
{
  "data": {
    "total_credits": 100.5,
    "total_usage": 25.75
  }
}
```

`/key` uses the fields `usage`, `usage_daily`, `usage_weekly`,
`usage_monthly`, `limit`, `limit_remaining`, `limit_reset`, and `is_free_tier`.
Observed amounts must be finite and not negative. `limit` and
`limit_remaining` can be null. The accepted cadence is `daily`, `weekly`,
`monthly`, or null.

## Failures

- `401`: invalid or revoked key.
- `403`: insufficient permission for that capability.
- `429`: throttle with optional `Retry-After`.
- network, timeout, and other statuses: transient failure.
- JSON, origin, size, or schema: contract failure.

Messages must not include the key, the remote body, or internal exceptions.
Responses are limited to 64 KiB. The client reads the body only after the
final origin matches.

## Delivery cuts

1. `27A`: typed HTTP client and offline tests.
2. `27B`: Credential Locker and account-bound deletion, after the Ticket 24
   privacy control.
3. `27C`: runtime, cache, and partial results by capability.
4. `27D`: i18n UI, manual settings, and packaged test.
5. `27E`: authorized smoke with a disposable key. Do not print the key. Do not
   store it outside Credential Locker.

The 27A client does not enable network in the app and does not complete
Ticket 27.

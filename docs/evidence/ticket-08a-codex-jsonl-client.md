# Ticket 08A: Codex JSONL client and quota parser

Date: 2026-07-22

Status: implemented and verified with synthetic streams. The real Codex process,
account detection, snapshot mapping, cache wiring, and UI remain outside this
slice.

## Contract checked

The installed `codex-cli 0.145.0` generated its stable app-server schemas with:

```powershell
codex app-server generate-json-schema --out .scratch/wopenusage/codex-schema-0.145.0
```

The command emits protocol types only. It does not start app-server or read an
account response. Ticket 08A uses these generated definitions:

- `v1/InitializeParams.json`;
- `v1/InitializeResponse.json`;
- `v2/GetAccountRateLimitsResponse.json`.

The client now sends `initialize`, validates the matching response ID, sends the
`initialized` notification, and then allows `account/rateLimits/read`.

## Implementation

- `CodexAppServerClient` serializes one request at a time, uses monotonic IDs,
  applies timeout and cancellation, ignores server notifications, and rejects a
  mismatched response.
- `CodexJsonlTransport` reads UTF-8 incrementally with a hard line bound. It
  rejects an oversized, truncated, closed, or broken stream without including
  the line in the exception.
- `CodexRateLimitsParser` reads primary, secondary, and extra limit buckets. It
  accepts new fields, normalizes unknown plan values, validates percent/reset
  values, and caps safe additional IDs.
- Initialize results are validated only as an object. `codexHome`, account
  fields, raw JSON, error data, and server messages never enter the public
  result or an exception.
- A protocol, transport, timeout, or mid-request cancellation marks the session
  unusable. Disposal cancels the active exchange before closing the streams.

The parser returns provider-side immutable models. Core, cache format, WinUI,
process code, and package identity did not change.

## Synthetic proof

The in-memory fake peer covers:

- successful handshake and quota read;
- numeric and string response IDs;
- extra fields and interleaved notifications;
- primary, secondary, null, and additional windows;
- unknown plans;
- timeout versus caller cancellation;
- mismatched IDs and failed-session reuse;
- oversized, invalid, and truncated JSONL;
- sanitized JSON-RPC errors;
- percent values outside `0..100`;
- handshake requirement and idempotence.

No fixture contains a real token, email, account ID, quota, response, or user
path.

## Verification

```text
Focused Codex tests, x64: 18/18 passed
scripts/check.ps1 -Platform x64:
  Architecture 22/22, Core 32/32, Providers 32/32, build 0 warnings/errors
scripts/check.ps1 -Platform ARM64:
  Architecture 22/22, Core 32/32, Providers 32/32, build 0 warnings/errors
dotnet format WOpenUsage.slnx --verify-no-changes --no-restore: passed
git diff --check: passed
```

Grok Build supplied the slice plan and performed a read-only review of the real
files. The review returned `accept` with no P0/P1/P2 finding. Its implementation
runs produced no files and were discarded. Parent-local review and checks are
the source of truth.

## Remaining work

Ticket 08B maps the parsed primary, secondary, and additional windows into the
existing provider snapshot contract. Later slices add explicit account outcomes,
the supervised Windows process, cache-first composition, and the real Codex card.

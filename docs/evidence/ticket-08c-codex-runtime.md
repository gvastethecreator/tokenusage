# Ticket 08C: Codex account classification and provider runtime

Date: 2026-07-22

Status: implemented and verified over injectable clients and synthetic JSONL.
The trusted executable resolver, supervised Windows process, cache composition,
and live UI remain later slices.

## Account contract

Codex CLI 0.145.0 defines `account/read` as a read method with an optional
`refreshToken` flag. The client sends `refreshToken: false` and selects only:

- `requiresOpenaiAuth`;
- account `type`;
- ChatGPT `planType`.

It never keeps or returns the account email, raw response, token, account ID, or
user path. It does not invoke login, logout, token refresh, reset-credit, model,
or email methods.

## Runtime outcomes

`CodexProviderRuntime` now maps the local client and account states into the Core
provider contract:

| Condition | Provider outcome |
| --- | --- |
| Missing Codex CLI | `NotConfigured` |
| Unsupported CLI contract | `ContractFailure` with last-good data |
| App-server unavailable | `TransientFailure` with last-good data |
| Missing ChatGPT session | `NotConfigured` |
| API key, Bedrock, or unknown auth | `UnsupportedAccount` |
| ChatGPT without quota windows | `UnsupportedAccount` |
| Timeout or JSON-RPC rejection | `TransientFailure` with last-good data |
| Invalid protocol shape | `ContractFailure` with last-good data |
| Valid ChatGPT quota | `Success` with mapped Codex snapshot |
| User cancellation | Propagated to the caller |

Unsupported account states do not reuse a former account's last-good snapshot.
This avoids showing quota from a prior account after an auth change.

## Proof

Synthetic tests cover selective account parsing, the no-refresh request, private
field removal, all supported account kinds, invalid shapes, handshake order,
local detection, plan fallback, each provider outcome, last-good retention,
client disposal, and pre-cancelled refresh.

```text
Focused Codex provider tests, x64: 50/50 passed
scripts/check.ps1 -Platform x64:
  Architecture 22/22, Core 32/32, Providers 64/64, build 0 warnings/errors
scripts/check.ps1 -Platform ARM64:
  Architecture 22/22, Core 32/32, Providers 64/64, build 0 warnings/errors
dotnet format TokenUsage.slnx --verify-no-changes --no-restore: passed
```

Grok Build performed a read-only review of the exact 08C files. It returned
`accept` with no P0/P1/P2 finding. The run used seven model calls and reported
US$0.2418036. Parent-local inspection and checks remain authoritative.

## Next

Ticket 08D adds the Windows-only executable resolver and process supervisor.
Ticket 08E then composes this runtime with the cache-first dashboard and real
Codex card.

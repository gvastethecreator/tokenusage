# Ticket 74E2a: Vercel connection key ID

## Outcome

TokenUsage can now keep the optional public Vercel API key ID with its manual
AI Gateway connection. The API key remains the Credential Locker password. A
validated key ID uses the credential username `key-id:<raw-id>`; a connection
without quota metadata uses `manual`.

The current entry uses a new `/vercel-ai-gateway/v1` resource. Reads fall back
to the prior `/vercel-ai-gateway` resource, so existing installations keep
working. A successful save writes v1 first and then removes the legacy entry.
An older build will therefore see a disconnected provider after migration and
will never send metadata as an API key.

## Safety and lifecycle

- The key ID shares the quota client's validation contract and is rejected
  before cache or credential mutation.
- The store writes one normal username/password credential. It does not place
  JSON or a larger data blob in the password field.
- Save writes the new credential before removing prior v1 usernames and the
  legacy entry. A write failure leaves the prior credential intact.
- Read accepts zero or one v1 credential. Multiple or malformed entries fail
  with one sanitized `InvalidDataException` and do not read extra secrets.
- Disconnect removes every v1 username and the legacy entry.
- The old connect overload remains valid. It saves a v1 connection without a
  key ID, which keeps 74D behavior until the 74E3 UI supplies the ID.
- Debug and UI-test credential stores follow the same optional-ID contract.

Microsoft documents Credential Locker as username/password storage, supports
lookup by resource and caps AppContainer apps at 20 credentials. This design
uses those fields directly and needs at most two v1 entries for the short span
of a replacement operation. See [Credential locker for Windows
apps](https://learn.microsoft.com/en-us/windows/apps/develop/security/credential-locker).

## Automated proof

```text
Vercel provider tests: 80/80
Vercel Windows tests: 36/36
Release/x64 WOpenUsage.App build: passed, 0 warnings
```

Focused tests cover legacy reads, current reads with and without a key ID,
invalid IDs, multiple-entry failure, connect ordering, cache refusal,
cancellation, disconnect and key-ID cleanup.

## Review

Grok Build reviewed the storage design without file access after two prior CLI
runs had failed. Its useful finding was the downgrade risk of overwriting the
legacy resource with an encoded value. Local review replaced that plan with the
separate v1 resource and username field described above.

Grok also raised cache loss on write failure. The connection service already
keeps the old credential because it clears only cached provider data before the
write; it does not delete the credential during connect. A failed write can
force a fresh report on the next run, but it does not remove the working key.

## Remaining 74E2 work

The provider runtime still needs to call the quota client when `KeyId` exists,
add the budget metric to the snapshot and keep the report usable when the quota
call fails. The app UI remains in 74E3.

# Ticket 74C1: Vercel Credential Locker

Date: 2026-07-23
Status: credential store implemented and verified; disconnect coordination pending

## Delivered

- one package-scoped resource and stable `manual` user name;
- exact `resource + userName` reads, writes and deletion;
- presence checks through `FindAllByResource` without loading passwords;
- no `RetrieveAll`, environment import, file storage, logging or network access;
- API keys preserved byte-for-byte after non-empty validation;
- cancellation checks before and after each synchronous vault operation;
- an injected vault seam so unit tests never open the user's Credential Locker;
- exact handling of the Windows element-not-found HRESULT without hiding other errors.

The package is a full-trust desktop app. Microsoft notes that such apps can
access user lockers outside their own AppContainer, so the adapter always uses
the exact TokenUsage resource and never enumerates all credentials:

- https://learn.microsoft.com/en-us/uwp/api/windows.security.credentials.passwordvault
- https://learn.microsoft.com/en-us/uwp/api/windows.security.credentials.passwordvault.findallbyresource
- https://learn.microsoft.com/en-us/windows/apps/develop/security/credential-locker

## Delegation and review

Grok Build received a three-file implementation slice in a project-local
snapshot. Repeated `read_file` failures cancelled the run before it wrote any
file. The snapshot was discarded and its run evidence was preserved. The
parent implemented and reviewed the slice locally.

## Proof

```text
dotnet test tests\WOpenUsage.Platform.Windows.Tests\WOpenUsage.Platform.Windows.Tests.csproj \
  -c Release -p:Platform=x64 --no-restore

Passed: 71, Failed: 0, Skipped: 0
```

Thirteen tests cover the credential store. The full Windows test project also
passes. No test writes to the real locker and no live credential was used.

## Remaining gate

Disconnect must share a gate with the full Vercel refresh. It must wait for an
active refresh, remove the exact credential and then remove only the Vercel
cache entry. A failed cache removal must return a typed partial result.

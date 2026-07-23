# Ticket 74C2: Vercel disconnect coordination

Date: 2026-07-23
Status: implemented and verified

## Delivered

- reusable async provider operation gate;
- cache-first publication remains outside the gate;
- provider refresh, provider-ID validation and last-good save share one lease;
- `ProviderCompleted` is emitted after release;
- Vercel refresh coordinator shares the gate with connection management;
- disconnect waits for an active refresh, removes the exact credential, then
  removes only `vercel-ai-gateway` from cache;
- cleanup becomes non-cancelable after gate acquisition, avoiding a deleted
  credential with active cache after mid-operation cancellation;
- typed cache cleanup states for removed, missing, quarantined, future schema,
  I/O, access, lock timeout and rejected writes;
- corrupt and future-schema cache states stay partial and expose only a file
  name or schema version.

## Concurrency proof

The integration test blocks the HTTP report client after refresh owns the gate,
starts disconnect and proves the credential remains untouched. After the report
returns, refresh saves last-good, releases the gate, disconnect deletes the
credential and removes the saved snapshot. A later refresh returns
`NotConfigured` and makes no second HTTP call.

Additional tests cover cancellation while waiting, cancellation after
acquisition, future cache versions, corrupt-cache quarantine, missing state,
provider exceptions and idempotent lease disposal.

## Delegation and review

Grok Build received the bounded core implementation in a project-local
snapshot. Repeated `read_file` errors exhausted the turn limit; no files were
written and the snapshot was discarded. A later read-only review also exceeded
its turn limit and returned contradictory partial notes, so it was rejected as
invalid evidence.

Parent review accepted the core gate, rejected cancellation and torn-write
claims contradicted by `CancellationToken.None` and atomic `SnapshotStore`
writes, and accepted one lifecycle concern. The gate no longer disposes its
managed semaphore at app shutdown, avoiding a release-versus-dispose race. It
never accesses `SemaphoreSlim.AvailableWaitHandle`, so it creates no optional
OS wait handle.

## Proof

Focused:

```text
CacheFirstRefreshTests + CacheFirstRefreshOperationGateTests: 10/10
VercelGatewayConnectionServiceTests: 6/6
dotnet format --verify-no-changes: passed for all changed C# files
```

Full Release/x64 gate:

```text
.\scripts\check.ps1 -Platform x64 -Configuration Release

Architecture: 62/62
Core: 83/83
CLI: 82/82
Providers: 216/216
Platform Windows: 77/77
Solution + x64 MSIX package build: passed
```

## Remaining gates

- 74D must wire this coordinator into the app and add explicit connect,
  consent, disconnect and partial-cleanup UI in English and Spanish.
- 74E adds per-key quota after the approved endpoint contract.
- 74F requires an authorized packaged smoke with a disposable key and verified
  credential removal.

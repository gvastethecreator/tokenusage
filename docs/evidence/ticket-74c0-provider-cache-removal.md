# Ticket 74C0: provider-scoped cache removal

Date: 2026-07-23
Status: implemented and verified

## Delivered

- `SnapshotStore.RemoveProviderAsync` under the existing cross-process mutex;
- atomic replacement that preserves every other provider;
- valid empty v1 cache when the removed provider was the last entry;
- no write for missing files or providers;
- explicit results for removed, missing, corrupt and future-schema states;
- future-schema bytes remain untouched;
- corrupt data follows existing quarantine behavior with file-name-only output;
- failed replacement preserves prior bytes and removes its temporary file;
- cancellation before the lock changes nothing.

## Delegation and review

Grok Build received the bounded implementation slice in a project-local
snapshot. Its run failed before returning a valid result and produced no
changes. The parent discarded the snapshot and implemented the slice locally.

Independent concurrency review returned `ACCEPT`. It confirmed one mutex spans
read-modify-write, atomic replacement remains intact and disconnect can branch
on every typed result. Final parent Sol audit: `accept`.

## Proof

```text
dotnet test tests\WOpenUsage.Core.Tests\WOpenUsage.Core.Tests.csproj \
  --filter FullyQualifiedName~SnapshotStoreTests --no-restore

Passed: 27, Failed: 0, Skipped: 0
```

`dotnet format` returned success for the three affected files and printed the
known workspace-load warning without a file or diagnostic.

## Remaining gate

Credential removal must stop or exclude concurrent Vercel refresh before it
removes the cached snapshot. Otherwise a later refresh can add the provider
again after disconnect. Ticket 74C1 owns that coordination.

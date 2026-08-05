# Ticket 10: Codex recovery and retained data

Date: 2026-07-22

Status: implemented and verified on x64. ARM64 has cross-build proof.

## Result

The Codex path now keeps the last valid snapshot during transient, contract,
process, and executable failures. It adds:

- one active refresh per provider;
- one active Codex process owner per factory;
- exponential backoff from 15 seconds to 5 minutes with 20% jitter;
- manual refresh that bypasses backoff but waits for the active process;
- automatic retry after the backoff deadline;
- retained-snapshot age and retry time in the live status;
- a 30-second UI tick for current relative time;
- recovery after process close, timeout, contract failure, and executable-path
  change.

Missing Codex CLI now keeps a prior snapshot as a transient failure. A first
run without Codex remains an explicit not-configured state.

## Real Codex integration

The first real smoke failed before handshake. The resolver had selected a
broken Bun global shim before the installed native Codex binary. The resolver
now keeps explicit override first, then prefers the official architecture-
specific npm binary, and leaves Bun shims as fallback.

After that fix, Codex CLI 0.145.0 passed:

1. real app-server start and handshake;
2. controlled process close;
3. a second start and live snapshot read;
4. packaged WinUI activation;
5. live card publication;
6. manual refresh without losing the card.

The smoke logs only pass/fail, provider ID, and metric ID classes. It does not
print quota values, account data, executable paths, or cached content. The UI
proof did not take a screenshot because that would store real quota data.

Run the real smoke explicitly:

```powershell
$env:TOKENUSAGE_RUN_REAL_CODEX_SMOKE = '1'
dotnet test tests/TokenUsage.Platform.Windows.Tests/TokenUsage.Platform.Windows.Tests.csproj `
  -p:Platform=x64 `
  --filter FullyQualifiedName~RealCodexRecoverySmokeRestartsAfterAControlledClose
```

## Synthetic recovery proof

`FakeCodex` supports quota, crash, timeout, and contract modes. The recovery
test starts with a valid snapshot, crosses all three failures, then copies the
fake binary to a new path and proves that the replacement path started. Every
failure retains the original observation time. The final run replaces it with
a valid snapshot.

The fake clock is fixed for stable usage dates and reset times. A marker file
under the test temp directory proves the replacement executable ran.

## Verification

```text
scripts/check.ps1 -Platform x64 -Configuration Debug:
  Architecture 22/22, Core 44/44, Providers 116/116,
  Platform.Windows 52/52, build 0 warnings/errors
scripts/check.ps1 -Platform ARM64 -Configuration Debug:
  same host suites, ARM64 build 0 warnings/errors
real Codex recovery smoke: 1/1 passed
packaged real Codex UI and manual refresh: passed
tests/ui/ticket-10-real-codex.ps1: passed
dotnet format --verify-no-changes: passed
git diff --check: passed
```

The first x64 checkpoint exposed an ambiguous WinUI timer type. The field now
uses the full Microsoft UI type, and the full checkpoint passed afterward.

## Review

Nine bounded Grok Build runs covered cache, concurrency, backoff, binary
recovery, fake process, UI, and the final diff. Five produced usable verdicts;
four hit the 12-turn cap. Total reported cost was US$2.6396012.

Accepted findings led to process ownership, live time updates, retry text,
deterministic fake dates, direct replacement-path proof, and safer cleanup.
The parent reviewed every change and ran all proof above.

## Claim boundary

This closes Ticket 10 for Codex on x64 with a real account smoke and packaged
UI proof. It does not claim native ARM64 runtime, real throttle response, or a
stored screenshot of private quota values.

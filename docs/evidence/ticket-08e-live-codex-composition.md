# Ticket 08E: live Codex dashboard composition

Date: 2026-07-22

Status: implemented and verified with a synthetic Codex process. A real account
call remains an opt-in human check.

## Runtime path

The packaged app now builds one live Codex path when sample mode is off:

1. resolve the local `codex.exe`;
2. start `codex app-server --stdio` on a worker thread;
3. complete the JSONL handshake and quota request;
4. map the response into the provider snapshot;
5. show a cached snapshot first, then replace it with the fresh result;
6. project quota windows, plan, remaining amount, and reset time into the
   existing compact dashboard.

`CodexAppServerQuotaClientFactory` owns the process and protocol client as one
unit. Cancellation after process start closes that unit before it propagates.
Shutdown errors cannot erase quota that the request already returned.

## UI states

The live surface keeps the sample layout and its animated charts. It adds:

- a loading state while the live refresh runs;
- explicit text for a missing CLI, a missing login, an unsupported account, and
  an unsupported CLI contract;
- a cache age or refresh result below the cards;
- all public quota windows returned by Codex, except internal IDs;
- no spend total when Codex supplies quota without billing data.

The sample switch still restores the five-provider fake dashboard and never
starts the Codex process.

## Privacy boundary

The live composition stores and projects only provider ID, plan, limit labels,
used and remaining values, reset times, and safe state text. It does not project
email, raw JSON, tokens, stderr, executable paths, or account content. A scan of
the changed files found no credential-shaped values.

## Synthetic end-to-end proof

`CodexLiveCompositionTests` launches the compiled fake `codex.exe` through the
real Windows process owner. The fake peer handles initialize, initialized,
`account/read`, and `account/rateLimits/read`. The test then proves the complete
path through the JSONL client, provider runtime, atomic cache, coordinator, and
dashboard projector. It also checks that the projected model has no account
identity or spend value.

```text
scripts/check.ps1 -Platform x64 -Configuration Debug:
  Architecture 22/22, Core 32/32, Providers 69/69,
  Platform.Windows 48/48, solution build 0 warnings/errors
scripts/check.ps1 -Platform ARM64 -Configuration Debug:
  Architecture 22/22, Core 32/32, Providers 69/69,
  Platform.Windows 48/48 on the x64 host,
  ARM64 solution build 0 warnings/errors
tests/ui/ticket-08e-codex.ps1: 5/5 passed
git diff --check: passed
credential-shaped source scan: no matches
```

## Visual proof

- `artifacts/ticket-08e/01-live-codex.png` shows the compact live surface and
  explicit unavailable state with no fake spend.
- `artifacts/ticket-08e/02-sample-preserved.png` shows the preserved sample
  dashboard, charts, provider colors, and short footer.
- `artifacts/ticket-08e/ui-results.json` records the five UI Automation checks.

Package activation through AUMID does not inherit the shell-only executable
override, so the synthetic live-success state has integration proof without a
matching screenshot. The test did not invoke a real Codex account. ARM64 has
cross-build proof; native runtime proof remains on the x64 host.

## Review

Grok Build found two useful risks during its first bounded read-only pass:
shutdown failure could hide a valid response, and JSON-RPC `-32601` needed an
explicit unsupported-CLI state. Both have focused regressions. That pass reached
its 12-turn cap and reported `Cancelled` after US$0.346906. A second read-only
pass stalled after a file-read error and produced no verdict. The parent reviewed
the complete diff, checked the WinUI bindings and process ownership, and ran all
proof above. No actionable P0, P1, or P2 issue remains in the reviewed slice.

## Claim boundary

Ticket 08E proves the live composition with a synthetic protocol peer and the
unavailable UI with the packaged app. It does not claim a real-account result,
native ARM64 execution, or visual proof of the synthetic success state.

## Next

Ticket 09 adds daily Codex use and pace from `account/usage/read` while keeping
the same privacy boundary and cache path.

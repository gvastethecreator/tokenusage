# Ticket 74D: Vercel AI Gateway app integration

## Outcome

TokenUsage now exposes an experimental Vercel AI Gateway connection in the
Windows options view. The user must enter a key and accept the stated risk
before connecting. The app stores the key in Credential Locker, clears the
password field at once, loads the account-wide 30-day report and can disconnect
while removing the Vercel cache.

The live dashboard shows gateway spend, input and output tokens, request count,
source, observed time, coverage and report lag. The provider status view keeps
quota, usage, spend and coverage separate. English and Spanish resources cover
the full flow.

## Safety and lifecycle

- Connect, refresh, configured-state checks and disconnect share one operation
  gate.
- Connect removes only the Vercel snapshot before saving a replacement key. A
  future cache schema blocks the change and preserves the prior key and bytes.
- Once a gated mutation starts, caller cancellation cannot leave half of that
  mutation committed.
- Fresh reports are reused for ten minutes unless the user requests a forced
  refresh, which avoids needless paid report calls.
- The UI keeps only key presence in its ViewModel. The password itself stays in
  the PasswordBox long enough to start the connection and is then cleared.
- The Release test fixture uses an in-memory fake key and report. It is compiled
  only when `EnableUiTestFixtures=true`; normal Release builds exclude it.

## UI proof

The packaged Release/x64 fixture passed `tests/ui/ticket-74d-vercel.ps1`:

```text
Passed: 7 | Failed: 0
```

Covered states:

- risk notice and disabled connect action;
- consent plus key enabling connect;
- connected report through the real app composition;
- independent provider capability rows;
- spend and token card;
- disconnect and card removal;
- AutomationId coverage for interactive controls.

Visual artifacts are kept in `artifacts/ticket-74d/`. Review confirmed the
compact options state, the Vercel card, localized copy, metric fit and provider
mark. The packaged launch also exposed an existing asset-layout fault: WAP
places the tray icon at the package root while the executable is nested. The
icon resolver now supports both layouts.

## Automated proof

Focused Release checks:

```text
Vercel provider tests: 49/49
Vercel Windows and projector tests: 27/27
Localization contract tests: 5/5
```

Full Release/x64 gate:

```text
.\scripts\check.ps1 -Platform x64 -Configuration Release

Architecture: 62/62
Core: 83/83
CLI: 82/82
Providers: 219/219
Platform Windows: 85/85
Solution and x64 MSIX package build: passed
```

## Review

Local WinUI review moved `InfoBarSeverity` mapping out of the ViewModel and into
a view converter. No P0-P2 finding remains in the reviewed slice.

Grok Build was asked for a read-only final review. It reached the eight-turn cap
after a `read_file` tool error and returned a cancelled, contradictory result,
so it is not accepted as review evidence. The run remains under
`.scratch/agent-cli-delegation/grok-build/runs/`.

## Remaining gate

74D uses only synthetic credentials and data for runtime proof. Ticket 74F
still requires an authorized packaged smoke with a disposable real key, then a
check that Credential Locker and the Vercel cache contain no remaining data.
Ticket 74E remains separate because per-key quota needs its own approved API
contract.

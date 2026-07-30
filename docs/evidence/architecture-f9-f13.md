# Architecture F9-F13 evidence

Date: 2026-07-25

## Scope

This change implements the accepted F9-F13 findings from
`docs/architecture/architecture-review-2026-07-24.md`:

- concurrent and selected-provider refresh through one host;
- one Windows provider catalog for App and CLI composition;
- one App session host for refresh, alerts, cancellation, and shutdown;
- child WinUI views and bindable surface modules;
- shared typed automation queries for CLI and a later local API.

The change does not add providers, expose an HTTP service, use live credentials,
sign a release, publish a package, or change the remote tracker.

## Implementation proof

- `ProviderRefreshHost` starts provider work together, publishes each completion,
  observes sibling tasks after cancellation, and supports one selected provider.
- `WindowsProviderCatalog` owns IDs, capabilities, paths, local usage sources,
  runtime factories, and the optional Vercel binding.
- `AppSessionHost` owns initial, five-minute, manual, and provider refresh. It
  publishes immutable state, evaluates alerts, cancels replaced work, waits for
  cleanup, and stops before its resources are released.
- `MainPage.xaml` fell from 1,924 to 280 lines. `FlyoutViewModel` fell from 1,620
  to under 350 lines. Dashboard and six option areas now bind to child modules
  and views.
- The split keeps all 97 XAML automation ID declarations and 85 distinct values.
  Architecture tests enforce those counts and the bounded shell files.
- `UsageQuery`, `LimitsQuery`, and provider diagnostics records live in Core.
  The Windows diagnostics query lives in Runtime.Windows. CLI code maps these
  typed facts to its existing wire files.

## Automated checks

`dotnet format WOpenUsage.slnx --verify-no-changes --no-restore` passed. The
workspace loader printed its existing non-fatal warning and returned exit code 0.

`./scripts/check.ps1 -Platform x64 -Configuration Release` passed twice. The
final run used the normal Release build and produced this result:

| Gate | Result |
| --- | ---: |
| Architecture | 67/67 |
| Core | 191/191 |
| CLI | 82/82 |
| Providers | 343/343 |
| Platform Windows | 115/115 |
| Total | 798/798 |
| Solution and MSIX package | passed, x64 Release |

Focused checks also passed:

- App session host: 5/5, including delayed provider cleanup during stop;
- WinUI surface modules: 9/9;
- App project build: Release/x64, 0 warnings and 0 errors.

## Packaged UI proof

The x64 Release package was rebuilt with the repo's `EnableUiTestFixtures=true`
flag only for UI automation. No credential value was read or changed. The final
script at `.scratch/ui/architecture-f9-f13/ui-tests.ps1` passed 10/10:

- dashboard load;
- options home;
- General, Appearance, Panel, Providers, Vercel, and provider-status views;
- focus on each navigation target;
- focus and dashboard state after the full back path.

Nine screenshots cover those states at 480 DIPs, rendered as 600 by 900 pixels
at the active scale. Visual review found no overlap, cut text, stray horizontal
scroll, missing right-edge controls, or theme faults. Panel and dashboard use
their expected vertical scroll regions.

## Safety and limits

- `git diff --check` passed.
- Secret patterns across tracked changes and 40 untracked source files returned
  zero private keys, credential assignments, OpenAI keys, and GitHub tokens.
- No unfinished `TODO`, `FIXME`, `HACK`, or `NotImplementedException` marker was
  added in the new modules.
- ARM64, real provider accounts, signing, publishing, commit, and push remain
  outside this task.

# Ticket 06: tray recovery and single-instance activation

Date: 2026-07-22

## Outcome

TokenUsage now registers the Explorer `TaskbarCreated` message and restores its
notification icon with the existing icon handle and window subclass. The app
also chooses one `AppInstance` before WinUI creates a window. Later launches
redirect to that instance and request its flyout.

The startup design follows Microsoft's WinUI single-instance guidance: disable
the generated XAML entry point, decide redirection before window creation, and
use `RedirectActivationToAsync` for later activations:

- <https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/applifecycle/applifecycle-single-instance>

## Automated evidence

- `dotnet test tests/WOpenUsage.Platform.Windows.Tests/WOpenUsage.Platform.Windows.Tests.csproj -c Debug -p:Platform=x64 --no-restore`: 56 passed.
- `BuildAndRun.ps1 ... -SkipRun /p:Platform=x64 /p:Configuration=Debug`: 0 warnings, 0 errors.
- `tests/ui/ticket-06-single-instance.ps1`: a second packaged activation kept
  the first PID and exposed its flyout.
- Ticket 05 tray regression: 7 relevant checks passed, including tray startup,
  native menu commands, automation IDs, Escape, and clean process exit. Five
  old empty/loading-state checks failed because the current app had real
  provider data; those checks do not cover this ticket.

## Proof boundary

The Explorer restart path has focused policy tests and static review. This run
did not restart Explorer, so live `TaskbarCreated` recovery still needs a
controlled desktop smoke test. Grok Build failed to finish two bounded reviews
because its file reader hit turn limits. Its partial output added no confirmed
defect after local review.

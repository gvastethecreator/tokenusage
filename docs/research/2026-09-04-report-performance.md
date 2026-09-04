# Report performance and recovery

## Changes

- Sum cycle tokens in SQLite instead of loading complete usage events. The repository
  owns the aggregate, UTC boundaries, agent filter, and checked token arithmetic.
- Run report reads and aggregation off the WinUI thread. Keep presentation changes
  on that thread. No new service, interface, database index, or schema migration.
- Cancel superseded loads, check cancellation before applying their results, and let
  each load dispose its own cancellation source. Filter choices made during loading
  now start a new load instead of being ignored.
- Keep the last loaded report when a refresh fails, with a clear warning and a keyboard
  accessible retry button. A failed filter change hides the old report so its values
  cannot be mistaken for the newly selected range.

[Microsoft's SQLite guidance](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/async)
confirms that this provider executes its asynchronous database methods synchronously.
`ConfigureAwait(false)` alone does not move that work off the calling thread.

## Measured result

Local comparison on 2026-09-04, using the same synthetic database with 100,000 Codex
events. Each event has all five token counters; the three-day query includes them all.
The measurement calls `LocalUsageRefresh.SumTokensSinceAsync`, warms it once, then
records five elapsed times and process-wide managed allocation deltas. Database
creation and ingestion are outside the measurement.

| Measurement | Before | After |
| --- | ---: | ---: |
| Median time | 844.87 ms | 163.14 ms |
| Median allocated bytes | 91,714,392 | 9,368 |
| Total tokens, every run | 18,500,000 | 18,500,000 |

This query was about 5.2 times faster in this experiment. It is not a whole-app speedup
or a prediction for other machines. The five timing samples were 805.06, 867.35,
831.76, 844.87, 854.37 ms before; 158.15, 163.14, 163.12, 163.25, 175.28 ms after.
The local script and logs are `.scratch/measure-cycle-query.cs` and
`.scratch/cycle-query-{before,after}.log`.

## Verification

`scripts/check.ps1 -Platform x64 -Configuration Release` passed:

- Architecture: 102. Core: 261. CLI: 130. Providers: 625. Windows platform: 174.
- Total: 1,292 passed, none skipped.
- Full solution and MSIX build passed.
- The first run stopped at the existing automation-ID contract because the error
  message and retry button added two public IDs. The contract now includes both names
  and the corresponding counts. The complete rerun passed.
- One repository regression covers all five counters, an inclusive start, exclusive
  end, other-agent exclusion, and an empty result through a read-only connection.

Native verification used the compiled report page and view model in a disposable
WinUI executable, with synthetic data and a controlled refresh callback. The app
assembly matched the verified Release build. The diagnostic package was separate
from the user's installed TokenUsage package. No provider hooks, credentials, live
inference, or real collection settings were changed.

At one 1120 by 900 desktop window:

- The report rendered its tokens, costs, chart, provider, and model breakdown.
- A simulated refresh failure kept the report visible and showed the warning.
- The retry button received keyboard focus. Enter recovered the report and removed
  the warning.
- A held refresh was superseded by day, provider, and all-history selections. Its
  later failure did not replace the current report or show an obsolete error.
- During a real SQLite exclusive lock on the synthetic store, the token metric
  remained selectable. The UI automation invocation returned in 269 ms while the
  database read was still waiting. The later timeout preserved the visible report.
- A filter change under the same lock showed the read error without stale values.
  Retrying after the lock was released restored the selected report.

Screenshots and state logs are under `.scratch/report-probe/`, including
`after-loaded.png`, `after-error.png`, and `after-filter-error.png`.
The baseline diagnostic encountered a WinUI reentrancy crash with its immediately
completed refresh callback; the updated assembly completed that same path. This
does not establish the behavior of every full-app startup path.

## Limits and deferred findings

- ARM64, other display scales, screen-reader speech, and live provider refresh were
  not exercised. These changes did not add a responsive layout or change release settings.
- Exact-cycle detail reports still read individual events to preserve cost and
  coverage semantics. Only the scalar cycle-token query received a SQL aggregate.
- Startup composition still blocks on collection settings and performs hook setup
  synchronously. Moving those operations requires a separate lifecycle change that
  preserves the user's collection policy and real hook registrations.

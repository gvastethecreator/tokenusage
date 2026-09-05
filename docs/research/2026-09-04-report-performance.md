# Report performance and recovery

## Report redesign implementation (September 5)

The report now uses an integrated title area with the native Windows caption
buttons. Refresh and share sit before the scope tabs. The compact toolbar places
Share, Report, and the visualization selector in that order. Captures show the
report title and period on the left and the TokenUsage brand on the right. They
hide navigation controls, focus, and chart tooltips, and restore the UI in finally.

Daily bars and provider grouping are the new defaults. Smooth curves, straight
lines, steps, grouped bars, and stacked areas use the existing native renderer
with linear axes. Comparison areas have independent baselines. Model grouping
keeps one panel per provider and ranks shades by the selected metric and period.
Names and values accompany each color. The existing gpt-reserve identifier is
displayed as Luna Reserve with a moon, and remains unpriced.

Daily model aggregates reuse the existing rollups. Active days count distinct
dates with positive token totals. Model, source, and day tables sort raw values,
keep ties stable, and place unknown costs last in either direction. Model rows
are no longer limited to 30. Row identities and the selected sort survive refresh.
There is no database migration and no change to the public CLI JSON.

Appearance schema 5 preserves existing preferences and adds chart style and
grouping. Notifications have a separate view model and category using the existing
alert store. Background collection is a switch with an explanation that it works
after tasks even when TokenUsage is closed. The open-app refresh interval remains
separate. Marker movement is coalesced at 40 ms and eased over 220 ms; row movement
uses 240 ms. Keyboard selection and reduced-motion state changes are immediate.

Local Tabler outline vectors are pinned to
[v3.46.0](https://github.com/tabler/tabler-icons/tree/v3.46.0/icons/outline), with
their MIT attribution in THIRD-PARTY-NOTICES.md. The header follows
[Microsoft's title-bar contract](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/title-bar).
The composition legend uses the native
[wrapping layout](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.uniformgridlayout?view=windows-app-sdk-1.8).
The improve-win-ui pass guided native caption behavior, theme-aware icons,
accessible sorting, reduced motion, and the normal/narrow visual checks.

### September 5 verification

The x64 Release checks passed: Architecture 106, Core 262, CLI 130, Providers 625,
and Windows platform 174. Total: 1,297, none skipped. The first architecture run
found the changed automation-ID count; its repaired contract passed the focused
rerun (200 occurrences, 185 distinct IDs). The solution/MSIX build passed, and the
package build was repeated after the native UI fixes. No build warnings remained.

The added coverage checks daily model totals, duplicate dates, provider isolation,
zero consumption, more than 30 models, raw numeric order, unknown prices, shades,
bar/step/area geometry, comparison baselines, and appearance migration/persistence.
Existing CLI and provider suites remained unchanged in scope.

Native checks used the real report window in the existing isolated probe and then
the installed application. The probe uses 36 models, two providers, repeated dates,
an unpriced reserve, and a zero-token model. It does not replace the user's database.
The installed app retained its LocalState database and preferences. Both application
files and package resources were compared against the final MSIX payload.

Verified paths:

- Five rendered styles and saved choices in both the probe and installed app.
- Provider/model panels, names and shades, Luna Reserve, and the full model table.
- Numeric and date sort direction, active days, unknown costs, and refresh retention.
- UI Automation names, keyboard focus, chart Home/End navigation, and sort ItemStatus.
- Native maximize/restore and the Snap layout popup.
- Light and dark themes; 1280 by 900 and 820 by 900 installed windows at 96 DPI.
- Complete installed-report capture, correct branding, and restoration afterward.
- A controlled clipboard lock produced a capture error and restored the share button
  and report controls. The lock was released.
- Windows client-area animations were temporarily disabled, checked, and restored
  to their original enabled setting. The final chart and sorted rows remained usable.

The narrow check found overlapping provider labels and a composition bar that kept
its old width. The legend now wraps below its heading, and the bar derives its
width from the surrounding layout rather than its previous content size.

Local evidence is under .scratch/report-probe/: installed-options.png,
installed-{smooth,line,step,area,bars}.png, installed-models.png, installed-table.png,
installed-narrow-final.png, redesign-sort-final.png, and redesign-no-animations.png.
redesign-marker.mp4 and redesign-sort.mp4 include timestamped 30 fps frame evidence.
The capture cadence is not an application frame-rate benchmark. Report PNG exports
are in the configured Downloads folder; screenshots and usage data are not committed.

### Verification limits

Only 96-DPI / standard text scaling was available for the native checks. Other DPI
and text scales, high-contrast rendering, and spoken screen-reader output are not
verified. Windows later refused foreground activation, so title-bar dragging,
double-click, system-menu mouse gestures, and a fully isolated rapid-interruption
motion pass could not all be completed. Native caption controls remain responsible
for those shell behaviors; this is not a claim that each gesture was exercised.
Live provider integrations outside the existing collection path and ARM64 were not
tested in this change.

### Local delivery

The registered development package is now at
.scratch/local-report-update-reviewed, deployed from the final x64 Release MSIX.
Windows reports the package healthy. All 245 application payload files and the root
resources.pri match the extracted MSIX. LocalState was not deleted, and the existing
usage database remains in place. Report preferences were returned to bars/provider;
the user's other appearance and collection settings were retained.

The final narrow screenshot confirms both wrapped labels and correctly sized
composition segments. The two affected report/automation structure checks passed
again after the layout correction. Tests are not duplicated in the total above.

Changes are grouped into four commits: report data/charts; window header/icons;
table/motion; and options with this evidence. The destination is origin/main.
The pushed revision and remote CI run are reported with the delivery handoff.

### Remote build follow-up

The four feature commits reached origin/main at 247d917. The
[Pages deployment](https://github.com/gvastethecreator/tokenusage/actions/runs/33992479685)
passed. The [first CI run](https://github.com/gvastethecreator/tokenusage/actions/runs/33992479583)
passed all 1,297 tests, then failed during packaging with NETSDK1032: win-x86 did
not match the requested x64 platform. The baseline run at 529fc94 had the same error.

The app project selected its runtime from the MSBuild process architecture. It now
selects win-x64 for Platform=x64 and win-arm64 for Platform=ARM64. This change does
not add an x86 target. The existing packaging contract test now checks both mappings;
its focused run passed. MSBuild property evaluation also returned the correct runtime
and platform target for each supported platform. The repeated local x64 Release
solution/MSIX build passed without warnings. ARM64 execution remains untested.

The fix-ci pass kept this correction limited to the project setting and its existing
contract test. The corrective push uses the same
[CI workflow](https://github.com/gvastethecreator/tokenusage/actions/workflows/ci.yml);
its final remote result is included in the delivery handoff.

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

## Local pricing diagnosis and report clarity

The running user installation still used
`.scratch/winapp-health-alerts-20260903-0040/TokenUsage.App/TokenUsage.App.exe`.
Its app assembly was dated September 2, before the Astra catalog update. The current
source and Release build therefore did not describe the executable being tested.
The September 4 report showed 154.5 million Astra tokens with zero price coverage;
the old UI presented the missing price as `$0.00 USD` and `0%` cost share.

The report now distinguishes an unavailable price (`Unpriced`, with no cost share)
from a provider-reported zero cost. Totals, provider, model, day, and comparison
cost displays use the existing nullable-cost rule. Model shares now follow the
selected metric: token shares use tokens, not costs. The headings say `Cost share`,
`Token share`, `Known cost`, and `Priced tokens`; summary tooltips explain that
missing prices do not mean free usage. Layout and motion remain unchanged.

Verification for this follow-up:

- The 102 existing architecture and localization checks passed against the changed
  sources. The x64 Release package build passed. Core and provider code did not
  change, so their previously passed suites were not repeated.
- The existing native diagnostic host loaded the compiled report page with three
  synthetic events: 100 tokens at $2, 300 with no price, and 100 with a reported $0.
  These are presentation fixtures, not assertions about Astra's actual tariff.
- Native output showed $2 known cost, 500 tokens, and 40% priced tokens. The unknown
  model showed `Unpriced` and a dash for cost share; the zero-cost model showed
  `$0.00 USD` and 100% priced tokens. Token mode showed shares of 60%, 20%, and 20%.
- UI Automation exposed the new headings and values. Keyboard focus and Space
  selected the cost metric successfully. Rendered labels and columns were legible.
- The diagnostic app assembly matched the Release assembly SHA-256:
  `C81923061B052555746F217B52F9B8EED9CB9784F200569BCE5666170305BAFB`.

Capture context: the user's original report was a 1351 by 1000 window with real
last-day data. The separate diagnostic report was 1120 by 900 with synthetic
30-day data. Both were on `\\.\DISPLAY2`, at 96 DPI, in dark mode, without high
contrast, using English app text and Spanish window chrome. Windows had no
configured text-scale registry override; increased text scaling was not tested.
Both used development package registration. These captures establish the stated
values and layout in each host, not a pixel-matched comparison of the same data.
Evidence is in `.scratch/pricing-zero-before.png` and
`.scratch/report-probe/pricing-{cost,tokens,cost-final}.png`, with adjacent UIA trees.

Updating and restarting the user's installation still requires the requested
confirmation. No real refresh, historical repricing, or installed-build correction
is claimed yet. Other themes, DPI settings, increased text sizes, Narrator speech,
and ARM64 were not exercised. Busy/error behavior was unchanged and retains the
earlier evidence above; the empty state also rendered in the diagnostic host.

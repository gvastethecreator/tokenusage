# Better usage measurement: implementation plan

Date: 2026-09-07. Status: local implementation approved by Cristian on 2026-09-07; M0-M4 implemented locally; M5 local checks passed, with release qualification limits recorded below.

## Outcome and scope

Help users answer four questions with the data TokenUsage can actually observe:

1. How much usage and known cost did I accumulate, and how much remains unpriced?
2. What changed this week compared with the previous week?
3. Which models contributed to the change, and what changed within the same model?
4. How do 2–4 reset cycles compare at the same elapsed time, and which provider does each represent?

Cristian confirmed that post-reset sessions means **cycles between resets**, not
conversations. Conversation identifiers, task tracking, and content collection
are outside this plan.

Keep the current WinUI report, native charts, SQLite store, collectors, and
Core/Presentation/App/CLI boundaries. Reuse the existing comparison paths.
The first useful release is weekly spending and model contribution, not a new
analytics platform. Begin collecting quota evidence early; release normalized
quota comparisons only when that evidence supports them.

## Baseline and research

- Product baseline: `27d36e2b95aeb8ef4111f02233143ca6e2e87aaf` on `main`.
- [PR #60](https://github.com/gvastethecreator/tokenusage/pull/60) was open at the planning review. Its documentation is research, not implemented behavior.
- Pinned [audit](https://github.com/gvastethecreator/tokenusage/blob/eaa2965c76206fcb28da1631dbabf89a1ad500b9/docs/research/usage-comparison/AUDIT.md), [contracts](https://github.com/gvastethecreator/tokenusage/blob/eaa2965c76206fcb28da1631dbabf89a1ad500b9/docs/research/usage-comparison/RFC.md), and [22-ticket backlog](https://github.com/gvastethecreator/tokenusage/blob/eaa2965c76206fcb28da1631dbabf89a1ad500b9/docs/research/usage-comparison/BACKLOG.md).
- Existing rules: [pricing](../PRICING.md), [provider coverage](../PROVIDER-MATRIX.md), [contribution scope](../../CONTRIBUTING.md), and [testing](../CONTRIBUTOR-TESTING.md).

The prior review checked production source and eight synthetic research
counterexamples. These support prioritization; they do not replace C# regression
tests or installed-app verification. Recheck the baseline before implementation.

## Measurement rules

| Measure | Meaning and display rule |
|---|---|
| Provider-reported cost | Preserve the source's financial meaning. A reported usage value is not automatically an invoice or cash charge. |
| Estimated API value | Tokens priced using the applicable host/model catalog. Label it as an estimate, never subscription spending. |
| Actual charge | Display only when an admitted source explicitly reports a charge and its billing period. No new billing integration is included. Otherwise show unavailable. |
| Unpriced usage | Keep its tokens and model visible. Unknown price is not zero cost. Luna Reserve remains unpriced unless verified evidence changes that. |
| Price coverage | Priced tokens divided by eligible observed tokens, with numerator and denominator available. It does not measure how much account activity was collected. |
| Collection coverage | Source, supported interval, freshness, known gaps, and precision. Do not invent a completeness percentage without a known denominator. |
| Quota usage | Keep the current used level separate from consumption during an interval, replenishment, and resets. Quota points are not USD. |
| Average cost per million tokens | Use cost and tokens from the same priced cohort and cost meaning. Show exclusions; never divide partial known cost by all tokens. |

Local events and official account-day totals are overlapping views, not additive
sources. Show them separately. Do not distribute an account total across locally
observed models. Compare their discrepancy only when period, timezone, units,
and source scope match; a discrepancy is not automatically missing local usage.

Every comparison uses A = baseline. Pair comparisons use B = current; cycle
comparisons can also include C and D. Each change is the selected value minus A;
relative change is `100 * (B - A) / A` only for a positive baseline. With a zero
baseline show the absolute change and new activity, not infinity. With missing
inputs show an unavailable result and its reason.

## Delivery sequence

M0–M4 are implemented locally. M5 local verification passed within the limits
recorded in the closeout. The sections below remain the acceptance contract,
not a claim that every production or OS qualification check has passed. Each milestone produces a bounded, reviewable outcome. The usage foundation in M0/M1 unlocks M2: the first useful
release already shows weekly totals and model contributions. M3 then adds direct
model comparisons and price controls. M4 adds post-reset comparisons after enough
evidence exists. M5 is the release gate for each accumulated delivery, not a
reason to defer all verification until the end.

### M0 — Protect history and stop misleading results

**Value:** collecting or refreshing data must not erase the baseline used for comparison.

- Replace generic parser retirement with explicit, successful replacement evidence. A failed or empty source cannot authorize deletion.
- Preserve aggregate-only historical dates when retention and reconciliation run together. Rebuild only ranges with authoritative replacement data.
- Reject stale complete snapshots before they can remove or recreate quota pools. Preserve pool identity and authoritative reset-credit counts even when detail lists are capped.
- Distinguish unavailable history from empty history. Keep a last-good result visibly stale; do not silently return zero.
- Immediately withhold the current tokens-per-quota-point result when it uses a final level as interval consumption or lacks matching pool attribution. Keep descriptive totals visible.

**Acceptance:** refresh, parser changes, stale snapshots, and recoverable read
failures cannot manufacture a saving, a reset, an empty history, or a complete
comparison. Closed cycles are not automatically measurement-complete.

**Research mapping:** TU-CMP-015, 016, 017, 018, 021, 022; conservative restrictions from 006 and 020. Keep these as separate fixes where their failure classes differ.

Parser/retention safety and honest cost/coverage states gate weekly comparisons.
Snapshot ordering and pool identity gate journal/cycle work. Reset-credit detail
fixes are useful but must not delay the spending release.

### M1 — Preserve the evidence needed for comparisons

**Value:** model changes and reset boundaries can be measured from observations, not reconstructed from daily totals.

- Extend existing usage and cost contracts with time precision, source attribution, field availability, and interpretation revision. Do not create a second cost abstraction.
- Preserve Codex numeric deltas before daily projection. Give them stable identities, source time support, and the model observed for that delta. A delta is not necessarily one request.
- When a delta spans an unknown model transition or reset boundary, mark its attribution ambiguous. Do not assign accumulated counters to the final model or invent the missing timing.
- Store official daily usage as an account interval with explicit timezone and scope. Keep it separate from local observations and remove proportional model allocation.
- Start a bounded quota-reading journal through the existing authorized refresh path. Record pool identity, source/received times, raw units or percent, meter precision, reset schedule, window semantics, and collection gaps.
- Make observation persistence and scanner checkpoints recoverable together. Repeated scans, resume, file rotation, and interrupted writes must not duplicate usage.
- Migrate only TokenUsage-owned data. Preserve originals recoverably and record which historical ranges have daily-only versus finer evidence. Do not relabel legacy aggregates as exact events.

**Acceptance:** unambiguous observations of 600 tokens at 09:00 and 400 at 13:00
split correctly around 11:00; refresh preserves that split. A daily-only record
cannot support the same split. Overlapping local/account data is never summed
twice. Model changes do not reassign earlier usage. Quota collection gaps remain
visible, including while collection is disabled or cannot run.

**Research mapping:** 001, 002, 003, the journal part of 004, and migration/replay safeguards from 014.

Ship the usage foundation and quota journal as independent units. Start the
journal early to accumulate observations, but do not make its completion a
dependency of M2 or M3. Exact cycle analysis starts only where evidence exists;
older daily history remains useful for supported daily and weekly summaries.

### M2 — Weekly spending with a clear baseline

**Value:** answer “what changed this week?” without confusing more elapsed time with more intensive usage.

- Add presets to the existing Compare view: last complete calendar week versus the previous week; current week versus the same elapsed part of the previous week; rolling 7 days versus the preceding 7 days.
- Use Monday-start calendar weeks and the selected report timezone. Show both date ranges. Keep rolling weeks explicitly distinct from calendar weeks.
- Use half-open intervals and a shared supported cutoff. If a source supports only complete days, compare the same completed weekdays and display that limit instead of inventing a partial-day value.
- Show A, B, absolute change, and relative change for tokens, separate known-cost categories, active days, priced tokens, and cache composition. Put freshness and collection limits beside the result.
- Show contribution to the cost/token change by provider and model, with an explicit unknown/unattributed row. Explain that contribution identifies where the change occurred, not its cause.
- Reuse a pure comparison policy/calculator in Core, adapted to the report. Keep current CLI contracts unchanged unless a separately approved version is needed.

**Acceptance:** a Wednesday result is not silently compared with seven full days;
timezone boundaries and DST are explicit; baseline-zero and missing-cost states
do not produce false percentages. Active days count observed dates with positive
tokens, not days the app was open. Refresh retains the chosen comparison.

**Research mapping:** the descriptive subset of 006, 007, and 010. This release does not depend on full quota attribution.

### M3 — Explain model changes and price effects

**Value:** distinguish using a model more often from its observed cost per token changing.

- Add model A/B selection within the existing Compare view. Support both the same model across periods and two models within a common period.
- Show model token share, cost contribution, cache read/write and uncached/output composition, active days, and cost per million priced tokens. Label any elapsed-hour rate as elapsed time, not active work time.
- Retain observed versus canonical model identity and available effort/service-tier metadata. Missing configuration stays unknown; do not infer it from a friendly model name.
- For an observed model transition, show the usage timeline around it only at the source's supported precision. A daily-only provider supports daily mix changes, not an exact switch time.
- Keep as-recorded cost as the default. Add an explicit fixed-price reference view to compare volumes and model mix without catalog changes obscuring the result. Apply each model's own rates from that reference, not one price to all models.
- Pin the catalog, filters, intervals, data revision, and exclusions of a saved comparison. Recalculation creates a named revision rather than silently changing a saved result.
- Use the common priced cohort for price-normalized comparisons. If request-size or tier evidence is missing, exclude/disclose the affected amount; do not reconstruct it from a daily total.

**Acceptance:** a catalog update does not rewrite a saved baseline. An unpriced
model cannot rank as free. Two models with different workload/cache/configuration
mix are shown descriptively, not declared equally effective or causally cheaper.
Contribution rows reconcile with their displayed total and unknown remainder.

**Research mapping:** selected parts of 008, 009, and 010. Defer advanced cohort matching, statistical drift, and causal explanations.

### M4 — Compare cycles between resets

**Value:** answer “how much have I used since this reset compared with the previous one?”

- Use the quota journal to derive cycle boundaries, current levels, evidenced consumption, and replenishment separately. Distinguish scheduled reset, observed reset, manual/credit cause when reported, and unknown cause.
- Keep historical pools and entitlement/window epochs. Do not filter old cycles using only today's pool membership. Do not invent cycles across missed observations or extend an old boundary into a false long cycle.
- Compare 2–4 distinct cycles against A using the shortest supported elapsed prefix across the set. Display the provider, pool, elapsed duration, supported UTC cutoff, gaps, and provisional status. Codex is currently the only cycle provider. Limit the selector to available history; never duplicate a cycle to fill a slot.
- Align chart buckets to each reset, not calendar midnight. Save all selected reports, provider identities, exact cutoffs, and evidence in one immutable revision.
- For a known boundary `S` and shared supported duration `d`, align each side to `[S, S + d)`. A cycle's first observation halfway through is not evidence for its missing first half. A daily aggregate crossing a boundary is not an exact cycle contribution.
- Separate fixed reset windows from rolling windows. Match provider, pool, window semantics, and known entitlement epoch before showing normalized deltas. Unknown profile continuity stays unknown without storing account identifiers.
- Show tokens, cost categories, model mix, current used quota level, replenishment, and evidence status. Only expose quota points per million tokens and its inverse when interval consumption and corresponding usage-to-pool membership are evidenced.
- Support many-to-many pool membership when a usage observation consumes multiple independent pools. Never match one pool against all provider tokens by default or sum percentages across pools.
- If pool attribution or quota evidence is unavailable, show usage and quota as separate descriptive series with a reason. This is a valid useful result, not zero efficiency.

**Acceptance:** in a fully evidenced fixed-capacity example, 0 -> 40 -> 20 -> 60
means 80 points consumed and 20 replenished, not 60 consumed. Sampled readings
alone do not establish that accounting when refills, rounding, rolling expiry,
or collection gaps are unknown. With the same 1M matching tokens, 20 -> 30
consumed points means +50% intensity and -33.3% inverse efficiency. No test or
comparison consumes an actual reset credit.

**Research mapping:** remaining 004, 005, 006, 007, 019, 020, and cycle UI from 010.

### M5 — Verify, document, and release the selected scope

**Value:** useful comparisons remain correct after refresh, migration, retention, and restart.

- Extend existing tests for each distinct uncovered failure. Prioritize production C# parser -> repository -> query tests for granularity, replay, and retention; use pure calculator tests for arithmetic and eligibility.
- Test source failure separately from no activity, unsupported history, partial coverage, stale data, and zero denominators. Include model changes, provider overlap, delayed readings, reset boundaries, and a week crossing DST.
- Verify migration interruption and older/newer schema handling on sanitized stores. Do not test against or overwrite the user's live database.
- Measure ingest time, report-query latency, memory, and store growth against the current implementation with the same sanitized corpus. Record the proposed retention/budget before enabling the new journal broadly. Use incremental writes, indexed interval/model/pool reads, and cancellation; no full log scan on chart hover or selection.
- Verify the packaged report with real supported states: keyboard, accessible labels, loading/error/partial states, preserved selection, narrow layout, text scale, themes, and reduced motion. Do not use color alone for evidence quality.
- Run `scripts/check.ps1 -Platform x64 -Configuration Release` at each substantial release boundary, reusing unchanged proof and rerunning only affected failures. Record runtime limits rather than claiming fixture tests prove provider support.
- Update pricing/coverage documentation where behavior changes. Keep user data, paths, and private screenshots out of published evidence.

Release the spending slice through M2 once its acceptance is met; do not wait
for M3's fixed-price controls. M3 and M4 follow independently after their own
prerequisites; cycle totals do not depend on fixed-price comparisons. Do not wait
for advanced statistics or broad provider parity.
Installation, commit, and push require authorization for that implementation
scope. Do not redeem credits, run paid workloads, or enable new collectors as
part of verification.

## Code ownership and review units

| Existing seam | Planned responsibility |
|---|---|
| `TokenUsage.Core/Usage/UsageRepository.cs` and `LocalUsageRefresh.cs` | Safe replacement/retention, observation storage, revisions, indexed queries. |
| `TokenUsage.Core/Usage/UsageEvent.cs` | Extend existing precision, provenance, and cost semantics without a parallel hierarchy. |
| `TokenUsage.Providers/Codex/CodexUsageEventSource.Scan.cs` and `.Map.cs` | Preserve numeric observations and keep account aggregates separate. |
| `TokenUsage.Core/Usage/QuotaResetHistoryStore.cs` and Codex rate-limit mapper | Ordered readings, historical identity, cycle evidence, reset inventory. |
| Existing `UsageReportCycleComparisonCalculator` | Move reusable calculation/eligibility into pure Core; Presentation formats results. |
| `UsageReportViewModel`, `UsageReportRequest`, and report page | Weekly/model/cycle selection, explanatory evidence, native UI states. |
| Existing Core, Providers, CLI, and architecture test projects | Focused regressions, public-contract preservation, and calculator parity where exposed. |

Suggested independently reviewable units: (1) parser retirement; (2) rollup
retention; (3) snapshot identity/order; (4) history availability and honest ratio
states; (5) cost/precision contracts and migration; (6) observation collection;
(7) quota journal; (8) weekly comparison; (9) model and fixed-price comparison;
(10) cycle derivation and eligibility; (11) cycle UI. Keep distinct inventory or
alias fixes separate when they have independent tests. Each unit carries its
own required tests/docs; do not collect all proof in a final catch-all commit.

Before opening implementation PRs, search and link the corresponding issue as
required by CONTRIBUTING. Research TU-CMP identifiers are not GitHub issues.
This local implementation creates no issues, PR comments, commits, or remote changes.

## Explicitly deferred

- TU-CMP-011: new OpenTelemetry source and integration work.
- TU-CMP-012: task outcomes, conversation correlation, quality benchmarks, or cost per successful task.
- TU-CMP-013: automated backend-change detection, statistical alerts, or causal model rankings.
- Predictive spending, subscription amortization, invented USD-per-quota conversions, currency conversion, and invoice import.
- New providers, hidden-account discovery, cloud storage, a separate dashboard, or a generic event platform.

These are not prerequisites for trustworthy weekly/model totals or useful
post-reset comparisons. Reconsider them only after the first releases show a
specific unanswered user question and a permitted source that can answer it.
## Local implementation closeout — 2026-09-07

M0-M4 are implemented through the existing Core, provider, report and storage
seams. Weekly/model comparisons support fixed reference pricing and saved
revisions. Cycle comparison supports 2–4 distinct cycles with A as baseline,
explicit provider identity and a shared supported elapsed interval. Unsupported
quota attribution remains unavailable, not zero. Saved revisions include the
whole selected set and its evidence.

The numeric store uses schema 5. Codex keeps timed observations and separates
account totals from model events. Quota history preserves ordered observations
and retired pools. The numeric quota journal runs off the UI thread and is
bounded to 90 days and 250,000 rows. No conversation content or identifiers
were added.

### Verification and limits

- The repository x64 Release gate passed: Architecture 106, Core 274, CLI 130,
  Providers 629 and Windows platform 174; 1,313 tests total, no failures.
  It generated the local MSIX. Log: `.scratch/tokenusage-measurement/final-release-check.log`.
- A separate native WinUI probe exercised synthetic data, 2/4-cycle selection,
  saved four-cycle reopening, model A/B, fixed reference pricing, refresh failure,
  narrow layout, light/dark surfaces and full PNG export. The usual report was
  1280 DIP wide; the narrow report was 760 DIP, including observed 150% DPI.
- The [experience audit](2026-09-07-experience-design/report.json) records the
  implemented polish, observed states, evidence and remaining verification.
  A later capture-only layout repair has its own package and native proof.
- Marker reversal and exit were recorded. Inspected frames show redirection
  and a cleared final hover, not a measured frame-pacing or user satisfaction gain.
- High contrast, OS reduced motion, enlarged text, screen readers, real credential
  storage, OS tray activation and the production shell are not fully qualified.
  Native fixture proof is not installed production-app proof.
- The installed main app and user database were not replaced or migrated.
  There were no paid workloads, reset redemptions, commits or remote writes.

### Synthetic performance evidence

Same 10,000-event, 12-model corpus; medians of three samples after warmup.
The final schema stores more metadata and runs more exact support checks, so
these figures are not an isolated optimization experiment.

| Measurement | Baseline | Current |
|---|---:|---:|
| Ingest | 433.82 ms | 373.87 ms |
| Daily + two-hour report | 107.30 ms | 132.90 ms |
| Exact seven-day report | 50.98 ms | 87.54 ms |
| Database size | 3,633,152 bytes | 4,382,720 bytes |
| Process working set | 70,877,184 bytes | 81,977,344 bytes |

The exact query became slower and the store grew about 20.6%; no performance
improvement is claimed. Logs are `perf-baseline.log` and
`perf-current-final-interval.log` in the task scratch directory.
A separate 2,000-observation journal run took 76,135 ms total (about 38.1 ms per
write); its read took 26.31 ms and its store used 544,768 bytes. The 250,000-row
limit was not benchmarked. This remains a scale-qualification risk before any
broad collector rollout.
## Competitor audit follow-up — 2026-09-07

The competitor report was checked against upstream changes and current local
code. It is a source of candidate failures, not a list of missing features.
TokenUsage already has pace, notifications, model tables, PNG capture and the
local comparison changes above.

Three confirmed behaviors were repaired:

- A zero quota level no longer produces a favorable pace forecast. The observed
  remaining quota and reset stay visible. Positive-usage extrapolation keeps its
  existing eligibility rules; this does not turn it into an observed rate ledger.
- Grok snapshot discovery uses `updates.jsonl`, not the presence of
  `summary.json`. A self-contained numeric snapshot can be counted alone.
- An explicit model on a Grok inference takes precedence over an older
  process-level model, preventing wrong model attribution and catalog pricing.

The tray uses separate named session and period metrics, not CodexBar's automatic
switcher. Regression cases cover zero in either window while the other remains
healthy. No product change to metric selection was needed.

Full fork/replay accounting is not claimed. TokenUsage's cumulative snapshot
format differs from the event-ID ledger in
[OpenUsage #1193](https://github.com/robinebers/openusage/pull/1193).
Do not deduplicate equal counters or mix unified events with overlapping session
snapshots. That could remove legitimate usage or count the same work twice.

No new providers, credentials, pricing rates, export formats or collectors were
added. The imported audit file was not changed. Verification passed: 1,316 tests across the existing suites and the x64 Release
solution/package build. The initial gate stopped on a test property typo;
after repair, Providers and the remaining stages passed without repeating the
successful suites. Logs are `competitor-followup-{check,providers,windows,package}.log`
in `.scratch/tokenusage-measurement`. This follow-up changed no visual layout;
projection tests cover the visible state. No real-account or installed-app
reconciliation was performed.

## Options refinement and local update — 2026-09-07

Options now use the same underlined tabs as the report. The content host animates
its height from the current position and keeps each panel's scroll state. Report
controls, cycle labels and numeric columns use a clearer shared alignment.
Native fixture checks covered dark and light themes, narrow layouts, keyboard
activation, rapid tab changes, weekly comparisons and two to four cycles.

The existing 1,316 passing tests remain the source gate. The final x64 Release
solution and MSIX build passed after the last layout change. Its log is
`.scratch/tokenusage-measurement/local-update-build.log`.

The local update uses package version `0.0.1.0`, application source `c17892a`,
and the existing `GVASTETHECREATOR.TokenUsage` identity. The MSIX SHA-256 is
`6d6d219af1de277a7ecc04ae595ea8f4036aae10030c4f45bea9d635d72502e3`.
This is an internal unsigned build, not a public release. Clean-machine install,
repair, uninstall, reboot, signature trust, high contrast, enlarged text and
Windows reduced-motion qualification remain untested.

## Compact limits and reset markers follow-up — 2026-09-07

Compact limits keep two columns. Names can wrap to two lines, reset times remain
visible, and the observed-token explanation moves into an accessible information
tooltip. The explanation no longer determines the uniform tile size. Native
synthetic captures cover 480- and 420-pixel windows in dark and light themes.

Report charts show a small top-edge marker for days with recorded quota resets.
The existing day tooltip and keyboard help include the provider, quota window
and local timestamp. Multiple resets share one day marker. Calendar comparisons
map each period separately; cycle comparisons use elapsed time and exclude the
next cycle boundary. Missing history produces no invented markers. The feature
reuses the reset ledger and does not detect new resets from chart values.

Two projection regressions passed, and the final x64 Release application build
passed with no warnings or errors. Native checks covered the combined and
provider charts plus keyboard access to reset details. Evidence is stored in
`.scratch/tokenusage-measurement/reset-markers-{tests,build}.log`,
`reset-markers-{native,tooltip}.png` and `compact-columns{,-narrow}.png`.
The installed application was not replaced for this follow-up. Additional DPI,
enlarged text, high contrast and a full package gate were not rerun.

## Options layout follow-up — 2026-09-07

Category icons now sit in the shared tabs, outside the content cards. Redundant
section headings are removed. Toggle labels and switches share a row. The Back
button shares the tab row; tabs size to their text so Notifications is not cut
off in the narrow layout. Navigation and panel height changes retain their
current visual position when interrupted and respect Windows motion settings.

The options contract checks passed. The final x64 Release build passed with no
warnings or errors. Native inspection covered the product window at 460 pixels
and a narrow options fixture at 420 pixels. The Back action returned to the
dashboard. Evidence: `.scratch/tokenusage-measurement/options-single-row.png`,
`options-single-row-narrow-final.png`, and `options-single-row-build.log`.
Earlier navigation motion was recorded in `options-window-motion.mp4`; the final
tab-row adjustment was checked with still captures. Reduced-motion runtime,
additional DPI and enlarged text remain untested. The installed app is unchanged.

## Report action placement — 2026-09-07

Coverage help, refresh and share now sit beside the report title. Chart style and
small-value scale sit before the chart view tabs. One shared control group moves
between Global, Provider and Compare, preserving commands and automation IDs.
Captures hide and restore both relocated action groups. The scale icon now follows
the toggle foreground so it stays visible in the light theme.

The x64 Release build and three existing report contracts passed. Native checks
used the separate MeasurementProbe package with synthetic data, 1280- and
760-pixel windows, English, light/dark themes and normal text scale. Checks covered
the three scopes, keyboard scale switching and control reuse after capture.
Evidence is under `.scratch/tokenusage-measurement/report-actions-*`; the shared
image is `C:/Users/cristian/Downloads/TokenUsage-report-2026-09-07-171615.png`.
No new motion, package installation or remote changes were made. Additional DPI,
enlarged text, contrast themes and forced capture failure were not tested.

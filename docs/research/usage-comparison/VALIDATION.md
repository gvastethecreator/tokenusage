# Validation and evidence ledger

**Baseline:** `27d36e2b95aeb8ef4111f02233143ca6e2e87aaf`. **Date:** 2026-09-06.

## What was checked

GitHub repository metadata, main commit, baseline comparison, branch list and open-PR list were read again. Main compared identical to the original audit revision: zero ahead, zero behind, no changed files. The critical reset/store/refresh/report/mapper paths and relevant existing tests were inspected at that pinned revision. The prior audit/RFC/backlog were read and consolidated rather than represented as new code.

The following standalone check was run with the Python standard library and an in-memory SQLite database:

```text
python reproduce_counterexamples.py
Ran 8 tests
OK
```

This reproduces reduced limitations at the audited baseline. A passing check means the counterexample behaves as documented, NOT that TokenUsage is fixed. It does not compile or execute the C# implementation, migration code, scheduler, provider processes or WinUI. The SQL fixture reduces columns to one agent/model/timezone and preserves the relevant delete/rebuild predicates; it is not a byte-for-byte production SQL dump.

| Counterexample | Demonstrated result |
|---|---|
| Precise timestamps versus daily noon | 600/400 across 11:00 becomes 0/1,000 with a synthetic daily point. |
| Refresh-time daily stamp | An unchanged daily total changes cycle membership. |
| Meter versus gross consumption | Fully known 0→40→20→60 produces 80 consumed while final meter is 60. |
| Aggregate-only history rebuild | Zero fine-event candidates, but broad delete/rebuild removes a retained 300-token rollup. |
| Parser-version predicate | Old v7 event is selected with no v8 replacement rows. |
| Equal values versus pool identity | Different pool IDs can have identical window readings. |
| Capped reset-credit details | min(5 reported, 2 detail rows) undercounts official inventory. |
| Inverse metric percentages | 20→30 points/M gives +50% intensity and -33.3% inverse. |

The arithmetic assumes only the synthetic conditions stated in each test. In particular, gross-consumption reconstruction in the script is not a generally valid polling algorithm.

## What was not run

The working environment did not have `dotnet`, WinUI or Windows packaging tools. An attempted public Git clone failed DNS resolution; repository reads/writes used the connected GitHub interface. No production build, xUnit suite, packaged app, live account comparison, performance benchmark, original database migration or user-history inspection was executed. Browser tests from the earlier prototype are not rerun or counted as validation of this PR.

## Required implementation evidence

Add regression tests to the existing suites rather than creating a parallel research-only definition of correctness. Start with `UsageRepositoryTests`, `LocalUsageRefreshTests`, `QuotaResetHistoryStoreTests`, `CodexRateLimitsSnapshotMapperTests`, `UsageReportCycleComparisonTests` and automation/report tests.

Storage tests must combine normal retention and parser supersession in both orders, test no-op and nonempty prune batches, and include a date with mixed retained/expired evidence. Ordering tests need complete/partial/empty snapshots, old observations and persistence across restart. Quota tests need distinct equal-valued pools, missing multiple windows, postponed expected resets, identity/entitlement changes and subset-pool activity. Timing tests need actual event timestamps, daily interval precision, UTC/DST, source-day definitions and delayed ingestion. Report tests need expired evidence, locked/corrupt/newer history, typed errors and CLI/UI consistency.

For every corrective implementation, demonstrate the baseline failure at the real C# seam, then the corrected behavior. Existing tests that encode old policies (generic fossil retirement, value-based default aliasing, effective credit count) require deliberate updates plus controls preserving the legitimate cases. Do not simply delete a disagreeing test.

Run the repository's existing contributor gate in an appropriate Windows environment:

```powershell
.\scripts\check.ps1 -Platform x64 -Configuration Release
```

Follow `docs/CONTRIBUTOR-TESTING.md` for affected ARM64/package, privacy and accessibility checks. Record commands, exact results and skipped checks. CI status must be read from the created PR; it is not inferred from this ledger. Merging documentation is not closing the implementation tickets.

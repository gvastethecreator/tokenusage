# Second-pass audit: measurement correctness before comparison features

**Date:** 2026-09-06. **Baseline:** `27d36e2b95aeb8ef4111f02233143ca6e2e87aaf`.

The remote main is identical to the first audit baseline. F01–F09 are retained findings, not newly introduced regressions. F10–F18 are additional findings from this pass. This is targeted static source review plus reduced analytical/SQLite reproductions, not an exhaustive execution of every repository path or a reproduction against a user's database. No production code is changed by this package.

P0 means protect data or prevent unsupported measurement claims before releasing normalized comparisons. It does not automatically mean a security vulnerability. Read the named symbols in the pinned sources; line ranges are navigation aids for this revision only.

## Existing strengths

`UsageEvent` carries parser/pricing versions; `CostObservation` distinguishes reported, estimated, and unavailable costs. Codex token normalization subtracts cache and reasoning subcategories already included in provider counters: this review does not assert a general double-counting bug there. SQLite uses transactions, idempotent keys, migrations and bounded retention; reset history separates unknown causes from official manual signals. `ReadExactAsync` correctly uses a UTC half-open interval. Keep those properties. [S1][S2][S3][S4]

There are already cycle comparisons, tokens-per-quota-point and price-per-million calculations, model/day reports, all-history selection, and an official pricing evidence catalog. Do not create duplicate features merely because they were not visible in a screenshot. [S5][S6][S7]

## Retained findings

### F01 — P0 — Daily aggregates masquerade as precise events

`CreateOfficialEvent` calls `DailyTimestamp`: past days use local noon; today uses observation time. `ReadExactAsync` subsequently filters these rows as point events. A correct daily total is not sufficient for an exact reset slice. [S2][S4]

Synthetic example: 600 tokens at 09:00 and 400 at 13:00, reset at 11:00. Correct split: 600/400. A 1,000-token noon row yields 0/1,000. Today's unchanged total can move between cycles on refresh because its timestamp changes. Mark daily interval precision and reject unsupported sub-day attribution; do not invent better point timestamps. Tests: exact boundary, same-day refresh, timezone and migration. Tickets 001–003.

### F02 — P0 — Model allocation is inferred from samples

`CreateOfficialEvents`, `Allocate`, `ScaleTokens`, and `ScaleSampleCost` distribute an account total according to local samples in one production path. Other paths prefer recent local totals or fall back to local samples. A generic `Partial` marker does not identify these different attribution methods. [S2][S8]

Changes in samples, client visibility, or remote activity can change the inferred model split without changed model behavior. Preserve local observed events and account aggregates as parallel, potentially overlapping series. Never silently distribute the residue across local models. Tests: source transitions, incomplete samples, external activity, conservation without double counting. Tickets 001–003.

### F03 — P0 — Useful deltas are collapsed too early

`ProcessRecentLine` already computes counter deltas and handles replay/regression, then accumulates into `checkpoint.Daily[(date, model)]`. Preserve an idempotent numeric observation before this aggregation. Do not rewrite the entire scanner or assume every counter delta equals a request. [S9]

Tests: model change in one session, identical counters, resume carry, child replay, file rotation, append after partial line, restart between event write and checkpoint advancement. Tickets 002 and 014.

### F04 — P0 — Final meter level is not interval consumption

`TokensPerQuotaPoint` divides tokens by `QuotaUsedPercent`; cycle observations supply a meter reading, not reconstructed gross consumption. Replenishments are recorded separately. [S3][S5]

Under fully known conditions, 0→40→20→60 represents 80 points consumed, 20 replenished and a 60% final meter. Positive-delta summation is not a universal fix: polling gaps can conceal simultaneous consumption/refill, rounding introduces noise, and rolling windows expire old activity. Expose the reconstruction method and bounds, or withhold the ratio. Tickets 004–007.

### F05 — P0 — Closed is treated as complete

`CreateCycleObservation` uses `!cycle.IsCurrent` as `IsComplete`. The comparison calculator uses `GroupId` equality for compatibility and still constructs several differences even for incompatible observations. Existing UI warnings do not establish temporal coverage, pool alignment or causal comparability. [S5][S6]

Separate lifecycle from measurement quality. A completed cycle may lack its beginning or include aggregate timestamps; an open cycle may support a provisional equal-exposure description. Unsupported normalized results must be null with reasons, not merely an alarming color. Tickets 006–007.

### F06 — P0 — No durable history of every quota observation

`QuotaResetHistoryStore` overwrites current window states and keeps bounded reset/replenishment lists, rather than every reading. Keys omit profile/entitlement epochs. This prevents replaying the full measurement and distinguishing a capacity change from a consumption change. [S3]

Keep append-only numeric observations with provider/pool identity, local profile epoch, source/received times, units, precision, expected reset and window semantics. Missing readings remain missing. A reset cause remains unknown unless supported by an authorized signal. Tickets 004–005 and 014.

### F07 — P1 — Comparison dimensions are lost

The common event contract does not persist separate raw/canonical model identities, effort, speed, service tier, timing precision or attribution method. `ModelIdentity` is a normalization, not proof of a backend revision. Preserve available numeric/configuration fields with field-level availability; do not convert an unexposed counter into a measured zero. [S1][S10]

Use optional local pseudonymous session/task relationships only after policy review. A model name is insufficient to control context, cache mix and client behavior. Tickets 001 and 008.

### F08 — P1 — Reinterpretation can look like consumption drift

Reconciliation/upserts replace events and rebuild projections. Parser and catalog versions exist, but historical comparisons are not immutable analytical snapshots. The 35-day reconciliation window is not by itself a retention policy; the additional cleanup behavior is examined in F10–F11. [S1][S7][S11]

Version the definition, measurement revision and price reference. Distinguish as-recorded from explicitly recomputed results. Compare price-covered cohorts consistently and show excluded data. Tickets 009 and 014.

### F09 — P1 — Analysis remains coupled to UI orchestration

The approximately 101 KB `UsageReportViewModel.cs` mixes selection, reads, comparisons, projections and formatting. A calculator already exists in Presentation; extend/extract at that seam instead of replacing the stack. [S5][S6]

Keep pure eligibility and comparison in Core, return typed results, and share them with UI/CLI. Introduce database revision/watermark-aware cache invalidation and stable A/B selections. Tickets 006 and 010.

## Additional findings

### F10 — P0 — Generic parser supersession can delete unreplaced history

`LocalUsageRefresh.RefreshAsync` derives active parser versions from configured windowed sources, independently of whether those reads yielded authoritative replacements. It calls `ApplyRetentionIfDueAsync` with `parserSupersessionDays = ReconciliationDays`. `ApplyParserSupersessionAsync` selects older rows by parser-version mismatch, tombstones/deletes them and rebuilds history. [S11, lines 688–829][S12, lines 328–356]

A version mismatch alone does not prove an event is obsolete, duplicated, or replaceable. A valid older event can be selected even with zero new-parser events or an unavailable source. There is an existing test that intentionally retires a known synthetic account fossil; preserve that valid cleanup case with an explicit rule rather than generic deletion. [S13, `ParserSupersessionRetiresOnlyOldEventsFromSupersededParsers`]

Require a supersession plan identifying obsolete representation, complete replacement coverage, affected keys and reconciliation evidence. Unknown older versions remain historical data with their original interpretation. No-data/error cannot authorize retirement. Regression: valid v7 history outside the window survives a v8 upgrade without backfill. Ticket 015.

### F11 — P0 — Rebuild can erase retained rollups even when zero rows were pruned

Ordinary retention deletes old fine events but intentionally preserves daily rollups. Supersession subsequently calls `RebuildAgentRollupsInRangeAsync(DateOnly.MinValue, cutoffDate)` on each loop, before checking whether any rows were deleted. `RebuildAgentRollupsCoreAsync` deletes the target rollups and recreates them solely from surviving events. [S11, lines 650–829 and 1203–1350]

If a historical rollup has no surviving fine events, that rebuild removes it. This can occur on a zero-deletion supersession pass and does not require a parser change. The broad rebuild also repeats work for each batch; no timing benchmark is claimed. Existing tests cover retention and supersession individually, but the interaction needs a dedicated fixture. [S13]

Preserve aggregate-only partitions; do not rebuild a partition from a known incomplete source. Limit corrections to proven affected keys/dates and the retained evidence horizon. A no-op prune must not rewrite history. Merely skipping zero-delete rebuilds is insufficient when another date actually contains deletions. The standalone script reproduces the reduced SQLite mechanism. Ticket 016.

### F12 — P0 — Stale complete snapshots can remove newer window topology

`ObserveCore` removes windows missing from a complete snapshot before its per-window out-of-order timestamp check. Existing checks protect values of retained metrics, not this deletion. [S3, lines 332–404]

Sequence: a newer complete snapshot contains primary+secondary; an older complete snapshot contains primary only. The secondary can be removed before the old primary reading is ignored. Likewise an old snapshot can recreate a removed metric because the new-key branch lacks prior state. Apply provider-snapshot ordering before membership reconciliation, including empty complete snapshots, and retain a provider watermark/tombstone sufficient to prevent resurrection. Test durable replay and restart. Ticket 017.

### F13 — P0 — Missing multiple windows can produce a misleading current cycle

`DetectChange` can infer a scheduled reset from crossing the previously expected boundary alone. `ObserveCore` then uses that old boundary as the current cycle start; it does not establish how many windows elapsed while no data was collected. `QuotaResetCycleQuery.Build` extends the current cycle to now. [S3, `DetectChange`, `ObserveCore`, `Build`]

A five-hour pool first reobserved much later can be paired with a multi-window token interval and only the newest meter. Also, a postponed schedule first observed after the old boundary needs explicit handling rather than a confirmed reset based only on the old expectation. Preserve unknown gaps and expected-versus-observed boundaries; do not invent a chain of resets. Add bounded timing and coherent current-window inference only when the source semantics support it. Extend ticket 005; evidence availability is covered by 018.

### F14 — P1 — Historical reset counts depend on today's active pools

`QuotaResetCountQuery.Summarize` filters reset records through `history.Windows` and the active window duration. A recorded reset can disappear from the count after its pool is removed or changes cadence, even though the record still exists. [S3, lines 200–250]

This may be useful for an explicitly named current-entitlement view, but it is unsuitable as the unqualified historical count in longitudinal analysis. Define separate all-recorded and current-epoch queries, label them, and preserve archived pool identity. Test both semantics so correcting history does not silently break the intended current-window UX. Ticket 019.

### F15 — P0 — A pool-specific report reads all Codex agent activity

`ReadResetCycleAsync` calls `ReadExactAsync(cycle.FromUtc, cycle.ToUtc, new AgentId("codex"), ...)`. It does not filter token events by the selected pool; `UsageEvent` has no pool mapping. Meanwhile the mapper supports additional named limit buckets. [S6, lines 735–754][S1][S14]

For a pool that meters only a subset of models/surfaces, all-agent tokens are an invalid numerator. A matching time interval does not establish pool membership. One request can legitimately affect several concurrent pools; model this as a relation, and never add their percentages into one total. When mapping is unavailable, show parallel descriptive series and block per-pool model intensity. Ticket 020.

### F16 — P1 — Equal window values are used as evidence of duplicate identity

The mapper skips any additional bucket when `HasSameWindows(default, bucket)` is true. It compares window records, not stable metered identity. [S14, lines 61–83 and `HasSameWindows`]

Two independent pools can temporarily have equal utilization, duration and reset values. Omitting one makes its existence value-dependent; a later unequal reading can reintroduce it, feeding unstable topology into reset history. The official protocol identifies the metered bucket through `limitId`/map keys. Preserve distinct identities; skip a proven default alias only with identity/contract evidence. Existing mirrored-default tests must remain valid. [O1]

Regression: independent IDs with identical readings remain independently visible, then separate without a fabricated reset. Ticket 021.

### F17 — P1 — Capped reset-credit details can undercount available inventory

`AddResetCreditMetrics` assigns `effectiveAvailable = Math.Min(inventory.AvailableCount, available.Length)` when detail rows exist. The official protocol explicitly permits capped details and treats `availableCount` as authoritative. With count 5 and two listed available details, the mapper emits effective availability 2. [S14, lines 104–150][O1]

Retain the official count; report observed details/expirations separately with list coverage. A minimum known expiry among returned details is not necessarily the next expiry across all credits. The existing test expecting a lower effective count encodes the old policy and requires a deliberate contract-aligned update. No reset redemption belongs in this work. Test null, empty, capped, expired and inconsistent detail lists. [S15, `ResetCreditsMapReportedEffectiveExpiredAndExpiryValues`]

Ticket 022.

### F18 — P1 — Reset-history read failures are displayed as empty history

`LoadResetCyclesAsync` catches IO, permission, timeout and `InvalidOperationException`, then assigns `QuotaResetHistory.Empty`. The schema-too-new exception derives from `InvalidOperationException`. Thus a locked, unreadable or newer-schema history can become indistinguishable from no recorded cycles at this surface. [S6, lines 1105–1130][S3]

Return a typed availability state, preserve the last reliable view where safe, show a retry/error explanation, and withhold comparison eligibility. Do not copy exception paths/secrets into UI exports. Fine-event retention also needs an explicit unavailable-evidence state rather than an apparently exact zero. Ticket 018.

## Scope and confidence

Code paths and named predicates above are directly observed at the baseline. Consequences are deductions, with reduced synthetic cases executed separately; no claim is made that the user's actual history was corrupted or that all providers exhibit every case. No newer main revision was found. Production C#/WinUI and account validation remain required. See [VALIDATION.md](VALIDATION.md).

[S1]: https://github.com/gvastethecreator/tokenusage/blob/27d36e2b95aeb8ef4111f02233143ca6e2e87aaf/src/TokenUsage.Core/Usage/UsageEvent.cs
[S2]: https://github.com/gvastethecreator/tokenusage/blob/27d36e2b95aeb8ef4111f02233143ca6e2e87aaf/src/TokenUsage.Providers/Codex/CodexUsageEventSource.Map.cs
[S3]: https://github.com/gvastethecreator/tokenusage/blob/27d36e2b95aeb8ef4111f02233143ca6e2e87aaf/src/TokenUsage.Core/Usage/QuotaResetHistoryStore.cs
[S4]: https://github.com/gvastethecreator/tokenusage/blob/27d36e2b95aeb8ef4111f02233143ca6e2e87aaf/src/TokenUsage.Core/Automation/UsageReportQuery.cs
[S5]: https://github.com/gvastethecreator/tokenusage/blob/27d36e2b95aeb8ef4111f02233143ca6e2e87aaf/src/TokenUsage.Presentation/ViewModels/Reports/UsageReportCycleComparison.cs
[S6]: https://github.com/gvastethecreator/tokenusage/blob/27d36e2b95aeb8ef4111f02233143ca6e2e87aaf/src/TokenUsage.App/ViewModels/Reports/UsageReportViewModel.cs
[S7]: https://github.com/gvastethecreator/tokenusage/blob/27d36e2b95aeb8ef4111f02233143ca6e2e87aaf/docs/PRICING.md
[S8]: https://github.com/gvastethecreator/tokenusage/blob/27d36e2b95aeb8ef4111f02233143ca6e2e87aaf/src/TokenUsage.Runtime.Windows/Providers/WindowsProviderCatalog.cs
[S9]: https://github.com/gvastethecreator/tokenusage/blob/27d36e2b95aeb8ef4111f02233143ca6e2e87aaf/src/TokenUsage.Providers/Codex/CodexUsageEventSource.Scan.cs
[S10]: https://github.com/gvastethecreator/tokenusage/blob/27d36e2b95aeb8ef4111f02233143ca6e2e87aaf/src/TokenUsage.Providers/Pricing/ModelIdentity.cs
[S11]: https://github.com/gvastethecreator/tokenusage/blob/27d36e2b95aeb8ef4111f02233143ca6e2e87aaf/src/TokenUsage.Core/Usage/UsageRepository.cs
[S12]: https://github.com/gvastethecreator/tokenusage/blob/27d36e2b95aeb8ef4111f02233143ca6e2e87aaf/src/TokenUsage.Core/Usage/LocalUsageRefresh.cs
[S13]: https://github.com/gvastethecreator/tokenusage/blob/27d36e2b95aeb8ef4111f02233143ca6e2e87aaf/tests/TokenUsage.Core.Tests/Usage/UsageRepositoryTests.cs
[S14]: https://github.com/gvastethecreator/tokenusage/blob/27d36e2b95aeb8ef4111f02233143ca6e2e87aaf/src/TokenUsage.Providers/Codex/CodexRateLimitsSnapshotMapper.cs
[S15]: https://github.com/gvastethecreator/tokenusage/blob/27d36e2b95aeb8ef4111f02233143ca6e2e87aaf/tests/TokenUsage.Providers.Tests/Codex/CodexRateLimitsSnapshotMapperTests.cs
[O1]: https://learn.chatgpt.com/docs/app-server

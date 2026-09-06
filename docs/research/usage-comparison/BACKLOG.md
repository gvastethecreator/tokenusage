# Development backlog — 22 proposed tickets

**Status:** none of these implementation tickets is completed by this documentation PR. IDs are local planning IDs, not GitHub issue numbers. Baseline `27d36e2b95aeb8ef4111f02233143ca6e2e87aaf`. Findings reference [AUDIT.md](AUDIT.md); contracts reference [RFC.md](RFC.md).

Preserve TU-CMP-001–014 from the initial audit. Add 015–022 rather than renumbering previous work. Before changing public contracts or adding a provider, follow the repository requirement to open the corresponding implementation issue. Do not create one giant runtime rewrite.

## Delivery order

| Wave | Tickets | Observable outcome |
|---|---|---|
| 0 — Protect evidence | 015, 016, 017, 021, 022; immediate UI restrictions from 006/020 | No destructive generic history cleanup; stable snapshot/pool identity; inventory semantics respected; unsafe conclusions blocked. |
| A — Precise usage | 001, 002, 003 | Real local deltas survive; daily account data is a separate aggregate. |
| B — Quota evidence | 004, 005, 018, 019, 020 | Replayable readings, explicit gaps/epochs/pool membership and honest historical queries. |
| C — Shared comparisons | 006, 007 | UI/CLI share eligibility, units and reproducible results. |
| D — Explain differences | 008, 009, 010 | Configuration cohorts, immutable revisions and matched-exposure UX. |
| E — Optional source | 011 | Approved, opt-in OTel; not a prerequisite for initial useful comparisons. |
| F — Outcomes and drift | 012, 013; complete 014 | Accepted-work comparisons and calibrated longitudinal analysis. |

014 is cross-cutting from A onward. Restrictions can ship before new metrics: displaying 'insufficient evidence' is preferable to continuing an invalid ratio. Wave 0 does not require a journal/schema redesign for every targeted fix.

## Original tickets, retained and clarified

### TU-CMP-001 — P0 — Granularity and provenance contracts

**Findings:** F01, F02, F07. **Area:** Core/Usage, schema, DTOs. **Depends:** none.

Add event-versus-interval representation, precision, attribution, field availability and measurement revision. Keep CostObservation and existing version fields; do not create parallel incompatible cost abstractions.

**Acceptance:** a daily row cannot enter an exact sub-day comparison as a precise event; legacy unknowns remain unknown; measured zero is distinct from absent; migration is repeatable and rolls back/recoverably preserves originals on error. **Tests:** known legacy shapes, schema-too-new, partial migration, overflow and source timestamp invariants. **Exclude:** invented interpolation and mandatory new providers.

### TU-CMP-002 — P0 — Preserve Codex deltas before daily projection

**Findings:** F01–F03. **Area:** Scan/Map/checkpoints/repository. **Depends:** 001.

Emit numeric observations from existing delta handling. Persist stable source identity and the observed model for that delta. Commit/checkpoint recovery must tolerate interruption.

**Acceptance:** 09:00=600 and 13:00=400 split at 11:00 into 600/400; refresh does not move rows; switching model does not attribute all accumulated usage to the last model. Resume carry, replay, equal counters, file rotation and incomplete lines do not double count. **Tests:** parser→repository→exact query and crash/restart at both commit boundaries. **Exclude:** equating every delta with one API request.

### TU-CMP-003 — P0 — Separate account aggregates from local events

**Findings:** F01–F02. **Area:** official Codex usage adapter and report queries. **Depends:** 001, 002.

Keep account totals and local event streams in parallel; define provider-day timezone/scope. Replace proportional model allocation with explicit unsupported attribution.

**Acceptance:** remote residue is not distributed to local models; overlapping series do not sum twice; fallback does not relabel a sample as an account total; discrepancy is shown only for compatible intervals/units/scopes. **Tests:** delayed account summaries, external activity, unavailable official interface and source transitions.

### TU-CMP-004 — P0 — Durable quota observation journal

**Findings:** F04, F06. **Area:** Core/Usage and refresh/storage seam. **Depends:** 001.

Record ordered numeric readings, source/received times, pool/units/resolution, expected reset and collection coverage. Use bounded, incremental storage; no conversation payloads.

**Acceptance:** replay reproduces readings and derived changes; duplicates do not manufacture consumption; absent collection never becomes zero; compaction retains observed time support; disabling collection produces an explicit gap. **Tests:** restarts, cancellation, stale readings and constant-value spans.

### TU-CMP-005 — P0 — Epochs and evidence-based cycle reconstruction

**Findings:** F04, F06, F13. **Area:** reset derivation/query/migration. **Depends:** 004, 017.

Separate profile, entitlement and window semantics. Record observed versus inferred boundaries, unknown timing spans, replenishment and capacity changes. Keep current unknown/manual/credit cause distinctions.

**Acceptance:** fully known 0→40→20→60 yields 80 consumed/20 replenished; noisy or unobserved intervals degrade eligibility; rolling expiration is not a refill; postponed schedules and multiple missed windows do not become confirmed invented cycles. **Tests:** first observation mid-cycle, activity-anchored resets, account change, duration change, changed expected boundary and explicit official cause. Never execute a reset as part of testing.

### TU-CMP-006 — P0 — Shared comparison eligibility policy

**Findings:** F05, F09, F15. **Area:** pure Core service with Presentation/CLI adapters. **Depends:** 001, 003, 005, 018, 020 for full normalized release; immediate conservative blocking can ship first.

Provide separate descriptive, same-model-drift and model-selection intents. Return per-metric values/units/reasons; do not hide invalid data behind a generic confidence score.

**Acceptance:** closed is not automatically complete; pool/scope mismatch blocks intensity; model-selection permits changing model while drift controls observed identity; unknown capacity remains disclosed; incompatible normalized deltas are null. **Tests:** table-driven eligibility, same inputs produce identical UI/CLI numbers, no NaN/infinity, no free-use conclusion from zero coarse movement.

### TU-CMP-007 — P0 — Unambiguous ratios, units and count semantics

**Findings:** F04–F05. **Area:** metric calculations and formatters. **Depends:** 006.

Expose quota points/M tokens and tokens/point as inverses, with a consistent A=baseline/B=current sign convention. Separate records, requests, turns and tasks.

**Acceptance:** 20→30 points with 1M tokens shows +50% intensity, -33.3% inverse, 0% volume change; zero baseline has no relative change; insufficient resolution produces a reason; costs and percentages are not mixed into one score. **Tests:** decimal rounding, nullable operands, large numbers, zero denominators and negative signed changes.

### TU-CMP-008 — P1 — Cohorts by observed model/configuration

**Findings:** F07. **Area:** permitted parser metadata, Core queries/indexes. **Depends:** 001, 006.

Retain available effort/speed/service tier/context/client version and original/canonical model IDs. Optional task/project dimensions require local pseudonymous, policy-reviewed linkage.

**Acceptance:** fast/standard or effort changes remain visible; missing metadata is not defaulted to a favorable value; cache/context strata have common support and report excluded observations. **Tests:** alias changes, unknown backend version, model switches and multiple token semantics. **Exclude:** content indexing and guessing exact backend revisions.

### TU-CMP-009 — P1 — Fixed-price views and immutable comparison revisions

**Findings:** F08, F10. **Area:** pricing, repository, exports. **Depends:** 001, 006, 008, 015–016.

Separate as-recorded values from explicit recalculation using a pinned reference catalog. Record the interpretation revision and financial meaning of costs. Provide declared quantity/price decomposition on compatible priced cohorts.

**Acceptance:** catalog refresh does not silently rewrite saved comparisons; recalculation creates a revision; incomplete cost coverage excludes/discloses the unmatched cohort; daily data does not pretend to expose request-size pricing. **Tests:** effective-date/host scopes, missing prices, tier thresholds and replay. **Exclude:** API-price-to-quota conversion.

### TU-CMP-010 — P1 — Matched-exposure comparison UI

**Findings:** F05, F09, F18. **Area:** report page/projectors/controls. **Depends:** 006–009, 018.

Add intent, A/B cohorts, elapsed-since-reset alignment, common watermark and evidence drawer. Keep tray simple. Preserve selection on refresh and tie query caching to measurement revision, not only date range.

**Acceptance:** every claim opens supporting evidence and exclusions; resets/refills differ; gaps remain visible; an open cycle compares only matched exposure and remains provisional; empty and failed history states are distinct. **Tests:** keyboard, text scale, contrast, reduced motion, narrow layout, cancellation/racing reads and packaged x64/ARM64 where affected. The previous HTML prototype is inspiration only.

### TU-CMP-011 — P1 optional — Safe Claude OpenTelemetry ingestion

**Area:** Providers, configuration, privacy. **Depends:** 001, 008 and explicit source-policy approval.

Use the documented interface only after consent, without overwriting an existing endpoint. Allowlist before persistence; handle cumulative/delta temporality and duplicated metrics/events.

**Acceptance:** no email/account IDs, paths, prompts, responses, commands or tool content enter storage/export; disabling stops collection; exporter retries and restarts do not double count. **Tests:** malicious/oversized attributes, identity fields, schema change, replay and counter resets. **Exclude:** claiming that numeric tokens expose subscription quota or automatically installing a collector.

### TU-CMP-012 — P2 — Opt-in evaluated measurement sessions

**Area:** task definitions, outcomes, export. **Depends:** 006, 008, 010.

Version task fixtures, tested repository revision, model configuration and acceptance criteria. Record accepted outcomes, duration, retries, failures and attributable usage without storing private content.

**Acceptance:** order is alternated/randomized by block; cold/warm cache are separated; failed attempts count toward cost; quality requires an actual criterion, not a commit. **Tests:** replayed result metadata and missing outcomes. **Exclude:** automatic paid runs or reset-credit consumption.

### TU-CMP-013 — P2 — Calibrated longitudinal change detection

**Area:** pure analytics and optional alerts. **Depends:** 008, 009 plus sufficient comparable independent observations; 012 when using controlled tasks.

Use cohort baselines, independent task/session/cycle units, uncertainty and practical-effect thresholds. Distinguish observed unexplained drift from proved provider behavior.

**Acceptance:** null fixtures do not systematically trigger; cache/parser/pool changes are not labeled backend changes; repeated-testing effects are evaluated; two cycles do not produce fabricated confidence. **Tests:** seeded null/known-change series, temporal dependence, missingness and holdout checks. Numeric trigger budgets must be justified by calibration.

### TU-CMP-014 — P1 cross-cutting — Replay, retention and safe export

**Findings:** F08, F10–F11, F18. **Area:** schema/migrations/diagnostics/CI. **Depends:** begins with 001/015/016; closes with 006/009.

Separate fine-event, quota-reading and rollup retention. Export definitions, revisions, units, permitted evidence and limitations. Define backup/restore and safe deletion without touching third-party originals.

**Acceptance:** expired fine evidence changes eligibility; export remains replayable at its revision; no identities/paths/content leak; schema and timezone are explicit; formula-like CSV cells are safe if CSV is added. **Tests:** UTC/DST, migration restart, retention+supersession composition, corrupt/newer schemas and UI/CLI parity. No new tests are accepted as a substitute for the existing Windows gate.

## Second-pass tickets

### TU-CMP-015 — P0 — Replace generic parser retirement with explicit supersession

**Finding:** F10. **Area:** `LocalUsageRefresh`, `UsageRepository.ApplyParserSupersessionAsync`. **Depends:** none for targeted safety design; coordinate 014.

Define known obsolete representations and the exact evidence required to retire them. Do not globally disable legitimate cleanup of known duplicate synthetic aggregates without a replacement policy.

**Acceptance:** valid older-parser history survives version changes without verified backfill; a missing/partial/failed source cannot authorize retirement; proven obsolete account fossils can still be explicitly excluded/retired; before/after evidence records keys, dates and reason. **Tests:** existing fossil test plus real historical event, empty read, inaccessible source and revision rollback. **Rollback:** recover original evidence or retain an exclusion revision; do not rely on tombstones to recover deleted values.

### TU-CMP-016 — P0 — Preserve aggregate-only history during rebuilds

**Finding:** F11. **Area:** retention and `RebuildAgentRollupsCoreAsync`. **Depends:** none; coordinate 015.

Protect partitions whose source events have expired. Bound rebuilds to complete affected evidence and avoid whole-history work per delete batch.

**Acceptance:** retained rollups survive zero-delete supersession; they also survive when another date really is pruned; repeat retention does not change totals; known obsolete rows are removed exactly once; incomplete source partitions are not falsely reconstructed. **Tests:** retention→supersession, supersession→retention, mixed parser generations, correction on same date as retired records and multi-batch inputs. A simple `if deleted == 0` guard alone does not satisfy this ticket.

### TU-CMP-017 — P0 — Order provider snapshots before topology mutations

**Finding:** F12. **Area:** `QuotaResetHistoryStore.ObserveCore` and persisted ordering state. **Depends:** none.

Apply timestamp/generation ordering before removing or adding metrics from complete snapshots. Preserve an ordering watermark for empty snapshots and removed pools.

**Acceptance:** an old complete snapshot cannot remove a newer pool or resurrect an archived pool; duplicates are no-ops; partial snapshots do not assert complete membership; restart preserves ordering. **Tests:** primary+secondary newer → primary-only older; empty newer → old nonempty; equal timestamps and provider isolation. Keep legitimate new complete-snapshot removals as archival state changes.

### TU-CMP-018 — P1 — Typed history and fine-evidence availability

**Findings:** F13, F18. **Area:** history load result/report orchestration/query DTOs. **Depends:** none for UI errors; integrate with 001/004 for evidence horizons.

Distinguish no-history, stale last-good, locked/unreadable, corrupt/quarantined, unsupported schema and expired fine evidence. Do not translate all of them into an empty successful report.

**Acceptance:** unsupported schema produces a non-destructive actionable state; retry preserves selection; exact historical query over expired detail is unavailable, not zero; no exception path leaks into exports. **Tests:** timeout, permission, corrupt JSON, schema-too-new, expired events with surviving rollups and recovery to healthy data.

### TU-CMP-019 — P1 — Explicit historical versus current-epoch reset counts

**Finding:** F14. **Area:** `QuotaResetCountQuery` and consumers. **Depends:** 005 for complete epoch grouping; named query separation can precede it.

Offer all recorded resets over a range and a separately named current-epoch/current-pool view. Archive old duration/name metadata.

**Acceptance:** removing a pool or changing duration does not alter the all-recorded count; current-epoch count can exclude it with a visible scope; dates are half-open and provider-isolated. **Tests:** inactive pool, cadence changes, renamed display label, exact boundary and migration.

### TU-CMP-020 — P0 — Enforce usage-to-pool membership

**Finding:** F15. **Area:** query contract, pool mapping, comparison policy. **Depends:** 001/004 for full relation; immediate blocking does not wait.

Stop using all-agent tokens as a pool-specific numerator. Preserve mapping evidence and allow legitimate many-to-many charging across concurrent allowances.

**Acceptance:** model-A-only pool excludes model-B events; unknown mapping yields no normalized ratio; short+weekly participation is not counted as double token use or summed quota; external activity remains unassigned. **Tests:** two models/two pools, shared pool, unknown membership and remote-only activity. **Exclude:** proportional guesswork.

### TU-CMP-021 — P1 — Deduplicate quota aliases by identity, not readings

**Finding:** F16. **Area:** `CodexRateLimitsSnapshotMapper`, stable metric IDs and history. **Depends:** none.

Use metered identity/contract evidence to recognize the backward-compatible default alias. Preserve distinct buckets with coincident values and existing collision handling.

**Acceptance:** proven default alias remains single; distinct IDs with equal windows remain distinct; later divergence does not fabricate pool creation/reset; unknown identity remains unresolved rather than merged. **Tests:** extend `StableDefaultMetricsRemainAndMirroredAdditionalBucketIsSkipped`, including named identical windows and collision cases. Coordinate persisted identity migration with 017/019.

### TU-CMP-022 — P1 — Keep reset-credit inventory authoritative

**Finding:** F17. **Area:** reset-credit mapper/presentation/tests. **Depends:** none.

Honor official `availableCount` when detail rows are capped. Separate official count, observed details and any minimum known expiry; do not infer a complete inventory from the returned subset.

**Acceptance:** official count 5 with 2 available details stays 5; null/empty details are distinguished; expired rows do not silently rewrite inventory; unsupported expiry completeness is disclosed; no credit is redeemed. **Tests:** deliberately revise the existing effective-count test and add capped, empty, null and contradictory detail shapes. Cite the official protocol in the change. Preserve public metric compatibility or version an intentional semantic change.

## Definition of done

Tickets require their behavior in production seams, not just this plan or a passing research script. At minimum: authoritative source evidence, C# regression tests, migration/retention composition where relevant, no-content inspection, UI/CLI parity, and the existing packaged Windows gate. List skipped checks and actual blockers. Attach exact commands/results; never treat test count as universal proof.

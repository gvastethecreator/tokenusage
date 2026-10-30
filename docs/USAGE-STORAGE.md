# Usage history upgrades

TokenUsage migrates its usage database in a SQLite transaction. Before upgrading
an existing schema, it creates a separate recovery copy beside that database:
`<database>.pre-v<target-version>-<unique-id>.db`.

The copy uses SQLite's online backup API, including committed WAL content. The
migration holds the writer lock while a separate private reader creates the copy.
The copy must pass an integrity check and match the old schema before migration
continues. A `.pending` suffix means the copy was not fully verified; do not treat
it as a completed backup. New databases and already-current schemas do not create
migration backups.

Migration errors roll back the database changes. The migration also checks its
final schema version before committing. Retrying after the cause is resolved
creates a new copy and preserves earlier copies. Backup creation failures stop the
upgrade; they do not reset history or start an empty database.

Recovery copies are retained and are never restored automatically. They represent
history at the time of the upgrade and may lack later activity. Do not replace a
working usage database with an older copy, or rename an open SQLite database. Keep
the current database and its sidecar files for investigation. Inspect a recovery
copy separately. The CLI can create and upgrade a separate recovered database:

```powershell
tokenusage recover-usage --backup "D:\Recovery\usage.db.pre-v7-example.db" --output "D:\Recovery\recovered.db"
```

The output must be new, with no SQLite sidecar files at that path. Recovery checks
the source schema and integrity, copies its committed snapshot, migrates the copy,
and gives the recovered database a new identity. It does not change the configured
application data location or merge newer activity. Inspect the recovered copy
before deciding how to use it. There is no automatic promotion or recovery UI.

Exit code 0 means the copy is ready; 2 means invalid arguments; 4 means recovery
failed. A failed or cancelled attempt may leave output or migration-copy files.
Keep them for inspection, do not treat them as completed recovery, and use a new
output path for a retry after resolving the cause. The source is opened read-only.

These copies protect TokenUsage's own usage store. They do not back up provider
logs, credentials, or other application data.

## Account quotas

Account quotas are additive. They do not migrate the usage database or change
event keys, prices, totals, or scanner checkpoints. Selection is in
`accounts/codex/selection.v1.json` for code-swap profiles and
`accounts/claude/selection.v1.json` for claude-swap profiles. Each opaque account
directory contains its own
snapshot cache, quota reset history, observation journal, and alert decision
state. Documents bind their schema version, provider, and account key. The
observation journal stores the same binding in its metadata table.

The first reading starts a baseline. It is never compared with another account
or with the old global quota history. Renaming or reordering a profile does not
move its data. Removing and selecting it again uses the same identity. New quota
observations do not assign shared tokens or costs to account cycles.

The account namespace marks activation even with an empty selection. Do not
delete it to disable queries: save an empty selection in Settings. Missing or
invalid selection data must not restart the old global history writer for that
provider.
Other providers and local usage collection continue. Corrupt, mismatched, and
future documents are preserved for recovery; they are not replaced with empty
state. Writes use existing atomic file replacement and document locks.

Before a real-data trial, stop the app and CLI writers. Copy each SQLite database
with SQLite's backup API so committed WAL data is included, then copy the closed
documents. Run `PRAGMA quick_check` on the copied databases. Restore into a new
directory and verify the tables and documents before using the copy. Preserve the
Windows-protected account identity secret separately through its secure store;
plain `LocalState` copies do not contain it. A missing secret blocks restored
account queries rather than silently assigning new identities.

Keep the installed package family `GVASTETHECREATOR.TokenUsage_h2dcbfhqhrgv8`
and its `LocalState`. Never uninstall to reset an update. Recovery starts in a
separate directory and must not overwrite newer data automatically.

## Codex session cleanup

Deleting Codex session logs does not remove usage already saved by TokenUsage.
Collection keeps checkpointed observations, ignores deleted files still listed in
Codex's local index, and merges new observations by their existing event keys.
Repeated refreshes and copies of a session in the archive do not add its usage
again. Keep TokenUsage's own data folder; sessions deleted before they were ever
collected cannot be reconstructed from those deleted logs.

An older checkpoint can migrate when at least one original path still matches
the current profile's sessions or local index. Missing sessions do not block that
migration. The original checkpoint is preserved as `.pre-v4`, and historical
tokens and prices remain unchanged. Detail not recorded by the older version
stays unknown. A checkpoint with no evidence connecting it to the current profile
is not assigned automatically.

## Detail retention

Normal collection retains raw usage events for 400 days. Retention keeps daily
totals and recorded prices, and stores retired event keys so ordinary replay does
not count those events again. Expired events no longer provide their configuration
or source-detail metadata. Daily totals cannot reconstruct that detail.

When configuration detail is loaded, its coverage reports how many aggregate
records still have raw evidence and how many do not. Retention also changes the
data revision. A configuration selection covers available detail only.

## Collection freshness

Schema 8 keeps the last successful collection time separately from the latest
attempt, status and issue. Complete reads and successfully scanned empty sources
advance the successful time. Partial reads and unavailable sources preserve it.
An older success arriving after a newer failed attempt can advance the successful
time without replacing the latest failed status. Older duplicate reports change
neither timestamp nor the data revision.

Migration derives a successful time only when the stored latest result proves one.
An earlier partial result leaves that time unknown. The migration uses the same
transaction, backup and writer-version protection as other history upgrades.

## Reset cycles in reports

Reports reads each quota's reset history. A cycle covers its provider schedule:
the expected reset minus the window length. The stored start is used only when it
is later, for example after an early reset. A reset that happened while
TokenUsage was not running therefore does not stretch the next cycle backwards.
Pace curves read quota observations inside that span. A drop in used quota inside
it, such as an early reset or a claude-swap account switch, starts a new line
instead of drawing usage going backwards.

Cycle token totals count records with a timestamp or a bounded interval inside the
span. Records with daily or unknown timing are left out, not spread across
cycles. Account quotas show no token totals: local logs cannot be assigned to one
account, so a ratio would be invented.

## Removed attribution (schema 13)

Schema 13 drops `session_attribution`, `project_attribution`, `operation_fact`,
and `saved_usage_comparison`. Optional session, project, and tool attribution and
saved report comparisons were removed. The upgrade deletes those tables with their
rows and triggers in the same backed-up transaction as other history upgrades.
Claude records written by parser `claude-jsonl/4` always held each message's own
timestamp but stored its precision as unknown; the same upgrade marks them as
timestamped so reset-cycle totals include them. Token counts, daily rollups,
prices, and event keys are unchanged. TokenUsage
also deletes its `attribution-consent.v1.json` and `attribution-aliases.v1.json`
documents. Earlier versions wrote `<state db>.attribution-admission.v1.json` next
to Cursor's local database. It holds only a numeric watermark; TokenUsage no
longer reads it and does not delete files in another client's directory.

## Codex collection progress

Source range reconciliation checks that stored daily totals still have all their
raw records before rebuilding them. If retention has removed records for a day,
the read is not authoritative: new rows for that day are withheld and the retained
totals stay unchanged. An empty source result cannot erase that retained history.
Ordinary upserts apply the same check before rebuilding totals; they fail in their
transaction and keep the totals and data revision unchanged. Source-attributed
inserts also reject backfill into a day with retired detail. Replaying the same
retired key remains a no-op through its tombstone. A new key cannot establish that
the old usage is distinct. Recent ranges with their records intact can still
reconcile normally.

Reading Codex usage prepares checkpoint changes in memory. The collection
coordinator commits them only after the admitted numeric batch is durable. Failed
ingest or a withheld batch leaves the stored checkpoint unchanged, allowing replay.
Before numeric admission, the checkpoint store checks both its original file and
a 16-byte `.admission` generation under the existing cross-process lock. That lock
covers numeric persistence and optional checkpoint publication. The generation
changes before persistence, including failed or withheld batches; it contains no
cursor, identifier or usage. Older prepared reads fail before touching SQLite,
even when a newer partial batch did not advance the cursor. A fresh read can replay
after failure. If checkpoint writing fails after ingest, durable numeric records
remain and can be replayed on retry. Keep the admission file with its checkpoint;
it is coordination state, not report data or an export field.

The source-admission storage operation associates exact legacy matches and writes
admitted observations in one transaction. It leaves unmatched history unattributed.
New identities on a day with unmatched history are withheld; observations on an
unambiguous day can be stored. An unresolved read cannot delete the source's prior
rows. A partial read using a different parser also cannot combine competing
representations. Callers receive the withheld count and unresolved-history state
so they can report partial coverage and retain uncommitted source progress. Codex
collection uses this operation with the profile binding proved for the individual
read. An unbound numeric read is held without changing its checkpoint. A bound
read with withheld observations is reported as partial and cannot advance its
checkpoint. All admitted observations must be durable before progress is saved.

New Codex checkpoints include an opaque profile ID. A different profile cannot use
that checkpoint as its own. Earlier checkpoint formats keep their numeric history
and a `.pre-v4` copy during upgrade. A single-profile legacy checkpoint can bind
when discovery finds an original checkpoint path in that profile. Original path hashes
remain fixed when replay locations change, so copying a checkpoint to another
profile cannot gain authority through repeated reads. No matching original path
keeps the history unbound. This identity describes the profile supplying records,
not proof of which computer executed the model or fresh validation of cached tokens.

New Codex observations keep evidence of which token components were present. An
explicit zero is measured; an absent optional counter is unknown. Exclusive input
and output require evidence for their subtracted cache and reasoning components.
Cumulative differences require measured counters at both endpoints. Checkpoints
keep that evidence for the previous cumulative and each admitted observation, so
restart does not turn missing counters into measured zeros. Earlier cached
observations remain unknown when their format did not retain this evidence.

When cumulative counters reclassify tokens between input/cache or output/reasoning,
the reader preserves the inclusive increase instead of summing independently
clamped component changes. A matching, validated last-usage record can prove the
split. Otherwise the affected split is unknown: its inclusive amount is carried in
the input or output slot for total conservation, with all affected component
availability states set to unknown. Those slots must not be presented as measured
exclusive components. This rule applies when decoding source records; cached
historical observations retain their stored evidence until a justified replay.

Newly decoded Codex observations carry numeric representation revision 1. An
initial valid cumulative counter without a valid last-usage record is a snapshot,
with unknown usage-time support. Supported cumulative differences remain interval
deltas; a timestamped last-usage record alone never establishes a final request.
Checkpoint history without a representation revision stays unverified. Its stored
values remain available without claiming that the current decoder repaired them;
unresolved source ownership still reports an unresolved-history issue.

## Cleanup in other local clients

Local client reads merge by stable event identity. Removing a client's logs or
database does not delete already collected usage. Recreated sources can add new
events, and repeated reads update the same events without adding another copy.
Normal retention rules still apply. Partial scans keep their coverage warning;
they do not justify deleting missing records.

Cursor explicitly identifies composer estimates superseded by measured turn
counters. Only those estimates are retired, in the same transaction that stores
their replacements; unrelated deleted conversations retain their history.
Clients without a detected source or retained history are hidden from the local
provider status list. Detection runs again on refresh, so installing a client later
does not require resetting TokenUsage.

Dashboard provider totals also include retained usage when current source
detection is missing. A failed refresh keeps the last displayed data and adds a
failure notice. The dashboard shows 30 days; choose All history in the report to
view older retained dates.

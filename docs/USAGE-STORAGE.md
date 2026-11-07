# Usage history and storage

TokenUsage stores numeric usage locally. Keep its data folder when moving the
portable app, and preserve the installed package's `LocalState` when updating.
Deleting a provider's logs does not delete usage that TokenUsage already saved.
This page covers upgrades, recovery, retention, and the collection rules that
protect those totals.

## Database upgrades and recovery

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

## Provider cleanup and retained usage

Deleting a local client's logs or database does not remove usage already saved
by TokenUsage. New reads merge by stable event identity: recreated sources can
add new events, while repeated reads update existing events without adding a
copy. Partial scans keep their coverage warning and cannot justify deleting
missing records. Normal retention still applies.

Codex collection also ignores deleted files still listed in its local index.
Repeated refreshes and archived copies of a session do not add its usage again.
Sessions deleted before TokenUsage collected them cannot be reconstructed from
those deleted logs.

An older checkpoint can bind to a profile when at least one original path still
matches that profile's sessions or local index. Missing sessions do not block
the binding. Historical tokens and prices remain unchanged, and detail the older
format did not record stays unknown. Without evidence connecting a checkpoint to
the current profile, TokenUsage does not assign it automatically. See
[collection checkpoints](#collection-checkpoints) for the storage upgrade.

Cursor identifies composer estimates that measured turn counters replace. Only
those estimates are retired, in the transaction that stores their replacements.
Unrelated deleted conversations keep their history.

Clients without a detected source or retained history are hidden from the local
provider status list. Detection runs on every refresh, so installing a client
later does not require resetting TokenUsage. Dashboard totals include retained
usage even when source detection fails. A failed refresh keeps the last displayed
data and adds a failure notice. The dashboard shows 30 days, and the report offers
7, 30, and 90 days. Use `tokenusage report --from <date> --to <date>` for another
retained date range.

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

The report reads each quota's reset history. A cycle covers its provider schedule:
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

## Collection checkpoints

Usage schema 14 adopts incremental checkpoint collection without changing stored
token counts or prices. The normal migration backup and writer-version guards
apply. Older builds cannot write to the upgraded usage database; do not reuse a
pre-upgrade copy as the live database to make an older build run.

Codex and Claude collection use an incremental SQLite store at
`<checkpoint>.sqlite`. It keeps numeric observations, hashed file identities,
offsets, and collection progress. The first committed collection imports a
supported existing JSON checkpoint. That original JSON remains unchanged as a
recovery source; later collections use SQLite and do not update or create a
parallel JSON checkpoint. Keep the SQLite database and its sidecar files with
the rest of TokenUsage's data. Older `.pre-v4` recovery copies remain useful
for inspection and are not active progress stores.

An unchanged scan does not rewrite retained observations. It still commits a small
admission generation so an older prepared read cannot publish over a newer read.
This coordination write is separate from usage rows and rollup changes.

A source read states whether it contains a full snapshot, a delta, or no changed
observations. The usage database saves the source cursor in the same transaction
as the numeric records. A delta or unchanged read is accepted only when the
previous cursor matches. If the usage database was recreated, restored, or
otherwise differs from the checkpoint, collection replays the retained snapshot
instead. A checkpoint alone cannot prove that a particular database received the
records. This also permits replay after a crash between numeric persistence and
checkpoint publication.

Unchanged reads update collection freshness without rewriting stored events or
daily totals. Checkpoint transactions update changed file and observation records
rather than serializing the entire history for each refresh. These are local
storage rules; public report formats remain unchanged.

OpenCode, Hermes, and Goose snapshot updates require a complete bounded read.
Normal empty sessions are valid; corrupt rows and exhausted scan limits retain
the last accepted snapshot. Model detail must reconcile with session counters
before it replaces an earlier aggregate. A stored zero residual clears the old
aggregate's contribution while keeping its identity available for later updates.
It adds no tokens, although it remains an observation in the event count.

The app and CLI share one collection gate for each usage database. A hook that
arrives during collection leaves one pending refresh request and exits. The
active collector drains the request. The operating system releases ownership if
the collecting process exits; a later collector can service pending work.

### Numeric admission and retained history

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

Codex and Claude reads prepare checkpoint changes in memory. The collection
coordinator publishes those changes only after numeric persistence succeeds.
Failed ingest or a withheld batch leaves observation progress unchanged so a
fresh read can replay it. A generation stored inside the checkpoint database
rejects an older prepared read before it writes numeric data. That generation
changes before persistence, even when persistence fails or withholds a batch.
The cross-process checkpoint lock covers numeric admission and checkpoint
publication. There is no separate `.admission` file in the active storage path.
If checkpoint publication fails after ingest, the durable numeric records and
their cursor receipt remain available for replay.

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

### Codex identity and numeric evidence

New Codex checkpoints include an opaque profile ID. A different profile cannot use
that checkpoint as its own. A single-profile legacy checkpoint can bind when
discovery finds an original checkpoint path in that profile. Original path hashes
remain fixed when replay locations change, so copying a checkpoint to another
profile cannot gain authority through repeated reads. Without a matching original
path, history stays unbound. This identity describes the profile supplying
records; it does not identify the computer that ran the model or validate cached
tokens again.

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

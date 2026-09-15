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

## Detail retention

Normal collection retains raw usage events for 400 days. Retention keeps daily
totals and recorded prices, and stores retired event keys so ordinary replay does
not count those events again. Expired events no longer provide their configuration
or source-detail metadata. Daily totals cannot reconstruct that detail.

When configuration detail is loaded, its coverage reports how many aggregate
records still have raw evidence and how many do not. Retention also changes the
data revision: an open event-page cursor must restart instead of continuing over
a changed data set. A configuration selection covers available detail only.

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
Reports shows both dates in its collection summary and measurement details. A
missing successful date is shown as not recorded; unresolved history is identified
as requiring verification rather than a complete collection.

## Activity evidence

Daily report charts do not load hourly detail. Two-hour chart styles request it
through the report's bounded load path and cancel obsolete selections.
`UsageReport.HasTimeBucketDetails` distinguishes an unloaded report from a loaded
empty result. While detail is unloaded, timing coverage is unknown. Saved daily
snapshots show an unavailable message when viewed as a two-hour chart; they do
not read current data or draw zero usage. Exact-cycle and configuration detail
keep their own timing evidence.

Loading retained detail also projects activity coverage for the selected tool,
host, model and configuration. Timestamped observations, interval observations,
non-placeable usage and totals without retained detail remain separate. Their
record/token totals reconcile with the report. Activity clock buckets contain only
supported timestamped usage; explicit snapshots and daily aggregates do not gain
hourly support from an observation timestamp.

Exact queries use half-open boundaries. Intervals that cross the selected boundary
and records without supported usage time are excluded explicitly. Reference-price
changes preserve activity token counts. The Reports Activity expander loads this
shared detail on demand. It shows the four coverage groups and two-hour local
clock windows in each recorded time zone. Repeated daylight-saving hours are
combined, not interpreted as equal elapsed durations. The list starts with 24
windows and adds 24 on request; changing the selection resets the display limit.
These rows count observations, not proved final requests or measured durations.

Measurement details distinguishes Codex reader capabilities from the observation
count in the current selection. Reader support does not guarantee available
records. Zero matching observations does not establish zero usage. The count can
include aggregate history without retained detail; Activity supplies that separate
coverage. Last success and latest attempt describe collection, not completeness
of the selected model, period, account or PC.

## Session attribution

Schema 9 adds `session_attribution`, an optional side table keyed by the existing
event key. It stores only app-owned opaque session and parent keys plus the
consent epoch that admitted the link. Native session IDs, paths, prompts, and
working directories are not stored. Consent lives in a JSON document outside the
usage database, so restoring a `.pre-vN` copy cannot revive permission.

Attribution starts off. Enabling it does not rewrite historic numeric events.
Automatic links start at enablement: Codex snapshots already-known observation
keys when the consent epoch changes, and Cursor persists the same watermark beside
the local database. Occurrence timestamps are not the only boundary. Disabled
readers select epoch 0, so a restored backup cannot expose restored links even if
those rows still exist in SQLite. Revoke and purge delete rows in
`session_attribution` and derived checkpoint keys. They do not delete
`usage_event`, daily rollups, prices, or event keys. A crash that leaves purge
pending keeps the capability disabled until the purge finishes. Re-enabling
starts a new epoch and does not republish purged links. Historical attribution
requires an explicit bounded backfill of a retained source range. A successful
explicit admission is recorded in the Codex checkpoint for that consent epoch so
ordinary refresh and source restart keep the link after the temporary range is
cleared. Session and project committed keys stay independent. A new epoch still
starts empty and does not rebuild purged admissions. Events without
a live matching epoch remain Unassigned.

Default report snapshots omit aliases and persistent attribution keys. Session
and project populations, when exported, use ephemeral IDs that are valid only
inside that file. Money amounts are exact invariant decimals, not a six-place
display format.

Session outliers (`session-outlier/v1`) use comparable attributed session totals
from the live consent epoch. They do not require RequestFinal. Request input-size
distributions stay gated on proved final observations.

## Project attribution

Schema 10 adds `project_attribution`, an optional side table keyed by the same
event key. It stores an opaque project key, the consent epoch, and mapping kind
(`observed`, `user-mapped`, or `ambiguous`). Ambiguous rows may omit the key.
Working directories, native IDs, and paths are not stored. Project consent is a
separate JSON choice from session consent. User-mapped rows survive observed
backfill. Revoke and purge delete project links and local aliases. They do not
delete `usage_event`, daily rollups, prices, or event keys.

Schema 10 also adds `session_attribution.capability` so Cursor composer links
can share the session table without mixing Codex epochs.

## Operation facts

Schema 11 adds `operation_fact`, an optional adjunct keyed by an app-owned opaque
operation identity. It stores a capability, consent epoch, kind
(`tool`, `mcp`, `skill`, `spawn`, `command`, `file`), bounded tool/server labels,
an outcome enum, timestamps, an optional opaque session key, and a quantity.
Arguments, URIs, command text, paths, prompts, and tool outputs are not stored.
Each capability (`codex-mcp`, `codex-skills`, `codex-commands`, `codex-files`) is
an independent Settings choice, off by default, with its own epoch. Enabling it
does not rewrite numeric usage. Historical facts require an explicit bounded
backfill. Revoke and purge delete `operation_fact` rows for that capability and
derived checkpoint admissions. They do not delete `usage_event`, daily rollups,
prices, or event keys. Re-enabling starts a new epoch and does not republish
purged facts. Codex DynamicToolCall names are allowlisted to `Read` and `Edit`.
Command families are derived transiently from argv and stored as a family label
only. File rows store an HMAC of a normalized path, never the path.

Schema 12 adds nullable `operation_fact.source_instance`. New opaque operation
keys include the collector's source authority in HMAC material, so two homes
with the same provider `call_id` stay distinct. Opening a schema-11 database
adds the column as NULL and keeps admitted history. A later observation that
carries the recovered unnamed-authority key deletes that NULL row in the same
transaction before inserting the authority-scoped key, so one logical call
stays one invocation. Two genuine source authorities still count two. When a
NULL-authority row and a named-authority row coexist without that recovered
key, Reports does not treat their sum as an exact combined count: named rows
are the exact population, and the unrecovered legacy rows are listed separately
until an explicit backfill matches them. Unique identity remains
`operation_key`. Capability purge remains available when identity cannot be
recovered.

Reports derives `derived-activity/v1` at query time from admitted operation facts.
The counting unit is ranking invocations after `GROUP BY kind, tool, server`, not
distinct `operation_key` rows. The partition is exclusive: edit (file or
allowlisted Edit), read/search (allowlisted Read or search family), test (test
family), delegate (spawn), and Unknown. MCP, generic shell, and Skill (if it
ever appeared) are Unknown. Prompt text is not classified. Cost by category is
unavailable. Category evidence is the ranked rows whose `Classify` matches that
bucket; mixed and proved ranking rows are disjoint and both are included.
`workflow-indicators/v1` counts
same-file verification-separated edits when a completed admitted test-family
operation sits strictly between two completed ordered edits of the same opaque
file identity. A missing end is incomplete, not a zero-duration completion.
Failed verifications with a proved end still count. Concurrent completed pairs
are excluded; file/file and edit/test exclusions are stored separately so a
revoked commands permission cannot keep a joint count. Clicking that indicator
opens the contributing event sequence (the two edits and the between test),
not every test-family ranking row or every session that touched the file.
Frozen workflow counts carry the files/commands consent epochs internally and
drop joint fields when either epoch is revoked or replaced. First-edit latency
stays unavailable: no proved task start is retained, and RequestFinal is not used as a
substitute. Both method versions are
written on frozen snapshots; they are not stored in SQLite. CSV and HTML export
the same method, unit, category counts, eligible/excluded populations and
unavailable reasons as JSON. Operation navigation reuses the opened
project/session/model scope for ranking, timeline, derived activity, workflow
and export. Explicit operational backfill replays the unchanged prefix even
when the log has grown, then scans the new suffix; ordinary refresh does not.

The retained-detail query also provides `measured-input-cache-share/v1`. Its
numerator is measured cache-read tokens; its denominator is measured input plus
cache-read plus cache-write tokens. All three components must be measured for a
record to qualify. It sums amounts before division and never averages percentages.
Unknown components, unavailable components and missing retained detail form
separate exclusion counts. A measured zero denominator has no percentage; a
positive denominator with zero cache reads has a measured zero percent. Selection
and reference repricing preserve these rules. Reports can load this shared detail
from its explanation card and display the eligible amounts and exclusions.

`observed-change-explanations/v1` compares calendar snapshots with equal period
lengths and known matching parser definitions and time zones. Unsupported axes,
exact intervals or scenario methods return explicit limitations. Cost explanations
also require matching pricing definitions and fully priced observations. Changes
of at least 10 percentage points in price or retained-detail coverage are reported
before contributions; unknown detail coverage remains unknown. This does not
estimate coverage of all PC activity.

For compatible snapshots, up to two model contributions and the remaining-model
amount reconcile exactly with the observed change. No percentage of a near-zero
net change is calculated. Missing model detail blocks the explanation if its sum
does not match the report. The result keeps its metric, selection, rule version,
coverage differences and cache evidence. Reports saves this result with the selected
metric and displays it without rewriting its version on load. Older snapshots show
that the explanation is absent. A different stored rule version or metric is not
silently interpreted under the current rules. The evidence and model-breakdown
actions lead to existing report detail and provide a keyboard return path.

## Codex collection progress

Range reconciliation checks that stored daily totals still have all their raw
records before rebuilding them. If retention has removed records, the operation
fails in its transaction and keeps the totals and data revision unchanged. An
empty source result cannot erase that retained history. Ordinary upserts and whole
agent replacements apply the same check before rebuilding totals. Source-attributed
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
and a `.pre-v4` copy during upgrade. They can bind to a profile only after a complete
scan finds every original checkpoint path in that profile. Original path hashes
remain fixed when replay locations change, so copying a checkpoint to another
profile cannot gain authority through repeated reads. Missing or partial evidence
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
Checkpoint history without a representation revision stays unverified and makes
the source read partial with an unresolved-history issue. Its stored values remain
available without claiming that the current decoder repaired them.

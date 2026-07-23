# Ticket 12A: dashboard layout foundation

Status: implemented and verified as a Core foundation. This cut has no visible
WinUI controls; Tickets 12B and 12C will connect it to the dashboard.

## Delivered behavior

- Immutable provider and metric preferences keep order, visibility and
  highlight state.
- Move actions accept one step in either direction and clamp at list edges.
- Catalog reconciliation keeps saved and unknown entries, appends new entries
  in catalog order and keeps saved entries when the 100-item safety limit is
  full.
- `dashboard-layout.v1.json` uses deterministic camel-case JSON, a 64 KiB size
  limit and a depth limit of 16.
- Saves use a same-directory temporary file, disk flush and atomic replacement.
- A named mutex serializes load and save across callers. Lock waits have a
  30-second bound and support cancellation.
- Missing files return an empty result. Invalid files move to an exact-byte
  quarantine. Future schema versions remain untouched and block overwrite.
- Existing corrupt or oversized documents also block overwrite and keep every
  original byte.

## Delegation and review

Grok Build worked in
`D:\DEV\wopenusage\.snapshots\grok-build\ticket-12a-layout-foundation`.
The first run failed while reading files. The retry produced a draft but timed
out before a valid result. The parent applied the declared outputs, removed the
used snapshot and kept the redacted run records under
`.scratch/agent-cli-delegation/grok-build/runs/`.

Local review rejected the draft mutex lifetime, unbounded wait, truncated
quarantine and broad I/O handling. Those parts were replaced before proof. A
fresh independent review then found mutable array exposure, unsafe overwrite
of corrupt schema-v1 files and undefined limit growth. The repaired cut received
`ACCEPT` with no remaining P0-P2 findings.

## Automated proof

Focused Release/x64 layout tests: 42/42.

Core Release/x64 suite: 128/128.

Final `scripts/check.ps1 -Platform x64 -Configuration Release`:

- Architecture: 62/62;
- Core: 128/128;
- CLI: 82/82;
- Providers: 254/254;
- Platform Windows: 98/98;
- solution and x64 MSIX package build: passed.

## Boundary

This cut persists and reconciles layout data only. It does not yet expose
reorder, hide, highlight, undo or reset controls in the packaged app.

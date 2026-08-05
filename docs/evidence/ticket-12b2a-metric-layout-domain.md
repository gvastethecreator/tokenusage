# Ticket 12B2A: metric layout domain

Status: implemented and focused-verified. Projection and WinUI controls remain
in Tickets 12B2B and 12B2C.

## Delivered behavior

- Metric preferences now keep Always Visible versus On Demand membership.
- The v1 JSON writes `isOnDemand`; older v1 files omit it and load as Always
  Visible.
- Catalog reconciliation accepts a default section for each new metric while
  preserving every saved flag and unknown row.
- A provider accepts at most two new highlighted metrics. A third request is a
  no-op and never evicts an existing choice.
- Historical v1 files with more than two highlighted metrics still load without
  quarantine. Users can remove old choices until the layout reaches the limit.
- The old metric-ID reconciliation entry point stays source-compatible. The new
  typed catalog uses a distinct method name to avoid ambiguous null or collection
  expressions.

## Reference decision

The local OpenUsage reference keeps order, visibility, Always Visible/On Demand
membership and up to two pinned metrics per provider as separate state. The
Windows domain now has the same four facts. Metric identity and UI integration
remain separate so this cut does not derive IDs from localized labels or UIA IDs.

## Delegation and review

The first Grok Build snapshot had two outputs and was cancelled before any edit.
The parent preserved its run evidence and removed the snapshot. A one-file retry
completed in four turns and reported USD 0.1490756. The parent added tests and
JSON persistence.

Independent review found two issues:

- rejecting old v1 files with three highlights would quarantine valid user data;
- generic `Reconcile` overloads made null and collection-expression calls
  ambiguous.

The accepted repair keeps legacy highlights and names the new method
`ReconcileWithMetricCatalog`.

## Proof

Release/x64 focused command:

`dotnet test tests\TokenUsage.Core.Tests\TokenUsage.Core.Tests.csproj -c Release -p:Platform=x64 --no-restore --filter "FullyQualifiedName~DashboardLayoutTests|FullyQualifiedName~DashboardLayoutStoreTests"`

Result: 48/48.

Coverage includes mutation and idempotence, catalog defaults, saved membership,
two-highlight refusal, old-over-limit v1 load, round trip, deterministic JSON,
unknown retention, limits, cancellation and quarantine paths.

The batch-level full suite is deferred until 12B2B closes, per the project test
budget. This cut changes no app or rendered UI.

## Boundary

No dashboard metric has a layout ID yet. Quota windows, primary metrics and
secondary metrics still render in their prior order. No WinUI control consumes
the new section field in this cut.

# Ticket 74E2b: Vercel quota runtime

## Outcome

The Vercel runtime now queries the per-key budget when its stored connection has
a public key ID. A found budget adds one USD progress metric with used amount,
limit, reset cadence and active state. The existing 30-day report remains the
primary source for spend, tokens and requests.

The runtime fetches the report first. It skips quota for legacy or key-only
connections, and it never pays for or waits on a quota call after a report
failure. A fresh provider cache returns before either network call.

## Independent quota state

Provider snapshots now carry typed capability states. Vercel records
`quota.gateway.key.budget` as:

- `Available` when a validated budget was returned;
- `NotRequested` when the connection has no key ID;
- `NotConfigured` for the documented `Quota not found` response;
- `Degraded` when the quota client returns a typed error.

This state is separate from metrics and from report coverage. A quota error
returns `PartialSuccess` with a sanitized `SourceDegraded` warning, but a fully
valid report keeps `CoverageKind.Complete`. The degraded state remains in the
cached snapshot, so the UI can explain the quota without casting doubt on the
report.

## Core and cache contract

`ProgressMetricSnapshot` has optional unit, reset cadence and active metadata.
Existing providers and old cache documents remain valid. Cache schema v1 writes
the new fields only when present and validates them on read.

Provider capability records are also additive in schema v1. Old readers ignore
the new `capabilities` collection and unknown Vercel metric ID. The existing
Vercel projector selects known scalar IDs, so a downgraded app does not render
the USD budget as another progress type. New readers round-trip capability ID,
state and provenance and reject unknown states.

The Vercel adapter contract advances to version 2. Report metrics retain
`vercel-ai-gateway-report/1`; the budget metric and capability use
`vercel-ai-gateway-quota/1`. Locally derived `NotRequested` and `Degraded`
states use `vercel-ai-gateway-quota-state/1`.

## Automated proof

```text
Vercel provider tests: 84/84
Focused core contract and cache tests: 43/43
Vercel Windows tests: 36/36
Release/x64 WOpenUsage.App build: passed, 0 warnings
```

Coverage includes found quota, all budget metadata, missing key ID, no-budget,
typed quota failure, safe warning copy, report preservation, capability state,
cache round-trip, old cache input and invalid cadence quarantine.

Full Release/x64 checkpoint:

```text
.\scripts\check.ps1 -Platform x64 -Configuration Release

Architecture: 62/62
Core: 86/86
CLI: 82/82
Providers: 254/254
Platform Windows: 94/94
Solution and x64 MSIX package build: passed
```

## Review

Grok Build reviewed the runtime design without file access. Local review accepted
its finding that optional quota health must not change report coverage and that
missing quota metrics need an explicit state. The capability model implements
both corrections.

Caller cancellation already propagates outside the typed quota-error catch and
does not write a degraded result. Connection changes clear the Vercel snapshot,
so a new or rotated key ID cannot reuse a report-only fresh cache.

## Remaining gate

74E3 must collect the public key ID, pass it through connect, and render the four
capability states plus the budget metric in English and Spanish. Ticket 74F
still needs an authorized packaged smoke with a disposable real key.

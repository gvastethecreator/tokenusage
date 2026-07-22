# Ticket 08B: Codex quota snapshot mapping

Date: 2026-07-22

Status: implemented and verified with synthetic quota models. Runtime detection,
the Windows child process, cache composition, and UI remain later slices.

## Mapping contract

`CodexRateLimitsSnapshotMapper` converts the parsed app-server response into the
existing Core snapshot schema:

- `usedPercent` becomes progress `used` with `limit = 100`;
- primary and secondary use stable `quota.primary` and `quota.secondary` IDs;
- reported window duration is stored as a scalar `*.window-minutes` metric;
- additional buckets use a normalized `quota.<limit>.primary|secondary` prefix;
- a mirrored additional bucket is skipped while the stable default metric stays;
- additional buckets are sorted, capped, and rejected on normalized ID collision;
- plan values become short display labels;
- provenance is `OfficialLocalApi`, `ProviderReported`,
  `codex-app-server/1`;
- fetch and observation times are the same UTC response time;
- a response with no quota window returns typed `NoRateLimits` instead of a
  successful empty snapshot.

Core and cache v1 already support progress and scalar metrics, so this slice did
not add a project reference, metric kind, or cache migration.

## Proof

The mapper tests cover:

- primary and secondary progress, reset, duration, plan, provenance, and time;
- exact remaining-percent behavior at 0% and 100% used;
- stable default IDs with mirrored additional data;
- deterministic additional metrics and empty additional buckets;
- unique additional plan fallback;
- no-window result;
- normalized-ID collision with a fixed private error;
- non-UTC observation rejection.

```text
Focused Codex provider tests, x64: 27/27 passed
scripts/check.ps1 -Platform x64:
  Architecture 22/22, Core 32/32, Providers 41/41, build 0 warnings/errors
scripts/check.ps1 -Platform ARM64:
  Architecture 22/22, Core 32/32, Providers 41/41, build 0 warnings/errors
dotnet format WOpenUsage.slnx --verify-no-changes --no-restore: passed
git diff --check: passed
```

The Grok read-only review hit its turn limit and produced contradictory advice,
so it did not pass the review gate. Parent inspection retained the stable default
metric, skipped only its mirrored extra bucket, and preserved window duration in
the existing scalar metric contract. Local tests and builds remain authoritative.

## Next

Ticket 08C adds a Codex provider runtime over injectable client and locator seams.
It must distinguish missing CLI, missing login, unsupported account, timeout,
protocol failure, and a valid mapped snapshot without starting a real login.

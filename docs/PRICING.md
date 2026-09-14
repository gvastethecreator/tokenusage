# Pricing evidence and refresh

TokenUsage estimates raw API value only when a local event contains a concrete
model and numeric token counters. It does not treat an API estimate as a
subscription invoice or plan-credit charge.

Every active rate has typed evidence with:

- an official HTTPS source;
- the date that source was reviewed;
- direct-provider or host-specific billing scope;
- an effective start and, when applicable, an explicit inclusive or exclusive end;
- the catalog version and exact price match stored with the event.

Host-specific rates are separate catalog entries. For example, Cursor's Gemini
3.8 Flash output rate does not replace the direct Google API rate.

GPT-6 Astra uses the [official OpenAI rates](https://developers.openai.com/api/docs/models/gpt-6-astra),
reviewed on 2026-09-04: $10 input, $1 cached input, $12.50 cache writes, and
$50 output per million tokens. Input and cache rates double above 272,000 prompt
tokens. Output, including reasoning, then uses 1.5 times the standard rate.
The catalog records these rates as `openai-api-2026-09-04`.

Astra remains a model within each agent's usage. Its addition does not create
another provider or subscription quota. Codex keeps an allowlisted service tier when the numeric record supplies it.
The current estimator accepts Standard or an unspecified tier; explicitly different
tiers stay unpriced. An unspecified tier is not proof of Standard billing.
A reasoning-effort suffix does not imply a different service tier.

Codex parser `codex-jsonl/9` preserves numeric deltas and prices supported
timestamped records at their event time. Unknown model transitions or timing
stay unpriced; daily account totals remain a separate account view. The collector
no longer distributes account totals across local models or invents a timestamp
at local noon. Existing historical prices remain as recorded, with legacy timing
explicitly unknown.

The Compare view can reprice a matching supported cohort at a selected reference
date. The reference is off by default and does not rewrite stored events. Codex
and Cursor use their existing catalogs; unsupported providers, tiers, or missing
raw history remain excluded and visible. Cost per million uses priced tokens,
not the full partially priced population. Saved comparisons freeze the selected
reference, catalog versions, exclusions, data revision, and displayed results.

The Rates axis prices the same retained observations at two catalog dates.
Only observations that are priceable under both dates and the same policy
enter the comparison. Volume and mix are not applicable to this fixed-cohort
experiment. Equal token totals with disjoint priced identities make the
comparison unavailable; they are not a tariff delta. A legitimate catalog
price of zero is distinct from an unavailable cost. Catalog dates are list-rate
scenarios, not a bill or a historical cause of spend.

Known usage cost is not an invoice, subscription charge, or USD-per-quota value.

Periods also offers a historical linear reference scenario. It keeps recorded
tables unchanged and explains eligible catalog value in the order volume, mix
of models and token components, then price (`sequential-vmp-linear/v1`). Both
periods and their retained observations come from one SQLite snapshot. Each
cell needs rates at both reference dates, an explicit standard tier, measured
disjoint components, and compatible measurement and pricing regimes. Missing
detail and unsupported cells are counted outside the calculation; missing
configuration membership is not inferred for retired detail.
Timestamped records and intervals wholly inside the selected civil-day period
can enter the scenario. Unknown timing, cumulative snapshots, daily aggregates,
and intervals crossing the period boundary remain outside it.

Codex and Cursor OpenAI models reuse published linear catalog entries. Models
with long-context thresholds remain excluded even below the threshold. Claude
and Cursor Anthropic rows are eligible only without cache-write or separate
reasoning tokens, because the retained generic record does not preserve the
cache-write duration split. Eligible Cursor first-party entries reuse their
existing catalog; an unknown host still excludes the row. No rates, discounts,
or billing charges are inferred from daily averages.

Amounts and effects round separately to six USD decimal places, away from zero;
a separate residual reconciles those rounded effects with the scenario delta.
Zero eligible volume leaves effects unavailable. Saved scenarios retain cells,
rates, dates, policy, exclusions, precision, and method instead of repricing on
open. These order-dependent calculations do not establish causes, invoice
savings, or model quality. The fixed-cohort Rates method remains available.

Quota efficiency remains unavailable without evidenced consumption and matching
pool attribution, even when a cycle has ended.

Run the local audit:

```powershell
tokenusage pricing audit --format human
```

Run the weekly refresh logic without writing files:

```powershell
tokenusage pricing refresh --dry-run
```

The refresh fetches only the URLs compiled into the allowlist. Redirects are
disabled. Each request has a 20-second timeout, a 1 MiB maximum, an allowed text
content type, and a fixed projection of public pricing markers. It stores only
the projection result, never the fetched page.

Unstructured HTML changes create a review item in
[`pricing-refresh.md`](pricing-refresh.md); they do not edit a rate. A promotion
that expires without a versioned successor fails the refresh. The scheduled
workflow writes only a changed report, opens or updates one draft pull request,
and never merges it. A human must verify the official source, make any catalog
or evidence edit, approve the pull request, and merge it.

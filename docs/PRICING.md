# Pricing evidence and refresh

The 2026-10-12 review checked all 13 official sources and their current model
price tables. It corrected Sonnet 5.5 cache reads from $0.20 to $0.10 per million
tokens in `anthropic-api-2026-10-12`. Other rates and effective dates are unchanged.
The refresh now reads the official Markdown documents for Anthropic and Kimi.
Anthropic's HTML page exceeds the 1 MiB bound. Kimi's Markdown exposes the exact
table values, including its JSX currency markup. Manual inspection confirmed K3:
$3 input, $0.30 cached input, $15 output, and $3/$6 cache writes for 5min/1h.
Markers alone do not prove that every price belongs to the expected model row.

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
reviewed on 2026-10-02: $10 input, $1 cached input, $12.50 cache writes, and
$50 output per million tokens. Input and cache rates double above 272,000 prompt
tokens. Output, including reasoning, then uses 1.5 times the standard rate.
The current catalog version is `openai-api-2026-10-02`.

GPT-6 Sol and GPT-6 Luna use the [official flagship rates](https://developers.openai.com/api/docs/pricing),
reviewed on 2026-09-22. Sol is $2 input, $0.20 cached input, $2.50 cache writes,
and $10 output per million tokens. Luna is $0.10, $0.01, $0.125, and $0.50.
Above 272,000 prompt tokens, input and cache rates double and output uses 1.5
times the standard rate, the same rule as Astra.

GPT-6.1 Sol uses the [official flagship rates](https://developers.openai.com/api/docs/pricing),
reviewed on 2026-10-02: $2 input, $0.10 cache reads, $2.50 cache writes, and
$10 output per million tokens. Above 272,000 prompt tokens, these become
$4, $0.20, $5, and $15. GPT-6 Sol keeps its separate $0.20 short-context
cache-read rate.

GPT-5.6 Cyber uses its [official model pricing](https://developers.openai.com/api/docs/models/gpt-5.6-cyber),
reviewed on 2026-10-02: $12.50 input, $1.25 cache reads, $15.625 cache writes,
and $75 output per million tokens. Its model page specifies doubled input
and cache rates and 1.5 times the output rate above 272,000 prompt tokens.
The catalog estimates recorded usage; model access still requires OpenAI approval.

Claude Opus 5.5 uses the [official Anthropic rates](https://platform.claude.com/docs/en/about-claude/pricing):
$4 input, $5 five-minute cache writes, $8 one-hour cache writes, $0.20 cache
reads, and $20 output. Fast mode is $8 input and $40 output, with the same
cache multipliers. Claude Sonnet 5 remains at its published $2 and $10 rates.

Claude Sonnet 5.5 (`claude-sonnet-5-5`) uses these
[published standard rates](https://platform.claude.com/docs/en/models/sonnet-5-5/overview):
$2 input, $2.50 five-minute cache writes, $4 one-hour cache writes, $0.10 cache
reads, and $10 output per million tokens. These rates were reviewed on
2026-10-12 and are retained in `anthropic-api-2026-10-12`. Cursor also lists
Sonnet 5.5; its local model IDs resolve through the shared Anthropic catalog.

Claude Haiku 5.5 (`claude-haiku-5-5`) uses the
[official rates](https://platform.claude.com/docs/en/models/haiku-5-5/overview),
reviewed on its October 7, 2026 release: $0.10 input, $0.50 output, $0.125
five-minute cache writes, $0.20 one-hour cache writes, and $0.01 cache reads
per million tokens. Above 100,000 prompt tokens, all five rates multiply by
five. Input, cache reads, and cache writes count toward that threshold;
output does not. The shared catalog also resolves provider-prefixed IDs.
Session totals without per-request counters stay unpriced unless a cost was
reported. Linear price scenarios exclude Haiku 5.5 because its tier can change.

Grok 4.7 uses the [official xAI rates](https://docs.x.ai/developers/pricing):
$2 input, $0.50 cached input, and $6 output, doubling at 200,000 prompt tokens.
Grok Build Fast costs $4, $1, and $12 up to 200,000 prompt tokens, then
$6, $1.50, and $18. [Cursor's Grok 4.7](https://cursor.com/docs/models/grok-4-7)
uses its own threshold: above 256,000 input tokens, Standard doubles and
Fast costs $6, $1.50, and $18. Cached input counts toward the input window;
output does not. Both Cursor variants are excluded from linear price scenarios.
GLM-5.3-FlashX uses the
[official Z.ai rates](https://docs.z.ai/guides/overview/pricing): $0.37 input,
$0.075 cached input, and $1.25 output.

The 2026-10-02 review added [Muse Spark 1.3 in Cursor](https://cursor.com/docs/models/muse-spark-1-3):
$1.25 input, $0.15 cached input, and $4.25 output per million tokens, with
no cache-write charge or long-context multiplier. This host-specific entry
belongs to the Cursor catalog; it does not establish a direct Meta API rate.

The 2026-10-19 review updates Gemini 3.8 Flash in Cursor to $3.75 output per
million tokens, with $0.75 input and $0.075 cache reads. It also adds
[Gemini 4 Argon in Cursor](https://cursor.com/docs/models/gemini-4-argon) at
$2 input, $0.10 cache reads, and $10 output, and
[Muse Wonder](https://cursor.com/docs/models/muse-wonder) (`muse-wonder-1.0`)
at $4 input, $0.20 cache reads, and $20 output. Neither new Cursor entry
charges cache writes. Muse Wonder has no long-context multiplier. These
host-specific rates belong to `cursor-models-2026-10-19`.

[Google's direct Gemini 4 Argon rates](https://ai.google.dev/gemini-api/docs/pricing)
are $2 input, $0.10 cache reads, and $10 output through November 30, 2026.
They become $4, $0.20, and $20 on December 1, 2026. The
`google-api-2026-10-19` catalog applies that published transition separately
from Cursor's rates. Argon uses implicit caching and has no long-context
multiplier. All 15 pricing sources were reviewed on 2026-10-19.

[Kimi K3](https://platform.kimi.ai/docs/pricing/chat) charges $3 per million
five-minute cache writes and $6 for one-hour writes. Generic local events do
not retain that duration, so K3 events with cache writes stay unpriced unless
the source reports a cost. Other Kimi rates are unchanged. The catalog version
is `moonshot-api-2026-10-02`.

[GPT-5.6 Sol](https://developers.openai.com/api/docs/models/gpt-5.6-sol) guarantees
its current promotional rates at least through November 21, 2026. No successor
rate is published. Events from November 22 stay unpriced until a rate is
verified. The audit flags this evidence deadline; it does not infer a price rise.

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
content type, and a fixed projection of public pricing markers. The timeout
covers headers and the response body. It stores only the projection result,
never the fetched page. Matching markers are a limited page check: they do
not validate model-price row associations or discover new models. Manual
review is still required, even when every marker matches.

Unstructured HTML changes create a review item in
[`pricing-refresh.md`](pricing-refresh.md); they do not edit a rate. A promotion
that expires without a versioned successor fails the refresh. The scheduled
workflow writes only a changed report, opens or updates one draft pull request,
and never merges it. A human must verify the official source, make any catalog
or evidence edit, approve the pull request, and merge it.

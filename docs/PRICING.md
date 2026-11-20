# Pricing evidence and refresh

The 2026-11-19 review added Gemini 4 Neon at $3 input, $0.15 cache reads,
and $15 output per million tokens. The
[Google API](https://aistudio.google.com/docs/pricing#gemini-4-neon) Standard tier
and [Cursor](https://cursor.com/docs/models-and-pricing#model-pricing) publish
the same rates. The catalogs keep separate source evidence and use November 18
UTC, the [Google launch date](https://blog.google/innovation-and-ai/models-and-research/gemini-models/introducing-gemini-4-neon/),
as the first pricing day. Records before that day or with explicit cache-write
tokens remain unpriced. The estimate covers token value. Batch, priority,
grounding, and subscription charges fall outside this estimate.

The 2026-11-13 review added Gemini 4 Hydrogen for the
[Google API](https://aistudio.google.com/docs/pricing#gemini-4-hydrogen) and
[Cursor](https://cursor.com/docs/models/gemini-4-hydrogen): $1.50 input, $0.075
cache reads, and $7.50 output per million tokens. Neither page specifies a
dated promotion. The `google-api-2026-11-13` and `cursor-models-2026-11-13`
catalogs retain separate direct and host evidence. Positive explicit
cache-write counters remain unpriced because neither source publishes a rate.
The refresh manifest now includes 19 official sources. Existing rates and
promotion cutoffs are unchanged.

The 2026-11-12 review added Gemini 4 Carbon for the
[Google API](https://aistudio.google.com/docs/pricing#gemini-4-carbon) and
[Cursor](https://cursor.com/docs/models/gemini-4-carbon). Both publish $2 input,
$0.10 cache reads, and $10 output per million tokens through November 30,
followed by $4/$0.20/$20 from December 1, 2026. The
`google-api-2026-11-12` and `cursor-models-2026-11-12` catalogs keep separate
direct and host evidence and apply that cutoff to the usage timestamp.
Google supports only implicit caching for Carbon and Argon, so direct API
records with explicit cache-write counters remain unpriced.
Cursor does not publish a Carbon cache-write rate either, so those records
also remain unpriced.

This review compared the current rows and conditions in the 17 existing
sources and added Cursor's Carbon page as source 18. No other current price
change was found. Kimi's current page retains the K3 and K3.1 rows but no longer
lists K2 prices; the stored K2 rates retain their earlier evidence and were not
requalified. Marker checks and historical-price limits remain as described below.

The 2026-11-09 maintenance review added [Claude Fable 5.2 and Mythos 5.2](https://platform.claude.com/docs/en/about-claude/pricing), released on November 9.
Their standard rates per million tokens are $8 input, $0.20 cache reads, $40
output, and $10/$16 for 5-minute/1-hour cache writes. Fast mode doubles these
rates. The `anthropic-api-2026-11-09` catalog applies them from their release
date and keeps the previous models' rates unchanged.

[GPT-6 Cyber](https://developers.openai.com/api/docs/models/gpt-6-cyber) now has
published Standard rates above 272,000 prompt tokens: $40 input, $4 cached input,
and $150 output per million tokens. `openai-api-2026-11-09` uses that tier when
request counters identify it. Long-context cache writes remain unpriced because
the source specifies their retention prices only for short context. Session
totals that cannot identify a request's tier also remain unpriced.

The same review compared the relevant current rows and pricing conditions in
all 17 configured sources. The existing Google promotion dates, Cursor-specific
rates, Kimi K3.1 rates, and Z.ai rates still matched. This is a documentation
comparison, not a live billing or account reconciliation. The bounded refresh
report remains a separate marker check. Legacy models that no longer appear on
current pages retain their previously recorded evidence; this review does not
requalify those historical rates.

The 2026-10-12 review checked all 13 official sources and their current model
price tables. It corrected Sonnet 5.5 cache reads from $0.20 to $0.10 per million
tokens in `anthropic-api-2026-10-12`. Other rates and effective dates are unchanged.
The refresh reads Anthropic's official Markdown document because its HTML page
exceeds the 1 MiB bound. The November review switched Kimi to its bounded
official HTML pricing page: its Markdown now omits the pricing tables, while
the HTML includes their compiled table data. Manual inspection confirmed K3:
$3 input, $0.30 cached input, $15 output, and $3/$6 cache writes for 5min/1h.
Markers alone do not prove that every price belongs to the expected model row.

The 2026-11-07 review added these direct API rates per million tokens:

| Model | Input | Cached input | Output | Official source |
| --- | ---: | ---: | ---: | --- |
| GPT-6.1 Terra | $1 | $0.05 | $5 | [OpenAI](https://developers.openai.com/api/docs/models/gpt-6.1-terra) |
| GPT-6 Cyber | $20 | $2 | $100 | [OpenAI](https://developers.openai.com/api/docs/models/gpt-6-cyber) |
| Kimi K3.1 Pro | $2 | $0.20 | $10 | [Kimi](https://platform.kimi.ai/docs/pricing/chat) |
| Kimi K3.1 Pro Highspeed | $4 | $0.20 | $20 | [Kimi](https://platform.kimi.ai/docs/pricing/chat) |
| GLM-5.3-FlashX-Legal | $0.37 | $0.075 | $1.25 | [Z.ai](https://docs.z.ai/guides/overview/pricing) |

Their initial catalog versions were `openai-api-2026-11-07`, `moonshot-api-2026-11-07`,
and `zai-api-2026-11-07`. Terra doubles input and cache rates above 272,000
prompt tokens and multiplies output by 1.5. The November 9 review adds Cyber 6
long-context prices as described above. Session totals above a model's
context threshold also stay unpriced when their individual request sizes are
unknown. Smaller totals can use the base rate. Updating the catalog does not
rewrite the usage database. A later accepted provider snapshot can correct its
own observations and estimates through the existing reconciliation rules.

OpenAI cache-write estimates use the published 30-minute Standard rate. Terra's
rate is $1.25 and Cyber 6's is $25 per million tokens before any context
adjustment. Their 12-hour rates are $2.50 and $50. The local token records do
not identify retention, so these estimates cannot establish a 12-hour charge.

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
The current catalog version is `openai-api-2026-11-17`.

GPT-6.2 Astra and Sol use OpenAI's Standard API rates from the November 17
release date. Prices are USD per million text tokens.

| Model | Input | Cache read | 30-minute cache write | Output |
|---|---:|---:|---:|---:|
| [GPT-6.2 Astra](https://developers.openai.com/api/docs/models/gpt-6.2-astra) | 8.00 | 0.40 | 10.00 | 40.00 |
| [GPT-6.2 Sol](https://developers.openai.com/api/docs/models/gpt-6.2-sol) | 2.00 | 0.10 | 2.50 | 10.00 |

Above 272,000 input tokens per request, input and cache rates double and output
uses 1.5 times the short-context rate. These estimates use Standard and
30-minute cache writes. Batch, Fast, Ultrafast, background completion windows,
and 12-hour cache writes require separate billing details.

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
reported.

Grok 4.7 uses the [official xAI rates](https://docs.x.ai/developers/pricing):
$2 input, $0.50 cached input, and $6 output, doubling at 200,000 prompt tokens.
Grok Build Fast costs $4, $1, and $12 up to 200,000 prompt tokens, then
$6, $1.50, and $18. [Cursor's Grok 4.7](https://cursor.com/docs/models/grok-4-7)
uses its own threshold: above 256,000 input tokens, Standard doubles and
Fast costs $6, $1.50, and $18. Cached input counts toward the input window;
output does not.
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

[Google's direct Gemini 4 Argon rates](https://aistudio.google.com/docs/pricing)
are $2 input, $0.10 cache reads, and $10 output through November 30, 2026.
They become $4, $0.20, and $20 on December 1, 2026. The
`google-api-2026-10-19` catalog applies that published transition separately
from Cursor's rates. Argon uses implicit caching and has no long-context
multiplier. That review checked the 15 pricing sources then configured on 2026-10-19.
The November review updated the OpenAI Sol and flagship sources, Kimi, and Z.ai,
and added dedicated Terra and Cyber 6 sources. Other source review dates remain
unchanged.

[Kimi K3](https://platform.kimi.ai/docs/pricing/chat) charges $3 per million
five-minute cache writes and $6 for one-hour writes. K3.1 Pro charges $2/$4,
and Pro Highspeed charges $4/$8 for the same durations. Generic local events do
not retain that duration, so K3-series events with cache writes stay unpriced
unless the source reports a cost. Other Kimi rates are unchanged. The catalog
version is `moonshot-api-2026-11-07`.

[GPT-5.6 Sol](https://developers.openai.com/api/docs/models/gpt-5.6-sol) guarantees
its current promotional rates through November 30, 2026. No successor
rate is published. Events from December 1 stay unpriced until a rate is
verified. The audit flags this evidence deadline; it does not infer a price rise.

The November 13 review found a different date in the Markdown metadata of
[Cursor's model list](https://cursor.com/docs/models-and-pricing): November 21
for the Sol promotion. Its visible HTML and dedicated model page do not confirm
that date. TokenUsage's current Sol estimate uses direct OpenAI evidence; it
does not establish Cursor's promotional terms. The discrepancy remains open,
without a guessed successor rate or a change to the direct API cutoff.

Astra remains a model within each agent's usage. Its addition does not create
another provider or subscription quota. Codex uses the tier in the turn context
for pricing. Ultrafast and unknown tiers stay unpriced. These pricing signals
remain in the reader checkpoint. Stored usage events use the existing tier labels.
An owned thread settings snapshot with a nonstandard tier also blocks the
Standard estimate when the turn tier is missing. The estimator accepts
Standard and records with no tier signal.
[OpenAI's published Ultrafast rates](https://developers.openai.com/api/docs/pricing)
require proof of the billed tier for each response.
A reasoning-effort suffix does not imply a different service tier.

Codex parser `codex-jsonl/9` preserves numeric deltas and prices supported
timestamped records at their event time. Unknown model transitions or timing
stay unpriced; daily account totals remain a separate account view. The collector
no longer distributes account totals across local models or invents a timestamp
at local noon. Existing historical prices remain as recorded, with legacy timing
explicitly unknown.

Known usage cost is not an invoice, subscription charge, or USD-per-quota value.

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

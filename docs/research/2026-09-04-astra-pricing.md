# GPT-6 Astra pricing update

## Evidence

The [official GPT-6 Astra page](https://developers.openai.com/api/docs/models/gpt-6-astra)
was reviewed on 2026-09-04. Standard rates per million tokens are $10 input,
$1 cached input, $12.50 cache writes, and $50 output. Above 272,000 prompt
tokens, input and cache rates double and output uses a 1.5 multiplier.

The page lists `gpt-6-astra`. No `gpt-6` shorthand or separate preview model
is inferred. Effort suffixes remain distinct from service tiers.

## Changes

- Add Astra to the shared OpenAI catalog with version `openai-api-2026-09-04`.
- Normalize Astra effort suffixes and OpenAI-prefixed model names.
- Add a separate source and fixture to the existing pricing refresh.
- Use recorded dates for Codex prices. Daily checkpoints use the report bucket timestamp.
- Correct the provider matrix to match Gemini CLI's existing blocked state.

Standard API estimates remain separate from subscription charges. Daily totals
cannot recover per-request context lengths or price changes within a day.
See [pricing semantics](../PRICING.md).

## Verification

- Architecture: 102 passed. Core: 260 passed. CLI: 130 passed.
- Providers: 625 passed. Windows platform: 174 passed.
- Full solution and MSIX x64 Release build: passed.
- The new historical checkpoint test initially failed because its file timestamp
  preceded the simulated scan window. Matching the fixture timestamp to its clock
  fixed the setup. The provider suite then passed.
- Live pricing refresh: all nine source projections matched, including Astra's seven markers.
- CLI pricing audit: 92 evidenced rates and nine official sources.
- Dependency check: no direct updates. NuGet audit: no known direct or transitive vulnerabilities.

Tests use synthetic counters. No live account session, paid inference, or visual
validation was used for this catalog and ingestion change.

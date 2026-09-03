# Provider model and pricing refresh

## Question

Which newly published agent models and price changes are missing from TokenUsage on 2026-09-02?

## Answer

The current OpenAI, Anthropic, xAI, Z.ai, and Kimi entries match their official catalogs. Google added three relevant text models that TokenUsage did not price: Gemini 3.8 Flash, Gemini 3.5 Flash-Lite, and Gemini 3.1 Flash-Lite.

Cursor also lists Gemini 3.8 Flash. Its published output price is $3.50 per million tokens, while Google lists $3.75. TokenUsage must keep that Cursor-specific rate for Cursor events and use Google's rate for other providers.

## Findings

- Google lists Gemini 3.8 Flash at $0.75 input, $0.075 cached input, and $3.75 output per million tokens through 2026-12-31. The rates double on 2027-01-01.
- Google lists Gemini 3.5 Flash-Lite at $0.30 input, $0.03 cached input, and $2.50 output per million tokens.
- Google lists Gemini 3.1 Flash-Lite at $0.25 input, $0.025 cached input, and $1.50 output per million text tokens.
- Cursor lists Gemini 3.8 Flash at $0.75 input, $0.075 cache read, and $3.50 output per million tokens.
- OpenAI's current agent family remains GPT-5.6 Sol, Terra, and Luna. Their catalog rates already match TokenUsage.
- Anthropic's current Claude Fable 5.1, Opus 5, Sonnet 5, and Haiku 4.5 rates already match TokenUsage.
- xAI's Grok 4.6, Grok Build, Grok 4.5, Grok 4.3, and Grok 4.20 rates already match TokenUsage.
- Z.ai's current documentation names GLM-5.3 and GLM-5.3-Flash. Kimi names K3, K2.7 Code, and K2.6. TokenUsage already includes them.

## Sources

- [Google Gemini API pricing](https://ai.google.dev/gemini-api/docs/pricing)
- [Cursor models and pricing](https://cursor.com/docs/models-and-pricing)
- [OpenAI model catalog](https://developers.openai.com/api/docs/models)
- [Anthropic model pricing](https://platform.claude.com/docs/en/about-claude/pricing)
- [xAI API pricing](https://docs.x.ai/developers/pricing)
- [Z.ai documentation index](https://docs.z.ai/llms.txt)
- [Kimi API documentation index](https://platform.kimi.ai/docs/llms.txt)

## Implementation decision

Add the three missing Gemini models to the Google catalog. Add a Cursor-specific Gemini 3.8 Flash rate so Cursor events use Cursor's published output price. Keep all other provider rates unchanged.

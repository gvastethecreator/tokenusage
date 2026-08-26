namespace TokenUsage.Providers.Catalog;

/// <summary>
/// Presentation facts that every surface should take from the provider catalog:
/// list rank, translated name key, and mark file. Screens must not keep their own ID tables.
/// </summary>
public static class ProviderPresentationCatalog
{
    private static readonly string[] CuratedOrder =
    [
        "codex",
        "opencode",
        "antigravity",
        "grok",
        "cursor",
        "claude",
        "amp",
        "mux",
        "goose",
        "hermes",
        "zcode",
    ];

    private static readonly Dictionary<string, int> CuratedRanks = CuratedOrder
        .Select((id, index) => (id, index))
        .ToDictionary(pair => pair.id, pair => pair.index, StringComparer.Ordinal);

    private static readonly Dictionary<string, string> MarkFiles =
        new(StringComparer.Ordinal)
        {
            ["alibaba-cloud"] = "alibaba-cloud.svg",
            ["amp"] = "amp.svg",
            ["antigravity"] = "antigravity.svg",
            ["anthropic"] = "anthropic.svg",
            ["azure-openai"] = "openai.svg",
            ["claude"] = "claude.svg",
            ["codex"] = "codex.svg",
            ["copilot"] = "copilot.svg",
            ["cursor"] = "cursor.svg",
            ["cursor-agent"] = "cursor.svg",
            ["deepseek"] = "deepseek.svg",
            ["devin"] = "devin.svg",
            ["droid"] = "droid.svg",
            ["gemini-api"] = "gemini-api.svg",
            ["gemini-cli"] = "gemini-cli.svg",
            ["goose"] = "goose.svg",
            ["grok"] = "grok.svg",
            ["grok-bot"] = "grok.svg",
            ["groq"] = "groq.svg",
            ["hermes"] = "hermes.svg",
            ["kilo-code"] = "kilo-code.svg",
            ["kimi-cli"] = "kimi.svg",
            ["kimi-code"] = "kimi.svg",
            ["kiro"] = "kiro.svg",
            ["mistral"] = "mistral.svg",
            ["mistral-vibe"] = "mistral.svg",
            ["moonshot"] = "moonshot.svg",
            ["mux"] = "mux.svg",
            ["ollama"] = "ollama.svg",
            ["openai"] = "openai.svg",
            ["openclaude"] = "claude.svg",
            ["openclaw"] = "openclaw.svg",
            ["opencode"] = "opencode.svg",
            ["openrouter"] = "openrouter.svg",
            ["perplexity"] = "perplexity.svg",
            ["pi"] = "pi.svg",
            ["qwen-cli"] = "qwen-cli.svg",
            ["roo-code"] = "roo-code.svg",
            ["vercel-ai-gateway"] = "vercel-ai-gateway.svg",
            ["xai"] = "xai.svg",
            ["zai"] = "zai.svg",
            ["zcode"] = "zcode.svg",
            ["zed"] = "zed.svg",
        };

    private static readonly Dictionary<string, string> DisplayNameResourceKeys =
        new(StringComparer.Ordinal)
        {
            ["antigravity"] = "LocalUsageAgentAntigravity",
            ["claude"] = "LocalUsageAgentClaude",
            ["codex"] = "LocalUsageAgentCodex",
            ["cursor"] = "LocalUsageAgentCursor",
            ["grok"] = "LocalUsageAgentGrok",
            ["grok-bot"] = "LocalUsageAgentGrokBot",
            ["opencode"] = "LocalUsageAgentOpenCode",
            ["zcode"] = "LocalUsageAgentZcode",
        };

    public static int CuratedRank(string? providerId) =>
        providerId is not null && CuratedRanks.TryGetValue(providerId, out int rank)
            ? rank
            : int.MaxValue;

    public static string? MarkFileName(string? providerId) =>
        providerId is not null && MarkFiles.TryGetValue(providerId, out string? fileName)
            ? fileName
            : null;

    public static string? DisplayNameResourceKey(string? providerId) =>
        providerId is not null
            && DisplayNameResourceKeys.TryGetValue(providerId, out string? key)
            ? key
            : null;
}

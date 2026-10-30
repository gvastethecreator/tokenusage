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
        "copilot",
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
            ["amp"] = "amp.svg",
            ["antigravity"] = "antigravity.svg",
            ["claude"] = "claude.svg",
            ["codex"] = "codex.svg",
            ["copilot"] = "copilot.svg",
            ["cursor"] = "cursor.svg",
            ["goose"] = "goose.svg",
            ["grok"] = "grok.svg",
            ["hermes"] = "hermes.svg",
            ["mux"] = "mux.svg",
            ["opencode"] = "opencode.svg",
            ["zcode"] = "zcode.svg",
        };

    private static readonly Dictionary<string, string> DisplayNameResourceKeys =
        new(StringComparer.Ordinal)
        {
            ["antigravity"] = "LocalUsageAgentAntigravity",
            ["claude"] = "LocalUsageAgentClaude",
            ["codex"] = "LocalUsageAgentCodex",
            ["copilot"] = "LocalUsageAgentCopilot",
            ["cursor"] = "LocalUsageAgentCursor",
            ["grok"] = "LocalUsageAgentGrok",
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

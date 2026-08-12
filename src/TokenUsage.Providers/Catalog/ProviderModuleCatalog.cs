using TokenUsage.Core.Providers;

namespace TokenUsage.Providers.Catalog;

public enum ProviderModuleStage
{
    Active,
    OptIn,
    Prepared,
    PolicyBlocked,
}

public sealed record ProviderModuleDefinition
{
    public ProviderModuleDefinition(
        string id,
        string displayName,
        IEnumerable<ProviderCapability> capabilities,
        ProviderModuleStage stage,
        bool isOpenUsageProvider)
    {
        Id = new ProviderId(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(capabilities);
        if (!Enum.IsDefined(stage))
        {
            throw new ArgumentOutOfRangeException(nameof(stage));
        }

        ProviderCapability[] capabilityArray = capabilities.Distinct().ToArray();
        if (capabilityArray.Length == 0 || capabilityArray.Any(value => !Enum.IsDefined(value)))
        {
            throw new ArgumentException(
                "At least one valid provider capability is required.",
                nameof(capabilities));
        }

        DisplayName = displayName;
        Capabilities = Array.AsReadOnly(capabilityArray);
        Stage = stage;
        IsOpenUsageProvider = isOpenUsageProvider;
    }

    public ProviderId Id { get; }

    public string DisplayName { get; }

    public IReadOnlyList<ProviderCapability> Capabilities { get; }

    public ProviderModuleStage Stage { get; }

    public bool IsOpenUsageProvider { get; }
}

public static class ProviderModuleCatalog
{
    private static readonly IReadOnlyList<ProviderModuleDefinition> Catalog =
        Array.AsReadOnly<ProviderModuleDefinition>(
        [
            new(
                "claude",
                "Claude",
                [ProviderCapability.LocalUsage],
                ProviderModuleStage.OptIn,
                isOpenUsageProvider: true),
            new(
                "codex",
                "Codex",
                [
                    ProviderCapability.Limits,
                    ProviderCapability.LocalUsage,
                    ProviderCapability.Spend,
                ],
                ProviderModuleStage.Active,
                isOpenUsageProvider: true),
            new(
                "cursor",
                "Cursor",
                [ProviderCapability.LocalUsage],
                ProviderModuleStage.Active,
                isOpenUsageProvider: true),
            new(
                "antigravity",
                "Antigravity",
                [ProviderCapability.LocalUsage, ProviderCapability.Spend],
                ProviderModuleStage.Active,
                isOpenUsageProvider: true),
            new(
                "copilot",
                "GitHub Copilot",
                [ProviderCapability.Usage, ProviderCapability.Spend],
                ProviderModuleStage.Prepared,
                isOpenUsageProvider: true),
            new(
                "devin",
                "Devin",
                [ProviderCapability.Usage],
                ProviderModuleStage.Prepared,
                isOpenUsageProvider: true),
            new(
                "grok",
                "Grok Build",
                [ProviderCapability.LocalUsage],
                ProviderModuleStage.Active,
                isOpenUsageProvider: true),
            new(
                "opencode",
                "OpenCode",
                [ProviderCapability.LocalUsage],
                ProviderModuleStage.Active,
                isOpenUsageProvider: true),
            new(
                "openrouter",
                "OpenRouter",
                [
                    ProviderCapability.Limits,
                    ProviderCapability.Usage,
                    ProviderCapability.Spend,
                ],
                ProviderModuleStage.Prepared,
                isOpenUsageProvider: true),
            new(
                "zai",
                "Z.ai",
                [ProviderCapability.Limits, ProviderCapability.Usage],
                ProviderModuleStage.PolicyBlocked,
                isOpenUsageProvider: true),
            new(
                "vercel-ai-gateway",
                "Vercel AI Gateway",
                [ProviderCapability.Limits, ProviderCapability.Spend],
                ProviderModuleStage.OptIn,
                isOpenUsageProvider: false),
        ]);

    public static IReadOnlyList<ProviderModuleDefinition> Entries { get; } = Catalog;

    public static IReadOnlyList<ProviderModuleDefinition> OpenUsageEntries { get; } =
        Array.AsReadOnly(Catalog.Where(entry => entry.IsOpenUsageProvider).ToArray());

    public static ProviderModuleDefinition Get(string providerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        return Catalog.Single(entry => string.Equals(
            entry.Id.Value,
            providerId,
            StringComparison.Ordinal));
    }
}

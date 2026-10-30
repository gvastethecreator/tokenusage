using TokenUsage.Core.Providers;

namespace TokenUsage.Providers.Catalog;

public sealed record ProviderModuleDefinition
{
    public ProviderModuleDefinition(
        string id,
        string displayName,
        IEnumerable<ProviderCapability> capabilities,
        IEnumerable<string>? aliases = null,
        bool isQuotaBlocked = false)
    {
        Id = new ProviderId(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(capabilities);

        ProviderCapability[] capabilityArray = capabilities.Distinct().ToArray();
        if (capabilityArray.Length == 0 || capabilityArray.Any(value => !Enum.IsDefined(value)))
        {
            throw new ArgumentException(
                "At least one valid provider capability is required.",
                nameof(capabilities));
        }

        string[] aliasArray = (aliases ?? [])
            .Select(alias => new ProviderId(alias).Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (aliasArray.Contains(Id.Value, StringComparer.Ordinal))
        {
            throw new ArgumentException("A provider alias cannot equal its canonical ID.", nameof(aliases));
        }

        if (isQuotaBlocked && capabilityArray.Contains(ProviderCapability.Limits))
        {
            throw new ArgumentException(
                "A provider that reports limits cannot also have its quota blocked.",
                nameof(isQuotaBlocked));
        }

        DisplayName = displayName;
        Capabilities = Array.AsReadOnly(capabilityArray);
        Aliases = Array.AsReadOnly(aliasArray);
        IsQuotaBlocked = isQuotaBlocked;
    }

    public ProviderId Id { get; }

    public string DisplayName { get; }

    public IReadOnlyList<ProviderCapability> Capabilities { get; }

    public IReadOnlyList<string> Aliases { get; }

    /// <summary>
    /// The tool has a quota, and no apt public interface or its own policy lets the app read
    /// it. This is a claim about the provider, so it belongs with the provider definition and
    /// not in a screen: a status row says "blocked by provider policy" only for these.
    /// See <c>docs/PROVIDER-MATRIX.md</c>.
    /// </summary>
    public bool IsQuotaBlocked { get; }
}

/// <summary>
/// The twelve tools the app reads from disk. Every entry has a bounded local reader.
/// </summary>
public static class ProviderModuleCatalog
{
    private static readonly ProviderCapability[] LocalSpend =
        [ProviderCapability.LocalUsage, ProviderCapability.Spend];

    private static readonly IReadOnlyList<ProviderModuleDefinition> Catalog =
        Array.AsReadOnly<ProviderModuleDefinition>(
        [
            new("claude", "Claude", [ProviderCapability.Limits, ProviderCapability.LocalUsage, ProviderCapability.Spend], ["claude-code"]),
            new("codex", "Codex", [ProviderCapability.Limits, ProviderCapability.LocalUsage, ProviderCapability.Spend]),
            new("cursor", "Cursor", LocalSpend),
            new("antigravity", "Antigravity", LocalSpend, isQuotaBlocked: true),
            new("grok", "Grok Build", LocalSpend, ["grok-build"], isQuotaBlocked: true),
            new("opencode", "OpenCode", LocalSpend),
            new("copilot", "GitHub Copilot", [ProviderCapability.Usage, ProviderCapability.LocalUsage, ProviderCapability.Spend]),
            new("amp", "Amp", LocalSpend),
            new("goose", "Goose", LocalSpend),
            new("hermes", "Hermes", LocalSpend),
            new("mux", "Mux", LocalSpend),
            new("zcode", "ZCode", LocalSpend),
        ]);

    public static IReadOnlyList<ProviderModuleDefinition> Entries { get; } = Catalog;

    private static readonly HashSet<string> ActiveLocalUsageIds = Catalog
        .Where(entry => entry.Capabilities.Contains(ProviderCapability.LocalUsage))
        .Select(entry => entry.Id.Value)
        .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Tools the app reads from disk. A screen that lists local providers asks this instead of
    /// keeping its own list of IDs.
    /// </summary>
    public static IReadOnlyList<ProviderModuleDefinition> ActiveLocalUsageEntries { get; } =
        Array.AsReadOnly(Catalog
            .Where(entry => entry.Capabilities.Contains(ProviderCapability.LocalUsage))
            .ToArray());

    public static bool IsActiveLocalUsageProvider(string providerId) =>
        !string.IsNullOrWhiteSpace(providerId) && ActiveLocalUsageIds.Contains(providerId);

    public static ProviderModuleDefinition Get(string providerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        return Catalog.Single(entry => string.Equals(
            entry.Id.Value,
            providerId,
            StringComparison.Ordinal));
    }

    public static ProviderModuleDefinition Resolve(string providerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        return Catalog.Single(entry => string.Equals(entry.Id.Value, providerId, StringComparison.Ordinal)
            || entry.Aliases.Contains(providerId, StringComparer.Ordinal));
    }
}

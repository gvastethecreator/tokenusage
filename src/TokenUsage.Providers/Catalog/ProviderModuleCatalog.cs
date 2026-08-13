using TokenUsage.Core.Providers;

namespace TokenUsage.Providers.Catalog;

public enum ProviderModuleStage
{
    Active,
    OptIn,
    Prepared,
    PolicyBlocked,
}

[Flags]
public enum ProviderReference
{
    None = 0,
    OpenUsage = 1,
    CodexBar = 2,
    CodeBurn = 4,
}

public sealed record ProviderModuleDefinition
{
    public ProviderModuleDefinition(
        string id,
        string displayName,
        IEnumerable<ProviderCapability> capabilities,
        ProviderModuleStage stage,
        ProviderReference references,
        IEnumerable<string>? aliases = null,
        bool isQuotaBlocked = false,
        ManualCredentialKind manualCredentialKind = ManualCredentialKind.None)
    {
        Id = new ProviderId(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(capabilities);
        if (!Enum.IsDefined(stage) || references == ProviderReference.None)
        {
            throw new ArgumentOutOfRangeException(nameof(stage));
        }

        if (!Enum.IsDefined(manualCredentialKind))
        {
            throw new ArgumentOutOfRangeException(nameof(manualCredentialKind));
        }

        if (stage == ProviderModuleStage.PolicyBlocked
            && manualCredentialKind != ManualCredentialKind.None)
        {
            throw new ArgumentException(
                "A policy-blocked provider cannot accept a manual credential.",
                nameof(manualCredentialKind));
        }

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
        Stage = stage;
        References = references;
        Aliases = Array.AsReadOnly(aliasArray);
        IsQuotaBlocked = isQuotaBlocked;
        ManualCredentialKind = manualCredentialKind;
    }

    public ProviderId Id { get; }

    public string DisplayName { get; }

    public IReadOnlyList<ProviderCapability> Capabilities { get; }

    public ProviderModuleStage Stage { get; }

    public ProviderReference References { get; }

    public IReadOnlyList<string> Aliases { get; }

    /// <summary>
    /// The tool has a quota, and no apt public interface or its own policy lets the app read
    /// it. This is a claim about the provider, so it belongs with the provider definition and
    /// not in a screen: a status row says "blocked by provider policy" only for these.
    /// See <c>docs/PROVIDER-MATRIX.md</c>.
    /// </summary>
    public bool IsQuotaBlocked { get; }

    public ManualCredentialKind ManualCredentialKind { get; }

    public bool AcceptsManualCredential => ManualCredentialKind != ManualCredentialKind.None;

    public bool IsOpenUsageProvider => References.HasFlag(ProviderReference.OpenUsage);

    public bool IsCodexBarProvider => References.HasFlag(ProviderReference.CodexBar);

    public bool IsCodeBurnProvider => References.HasFlag(ProviderReference.CodeBurn);
}

public static class ProviderModuleCatalog
{
    private const ProviderReference AllReferences =
        ProviderReference.OpenUsage | ProviderReference.CodexBar | ProviderReference.CodeBurn;
    private const ProviderReference OpenUsageAndCodeBurn =
        ProviderReference.OpenUsage | ProviderReference.CodeBurn;

    private static readonly IReadOnlyList<ProviderModuleDefinition> Catalog =
        Array.AsReadOnly<ProviderModuleDefinition>(
        [
            Module("claude", "Claude", [ProviderCapability.LocalUsage, ProviderCapability.Spend], ProviderModuleStage.Active, AllReferences, ["claude-code"]),
            Module("codex", "Codex", [ProviderCapability.Limits, ProviderCapability.LocalUsage, ProviderCapability.Spend], ProviderModuleStage.Active, AllReferences),
            Module("cursor", "Cursor", [ProviderCapability.LocalUsage, ProviderCapability.Spend], ProviderModuleStage.Active, AllReferences),
            Module("antigravity", "Antigravity", [ProviderCapability.LocalUsage, ProviderCapability.Spend], ProviderModuleStage.Active, AllReferences, quotaBlocked: true),
            Module("grok", "Grok Build", [ProviderCapability.LocalUsage, ProviderCapability.Spend], ProviderModuleStage.Active, AllReferences, ["grok-build"], quotaBlocked: true),
            Module("opencode", "OpenCode", [ProviderCapability.LocalUsage, ProviderCapability.Spend], ProviderModuleStage.Active, AllReferences),

            Module("openai", "OpenAI API", [ProviderCapability.Limits, ProviderCapability.Usage, ProviderCapability.Spend], ProviderModuleStage.Prepared, ProviderReference.OpenUsage, credential: ManualCredentialKind.ApiKey),
            Module("anthropic", "Anthropic API", [ProviderCapability.Limits, ProviderCapability.Usage, ProviderCapability.Spend], ProviderModuleStage.Prepared, ProviderReference.OpenUsage, credential: ManualCredentialKind.ApiKey),
            Module("azure-openai", "Azure OpenAI", [ProviderCapability.Limits, ProviderCapability.Usage, ProviderCapability.Spend], ProviderModuleStage.Prepared, ProviderReference.OpenUsage, ["azure-openai-api"], credential: ManualCredentialKind.ApiKeyAndEndpoint),
            Module("alibaba-cloud", "Alibaba Cloud", [ProviderCapability.Limits, ProviderCapability.Usage, ProviderCapability.Spend], ProviderModuleStage.Prepared, ProviderReference.OpenUsage, credential: ManualCredentialKind.ApiKey),
            Module("openrouter", "OpenRouter", [ProviderCapability.Limits, ProviderCapability.Usage, ProviderCapability.Spend], ProviderModuleStage.Prepared, ProviderReference.OpenUsage | ProviderReference.CodexBar, credential: ManualCredentialKind.ApiKey),
            Module("perplexity", "Perplexity", [ProviderCapability.Usage, ProviderCapability.Spend], ProviderModuleStage.PolicyBlocked, ProviderReference.OpenUsage),
            Module("groq", "Groq", [ProviderCapability.Limits, ProviderCapability.Usage], ProviderModuleStage.Prepared, ProviderReference.OpenUsage, credential: ManualCredentialKind.ApiKey),
            Module("mistral", "Mistral AI", [ProviderCapability.Limits, ProviderCapability.Usage, ProviderCapability.Spend], ProviderModuleStage.Prepared, ProviderReference.OpenUsage, credential: ManualCredentialKind.ApiKey),
            Module("moonshot", "Moonshot", [ProviderCapability.Limits, ProviderCapability.Usage, ProviderCapability.Spend], ProviderModuleStage.Prepared, ProviderReference.OpenUsage, credential: ManualCredentialKind.ApiKey),
            Module("deepseek", "DeepSeek", [ProviderCapability.Limits, ProviderCapability.Usage, ProviderCapability.Spend], ProviderModuleStage.Prepared, ProviderReference.OpenUsage, credential: ManualCredentialKind.ApiKey),
            Module("xai", "xAI API", [ProviderCapability.Limits, ProviderCapability.Usage, ProviderCapability.Spend], ProviderModuleStage.Prepared, ProviderReference.OpenUsage, credential: ManualCredentialKind.ApiKey),
            Module("zai", "Z.ai", [ProviderCapability.Limits, ProviderCapability.Usage], ProviderModuleStage.PolicyBlocked, ProviderReference.OpenUsage | ProviderReference.CodexBar),
            Module("gemini-api", "Gemini API", [ProviderCapability.Limits, ProviderCapability.Usage, ProviderCapability.Spend], ProviderModuleStage.Prepared, ProviderReference.OpenUsage, credential: ManualCredentialKind.ApiKey),
            Module("gemini-cli", "Gemini CLI", [ProviderCapability.LocalUsage, ProviderCapability.Spend], ProviderModuleStage.Prepared, AllReferences),
            Module("ollama", "Ollama", [ProviderCapability.LocalUsage], ProviderModuleStage.Prepared, ProviderReference.OpenUsage | ProviderReference.CodexBar),
            Module("copilot", "GitHub Copilot", [ProviderCapability.Usage, ProviderCapability.Spend], ProviderModuleStage.Prepared, AllReferences, credential: ManualCredentialKind.ApiKeyAndOptionalOrganization),
            Module("devin", "Devin", [ProviderCapability.Usage], ProviderModuleStage.Prepared, AllReferences, credential: ManualCredentialKind.ApiKeyAndOrganization),
            Module("amp", "Amp", [ProviderCapability.LocalUsage, ProviderCapability.Spend], ProviderModuleStage.Active, OpenUsageAndCodeBurn),
            Module("goose", "Goose", [ProviderCapability.LocalUsage, ProviderCapability.Spend], ProviderModuleStage.Active, OpenUsageAndCodeBurn),
            Module("hermes", "Hermes", [ProviderCapability.LocalUsage, ProviderCapability.Spend], ProviderModuleStage.Active, OpenUsageAndCodeBurn),
            Module("mux", "Mux", [ProviderCapability.LocalUsage, ProviderCapability.Spend], ProviderModuleStage.Active, OpenUsageAndCodeBurn),
            Module("droid", "Droid", [ProviderCapability.LocalUsage, ProviderCapability.Spend], ProviderModuleStage.Prepared, OpenUsageAndCodeBurn),
            Module("crush", "Crush", [ProviderCapability.LocalUsage, ProviderCapability.Spend], ProviderModuleStage.Prepared, OpenUsageAndCodeBurn),
            Module("roo-code", "Roo Code", [ProviderCapability.LocalUsage, ProviderCapability.Spend], ProviderModuleStage.Prepared, OpenUsageAndCodeBurn, ["roocode"]),
            Module("kilo-code", "Kilo Code", [ProviderCapability.LocalUsage, ProviderCapability.Spend], ProviderModuleStage.PolicyBlocked, OpenUsageAndCodeBurn, ["kilocode"]),
            Module("kiro", "Kiro", [ProviderCapability.LocalUsage, ProviderCapability.Spend], ProviderModuleStage.Prepared, OpenUsageAndCodeBurn, ["kiro-cli"]),
            Module("zed", "Zed", [ProviderCapability.LocalUsage], ProviderModuleStage.PolicyBlocked, OpenUsageAndCodeBurn),
            Module("codebuff", "Codebuff", [ProviderCapability.LocalUsage, ProviderCapability.Spend], ProviderModuleStage.Prepared, OpenUsageAndCodeBurn),
            Module("kimi-cli", "Kimi CLI", [ProviderCapability.LocalUsage, ProviderCapability.Spend], ProviderModuleStage.PolicyBlocked, OpenUsageAndCodeBurn),
            Module("openclaw", "OpenClaw", [ProviderCapability.LocalUsage, ProviderCapability.Spend], ProviderModuleStage.Prepared, OpenUsageAndCodeBurn),
            Module("pi", "Pi", [ProviderCapability.LocalUsage, ProviderCapability.Spend], ProviderModuleStage.Prepared, OpenUsageAndCodeBurn),
            Module("qwen-cli", "Qwen CLI", [ProviderCapability.LocalUsage, ProviderCapability.Spend], ProviderModuleStage.Prepared, OpenUsageAndCodeBurn),

            Module("cline", "Cline", [ProviderCapability.Usage, ProviderCapability.Spend], ProviderModuleStage.PolicyBlocked, ProviderReference.CodexBar | ProviderReference.CodeBurn),
            Module("cline-cli", "Cline CLI", [ProviderCapability.LocalUsage, ProviderCapability.Spend], ProviderModuleStage.PolicyBlocked, ProviderReference.CodeBurn),
            Module("codewhale", "CodeWhale", [ProviderCapability.LocalUsage, ProviderCapability.Spend], ProviderModuleStage.Prepared, ProviderReference.CodeBurn),
            Module("cursor-agent", "Cursor Agent", [ProviderCapability.LocalUsage, ProviderCapability.Spend], ProviderModuleStage.Prepared, ProviderReference.CodeBurn),
            Module("forge", "Forge", [ProviderCapability.LocalUsage, ProviderCapability.Spend], ProviderModuleStage.Prepared, ProviderReference.CodeBurn),
            Module("ibm-bob", "IBM Bob", [ProviderCapability.LocalUsage, ProviderCapability.Spend], ProviderModuleStage.Prepared, ProviderReference.CodeBurn),
            Module("kimi-code", "Kimi Code", [ProviderCapability.LocalUsage, ProviderCapability.Spend], ProviderModuleStage.PolicyBlocked, ProviderReference.CodeBurn),
            Module("lingtai-tui", "LingTai TUI", [ProviderCapability.LocalUsage, ProviderCapability.Spend], ProviderModuleStage.Prepared, ProviderReference.CodeBurn),
            Module("mistral-vibe", "Mistral Vibe", [ProviderCapability.LocalUsage, ProviderCapability.Spend], ProviderModuleStage.Prepared, ProviderReference.CodeBurn),
            Module("openclaude", "OpenClaude", [ProviderCapability.LocalUsage, ProviderCapability.Spend], ProviderModuleStage.Prepared, ProviderReference.CodeBurn),
            Module("open-design", "OpenDesign", [ProviderCapability.LocalUsage, ProviderCapability.Spend], ProviderModuleStage.Prepared, ProviderReference.CodeBurn),
            Module("omp", "OMP", [ProviderCapability.LocalUsage, ProviderCapability.Spend], ProviderModuleStage.Prepared, ProviderReference.CodeBurn),
            Module("quickdesk", "QuickDesk", [ProviderCapability.LocalUsage, ProviderCapability.Spend], ProviderModuleStage.Prepared, ProviderReference.CodeBurn),
            Module("vercel-ai-gateway", "Vercel AI Gateway", [ProviderCapability.Limits, ProviderCapability.Spend], ProviderModuleStage.OptIn, ProviderReference.CodeBurn, credential: ManualCredentialKind.ApiKeyAndOptionalKeyId),
            Module("warp", "Warp", [ProviderCapability.LocalUsage, ProviderCapability.Spend], ProviderModuleStage.Prepared, ProviderReference.CodeBurn),
            Module("zcode", "ZCode", [ProviderCapability.LocalUsage, ProviderCapability.Spend], ProviderModuleStage.PolicyBlocked, ProviderReference.CodeBurn),
            Module("zerostack", "ZeroStack", [ProviderCapability.LocalUsage, ProviderCapability.Spend], ProviderModuleStage.Prepared, ProviderReference.CodeBurn),
        ]);

    public static IReadOnlyList<ProviderModuleDefinition> Entries { get; } = Catalog;

    public static IReadOnlyList<ProviderModuleDefinition> OpenUsageEntries { get; } =
        Array.AsReadOnly(Catalog.Where(entry => entry.IsOpenUsageProvider).ToArray());

    public static IReadOnlyList<ProviderModuleDefinition> CodexBarEntries { get; } =
        Array.AsReadOnly(Catalog.Where(entry => entry.IsCodexBarProvider).ToArray());

    public static IReadOnlyList<ProviderModuleDefinition> CodeBurnEntries { get; } =
        Array.AsReadOnly(Catalog.Where(entry => entry.IsCodeBurnProvider).ToArray());

    /// <summary>
    /// Tools the app reads from disk today: shipped, not opt-in, not prepared, and able to
    /// report local usage. A screen that lists local providers asks this instead of keeping
    /// its own list of IDs.
    /// </summary>
    public static IReadOnlyList<ProviderModuleDefinition> ActiveLocalUsageEntries { get; } =
        Array.AsReadOnly(Catalog
            .Where(entry => entry.Stage == ProviderModuleStage.Active
                && entry.Capabilities.Contains(ProviderCapability.LocalUsage))
            .ToArray());

    /// <summary>
    /// Providers that accept a key the user pastes into TokenUsage. The list can store that
    /// key before a live client exists. Policy-blocked providers are never included.
    /// </summary>
    public static IReadOnlyList<ProviderModuleDefinition> ManualCredentialEntries { get; } =
        Array.AsReadOnly(Catalog.Where(entry => entry.AcceptsManualCredential).ToArray());

    private static readonly HashSet<string> ActiveLocalUsageIds = ActiveLocalUsageEntries
        .Select(entry => entry.Id.Value)
        .ToHashSet(StringComparer.Ordinal);

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

    private static ProviderModuleDefinition Module(
        string id,
        string displayName,
        ProviderCapability[] capabilities,
        ProviderModuleStage stage,
        ProviderReference references,
        string[]? aliases = null,
        bool quotaBlocked = false,
        ManualCredentialKind credential = ManualCredentialKind.None) =>
        new(id, displayName, capabilities, stage, references, aliases, quotaBlocked, credential);
}

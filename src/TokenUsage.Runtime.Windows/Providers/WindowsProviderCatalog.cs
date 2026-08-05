using TokenUsage.Core.Cache;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;
using TokenUsage.Providers.Antigravity;
using TokenUsage.Providers.Claude;
using TokenUsage.Providers.Codex;
using TokenUsage.Providers.Grok;
using TokenUsage.Providers.OpenCode;
using TokenUsage.Runtime.Windows.Codex;
using TokenUsage.Runtime.Windows.VercelAiGateway;

namespace TokenUsage.Runtime.Windows.Providers;

public sealed class WindowsProviderCatalogEntry
{
    private readonly Func<CompositionContext, ProviderBinding>? _compose;
    private readonly Func<string, IRootDetectingUsageEventSource>? _localUsageFactory;

    internal WindowsProviderCatalogEntry(
        string id,
        string displayName,
        IEnumerable<ProviderCapability> capabilities,
        string? cacheDirectoryName,
        string? localUsageAgentId,
        string? detectionCheckId,
        string dataCheckId,
        bool isEnabledByDefault = true,
        Func<CompositionContext, ProviderBinding>? compose = null,
        Func<string, IRootDetectingUsageEventSource>? localUsageFactory = null)
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

        if (cacheDirectoryName is not null)
        {
            _ = new ProviderId(cacheDirectoryName);
        }

        DisplayName = displayName;
        Capabilities = Array.AsReadOnly(capabilityArray);
        CacheDirectoryName = cacheDirectoryName;
        LocalUsageAgentId = localUsageAgentId is null ? null : new AgentId(localUsageAgentId);
        DetectionCheckId = detectionCheckId;
        ArgumentException.ThrowIfNullOrWhiteSpace(dataCheckId);
        DataCheckId = dataCheckId;
        IsEnabledByDefault = isEnabledByDefault;
        _compose = compose;
        _localUsageFactory = localUsageFactory;
        if (_compose is null && _localUsageFactory is null)
        {
            throw new ArgumentException("A provider integration factory is required.");
        }
    }

    public ProviderId Id { get; }

    public string DisplayName { get; }

    public IReadOnlyList<ProviderCapability> Capabilities { get; }

    public string? CacheDirectoryName { get; }

    public AgentId? LocalUsageAgentId { get; }

    public string? DetectionCheckId { get; }

    public string DataCheckId { get; }

    public bool IsEnabledByDefault { get; }

    public IRootDetectingUsageEventSource? CreateLocalUsageSource(string timeZoneId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);
        return _localUsageFactory?.Invoke(timeZoneId);
    }

    internal ProviderBinding Compose(CompositionContext context)
    {
        ProviderBinding binding = _compose?.Invoke(context) ?? new ProviderBinding();
        return binding with
        {
            LocalUsageSource = binding.LocalUsageSource
                ?? _localUsageFactory?.Invoke(context.TimeZoneId),
        };
    }
}

public sealed record WindowsProviderCompositionOptions(
    string? TimeZoneId = null,
    ICodexQuotaClientFactory? CodexClientFactory = null,
    VercelGatewayRefreshCoordinator? VercelCoordinator = null,
    bool EnableVercelGateway = false);

public sealed class WindowsProviderComposition
{
    internal WindowsProviderComposition(
        ProviderRefreshHost refreshHost,
        IReadOnlyList<IUsageEventSource> localUsageSources,
        VercelGatewayRefreshCoordinator? vercelCoordinator)
    {
        RefreshHost = refreshHost;
        LocalUsageSources = localUsageSources;
        VercelCoordinator = vercelCoordinator;
    }

    public ProviderRefreshHost RefreshHost { get; }

    public IReadOnlyList<IUsageEventSource> LocalUsageSources { get; }

    public VercelGatewayRefreshCoordinator? VercelCoordinator { get; }
}

public static class WindowsProviderCatalog
{
    private static readonly IReadOnlyList<WindowsProviderCatalogEntry> Catalog =
        Array.AsReadOnly<WindowsProviderCatalogEntry>(
        [
            new(
                "claude",
                "Claude",
                [ProviderCapability.LocalUsage],
                cacheDirectoryName: null,
                localUsageAgentId: "claude",
                detectionCheckId: null,
                dataCheckId: "local-usage-claude",
                localUsageFactory: timeZoneId => new ClaudeUsageEventSource(timeZoneId)),
            new(
                "codex",
                "Codex",
                [
                    ProviderCapability.Limits,
                    ProviderCapability.LocalUsage,
                    ProviderCapability.Spend,
                ],
                cacheDirectoryName: "codex",
                localUsageAgentId: "codex",
                detectionCheckId: "codex-cli",
                dataCheckId: "codex-cache",
                compose: context => new ProviderBinding(
                    RefreshRegistration: new CodexRefreshCoordinator(
                        context.CacheDirectory("codex"),
                        context.Clock,
                        context.CodexClientFactory).CreateRegistration(),
                    LocalUsageSource: new CodexUsageEventSource(
                        context.TimeZoneId,
                        clientFactory: context.CodexClientFactory)),
                localUsageFactory: timeZoneId => new CodexUsageEventSource(timeZoneId)),
            new(
                "grok",
                "Grok Build",
                [ProviderCapability.LocalUsage],
                cacheDirectoryName: null,
                localUsageAgentId: "grok",
                detectionCheckId: null,
                dataCheckId: "local-usage-grok",
                localUsageFactory: timeZoneId => new GrokUsageEventSource(timeZoneId)),
            new(
                "opencode",
                "OpenCode",
                [ProviderCapability.LocalUsage],
                cacheDirectoryName: null,
                localUsageAgentId: "opencode",
                detectionCheckId: null,
                dataCheckId: "local-usage-opencode",
                localUsageFactory: timeZoneId => new OpenCodeUsageEventSource(timeZoneId)),
            new(
                "antigravity",
                "Antigravity",
                [ProviderCapability.LocalUsage, ProviderCapability.Spend],
                cacheDirectoryName: null,
                localUsageAgentId: "antigravity",
                detectionCheckId: null,
                dataCheckId: "local-usage-antigravity",
                localUsageFactory: timeZoneId => new AntigravityUsageEventSource(timeZoneId)),
            new(
                "vercel-ai-gateway",
                "Vercel AI Gateway",
                [ProviderCapability.Limits, ProviderCapability.Spend],
                cacheDirectoryName: "vercel-ai-gateway",
                localUsageAgentId: null,
                detectionCheckId: "vercel-ai-gateway-credential",
                dataCheckId: "vercel-ai-gateway-cache",
                isEnabledByDefault: false,
                compose: CreateVercelBinding),
        ]);

    public static IReadOnlyList<WindowsProviderCatalogEntry> Entries { get; } =
        Array.AsReadOnly(Catalog.Where(entry => entry.IsEnabledByDefault).ToArray());

    public static IReadOnlyList<WindowsProviderCatalogEntry> DeferredEntries { get; } =
        Array.AsReadOnly(Catalog.Where(entry => !entry.IsEnabledByDefault).ToArray());

    public static WindowsProviderComposition CreateComposition(
        string dataDirectory,
        TimeProvider clock,
        HttpClient? vercelHttpClient = null,
        WindowsProviderCompositionOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentNullException.ThrowIfNull(clock);
        options ??= new WindowsProviderCompositionOptions();
        if (options.EnableVercelGateway && vercelHttpClient is null)
        {
            throw new ArgumentNullException(
                nameof(vercelHttpClient),
                "Vercel AI Gateway needs an HTTP client when it is enabled.");
        }

        var context = new CompositionContext(
            Path.GetFullPath(dataDirectory),
            options.TimeZoneId ?? TimeZoneInfo.Local.Id,
            clock,
            options.CodexClientFactory ?? new CodexAppServerQuotaClientFactory(clock),
            vercelHttpClient,
            options.VercelCoordinator);
        ProviderBinding[] bindings = Catalog
            .Where(entry => entry.IsEnabledByDefault || options.EnableVercelGateway)
            .Select(entry => entry.Compose(context))
            .ToArray();
        ProviderRefreshRegistration[] registrations = bindings
            .Where(binding => binding.RefreshRegistration is not null)
            .Select(binding => binding.RefreshRegistration!)
            .ToArray();
        IUsageEventSource[] usageSources = bindings
            .Where(binding => binding.LocalUsageSource is not null)
            .Select(binding => binding.LocalUsageSource!)
            .ToArray();
        VercelGatewayRefreshCoordinator? vercelCoordinator = bindings
            .Select(binding => binding.VercelCoordinator)
            .SingleOrDefault(coordinator => coordinator is not null);

        return new WindowsProviderComposition(
            new ProviderRefreshHost(registrations, clock),
            Array.AsReadOnly(usageSources),
            vercelCoordinator);
    }

    private static ProviderBinding CreateVercelBinding(CompositionContext context)
    {
        VercelGatewayRefreshCoordinator coordinator = context.VercelCoordinator
            ?? new VercelGatewayRefreshCoordinator(
                context.CacheDirectory("vercel-ai-gateway"),
                context.Clock,
                context.VercelHttpClient
                    ?? throw new InvalidOperationException(
                        "Vercel AI Gateway is enabled without an HTTP client."));
        return new ProviderBinding(
            RefreshRegistration: coordinator.CreateRegistration(),
            VercelCoordinator: coordinator);
    }
}

internal sealed record CompositionContext(
    string DataDirectory,
    string TimeZoneId,
    TimeProvider Clock,
    ICodexQuotaClientFactory CodexClientFactory,
    HttpClient? VercelHttpClient,
    VercelGatewayRefreshCoordinator? VercelCoordinator)
{
    public string CacheDirectory(string name) => Path.Combine(
        DataDirectory,
        "cache",
        "providers",
        name);
}

internal sealed record ProviderBinding(
    ProviderRefreshRegistration? RefreshRegistration = null,
    IUsageEventSource? LocalUsageSource = null,
    VercelGatewayRefreshCoordinator? VercelCoordinator = null);

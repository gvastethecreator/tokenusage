using TokenUsage.Core.Cache;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;
using TokenUsage.Providers.Antigravity;
using TokenUsage.Providers.Claude;
using TokenUsage.Providers.Codex;
using TokenUsage.Providers.Cursor;
using TokenUsage.Providers.Catalog;
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
        ProviderModuleDefinition module,
        string? cacheDirectoryName,
        string? localUsageAgentId,
        string? detectionCheckId,
        string? dataCheckId,
        Func<CompositionContext, ProviderBinding>? compose = null,
        Func<string, IRootDetectingUsageEventSource>? localUsageFactory = null)
    {
        Module = module ?? throw new ArgumentNullException(nameof(module));

        if (cacheDirectoryName is not null)
        {
            _ = new ProviderId(cacheDirectoryName);
        }

        CacheDirectoryName = cacheDirectoryName;
        LocalUsageAgentId = localUsageAgentId is null ? null : new AgentId(localUsageAgentId);
        DetectionCheckId = detectionCheckId;
        DataCheckId = dataCheckId;
        _compose = compose;
        _localUsageFactory = localUsageFactory;
        bool needsRuntime = module.Stage is ProviderModuleStage.Active or ProviderModuleStage.OptIn;
        if (needsRuntime && (_compose is null && _localUsageFactory is null))
        {
            throw new ArgumentException("A provider integration factory is required.");
        }

        if (needsRuntime && string.IsNullOrWhiteSpace(dataCheckId))
        {
            throw new ArgumentException("A provider data check is required.", nameof(dataCheckId));
        }

        if (!needsRuntime && (_compose is not null || _localUsageFactory is not null))
        {
            throw new ArgumentException("A prepared provider cannot activate a runtime factory.");
        }
    }

    public ProviderModuleDefinition Module { get; }

    public ProviderId Id => Module.Id;

    public string DisplayName => Module.DisplayName;

    public IReadOnlyList<ProviderCapability> Capabilities => Module.Capabilities;

    public ProviderModuleStage Stage => Module.Stage;

    public string? CacheDirectoryName { get; }

    public AgentId? LocalUsageAgentId { get; }

    public string? DetectionCheckId { get; }

    public string? DataCheckId { get; }

    public bool IsEnabledByDefault => Stage == ProviderModuleStage.Active;

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
    bool EnableVercelGateway = false,
    bool EnableClaudeLocalUsage = false);

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
                ProviderModuleCatalog.Get("claude"),
                cacheDirectoryName: null,
                localUsageAgentId: "claude",
                detectionCheckId: null,
                dataCheckId: "local-usage-claude",
                localUsageFactory: timeZoneId => new ClaudeUsageEventSource(timeZoneId)),
            new(
                ProviderModuleCatalog.Get("codex"),
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
                        clientFactory: context.CodexClientFactory,
                        checkpointPath: Path.Combine(
                            context.DataDirectory,
                            "scanner",
                            "codex-usage.v1.json"),
                        clock: context.Clock)),
                localUsageFactory: timeZoneId => new CodexUsageEventSource(timeZoneId)),
            new(
                ProviderModuleCatalog.Get("grok"),
                cacheDirectoryName: null,
                localUsageAgentId: "grok",
                detectionCheckId: null,
                dataCheckId: "local-usage-grok",
                localUsageFactory: timeZoneId => new GrokUsageEventSource(timeZoneId)),
            new(
                ProviderModuleCatalog.Get("opencode"),
                cacheDirectoryName: null,
                localUsageAgentId: "opencode",
                detectionCheckId: null,
                dataCheckId: "local-usage-opencode",
                localUsageFactory: timeZoneId => new OpenCodeUsageEventSource(timeZoneId)),
            new(
                ProviderModuleCatalog.Get("antigravity"),
                cacheDirectoryName: null,
                localUsageAgentId: "antigravity",
                detectionCheckId: null,
                dataCheckId: "local-usage-antigravity",
                localUsageFactory: timeZoneId => new AntigravityUsageEventSource(timeZoneId)),
            new(
                ProviderModuleCatalog.Get("cursor"),
                cacheDirectoryName: null,
                localUsageAgentId: "cursor",
                detectionCheckId: null,
                dataCheckId: "local-usage-cursor",
                localUsageFactory: timeZoneId => new CursorUsageEventSource(timeZoneId)),
            new(
                ProviderModuleCatalog.Get("copilot"),
                cacheDirectoryName: null,
                localUsageAgentId: null,
                detectionCheckId: null,
                dataCheckId: null),
            new(
                ProviderModuleCatalog.Get("devin"),
                cacheDirectoryName: null,
                localUsageAgentId: null,
                detectionCheckId: null,
                dataCheckId: null),
            new(
                ProviderModuleCatalog.Get("openrouter"),
                cacheDirectoryName: null,
                localUsageAgentId: null,
                detectionCheckId: null,
                dataCheckId: null),
            new(
                ProviderModuleCatalog.Get("zai"),
                cacheDirectoryName: null,
                localUsageAgentId: null,
                detectionCheckId: null,
                dataCheckId: null),
            new(
                ProviderModuleCatalog.Get("vercel-ai-gateway"),
                cacheDirectoryName: "vercel-ai-gateway",
                localUsageAgentId: null,
                detectionCheckId: "vercel-ai-gateway-credential",
                dataCheckId: "vercel-ai-gateway-cache",
                compose: CreateVercelBinding),
        ]);

    public static IReadOnlyList<WindowsProviderCatalogEntry> AllEntries { get; } = Catalog;

    public static IReadOnlyList<WindowsProviderCatalogEntry> Entries { get; } =
        Array.AsReadOnly(Catalog.Where(entry => entry.IsEnabledByDefault).ToArray());

    public static IReadOnlyList<WindowsProviderCatalogEntry> DeferredEntries { get; } =
        Array.AsReadOnly(Catalog.Where(entry => entry.Stage == ProviderModuleStage.OptIn).ToArray());

    public static IReadOnlyList<WindowsProviderCatalogEntry> PreparedEntries { get; } =
        Array.AsReadOnly(Catalog.Where(entry => entry.Stage == ProviderModuleStage.Prepared).ToArray());

    public static IReadOnlyList<WindowsProviderCatalogEntry> PolicyBlockedEntries { get; } =
        Array.AsReadOnly(Catalog.Where(entry => entry.Stage == ProviderModuleStage.PolicyBlocked).ToArray());

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
            .Where(entry => entry.Stage == ProviderModuleStage.Active
                || options.EnableClaudeLocalUsage && entry.Id.Value == "claude"
                || options.EnableVercelGateway && entry.Id.Value == "vercel-ai-gateway")
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

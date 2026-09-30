using TokenUsage.Core.Cache;
using TokenUsage.Core.Credentials;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;
using TokenUsage.Providers.Amp;
using TokenUsage.Providers.Antigravity;
using TokenUsage.Providers.Claude;
using TokenUsage.Providers.Codex;
using TokenUsage.Providers.CopilotVsCode;
using TokenUsage.Providers.Cursor;
using TokenUsage.Providers.Catalog;
using TokenUsage.Providers.Grok;
using TokenUsage.Providers.Goose;
using TokenUsage.Providers.Hermes;
using TokenUsage.Providers.Mux;
using TokenUsage.Providers.OpenCode;
using TokenUsage.Providers.Zcode;
using TokenUsage.Runtime.Windows.Codex;
using TokenUsage.Runtime.Windows.OpenRouter;
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

/// <summary>
/// <see cref="EnableOptInProviders"/> composes every opt-in provider (Vercel AI Gateway and
/// OpenRouter). An opt-in runtime reads its saved key first and returns "not configured"
/// without a network call when there is none, so saving a key is the user's opt-in.
/// </summary>
public sealed record WindowsProviderCompositionOptions(
    string? TimeZoneId = null,
    ICodexQuotaClientFactory? CodexClientFactory = null,
    VercelGatewayRefreshCoordinator? VercelCoordinator = null,
    bool EnableOptInProviders = false,
    IAttributionConsentSource? AttributionConsent = null,
    IOpaqueKeyDeriver? AttributionKeys = null,
    bool CodexActiveAccountOnly = false);

public sealed class WindowsProviderComposition
{
    private readonly IReadOnlyDictionary<string, ProviderRefreshRegistration> _manualKeyRegistrations;

    internal WindowsProviderComposition(
        ProviderRefreshHost refreshHost,
        IReadOnlyList<IUsageEventSource> localUsageSources,
        IReadOnlyDictionary<string, ProviderRefreshRegistration> manualKeyRegistrations,
        CodexAccountService accounts)
    {
        RefreshHost = refreshHost;
        LocalUsageSources = localUsageSources;
        _manualKeyRegistrations = manualKeyRegistrations;
        Accounts = accounts;
    }

    public ProviderRefreshHost RefreshHost { get; }

    public CodexAccountService Accounts { get; }

    public IReadOnlyList<IUsageEventSource> LocalUsageSources { get; }

    /// <summary>
    /// Removes the cached reading of a provider whose saved key just changed or was removed.
    /// It runs under the provider's operation gate, so a refresh that read the old key either
    /// finished before the removal or starts after it with the new key.
    /// </summary>
    public async Task<ManualCredentialChangeResult> ResetProviderCacheAsync(
        string providerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        if (!_manualKeyRegistrations.TryGetValue(
                providerId,
                out ProviderRefreshRegistration? registration))
        {
            return ManualCredentialChangeResult.NoLiveSource;
        }

        IAsyncDisposable? lease = registration.OperationGate is null
            ? null
            : await registration.OperationGate.EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            SnapshotCacheRemoveResult result = await registration.Store
                .RemoveProviderAsync(new ProviderId(providerId), CancellationToken.None)
                .ConfigureAwait(false);
            // An unreadable cache is quarantined, so the old reading is gone either way.
            return result is SnapshotCacheRemoveResult.Removed
                or SnapshotCacheRemoveResult.Missing
                or SnapshotCacheRemoveResult.Unreadable
                ? ManualCredentialChangeResult.CacheCleared
                : ManualCredentialChangeResult.CacheCleanupFailed;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or TimeoutException
            or InvalidOperationException)
        {
            return ManualCredentialChangeResult.CacheCleanupFailed;
        }
        finally
        {
            if (lease is not null)
            {
                await lease.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}

public static class WindowsProviderCatalog
{
    private static readonly IReadOnlyList<WindowsProviderCatalogEntry> IntegratedCatalog =
        Array.AsReadOnly<WindowsProviderCatalogEntry>(
        [
            new(
                ProviderModuleCatalog.Get("amp"),
                cacheDirectoryName: null,
                localUsageAgentId: "amp",
                detectionCheckId: null,
                dataCheckId: "local-usage-amp",
                localUsageFactory: timeZoneId => new AmpUsageEventSource(timeZoneId)),
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
                        clock: context.Clock,
                        attributionConsent: context.AttributionConsent,
                        attributionKeys: context.AttributionKeys)),
                localUsageFactory: timeZoneId => new CodexUsageEventSource(timeZoneId)),
            new(
                ProviderModuleCatalog.Get("grok"),
                cacheDirectoryName: null,
                localUsageAgentId: "grok",
                detectionCheckId: null,
                dataCheckId: "local-usage-grok",
                compose: context => new ProviderBinding(
                    LocalUsageSource: new GrokUsageEventSource(
                        context.TimeZoneId,
                        checkpointPath: Path.Combine(
                            context.DataDirectory,
                            "scanner",
                            "grok-usage.v1.json"),
                        clock: context.Clock)),
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
                compose: context => new ProviderBinding(
                    LocalUsageSource: new CursorUsageEventSource(
                        context.TimeZoneId,
                        clock: context.Clock,
                        attributionConsent: context.AttributionConsent,
                        attributionKeys: context.AttributionKeys)),
                localUsageFactory: timeZoneId => new CursorUsageEventSource(timeZoneId)),
            new(
                ProviderModuleCatalog.Get("zcode"),
                cacheDirectoryName: null,
                localUsageAgentId: "zcode",
                detectionCheckId: null,
                dataCheckId: "local-usage-zcode",
                localUsageFactory: timeZoneId => new ZcodeUsageEventSource(timeZoneId)),
            new(
                ProviderModuleCatalog.Get("mux"),
                cacheDirectoryName: null,
                localUsageAgentId: "mux",
                detectionCheckId: null,
                dataCheckId: "local-usage-mux",
                localUsageFactory: timeZoneId => new MuxUsageEventSource(timeZoneId)),
            new(
                ProviderModuleCatalog.Get("goose"),
                cacheDirectoryName: null,
                localUsageAgentId: "goose",
                detectionCheckId: null,
                dataCheckId: "local-usage-goose",
                localUsageFactory: timeZoneId => new GooseUsageEventSource(timeZoneId)),
            new(
                ProviderModuleCatalog.Get("hermes"),
                cacheDirectoryName: null,
                localUsageAgentId: "hermes",
                detectionCheckId: null,
                dataCheckId: "local-usage-hermes",
                localUsageFactory: timeZoneId => new HermesUsageEventSource(timeZoneId)),
            new(
                ProviderModuleCatalog.Get("copilot"),
                cacheDirectoryName: null,
                localUsageAgentId: "copilot",
                detectionCheckId: null,
                dataCheckId: "local-usage-copilot",
                compose: context => new ProviderBinding(
                    LocalUsageSource: new CopilotVsCodeUsageEventSource(
                        context.TimeZoneId,
                        clock: context.Clock)),
                localUsageFactory: timeZoneId => new CopilotVsCodeUsageEventSource(timeZoneId)),
            new(
                ProviderModuleCatalog.Get("devin"),
                cacheDirectoryName: null,
                localUsageAgentId: null,
                detectionCheckId: null,
                dataCheckId: null),
            new(
                ProviderModuleCatalog.Get("openrouter"),
                cacheDirectoryName: "openrouter",
                localUsageAgentId: null,
                detectionCheckId: null,
                dataCheckId: "openrouter-cache",
                compose: CreateOpenRouterBinding),
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

    private static readonly IReadOnlyList<WindowsProviderCatalogEntry> Catalog =
        Array.AsReadOnly(IntegratedCatalog
            .Concat(ProviderModuleCatalog.Entries
                .Where(module => IntegratedCatalog.All(entry => entry.Id != module.Id))
                .Select(module => new WindowsProviderCatalogEntry(
                    module,
                    cacheDirectoryName: null,
                    localUsageAgentId: null,
                    detectionCheckId: null,
                    dataCheckId: null)))
            .OrderBy(entry => entry.Module.Id.Value, StringComparer.Ordinal)
            .ToArray());

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
        HttpClient? apiHttpClient = null,
        WindowsProviderCompositionOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentNullException.ThrowIfNull(clock);
        options ??= new WindowsProviderCompositionOptions();
        if (options.EnableOptInProviders && apiHttpClient is null)
        {
            throw new ArgumentNullException(
                nameof(apiHttpClient),
                "Opt-in providers need an HTTP client when they are enabled.");
        }

        var context = new CompositionContext(
            Path.GetFullPath(dataDirectory),
            options.TimeZoneId ?? TimeZoneInfo.Local.Id,
            clock,
            options.CodexClientFactory ?? new CodexAppServerQuotaClientFactory(clock),
            apiHttpClient,
            options.VercelCoordinator,
            options.AttributionConsent,
            options.AttributionKeys);
        (WindowsProviderCatalogEntry Entry, ProviderBinding Binding)[] bindings = Catalog
            .Where(entry => entry.Stage == ProviderModuleStage.Active
                || options.EnableOptInProviders && entry.Stage == ProviderModuleStage.OptIn)
            .Select(entry => (entry, entry.Compose(context)))
            .ToArray();
        ProviderRefreshRegistration[] registrations = bindings
            .Where(item => item.Binding.RefreshRegistration is not null)
            .Select(item => item.Binding.RefreshRegistration!)
            .ToArray();
        IUsageEventSource[] usageSources = bindings
            .Where(item => item.Binding.LocalUsageSource is not null)
            .Select(item => item.Binding.LocalUsageSource!)
            .ToArray();
        Dictionary<string, ProviderRefreshRegistration> manualKeyRegistrations = bindings
            .Where(item => item.Entry.Module.AcceptsManualCredential
                && item.Binding.RefreshRegistration is not null)
            .ToDictionary(
                item => item.Entry.Id.Value,
                item => item.Binding.RefreshRegistration!,
                StringComparer.Ordinal);

        var accounts = new CodexAccountService(dataDirectory, clock);
        var refreshHost = new ProviderRefreshHost(registrations, clock)
        {
            ResolveRegistrationsAsync = (items, token) => accounts.ResolveAsync(items, token, options.CodexActiveAccountOnly),
        };
        return new WindowsProviderComposition(
            refreshHost,
            Array.AsReadOnly(usageSources),
            manualKeyRegistrations.AsReadOnly(), accounts);
    }

    private static ProviderBinding CreateVercelBinding(CompositionContext context)
    {
        VercelGatewayRefreshCoordinator coordinator = context.VercelCoordinator
            ?? new VercelGatewayRefreshCoordinator(
                context.CacheDirectory("vercel-ai-gateway"),
                context.Clock,
                context.RequireApiHttpClient());
        return new ProviderBinding(RefreshRegistration: coordinator.CreateRegistration());
    }

    private static ProviderBinding CreateOpenRouterBinding(CompositionContext context) =>
        new(RefreshRegistration: new OpenRouterRefreshCoordinator(
                context.CacheDirectory("openrouter"),
                context.Clock,
                context.RequireApiHttpClient())
            .CreateRegistration());
}

internal sealed record CompositionContext(
    string DataDirectory,
    string TimeZoneId,
    TimeProvider Clock,
    ICodexQuotaClientFactory CodexClientFactory,
    HttpClient? ApiHttpClient,
    VercelGatewayRefreshCoordinator? VercelCoordinator,
    IAttributionConsentSource? AttributionConsent = null,
    IOpaqueKeyDeriver? AttributionKeys = null)
{
    public string CacheDirectory(string name) => Path.Combine(
        DataDirectory,
        "cache",
        "providers",
        name);

    public HttpClient RequireApiHttpClient() => ApiHttpClient
        ?? throw new InvalidOperationException("An opt-in provider is enabled without an HTTP client.");
}

internal sealed record ProviderBinding(
    ProviderRefreshRegistration? RefreshRegistration = null,
    IUsageEventSource? LocalUsageSource = null);

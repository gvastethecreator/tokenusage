using TokenUsage.Core.Cache;
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
using TokenUsage.Runtime.Windows.Claude;

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
        if (_compose is null && _localUsageFactory is null)
        {
            throw new ArgumentException("A provider integration factory is required.");
        }

        if (string.IsNullOrWhiteSpace(dataCheckId))
        {
            throw new ArgumentException("A provider data check is required.", nameof(dataCheckId));
        }
    }

    public ProviderModuleDefinition Module { get; }

    public ProviderId Id => Module.Id;

    public string DisplayName => Module.DisplayName;

    public IReadOnlyList<ProviderCapability> Capabilities => Module.Capabilities;

    public string? CacheDirectoryName { get; }

    public AgentId? LocalUsageAgentId { get; }

    public string? DetectionCheckId { get; }

    public string? DataCheckId { get; }

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
    IAttributionConsentSource? AttributionConsent = null,
    IOpaqueKeyDeriver? AttributionKeys = null,
    bool CodexActiveAccountOnly = false);

public sealed class WindowsProviderComposition
{
    private readonly IReadOnlyDictionary<string, ProviderRefreshRegistration> _resettableRegistrations;

    internal WindowsProviderComposition(
        ProviderRefreshHost refreshHost,
        IReadOnlyList<IUsageEventSource> localUsageSources,
        IReadOnlyDictionary<string, ProviderRefreshRegistration> resettableRegistrations,
        CodexAccountService accounts)
    {
        RefreshHost = refreshHost;
        LocalUsageSources = localUsageSources;
        _resettableRegistrations = resettableRegistrations;
        Accounts = accounts;
    }

    public ProviderRefreshHost RefreshHost { get; }

    public CodexAccountService Accounts { get; }

    public IReadOnlyList<IUsageEventSource> LocalUsageSources { get; }

    /// <summary>
    /// Removes the cached Claude quota reading after limits are turned off. It runs under the
    /// provider's operation gate, so a refresh either finished before the removal or starts after it.
    /// </summary>
    public async Task<bool> ClearClaudeLimitsCacheAsync(CancellationToken cancellationToken = default)
    {
        if (!_resettableRegistrations.TryGetValue("claude", out ProviderRefreshRegistration? registration))
        {
            return true;
        }

        IAsyncDisposable? lease = registration.OperationGate is null
            ? null
            : await registration.OperationGate.EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            SnapshotCacheRemoveResult result = await registration.Store
                .RemoveProviderAsync(new ProviderId("claude"), CancellationToken.None)
                .ConfigureAwait(false);
            // An unreadable cache is quarantined, so the old reading is gone either way.
            return result is SnapshotCacheRemoveResult.Removed
                or SnapshotCacheRemoveResult.Missing
                or SnapshotCacheRemoveResult.Unreadable;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or TimeoutException
            or InvalidOperationException)
        {
            return false;
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
                cacheDirectoryName: "claude",
                localUsageAgentId: "claude",
                detectionCheckId: null,
                dataCheckId: "local-usage-claude",
                compose: context => new ProviderBinding(
                    RefreshRegistration: ClaudeUsageRuntime.CreateRegistration(
                        context.DataDirectory, context.CacheDirectory("claude"), context.Clock, context.TimeZoneId),
                    LocalUsageSource: new ClaudeUsageEventSource(context.TimeZoneId,
                        checkpointPath: Path.Combine(context.DataDirectory, "scanner", "claude-usage.v1.json"), clock: context.Clock)),
                localUsageFactory: timeZoneId => new ClaudeUsageEventSource(timeZoneId)),
            new(
                ProviderModuleCatalog.Get("codex"),
                cacheDirectoryName: "codex",
                localUsageAgentId: "codex",
                detectionCheckId: "codex-cli",
                dataCheckId: "codex-cache",
                compose: context => new ProviderBinding(
                    RefreshRegistration: CodexRefreshCoordinator.CreateRegistration(
                        context.CacheDirectory("codex"),
                        context.Clock,
                        context.CodexClientFactory),
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
        ]);

    public static IReadOnlyList<WindowsProviderCatalogEntry> Entries { get; } =
        Array.AsReadOnly(IntegratedCatalog
            .OrderBy(entry => entry.Module.Id.Value, StringComparer.Ordinal)
            .ToArray());

    public static WindowsProviderComposition CreateComposition(
        string dataDirectory,
        TimeProvider clock,
        WindowsProviderCompositionOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentNullException.ThrowIfNull(clock);
        options ??= new WindowsProviderCompositionOptions();
        var context = new CompositionContext(
            Path.GetFullPath(dataDirectory),
            options.TimeZoneId ?? TimeZoneInfo.Local.Id,
            clock,
            options.CodexClientFactory ?? new CodexAppServerQuotaClientFactory(clock),
            options.AttributionConsent,
            options.AttributionKeys);
        (WindowsProviderCatalogEntry Entry, ProviderBinding Binding)[] bindings = Entries
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
        Dictionary<string, ProviderRefreshRegistration> resettableRegistrations = bindings
            .Where(item => item.Entry.Id.Value == "claude"
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
            resettableRegistrations.AsReadOnly(),
            accounts);
    }
}

internal sealed record CompositionContext(
    string DataDirectory,
    string TimeZoneId,
    TimeProvider Clock,
    ICodexQuotaClientFactory CodexClientFactory,
    IAttributionConsentSource? AttributionConsent = null,
    IOpaqueKeyDeriver? AttributionKeys = null)
{
    public string CacheDirectory(string name) => Path.Combine(
        DataDirectory,
        "cache",
        "providers",
        name);
}

internal sealed record ProviderBinding(
    ProviderRefreshRegistration? RefreshRegistration = null,
    IUsageEventSource? LocalUsageSource = null);

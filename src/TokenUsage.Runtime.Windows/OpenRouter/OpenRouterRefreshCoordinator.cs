using TokenUsage.Core.Cache;
using TokenUsage.Core.Credentials;
using TokenUsage.Core.Providers;
using TokenUsage.Providers.OpenRouter;
using TokenUsage.Runtime.Windows.Credentials;

namespace TokenUsage.Runtime.Windows.OpenRouter;

/// <summary>
/// Owns the OpenRouter snapshot cache and runtime. The key comes from the same Windows
/// Credential Locker entry the provider list saves.
/// </summary>
public sealed class OpenRouterRefreshCoordinator
{
    public const string ProviderId = "openrouter";

    private readonly SnapshotStore _store;
    private readonly ProviderOperationGate _operationGate = new();
    private readonly IProviderRuntime _provider;

    public OpenRouterRefreshCoordinator(
        string cacheDirectory,
        TimeProvider clock,
        HttpClient httpClient)
        : this(
            CreateStore(cacheDirectory, clock),
            new ManualCredentialOpenRouterKeySource(new WindowsManualProviderCredentialStore()),
            new OpenRouterClient(httpClient ?? throw new ArgumentNullException(nameof(httpClient))))
    {
    }

    public OpenRouterRefreshCoordinator(
        SnapshotStore snapshotStore,
        IOpenRouterKeySource keySource,
        IOpenRouterClient client)
    {
        _store = snapshotStore ?? throw new ArgumentNullException(nameof(snapshotStore));
        _provider = new ResilientProviderRuntime(new OpenRouterProviderRuntime(
            keySource ?? throw new ArgumentNullException(nameof(keySource)),
            client ?? throw new ArgumentNullException(nameof(client))));
    }

    public ProviderRefreshRegistration CreateRegistration() =>
        new(_provider, _store, _operationGate);

    private static SnapshotStore CreateStore(string cacheDirectory, TimeProvider clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        ArgumentNullException.ThrowIfNull(clock);
        return new SnapshotStore(
            Path.Combine(Path.GetFullPath(cacheDirectory), SnapshotStore.DefaultFileName),
            clock);
    }
}

/// <summary>
/// Reads the OpenRouter key from the manual credential store. A stored entry that cannot be
/// read counts as no key, so a damaged entry shows as "not configured" instead of failing.
/// </summary>
public sealed class ManualCredentialOpenRouterKeySource : IOpenRouterKeySource
{
    private readonly IManualProviderCredentialStore _credentials;

    public ManualCredentialOpenRouterKeySource(IManualProviderCredentialStore credentials)
    {
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
    }

    public async Task<string?> ReadApiKeyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            ManualProviderSecret? secret = await _credentials
                .ReadAsync(OpenRouterRefreshCoordinator.ProviderId, cancellationToken)
                .ConfigureAwait(false);
            return secret?.ApiKey;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }
}

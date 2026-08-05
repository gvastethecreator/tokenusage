using TokenUsage.Core.Cache;
using TokenUsage.Core.Providers;
using TokenUsage.Providers.VercelAiGateway;

namespace TokenUsage.Runtime.Windows.VercelAiGateway;

public sealed class VercelGatewayRefreshCoordinator
{
    private readonly SnapshotStore _store;
    private readonly ProviderOperationGate _operationGate;
    private readonly IProviderRuntime _provider;
    private readonly ProviderRefreshHost _refreshHost;

    public VercelGatewayRefreshCoordinator(
        string cacheDirectory,
        TimeProvider clock,
        HttpClient httpClient)
        : this(
            CreateStore(cacheDirectory, clock),
            new VercelGatewayCredentialStore(),
            new VercelGatewayReportClient(
                httpClient ?? throw new ArgumentNullException(nameof(httpClient))),
            new VercelGatewayQuotaClient(httpClient),
            clock)
    {
    }

    public VercelGatewayRefreshCoordinator(
        SnapshotStore snapshotStore,
        IVercelGatewayCredentialStore credentialStore,
        IVercelGatewayReportClient reportClient,
        IVercelGatewayQuotaClient quotaClient,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(snapshotStore);
        ArgumentNullException.ThrowIfNull(credentialStore);
        ArgumentNullException.ThrowIfNull(reportClient);
        ArgumentNullException.ThrowIfNull(quotaClient);
        Clock = clock ?? throw new ArgumentNullException(nameof(clock));

        _store = snapshotStore;
        _operationGate = new ProviderOperationGate();
        _provider = new ResilientProviderRuntime(
            new VercelGatewayProviderRuntime(credentialStore, reportClient, quotaClient));
        _refreshHost = new ProviderRefreshHost([CreateRegistration()], clock);
        Connections = new VercelGatewayConnectionService(
            credentialStore,
            snapshotStore,
            _operationGate);
    }

    public TimeProvider Clock { get; }

    public VercelGatewayConnectionService Connections { get; }

    public ProviderRefreshRegistration CreateRegistration() =>
        new(_provider, _store, _operationGate);

    public IAsyncEnumerable<CacheFirstEvent> RunAsync(
        bool forceRefresh,
        CancellationToken cancellationToken) =>
        _refreshHost.RunAsync(forceRefresh, cancellationToken);

    private static SnapshotStore CreateStore(string cacheDirectory, TimeProvider clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        ArgumentNullException.ThrowIfNull(clock);
        string cachePath = Path.Combine(
            Path.GetFullPath(cacheDirectory),
            SnapshotStore.DefaultFileName);
        return new SnapshotStore(cachePath, clock);
    }
}

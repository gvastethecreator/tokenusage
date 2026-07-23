using WOpenUsage.Core.Cache;
using WOpenUsage.Core.Providers;
using WOpenUsage.Providers.VercelAiGateway;

namespace WOpenUsage.Runtime.Windows.VercelAiGateway;

public sealed class VercelGatewayRefreshCoordinator
{
    private readonly ProviderOperationGate _operationGate;
    private readonly CacheFirstRefresh _refresh;

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

        _operationGate = new ProviderOperationGate();
        var provider = new ResilientProviderRuntime(
            new VercelGatewayProviderRuntime(credentialStore, reportClient, quotaClient));
        _refresh = new CacheFirstRefresh(
            snapshotStore,
            [provider],
            clock,
            _operationGate);
        Connections = new VercelGatewayConnectionService(
            credentialStore,
            snapshotStore,
            _operationGate);
    }

    public TimeProvider Clock { get; }

    public VercelGatewayConnectionService Connections { get; }

    public IAsyncEnumerable<CacheFirstEvent> RunAsync(
        bool forceRefresh,
        CancellationToken cancellationToken) =>
        _refresh.RunAsync(forceRefresh, cancellationToken);

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

using WOpenUsage.Core.Cache;
using WOpenUsage.Core.Providers;
using WOpenUsage.Providers.Codex;

namespace WOpenUsage.Runtime.Windows.Codex;

public sealed class CodexRefreshCoordinator
{
    private readonly SnapshotStore _store;
    private readonly IProviderRuntime _provider;
    private readonly CacheFirstRefresh _refresh;

    public CodexRefreshCoordinator(
        string cacheDirectory,
        TimeProvider clock,
        ICodexQuotaClientFactory clientFactory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        Clock = clock ?? throw new ArgumentNullException(nameof(clock));
        ArgumentNullException.ThrowIfNull(clientFactory);

        string cachePath = Path.Combine(
            Path.GetFullPath(cacheDirectory),
            SnapshotStore.DefaultFileName);
        _store = new SnapshotStore(cachePath, clock);
        _provider = new ResilientProviderRuntime(new CodexProviderRuntime(clientFactory));
        _refresh = new CacheFirstRefresh(_store, [_provider], clock);
    }

    public TimeProvider Clock { get; }

    public ProviderRefreshRegistration CreateRegistration() =>
        new(_provider, _store);

    public IAsyncEnumerable<CacheFirstEvent> RunAsync(
        bool forceRefresh,
        CancellationToken cancellationToken) =>
        _refresh.RunAsync(forceRefresh, cancellationToken);
}

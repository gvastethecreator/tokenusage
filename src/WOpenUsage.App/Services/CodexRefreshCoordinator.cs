using WOpenUsage.Core.Cache;
using WOpenUsage.Core.Providers;
using WOpenUsage.Providers.Codex;

namespace WOpenUsage.App.Services;

public sealed class CodexRefreshCoordinator
{
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
        var store = new SnapshotStore(cachePath, clock);
        var provider = new ResilientProviderRuntime(new CodexProviderRuntime(clientFactory));
        _refresh = new CacheFirstRefresh(store, [provider], clock);
    }

    public TimeProvider Clock { get; }

    public IAsyncEnumerable<CacheFirstEvent> RunAsync(
        bool forceRefresh,
        CancellationToken cancellationToken) =>
        _refresh.RunAsync(forceRefresh, cancellationToken);
}

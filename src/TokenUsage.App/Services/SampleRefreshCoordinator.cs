using WOpenUsage.App.ViewModels.Sample;
using WOpenUsage.Core.Cache;
using WOpenUsage.Core.Providers;
using WOpenUsage.Providers.Fakes;

namespace WOpenUsage.App.Services;

public sealed class SampleRefreshCoordinator
{
    private static readonly ProviderDescriptor CodexDescriptor =
        new(new ProviderId("codex"), "Codex", isExperimental: true);
    private readonly string _cacheDirectory;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _providerDelay;

    public SampleRefreshCoordinator(
        string cacheDirectory,
        TimeProvider clock,
        TimeSpan? providerDelay = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        _cacheDirectory = Path.GetFullPath(cacheDirectory);
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _providerDelay = providerDelay ?? TimeSpan.FromMilliseconds(1200);
        if (_providerDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(providerDelay));
        }
    }

    public TimeProvider Clock => _clock;

    public IAsyncEnumerable<CacheFirstEvent> RunAsync(
        SampleScenario scenario,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        var provider = new FakeProviderRuntime(
            MapScenario(scenario),
            _providerDelay,
            CodexDescriptor);
        var store = new SnapshotStore(GetCachePath(scenario), _clock);
        var refresh = new CacheFirstRefresh(store, [provider], _clock);
        return refresh.RunAsync(forceRefresh, cancellationToken);
    }

    private string GetCachePath(SampleScenario scenario) =>
        Path.Combine(
            _cacheDirectory,
            GetCachePartition(scenario),
            SnapshotStore.DefaultFileName);

    private static string GetCachePartition(SampleScenario scenario) =>
        scenario switch
        {
            SampleScenario.Normal or SampleScenario.Error => "normal",
            SampleScenario.NearLimit => "near-limit",
            SampleScenario.Partial => "partial",
            SampleScenario.Stale => "stale",
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };

    private static FakeProviderScenario MapScenario(SampleScenario scenario) =>
        scenario switch
        {
            SampleScenario.Normal => FakeProviderScenario.Success,
            SampleScenario.NearLimit => FakeProviderScenario.NearLimit,
            SampleScenario.Partial => FakeProviderScenario.Partial,
            SampleScenario.Stale => FakeProviderScenario.Stale,
            SampleScenario.Error => FakeProviderScenario.Error,
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
}

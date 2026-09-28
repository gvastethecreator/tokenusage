using TokenUsage.App.ViewModels.Sample;
using TokenUsage.Core.Cache;
using TokenUsage.Core.Providers;
using TokenUsage.Providers.Fakes;

namespace TokenUsage.App.Services;

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
        (FakeProviderScenario providerScenario, string cachePartition) = scenario switch
        {
            SampleScenario.Normal => (FakeProviderScenario.Success, "normal"),
            SampleScenario.NearLimit => (FakeProviderScenario.NearLimit, "near-limit"),
            SampleScenario.Partial => (FakeProviderScenario.Partial, "partial"),
            SampleScenario.Stale => (FakeProviderScenario.Stale, "stale"),
            SampleScenario.Error => (FakeProviderScenario.Error, "normal"),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
        var provider = new FakeProviderRuntime(
            providerScenario,
            _providerDelay,
            CodexDescriptor);
        var store = new SnapshotStore(
            Path.Combine(_cacheDirectory, cachePartition, SnapshotStore.DefaultFileName),
            _clock);
        var refresh = new CacheFirstRefresh(store, [provider], _clock);
        return refresh.RunAsync(forceRefresh, cancellationToken);
    }
}

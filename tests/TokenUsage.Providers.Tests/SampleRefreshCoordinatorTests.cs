using WOpenUsage.App.Services;
using WOpenUsage.App.ViewModels.Sample;
using WOpenUsage.Core.Cache;
using WOpenUsage.Core.Providers;

namespace WOpenUsage.Providers.Tests;

public sealed class SampleRefreshCoordinatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ScenariosUseSeparateCachePartitions()
    {
        using var folder = new TemporaryFolder();
        var coordinator = new SampleRefreshCoordinator(
            folder.Path,
            new FixedTimeProvider(Now),
            TimeSpan.Zero);

        await CollectAsync(coordinator.RunAsync(SampleScenario.Normal, true, CancellationToken.None));
        await CollectAsync(coordinator.RunAsync(SampleScenario.NearLimit, true, CancellationToken.None));

        SnapshotCacheReadResult.Loaded normal = await LoadAsync(folder.Path, "normal");
        SnapshotCacheReadResult.Loaded nearLimit = await LoadAsync(folder.Path, "near-limit");
        Assert.Equal(58m, Remaining(Assert.Single(normal.Snapshots)));
        Assert.Equal(8m, Remaining(Assert.Single(nearLimit.Snapshots)));
    }

    [Fact]
    public async Task ErrorUsesNormalLastGoodWithoutCreatingAnErrorPartition()
    {
        using var folder = new TemporaryFolder();
        var coordinator = new SampleRefreshCoordinator(
            folder.Path,
            new FixedTimeProvider(Now),
            TimeSpan.Zero);
        await CollectAsync(coordinator.RunAsync(SampleScenario.Normal, true, CancellationToken.None));

        IReadOnlyList<CacheFirstEvent> events = await CollectAsync(
            coordinator.RunAsync(SampleScenario.Error, true, CancellationToken.None));

        CacheFirstEvent.ProviderCompleted completed = Assert.IsType<CacheFirstEvent.ProviderCompleted>(events[1]);
        ProviderOutcome.TransientFailure failure = Assert.IsType<ProviderOutcome.TransientFailure>(completed.Outcome);
        Assert.NotNull(failure.LastGood);
        Assert.False(Directory.Exists(Path.Combine(folder.Path, "error")));
    }

    [Fact]
    public async Task FirstErrorHasNoSyntheticLastGood()
    {
        using var folder = new TemporaryFolder();
        var coordinator = new SampleRefreshCoordinator(
            folder.Path,
            new FixedTimeProvider(Now),
            TimeSpan.Zero);

        IReadOnlyList<CacheFirstEvent> events = await CollectAsync(
            coordinator.RunAsync(SampleScenario.Error, true, CancellationToken.None));

        Assert.IsType<SnapshotCacheReadResult.Empty>(
            Assert.IsType<CacheFirstEvent.CachePublished>(events[0]).ReadResult);
        ProviderOutcome.TransientFailure failure = Assert.IsType<ProviderOutcome.TransientFailure>(
            Assert.IsType<CacheFirstEvent.ProviderCompleted>(events[1]).Outcome);
        Assert.Null(failure.LastGood);
    }

    private static async Task<SnapshotCacheReadResult.Loaded> LoadAsync(
        string root,
        string partition)
    {
        var store = new SnapshotStore(
            Path.Combine(root, partition, SnapshotStore.DefaultFileName),
            new FixedTimeProvider(Now));
        return Assert.IsType<SnapshotCacheReadResult.Loaded>(await store.LoadAsync());
    }

    private static async Task<IReadOnlyList<CacheFirstEvent>> CollectAsync(
        IAsyncEnumerable<CacheFirstEvent> source)
    {
        var events = new List<CacheFirstEvent>();
        await foreach (CacheFirstEvent item in source)
        {
            events.Add(item);
        }

        return events;
    }

    private static decimal Remaining(ProviderSnapshot snapshot) =>
        Assert.IsType<ProgressMetricSnapshot>(snapshot.Metrics[0]).RemainingPercent;

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class TemporaryFolder : IDisposable
    {
        public TemporaryFolder()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "wopenusage-sample-coordinator-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}

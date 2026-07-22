using WOpenUsage.Core.Cache;
using WOpenUsage.Core.Providers;

namespace WOpenUsage.Core.Tests;

public sealed class CacheFirstRefreshTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CacheIsPublishedBeforeProviderRefreshStarts()
    {
        using var folder = new TemporaryFolder();
        var clock = new FixedTimeProvider(Now);
        var store = new SnapshotStore(folder.DocumentPath, clock);
        ProviderSnapshot cached = CreateSnapshot(10m);
        ProviderSnapshot refreshed = CreateSnapshot(80m);
        await store.UpsertLastGoodAsync(cached);
        var provider = new RecordingProvider(new ProviderOutcome.Success(refreshed));
        var refresh = new CacheFirstRefresh(store, [provider], clock);

        await using IAsyncEnumerator<CacheFirstEvent> events = refresh.RunAsync().GetAsyncEnumerator();

        Assert.True(await events.MoveNextAsync());
        CacheFirstEvent.CachePublished first = Assert.IsType<CacheFirstEvent.CachePublished>(events.Current);
        Assert.Equal(0, provider.RefreshCalls);
        Assert.Equal(cached.ProviderId, first.Snapshots.Single().ProviderId);
        Assert.Equal(10m, ProgressUsed(first.Snapshots.Single()));

        Assert.True(await events.MoveNextAsync());
        CacheFirstEvent.ProviderCompleted second = Assert.IsType<CacheFirstEvent.ProviderCompleted>(events.Current);
        Assert.Equal(1, provider.RefreshCalls);
        Assert.NotNull(provider.LastContext?.LastGood);
        Assert.Equal(10m, ProgressUsed(provider.LastContext.LastGood));
        Assert.True(second.CacheUpdated);
        Assert.Equal(CacheUpdateStatus.Updated, second.CacheStatus);
        Assert.IsType<ProviderOutcome.Success>(second.Outcome);

        SnapshotCacheReadResult.Loaded saved = Assert.IsType<SnapshotCacheReadResult.Loaded>(
            await store.LoadAsync());
        Assert.Equal(80m, ProgressUsed(Assert.Single(saved.Snapshots)));
    }

    [Fact]
    public async Task FailureDoesNotOverwriteLastGoodSnapshot()
    {
        using var folder = new TemporaryFolder();
        var clock = new FixedTimeProvider(Now);
        var store = new SnapshotStore(folder.DocumentPath, clock);
        await store.UpsertLastGoodAsync(CreateSnapshot(25m));
        var provider = new RecordingProvider(
            new ProviderOutcome.TransientFailure(
                new ProviderError(ProviderErrorCode.TransientSourceFailure, "Synthetic source unavailable."),
                null));
        var refresh = new CacheFirstRefresh(store, [provider], clock);

        IReadOnlyList<CacheFirstEvent> events = await CollectAsync(refresh.RunAsync());

        CacheFirstEvent.ProviderCompleted completed = Assert.IsType<CacheFirstEvent.ProviderCompleted>(events[1]);
        Assert.False(completed.CacheUpdated);
        Assert.Equal(CacheUpdateStatus.NotAttempted, completed.CacheStatus);
        SnapshotCacheReadResult.Loaded saved = Assert.IsType<SnapshotCacheReadResult.Loaded>(
            await store.LoadAsync());
        Assert.Equal(25m, ProgressUsed(Assert.Single(saved.Snapshots)));
    }

    [Fact]
    public async Task PartialSuccessUpdatesLastGoodSnapshot()
    {
        using var folder = new TemporaryFolder();
        var clock = new FixedTimeProvider(Now);
        var store = new SnapshotStore(folder.DocumentPath, clock);
        var provider = new RecordingProvider(
            new ProviderOutcome.PartialSuccess(
                CreateSnapshot(65m, CoverageKind.Partial),
                [new ProviderWarning(ProviderWarningCode.PartialCoverage, "Synthetic coverage is partial.")]));
        var refresh = new CacheFirstRefresh(store, [provider], clock);

        IReadOnlyList<CacheFirstEvent> events = await CollectAsync(refresh.RunAsync());

        Assert.IsType<CacheFirstEvent.CachePublished>(events[0]);
        CacheFirstEvent.ProviderCompleted completed = Assert.IsType<CacheFirstEvent.ProviderCompleted>(events[1]);
        Assert.True(completed.CacheUpdated);
        Assert.Equal(CacheUpdateStatus.Updated, completed.CacheStatus);
        SnapshotCacheReadResult.Loaded saved = Assert.IsType<SnapshotCacheReadResult.Loaded>(
            await store.LoadAsync());
        Assert.Equal(CoverageKind.Partial, Assert.Single(saved.Snapshots).Coverage);
    }

    [Fact]
    public async Task MismatchedProviderSnapshotBecomesContractFailureAndIsNotCached()
    {
        using var folder = new TemporaryFolder();
        var clock = new FixedTimeProvider(Now);
        var store = new SnapshotStore(folder.DocumentPath, clock);
        await store.UpsertLastGoodAsync(CreateSnapshot(25m));
        var provider = new RecordingProvider(
            new ProviderOutcome.Success(CreateSnapshot(75m, providerId: "other")));
        var refresh = new CacheFirstRefresh(store, [provider], clock);

        IReadOnlyList<CacheFirstEvent> events = await CollectAsync(refresh.RunAsync());

        CacheFirstEvent.ProviderCompleted completed = Assert.IsType<CacheFirstEvent.ProviderCompleted>(events[1]);
        ProviderOutcome.ContractFailure failure = Assert.IsType<ProviderOutcome.ContractFailure>(completed.Outcome);
        Assert.Equal(ProviderErrorCode.ContractViolation, failure.Error.Code);
        Assert.False(completed.CacheUpdated);
        Assert.Equal(CacheUpdateStatus.NotAttempted, completed.CacheStatus);
        SnapshotCacheReadResult.Loaded saved = Assert.IsType<SnapshotCacheReadResult.Loaded>(
            await store.LoadAsync());
        Assert.Equal(25m, ProgressUsed(Assert.Single(saved.Snapshots)));
    }

    [Fact]
    public async Task CacheIoFailureStillPublishesSuccessfulProviderOutcome()
    {
        using var folder = new TemporaryFolder();
        var clock = new FixedTimeProvider(Now);
        var store = new SnapshotStore(folder.DocumentPath, clock);
        await store.UpsertLastGoodAsync(CreateSnapshot(25m));
        var provider = new RecordingProvider(new ProviderOutcome.Success(CreateSnapshot(75m)));
        var refresh = new CacheFirstRefresh(store, [provider], clock);

        IReadOnlyList<CacheFirstEvent> events;
        await using (FileStream heldDocument = new(
            folder.DocumentPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read))
        {
            events = await CollectAsync(refresh.RunAsync());
        }

        CacheFirstEvent.ProviderCompleted completed = Assert.IsType<CacheFirstEvent.ProviderCompleted>(events[1]);
        Assert.IsType<ProviderOutcome.Success>(completed.Outcome);
        Assert.Equal(CacheUpdateStatus.AccessDenied, completed.CacheStatus);
        Assert.False(completed.CacheUpdated);
        SnapshotCacheReadResult.Loaded saved = Assert.IsType<SnapshotCacheReadResult.Loaded>(
            await store.LoadAsync());
        Assert.Equal(25m, ProgressUsed(Assert.Single(saved.Snapshots)));
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

    private static decimal ProgressUsed(ProviderSnapshot snapshot) =>
        Assert.IsType<ProgressMetricSnapshot>(snapshot.Metrics[0]).Used;

    private static ProviderSnapshot CreateSnapshot(
        decimal used,
        CoverageKind coverage = CoverageKind.Complete,
        string providerId = "fake") =>
        new(
            new ProviderId(providerId),
            "Fake provider",
            "Sample",
            Now,
            Now.AddSeconds(-30),
            "UTC",
            [
                new ProgressMetricSnapshot(
                    new MetricId("session"),
                    used,
                    100m,
                    Now.AddHours(4),
                    new DataProvenance(
                        SourceKind.Synthetic,
                        MeasurementKind.ProviderReported,
                        "fake/1")),
            ],
            coverage,
            1);

    private sealed class RecordingProvider(ProviderOutcome outcome) : IProviderRuntime
    {
        public ProviderDescriptor Descriptor { get; } = new(new ProviderId("fake"), "Fake provider");

        public int RefreshCalls { get; private set; }

        public RefreshContext? LastContext { get; private set; }

        public ValueTask<ProviderDetection> DetectAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<ProviderDetection>(new ProviderDetection.Available());

        public Task<ProviderOutcome> RefreshAsync(
            RefreshContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RefreshCalls++;
            LastContext = context;
            return Task.FromResult(outcome);
        }
    }

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
                "WOpenUsage.Core.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
            DocumentPath = System.IO.Path.Combine(Path, SnapshotStore.DefaultFileName);
        }

        public string Path { get; }

        public string DocumentPath { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}

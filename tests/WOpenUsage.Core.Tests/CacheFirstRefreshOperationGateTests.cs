using WOpenUsage.Core.Cache;
using WOpenUsage.Core.Providers;

namespace WOpenUsage.Core.Tests;

public sealed class CacheFirstRefreshOperationGateTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 23, 7, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CachePublishesBeforeBlockedProviderAcquiresGate()
    {
        using var folder = new TemporaryFolder();
        var gate = new ProviderOperationGate();
        await using IAsyncDisposable blocker = await gate.EnterAsync();
        var provider = new ControlledProvider(CreateSnapshot(75m));
        var store = new SnapshotStore(folder.DocumentPath, new FixedTimeProvider(Now));
        var refresh = new CacheFirstRefresh(
            store,
            [provider],
            new FixedTimeProvider(Now),
            gate);
        await using IAsyncEnumerator<CacheFirstEvent> events = refresh
            .RunAsync()
            .GetAsyncEnumerator();

        Assert.True(await events.MoveNextAsync());
        Assert.IsType<CacheFirstEvent.CachePublished>(events.Current);

        Task<bool> providerMove = events.MoveNextAsync().AsTask();
        await Task.Yield();
        Assert.False(providerMove.IsCompleted);
        Assert.Equal(0, provider.RefreshCalls);

        await blocker.DisposeAsync();
        Assert.True(await providerMove);
        Assert.IsType<CacheFirstEvent.ProviderCompleted>(events.Current);
        SnapshotCacheReadResult.Loaded cached = Assert.IsType<SnapshotCacheReadResult.Loaded>(
            await store.LoadAsync());
        Assert.Equal(75m, ProgressUsed(Assert.Single(cached.Snapshots)));
    }

    [Fact]
    public async Task CompetingOperationEntersOnlyAfterSnapshotIsSaved()
    {
        using var folder = new TemporaryFolder();
        var gate = new ProviderOperationGate();
        var provider = new ControlledProvider(CreateSnapshot(82m), holdRefresh: true);
        var store = new SnapshotStore(folder.DocumentPath, new FixedTimeProvider(Now));
        var refresh = new CacheFirstRefresh(
            store,
            [provider],
            new FixedTimeProvider(Now),
            gate);
        await using IAsyncEnumerator<CacheFirstEvent> events = refresh
            .RunAsync()
            .GetAsyncEnumerator();
        Assert.True(await events.MoveNextAsync());

        Task<bool> providerMove = events.MoveNextAsync().AsTask();
        await provider.Started.Task;
        Task<SnapshotCacheReadResult> competingRead = ReadAfterEnteringGateAsync(gate, store);
        await Task.Yield();
        Assert.False(competingRead.IsCompleted);

        provider.Release();
        Assert.True(await providerMove);
        SnapshotCacheReadResult.Loaded cached = Assert.IsType<SnapshotCacheReadResult.Loaded>(
            await competingRead);
        Assert.Equal(82m, ProgressUsed(Assert.Single(cached.Snapshots)));
    }

    [Fact]
    public async Task CancellationWhileWaitingDoesNotCallProviderOrCorruptGate()
    {
        using var folder = new TemporaryFolder();
        var gate = new ProviderOperationGate();
        await using IAsyncDisposable blocker = await gate.EnterAsync();
        var provider = new ControlledProvider(CreateSnapshot(40m));
        var store = new SnapshotStore(folder.DocumentPath, new FixedTimeProvider(Now));
        var refresh = new CacheFirstRefresh(
            store,
            [provider],
            new FixedTimeProvider(Now),
            gate);
        using var cancellation = new CancellationTokenSource();
        await using IAsyncEnumerator<CacheFirstEvent> events = refresh
            .RunAsync(cancellationToken: cancellation.Token)
            .GetAsyncEnumerator(cancellation.Token);
        Assert.True(await events.MoveNextAsync());

        Task<bool> providerMove = events.MoveNextAsync().AsTask();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => providerMove);
        Assert.Equal(0, provider.RefreshCalls);

        await blocker.DisposeAsync();
        await using IAsyncDisposable proofLease = await gate.EnterAsync();
    }

    [Fact]
    public async Task ProviderExceptionReleasesGate()
    {
        using var folder = new TemporaryFolder();
        var gate = new ProviderOperationGate();
        var provider = new ControlledProvider(
            CreateSnapshot(10m),
            exception: new InvalidOperationException("Synthetic provider failure."));
        var refresh = new CacheFirstRefresh(
            new SnapshotStore(folder.DocumentPath, new FixedTimeProvider(Now)),
            [provider],
            new FixedTimeProvider(Now),
            gate);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (CacheFirstEvent _ in refresh.RunAsync())
            {
            }
        });

        await using IAsyncDisposable proofLease = await gate.EnterAsync();
    }

    [Fact]
    public async Task LeaseReleasesOnlyOnce()
    {
        var gate = new ProviderOperationGate();
        IAsyncDisposable first = await gate.EnterAsync();
        await first.DisposeAsync();
        await first.DisposeAsync();

        await using IAsyncDisposable second = await gate.EnterAsync();
        Task<IAsyncDisposable> thirdAcquire = gate.EnterAsync().AsTask();
        await Task.Yield();
        Assert.False(thirdAcquire.IsCompleted);

        await second.DisposeAsync();
        await using IAsyncDisposable third = await thirdAcquire;
    }

    private static async Task<SnapshotCacheReadResult> ReadAfterEnteringGateAsync(
        ProviderOperationGate gate,
        SnapshotStore store)
    {
        await using IAsyncDisposable lease = await gate.EnterAsync();
        return await store.LoadAsync();
    }

    private static decimal ProgressUsed(ProviderSnapshot snapshot) =>
        Assert.IsType<ProgressMetricSnapshot>(snapshot.Metrics[0]).Used;

    private static ProviderSnapshot CreateSnapshot(decimal used) =>
        new(
            new ProviderId("fake"),
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
            CoverageKind.Complete,
            1);

    private sealed class ControlledProvider : IProviderRuntime
    {
        private readonly ProviderSnapshot _snapshot;
        private readonly bool _holdRefresh;
        private readonly Exception? _exception;
        private readonly TaskCompletionSource _release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public ControlledProvider(
            ProviderSnapshot snapshot,
            bool holdRefresh = false,
            Exception? exception = null)
        {
            _snapshot = snapshot;
            _holdRefresh = holdRefresh;
            _exception = exception;
            if (!holdRefresh)
            {
                _release.TrySetResult();
            }
        }

        public ProviderDescriptor Descriptor { get; } =
            new(new ProviderId("fake"), "Fake provider");

        public int RefreshCalls { get; private set; }

        public TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => _release.TrySetResult();

        public ValueTask<ProviderDetection> DetectAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<ProviderDetection>(new ProviderDetection.Available());

        public async Task<ProviderOutcome> RefreshAsync(
            RefreshContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RefreshCalls++;
            Started.TrySetResult();
            if (_holdRefresh)
            {
                await _release.Task.WaitAsync(cancellationToken);
            }

            if (_exception is not null)
            {
                throw _exception;
            }

            return new ProviderOutcome.Success(_snapshot);
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

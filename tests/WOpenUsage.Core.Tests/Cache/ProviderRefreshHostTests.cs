using WOpenUsage.Core.Cache;
using WOpenUsage.Core.Providers;

namespace WOpenUsage.Core.Tests.Cache;

public sealed class ProviderRefreshHostTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunPublishesMergedCacheThenCompletesEachProviderInOrder()
    {
        using var folder = new TemporaryFolder();
        var clock = new FixedTimeProvider(Now);
        var codexStore = new SnapshotStore(Path.Combine(folder.Root, "codex", SnapshotStore.DefaultFileName), clock);
        var vercelStore = new SnapshotStore(Path.Combine(folder.Root, "vercel", SnapshotStore.DefaultFileName), clock);

        ProviderSnapshot codexCached = CreateSnapshot("codex", 10m);
        ProviderSnapshot vercelCached = CreateSnapshot("vercel-ai-gateway", 20m);
        await codexStore.UpsertLastGoodAsync(codexCached);
        await vercelStore.UpsertLastGoodAsync(vercelCached);

        var codexProvider = new RecordingProvider(
            new ProviderId("codex"),
            "Codex",
            new ProviderOutcome.Success(CreateSnapshot("codex", 80m)));
        var vercelProvider = new RecordingProvider(
            new ProviderId("vercel-ai-gateway"),
            "Vercel AI Gateway",
            new ProviderOutcome.Success(CreateSnapshot("vercel-ai-gateway", 90m)));

        var host = new ProviderRefreshHost(
            [
                new ProviderRefreshRegistration(codexProvider, codexStore),
                new ProviderRefreshRegistration(vercelProvider, vercelStore),
            ],
            clock);

        await using IAsyncEnumerator<CacheFirstEvent> events =
            host.RunAsync(forceRefresh: true).GetAsyncEnumerator();

        Assert.True(await events.MoveNextAsync());
        CacheFirstEvent.CachePublished published =
            Assert.IsType<CacheFirstEvent.CachePublished>(events.Current);
        Assert.Equal(2, published.Snapshots.Count);
        Assert.Contains(published.Snapshots, s => s.ProviderId.Value == "codex");
        Assert.Contains(published.Snapshots, s => s.ProviderId.Value == "vercel-ai-gateway");
        Assert.Equal(0, codexProvider.RefreshCalls);
        Assert.Equal(0, vercelProvider.RefreshCalls);

        Assert.True(await events.MoveNextAsync());
        CacheFirstEvent.ProviderCompleted first =
            Assert.IsType<CacheFirstEvent.ProviderCompleted>(events.Current);
        Assert.Equal("codex", first.ProviderId.Value);
        Assert.Equal(1, codexProvider.RefreshCalls);
        Assert.True(codexProvider.LastContext?.ForceRefresh);

        Assert.True(await events.MoveNextAsync());
        CacheFirstEvent.ProviderCompleted second =
            Assert.IsType<CacheFirstEvent.ProviderCompleted>(events.Current);
        Assert.Equal("vercel-ai-gateway", second.ProviderId.Value);
        Assert.Equal(1, vercelProvider.RefreshCalls);
        Assert.True(vercelProvider.LastContext?.ForceRefresh);

        Assert.False(await events.MoveNextAsync());
    }

    [Fact]
    public async Task ForceRefreshFalsePropagatesToProviders()
    {
        using var folder = new TemporaryFolder();
        var clock = new FixedTimeProvider(Now);
        var store = new SnapshotStore(folder.DocumentPath, clock);
        var provider = new RecordingProvider(
            new ProviderId("codex"),
            "Codex",
            new ProviderOutcome.Success(CreateSnapshot("codex", 5m)));
        var host = new ProviderRefreshHost(
            [new ProviderRefreshRegistration(provider, store)],
            clock);

        _ = await CollectAsync(host.RunAsync(forceRefresh: false));
        Assert.False(provider.LastContext?.ForceRefresh);
    }

    private static async Task<IReadOnlyList<CacheFirstEvent>> CollectAsync(
        IAsyncEnumerable<CacheFirstEvent> events)
    {
        var list = new List<CacheFirstEvent>();
        await foreach (CacheFirstEvent item in events)
        {
            list.Add(item);
        }

        return list;
    }

    private static ProviderSnapshot CreateSnapshot(string providerId, decimal used) =>
        new(
            new ProviderId(providerId),
            "Provider " + providerId,
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

    private sealed class TemporaryFolder : IDisposable
    {
        public TemporaryFolder()
        {
            Root = Path.Combine(Path.GetTempPath(), "wou-refresh-host-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            DocumentPath = Path.Combine(Root, SnapshotStore.DefaultFileName);
        }

        public string Root { get; }

        public string DocumentPath { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
                }
            }
            catch
            {
            }
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow.ToUniversalTime();

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }

    private sealed class RecordingProvider : IProviderRuntime
    {
        private readonly ProviderOutcome _outcome;

        public RecordingProvider(ProviderId id, string displayName, ProviderOutcome outcome)
        {
            Descriptor = new ProviderDescriptor(id, displayName);
            _outcome = outcome;
        }

        public ProviderDescriptor Descriptor { get; }

        public int RefreshCalls { get; private set; }

        public RefreshContext? LastContext { get; private set; }

        public ValueTask<ProviderDetection> DetectAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<ProviderDetection>(new ProviderDetection.Available());

        public Task<ProviderOutcome> RefreshAsync(
            RefreshContext context,
            CancellationToken cancellationToken)
        {
            RefreshCalls++;
            LastContext = context;
            return Task.FromResult(_outcome);
        }
    }
}

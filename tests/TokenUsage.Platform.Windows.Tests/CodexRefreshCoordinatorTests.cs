using TokenUsage.Core.Cache;
using TokenUsage.Core.Providers;
using TokenUsage.Providers.Codex;
using TokenUsage.Runtime.Windows.Codex;

namespace TokenUsage.Platform.Windows.Tests;

public sealed class CodexRefreshCoordinatorTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 22, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RefreshPublishesEmptyCacheThenSavesRealCodexLastGood()
    {
        using var folder = new TemporaryFolder();
        var client = new StubClient();
        var coordinator = new CodexRefreshCoordinator(
            folder.Path,
            new FixedTimeProvider(Now),
            new StubFactory(client));

        IReadOnlyList<CacheFirstEvent> first = await CollectAsync(
            CoordinatorRefresh.Run(coordinator, forceRefresh: false, CancellationToken.None));
        IReadOnlyList<CacheFirstEvent> second = await CollectAsync(
            CoordinatorRefresh.Run(coordinator, forceRefresh: false, CancellationToken.None));

        Assert.IsType<SnapshotCacheReadResult.Empty>(
            Assert.IsType<CacheFirstEvent.CachePublished>(first[0]).ReadResult);
        CacheFirstEvent.ProviderCompleted completed =
            Assert.IsType<CacheFirstEvent.ProviderCompleted>(first[1]);
        Assert.IsType<ProviderOutcome.Success>(completed.Outcome);
        Assert.Equal(CacheUpdateStatus.Updated, completed.CacheStatus);

        SnapshotCacheReadResult.Loaded cached = Assert.IsType<SnapshotCacheReadResult.Loaded>(
            Assert.IsType<CacheFirstEvent.CachePublished>(second[0]).ReadResult);
        ProviderSnapshot cachedSnapshot = Assert.Single(cached.Snapshots);
        Assert.Equal("codex", cachedSnapshot.ProviderId.Value);
        ScalarMetricSnapshot usage = Assert.Single(
            cachedSnapshot.Metrics.OfType<ScalarMetricSnapshot>(),
            metric => metric.Id.Value == "usage.tokens.today");
        Assert.Equal(12m, usage.Value);
        Assert.Equal(2, client.DisposeCount);
    }

    [Fact]
    public async Task UsageFailureReplacesStaleUsageWithFreshQuotaOnly()
    {
        using var folder = new TemporaryFolder();
        var client = new StubClient();
        var coordinator = new CodexRefreshCoordinator(
            folder.Path,
            new FixedTimeProvider(Now),
            new StubFactory(client));
        await CollectAsync(CoordinatorRefresh.Run(coordinator, forceRefresh: false, CancellationToken.None));
        client.UsageException = new CodexRequestTimeoutException();

        IReadOnlyList<CacheFirstEvent> refresh = await CollectAsync(
            CoordinatorRefresh.Run(coordinator, forceRefresh: false, CancellationToken.None));
        IReadOnlyList<CacheFirstEvent> after = await CollectAsync(
            CoordinatorRefresh.Run(coordinator, forceRefresh: false, CancellationToken.None));

        CacheFirstEvent.ProviderCompleted completed =
            Assert.IsType<CacheFirstEvent.ProviderCompleted>(refresh[1]);
        Assert.IsType<ProviderOutcome.PartialSuccess>(completed.Outcome);
        Assert.Equal(CacheUpdateStatus.Updated, completed.CacheStatus);
        ProviderSnapshot cached = Assert.Single(
            Assert.IsType<SnapshotCacheReadResult.Loaded>(
                Assert.IsType<CacheFirstEvent.CachePublished>(after[0]).ReadResult).Snapshots);
        Assert.Equal(CoverageKind.Partial, cached.Coverage);
        Assert.DoesNotContain(
            cached.Metrics,
            metric => metric.Id.Value.StartsWith("usage.", StringComparison.Ordinal));
        Assert.Contains(cached.Metrics, metric => metric.Id.Value == "quota.primary");
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

    private sealed class StubFactory(StubClient client) : ICodexQuotaClientFactory
    {
        public ValueTask<CodexClientAvailability> DetectAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(CodexClientAvailability.Available);
        }

        public Task<ICodexQuotaClient> CreateAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<ICodexQuotaClient>(client);
        }
    }

    private sealed class StubClient : ICodexQuotaClient
    {
        public int DisposeCount { get; private set; }

        public Exception? UsageException { get; set; }

        public Task HandshakeAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task<CodexAccountStatus> ReadAccountStatusAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new CodexAccountStatus(
                CodexAccountKind.ChatGpt,
                requiresOpenAiAuth: true,
                planType: "plus"));
        }

        public Task<CodexRateLimitsSnapshot> ReadRateLimitsAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new CodexRateLimitsSnapshot(
                new CodexRateLimitBucket(
                    "plus",
                    new CodexRateLimitWindow(42, Now.AddHours(4), 300),
                    null),
                new Dictionary<string, CodexRateLimitBucket>()));
        }

        public Task<CodexTokenUsageSnapshot> ReadTokenUsageAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return UsageException is null
                ? Task.FromResult(new CodexTokenUsageSnapshot(
                    new CodexUsageSummary(null, null, null, null, null),
                    [new CodexUsageDailyBucket(new DateOnly(2026, 7, 22), 12)]))
                : Task.FromException<CodexTokenUsageSnapshot>(UsageException);
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
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
                "tokenusage-codex-coordinator-tests",
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

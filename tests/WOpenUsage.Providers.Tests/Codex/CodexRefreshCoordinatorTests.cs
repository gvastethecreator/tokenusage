using WOpenUsage.App.Services;
using WOpenUsage.Core.Cache;
using WOpenUsage.Core.Providers;
using WOpenUsage.Providers.Codex;

namespace WOpenUsage.Providers.Tests.Codex;

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
            coordinator.RunAsync(forceRefresh: false, CancellationToken.None));
        IReadOnlyList<CacheFirstEvent> second = await CollectAsync(
            coordinator.RunAsync(forceRefresh: false, CancellationToken.None));

        Assert.IsType<SnapshotCacheReadResult.Empty>(
            Assert.IsType<CacheFirstEvent.CachePublished>(first[0]).ReadResult);
        CacheFirstEvent.ProviderCompleted completed =
            Assert.IsType<CacheFirstEvent.ProviderCompleted>(first[1]);
        Assert.IsType<ProviderOutcome.Success>(completed.Outcome);
        Assert.Equal(CacheUpdateStatus.Updated, completed.CacheStatus);

        SnapshotCacheReadResult.Loaded cached = Assert.IsType<SnapshotCacheReadResult.Loaded>(
            Assert.IsType<CacheFirstEvent.CachePublished>(second[0]).ReadResult);
        Assert.Equal("codex", Assert.Single(cached.Snapshots).ProviderId.Value);
        Assert.Equal(2, client.DisposeCount);
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
                "wopenusage-codex-coordinator-tests",
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

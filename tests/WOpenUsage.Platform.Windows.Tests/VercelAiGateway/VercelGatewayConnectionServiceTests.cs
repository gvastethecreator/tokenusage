using WOpenUsage.Core.Cache;
using WOpenUsage.Core.Providers;
using WOpenUsage.Providers.VercelAiGateway;
using WOpenUsage.Runtime.Windows.VercelAiGateway;

namespace WOpenUsage.Platform.Windows.Tests.VercelAiGateway;

public sealed class VercelGatewayConnectionServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 23, 7, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task DisconnectWaitsForRefreshSaveThenRemovesCredentialAndCache()
    {
        using var folder = new TemporaryFolder();
        var clock = new FixedTimeProvider(Now);
        var snapshotStore = new SnapshotStore(folder.DocumentPath, clock);
        var credentials = new FakeCredentialStore("test-api-key");
        var reportClient = new ControlledReportClient(holdResponse: true);
        var coordinator = new VercelGatewayRefreshCoordinator(
            snapshotStore,
            credentials,
            reportClient,
            clock);
        await using IAsyncEnumerator<CacheFirstEvent> events = coordinator
            .RunAsync(forceRefresh: true, CancellationToken.None)
            .GetAsyncEnumerator();

        Assert.True(await events.MoveNextAsync());
        Task<bool> refreshCompletion = events.MoveNextAsync().AsTask();
        await reportClient.Started.Task;

        Task<VercelGatewayDisconnectResult> disconnect =
            coordinator.Connections.DisconnectAsync();
        await Task.Yield();
        Assert.False(disconnect.IsCompleted);
        Assert.Equal(0, credentials.DeleteCalls);

        reportClient.Release();
        Assert.True(await refreshCompletion);
        CacheFirstEvent.ProviderCompleted completed =
            Assert.IsType<CacheFirstEvent.ProviderCompleted>(events.Current);
        Assert.Equal(CacheUpdateStatus.Updated, completed.CacheStatus);

        VercelGatewayDisconnectResult result = await disconnect;
        Assert.True(result.CredentialRemoved);
        Assert.True(result.IsComplete);
        Assert.Equal(VercelGatewayCacheCleanupStatus.Removed, result.CacheStatus);
        Assert.Null(credentials.ApiKey);

        SnapshotCacheReadResult.Loaded afterDisconnect =
            Assert.IsType<SnapshotCacheReadResult.Loaded>(await snapshotStore.LoadAsync());
        Assert.Empty(afterDisconnect.Snapshots);

        IReadOnlyList<CacheFirstEvent> after = await CollectAsync(
            coordinator.RunAsync(forceRefresh: true, CancellationToken.None));
        CacheFirstEvent.ProviderCompleted disconnectedRefresh =
            Assert.IsType<CacheFirstEvent.ProviderCompleted>(after[1]);
        Assert.IsType<ProviderOutcome.NotConfigured>(disconnectedRefresh.Outcome);
        Assert.Equal(1, reportClient.Calls);
    }

    [Fact]
    public async Task CancellationWhileRefreshOwnsGateLeavesConnectionUntouched()
    {
        using var folder = new TemporaryFolder();
        var clock = new FixedTimeProvider(Now);
        var credentials = new FakeCredentialStore("test-api-key");
        var reportClient = new ControlledReportClient(holdResponse: true);
        var coordinator = new VercelGatewayRefreshCoordinator(
            new SnapshotStore(folder.DocumentPath, clock),
            credentials,
            reportClient,
            clock);
        await using IAsyncEnumerator<CacheFirstEvent> events = coordinator
            .RunAsync(forceRefresh: true, CancellationToken.None)
            .GetAsyncEnumerator();
        Assert.True(await events.MoveNextAsync());
        Task<bool> refreshCompletion = events.MoveNextAsync().AsTask();
        await reportClient.Started.Task;
        using var cancellation = new CancellationTokenSource();

        Task<VercelGatewayDisconnectResult> disconnect =
            coordinator.Connections.DisconnectAsync(cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => disconnect);
        Assert.Equal("test-api-key", credentials.ApiKey);
        Assert.Equal(0, credentials.DeleteCalls);

        reportClient.Release();
        Assert.True(await refreshCompletion);
    }

    [Fact]
    public async Task FutureCacheVersionReturnsTypedPartialAfterCredentialRemoval()
    {
        using var folder = new TemporaryFolder();
        const string futureDocument =
            "{\"schemaVersion\":999,\"savedAtUtc\":\"2026-07-23T07:00:00Z\",\"providers\":[]}";
        await File.WriteAllTextAsync(folder.DocumentPath, futureDocument);
        var credentials = new FakeCredentialStore("test-api-key");
        var gate = new ProviderOperationGate();
        var service = new VercelGatewayConnectionService(
            credentials,
            new SnapshotStore(folder.DocumentPath, new FixedTimeProvider(Now)),
            gate);

        VercelGatewayDisconnectResult result = await service.DisconnectAsync();

        Assert.True(result.CredentialRemoved);
        Assert.False(result.IsComplete);
        Assert.Equal(
            VercelGatewayCacheCleanupStatus.RefusedUnsupportedVersion,
            result.CacheStatus);
        Assert.Equal(999, result.UnsupportedSchemaVersion);
        Assert.Equal(futureDocument, await File.ReadAllTextAsync(folder.DocumentPath));
    }

    [Fact]
    public async Task CorruptCacheReturnsQuarantineFileNameOnlyAndStaysPartial()
    {
        using var folder = new TemporaryFolder();
        await File.WriteAllTextAsync(folder.DocumentPath, "{not-json");
        var credentials = new FakeCredentialStore("test-api-key");
        var gate = new ProviderOperationGate();
        var service = new VercelGatewayConnectionService(
            credentials,
            new SnapshotStore(folder.DocumentPath, new FixedTimeProvider(Now)),
            gate);

        VercelGatewayDisconnectResult result = await service.DisconnectAsync();

        Assert.True(result.CredentialRemoved);
        Assert.False(result.IsComplete);
        Assert.Equal(VercelGatewayCacheCleanupStatus.Quarantined, result.CacheStatus);
        Assert.NotNull(result.QuarantineFileName);
        Assert.Equal(Path.GetFileName(result.QuarantineFileName), result.QuarantineFileName);
        Assert.False(File.Exists(folder.DocumentPath));
        Assert.True(File.Exists(Path.Combine(folder.Path, result.QuarantineFileName)));
    }

    [Fact]
    public async Task MissingCredentialAndCacheIsAlreadyComplete()
    {
        using var folder = new TemporaryFolder();
        var credentials = new FakeCredentialStore(apiKey: null);
        var gate = new ProviderOperationGate();
        var service = new VercelGatewayConnectionService(
            credentials,
            new SnapshotStore(folder.DocumentPath, new FixedTimeProvider(Now)),
            gate);

        VercelGatewayDisconnectResult result = await service.DisconnectAsync();

        Assert.False(result.CredentialRemoved);
        Assert.True(result.IsComplete);
        Assert.Equal(VercelGatewayCacheCleanupStatus.Missing, result.CacheStatus);
    }

    [Fact]
    public async Task CancellationAfterGateAcquisitionDoesNotInterruptCleanup()
    {
        using var folder = new TemporaryFolder();
        var clock = new FixedTimeProvider(Now);
        var snapshotStore = new SnapshotStore(folder.DocumentPath, clock);
        await snapshotStore.UpsertLastGoodAsync(CreateVercelSnapshot());
        using var cancellation = new CancellationTokenSource();
        var credentials = new FakeCredentialStore("test-api-key")
        {
            OnDelete = cancellation.Cancel,
        };
        var service = new VercelGatewayConnectionService(
            credentials,
            snapshotStore,
            new ProviderOperationGate());

        VercelGatewayDisconnectResult result =
            await service.DisconnectAsync(cancellation.Token);

        Assert.True(cancellation.IsCancellationRequested);
        Assert.True(result.CredentialRemoved);
        Assert.True(result.IsComplete);
        SnapshotCacheReadResult.Loaded cached =
            Assert.IsType<SnapshotCacheReadResult.Loaded>(await snapshotStore.LoadAsync());
        Assert.Empty(cached.Snapshots);
    }

    [Fact]
    public async Task ConnectRemovesOldSnapshotBeforeSavingExactNewKey()
    {
        using var folder = new TemporaryFolder();
        var clock = new FixedTimeProvider(Now);
        var snapshotStore = new SnapshotStore(folder.DocumentPath, clock);
        await snapshotStore.UpsertLastGoodAsync(CreateVercelSnapshot());
        var credentials = new FakeCredentialStore("old-test-api-key");
        credentials.OnSave = () =>
        {
            SnapshotCacheReadResult.Loaded cached = Assert.IsType<SnapshotCacheReadResult.Loaded>(
                snapshotStore.LoadAsync().GetAwaiter().GetResult());
            Assert.Empty(cached.Snapshots);
        };
        var service = new VercelGatewayConnectionService(
            credentials,
            snapshotStore,
            new ProviderOperationGate());

        VercelGatewayConnectResult result = await service.ConnectAsync(" new-test-api-key ");

        Assert.True(result.CredentialSaved);
        Assert.True(result.IsComplete);
        Assert.Equal(VercelGatewayCacheCleanupStatus.Removed, result.CacheStatus);
        Assert.Equal(" new-test-api-key ", credentials.ApiKey);
        Assert.Equal(1, credentials.SaveCalls);
    }

    [Fact]
    public async Task FutureCacheVersionRefusesConnectAndPreservesCredentialAndBytes()
    {
        using var folder = new TemporaryFolder();
        const string futureDocument =
            "{\"schemaVersion\":999,\"savedAtUtc\":\"2026-07-23T07:00:00Z\",\"providers\":[]}";
        await File.WriteAllTextAsync(folder.DocumentPath, futureDocument);
        var credentials = new FakeCredentialStore("old-test-api-key");
        var service = new VercelGatewayConnectionService(
            credentials,
            new SnapshotStore(folder.DocumentPath, new FixedTimeProvider(Now)),
            new ProviderOperationGate());

        VercelGatewayConnectResult result = await service.ConnectAsync("new-test-api-key");

        Assert.False(result.CredentialSaved);
        Assert.False(result.IsComplete);
        Assert.Equal(
            VercelGatewayCacheCleanupStatus.RefusedUnsupportedVersion,
            result.CacheStatus);
        Assert.Equal(999, result.UnsupportedSchemaVersion);
        Assert.Equal("old-test-api-key", credentials.ApiKey);
        Assert.Equal(0, credentials.SaveCalls);
        Assert.Equal(futureDocument, await File.ReadAllTextAsync(folder.DocumentPath));
    }

    [Fact]
    public async Task CancellationAfterConnectAcquisitionDoesNotInterruptCleanupOrSave()
    {
        using var folder = new TemporaryFolder();
        var clock = new FixedTimeProvider(Now);
        var snapshotStore = new SnapshotStore(folder.DocumentPath, clock);
        await snapshotStore.UpsertLastGoodAsync(CreateVercelSnapshot());
        using var cancellation = new CancellationTokenSource();
        var credentials = new FakeCredentialStore("old-test-api-key")
        {
            OnSave = cancellation.Cancel,
        };
        var service = new VercelGatewayConnectionService(
            credentials,
            snapshotStore,
            new ProviderOperationGate());

        VercelGatewayConnectResult result = await service.ConnectAsync(
            "new-test-api-key",
            cancellation.Token);

        Assert.True(cancellation.IsCancellationRequested);
        Assert.True(result.IsComplete);
        Assert.Equal("new-test-api-key", credentials.ApiKey);
        SnapshotCacheReadResult.Loaded cached =
            Assert.IsType<SnapshotCacheReadResult.Loaded>(await snapshotStore.LoadAsync());
        Assert.Empty(cached.Snapshots);
    }

    [Fact]
    public async Task WhitespaceConnectIsRejectedWithoutMutatingState()
    {
        using var folder = new TemporaryFolder();
        var clock = new FixedTimeProvider(Now);
        var snapshotStore = new SnapshotStore(folder.DocumentPath, clock);
        await snapshotStore.UpsertLastGoodAsync(CreateVercelSnapshot());
        var credentials = new FakeCredentialStore("old-test-api-key");
        var service = new VercelGatewayConnectionService(
            credentials,
            snapshotStore,
            new ProviderOperationGate());

        await Assert.ThrowsAsync<ArgumentException>(() => service.ConnectAsync("   "));

        Assert.Equal("old-test-api-key", credentials.ApiKey);
        Assert.Equal(0, credentials.SaveCalls);
        SnapshotCacheReadResult.Loaded cached =
            Assert.IsType<SnapshotCacheReadResult.Loaded>(await snapshotStore.LoadAsync());
        Assert.Single(cached.Snapshots);
    }

    [Fact]
    public async Task IsConfiguredWaitsForOperationGate()
    {
        using var folder = new TemporaryFolder();
        var gate = new ProviderOperationGate();
        await using IAsyncDisposable blocker = await gate.EnterAsync();
        var credentials = new FakeCredentialStore("test-api-key");
        var service = new VercelGatewayConnectionService(
            credentials,
            new SnapshotStore(folder.DocumentPath, new FixedTimeProvider(Now)),
            gate);

        Task<bool> configured = service.IsConfiguredAsync();
        await Task.Yield();
        Assert.False(configured.IsCompleted);

        await blocker.DisposeAsync();
        Assert.True(await configured);
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

    private static ProviderSnapshot CreateVercelSnapshot() =>
        new(
            new ProviderId("vercel-ai-gateway"),
            "Vercel AI Gateway",
            "Manual API key",
            Now,
            Now,
            "UTC",
            [
                new ScalarMetricSnapshot(
                    new MetricId("usage.cost.today"),
                    1.25m,
                    "USD",
                    new DataProvenance(
                        SourceKind.OfficialRemoteApi,
                        MeasurementKind.ProviderReported,
                        "vercel-ai-gateway/report-v1")),
            ],
            CoverageKind.Complete,
            1);

    private sealed class FakeCredentialStore(string? apiKey) : IVercelGatewayCredentialStore
    {
        public string? ApiKey { get; private set; } = apiKey;

        public int DeleteCalls { get; private set; }

        public int SaveCalls { get; private set; }

        public Action? OnDelete { get; init; }

        public Action? OnSave { get; set; }

        public Task<bool> IsConfiguredAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(ApiKey is not null);
        }

        public Task<VercelGatewayConnection?> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                ApiKey is null ? null : new VercelGatewayConnection(ApiKey));
        }

        public Task SaveAsync(
            string newApiKey,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveCalls++;
            OnSave?.Invoke();
            ApiKey = newApiKey;
            return Task.CompletedTask;
        }

        public Task<bool> DeleteAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DeleteCalls++;
            OnDelete?.Invoke();
            bool removed = ApiKey is not null;
            ApiKey = null;
            return Task.FromResult(removed);
        }
    }

    private sealed class ControlledReportClient(bool holdResponse) : IVercelGatewayReportClient
    {
        private readonly TaskCompletionSource _release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public int Calls { get; private set; }

        public void Release() => _release.TrySetResult();

        public async Task<VercelGatewayReport> GetDailyReportAsync(
            string apiKey,
            DateOnly startDate,
            DateOnly endDate,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            Started.TrySetResult();
            if (holdResponse)
            {
                await _release.Task.WaitAsync(cancellationToken);
            }

            return new VercelGatewayReport(
            [
                new VercelGatewayDailyReportRow(
                    endDate,
                    TotalCost: 1.25m,
                    MarketCost: 1m,
                    SurchargeCost: 0.25m,
                    GatewayCost: 0m,
                    InputTokens: 100,
                    OutputTokens: 25,
                    CachedInputTokens: 10,
                    CacheCreationInputTokens: 5,
                    ReasoningTokens: 3,
                    RequestCount: 2),
            ]);
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
                "wopenusage-vercel-disconnect-tests",
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

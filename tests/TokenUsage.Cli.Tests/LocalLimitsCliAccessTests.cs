using TokenUsage.Cli;
using TokenUsage.Core.Cache;
using TokenUsage.Core.Providers;

namespace TokenUsage.Cli.Tests;

public sealed class LocalLimitsCliAccessTests
{
    [Fact]
    public async Task ReadsTheCodexProviderCacheUsedByTheApp()
    {
        string dataRoot = CreateDataRoot();
        try
        {
            string cachePath = Path.Combine(
                dataRoot,
                "cache",
                "providers",
                "codex",
                SnapshotStore.DefaultFileName);
            var store = new SnapshotStore(cachePath);
            ProviderSnapshot snapshot = LimitsCommandTests.CreateCodexSnapshot();
            await store.UpsertLastGoodAsync(snapshot);

            IReadOnlyList<ProviderSnapshot> snapshots = await LocalLimitsCliAccess.ReadAsync(
                dataRoot,
                providerId: null,
                forceRefresh: false,
                TimeProvider.System,
                CancellationToken.None);

            ProviderSnapshot loaded = Assert.Single(snapshots);
            Assert.Equal("codex", loaded.ProviderId.Value);
            Assert.Equal(2, loaded.Metrics.Count);
        }
        finally
        {
            Directory.Delete(dataRoot, recursive: true);
        }
    }

    [Fact]
    public async Task IgnoresSnapshotsPlacedInTheWrongProviderPartition()
    {
        string dataRoot = CreateDataRoot();
        try
        {
            string cachePath = Path.Combine(
                dataRoot,
                "cache",
                "providers",
                "codex",
                SnapshotStore.DefaultFileName);
            var store = new SnapshotStore(cachePath);
            await store.SaveLastGoodAsync(
                [
                    LimitsCommandTests.CreateClaudeSnapshot(),
                    LimitsCommandTests.CreateCodexSnapshot(),
                ]);

            IReadOnlyList<ProviderSnapshot> snapshots = await LocalLimitsCliAccess.ReadAsync(
                dataRoot,
                providerId: "codex",
                forceRefresh: false,
                TimeProvider.System,
                CancellationToken.None);

            Assert.Equal("codex", Assert.Single(snapshots).ProviderId.Value);
        }
        finally
        {
            Directory.Delete(dataRoot, recursive: true);
        }
    }

    [Fact]
    public async Task MissingCacheReturnsNoSnapshots()
    {
        string dataRoot = CreateDataRoot();
        try
        {
            IReadOnlyList<ProviderSnapshot> snapshots = await LocalLimitsCliAccess.ReadAsync(
                dataRoot,
                providerId: null,
                forceRefresh: false,
                TimeProvider.System,
                CancellationToken.None);

            Assert.Empty(snapshots);
        }
        finally
        {
            Directory.Delete(dataRoot, recursive: true);
        }
    }

    [Fact]
    public async Task RequestedUnsupportedProviderDoesNotCreateCodexState()
    {
        string dataRoot = Path.Combine(
            Path.GetTempPath(),
            "tokenusage-limits-access-tests",
            Guid.NewGuid().ToString("N"));

        IReadOnlyList<ProviderSnapshot> snapshots = await LocalLimitsCliAccess.ReadAsync(
            dataRoot,
            providerId: "grok",
            forceRefresh: true,
            TimeProvider.System,
            CancellationToken.None);

        Assert.Empty(snapshots);
        Assert.False(Directory.Exists(dataRoot));
    }

    [Fact]
    public async Task ForceSelectionPrefersFreshSuccessOverPublishedCache()
    {
        ProviderSnapshot cached = CreateSnapshot("Cached");
        ProviderSnapshot fresh = CreateSnapshot("Fresh");

        IReadOnlyList<ProviderSnapshot> snapshots =
            await LocalLimitsCliAccess.SelectForceResultAsync(
                ToEvents(
                    new CacheFirstEvent.CachePublished(
                        new SnapshotCacheReadResult.Loaded([cached])),
                    new CacheFirstEvent.ProviderCompleted(
                        new ProviderId("codex"),
                        new ProviderOutcome.Success(fresh),
                        CacheUpdateStatus.Updated)),
                CancellationToken.None);

        Assert.Same(fresh, Assert.Single(snapshots));
    }

    [Fact]
    public async Task ForceSelectionKeepsPublishedCacheWhenRefreshHasNoSnapshot()
    {
        ProviderSnapshot cached = CreateSnapshot("Cached");

        IReadOnlyList<ProviderSnapshot> snapshots =
            await LocalLimitsCliAccess.SelectForceResultAsync(
                ToEvents(
                    new CacheFirstEvent.CachePublished(
                        new SnapshotCacheReadResult.Loaded([cached])),
                    new CacheFirstEvent.ProviderCompleted(
                        new ProviderId("codex"),
                        new ProviderOutcome.NotConfigured("Sign-in is unavailable."),
                        CacheUpdateStatus.NotAttempted)),
                CancellationToken.None);

        Assert.Same(cached, Assert.Single(snapshots));
    }

    [Fact]
    public async Task ForceSelectionUsesOutcomeLastGoodWithoutReadingErrorText()
    {
        ProviderSnapshot cached = CreateSnapshot("Cached");
        ProviderSnapshot lastGood = CreateSnapshot("Last good");
        var failure = new ProviderOutcome.TransientFailure(
            new ProviderError(
                ProviderErrorCode.TransientSourceFailure,
                "C:\\Users\\private\\auth.json bearer secret"),
            lastGood);

        IReadOnlyList<ProviderSnapshot> snapshots =
            await LocalLimitsCliAccess.SelectForceResultAsync(
                ToEvents(
                    new CacheFirstEvent.CachePublished(
                        new SnapshotCacheReadResult.Loaded([cached])),
                    new CacheFirstEvent.ProviderCompleted(
                        new ProviderId("codex"),
                        failure,
                        CacheUpdateStatus.NotAttempted)),
                CancellationToken.None);

        Assert.Same(lastGood, Assert.Single(snapshots));
    }

    [Fact]
    public async Task ForceSelectionReturnsNoDataWithoutCacheOrRefreshSnapshot()
    {
        IReadOnlyList<ProviderSnapshot> snapshots =
            await LocalLimitsCliAccess.SelectForceResultAsync(
                ToEvents(
                    new CacheFirstEvent.CachePublished(new SnapshotCacheReadResult.Empty()),
                    new CacheFirstEvent.ProviderCompleted(
                        new ProviderId("codex"),
                        new ProviderOutcome.UnsupportedAccount("No quota contract."),
                        CacheUpdateStatus.NotAttempted)),
                CancellationToken.None);

        Assert.Empty(snapshots);
    }

    [Fact]
    public async Task ForceSelectionPropagatesCallerCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => LocalLimitsCliAccess.SelectForceResultAsync(
                ToEvents(new CacheFirstEvent.CachePublished(new SnapshotCacheReadResult.Empty())),
                cancellation.Token));
    }

    private static ProviderSnapshot CreateSnapshot(string displayName) =>
        new(
            new ProviderId("codex"),
            displayName,
            "Plus",
            new DateTimeOffset(2026, 7, 23, 3, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 23, 2, 59, 0, TimeSpan.Zero),
            "UTC",
            [
                new ScalarMetricSnapshot(
                    new MetricId("tokens"),
                    1m,
                    "tokens",
                    new DataProvenance(
                        SourceKind.OfficialLocalApi,
                        MeasurementKind.ProviderReported,
                        "test/1")),
            ],
            CoverageKind.Complete,
            1);

    private static async IAsyncEnumerable<CacheFirstEvent> ToEvents(
        params CacheFirstEvent[] events)
    {
        foreach (CacheFirstEvent item in events)
        {
            await Task.Yield();
            yield return item;
        }
    }

    private static string CreateDataRoot()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "tokenusage-limits-access-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}

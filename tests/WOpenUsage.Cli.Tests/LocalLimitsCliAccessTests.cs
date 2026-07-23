using WOpenUsage.Cli;
using WOpenUsage.Core.Cache;
using WOpenUsage.Core.Providers;

namespace WOpenUsage.Cli.Tests;

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
                forceRefresh: false,
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
                forceRefresh: false,
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
                forceRefresh: false,
                CancellationToken.None);

            Assert.Empty(snapshots);
        }
        finally
        {
            Directory.Delete(dataRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ForceRefreshFailsClosedUntilRuntimeCompositionIsShared()
    {
        string dataRoot = CreateDataRoot();
        try
        {
            await Assert.ThrowsAsync<LimitsRefreshUnavailableException>(
                () => LocalLimitsCliAccess.ReadAsync(
                    dataRoot,
                    forceRefresh: true,
                    CancellationToken.None));
        }
        finally
        {
            Directory.Delete(dataRoot, recursive: true);
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

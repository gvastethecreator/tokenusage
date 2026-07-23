using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using WOpenUsage.Core.Cache;
using WOpenUsage.Core.Providers;
using WOpenUsage.Core.Usage;
using WOpenUsage.Providers.Codex;

namespace WOpenUsage.Cli.Tests;

public sealed class LocalProviderDiagnosticsAccessTests
{
    [Fact]
    public async Task MissingStateReturnsClosedStatusesWithoutCreatingDataRoot()
    {
        string dataRoot = NewDataRoot();
        var factory = new StubCodexFactory(CodexClientAvailability.MissingCli);

        ProviderDiagnosticsSnapshot result = await LocalProviderDiagnosticsAccess.ReadAsync(
            dataRoot,
            factory,
            _ => false,
            CancellationToken.None);

        Assert.False(Directory.Exists(dataRoot));
        Assert.Equal(0, factory.CreateCount);
        Assert.All(result.Providers, provider => Assert.Equal(ProviderDataStatus.Absent, provider.Data));
        Assert.Equal(
            ProviderDetectionStatus.Missing,
            result.Providers.Single(provider => provider.Id == "codex").Detection);
        Assert.All(
            result.Providers.Where(provider => provider.Id != "codex"),
            provider => Assert.Equal(ProviderDetectionStatus.Missing, provider.Detection));
        Assert.Equal(
            DoctorCheckStatus.Absent,
            result.Checks.Single(check => check.Id == "usage-db").Status);
    }

    [Fact]
    public async Task ReadsAppOwnedDataAndCacheWithoutChangingTheirBytes()
    {
        string dataRoot = NewDataRoot();
        string databasePath = Path.Combine(dataRoot, "scanner", "usage.v1.db");
        string cachePath = Path.Combine(
            dataRoot, "cache", "providers", "codex", SnapshotStore.DefaultFileName);
        try
        {
            UsageRepository writer = await UsageRepository.OpenAsync(databasePath);
            await writer.IngestAsync([CreateUsageEvent("grok-event", "grok")]);
            await new SnapshotStore(cachePath).UpsertLastGoodAsync(CreateCodexSnapshot());
            byte[] databaseBefore = await File.ReadAllBytesAsync(databasePath);
            byte[] cacheBefore = await File.ReadAllBytesAsync(cachePath);
            DateTime databaseWriteBefore = File.GetLastWriteTimeUtc(databasePath);
            DateTime cacheWriteBefore = File.GetLastWriteTimeUtc(cachePath);
            var factory = new StubCodexFactory(CodexClientAvailability.Available);

            ProviderDiagnosticsSnapshot result = await LocalProviderDiagnosticsAccess.ReadAsync(
                dataRoot,
                factory,
                providerId => providerId is "claude" or "grok",
                CancellationToken.None);

            Assert.Equal(0, factory.CreateCount);
            Assert.Equal(
                ProviderDataStatus.Present,
                result.Providers.Single(provider => provider.Id == "codex").Data);
            Assert.Equal(
                ProviderDataStatus.Present,
                result.Providers.Single(provider => provider.Id == "grok").Data);
            Assert.Equal(
                ProviderDataStatus.Absent,
                result.Providers.Single(provider => provider.Id == "claude").Data);
            Assert.Equal(
                ProviderDetectionStatus.Missing,
                result.Providers.Single(provider => provider.Id == "opencode").Detection);
            Assert.Equal(databaseBefore, await File.ReadAllBytesAsync(databasePath));
            Assert.Equal(cacheBefore, await File.ReadAllBytesAsync(cachePath));
            Assert.Equal(databaseWriteBefore, File.GetLastWriteTimeUtc(databasePath));
            Assert.Equal(cacheWriteBefore, File.GetLastWriteTimeUtc(cachePath));
        }
        finally
        {
            Directory.Delete(dataRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ValidCacheWithoutCodexSnapshotReportsCodexDataAbsent()
    {
        string dataRoot = NewDataRoot();
        string cachePath = Path.Combine(
            dataRoot, "cache", "providers", "codex", SnapshotStore.DefaultFileName);
        try
        {
            await new SnapshotStore(cachePath).UpsertLastGoodAsync(
                CreateCodexSnapshot("other"));

            ProviderDiagnosticsSnapshot result = await LocalProviderDiagnosticsAccess.ReadAsync(
                dataRoot,
                new StubCodexFactory(CodexClientAvailability.Available),
                _ => false,
                CancellationToken.None);

            Assert.Equal(
                ProviderDataStatus.Absent,
                result.Providers.Single(provider => provider.Id == "codex").Data);
            Assert.Equal(
                DoctorCheckStatus.Absent,
                result.Checks.Single(check => check.Id == "codex-cache").Status);
        }
        finally
        {
            Directory.Delete(dataRoot, recursive: true);
        }
    }

    [Fact]
    public async Task CorruptFilesRemainInPlaceAndOnlyProduceClosedStatuses()
    {
        string dataRoot = NewDataRoot();
        string databasePath = Path.Combine(dataRoot, "scanner", "usage.v1.db");
        string cachePath = Path.Combine(
            dataRoot, "cache", "providers", "codex", SnapshotStore.DefaultFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        byte[] databaseBytes = Encoding.UTF8.GetBytes("Bearer database secret@example.test");
        byte[] cacheBytes = Encoding.UTF8.GetBytes("{ \"schemaVersion\": 1, \"snapshots\": [");
        await File.WriteAllBytesAsync(databasePath, databaseBytes);
        await File.WriteAllBytesAsync(cachePath, cacheBytes);
        try
        {
            var factory = new StubCodexFactory(
                new IOException("C:\\Users\\private\\auth.json Bearer secret"));

            ProviderDiagnosticsSnapshot result = await LocalProviderDiagnosticsAccess.ReadAsync(
                dataRoot,
                factory,
                _ => throw new UnauthorizedAccessException("private path"),
                CancellationToken.None);

            Assert.All(result.Providers, provider =>
                Assert.Equal(ProviderDetectionStatus.Unavailable, provider.Detection));
            Assert.All(result.Providers, provider =>
                Assert.Equal(ProviderDataStatus.Unreadable, provider.Data));
            Assert.Equal(databaseBytes, await File.ReadAllBytesAsync(databasePath));
            Assert.Equal(cacheBytes, await File.ReadAllBytesAsync(cachePath));
            Assert.Equal(2, Directory.GetFiles(dataRoot, "*", SearchOption.AllDirectories).Length);
        }
        finally
        {
            Directory.Delete(dataRoot, recursive: true);
        }
    }

    [Fact]
    public async Task FutureUsageSchemaMapsToUnsupportedWithoutMigrating()
    {
        string dataRoot = NewDataRoot();
        string databasePath = Path.Combine(dataRoot, "scanner", "usage.v1.db");
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        try
        {
            await using (var connection = new SqliteConnection(
                $"Data Source={databasePath};Pooling=False"))
            {
                await connection.OpenAsync();
                await using SqliteCommand command = connection.CreateCommand();
                command.CommandText =
                    "CREATE TABLE schema_migration(version INTEGER NOT NULL PRIMARY KEY, applied_at_utc TEXT NOT NULL);"
                    + "INSERT INTO schema_migration VALUES (99, '2026-07-23T04:00:00Z');";
                await command.ExecuteNonQueryAsync();
            }

            ProviderDiagnosticsSnapshot result = await LocalProviderDiagnosticsAccess.ReadAsync(
                dataRoot,
                new StubCodexFactory(CodexClientAvailability.MissingCli),
                _ => false,
                CancellationToken.None);

            Assert.Equal(
                DoctorCheckStatus.UnsupportedSchema,
                result.Checks.Single(check => check.Id == "usage-db").Status);
            Assert.All(
                result.Providers.Where(provider => provider.Id != "codex"),
                provider => Assert.Equal(ProviderDataStatus.UnsupportedSchema, provider.Data));
        }
        finally
        {
            Directory.Delete(dataRoot, recursive: true);
        }
    }

    [Fact]
    public async Task CallerCancellationStopsBeforeDetection()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        bool detectorCalled = false;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            LocalProviderDiagnosticsAccess.ReadAsync(
                NewDataRoot(),
                new StubCodexFactory(CodexClientAvailability.Available),
                _ => detectorCalled = true,
                cancellation.Token));

        Assert.False(detectorCalled);
    }

    [Fact]
    public async Task CallerCancellationDuringLocalDetectionPropagates()
    {
        using var cancellation = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            LocalProviderDiagnosticsAccess.ReadAsync(
                NewDataRoot(),
                new StubCodexFactory(CodexClientAvailability.Available),
                _ =>
                {
                    cancellation.Cancel();
                    throw new OperationCanceledException(cancellation.Token);
                },
                cancellation.Token));
    }

    private static UsageEvent CreateUsageEvent(string identity, string agentId) =>
        new(
            new UsageEventKey(Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(identity))).ToLowerInvariant()),
            new AgentId(agentId),
            new ModelProviderId("xai"),
            new ModelId("test-model"),
            new DateTimeOffset(2026, 7, 23, 4, 0, 0, TimeSpan.Zero),
            "UTC",
            new TokenBreakdown(10, 2, 0, 1, 0),
            CostObservation.ProviderReported(0.01m),
            "test/1",
            CoverageKind.Complete);

    private static ProviderSnapshot CreateCodexSnapshot(string providerId = "codex") =>
        new(
            new ProviderId(providerId),
            "Codex",
            "Plus",
            new DateTimeOffset(2026, 7, 23, 4, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 23, 3, 59, 0, TimeSpan.Zero),
            "UTC",
            [
                new ScalarMetricSnapshot(
                    new MetricId("tokens"),
                    12m,
                    "tokens",
                    new DataProvenance(
                        SourceKind.OfficialLocalApi,
                        MeasurementKind.ProviderReported,
                        "test/1")),
            ],
            CoverageKind.Complete,
            1);

    private static string NewDataRoot() => Path.Combine(
        Path.GetTempPath(),
        "tokenusage-provider-diagnostics-tests",
        Guid.NewGuid().ToString("N"));

    private sealed class StubCodexFactory : ICodexQuotaClientFactory
    {
        private readonly CodexClientAvailability _availability;
        private readonly Exception? _detectFailure;

        public StubCodexFactory(CodexClientAvailability availability) =>
            _availability = availability;

        public StubCodexFactory(Exception detectFailure) =>
            _detectFailure = detectFailure;

        public int CreateCount { get; private set; }

        public ValueTask<CodexClientAvailability> DetectAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _detectFailure is null
                ? ValueTask.FromResult(_availability)
                : ValueTask.FromException<CodexClientAvailability>(_detectFailure);
        }

        public Task<ICodexQuotaClient> CreateAsync(CancellationToken cancellationToken)
        {
            CreateCount++;
            throw new InvalidOperationException("CreateAsync must not be called by diagnostics.");
        }
    }
}

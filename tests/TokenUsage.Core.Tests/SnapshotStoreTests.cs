using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TokenUsage.Core.Cache;
using TokenUsage.Core.Providers;

namespace TokenUsage.Core.Tests;

public sealed class SnapshotStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task LoadMissingFileReturnsEmpty()
    {
        using var folder = new TemporaryFolder();
        var store = CreateStore(folder.DocumentPath);

        SnapshotCacheReadResult result = await store.LoadAsync();

        Assert.IsType<SnapshotCacheReadResult.Empty>(result);
    }

    [Fact]
    public async Task SaveThenLoadRoundTripsMetricsInTheirOriginalOrder()
    {
        using var folder = new TemporaryFolder();
        var store = CreateStore(folder.DocumentPath);
        ProviderSnapshot expected = CreateSnapshot("fake", 42m);

        SnapshotCacheSaveResult saveResult = await store.UpsertLastGoodAsync(expected);
        SnapshotCacheReadResult.Loaded loaded = Assert.IsType<SnapshotCacheReadResult.Loaded>(
            await store.LoadAsync());

        Assert.IsType<SnapshotCacheSaveResult.Saved>(saveResult);
        ProviderSnapshot actual = Assert.Single(loaded.Snapshots);
        AssertSnapshot(expected, actual);
        Assert.Collection(
            actual.Metrics,
            metric => Assert.Equal("session", Assert.IsType<ProgressMetricSnapshot>(metric).Id.Value),
            metric => Assert.Equal("spend-usd", Assert.IsType<ScalarMetricSnapshot>(metric).Id.Value));

        using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(folder.DocumentPath));
        JsonElement root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("fake", root.GetProperty("snapshots")[0].GetProperty("providerId").GetString());
        JsonElement progress = root.GetProperty("snapshots")[0].GetProperty("metrics")[0];
        Assert.Equal("progress", progress.GetProperty("kind").GetString());
        Assert.Equal("usd", progress.GetProperty("unit").GetString());
        Assert.Equal("Monthly", progress.GetProperty("resetCadence").GetString());
        Assert.True(progress.GetProperty("isActive").GetBoolean());
        JsonElement capability = root.GetProperty("snapshots")[0].GetProperty("capabilities")[0];
        Assert.Equal("quota.key", capability.GetProperty("id").GetString());
        Assert.Equal("Available", capability.GetProperty("state").GetString());
        Assert.Equal("scalar", root.GetProperty("snapshots")[0].GetProperty("metrics")[1].GetProperty("kind").GetString());
    }

    [Fact]
    public async Task LoadValidIndependentJsonMapsToDomain()
    {
        using var folder = new TemporaryFolder();
        await File.WriteAllTextAsync(folder.DocumentPath, ValidDocumentJson, Encoding.UTF8);
        var store = CreateStore(folder.DocumentPath);

        SnapshotCacheReadResult.Loaded result = Assert.IsType<SnapshotCacheReadResult.Loaded>(
            await store.LoadAsync());

        ProviderSnapshot snapshot = Assert.Single(result.Snapshots);
        Assert.Equal("fake", snapshot.ProviderId.Value);
        Assert.Equal(Now.AddSeconds(-30), snapshot.SourceObservedAtUtc);
        Assert.Equal(2, snapshot.Metrics.Count);
        Assert.Equal(42m, Assert.IsType<ProgressMetricSnapshot>(snapshot.Metrics[0]).Used);
        Assert.Equal(12.34m, Assert.IsType<ScalarMetricSnapshot>(snapshot.Metrics[1]).Value);
    }

    [Fact]
    public async Task LoadCorruptJsonQuarantinesOriginalBytes()
    {
        using var folder = new TemporaryFolder();
        const string corruptJson = "{ \"schemaVersion\": 1, \"snapshots\": [";
        await File.WriteAllTextAsync(folder.DocumentPath, corruptJson, Encoding.UTF8);
        var store = CreateStore(folder.DocumentPath);

        SnapshotCacheReadResult.Corrupt result = Assert.IsType<SnapshotCacheReadResult.Corrupt>(
            await store.LoadAsync());

        Assert.False(File.Exists(folder.DocumentPath));
        string quarantinePath = Path.Combine(folder.Path, result.QuarantineFileName);
        Assert.True(File.Exists(quarantinePath));
        Assert.Equal(corruptJson, await File.ReadAllTextAsync(quarantinePath, Encoding.UTF8));
        Assert.DoesNotContain(folder.Path, result.QuarantineFileName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProbeMissingCacheDoesNotCreateIt()
    {
        using var folder = new TemporaryFolder();
        var store = CreateStore(folder.DocumentPath);

        SnapshotCacheProbeResult result = await store.ProbeAsync();

        Assert.IsType<SnapshotCacheProbeResult.Missing>(result);
        Assert.False(File.Exists(folder.DocumentPath));
    }

    [Fact]
    public async Task ProbeValidCacheReportsPresentWithoutChangingBytes()
    {
        using var folder = new TemporaryFolder();
        await File.WriteAllTextAsync(folder.DocumentPath, ValidDocumentJson, Encoding.UTF8);
        byte[] before = await File.ReadAllBytesAsync(folder.DocumentPath);
        DateTime lastWriteBefore = File.GetLastWriteTimeUtc(folder.DocumentPath);
        var store = CreateStore(folder.DocumentPath);

        SnapshotCacheProbeResult result = await store.ProbeAsync();

        Assert.IsType<SnapshotCacheProbeResult.Present>(result);
        Assert.Equal(before, await File.ReadAllBytesAsync(folder.DocumentPath));
        Assert.Equal(lastWriteBefore, File.GetLastWriteTimeUtc(folder.DocumentPath));
    }

    [Fact]
    public async Task ProbeProviderReportsOnlyTheRequestedSnapshotWithoutChangingBytes()
    {
        using var folder = new TemporaryFolder();
        var store = CreateStore(folder.DocumentPath);
        await store.SaveLastGoodAsync([]);
        Assert.IsType<SnapshotCacheProbeResult.Missing>(
            await store.ProbeProviderAsync(new ProviderId("codex")));

        await store.UpsertLastGoodAsync(CreateSnapshot("fake", 42m));
        byte[] before = await File.ReadAllBytesAsync(folder.DocumentPath);
        DateTime lastWriteBefore = File.GetLastWriteTimeUtc(folder.DocumentPath);

        Assert.IsType<SnapshotCacheProbeResult.Missing>(
            await store.ProbeProviderAsync(new ProviderId("codex")));
        Assert.IsType<SnapshotCacheProbeResult.Present>(
            await store.ProbeProviderAsync(new ProviderId("fake")));
        Assert.Equal(before, await File.ReadAllBytesAsync(folder.DocumentPath));
        Assert.Equal(lastWriteBefore, File.GetLastWriteTimeUtc(folder.DocumentPath));
    }

    [Fact]
    public async Task ProbeCorruptCacheNeverQuarantinesIt()
    {
        using var folder = new TemporaryFolder();
        const string corruptJson = "{ \"schemaVersion\": 1, \"snapshots\": [";
        await File.WriteAllTextAsync(folder.DocumentPath, corruptJson, Encoding.UTF8);
        var store = CreateStore(folder.DocumentPath);

        SnapshotCacheProbeResult result = await store.ProbeAsync();

        Assert.IsType<SnapshotCacheProbeResult.Unreadable>(result);
        Assert.True(File.Exists(folder.DocumentPath));
        Assert.Equal(corruptJson, await File.ReadAllTextAsync(folder.DocumentPath, Encoding.UTF8));
        Assert.Single(Directory.GetFiles(folder.Path));
    }

    [Fact]
    public async Task ProbeFutureSchemaReportsUnsupportedWithoutChangingIt()
    {
        using var folder = new TemporaryFolder();
        const string futureJson = "{ \"schemaVersion\": 2, \"snapshots\": [] }";
        await File.WriteAllTextAsync(folder.DocumentPath, futureJson, Encoding.UTF8);
        var store = CreateStore(folder.DocumentPath);

        SnapshotCacheProbeResult result = await store.ProbeAsync();

        Assert.IsType<SnapshotCacheProbeResult.UnsupportedVersion>(result);
        Assert.Equal(futureJson, await File.ReadAllTextAsync(folder.DocumentPath, Encoding.UTF8));
        Assert.Single(Directory.GetFiles(folder.Path));
    }

    [Fact]
    public async Task LoadInvalidDomainDataTreatsWholeDocumentAsCorrupt()
    {
        using var folder = new TemporaryFolder();
        string invalidJson = ValidDocumentJson.Replace("\"limit\": 100", "\"limit\": 0", StringComparison.Ordinal);
        await File.WriteAllTextAsync(folder.DocumentPath, invalidJson, Encoding.UTF8);
        var store = CreateStore(folder.DocumentPath);

        SnapshotCacheReadResult result = await store.LoadAsync();

        Assert.IsType<SnapshotCacheReadResult.Corrupt>(result);
        Assert.False(File.Exists(folder.DocumentPath));
    }

    [Fact]
    public async Task LoadUnknownProgressCadenceTreatsWholeDocumentAsCorrupt()
    {
        using var folder = new TemporaryFolder();
        string invalidJson = ValidDocumentJson.Replace(
            "\"resetsAtUtc\": \"2026-07-22T16:00:00Z\"",
            "\"resetsAtUtc\": \"2026-07-22T16:00:00Z\", \"resetCadence\": \"Hourly\"",
            StringComparison.Ordinal);
        await File.WriteAllTextAsync(folder.DocumentPath, invalidJson, Encoding.UTF8);
        var store = CreateStore(folder.DocumentPath);

        SnapshotCacheReadResult result = await store.LoadAsync();

        Assert.IsType<SnapshotCacheReadResult.Corrupt>(result);
        Assert.False(File.Exists(folder.DocumentPath));
    }

    [Theory]
    [MemberData(nameof(DocumentsWithNullListEntries))]
    public async Task LoadNullListEntryQuarantinesInsteadOfFailing(string invalidJson)
    {
        using var folder = new TemporaryFolder();
        await File.WriteAllTextAsync(folder.DocumentPath, invalidJson, Encoding.UTF8);
        var store = CreateStore(folder.DocumentPath);

        SnapshotCacheReadResult.Corrupt result = Assert.IsType<SnapshotCacheReadResult.Corrupt>(
            await store.LoadAsync());

        Assert.False(File.Exists(folder.DocumentPath));
        Assert.True(File.Exists(System.IO.Path.Combine(folder.Path, result.QuarantineFileName)));
    }

    [Fact]
    public async Task FutureSchemaIsNeverChangedByLoadOrSave()
    {
        using var folder = new TemporaryFolder();
        const string futureJson = "{\"schemaVersion\":2,\"writtenAtUtc\":\"2026-07-22T12:00:00Z\",\"snapshots\":[]}";
        await File.WriteAllTextAsync(folder.DocumentPath, futureJson, Encoding.UTF8);
        byte[] before = await File.ReadAllBytesAsync(folder.DocumentPath);
        var store = CreateStore(folder.DocumentPath);

        SnapshotCacheReadResult.UnsupportedVersion read =
            Assert.IsType<SnapshotCacheReadResult.UnsupportedVersion>(await store.LoadAsync());
        SnapshotCacheSaveResult.RefusedUnsupportedVersion save =
            Assert.IsType<SnapshotCacheSaveResult.RefusedUnsupportedVersion>(
                await store.UpsertLastGoodAsync(CreateSnapshot("fake", 50m)));

        Assert.Equal(2, read.SchemaVersion);
        Assert.Equal(2, save.SchemaVersion);
        Assert.Equal(SHA256.HashData(before), SHA256.HashData(await File.ReadAllBytesAsync(folder.DocumentPath)));
        Assert.Empty(Directory.GetFiles(folder.Path, "*.tmp"));
        Assert.Empty(Directory.GetFiles(folder.Path, "*.corrupt-*"));
    }

    [Fact]
    public async Task InterruptedTemporaryFileDoesNotReplaceLastGoodDocument()
    {
        using var folder = new TemporaryFolder();
        var store = CreateStore(folder.DocumentPath);
        await store.UpsertLastGoodAsync(CreateSnapshot("fake", 42m));
        string orphanPath = Path.Combine(folder.Path, $"{Path.GetFileName(folder.DocumentPath)}.{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(orphanPath, "{ interrupted", Encoding.UTF8);

        SnapshotCacheReadResult.Loaded result = Assert.IsType<SnapshotCacheReadResult.Loaded>(
            await store.LoadAsync());

        Assert.Equal(42m, Assert.IsType<ProgressMetricSnapshot>(Assert.Single(result.Snapshots).Metrics[0]).Used);
        Assert.True(File.Exists(orphanPath));
    }

    [Fact]
    public async Task ConcurrentProcessesMergeProvidersAndLeaveParseableJson()
    {
        if (Environment.GetEnvironmentVariable(ProcessRootVariable) is string processRoot)
        {
            await RunProcessWorkerAsync(processRoot);
            return;
        }

        using var folder = new TemporaryFolder();
        string startPath = System.IO.Path.Combine(folder.Path, "process-start.gate");
        using WorkerProcess workerA = StartWorkerProcess(folder.Path, "fake-a", startPath);
        using WorkerProcess workerB = StartWorkerProcess(folder.Path, "fake-b", startPath);
        await WaitForWorkersReadyAsync([workerA, workerB], TimeSpan.FromSeconds(30));
        await File.WriteAllTextAsync(startPath, "start", Encoding.UTF8);
        await Task.WhenAll(workerA.WaitForExitAsync(), workerB.WaitForExitAsync());
        await workerA.AssertSucceededAsync();
        await workerB.AssertSucceededAsync();

        var storeA = CreateStore(folder.DocumentPath);
        SnapshotCacheReadResult.Loaded loaded = Assert.IsType<SnapshotCacheReadResult.Loaded>(
            await storeA.LoadAsync());
        Assert.Equal(["fake-a", "fake-b"], loaded.Snapshots.Select(snapshot => snapshot.ProviderId.Value));
        using JsonDocument _ = JsonDocument.Parse(await File.ReadAllTextAsync(folder.DocumentPath));
        Assert.Empty(Directory.GetFiles(folder.Path, "*.tmp"));
    }

    [Fact]
    public async Task UpsertReplacesOneProviderAndPreservesTheOthers()
    {
        using var folder = new TemporaryFolder();
        var store = CreateStore(folder.DocumentPath);
        await store.SaveLastGoodAsync(
            [CreateSnapshot("fake-a", 10m), CreateSnapshot("fake-b", 20m)]);

        await store.UpsertLastGoodAsync(CreateSnapshot("fake-a", 90m));

        SnapshotCacheReadResult.Loaded loaded = Assert.IsType<SnapshotCacheReadResult.Loaded>(
            await store.LoadAsync());
        Assert.Equal(90m, ProgressUsed(loaded, "fake-a"));
        Assert.Equal(20m, ProgressUsed(loaded, "fake-b"));
    }

    [Fact]
    public async Task RemoveProviderPreservesOtherSnapshotsAndWritesValidJson()
    {
        using var folder = new TemporaryFolder();
        var store = CreateStore(folder.DocumentPath);
        await store.SaveLastGoodAsync(
            [CreateSnapshot("fake-b", 20m), CreateSnapshot("fake-a", 10m)]);

        SnapshotCacheRemoveResult.Removed removed =
            Assert.IsType<SnapshotCacheRemoveResult.Removed>(
                await store.RemoveProviderAsync(new ProviderId("fake-a")));

        ProviderSnapshot remaining = Assert.Single(removed.RemainingSnapshots);
        Assert.Equal("fake-b", remaining.ProviderId.Value);
        SnapshotCacheReadResult.Loaded loaded = Assert.IsType<SnapshotCacheReadResult.Loaded>(
            await store.LoadAsync());
        Assert.Equal(20m, ProgressUsed(loaded, "fake-b"));
        using JsonDocument document = JsonDocument.Parse(
            await File.ReadAllTextAsync(folder.DocumentPath));
        Assert.Equal("fake-b", document.RootElement
            .GetProperty("snapshots")[0]
            .GetProperty("providerId")
            .GetString());
    }

    [Fact]
    public async Task RemoveOnlyProviderWritesValidEmptyDocument()
    {
        using var folder = new TemporaryFolder();
        var store = CreateStore(folder.DocumentPath);
        await store.UpsertLastGoodAsync(CreateSnapshot("fake", 42m));

        SnapshotCacheRemoveResult.Removed removed =
            Assert.IsType<SnapshotCacheRemoveResult.Removed>(
                await store.RemoveProviderAsync(new ProviderId("fake")));

        Assert.Empty(removed.RemainingSnapshots);
        SnapshotCacheReadResult.Loaded loaded = Assert.IsType<SnapshotCacheReadResult.Loaded>(
            await store.LoadAsync());
        Assert.Empty(loaded.Snapshots);
    }

    [Fact]
    public async Task RemoveMissingProviderDoesNotCreateOrRewriteCache()
    {
        using var folder = new TemporaryFolder();
        var store = CreateStore(folder.DocumentPath);

        Assert.IsType<SnapshotCacheRemoveResult.Missing>(
            await store.RemoveProviderAsync(new ProviderId("fake")));
        Assert.False(File.Exists(folder.DocumentPath));

        await store.UpsertLastGoodAsync(CreateSnapshot("other", 42m));
        byte[] before = await File.ReadAllBytesAsync(folder.DocumentPath);
        DateTime lastWriteBefore = File.GetLastWriteTimeUtc(folder.DocumentPath);

        Assert.IsType<SnapshotCacheRemoveResult.Missing>(
            await store.RemoveProviderAsync(new ProviderId("fake")));
        Assert.Equal(before, await File.ReadAllBytesAsync(folder.DocumentPath));
        Assert.Equal(lastWriteBefore, File.GetLastWriteTimeUtc(folder.DocumentPath));
    }

    [Fact]
    public async Task RemoveProviderRefusesFutureSchemaWithoutChangingBytes()
    {
        using var folder = new TemporaryFolder();
        const string futureJson =
            "{\"schemaVersion\":2,\"writtenAtUtc\":\"2026-07-22T12:00:00Z\",\"snapshots\":[]}";
        await File.WriteAllTextAsync(folder.DocumentPath, futureJson, Encoding.UTF8);
        byte[] before = await File.ReadAllBytesAsync(folder.DocumentPath);
        var store = CreateStore(folder.DocumentPath);

        SnapshotCacheRemoveResult.RefusedUnsupportedVersion refused =
            Assert.IsType<SnapshotCacheRemoveResult.RefusedUnsupportedVersion>(
                await store.RemoveProviderAsync(new ProviderId("fake")));

        Assert.Equal(2, refused.SchemaVersion);
        Assert.Equal(before, await File.ReadAllBytesAsync(folder.DocumentPath));
        Assert.Empty(Directory.GetFiles(folder.Path, "*.tmp"));
        Assert.Empty(Directory.GetFiles(folder.Path, "*.corrupt-*"));
    }

    [Fact]
    public async Task RemoveProviderQuarantinesCorruptCacheAndReturnsFileNameOnly()
    {
        using var folder = new TemporaryFolder();
        const string corruptJson = "{ broken";
        await File.WriteAllTextAsync(folder.DocumentPath, corruptJson, Encoding.UTF8);
        var store = CreateStore(folder.DocumentPath);

        SnapshotCacheRemoveResult.Unreadable unreadable =
            Assert.IsType<SnapshotCacheRemoveResult.Unreadable>(
                await store.RemoveProviderAsync(new ProviderId("fake")));

        Assert.False(File.Exists(folder.DocumentPath));
        Assert.Equal(unreadable.QuarantineFileName, Path.GetFileName(unreadable.QuarantineFileName));
        Assert.Equal(
            corruptJson,
            await File.ReadAllTextAsync(
                Path.Combine(folder.Path, unreadable.QuarantineFileName),
                Encoding.UTF8));
    }

    [Fact]
    public async Task RemoveProviderWriteFailurePreservesPreviousDocument()
    {
        using var folder = new TemporaryFolder();
        var store = CreateStore(folder.DocumentPath);
        await store.SaveLastGoodAsync(
            [CreateSnapshot("fake-a", 10m), CreateSnapshot("fake-b", 20m)]);
        byte[] before = await File.ReadAllBytesAsync(folder.DocumentPath);

        await using (FileStream heldDocument = new(
            folder.DocumentPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read))
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                store.RemoveProviderAsync(new ProviderId("fake-a")));
        }

        Assert.Equal(before, await File.ReadAllBytesAsync(folder.DocumentPath));
        Assert.Empty(Directory.GetFiles(folder.Path, "*.tmp"));
    }

    [Fact]
    public async Task RemoveProviderCancellationBeforeLockChangesNothing()
    {
        using var folder = new TemporaryFolder();
        var store = CreateStore(folder.DocumentPath);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.RemoveProviderAsync(new ProviderId("fake"), cancellation.Token));

        Assert.Empty(Directory.GetFiles(folder.Path));
    }

    [Fact]
    public async Task SaveOverCorruptDocumentQuarantinesItBeforeWritingFreshData()
    {
        using var folder = new TemporaryFolder();
        const string corruptJson = "{ broken";
        await File.WriteAllTextAsync(folder.DocumentPath, corruptJson, Encoding.UTF8);
        var store = CreateStore(folder.DocumentPath);

        SnapshotCacheSaveResult.Saved save = Assert.IsType<SnapshotCacheSaveResult.Saved>(
            await store.UpsertLastGoodAsync(CreateSnapshot("fake", 55m)));

        Assert.Single(save.Snapshots);
        Assert.Single(Directory.GetFiles(folder.Path, "*.corrupt-*"));
        SnapshotCacheReadResult.Loaded loaded = Assert.IsType<SnapshotCacheReadResult.Loaded>(
            await store.LoadAsync());
        Assert.Equal(55m, ProgressUsed(loaded, "fake"));
    }

    [Fact]
    public async Task FailedReplacementKeepsThePreviousDocumentAndCleansItsTemporaryFile()
    {
        using var folder = new TemporaryFolder();
        var store = CreateStore(folder.DocumentPath);
        await store.UpsertLastGoodAsync(CreateSnapshot("fake", 25m));
        byte[] before = await File.ReadAllBytesAsync(folder.DocumentPath);

        await using (FileStream heldDocument = new(
            folder.DocumentPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read))
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                store.UpsertLastGoodAsync(CreateSnapshot("fake", 75m)));
        }

        Assert.Equal(before, await File.ReadAllBytesAsync(folder.DocumentPath));
        Assert.Empty(Directory.GetFiles(folder.Path, "*.tmp"));
    }

    [Fact]
    public async Task CancellationBeforeLockDoesNotCreateCacheArtifacts()
    {
        using var folder = new TemporaryFolder();
        var store = CreateStore(folder.DocumentPath);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.UpsertLastGoodAsync(CreateSnapshot("fake", 42m), cancellation.Token));

        Assert.Empty(Directory.GetFiles(folder.Path));
    }

    [Fact]
    public async Task LoadCancellationWhileWaitingForSharedMutexLeavesCacheUntouched()
    {
        using var folder = new TemporaryFolder();
        var store = CreateStore(folder.DocumentPath);
        await store.UpsertLastGoodAsync(CreateSnapshot("fake", 42m));
        byte[] before = await File.ReadAllBytesAsync(folder.DocumentPath);
        using var holderReady = new ManualResetEventSlim();
        using var releaseHolder = new ManualResetEventSlim();
        Exception? holderFailure = null;
        var holder = new Thread(() =>
        {
            try
            {
                using var heldMutex = new Mutex(
                    initiallyOwned: false,
                    CreateSnapshotMutexName(folder.DocumentPath));
                if (!heldMutex.WaitOne(TimeSpan.FromSeconds(1)))
                {
                    throw new TimeoutException("Test mutex holder could not acquire the cache lock.");
                }

                holderReady.Set();
                releaseHolder.Wait();
                heldMutex.ReleaseMutex();
            }
            catch (Exception exception)
            {
                holderFailure = exception;
                holderReady.Set();
            }
        })
        {
            IsBackground = true,
        };
        holder.Start();
        Assert.True(holderReady.Wait(TimeSpan.FromSeconds(2)));
        Assert.Null(holderFailure);
        try
        {
            using var cancellation = new CancellationTokenSource();
            Task<SnapshotCacheReadResult> load = store.LoadAsync(cancellation.Token);
            await Task.Delay(TimeSpan.FromMilliseconds(200));
            Assert.False(load.IsCompleted);
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => load);
        }
        finally
        {
            releaseHolder.Set();
        }
        Assert.True(holder.Join(TimeSpan.FromSeconds(2)));
        Assert.Null(holderFailure);

        Assert.Equal(before, await File.ReadAllBytesAsync(folder.DocumentPath));
        Assert.Empty(Directory.GetFiles(folder.Path, "*.tmp"));
        Assert.Empty(Directory.GetFiles(folder.Path, "*.corrupt-*"));
    }

    private static SnapshotStore CreateStore(string path) =>
        new(path, new FixedTimeProvider(Now));

    private static string CreateSnapshotMutexName(string documentPath)
    {
        string normalizedPath = Path.GetFullPath(documentPath).ToUpperInvariant();
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPath));
        return $"Local\\TokenUsage.SnapshotStore.{Convert.ToHexString(hash.AsSpan(0, 16))}";
    }

    private static WorkerProcess StartWorkerProcess(
        string rootPath,
        string providerId,
        string startPath)
    {
        string readyPath = System.IO.Path.Combine(rootPath, $"{providerId}.ready");
        string assemblyPath = typeof(SnapshotStoreTests).Assembly.Location;
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("vstest");
        startInfo.ArgumentList.Add(assemblyPath);
        startInfo.ArgumentList.Add(
            "--Tests:TokenUsage.Core.Tests.SnapshotStoreTests.ConcurrentProcessesMergeProvidersAndLeaveParseableJson");
        startInfo.ArgumentList.Add("--logger:console;verbosity=minimal");
        startInfo.Environment[ProcessRootVariable] = rootPath;
        startInfo.Environment[ProcessProviderVariable] = providerId;
        startInfo.Environment[ProcessReadyVariable] = readyPath;
        startInfo.Environment[ProcessStartVariable] = startPath;

        Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the snapshot cache worker process.");
        return new WorkerProcess(process, readyPath);
    }

    private static async Task RunProcessWorkerAsync(string rootPath)
    {
        string providerId = RequireProcessValue(ProcessProviderVariable);
        string readyPath = RequireProcessValue(ProcessReadyVariable);
        string startPath = RequireProcessValue(ProcessStartVariable);
        Directory.CreateDirectory(rootPath);
        await File.WriteAllTextAsync(
            readyPath,
            Environment.ProcessId.ToString(CultureInfo.InvariantCulture),
            Encoding.UTF8);

        var timeout = Stopwatch.StartNew();
        while (!File.Exists(startPath))
        {
            if (timeout.Elapsed > TimeSpan.FromSeconds(30))
            {
                throw new TimeoutException("The process worker did not receive its start gate.");
            }

            await Task.Delay(20);
        }

        var store = CreateStore(System.IO.Path.Combine(rootPath, SnapshotStore.DefaultFileName));
        decimal used = providerId == "fake-a" ? 10m : 20m;
        await store.UpsertLastGoodAsync(CreateSnapshot(providerId, used));
    }

    private static async Task WaitForWorkersReadyAsync(
        IReadOnlyList<WorkerProcess> workers,
        TimeSpan timeout)
    {
        var elapsed = Stopwatch.StartNew();
        while (workers.Any(worker => !File.Exists(worker.ReadyPath)))
        {
            WorkerProcess? failed = workers.FirstOrDefault(worker => worker.Process.HasExited);
            if (failed is not null)
            {
                await failed.AssertSucceededAsync();
                throw new InvalidOperationException("A cache worker exited before opening the start gate.");
            }

            if (elapsed.Elapsed > timeout)
            {
                throw new TimeoutException("The cache workers did not become ready in time.");
            }

            await Task.Delay(20);
        }
    }

    private static string RequireProcessValue(string variableName) =>
        Environment.GetEnvironmentVariable(variableName)
        ?? throw new InvalidOperationException($"Process worker variable '{variableName}' is missing.");

    private static decimal ProgressUsed(SnapshotCacheReadResult.Loaded loaded, string providerId) =>
        Assert.IsType<ProgressMetricSnapshot>(
            loaded.Snapshots.Single(snapshot => snapshot.ProviderId.Value == providerId).Metrics[0]).Used;

    private static ProviderSnapshot CreateSnapshot(string providerId, decimal used) =>
        new(
            new ProviderId(providerId),
            $"Provider {providerId}",
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
                    CreateProvenance(),
                    "usd",
                    ProgressResetCadence.Monthly,
                    isActive: true),
                new ScalarMetricSnapshot(
                    new MetricId("spend-usd"),
                    12.34m,
                    "USD",
                    CreateProvenance()),
            ],
            CoverageKind.Complete,
            1,
            [
                new ProviderCapabilitySnapshot(
                    new CapabilityId("quota.key"),
                    ProviderCapabilityState.Available,
                    CreateProvenance()),
            ]);

    private static DataProvenance CreateProvenance() =>
        new(SourceKind.Synthetic, MeasurementKind.ProviderReported, "fake/1");

    private static void AssertSnapshot(ProviderSnapshot expected, ProviderSnapshot actual)
    {
        Assert.Equal(expected.ProviderId, actual.ProviderId);
        Assert.Equal(expected.DisplayName, actual.DisplayName);
        Assert.Equal(expected.PlanLabel, actual.PlanLabel);
        Assert.Equal(expected.FetchedAtUtc, actual.FetchedAtUtc);
        Assert.Equal(expected.SourceObservedAtUtc, actual.SourceObservedAtUtc);
        Assert.Equal(expected.TimeZoneId, actual.TimeZoneId);
        Assert.Equal(expected.Coverage, actual.Coverage);
        Assert.Equal(expected.AdapterContractVersion, actual.AdapterContractVersion);
        Assert.Equal(expected.Capabilities, actual.Capabilities);

        var expectedProgress = Assert.IsType<ProgressMetricSnapshot>(expected.Metrics[0]);
        var actualProgress = Assert.IsType<ProgressMetricSnapshot>(actual.Metrics[0]);
        Assert.Equal(expectedProgress.Id, actualProgress.Id);
        Assert.Equal(expectedProgress.Used, actualProgress.Used);
        Assert.Equal(expectedProgress.Limit, actualProgress.Limit);
        Assert.Equal(expectedProgress.ResetsAtUtc, actualProgress.ResetsAtUtc);
        Assert.Equal(expectedProgress.Unit, actualProgress.Unit);
        Assert.Equal(expectedProgress.ResetCadence, actualProgress.ResetCadence);
        Assert.Equal(expectedProgress.IsActive, actualProgress.IsActive);
        Assert.Equal(expectedProgress.Provenance, actualProgress.Provenance);

        var expectedScalar = Assert.IsType<ScalarMetricSnapshot>(expected.Metrics[1]);
        var actualScalar = Assert.IsType<ScalarMetricSnapshot>(actual.Metrics[1]);
        Assert.Equal(expectedScalar.Id, actualScalar.Id);
        Assert.Equal(expectedScalar.Value, actualScalar.Value);
        Assert.Equal(expectedScalar.Unit, actualScalar.Unit);
        Assert.Equal(expectedScalar.Provenance, actualScalar.Provenance);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class WorkerProcess : IDisposable
    {
        private readonly Task<string> _standardOutput;
        private readonly Task<string> _standardError;

        public WorkerProcess(Process process, string readyPath)
        {
            Process = process;
            ReadyPath = readyPath;
            _standardOutput = process.StandardOutput.ReadToEndAsync();
            _standardError = process.StandardError.ReadToEndAsync();
        }

        public Process Process { get; }

        public string ReadyPath { get; }

        public async Task WaitForExitAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await Process.WaitForExitAsync(timeout.Token);
        }

        public async Task AssertSucceededAsync()
        {
            string output = await _standardOutput;
            string error = await _standardError;
            Assert.True(
                Process.HasExited && Process.ExitCode == 0,
                $"Cache worker exited with code {Process.ExitCode}.{Environment.NewLine}{output}{Environment.NewLine}{error}");
        }

        public void Dispose() => Process.Dispose();
    }

    private sealed class TemporaryFolder : IDisposable
    {
        public TemporaryFolder()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "TokenUsage.Core.Tests",
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

    private const string ValidDocumentJson = """
        {
          "schemaVersion": 1,
          "writtenAtUtc": "2026-07-22T12:00:00Z",
          "snapshots": [
            {
              "providerId": "fake",
              "displayName": "Fake provider",
              "planLabel": "Sample",
              "fetchedAtUtc": "2026-07-22T12:00:00Z",
              "sourceObservedAtUtc": "2026-07-22T11:59:30Z",
              "timeZoneId": "UTC",
              "coverage": "Complete",
              "adapterContractVersion": 1,
              "metrics": [
                {
                  "kind": "progress",
                  "id": "session",
                  "used": 42,
                  "limit": 100,
                  "resetsAtUtc": "2026-07-22T16:00:00Z",
                  "provenance": {
                    "sourceKind": "Synthetic",
                    "measurementKind": "ProviderReported",
                    "adapterVersion": "fake/1"
                  }
                },
                {
                  "kind": "scalar",
                  "id": "spend-usd",
                  "value": 12.34,
                  "unit": "USD",
                  "provenance": {
                    "sourceKind": "Synthetic",
                    "measurementKind": "ProviderReported",
                    "adapterVersion": "fake/1"
                  }
                }
              ]
            }
          ]
        }
        """;

    private const string ProcessRootVariable = "TOKENUSAGE_CACHE_TEST_ROOT";
    private const string ProcessProviderVariable = "TOKENUSAGE_CACHE_TEST_PROVIDER";
    private const string ProcessReadyVariable = "TOKENUSAGE_CACHE_TEST_READY";
    private const string ProcessStartVariable = "TOKENUSAGE_CACHE_TEST_START";

    public static TheoryData<string> DocumentsWithNullListEntries =>
        new()
        {
            """
            {
              "schemaVersion": 1,
              "writtenAtUtc": "2026-07-22T12:00:00Z",
              "snapshots": [null]
            }
            """,
            """
            {
              "schemaVersion": 1,
              "writtenAtUtc": "2026-07-22T12:00:00Z",
              "snapshots": [
                {
                  "providerId": "fake",
                  "displayName": "Fake provider",
                  "planLabel": "Sample",
                  "fetchedAtUtc": "2026-07-22T12:00:00Z",
                  "sourceObservedAtUtc": "2026-07-22T11:59:30Z",
                  "timeZoneId": "UTC",
                  "coverage": "Complete",
                  "adapterContractVersion": 1,
                  "metrics": [null]
                }
              ]
            }
            """,
        };
}

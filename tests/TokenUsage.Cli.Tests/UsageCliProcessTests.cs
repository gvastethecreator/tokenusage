using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;
using TokenUsage.Providers.Fakes;

namespace TokenUsage.Cli.Tests;

public sealed class UsageCliProcessTests
{
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task ExecutableReadsTheSharedDatabaseAndReturnsJson()
    {
        string dataRoot = Path.Combine(
            Path.GetTempPath(),
            "tokenusage-cli-process-tests",
            Guid.NewGuid().ToString("N"));
        string scannerDirectory = Path.Combine(dataRoot, "scanner");
        Directory.CreateDirectory(scannerDirectory);

        try
        {
            string databasePath = Path.Combine(scannerDirectory, "usage.v1.db");
            TimeProvider clock = TimeProvider.System;
            UsageRepository repository = await UsageRepository.OpenAsync(databasePath);
            var source = new SyntheticUsageEventSource(clock, "Argentina Standard Time");
            await repository.IngestAsync((await source.ReadAsync()).Events);

            string executablePath = Path.Combine(AppContext.BaseDirectory, "tokenusage.exe");
            var startInfo = new ProcessStartInfo(executablePath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            startInfo.ArgumentList.Add("usage");
            startInfo.ArgumentList.Add("--days");
            startInfo.ArgumentList.Add("30");
            startInfo.ArgumentList.Add("--format");
            startInfo.ArgumentList.Add("json");
            startInfo.Environment["TOKENUSAGE_DATA_DIR"] = dataRoot;

            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("The CLI process did not start.");
            string standardOutput = await process.StandardOutput.ReadToEndAsync();
            string standardError = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            Assert.Equal(0, process.ExitCode);
            Assert.Equal(string.Empty, standardError);
            using JsonDocument document = JsonDocument.Parse(standardOutput);
            Assert.Equal(
                "tokenusage.usage.v1",
                document.RootElement.GetProperty("schemaVersion").GetString());
            Assert.Equal(3, document.RootElement.GetProperty("events").GetInt32());
            Assert.Equal(
                53_080,
                document.RootElement.GetProperty("tokens").GetProperty("total").GetInt64());
            Assert.Equal(
                1.84m,
                document.RootElement.GetProperty("costUsd").GetProperty("reported").GetDecimal());
        }
        finally
        {
            Directory.Delete(dataRoot, recursive: true);
        }
    }

    [Fact]
    public async Task WriterAndCliShareWalWithoutLockOrPartialReport()
    {
        string dataRoot = Path.Combine(
            Path.GetTempPath(),
            "tokenusage-usage-writer-cli-tests",
            Guid.NewGuid().ToString("N"));
        string scannerDirectory = Path.Combine(dataRoot, "scanner");
        Directory.CreateDirectory(scannerDirectory);

        try
        {
            string databasePath = Path.Combine(scannerDirectory, "usage.v1.db");
            DateTimeOffset occurredAtUtc = DateTimeOffset.UtcNow;
            DateOnly eventDate = DateOnly.FromDateTime(occurredAtUtc.UtcDateTime);
            UsageRepository writerRepository = await UsageRepository.OpenAsync(databasePath);
            await writerRepository.IngestAsync([CreateEvent(0, occurredAtUtc)]);

            using var stopWriter = new CancellationTokenSource(ProcessTimeout);
            var firstWrite = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var writeWhileProbeOpen = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            int probeWriteRequested = 0;
            int insertedEventCount = 1;
            Task writer = Task.Run(async () =>
            {
                int identity = 1;
                while (!stopWriter.IsCancellationRequested)
                {
                    UsageIngestResult result = await writerRepository.IngestAsync(
                        [CreateEvent(identity++, occurredAtUtc)]);
                    if (result.InsertedCount != 1 || result.DuplicateCount != 0)
                    {
                        throw new InvalidOperationException("The concurrency writer lost an event.");
                    }

                    Interlocked.Increment(ref insertedEventCount);
                    firstWrite.TrySetResult();
                    if (Volatile.Read(ref probeWriteRequested) == 1)
                    {
                        writeWhileProbeOpen.TrySetResult();
                    }
                    await Task.Yield();
                }
            });

            ProcessResult[] results;
            try
            {
                await firstWrite.Task.WaitAsync(ProcessTimeout);
                await using (var walProbe = new SqliteConnection(
                    new SqliteConnectionStringBuilder
                    {
                        DataSource = databasePath,
                        Mode = SqliteOpenMode.ReadOnly,
                        Pooling = false,
                        DefaultTimeout = 5,
                    }.ToString()))
                {
                    await walProbe.OpenAsync();
                    Volatile.Write(ref probeWriteRequested, 1);
                    await writeWhileProbeOpen.Task.WaitAsync(ProcessTimeout);
                    await using SqliteCommand journalMode = walProbe.CreateCommand();
                    journalMode.CommandText = "PRAGMA journal_mode;";
                    Assert.Equal("wal", await journalMode.ExecuteScalarAsync());
                    Assert.True(File.Exists($"{databasePath}-wal"));
                    results = await Task.WhenAll(
                        RunUsageProcessAsync(dataRoot),
                        RunUsageProcessAsync(dataRoot),
                        RunUsageProcessAsync(dataRoot),
                        RunUsageProcessAsync(dataRoot));
                }
            }
            finally
            {
                stopWriter.Cancel();
                await writer.WaitAsync(ProcessTimeout);
            }

            int finalEventCount = Volatile.Read(ref insertedEventCount);
            foreach (ProcessResult result in results)
            {
                Assert.Equal(0, result.ExitCode);
                Assert.Equal(string.Empty, result.StandardError);
                using JsonDocument document = JsonDocument.Parse(result.StandardOutput);
                Assert.Equal(
                    "tokenusage.usage.v1",
                    document.RootElement.GetProperty("schemaVersion").GetString());
                int eventCount = document.RootElement.GetProperty("events").GetInt32();
                Assert.InRange(eventCount, 1, finalEventCount);
                Assert.Equal(
                    eventCount * 150L,
                    document.RootElement.GetProperty("tokens").GetProperty("total").GetInt64());
                Assert.Equal(
                    eventCount * 0.25m,
                    document.RootElement.GetProperty("costUsd").GetProperty("reported").GetDecimal());
                Assert.Equal(
                    JsonValueKind.Null,
                    document.RootElement.GetProperty("costUsd").GetProperty("estimated").ValueKind);
            }

            DailyUsageRollup finalRollup = Assert.Single(
                await writerRepository.QueryDailyRollupsAsync(eventDate, eventDate));
            Assert.Equal(finalEventCount, finalRollup.EventCount);
            Assert.Equal(finalEventCount * 150L, finalRollup.Tokens.Total);
            Assert.Equal(finalEventCount * 0.25m, finalRollup.ReportedCostUsd);
            Assert.Equal(0, finalRollup.UnpricedTokens);

            string databaseName = Path.GetFileName(databasePath);
            string[] allowedFiles = [databaseName, $"{databaseName}-wal", $"{databaseName}-shm"];
            Assert.All(
                Directory.EnumerateFiles(scannerDirectory),
                path => Assert.Contains(Path.GetFileName(path), allowedFiles));
        }
        finally
        {
            Directory.Delete(dataRoot, recursive: true);
        }
    }

    private static async Task<ProcessResult> RunUsageProcessAsync(string dataRoot)
    {
        string executablePath = Path.Combine(AppContext.BaseDirectory, "tokenusage.exe");
        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("usage");
        startInfo.ArgumentList.Add("--days");
        startInfo.ArgumentList.Add("30");
        startInfo.ArgumentList.Add("--format");
        startInfo.ArgumentList.Add("json");
        startInfo.Environment["TOKENUSAGE_DATA_DIR"] = dataRoot;

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The CLI process did not start.");
        Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> standardErrorTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(ProcessTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw new TimeoutException("The CLI process did not exit within 30 seconds.");
        }

        return new ProcessResult(
            process.ExitCode,
            await standardOutputTask,
            await standardErrorTask);
    }

    private static UsageEvent CreateEvent(int identity, DateTimeOffset occurredAtUtc)
    {
        string eventKey = Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes($"usage-concurrency-{identity}")))
            .ToLowerInvariant();
        return new UsageEvent(
            new UsageEventKey(eventKey),
            new AgentId("grok"),
            new ModelProviderId("xai"),
            new ModelId("grok-test"),
            occurredAtUtc,
            "UTC",
            new TokenBreakdown(100, 25, 5, 10, 10),
            CostObservation.ProviderReported(0.25m),
            "concurrency-test/1",
            CoverageKind.Complete);
    }

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}

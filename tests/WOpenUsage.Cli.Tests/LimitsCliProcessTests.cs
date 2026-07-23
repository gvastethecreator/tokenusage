using System.Diagnostics;
using System.Text.Json;
using WOpenUsage.Core.Cache;
using WOpenUsage.Core.Providers;
using WOpenUsage.Platform.Windows.Processes;

namespace WOpenUsage.Cli.Tests;

public sealed class LimitsCliProcessTests
{
    private const string FakeModeEnvironmentVariable = "WOPENUSAGE_FAKE_CODEX_MODE";
    private const string FakeNowEnvironmentVariable = "WOPENUSAGE_FAKE_NOW_UTC";

    [Fact]
    public async Task ConcurrentProcessesReadSharedCacheWithoutDamage()
    {
        string dataRoot = Path.Combine(
            Path.GetTempPath(),
            "tokenusage-limits-process-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataRoot);

        try
        {
            string cachePath = Path.Combine(
                dataRoot,
                "cache",
                "providers",
                "codex",
                SnapshotStore.DefaultFileName);
            var store = new SnapshotStore(cachePath);
            await store.UpsertLastGoodAsync(LimitsCommandTests.CreateCodexSnapshot());

            Task<ProcessResult> first = RunLimitsProcessAsync(dataRoot);
            Task<ProcessResult> second = RunLimitsProcessAsync(dataRoot);
            ProcessResult[] results = await Task.WhenAll(first, second);

            foreach (ProcessResult result in results)
            {
                Assert.Equal(0, result.ExitCode);
                Assert.Equal(string.Empty, result.StandardError);
                using JsonDocument document = JsonDocument.Parse(result.StandardOutput);
                Assert.Equal(
                    "wusage.limits.v1",
                    document.RootElement.GetProperty("schemaVersion").GetString());
                Assert.Equal(
                    "codex",
                    Assert.Single(document.RootElement
                        .GetProperty("providers")
                        .EnumerateArray())
                        .GetProperty("id")
                        .GetString());
            }

            SnapshotCacheReadResult.Loaded loaded =
                Assert.IsType<SnapshotCacheReadResult.Loaded>(await store.LoadAsync());
            Assert.Equal("codex", Assert.Single(loaded.Snapshots).ProviderId.Value);
            Assert.Empty(Directory.EnumerateFiles(
                Path.GetDirectoryName(cachePath)!,
                "*.corrupt-*",
                SearchOption.TopDirectoryOnly));
        }
        finally
        {
            Directory.Delete(dataRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ForceRefreshUsesRealCodexProtocolAndUpdatesSharedCache()
    {
        string dataRoot = Path.Combine(
            Path.GetTempPath(),
            "tokenusage-limits-force-process-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataRoot);

        try
        {
            ProcessResult result = await RunLimitsProcessAsync(dataRoot, forceRefresh: true);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(string.Empty, result.StandardError);
            Assert.DoesNotContain("private-live@example.invalid", result.StandardOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("auth.json", result.StandardOutput, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Bearer", result.StandardOutput, StringComparison.OrdinalIgnoreCase);
            using JsonDocument document = JsonDocument.Parse(result.StandardOutput);
            JsonElement provider = Assert.Single(document.RootElement
                .GetProperty("providers")
                .EnumerateArray());
            Assert.Equal("codex", provider.GetProperty("id").GetString());
            Assert.Contains(
                provider.GetProperty("metrics").EnumerateArray(),
                metric => string.Equals(
                    metric.GetProperty("id").GetString(),
                    "quota.primary",
                    StringComparison.Ordinal));

            string cachePath = Path.Combine(
                dataRoot,
                "cache",
                "providers",
                "codex",
                SnapshotStore.DefaultFileName);
            SnapshotCacheReadResult.Loaded cached = Assert.IsType<SnapshotCacheReadResult.Loaded>(
                await new SnapshotStore(cachePath).LoadAsync());
            Assert.Equal("codex", Assert.Single(cached.Snapshots).ProviderId.Value);
        }
        finally
        {
            Directory.Delete(dataRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ForceFailureKeepsCachedSnapshotWithoutLeakingProcessPath()
    {
        string dataRoot = Path.Combine(
            Path.GetTempPath(),
            "tokenusage-limits-force-failure-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataRoot);

        try
        {
            string cachePath = Path.Combine(
                dataRoot,
                "cache",
                "providers",
                "codex",
                SnapshotStore.DefaultFileName);
            await new SnapshotStore(cachePath).UpsertLastGoodAsync(
                LimitsCommandTests.CreateCodexSnapshot());
            string privateOverride = Path.Combine(
                dataRoot,
                "private-account",
                "codex.exe");

            ProcessResult result = await RunLimitsProcessAsync(
                dataRoot,
                forceRefresh: true,
                privateOverride);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(string.Empty, result.StandardError);
            Assert.DoesNotContain("private-account", result.StandardOutput, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(dataRoot, result.StandardOutput, StringComparison.OrdinalIgnoreCase);
            using JsonDocument document = JsonDocument.Parse(result.StandardOutput);
            Assert.Equal(
                "codex",
                Assert.Single(document.RootElement
                    .GetProperty("providers")
                    .EnumerateArray())
                    .GetProperty("id")
                    .GetString());
            Assert.True(document.RootElement.GetProperty("stale").GetBoolean());
        }
        finally
        {
            Directory.Delete(dataRoot, recursive: true);
        }
    }

    private static async Task<ProcessResult> RunLimitsProcessAsync(
        string dataRoot,
        bool forceRefresh = false,
        string? codexExecutableOverride = null)
    {
        string executablePath = Path.Combine(AppContext.BaseDirectory, "WOpenUsage.Cli.exe");
        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("limits");
        startInfo.ArgumentList.Add("codex");
        if (forceRefresh)
        {
            startInfo.ArgumentList.Add("--force");
            startInfo.Environment[CodexExecutableResolver.OverrideEnvironmentVariable] =
                codexExecutableOverride ?? GetFakeCodexPath();
            if (codexExecutableOverride is null)
            {
                startInfo.Environment[FakeModeEnvironmentVariable] = "quota";
                startInfo.Environment[FakeNowEnvironmentVariable] =
                    "2026-07-23T03:00:00.0000000+00:00";
            }
        }

        startInfo.ArgumentList.Add("--format");
        startInfo.ArgumentList.Add("json");
        startInfo.Environment["TOKENUSAGE_DATA_DIR"] = dataRoot;

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The CLI process did not start.");
        string standardOutput = await process.StandardOutput.ReadToEndAsync();
        string standardError = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessResult(process.ExitCode, standardOutput, standardError);
    }

    private static string GetFakeCodexPath()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "FakeCodex", "codex.exe");
        Assert.True(File.Exists(path), $"Fake Codex executable is missing: {path}");
        return path;
    }

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}

using System.Diagnostics;
using System.Text.Json;
using WOpenUsage.Core.Cache;
using WOpenUsage.Core.Providers;

namespace WOpenUsage.Cli.Tests;

public sealed class LimitsCliProcessTests
{
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

    private static async Task<ProcessResult> RunLimitsProcessAsync(string dataRoot)
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

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}

using System.Diagnostics;
using System.Text.Json;
using WOpenUsage.Core.Usage;
using WOpenUsage.Providers.Fakes;

namespace WOpenUsage.Cli.Tests;

public sealed class UsageCliProcessTests
{
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

            string executablePath = Path.Combine(AppContext.BaseDirectory, "WOpenUsage.Cli.exe");
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
                "wusage.usage.v1",
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
}

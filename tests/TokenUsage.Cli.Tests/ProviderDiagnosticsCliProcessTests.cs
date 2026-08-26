using System.Diagnostics;
using System.Text.Json;

namespace TokenUsage.Cli.Tests;

public sealed class ProviderDiagnosticsCliProcessTests
{
    [Theory]
    [InlineData("providers", "tokenusage.providers.v1", 11)]
    [InlineData("doctor", "tokenusage.doctor.v1", 13)]
    public async Task RealCommandsDetectWithoutStartingCodexOrCreatingAppData(
        string command,
        string schemaVersion,
        int expectedRows)
    {
        string dataRoot = Path.Combine(
            Path.GetTempPath(),
            "tokenusage-provider-process-tests",
            Guid.NewGuid().ToString("N"));
        string markerPath = Path.Combine(
            Path.GetTempPath(),
            $"tokenusage-codex-marker-{Guid.NewGuid():N}.txt");
        string privatePath = Path.Combine(dataRoot, "private-account@example.test-Bearer-secret");
        try
        {
            ProcessResult result = await RunAsync(command, dataRoot, markerPath, privatePath);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(string.Empty, result.StandardError);
            Assert.False(File.Exists(markerPath));
            Assert.False(Directory.Exists(dataRoot));
            Assert.DoesNotContain(privatePath, result.StandardOutput, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Bearer", result.StandardOutput, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("@example.test", result.StandardOutput, StringComparison.OrdinalIgnoreCase);
            using JsonDocument document = JsonDocument.Parse(result.StandardOutput);
            Assert.Equal(
                schemaVersion,
                document.RootElement.GetProperty("schemaVersion").GetString());
            string rowName = command == "providers" ? "providers" : "checks";
            Assert.Equal(expectedRows, document.RootElement.GetProperty(rowName).GetArrayLength());
        }
        finally
        {
            if (Directory.Exists(dataRoot))
            {
                Directory.Delete(dataRoot, recursive: true);
            }

            if (File.Exists(markerPath))
            {
                File.Delete(markerPath);
            }
        }
    }

    private static async Task<ProcessResult> RunAsync(
        string command,
        string dataRoot,
        string markerPath,
        string missingProviderRoot)
    {
        string executablePath = Path.Combine(AppContext.BaseDirectory, "tokenusage.exe");
        string fakeCodexPath = Path.Combine(AppContext.BaseDirectory, "FakeCodex", "codex.exe");
        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add(command);
        startInfo.ArgumentList.Add("--format");
        startInfo.ArgumentList.Add("json");
        startInfo.Environment["TOKENUSAGE_DATA_DIR"] = dataRoot;
        startInfo.Environment["TOKENUSAGE_CODEX_EXECUTABLE"] = fakeCodexPath;
        startInfo.Environment["TOKENUSAGE_FAKE_PATH_MARKER"] = markerPath;
        startInfo.Environment["CLAUDE_CONFIG_DIR"] = missingProviderRoot;
        startInfo.Environment["GROK_HOME"] = missingProviderRoot;
        startInfo.Environment["OPENCODE_DATA_DIR"] = missingProviderRoot;

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

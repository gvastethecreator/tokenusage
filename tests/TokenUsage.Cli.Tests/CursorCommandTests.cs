using System.Globalization;
using TokenUsage.Cli;
using TokenUsage.Providers.Cursor;
using TokenUsage.Runtime.Windows.Cursor;

namespace TokenUsage.Cli.Tests;

public sealed class CursorCommandTests
{
    [Fact]
    public async Task InstallIsANoOpAndStatusReportsTheLocalProfile()
    {
        string home = Path.Combine(
            Path.GetTempPath(),
            "tokenusage-cursor-command-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(home);
        Directory.CreateDirectory(Path.Combine(home, ".cursor"));
        try
        {
            var installer = new CursorHookInstaller(home);
            var source = new CursorUsageEventSource(
                "UTC",
                home,
                Path.Combine(home, "roaming"));
            var output = new StringWriter(CultureInfo.InvariantCulture);

            int installExitCode = await CursorCommand.RunAsync(
                ["install-hook"],
                output,
                TextWriter.Null,
                installer,
                source);
            output.GetStringBuilder().Clear();
            int statusExitCode = await CursorCommand.RunAsync(
                ["status"],
                output,
                TextWriter.Null,
                installer,
                source);

            Assert.Equal(0, installExitCode);
            Assert.Equal(0, statusExitCode);
            Assert.False(File.Exists(installer.ScriptPath));
            Assert.Equal(
                "Cursor local usage: no estimated context records found" + Environment.NewLine,
                output.ToString());
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }

    [Fact]
    public async Task InvalidCursorActionDoesNotEchoIt()
    {
        var error = new StringWriter(CultureInfo.InvariantCulture);

        int exitCode = await CursorCommand.RunAsync(
            ["private-value"],
            TextWriter.Null,
            error);

        Assert.Equal(2, exitCode);
        Assert.Equal(CursorCommand.UsageText + Environment.NewLine, error.ToString());
        Assert.DoesNotContain("private-value", error.ToString(), StringComparison.Ordinal);
    }
}

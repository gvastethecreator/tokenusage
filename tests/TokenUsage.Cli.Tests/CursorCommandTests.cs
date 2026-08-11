using System.Globalization;
using TokenUsage.Cli;
using TokenUsage.Runtime.Windows.Cursor;

namespace TokenUsage.Cli.Tests;

public sealed class CursorCommandTests
{
    [Fact]
    public async Task InstallAndStatusUseTheExplicitUserHookLocation()
    {
        string home = Path.Combine(
            Path.GetTempPath(),
            "tokenusage-cursor-command-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(home);
        try
        {
            var installer = new CursorHookInstaller(home);
            var output = new StringWriter(CultureInfo.InvariantCulture);

            int installExitCode = await CursorCommand.RunAsync(
                ["install-hook"],
                output,
                TextWriter.Null,
                installer);
            output.GetStringBuilder().Clear();
            int statusExitCode = await CursorCommand.RunAsync(
                ["status"],
                output,
                TextWriter.Null,
                installer);

            Assert.Equal(0, installExitCode);
            Assert.Equal(0, statusExitCode);
            Assert.Equal(
                "Cursor usage hook: installed" + Environment.NewLine,
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

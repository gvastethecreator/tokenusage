using System.Globalization;
using TokenUsage.Cli;

namespace TokenUsage.Cli.Tests;

public sealed class CliApplicationTests
{
    [Fact]
    public async Task UnknownCommandIsRedacted()
    {
        string dataRoot = CreateDataRoot();
        try
        {
            var error = new StringWriter(CultureInfo.InvariantCulture);

            int exitCode = await CliApplication.RunAsync(
                ["customer-secret"],
                TextWriter.Null,
                error,
                dataRoot,
                TimeProvider.System);

            Assert.Equal(2, exitCode);
            Assert.Equal(
                "Unknown command." + Environment.NewLine
                + CliApplication.UsageText + Environment.NewLine,
                error.ToString());
            Assert.DoesNotContain("customer-secret", error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dataRoot, recursive: true);
        }
    }

    [Fact]
    public async Task MissingCommandReturnsRootUsage()
    {
        string dataRoot = CreateDataRoot();
        try
        {
            var error = new StringWriter(CultureInfo.InvariantCulture);

            int exitCode = await CliApplication.RunAsync(
                [],
                TextWriter.Null,
                error,
                dataRoot,
                TimeProvider.System);

            Assert.Equal(2, exitCode);
            Assert.Equal(CliApplication.UsageText + Environment.NewLine, error.ToString());
        }
        finally
        {
            Directory.Delete(dataRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData("help")]
    [InlineData("--help")]
    [InlineData("-h")]
    public async Task HelpReturnsRootUsageOnStandardOutput(string argument)
    {
        string dataRoot = CreateDataRoot();
        try
        {
            var output = new StringWriter(CultureInfo.InvariantCulture);
            var error = new StringWriter(CultureInfo.InvariantCulture);

            int exitCode = await CliApplication.RunAsync(
                [argument],
                output,
                error,
                dataRoot,
                TimeProvider.System);

            Assert.Equal(0, exitCode);
            Assert.Equal(CliApplication.UsageText + Environment.NewLine, output.ToString());
            Assert.Empty(error.ToString());
        }
        finally
        {
            Directory.Delete(dataRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ReportCommandIsDispatchedWithoutOpeningStorageForInvalidOptions()
    {
        string dataRoot = CreateDataRoot();
        try
        {
            var error = new StringWriter(CultureInfo.InvariantCulture);

            int exitCode = await CliApplication.RunAsync(
                ["report", "--format", "csv"],
                TextWriter.Null,
                error,
                dataRoot,
                TimeProvider.System);

            Assert.Equal(2, exitCode);
            Assert.EndsWith(ReportCommand.UsageText + Environment.NewLine, error.ToString());
            Assert.DoesNotContain("Unknown command.", error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dataRoot, recursive: true);
        }
    }

    [Fact]
    public async Task RefreshCommandIsDispatchedWithoutOpeningStorageForInvalidOptions()
    {
        string dataRoot = CreateDataRoot();
        try
        {
            var error = new StringWriter(CultureInfo.InvariantCulture);

            int exitCode = await CliApplication.RunAsync(
                ["refresh", "--format", "csv"],
                TextWriter.Null,
                error,
                dataRoot,
                TimeProvider.System);

            Assert.Equal(2, exitCode);
            Assert.EndsWith(RefreshCommand.UsageText + Environment.NewLine, error.ToString());
            Assert.DoesNotContain("Unknown command.", error.ToString(), StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.Combine(dataRoot, "scanner")));
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
            "tokenusage-cli-application-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}

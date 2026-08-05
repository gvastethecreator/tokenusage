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

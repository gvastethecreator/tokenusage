using System.Globalization;
using TokenUsage.Cli;

namespace TokenUsage.Cli.Tests;

public sealed class HookCommandTests
{
    [Theory]
    [InlineData("")]
    [InlineData("start")]
    [InlineData("stop|now")]
    public async Task InvalidArgumentsReturnTwoWithUsage(string argumentLine)
    {
        string[] arguments = argumentLine.Length == 0
            ? []
            : argumentLine.Split('|');
        var error = new StringWriter(CultureInfo.InvariantCulture);

        int exitCode = await HookCommand.RunAsync(
            arguments,
            new StringWriter(CultureInfo.InvariantCulture),
            error);

        Assert.Equal(2, exitCode);
        Assert.Contains(HookCommand.UsageText, error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task StopDrainsInputRunsRefreshAndStaysSilent()
    {
        var payload = new StringReader(
            """{"prompt":"private content","transcript_path":"C:\\temp\\x.jsonl"}""");
        bool refreshed = false;
        var output = new StringWriter(CultureInfo.InvariantCulture);
        var error = new StringWriter(CultureInfo.InvariantCulture);

        int exitCode = await HookCommand.RunAsync(
            ["stop"],
            output,
            error,
            runRefresh: () =>
            {
                refreshed = true;
                return Task.FromResult(0);
            },
            standardInput: payload);

        Assert.Equal(0, exitCode);
        Assert.True(refreshed);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task StopWithoutRedirectedInputRunsRefreshAndStaysSilent()
    {
        bool refreshed = false;
        var output = new StringWriter(CultureInfo.InvariantCulture);
        var error = new StringWriter(CultureInfo.InvariantCulture);

        int exitCode = await HookCommand.RunAsync(
            ["stop"],
            output,
            error,
            runRefresh: () =>
            {
                refreshed = true;
                return Task.FromResult(0);
            },
            standardInput: null);

        Assert.Equal(0, exitCode);
        Assert.True(refreshed);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task StopSwallowsRefreshFailuresWithoutLeakingPaths()
    {
        var output = new StringWriter(CultureInfo.InvariantCulture);
        var error = new StringWriter(CultureInfo.InvariantCulture);

        int exitCode = await HookCommand.RunAsync(
            ["stop"],
            output,
            error,
            runRefresh: () => throw new IOException("C:\\Users\\private\\secret"),
            standardInput: new StringReader("{}"));

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }
}

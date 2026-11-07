using System.Globalization;
using TokenUsage.Cli;
using TokenUsage.Core.Usage;

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
        var payload = new MemoryStream(
            """{"prompt":"private content","transcript_path":"C:\\temp\\x.jsonl"}"""u8.ToArray());
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
            standardInput: new MemoryStream("{}"u8.ToArray()));

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task OverlappingHookRefreshesRunOnceMoreInsteadOfStacking()
    {
        string folder = Path.Combine(Path.GetTempPath(), "tokenusage-gate-" + Guid.NewGuid().ToString("N"));
        string database = Path.Combine(folder, "usage.db");
        var firstPass = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        int passes = 0;
        Task<int> first = LocalUsageRefreshGate.RunAsync(database, async token =>
        {
            if (Interlocked.Increment(ref passes) == 1)
            {
                firstPass.SetResult();
                await release.Task.WaitAsync(token);
            }
            return passes;
        });
        await firstPass.Task.WaitAsync(TimeSpan.FromSeconds(5));

        try
        {
            bool[] waiting = await Task.WhenAll(
                LocalUsageRefreshGate.TryRunAsync(database, _ => Task.FromResult(0)),
                LocalUsageRefreshGate.TryRunAsync(database, _ => Task.FromResult(0)));
            using var cancellation = new CancellationTokenSource();
            Task<int> canceledWaiter = LocalUsageRefreshGate.RunAsync(database,
                _ => Task.FromResult(0), cancellation.Token);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledWaiter);
            release.SetResult();

            Assert.Equal([false, false], waiting);
            Assert.Equal(2, await first.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(2, passes);
            Assert.False(File.Exists(database + ".refresh-pending"));
            Assert.Equal(3, await LocalUsageRefreshGate.RunAsync(database, _ => Task.FromResult(++passes)));

            await Assert.ThrowsAsync<IOException>(() => LocalUsageRefreshGate.TryRunAsync(database,
                _ => Task.FromException<int>(new IOException("Collection failed."))));
            Assert.True(File.Exists(database + ".refresh-pending"));
            Assert.Equal(4, await LocalUsageRefreshGate.RunAsync(database, _ => Task.FromResult(++passes)));
            Assert.False(File.Exists(database + ".refresh-pending"));
        }
        finally
        {
            release.TrySetResult();
            await first.WaitAsync(TimeSpan.FromSeconds(5));
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }
}

using System.Globalization;
using System.Text.Json;
using WOpenUsage.Cli;

namespace WOpenUsage.Cli.Tests;

public sealed class UsageCommandTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 22, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task JsonMatchesVersionedGoldenContract()
    {
        var output = new StringWriter(CultureInfo.InvariantCulture);
        var error = new StringWriter(CultureInfo.InvariantCulture);

        int exitCode = await UsageCommand.RunAsync(
            ["--format", "json"],
            output,
            error,
            (_, _, _) => Task.FromResult(new UsageCliSummary(
                3,
                53_080,
                1.84m,
                0.62m,
                9_460)),
            new FixedTimeProvider(Now));

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        string goldenPath = Path.Combine(
            AppContext.BaseDirectory,
            "Golden",
            "wusage.usage.v1.json");
        Assert.Equal(
            NormalizeNewlines(await File.ReadAllTextAsync(goldenPath)),
            NormalizeNewlines(output.ToString()));
    }

    [Fact]
    public async Task HumanOutputUsesInvariantFormattingInSpanishCulture()
    {
        using var culture = new CultureScope("es-ES");
        var output = new StringWriter(CultureInfo.CurrentCulture);

        int exitCode = await UsageCommand.RunAsync(
            ["--days", "7"],
            output,
            TextWriter.Null,
            (from, to, _) =>
            {
                Assert.Equal(new DateOnly(2026, 7, 16), from);
                Assert.Equal(new DateOnly(2026, 7, 22), to);
                return Task.FromResult(new UsageCliSummary(3, 53_080, 1.84m, null, 9_460));
            },
            new FixedTimeProvider(Now));

        Assert.Equal(0, exitCode);
        Assert.Equal(
            "Range: 2026-07-16 to 2026-07-22\n"
            + "Events: 3\n"
            + "Total tokens: 53,080\n"
            + "Unpriced tokens: 9,460\n"
            + "Reported USD: 1.84\n"
            + "Estimated USD: unavailable\n",
            NormalizeNewlines(output.ToString()));
    }

    [Fact]
    public async Task EmptySummaryWritesValidJsonAndReturnsNoData()
    {
        var output = new StringWriter(CultureInfo.InvariantCulture);

        int exitCode = await UsageCommand.RunAsync(
            ["--format", "json"],
            output,
            TextWriter.Null,
            (_, _, _) => Task.FromResult(new UsageCliSummary(0, 0, null, null, 0)),
            new FixedTimeProvider(Now));

        Assert.Equal(4, exitCode);
        using JsonDocument document = JsonDocument.Parse(output.ToString());
        Assert.Equal("wusage.usage.v1", document.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal(0, document.RootElement.GetProperty("events").GetInt32());
        Assert.Equal(JsonValueKind.Null, document.RootElement
            .GetProperty("costUsd")
            .GetProperty("reported")
            .ValueKind);
    }

    [Theory]
    [InlineData("--days")]
    [InlineData("--days|0")]
    [InlineData("--days|3651")]
    [InlineData("--days|+1")]
    [InlineData("--days|30|--days|7")]
    [InlineData("--format")]
    [InlineData("--format|xml")]
    [InlineData("--format|json|--format|human")]
    [InlineData("--unknown")]
    [InlineData("30")]
    public async Task InvalidArgumentsReturnUsageError(string argumentLine)
    {
        string[] arguments = argumentLine.Split('|');
        var output = new StringWriter(CultureInfo.InvariantCulture);
        var error = new StringWriter(CultureInfo.InvariantCulture);
        bool readerCalled = false;

        int exitCode = await UsageCommand.RunAsync(
            arguments,
            output,
            error,
            (_, _, _) =>
            {
                readerCalled = true;
                return Task.FromResult(new UsageCliSummary(1, 1, null, null, 1));
            },
            new FixedTimeProvider(Now));

        Assert.Equal(2, exitCode);
        Assert.False(readerCalled);
        Assert.Equal(string.Empty, output.ToString());
        Assert.EndsWith(UsageCommand.UsageText + Environment.NewLine, error.ToString());
    }

    [Fact]
    public async Task ReaderFailureIsRedacted()
    {
        var output = new StringWriter(CultureInfo.InvariantCulture);
        var error = new StringWriter(CultureInfo.InvariantCulture);

        int exitCode = await UsageCommand.RunAsync(
            [],
            output,
            error,
            (_, _, _) => throw new IOException("C:\\Users\\secret\\usage.v1.db"),
            new FixedTimeProvider(Now));

        Assert.Equal(4, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Equal("Unable to read local usage data." + Environment.NewLine, error.ToString());
        Assert.DoesNotContain("secret", error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnknownArgumentValueIsNotEchoed()
    {
        var error = new StringWriter(CultureInfo.InvariantCulture);

        int exitCode = await UsageCommand.RunAsync(
            ["--token=customer-secret"],
            TextWriter.Null,
            error,
            (_, _, _) => Task.FromResult(new UsageCliSummary(1, 1, null, null, 1)),
            new FixedTimeProvider(Now));

        Assert.Equal(2, exitCode);
        Assert.DoesNotContain("customer-secret", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CallerCancellationPropagates()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => UsageCommand.RunAsync(
            [],
            TextWriter.Null,
            TextWriter.Null,
            (_, _, token) => Task.FromCanceled<UsageCliSummary>(token),
            new FixedTimeProvider(Now),
            cancellation.Token));
    }

    private static string NormalizeNewlines(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _originalCulture = CultureInfo.CurrentCulture;
        private readonly CultureInfo _originalUiCulture = CultureInfo.CurrentUICulture;

        public CultureScope(string cultureName)
        {
            CultureInfo culture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _originalCulture;
            CultureInfo.CurrentUICulture = _originalUiCulture;
        }
    }
}

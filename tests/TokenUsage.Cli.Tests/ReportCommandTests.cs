using System.Globalization;
using System.Text.Json;
using TokenUsage.Cli;
using TokenUsage.Core.Automation;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;

namespace TokenUsage.Cli.Tests;

public sealed class ReportCommandTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 22, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task JsonMatchesVersionedGoldenContract()
    {
        var output = new StringWriter(CultureInfo.InvariantCulture);
        var error = new StringWriter(CultureInfo.InvariantCulture);

        int exitCode = await ReportCommand.RunAsync(
            ["--format", "json"],
            output,
            error,
            (_, _, _, _) => Task.FromResult(CreateReport()),
            new FixedTimeProvider(Now));

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        string goldenPath = Path.Combine(
            AppContext.BaseDirectory,
            "Golden",
            "tokenusage.report.v1.json");
        Assert.Equal(
            NormalizeNewlines(await File.ReadAllTextAsync(goldenPath)),
            NormalizeNewlines(output.ToString()));
    }

    [Fact]
    public async Task ExactRangeAndAgentFilterReachReaderAndHumanReport()
    {
        using var culture = new CultureScope("es-ES");
        var output = new StringWriter(CultureInfo.CurrentCulture);

        int exitCode = await ReportCommand.RunAsync(
            [
                "--from", "2026-07-20",
                "--to", "2026-07-22",
                "--agent", "codex",
            ],
            output,
            TextWriter.Null,
            (from, to, agentId, _) =>
            {
                Assert.Equal(new DateOnly(2026, 7, 20), from);
                Assert.Equal(new DateOnly(2026, 7, 22), to);
                Assert.Equal("codex", agentId?.Value);
                return Task.FromResult(CreateReport());
            },
            new FixedTimeProvider(Now));

        Assert.Equal(0, exitCode);
        string text = NormalizeNewlines(output.ToString());
        Assert.Contains("TokenUsage report\n", text, StringComparison.Ordinal);
        Assert.Contains("Agent: codex\n", text, StringComparison.Ordinal);
        Assert.Contains("Totals\n", text, StringComparison.Ordinal);
        Assert.Contains("Tokens\n", text, StringComparison.Ordinal);
        Assert.Contains("By agent\n", text, StringComparison.Ordinal);
        Assert.Contains("Top models (up to 10)\n", text, StringComparison.Ordinal);
        Assert.Contains("Highest-cost days\n", text, StringComparison.Ordinal);
        Assert.Contains("Daily\n", text, StringComparison.Ordinal);
        Assert.Contains("Price coverage: 89.7%\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("89,7", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--from|2026-07-20")]
    [InlineData("--to|2026-07-22")]
    [InlineData("--from|2026-07-22|--to|2026-07-20")]
    [InlineData("--days|30|--from|2026-07-20|--to|2026-07-22")]
    [InlineData("--agent|Customer Secret")]
    [InlineData("--format|csv")]
    public async Task InvalidArgumentsDoNotReadLocalData(string argumentLine)
    {
        string[] arguments = argumentLine.Split('|');
        var error = new StringWriter(CultureInfo.InvariantCulture);
        bool readerCalled = false;

        int exitCode = await ReportCommand.RunAsync(
            arguments,
            TextWriter.Null,
            error,
            (_, _, _, _) =>
            {
                readerCalled = true;
                return Task.FromResult(CreateReport());
            },
            new FixedTimeProvider(Now));

        Assert.Equal(2, exitCode);
        Assert.False(readerCalled);
        Assert.EndsWith(ReportCommand.UsageText + Environment.NewLine, error.ToString());
        Assert.DoesNotContain("Customer Secret", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmptyReportWritesValidJsonAndReturnsNoData()
    {
        var output = new StringWriter(CultureInfo.InvariantCulture);

        int exitCode = await ReportCommand.RunAsync(
            ["--format", "json"],
            output,
            TextWriter.Null,
            (_, _, _, _) => Task.FromResult(UsageReportQuery.Build([])),
            new FixedTimeProvider(Now));

        Assert.Equal(4, exitCode);
        using JsonDocument document = JsonDocument.Parse(output.ToString());
        Assert.Equal(
            ReportCommand.SchemaVersion,
            document.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal(0, document.RootElement
            .GetProperty("totals")
            .GetProperty("events")
            .GetInt32());
    }

    private static UsageReport CreateReport() => UsageReportQuery.Build(
    [
        new DailyUsageRollup(
            new DateOnly(2026, 7, 21),
            "UTC",
            new AgentId("codex"),
            new ModelProviderId("openai"),
            new ModelId("gpt-5"),
            new TokenBreakdown(1_000, 200, 50, 300, 0),
            1.25m,
            null,
            0,
            0,
            2,
            CoverageKind.Complete),
        new DailyUsageRollup(
            new DateOnly(2026, 7, 22),
            "UTC",
            new AgentId("opencode"),
            new ModelProviderId("anthropic"),
            new ModelId("claude-sonnet"),
            new TokenBreakdown(600, 300, 0, 100, 50),
            null,
            0.75m,
            0,
            0,
            1,
            CoverageKind.Partial),
        new DailyUsageRollup(
            new DateOnly(2026, 7, 20),
            "UTC",
            new AgentId("codex"),
            new ModelProviderId("openai"),
            new ModelId("unknown-model"),
            new TokenBreakdown(200, 100, 0, 0, 0),
            null,
            null,
            300,
            1,
            1,
            CoverageKind.Unpriced),
    ]);

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

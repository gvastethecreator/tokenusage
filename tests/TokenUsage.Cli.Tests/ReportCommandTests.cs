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
    [InlineData("--format|xml")]
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

    [Fact]
    public async Task JsonV2KeepsInvariantStringsAndOmitsAttribution()
    {
        var output = new StringWriter(CultureInfo.InvariantCulture);

        int exitCode = await ReportCommand.RunAsync(
            ["--format", "json-v2"],
            output,
            TextWriter.Null,
            (_, _, _, _) => Task.FromResult(CreateHugeReport()),
            new FixedTimeProvider(Now));

        Assert.Equal(0, exitCode);
        using JsonDocument document = JsonDocument.Parse(output.ToString());
        Assert.Equal(
            ReportJsonV2.SchemaVersion,
            document.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal(
            "9007199254740993",
            document.RootElement.GetProperty("totals").GetProperty("tokens").GetProperty("total").GetString());
        Assert.Equal(
            "1.25",
            document.RootElement.GetProperty("totals").GetProperty("reportedCost").GetProperty("amount").GetString());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("requestCount").ValueKind);
        Assert.Equal(
            "finality-not-established",
            document.RootElement.GetProperty("requestCountReason").GetString());
        string json = output.ToString();
        Assert.DoesNotContain("sessionKey", json, StringComparison.Ordinal);
        Assert.DoesNotContain("projectKey", json, StringComparison.Ordinal);
        Assert.DoesNotContain("alias", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SelectionFileDrivesJsonV2RangeAndRejectsLegacyCombinations()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(
            path,
            """{"schema":"tokenusage.selection.v1","from":"2026-07-20","to":"2026-07-22"}""");
        try
        {
            DateOnly? seenFrom = null;
            DateOnly? seenTo = null;
            int exitCode = await ReportCommand.RunAsync(
                ["--format", "json-v2", "--selection-file", path],
                TextWriter.Null,
                TextWriter.Null,
                (from, to, _, _) =>
                {
                    seenFrom = from;
                    seenTo = to;
                    return Task.FromResult(CreateReport());
                },
                new FixedTimeProvider(Now));
            Assert.Equal(0, exitCode);
            Assert.Equal(new DateOnly(2026, 7, 20), seenFrom);
            Assert.Equal(new DateOnly(2026, 7, 22), seenTo);

            var error = new StringWriter(CultureInfo.InvariantCulture);
            int rejected = await ReportCommand.RunAsync(
                ["--format", "json-v2", "--selection-file", path, "--days", "7"],
                TextWriter.Null,
                error,
                (_, _, _, _) => Task.FromResult(CreateReport()),
                new FixedTimeProvider(Now));
            Assert.Equal(2, rejected);
            Assert.Contains("cannot be combined", error.ToString(), StringComparison.Ordinal);

            var humanError = new StringWriter(CultureInfo.InvariantCulture);
            int humanRejected = await ReportCommand.RunAsync(
                ["--selection-file", path],
                TextWriter.Null,
                humanError,
                (_, _, _, _) => Task.FromResult(CreateReport()),
                new FixedTimeProvider(Now));
            Assert.Equal(2, humanRejected);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task SelectionFileRejectsUnknownAndDuplicateProperties()
    {
        string unknown = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        string duplicate = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(
            unknown,
            """{"schema":"tokenusage.selection.v1","from":"2026-07-20","to":"2026-07-22","extra":true}""");
        await File.WriteAllTextAsync(
            duplicate,
            """{"schema":"tokenusage.selection.v1","from":"2026-07-20","from":"2026-07-21","to":"2026-07-22"}""");
        try
        {
            var unknownError = new StringWriter(CultureInfo.InvariantCulture);
            Assert.Equal(
                2,
                await ReportCommand.RunAsync(
                    ["--format", "json-v2", "--selection-file", unknown],
                    TextWriter.Null,
                    unknownError,
                    (_, _, _, _) => Task.FromResult(CreateReport()),
                    new FixedTimeProvider(Now)));
            Assert.Contains("unsupported property", unknownError.ToString(), StringComparison.Ordinal);

            var duplicateError = new StringWriter(CultureInfo.InvariantCulture);
            Assert.Equal(
                2,
                await ReportCommand.RunAsync(
                    ["--format", "json-v2", "--selection-file", duplicate],
                    TextWriter.Null,
                    duplicateError,
                    (_, _, _, _) => Task.FromResult(CreateReport()),
                    new FixedTimeProvider(Now)));
            Assert.Contains("cannot repeat", duplicateError.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(unknown);
            File.Delete(duplicate);
        }
    }

    [Fact]
    public async Task ExactSelectionFileReachesExactReaderAndKeepsCivilBounds()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(
            path,
            """
            {"schema":"tokenusage.selection.v1","kind":"exact","from":"2026-09-06","to":"2026-09-06","fromUtc":"2026-09-06T00:00:00Z","toExclusiveUtc":"2026-09-07T00:00:00Z"}
            """);
        try
        {
            DateTimeOffset? seenFrom = null;
            DateTimeOffset? seenTo = null;
            bool? includeConfigurations = null;
            var output = new StringWriter(CultureInfo.InvariantCulture);
            int exitCode = await ReportCommand.RunAsync(
                ["--format", "json-v2", "--selection-file", path],
                output,
                TextWriter.Null,
                (_, _, _, _) => Task.FromResult(CreateReport()),
                new FixedTimeProvider(Now),
                readExact: (fromUtc, toUtc, _, include, _) =>
                {
                    seenFrom = fromUtc;
                    seenTo = toUtc;
                    includeConfigurations = include;
                    return Task.FromResult(CreateReport());
                });
            Assert.Equal(0, exitCode);
            Assert.Equal(new DateTimeOffset(2026, 9, 6, 0, 0, 0, TimeSpan.Zero), seenFrom);
            Assert.Equal(new DateTimeOffset(2026, 9, 7, 0, 0, 0, TimeSpan.Zero), seenTo);
            Assert.False(includeConfigurations);
            Assert.Contains("\"kind\": \"exact\"", output.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CsvAndHtmlEscapeInjectionAndMarkup()
    {
        var metrics = new UsageReportSnapshotV2.Metrics(
            "1",
            new UsageReportSnapshotV2.Tokens("1", "0", "0", "0", "0", "1", "0"),
            new UsageReportSnapshotV2.MoneyAmount("USD", "1.25"),
            null,
            "0",
            "complete",
            "100.0");
        var snapshot = new UsageReportSnapshotV2.Document(
            UsageReportSnapshotV2.SchemaVersion,
            "2026-07-22T15:00:00Z",
            new UsageReportSnapshotV2.Selection("calendar", "2026-07-20", "2026-07-22", "3", null),
            null,
            metrics,
            [],
            [new UsageReportSnapshotV2.ModelRow("=cmd", null, "<script>", metrics)],
            [],
            [],
            [],
            null,
            "finality-not-established");

        string csv = UsageReportSnapshotV2.WriteCsv(snapshot);
        Assert.Contains("'=cmd", csv, StringComparison.Ordinal);
        Assert.DoesNotContain(",=cmd,", csv, StringComparison.Ordinal);

        string html = UsageReportSnapshotV2.WriteHtml(snapshot);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
    }

    private static UsageReport CreateHugeReport() => UsageReportQuery.Build(
    [
        new DailyUsageRollup(
            new DateOnly(2026, 7, 22),
            "UTC",
            new AgentId("codex"),
            new ModelProviderId("openai"),
            new ModelId("gpt-5"),
            new TokenBreakdown(9_007_199_254_740_993, 0, 0, 0, 0),
            1.25m,
            null,
            0,
            0,
            1,
            CoverageKind.Complete),
    ]);

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

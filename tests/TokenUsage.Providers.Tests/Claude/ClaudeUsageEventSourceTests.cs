using System.Text.Json;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;
using TokenUsage.Providers.Claude;

namespace TokenUsage.Providers.Tests.Claude;

public sealed class ClaudeUsageEventSourceTests
{
    [Fact]
    public void RootDetectionDoesNotReadSessionFiles()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string projects = Directory.CreateDirectory(Path.Combine(root, "projects")).FullName;
        string trap = Path.Combine(projects, "private-session.jsonl");
        File.WriteAllText(trap, "Bearer private-account@example.test");
        try
        {
            using (var locked = new FileStream(
                       trap, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var source = new ClaudeUsageEventSource("UTC", configDirectoryOverride: root);
                Assert.True(source.IsRootAvailable);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task MissingProjectsDirectoryIsReportedAsNoData()
    {
        string home = Path.Combine(
            Path.GetTempPath(),
            "tokenusage-claude-missing",
            Guid.NewGuid().ToString("N"));
        var source = new ClaudeUsageEventSource("UTC", homeDirectory: home);

        UsageSourceReadResult result = await source.ReadAsync();

        Assert.False(source.IsRootAvailable);
        Assert.Empty(result.Events);
        Assert.Equal(UsageSourceReadStatus.NoData, result.Status);
        Assert.Equal(UsageSourceIssueKind.RootUnavailable, result.Issue);
    }

    [Fact]
    public async Task ReadsAllowedUsageWithoutRetainingSessionContent()
    {
        using var corpus = new ClaudeCorpus();
        corpus.WriteLines(
            UsageLine(
                "message-1",
                "request-1",
                input: 100,
                output: 20,
                cacheRead: 30,
                cacheWrite: 40,
                costUsd: 0.1234567m,
                content: "SECRET PROMPT AND RESPONSE"),
            "{\"type\":\"assistant\",\"message\":");

        var source = corpus.CreateSource();
        UsageSourceReadResult result = await source.ReadAsync();
        UsageEvent usageEvent = Assert.Single(result.Events);

        Assert.True(source.IsRootAvailable);
        Assert.Equal(SourceKind.LocalLog, source.SourceKind);
        Assert.Equal(UsageSourceReadStatus.Complete, result.Status);
        Assert.Equal("claude", usageEvent.AgentId.Value);
        Assert.Equal("anthropic", usageEvent.ModelProviderId?.Value);
        Assert.Equal(new TokenBreakdown(100, 20, 0, 30, 40), usageEvent.Tokens);
        Assert.Equal(CostKind.ProviderReported, usageEvent.Cost.Kind);
        Assert.Equal(0.123457m, usageEvent.Cost.ReportedCostUsd);
        Assert.Equal(64, usageEvent.EventKey.Value.Length);
        Assert.DoesNotContain("message", usageEvent.EventKey.Value, StringComparison.Ordinal);
        Assert.DoesNotContain(
            typeof(UsageEvent).GetProperties().Select(property => property.Name),
            name => name.Contains("content", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DeduplicatesExactMessagesAndSidechainReplays()
    {
        using var corpus = new ClaudeCorpus();
        corpus.WriteLines(
            UsageLine("message-1", "request-1", 10, 5, isSidechain: true),
            UsageLine("message-1", "request-1", 20, 5, isSidechain: false),
            UsageLine("message-1", "request-2", 18, 5, isSidechain: true),
            UsageLine("message-2", "request-3", 7, 2));

        IReadOnlyList<UsageEvent> events = (await corpus.CreateSource().ReadAsync()).Events;

        Assert.Equal(2, events.Count);
        Assert.Contains(events, usageEvent => usageEvent.Tokens.Input == 20);
        Assert.Contains(events, usageEvent => usageEvent.Tokens.Input == 7);
    }

    [Fact]
    public async Task KeepsTheLastStreamingUsageForOneMessageAndItsFirstTimestamp()
    {
        using var corpus = new ClaudeCorpus();
        corpus.WriteLines(
            UsageLine(
                "message-stream",
                "request-1",
                10,
                2,
                timestamp: "2026-07-22T11:00:00.000Z"),
            UsageLine(
                "message-stream",
                "request-2",
                80,
                12,
                timestamp: "2026-07-22T12:00:00.000Z"));

        UsageEvent usageEvent = Assert.Single(
            (await corpus.CreateSource().ReadAsync()).Events);

        Assert.Equal(80, usageEvent.Tokens.Input);
        Assert.Equal(12, usageEvent.Tokens.Output);
        Assert.Equal(
            new DateTimeOffset(2026, 7, 22, 11, 0, 0, TimeSpan.Zero),
            usageEvent.OccurredAtUtc);
        Assert.Equal(ClaudeUsageEventSource.ParserVersion, usageEvent.ParserVersion);
    }

    [Fact]
    public async Task IncludesAdvisorIterationsAsSeparateModelSpend()
    {
        using var corpus = new ClaudeCorpus();
        string line = UsageLine("message-advisor", "request-1", 20, 4).Replace(
            "\"cache_creation_input_tokens\":0",
            "\"cache_creation_input_tokens\":0,\"iterations\":[{\"type\":\"advisor_message\",\"model\":\"claude-opus-4-6\",\"input_tokens\":100,\"output_tokens\":10,\"cache_read_input_tokens\":5}]",
            StringComparison.Ordinal);
        corpus.WriteLines(line);

        IReadOnlyList<UsageEvent> events = (await corpus.CreateSource().ReadAsync()).Events;

        Assert.Equal(2, events.Count);
        UsageEvent advisor = Assert.Single(
            events,
            usageEvent => usageEvent.ModelId.Value == "claude-opus-4-6");
        Assert.Equal(new TokenBreakdown(100, 10, 0, 5, 0), advisor.Tokens);
        Assert.Equal(CostKind.CatalogEstimated, advisor.Cost.Kind);
    }

    [Fact]
    public async Task EstimatesExactModelsAndLeavesUnknownModelsUnpriced()
    {
        using var corpus = new ClaudeCorpus();
        corpus.WriteLines(
            UsageLine(
                "message-priced",
                "request-priced",
                1_000_000,
                100_000,
                cacheRead: 200_000,
                cacheWrite: 0,
                cacheWrite5Minutes: 300_000,
                cacheWrite1Hour: 400_000),
            UsageLine(
                "message-unknown",
                "request-unknown",
                100,
                20,
                model: "claude-future-9"));

        IReadOnlyList<UsageEvent> events = (await corpus.CreateSource().ReadAsync()).Events;
        UsageEvent priced = Assert.Single(
            events,
            usageEvent => usageEvent.ModelId.Value == "claude-sonnet-4-6");
        UsageEvent unknown = Assert.Single(
            events,
            usageEvent => usageEvent.ModelId.Value == "claude-future-9");

        Assert.Equal(8.085m, priced.Cost.EstimatedCostUsd);
        Assert.Equal(ClaudePricingCatalog.Version, priced.Cost.CatalogVersion);
        Assert.Equal(700_000, priced.Tokens.CacheWrite);
        Assert.Equal(CostKind.Unavailable, unknown.Cost.Kind);
        Assert.Equal(CoverageKind.Unpriced, unknown.Coverage);
    }

    [Fact]
    public async Task NeverEnumeratesCredentialsAndHonorsFileLimit()
    {
        using var corpus = new ClaudeCorpus();
        corpus.WriteLines(UsageLine("message-1", "request-1", 10, 2));
        File.WriteAllText(
            Path.Combine(corpus.ConfigRoot, ".credentials.json"),
            "invalid and forbidden");
        corpus.WriteFile("second.jsonl", UsageLine("message-2", "request-2", 20, 4));

        UsageSourceReadResult result = await corpus.CreateSource(maximumFiles: 1).ReadAsync();
        IReadOnlyList<UsageEvent> events = result.Events;

        Assert.Single(events);
        Assert.Equal(UsageSourceReadStatus.Partial, result.Status);
        Assert.Equal(UsageSourceIssueKind.PartialScan, result.Issue);
    }

    [Fact]
    public async Task InvalidOptionalCountersAndUnknownSpeedDoNotBreakTheScan()
    {
        using var corpus = new ClaudeCorpus();
        corpus.WriteLines(
            UsageLine("message-negative", "request-negative", 10, 2, cacheRead: -50),
            UsageLine("message-valid", "request-valid", 20, 4),
            UsageLine("message-speed", "request-speed", 30, 6).Replace(
                "\"cache_creation_input_tokens\":0",
                "\"cache_creation_input_tokens\":0,\"speed\":\"turbo\"",
                StringComparison.Ordinal));

        UsageSourceReadResult result = await corpus.CreateSource().ReadAsync();
        IReadOnlyList<UsageEvent> events = result.Events;

        Assert.Equal(2, events.Count);
        Assert.Contains(events, usageEvent => usageEvent.Tokens.CacheRead == 0);
        Assert.Equal(UsageSourceReadStatus.Partial, result.Status);
    }

    [Fact]
    public async Task SkipsOversizedLinesAndContinuesWithTheNextEvent()
    {
        using var corpus = new ClaudeCorpus();
        corpus.WriteLines(
            UsageLine(
                "message-large",
                "request-large",
                10,
                2,
                content: new string('x', 2_000)),
            UsageLine("message-valid", "request-valid", 20, 4));

        UsageSourceReadResult result = await corpus.CreateSource(
            maximumLineCharacters: 512).ReadAsync();
        IReadOnlyList<UsageEvent> events = result.Events;

        UsageEvent usageEvent = Assert.Single(events);
        Assert.Equal(20, usageEvent.Tokens.Input);
        Assert.Equal(UsageSourceReadStatus.Partial, result.Status);
    }

    [Fact]
    public async Task IgnoresAnUnfinishedTrailingRecordWhileClaudeIsWriting()
    {
        using var corpus = new ClaudeCorpus();
        corpus.WriteRaw(
            UsageLine("message-valid", "request-valid", 20, 4)
            + Environment.NewLine
            + "{\"type\":\"assistant\",\"message\":{\"usage\":");

        UsageSourceReadResult result = await corpus.CreateSource().ReadAsync();

        Assert.Single(result.Events);
        Assert.Equal(UsageSourceReadStatus.Complete, result.Status);
    }

    [Fact]
    public async Task PricingMatchesModelIdsWithoutCaseSensitivity()
    {
        using var corpus = new ClaudeCorpus();
        corpus.WriteLines(UsageLine(
            "message-case",
            "request-case",
            1_000,
            100,
            model: "CLAUDE-SONNET-4-6"));

        UsageEvent usageEvent = Assert.Single(
            (await corpus.CreateSource().ReadAsync()).Events);

        Assert.Equal(CostKind.CatalogEstimated, usageEvent.Cost.Kind);
        Assert.Equal("claude-sonnet-4-6", usageEvent.ModelId.Value);
    }

    [Fact]
    public void SonnetFiveUsesTheDocumentedPostIntroductoryPrice()
    {
        CostObservation cost = ClaudePricingCatalog.Resolve(
            "claude-sonnet-5",
            new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
            new TokenBreakdown(1_000_000, 100_000, 0, 0, 0),
            cacheWrite5Minutes: 0,
            cacheWrite1Hour: 0,
            reportedCostUsd: null,
            isFast: false);

        Assert.Equal(4.5m, cost.EstimatedCostUsd);
        Assert.Equal(ClaudePricingCatalog.SonnetFiveStandardVersion, cost.CatalogVersion);
    }

    private static string UsageLine(
        string messageId,
        string requestId,
        long input,
        long output,
        long cacheRead = 0,
        long cacheWrite = 0,
        decimal? costUsd = null,
        bool isSidechain = false,
        string model = "claude-sonnet-4-6",
        string content = "fixture",
        long? cacheWrite5Minutes = null,
        long? cacheWrite1Hour = null,
        string timestamp = "2026-07-22T12:00:00.000Z")
    {
        var usage = new Dictionary<string, object?>
        {
            ["input_tokens"] = input,
            ["output_tokens"] = output,
            ["cache_read_input_tokens"] = cacheRead,
            ["cache_creation_input_tokens"] = cacheWrite,
        };
        if (cacheWrite5Minutes is not null || cacheWrite1Hour is not null)
        {
            usage["cache_creation"] = new Dictionary<string, object?>
            {
                ["ephemeral_5m_input_tokens"] = cacheWrite5Minutes ?? 0,
                ["ephemeral_1h_input_tokens"] = cacheWrite1Hour ?? 0,
            };
        }

        var value = new Dictionary<string, object?>
        {
            ["type"] = "assistant",
            ["timestamp"] = timestamp,
            ["requestId"] = requestId,
            ["isSidechain"] = isSidechain,
            ["costUSD"] = costUsd,
            ["message"] = new Dictionary<string, object?>
            {
                ["id"] = messageId,
                ["model"] = model,
                ["content"] = content,
                ["usage"] = usage,
            },
        };
        return JsonSerializer.Serialize(value);
    }

    private sealed class ClaudeCorpus : IDisposable
    {
        private readonly string _path = Path.Combine(
            Path.GetTempPath(),
            "tokenusage-claude-corpus",
            Guid.NewGuid().ToString("N"));

        public ClaudeCorpus()
        {
            ConfigRoot = Path.Combine(_path, "config");
            ProjectRoot = Directory.CreateDirectory(
                Path.Combine(ConfigRoot, "projects", "project-a")).FullName;
        }

        public string ConfigRoot { get; }

        public string ProjectRoot { get; }

        public ClaudeUsageEventSource CreateSource(
            int maximumFiles = 100,
            int maximumLineCharacters = 8 * 1024 * 1024) =>
            new(
                "UTC",
                homeDirectory: _path,
                configDirectoryOverride: ConfigRoot,
                maximumFiles: maximumFiles,
                maximumLineCharacters: maximumLineCharacters);

        public void WriteLines(string line) =>
            File.WriteAllLines(Path.Combine(ProjectRoot, "session.jsonl"), [line]);

        public void WriteFile(string fileName, string line) =>
            File.WriteAllLines(Path.Combine(ProjectRoot, fileName), [line]);

        public void WriteRaw(string content) =>
            File.WriteAllText(Path.Combine(ProjectRoot, "session.jsonl"), content);

        public void WriteLines(string first, string second) =>
            File.WriteAllLines(Path.Combine(ProjectRoot, "session.jsonl"), [first, second]);

        public void WriteLines(string first, string second, string third, string fourth) =>
            File.WriteAllLines(Path.Combine(ProjectRoot, "session.jsonl"), [first, second, third, fourth]);

        public void WriteLines(string first, string second, string third) =>
            File.WriteAllLines(Path.Combine(ProjectRoot, "session.jsonl"), [first, second, third]);

        public void Dispose() => Directory.Delete(_path, recursive: true);
    }
}

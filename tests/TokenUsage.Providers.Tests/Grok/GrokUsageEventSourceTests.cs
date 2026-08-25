using System.Text.Json;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;
using TokenUsage.Providers.Grok;

namespace TokenUsage.Providers.Tests.Grok;

public sealed class GrokUsageEventSourceTests
{
    [Fact]
    public void RootDetectionDoesNotReadFiles()
    {
        string root = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))).FullName;
        string trap = Path.Combine(root, "auth.json");
        File.WriteAllText(trap, "Bearer private-account@example.test");
        try
        {
            using (var locked = new FileStream(
                       trap, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var source = new GrokUsageEventSource("UTC", grokHomeOverride: root);
                Assert.True(source.IsRootAvailable);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task MissingGrokDataIsReportedAsNoData()
    {
        string home = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var source = new GrokUsageEventSource("UTC", homeDirectory: home);

        UsageSourceReadResult result = await source.ReadAsync();

        Assert.False(source.IsRootAvailable);
        Assert.Equal("grok", source.AgentId.Value);
        Assert.Equal(SourceKind.LocalLog, source.SourceKind);
        Assert.Empty(result.Events);
        Assert.Equal(UsageSourceReadStatus.NoData, result.Status);
        Assert.Equal(UsageSourceIssueKind.RootUnavailable, result.Issue);
    }

    [Fact]
    public async Task UsesLatestSessionSnapshotAndEmitsEachModel()
    {
        using var corpus = new GrokCorpus();
        corpus.WriteSession(
            "session-a",
            Snapshot("2026-07-22T10:00:00Z", 10, 2, model: "old-model"),
            Snapshot(
                "2026-07-22T11:00:00Z",
                999,
                999,
                costUsdTicks: 9_999_999_999,
                modelUsage: new Dictionary<string, object?>
                {
                    ["grok-4.5-build"] = ModelUsage(100, 20, 30, 1_250_000_000),
                    ["grok-4.1-fast"] = ModelUsage(40, 8, 50, 0),
                }));

        GrokUsageEventSource source = corpus.CreateSource();
        UsageSourceReadResult result = await source.ReadAsync();

        Assert.True(source.IsRootAvailable);
        Assert.Equal(UsageSourceReadStatus.Complete, result.Status);
        Assert.Collection(
            result.Events.OrderBy(value => value.ModelId.Value),
            usageEvent =>
            {
                Assert.Equal("grok-4.1-fast", usageEvent.ModelId.Value);
                Assert.Equal(new TokenBreakdown(0, 8, 0, 50, 0), usageEvent.Tokens);
                Assert.Equal(CostKind.CatalogEstimated, usageEvent.Cost.Kind);
                Assert.Equal(
                    GrokPricingCatalog.Resolve(
                        "grok-4.1-fast",
                        usageEvent.Tokens).EstimatedCostUsd,
                    usageEvent.Cost.EstimatedCostUsd);
            },
            usageEvent =>
            {
                Assert.Equal("grok-4.5-build", usageEvent.ModelId.Value);
                Assert.Equal(new TokenBreakdown(70, 20, 0, 30, 0), usageEvent.Tokens);
                Assert.Equal(0.125m, usageEvent.Cost.ReportedCostUsd);
            });
    }

    [Fact]
    public async Task ReadsCurrentSessionUpdateMethodAndUnixTimestamp()
    {
        using var corpus = new GrokCorpus();
        long timestamp = new DateTimeOffset(
            2026,
            7,
            27,
            12,
            0,
            0,
            TimeSpan.Zero).ToUnixTimeSeconds();
        corpus.WriteSession(
            "session-current",
            JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["method"] = "_x.ai/session/update",
                ["timestamp"] = timestamp,
                ["params"] = new Dictionary<string, object?>
                {
                    ["update"] = new Dictionary<string, object?>
                    {
                        ["usage"] = new Dictionary<string, object?>
                        {
                            ["inputTokens"] = 100L,
                            ["outputTokens"] = 20L,
                            ["cachedReadTokens"] = 30L,
                            ["costUsdTicks"] = 1_250_000_000L,
                            ["current_model_id"] = "grok-4.5-build",
                            ["usageIsIncomplete"] = false,
                        },
                    },
                },
            }));

        UsageSourceReadResult result = await corpus.CreateSource().ReadAsync();
        UsageEvent usageEvent = Assert.Single(result.Events);

        Assert.Equal(UsageSourceReadStatus.Complete, result.Status);
        Assert.Equal(
            DateTimeOffset.FromUnixTimeSeconds(timestamp),
            usageEvent.OccurredAtUtc);
        Assert.Equal(new TokenBreakdown(70, 20, 0, 30, 0), usageEvent.Tokens);
        Assert.Equal(0.125m, usageEvent.Cost.ReportedCostUsd);
    }

    [Fact]
    public async Task IncompleteCurrentSnapshotKeepsDataAndMarksReadPartial()
    {
        using var corpus = new GrokCorpus();
        corpus.WriteSession(
            "session-incomplete",
            JsonSerializer.Serialize(new
            {
                method = "_x.ai/session/update",
                timestamp = 1_774_785_600L,
                @params = new
                {
                    update = new
                    {
                        usage = new
                        {
                            inputTokens = 100L,
                            outputTokens = 20L,
                            costUsdTicks = 1_000_000L,
                            current_model_id = "grok-4.5-build",
                            usageIsIncomplete = true,
                        },
                    },
                },
            }));

        UsageSourceReadResult result = await corpus.CreateSource().ReadAsync();

        Assert.Single(result.Events);
        Assert.Equal(UsageSourceReadStatus.Partial, result.Status);
    }

    [Fact]
    public async Task UsesTotalsWhenModelUsageIsEmptyAndKeepsKeysStable()
    {
        using var corpus = new GrokCorpus();
        corpus.WriteSession(
            "session-totals",
            Snapshot(
                "2026-07-22T11:00:00Z",
                input: 25,
                output: 4,
                cacheRead: 40,
                costUsdTicks: 0,
                model: "GROK-4.5-BUILD"));
        GrokUsageEventSource source = corpus.CreateSource();

        UsageEvent first = Assert.Single((await source.ReadAsync()).Events);
        UsageEvent second = Assert.Single((await source.ReadAsync()).Events);

        Assert.Equal("grok-4.5-build", first.ModelId.Value);
        Assert.Equal(new TokenBreakdown(0, 4, 0, 40, 0), first.Tokens);
        Assert.Equal(CostKind.CatalogEstimated, first.Cost.Kind);
        Assert.Equal(
            GrokPricingCatalog.Resolve("GROK-4.5-BUILD", first.Tokens).EstimatedCostUsd,
            first.Cost.EstimatedCostUsd);
        Assert.Equal(first.EventKey, second.EventKey);
        Assert.Equal(64, first.EventKey.Value.Length);
    }

    [Fact]
    public async Task SessionKeysIncludeTheRelativeWorkingDirectory()
    {
        using var corpus = new GrokCorpus();
        string snapshot = Snapshot("2026-07-22T11:00:00Z", 25, 4);
        corpus.WriteSessionInCwd("cwd-a", "same-session", snapshot);
        corpus.WriteSessionInCwd("cwd-b", "same-session", snapshot);

        IReadOnlyList<UsageEvent> events = (await corpus.CreateSource().ReadAsync()).Events;

        Assert.Equal(2, events.Count);
        Assert.Equal(2, events.Select(value => value.EventKey).Distinct().Count());
    }

    [Fact]
    public async Task UnifiedLogTakesPriorityOverSessionSnapshots()
    {
        using var corpus = new GrokCorpus();
        corpus.WriteSession(
            "session-preferred",
            Snapshot("2026-07-22T11:00:00Z", 25, 4, model: "grok-session-model"));
        corpus.WriteUnified(
            "{\"ts\":\"2026-06-17T11:00:00Z\",\"pid\":1,\"msg\":\"shell.turn.inference_done\",\"ctx\":{\"prompt_tokens\":1,\"completion_tokens\":1}}",
            "{\"ts\":\"2026-07-22T10:00:00Z\",\"pid\":1,\"msg\":\"model changed\",\"ctx\":{\"model\":\"grok-fallback-model\"}}",
            "{\"ts\":\"2026-07-22T10:01:00Z\",\"pid\":1,\"msg\":\"shell.turn.inference_done\",\"ctx\":{\"prompt_tokens\":100,\"completion_tokens\":20}}");

        UsageEvent usageEvent = Assert.Single(
            (await corpus.CreateSource(clock: GrokCorpus.FixtureClock).ReadAsync()).Events);

        Assert.Equal("grok-fallback-model", usageEvent.ModelId.Value);
    }

    [Fact]
    public async Task RotatedUnifiedLogFallsBackToSessionSnapshots()
    {
        using var corpus = new GrokCorpus();
        corpus.WriteSession(
            "session-history",
            Snapshot(
                "2026-08-09T11:00:00Z",
                250,
                40,
                costUsdTicks: 2_500_000_000,
                model: "grok-4.6-build"));
        corpus.WriteUnified(
            "{\"ts\":\"2026-08-25T10:00:00Z\",\"pid\":1,\"msg\":\"model changed\",\"ctx\":{\"model\":\"grok-4.6\"}}",
            "{\"ts\":\"2026-08-25T10:01:00Z\",\"pid\":1,\"msg\":\"shell.turn.inference_done\",\"ctx\":{\"prompt_tokens\":100,\"completion_tokens\":20}}");

        UsageSourceReadResult result = await corpus.CreateSource(
            clock: new FixedTimeProvider(
                new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero)))
            .ReadAsync();
        UsageEvent usageEvent = Assert.Single(result.Events);

        Assert.Equal(UsageSourceReadStatus.Complete, result.Status);
        Assert.Equal("grok-4.6-build", usageEvent.ModelId.Value);
        Assert.Equal(new TokenBreakdown(250, 40, 0, 0, 0), usageEvent.Tokens);
        Assert.Equal(CostKind.ProviderReported, usageEvent.Cost.Kind);
        Assert.Equal(0.25m, usageEvent.Cost.ReportedCostUsd);
    }

    [Fact]
    public async Task UnreadableUnifiedLogFallsBackToSessionSnapshots()
    {
        using var corpus = new GrokCorpus();
        corpus.WriteUnified(new string('x', 2_000));
        corpus.WriteSession(
            "session-fallback",
            Snapshot("2026-07-22T11:00:00Z", 25, 4, model: "session-model"));

        UsageSourceReadResult result = await corpus.CreateSource(
            maximumLineCharacters: 512).ReadAsync();
        UsageEvent usageEvent = Assert.Single(result.Events);

        Assert.Equal(UsageSourceReadStatus.Partial, result.Status);
        Assert.Equal("session-model", usageEvent.ModelId.Value);
    }

    [Fact]
    public async Task ReadsAndPricesUnifiedAsPrimarySource()
    {
        using var corpus = new GrokCorpus();
        corpus.WriteUnified(
            "{\"ts\":\"2026-07-22T10:00:00Z\",\"pid\":7,\"msg\":\"model catalog: notifying clients\",\"ctx\":{\"current_model_id\":\"grok-build\"}}",
            "{\"ts\":\"2026-07-22T10:01:00Z\",\"pid\":7,\"msg\":\"shell.turn.inference_done\",\"ctx\":{\"prompt_tokens\":100,\"cached_prompt_tokens\":30,\"completion_tokens\":20,\"reasoning_tokens\":5}}");

        UsageSourceReadResult result = await corpus.CreateSource().ReadAsync();
        UsageEvent usageEvent = Assert.Single(result.Events);

        Assert.Equal(UsageSourceReadStatus.Complete, result.Status);
        Assert.Equal(new TokenBreakdown(70, 20, 5, 30, 0), usageEvent.Tokens);
        Assert.Equal(CostKind.CatalogEstimated, usageEvent.Cost.Kind);
        Assert.Equal(0.000126m, usageEvent.Cost.EstimatedCostUsd);
        Assert.Equal(GrokPricingCatalog.Version, usageEvent.Cost.CatalogVersion);
        Assert.Equal(CoverageKind.Partial, usageEvent.Coverage);
        Assert.Equal(GrokUsageEventSource.ParserVersion, corpus.CreateSource().EventParserVersion);
        Assert.Equal(35, corpus.CreateSource().ReconciliationWindowDays);
    }

    [Fact]
    public async Task TurnWhoseModelIsNotInTheLogKeepsItsTokensWithoutAPrice()
    {
        using var corpus = new GrokCorpus();
        corpus.WriteUnified(
            "{\"ts\":\"2026-07-22T10:00:00Z\",\"pid\":7,\"msg\":\"model changed\",\"ctx\":{\"model\":\"grok-build\"}}",
            "{\"ts\":\"2026-07-22T10:01:00Z\",\"pid\":7,\"msg\":\"shell.turn.inference_done\",\"ctx\":{\"prompt_tokens\":100,\"completion_tokens\":20}}",
            "{\"ts\":\"2026-07-22T10:02:00Z\",\"pid\":9,\"msg\":\"shell.turn.inference_done\",\"ctx\":{\"prompt_tokens\":40,\"completion_tokens\":8}}");

        UsageSourceReadResult result = await corpus.CreateSource().ReadAsync();

        Assert.Equal(2, result.Events.Count);
        UsageEvent unnamed = Assert.Single(
            result.Events,
            item => item.ModelId.Value == GrokUsageEventSource.UnknownModel);
        Assert.Equal(new TokenBreakdown(40, 8, 0, 0, 0), unnamed.Tokens);
        Assert.Equal(CostKind.Unavailable, unnamed.Cost.Kind);
        Assert.Equal(CoverageKind.Unpriced, unnamed.Coverage);
    }

    [Fact]
    public async Task InferenceDoneUsesTheModelIdOnTheTurnContext()
    {
        using var corpus = new GrokCorpus();
        corpus.WriteUnified(
            "{\"ts\":\"2026-07-22T10:01:00Z\",\"pid\":11,\"msg\":\"shell.turn.inference_done\",\"ctx\":{\"current_model_id\":\"grok-4.5\",\"prompt_tokens\":100,\"completion_tokens\":20}}");

        UsageEvent usageEvent = Assert.Single((await corpus.CreateSource().ReadAsync()).Events);

        Assert.Equal("grok-4.5", usageEvent.ModelId.Value);
        Assert.Equal(CostKind.CatalogEstimated, usageEvent.Cost.Kind);
        Assert.Equal(0.00032m, usageEvent.Cost.EstimatedCostUsd);
    }

    [Fact]
    public async Task BadUsageAndScanLimitsReturnPartialWithoutLosingValidData()
    {
        using var corpus = new GrokCorpus();
        corpus.WriteSession(
            "session-partial",
            "{\"method\":\"params.update\",\"params\":{\"update\":{\"usage\":",
            Snapshot("2026-07-22T11:00:00Z", 25, 4));

        UsageSourceReadResult malformed = await corpus.CreateSource().ReadAsync();
        UsageSourceReadResult limited = await corpus.CreateSource(maximumFiles: 1).ReadAsync();

        Assert.Single(malformed.Events);
        Assert.Equal(UsageSourceReadStatus.Partial, malformed.Status);
        Assert.Empty(limited.Events);
        Assert.Equal(UsageSourceReadStatus.Partial, limited.Status);
    }

    [Fact]
    public async Task OversizedLinesAreSkippedAndCancellationIsObserved()
    {
        using var corpus = new GrokCorpus();
        corpus.WriteSession(
            "session-large",
            "{\"usage\":\"" + new string('x', 2_000) + "\"}",
            Snapshot("2026-07-22T11:00:00Z", 25, 4));
        GrokUsageEventSource source = corpus.CreateSource(maximumLineCharacters: 512);

        UsageSourceReadResult result = await source.ReadAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Single(result.Events);
        Assert.Equal(UsageSourceReadStatus.Partial, result.Status);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => source.ReadAsync(cancellation.Token));
    }

    [Fact]
    public async Task LargeSessionUsesTheLatestSnapshotFromTheBoundedTail()
    {
        using var corpus = new GrokCorpus();
        corpus.WriteSession(
            "session-large-tail",
            Snapshot("2026-07-22T10:00:00Z", 10, 2, model: "old-model"),
            new string('x', 5 * 1024 * 1024),
            Snapshot(
                "2026-07-22T11:00:00Z",
                25,
                4,
                model: "latest-model",
                modelUsage: new Dictionary<string, object?>
                {
                    ["latest-model"] = ModelUsage(25, 4, 0),
                }));

        UsageSourceReadResult result = await corpus.CreateSource().ReadAsync();
        UsageEvent usageEvent = Assert.Single(result.Events);

        Assert.Equal(UsageSourceReadStatus.Complete, result.Status);
        Assert.Equal("latest-model", usageEvent.ModelId.Value);
        Assert.Equal(new TokenBreakdown(25, 4, 0, 0, 0), usageEvent.Tokens);
    }

    [Fact]
    public async Task IgnoresCredentialAndChatFiles()
    {
        using var corpus = new GrokCorpus();
        File.WriteAllText(Path.Combine(corpus.Root, "auth.json"), "forbidden and invalid");
        File.WriteAllText(Path.Combine(corpus.Root, "chat_history.jsonl"), "forbidden and invalid");

        UsageSourceReadResult result = await corpus.CreateSource().ReadAsync();

        Assert.Empty(result.Events);
        Assert.Equal(UsageSourceReadStatus.NoData, result.Status);
        Assert.Equal(UsageSourceIssueKind.Empty, result.Issue);
    }

    [Fact]
    public async Task InvalidModelBreakdownMarksAnOtherwiseReadableSnapshotPartial()
    {
        using var corpus = new GrokCorpus();
        corpus.WriteSession(
            "session-partial-models",
            Snapshot(
                "2026-07-22T11:00:00Z",
                100,
                20,
                modelUsage: new Dictionary<string, object?>
                {
                    ["grok-4.5-build"] = ModelUsage(100, 20, 0, 1_000_000),
                    ["broken-model"] = new { inputTokens = -1, outputTokens = 2 },
                }));

        UsageSourceReadResult result = await corpus.CreateSource().ReadAsync();

        Assert.Single(result.Events);
        Assert.Equal(UsageSourceReadStatus.Partial, result.Status);
    }

    [Fact]
    public async Task InvalidGrokHomeFallsBackToTheDefaultRoot()
    {
        using var corpus = new GrokCorpus();
        var source = new GrokUsageEventSource(
            "UTC",
            homeDirectory: corpus.Root,
            grokHomeOverride: "\0invalid");

        UsageSourceReadResult result = await source.ReadAsync();

        Assert.Equal(UsageSourceReadStatus.NoData, result.Status);
    }

    private static Dictionary<string, object?> ModelUsage(
        long inputTokens,
        long outputTokens,
        long cacheReadInputTokens,
        long? costUsdTicks = null) => new Dictionary<string, object?>
        {
            ["inputTokens"] = inputTokens,
            ["outputTokens"] = outputTokens,
            ["cachedReadTokens"] = cacheReadInputTokens,
            ["costUsdTicks"] = costUsdTicks,
        };

    private static string Snapshot(
        string timestamp,
        long input,
        long output,
        long cacheRead = 0,
        long? costUsdTicks = null,
        string model = "grok-4.5-build",
        Dictionary<string, object?>? modelUsage = null)
    {
        var usage = new Dictionary<string, object?>
        {
            ["inputTokens"] = input,
            ["outputTokens"] = output,
            ["cacheReadInputTokens"] = cacheRead,
            ["current_model_id"] = model,
        };
        if (costUsdTicks is long ticks)
        {
            usage["costUsdTicks"] = ticks;
        }

        if (modelUsage is not null)
        {
            usage["modelUsage"] = modelUsage;
        }

        return JsonSerializer.Serialize(
            new Dictionary<string, object?>
            {
                ["method"] = "params.update",
                ["params"] = new Dictionary<string, object?>
                {
                    ["update"] = new Dictionary<string, object?>
                    {
                        ["timestamp"] = timestamp,
                        ["usage"] = usage,
                    },
                },
            });
    }

    private sealed class GrokCorpus : IDisposable
    {
        private readonly string _path = Path.Combine(
            Path.GetTempPath(),
            "tokenusage-grok-corpus",
            Guid.NewGuid().ToString("N"));

        public GrokCorpus() => Directory.CreateDirectory(Path.Combine(_path, "sessions"));

        public string Root => _path;

        public GrokUsageEventSource CreateSource(
            int maximumFiles = 100,
            int maximumLineCharacters = 8 * 1024 * 1024,
            TimeProvider? clock = null) => new(
            "UTC",
            homeDirectory: _path,
            grokHomeOverride: _path,
            maximumFiles: maximumFiles,
            maximumLineCharacters: maximumLineCharacters,
            clock: clock ?? FixtureClock);

        public static TimeProvider FixtureClock { get; } = new FixedTimeProvider(
            new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero));

        public void WriteSession(string id, params string[] lines)
            => WriteSessionInCwd("cwd", id, lines);

        public void WriteSessionInCwd(string cwd, string id, params string[] lines)
        {
            string session = Directory.CreateDirectory(
                Path.Combine(_path, "sessions", cwd, id)).FullName;
            File.WriteAllText(
                Path.Combine(session, "summary.json"),
                "{\"updated_at\":\"2026-07-22T11:00:00Z\",\"current_model_id\":\"grok-4.5-build\"}");
            File.WriteAllLines(Path.Combine(session, "updates.jsonl"), lines);
        }

        public void WriteUnified(params string[] lines)
        {
            string logs = Directory.CreateDirectory(Path.Combine(_path, "logs")).FullName;
            File.WriteAllLines(Path.Combine(logs, "unified.jsonl"), lines);
        }

        public void Dispose() => Directory.Delete(_path, recursive: true);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow.ToUniversalTime();
    }
}

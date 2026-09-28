using System.Text;
using System.Text.Json;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;
using TokenUsage.Providers.CopilotVsCode;

namespace TokenUsage.Providers.Tests.CopilotVsCode;

public sealed class CopilotVsCodeUsageEventSourceTests
{
    private const string Canary = "CANARY-private-prompt-text-7f3a";
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly long Started = Now.AddHours(-2).ToUnixTimeMilliseconds();
    private static readonly long Completed = Started + 30_000;

    [Fact]
    public async Task MissingVsCodeReturnsRootUnavailable()
    {
        using var corpus = new Corpus(createCode: false);

        CopilotVsCodeUsageEventSource source = corpus.CreateSource();
        UsageSourceReadResult result = await source.ReadAsync();

        Assert.False(source.IsRootAvailable);
        Assert.Equal("copilot", source.AgentId.Value);
        Assert.Equal(UsageSourceReadStatus.NoData, result.Status);
        Assert.Equal(UsageSourceIssueKind.RootUnavailable, result.Issue);
    }

    [Fact]
    public async Task OperationLogReplayKeepsTheLastWriteAndHonoursTruncation()
    {
        using var corpus = new Corpus();
        corpus.WriteWorkspaceSession(
            "session-a",
            Initial("session-a", Request("dropped", state: 1, promptTokens: 999, completionTokens: 999, credits: 9)),
            Push(["requests"], truncateAt: 0, Request("r1", state: 0)),
            Set(["requests", 0, "promptTokens"], 40_000),
            Set(["requests", 0, "completionTokens"], 300),
            Set(["requests", 0, "completionTokens"], 1_200),
            Set(["requests", 0, "copilotCredits"], 2.5),
            Set(["requests", 0, "result"], Result("claude-haiku-4-5-20251001")),
            Set(["requests", 0, "modelState"], new { value = 1, completedAt = Completed }),
            Set(["requests", 0, "response"], new[] { new { kind = "markdownContent", value = Canary } }));

        UsageSourceReadResult result = await corpus.CreateSource().ReadAsync();

        Assert.Equal(UsageSourceReadStatus.Complete, result.Status);
        UsageEvent usage = Assert.Single(result.Events);
        Assert.Equal(40_000, usage.Tokens.Input);
        Assert.Equal(1_200, usage.Tokens.Output);
        Assert.Equal(0, usage.Tokens.CacheRead);
        Assert.Equal(UsageComponentAvailability.Unknown, usage.DetailMetadata.Input);
        Assert.Equal(UsageComponentAvailability.Measured, usage.DetailMetadata.Output);
        Assert.Equal("claude-haiku-4-5-20251001", usage.ModelId.Value);
        Assert.Equal("anthropic", usage.ModelProviderId?.Value);
        Assert.Equal("auto", usage.ObservedModelId?.Value);
        Assert.Equal(CostKind.ProviderReported, usage.Cost.Kind);
        Assert.Equal(0.025m, usage.Cost.ReportedCostUsd);
        Assert.Equal(CoverageKind.Partial, usage.Coverage);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(Completed), usage.OccurredAtUtc);
        Assert.Equal(UsageTimePrecision.Interval, usage.TimePrecision);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(Started), usage.IntervalStartedAtUtc);
        Assert.Equal(Key("session-a", "r1"), usage.EventKey.Value);
        AssertNoCanary(result);
    }

    [Fact]
    public async Task LegacySnapshotUsesResultMetadataWhenRequestCountersAreAbsent()
    {
        using var corpus = new Corpus();
        Corpus.WriteRaw(
            Path.Combine(corpus.CodeUser, "workspaceStorage", "ws1", "chatSessions", "legacy.json"),
            JsonSerializer.Serialize(new
            {
                version = 3,
                sessionId = "legacy",
                requests = new object[]
                {
                    new
                    {
                        requestId = "old",
                        timestamp = Started,
                        modelId = "copilot/gpt-5.5",
                        message = new { text = Canary },
                        result = new
                        {
                            metadata = new
                            {
                                resolvedModel = "gpt-5.5",
                                promptTokens = 30_000,
                                outputTokens = 65,
                                renderedUserMessage = new[] { new { text = Canary } },
                            },
                        },
                    },
                },
            }));

        UsageSourceReadResult result = await corpus.CreateSource().ReadAsync();

        UsageEvent usage = Assert.Single(result.Events);
        Assert.Equal(30_000, usage.Tokens.Input);
        Assert.Equal(65, usage.Tokens.Output);
        Assert.Equal(UsageComponentAvailability.Unknown, usage.DetailMetadata.Output);
        Assert.Equal("gpt-5.5", usage.ModelId.Value);
        Assert.Null(usage.ObservedModelId);
        Assert.Equal(CostKind.Unavailable, usage.Cost.Kind);
        Assert.Equal(CoverageKind.Unpriced, usage.Coverage);
        Assert.Equal(UsageTimePrecision.Timestamp, usage.TimePrecision);
        AssertNoCanary(result);
    }

    [Fact]
    public async Task PendingAndWaitingRequestsAreSkipped()
    {
        using var corpus = new Corpus();
        corpus.WriteWorkspaceSession(
            "session-b",
            Initial(
                "session-b",
                Request("pending", state: 0, promptTokens: 10, completionTokens: 5, credits: 1),
                Request("waiting", state: 4, promptTokens: 10, completionTokens: 5, credits: 1),
                Request("cancelled", state: 2, promptTokens: 10, completionTokens: 5, credits: 1)));

        UsageSourceReadResult result = await corpus.CreateSource().ReadAsync();

        Assert.Equal(Key("session-b", "cancelled"), Assert.Single(result.Events).EventKey.Value);
    }

    [Fact]
    public async Task ModelTotalsAreMeasuredWithCachedTokensTakenFromInput()
    {
        using var corpus = new Corpus();
        corpus.WriteWorkspaceSession(
            "session-c",
            Initial(
                "session-c",
                Request("single", state: 1, promptTokens: 5, completionTokens: 5, credits: 4,
                    modelTotals: [new { model = "claude-sonnet-5", inputTokens = 100_000, cachedTokens = 80_000, outputTokens = 2_000 }]),
                Request("mixed", state: 1, promptTokens: 5, completionTokens: 5, credits: 1,
                    resolvedModel: "gpt-5.6-luna",
                    modelTotals:
                    [
                        new { model = "gpt-5.6-luna", inputTokens = 1_000, cachedTokens = 400, outputTokens = 50 },
                        new { model = "gpt-5-mini", inputTokens = 10, cachedTokens = 50, outputTokens = 5 },
                    ])));

        UsageSourceReadResult result = await corpus.CreateSource().ReadAsync();

        UsageEvent single = result.Events.Single(item => item.EventKey.Value == Key("session-c", "single"));
        Assert.Equal(20_000, single.Tokens.Input);
        Assert.Equal(80_000, single.Tokens.CacheRead);
        Assert.Equal(2_000, single.Tokens.Output);
        Assert.Equal(UsageComponentAvailability.Measured, single.DetailMetadata.Input);
        Assert.Equal(UsageComponentAvailability.Measured, single.DetailMetadata.CacheRead);
        Assert.Equal("claude-sonnet-5", single.ModelId.Value);

        UsageEvent mixed = result.Events.Single(item => item.EventKey.Value == Key("session-c", "mixed"));
        Assert.Equal(600, mixed.Tokens.Input);
        Assert.Equal(410, mixed.Tokens.CacheRead);
        Assert.Equal(55, mixed.Tokens.Output);
        Assert.Equal("gpt-5.6-luna", mixed.ModelId.Value);
        Assert.Equal("openai", mixed.ModelProviderId?.Value);
    }

    [Fact]
    public async Task SessionFoundUnderSeveralRootsIsCountedOnceFromTheNewestCopy()
    {
        using var corpus = new Corpus();
        string older = corpus.WriteWorkspaceSession(
            "shared",
            Initial("shared", Request("r1", state: 1, promptTokens: 10, completionTokens: 10, credits: 1)));
        File.SetLastWriteTimeUtc(older, Now.AddHours(-3).UtcDateTime);
        Corpus.WriteRaw(
            Path.Combine(corpus.InsidersUser, "globalStorage", "emptyWindowChatSessions", "shared.jsonl"),
            Lines(Initial("shared", Request("r1", state: 1, promptTokens: 10, completionTokens: 20, credits: 2))));

        UsageSourceReadResult result = await corpus.CreateSource().ReadAsync();

        UsageEvent usage = Assert.Single(result.Events);
        Assert.Equal(20, usage.Tokens.Output);
        Assert.Equal(0.02m, usage.Cost.ReportedCostUsd);
    }

    [Fact]
    public async Task GarbledLinesMarkTheReadPartialWithoutLeakingContent()
    {
        using var corpus = new Corpus();
        string path = Path.Combine(corpus.CodeUser, "workspaceStorage", "ws2", "chatSessions", "session-d.jsonl");
        Corpus.WriteRaw(
            path,
            Lines(Initial("session-d", Request("r1", state: 1, promptTokens: 10, completionTokens: 10, credits: 1)))
            + "{\"kind\":1,\"k\":[\"requests\",0,\"completionTokens\"],\"v\":" + Canary + "}\n"
            + Lines(Set(["requests", 0, "completionTokens"], 77))
            + "{\"kind\":1,\"k\":[\"requests\",0,\"completionTokens\"],\"v\":12");

        UsageSourceReadResult result = await corpus.CreateSource().ReadAsync();

        Assert.Equal(UsageSourceReadStatus.Partial, result.Status);
        Assert.Equal(UsageSourceIssueKind.PartialScan, result.Issue);
        Assert.Equal(77, Assert.Single(result.Events).Tokens.Output);
        AssertNoCanary(result);
    }

    [Fact]
    public async Task UnterminatedLastLineIsAWriteInProgress()
    {
        using var corpus = new Corpus();
        Corpus.WriteRaw(
            Path.Combine(corpus.CodeUser, "workspaceStorage", "ws3", "chatSessions", "session-e.jsonl"),
            Lines(Initial("session-e", Request("r1", state: 1, promptTokens: 10, completionTokens: 10, credits: 1)))
            + "{\"kind\":1,\"k\":[\"requests\",0,");

        UsageSourceReadResult result = await corpus.CreateSource().ReadAsync();

        Assert.Equal(UsageSourceReadStatus.Complete, result.Status);
        Assert.Single(result.Events);
    }

    [Fact]
    public async Task OversizedFileIsNotReadAndMarksTheReadPartial()
    {
        using var corpus = new Corpus();
        corpus.WriteWorkspaceSession(
            "big",
            Initial("big", Request("r1", state: 1, promptTokens: 10, completionTokens: 10, credits: 1, message: new string('x', 4_096))));

        UsageSourceReadResult result = await corpus.CreateSource(maximumFileBytes: 1_024).ReadAsync();

        Assert.Equal(UsageSourceReadStatus.NoData, result.Status);
        Assert.Equal(UsageSourceIssueKind.AccessBlocked, result.Issue);
    }

    [Fact]
    public async Task UnknownVersionOrEntryKindIsAnUnsupportedSchema()
    {
        using var corpus = new Corpus();
        Corpus.WriteRaw(
            Path.Combine(corpus.CodeUser, "workspaceStorage", "ws4", "chatSessions", "future.jsonl"),
            Lines(new
            {
                kind = 0,
                v = new { version = 9, sessionId = "future", requests = new[] { Request("r1", state: 1, promptTokens: 1, completionTokens: 1, credits: 1) } },
            }));
        Corpus.WriteRaw(
            Path.Combine(corpus.CodeUser, "workspaceStorage", "ws5", "chatSessions", "kinds.jsonl"),
            Lines(
                Initial("kinds", Request("r1", state: 1, promptTokens: 1, completionTokens: 1, credits: 1)),
                new { kind = 7, k = new object[] { "requests" } }));

        UsageSourceReadResult result = await corpus.CreateSource().ReadAsync();

        Assert.Equal(UsageSourceReadStatus.NoData, result.Status);
        Assert.Equal(UsageSourceIssueKind.UnsupportedSchema, result.Issue);
    }

    private static void AssertNoCanary(UsageSourceReadResult result)
    {
        string rendered = string.Join('\n', result.Events.Select(item => item.ToString()))
            + result.ToString();
        Assert.DoesNotContain(Canary, rendered, StringComparison.Ordinal);
    }

    private static string Key(string sessionId, string requestId) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(
            $"copilot-vscode\0chat-request-v1\0{sessionId}\0{requestId}"))).ToLowerInvariant();

    private static object Initial(string sessionId, params object[] requests) => new
    {
        kind = 0,
        v = new
        {
            version = 3,
            sessionId,
            customTitle = Canary,
            requests,
        },
    };

    private static object Set(object[] path, object value) => new { kind = 1, k = path, v = value };

    private static object Push(object[] path, int truncateAt, params object[] values) =>
        new { kind = 2, k = path, v = values, i = truncateAt };

    private static object Result(string resolvedModel) => new
    {
        timings = new { totalElapsed = 1_000 },
        metadata = new
        {
            resolvedModel,
            toolCallRounds = new[] { new { response = Canary, thinking = new { text = new[] { Canary }, tokens = 12 } } },
        },
        details = Canary,
    };

    private static Dictionary<string, object?> Request(
        string requestId,
        int state,
        long? promptTokens = null,
        long? completionTokens = null,
        double? credits = null,
        string? resolvedModel = null,
        object[]? modelTotals = null,
        string message = Canary)
    {
        var request = new Dictionary<string, object?>
        {
            ["requestId"] = requestId,
            ["timestamp"] = Started,
            ["modelId"] = "copilot/auto",
            ["message"] = new { text = message, parts = new[] { new { text = message } } },
            ["variableData"] = new { variables = new[] { new { value = Canary } } },
            ["modelState"] = state == 0 ? new { value = 0 } : (object)new { value = state, completedAt = Completed },
        };
        if (promptTokens is not null) request["promptTokens"] = promptTokens;
        if (completionTokens is not null) request["completionTokens"] = completionTokens;
        if (credits is not null) request["copilotCredits"] = credits;
        if (modelTotals is not null) request["modelTotals"] = modelTotals;
        if (resolvedModel is not null) request["result"] = Result(resolvedModel);
        return request;
    }

    private static string Lines(params object[] entries) =>
        string.Concat(entries.Select(entry => JsonSerializer.Serialize(entry) + "\n"));

    private sealed class Corpus : IDisposable
    {
        public Corpus(bool createCode = true)
        {
            Root = Path.Combine(Path.GetTempPath(), "tokenusage-copilot-vscode-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            CodeUser = Path.Combine(Root, "Code", "User");
            InsidersUser = Path.Combine(Root, "Code - Insiders", "User");
            if (createCode)
            {
                Directory.CreateDirectory(CodeUser);
                Directory.CreateDirectory(InsidersUser);
            }
        }

        public string Root { get; }

        public string CodeUser { get; }

        public string InsidersUser { get; }

        public CopilotVsCodeUsageEventSource CreateSource(long maximumFileBytes = CopilotVsCodeUsageEventSource.DefaultMaximumFileBytes) =>
            new("UTC", Root, maximumFileBytes: maximumFileBytes, clock: new FixedTimeProvider(Now));

        public string WriteWorkspaceSession(string sessionId, params object[] entries) =>
            WriteRaw(
                Path.Combine(CodeUser, "workspaceStorage", "ws-" + sessionId, "chatSessions", sessionId + ".jsonl"),
                Lines(entries));

        public static string WriteRaw(string path, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            File.SetLastWriteTimeUtc(path, Now.AddHours(-1).UtcDateTime);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}

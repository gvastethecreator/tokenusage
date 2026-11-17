using System.Text.Json;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;
using TokenUsage.Providers.Mux;

namespace TokenUsage.Providers.Tests.Mux;

public sealed class MuxUsageEventSourceTests
{
    [Fact]
    public async Task MissingSessionsDirectoryReturnsRootUnavailable()
    {
        using var folder = new TemporaryFolder();
        var source = new MuxUsageEventSource(
            "UTC",
            sessionsDirectoryOverride: Path.Combine(folder.Path, "missing"));

        UsageSourceReadResult result = await source.ReadAsync();

        Assert.False(source.IsRootAvailable);
        Assert.Equal(UsageSourceIssueKind.RootUnavailable, result.Issue);
        Assert.Empty(result.Events);
    }

    [Theory]
    [InlineData(".xum", false)]
    [InlineData(".mux", false)]
    [InlineData(".cmux", false)]
    [InlineData(".mux", true)]
    public async Task PublishedHomeSelectionReadsOneActiveRoot(string activeHome, bool markedLegacy)
    {
        using var folder = new TemporaryFolder();
        if (markedLegacy)
        {
            Directory.CreateDirectory(Path.Combine(folder.Path, ".xum"));
            File.WriteAllText(Path.Combine(folder.Path, ".xum.legacy-fallback"), activeHome + "\n");
        }
        string session = Directory.CreateDirectory(Path.Combine(folder.Path, activeHome, "sessions", "workspace-1")).FullName;
        File.WriteAllText(Path.Combine(session, "session-usage.json"), """
            {"version":1,"byModel":{"openai:gpt-5":{"input":{"tokens":100},"output":{"tokens":20}}},"lastRequest":{"timestamp":1794016860000}}
            """);

        UsageSourceReadResult result = await new MuxUsageEventSource("UTC", homeDirectory: folder.Path).ReadAsync();

        Assert.Equal(UsageSourceReadStatus.Complete, result.Status);
        Assert.Equal(120, Assert.Single(result.Events).Tokens.Total);
    }

    [Fact]
    public async Task PopulatedCanonicalHomeWinsOverLegacyHomeAndMarker()
    {
        using var folder = new TemporaryFolder();
        string canonical = Directory.CreateDirectory(Path.Combine(folder.Path, ".xum", "sessions")).FullName;
        Directory.CreateDirectory(Path.Combine(folder.Path, ".mux", "sessions", "workspace-legacy"));
        File.WriteAllText(Path.Combine(folder.Path, ".xum.legacy-fallback"), ".mux");
        string session = Directory.CreateDirectory(Path.Combine(canonical, "workspace-current")).FullName;
        File.WriteAllText(Path.Combine(session, "session-usage.json"), """
            {"version":1,"byModel":{"openai:gpt-5":{"input":{"tokens":100},"output":{"tokens":20}}},"lastRequest":{"timestamp":1794016860000}}
            """);

        UsageSourceReadResult result = await new MuxUsageEventSource("UTC", homeDirectory: folder.Path).ReadAsync();

        Assert.Equal(UsageSourceReadStatus.Complete, result.Status);
        Assert.Equal(120, Assert.Single(result.Events).Tokens.Total);
    }

    [Fact]
    public async Task ReadsAggregateTokensAndProviderReportedCost()
    {
        using var folder = new TemporaryFolder();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string sessions = Directory.CreateDirectory(Path.Combine(folder.Path, "sessions")).FullName;
        string session = Directory.CreateDirectory(Path.Combine(sessions, "workspace-1")).FullName;
        File.WriteAllText(
            Path.Combine(session, "session-usage.json"),
            JsonSerializer.Serialize(new
            {
                version = 1,
                byModel = new Dictionary<string, object>
                {
                    ["anthropic:claude-sonnet-4-6"] = new
                    {
                        input = new { tokens = 100L, cost_usd = 0.003m },
                        cached = new { tokens = 30L, cost_usd = 0.000009m },
                        cacheCreate = new { tokens = 20L, cost_usd = 0.00006m },
                        output = new { tokens = 10L, cost_usd = 0.00015m },
                        reasoning = new { tokens = 5L, cost_usd = 0m },
                    },
                },
                lastRequest = new
                {
                    model = "anthropic:claude-sonnet-4-6",
                    timestamp = now.AddDays(-1).ToUnixTimeMilliseconds(),
                },
                rolledUpFrom = new Dictionary<string, object>(),
                privateTranscript = "must not enter the event",
            }));
        var source = new MuxUsageEventSource("UTC", sessionsDirectoryOverride: sessions);

        UsageSourceReadResult result = await source.ReadAsync();
        UsageEvent usageEvent = Assert.Single(result.Events);

        Assert.Equal(UsageSourceReadStatus.Complete, result.Status);
        Assert.Equal("mux", usageEvent.AgentId.Value);
        Assert.Equal("anthropic", usageEvent.ModelProviderId?.Value);
        Assert.Equal("claude-sonnet-4-6", usageEvent.ModelId.Value);
        Assert.Equal(new TokenBreakdown(100, 10, 5, 30, 20), usageEvent.Tokens);
        Assert.Equal(CostKind.ProviderReported, usageEvent.Cost.Kind);
        Assert.Equal(0.003219m, usageEvent.Cost.ReportedCostUsd);
        Assert.Equal(CoverageKind.Complete, usageEvent.Coverage);

        string child = Directory.CreateDirectory(Path.Combine(sessions, "child")).FullName;
        File.WriteAllText(Path.Combine(child, "session-usage.json"), """
            {"version":1,"byModel":{"anthropic:claude-sonnet-4-6":{"input":{"tokens":30},"output":{"tokens":10}}}}
            """);
        UsageSourceReadResult initial = await source.ReadAsync();
        Assert.Equal(UsageSourceReadStatus.Complete, initial.Status);
        Assert.Equal(205, initial.Events.Sum(row => row.Tokens.Total));
        UsageEventKey childKey = Assert.Single(initial.Events, row => row.Tokens.Total == 40).EventKey;

        string destination = Path.Combine(folder.Path, "usage.v1.db");
        var refresh = new LocalUsageRefresh(destination, source, TimeProvider.System);
        await refresh.RefreshAsync();
        UsageRepository repository = await UsageRepository.OpenAsync(destination);
        Assert.Equal(205, (await Stored()).Sum(row => row.Tokens.Total));
        string healthy = Directory.CreateDirectory(Path.Combine(sessions, "healthy")).FullName;
        File.WriteAllText(Path.Combine(healthy, "session-usage.json"), """
            {"version":1,"byModel":{"anthropic:claude-sonnet-4-6":{"input":{"tokens":60},"output":{"tokens":10}}}}
            """);
        File.WriteAllText(Path.Combine(child, "session-usage.json"), """
            {"version":1,"byModel":{"anthropic:claude-sonnet-4-6":{"input":{"tokens":50},"output":{"tokens":10}}}}
            """);

        string[] rollups = ["""{"child":true}""", """{"child":{"totalTokens":40,"rolledUpAtMs":1794016860000}}"""];
        foreach (string rollup in rollups)
        {
            File.WriteAllText(Path.Combine(session, "session-usage.json"), $$$$"""
                {"version":1,"byModel":{"anthropic:claude-sonnet-4-6":{"input":{"tokens":130},"output":{"tokens":20},"cached":{"tokens":30},"cacheCreate":{"tokens":20},"reasoning":{"tokens":5}}},"rolledUpFrom":{{{{rollup}}}} }
                """);
            UsageSourceReadResult rolledUp = await source.ReadAsync();
            Assert.Equal(UsageSourceReadStatus.Partial, rolledUp.Status);
            Assert.Equal(UsageSourceIssueKind.PartialScan, rolledUp.Issue);
            Assert.Equal(130, rolledUp.Events.Sum(row => row.Tokens.Total));
            Assert.Equal(60, Assert.Single(rolledUp.Events, row => row.EventKey == childKey).Tokens.Total);
            Assert.Equal(UsageSourceReadStatus.Partial, (await refresh.RefreshAsync()).OverallStatus);
            IReadOnlyList<UsageEvent> stored = await Stored();
            Assert.Equal(3, stored.Count);
            Assert.Equal(295, stored.Sum(row => row.Tokens.Total));
            Assert.Equal(usageEvent.Tokens, Assert.Single(stored, row => row.EventKey == usageEvent.EventKey).Tokens);
            Assert.Equal(60, Assert.Single(stored, row => row.EventKey == childKey).Tokens.Total);
        }

        File.Delete(Path.Combine(session, "session-usage.json"));
        File.Delete(Path.Combine(child, "session-usage.json"));
        Assert.Equal(UsageSourceReadStatus.Complete, (await refresh.RefreshAsync()).OverallStatus);
        IReadOnlyList<UsageEvent> retained = await Stored();
        Assert.Equal(3, retained.Count);
        Assert.Equal(295, retained.Sum(row => row.Tokens.Total));
        Assert.Equal(usageEvent.Cost, Assert.Single(retained, row => row.EventKey == usageEvent.EventKey).Cost);

        Task<IReadOnlyList<UsageEvent>> Stored() => repository.QueryUsageEventsAsync(
            now.AddDays(-2), now.AddDays(1), source.AgentId);
    }

    [Fact]
    public async Task DottedGoogleModelIdsKeepTheirVersionWhenReportedCostIsZero()
    {
        using var folder = new TemporaryFolder();
        string sessions = Directory.CreateDirectory(Path.Combine(folder.Path, "sessions")).FullName;
        string session = Directory.CreateDirectory(Path.Combine(sessions, "workspace-1")).FullName;
        File.WriteAllText(
            Path.Combine(session, "session-usage.json"),
            JsonSerializer.Serialize(new
            {
                version = 1,
                byModel = new Dictionary<string, object>
                {
                    ["google:gemini-3.6-flash"] = new
                    {
                        input = new { tokens = 1_000_000L, cost_usd = 0m },
                        cached = new { tokens = 0L, cost_usd = 0m },
                        cacheCreate = new { tokens = 0L, cost_usd = 0m },
                        output = new { tokens = 100_000L, cost_usd = 0m },
                        reasoning = new { tokens = 0L, cost_usd = 0m },
                    },
                },
                lastRequest = new
                {
                    model = "google:gemini-3.6-flash",
                    timestamp = 1_786_488_925_618L,
                },
            }));

        UsageEvent usageEvent = Assert.Single(
            (await new MuxUsageEventSource(
                "UTC",
                sessionsDirectoryOverride: sessions).ReadAsync()).Events);

        Assert.Equal("gemini-3.6-flash", usageEvent.ModelId.Value);
        Assert.Equal("google", usageEvent.ModelProviderId?.Value);
        Assert.Equal(CostKind.CatalogEstimated, usageEvent.Cost.Kind);
        Assert.Equal(1.125m, usageEvent.Cost.EstimatedCostUsd);
    }

    private sealed class TemporaryFolder : IDisposable
    {
        public TemporaryFolder()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "tokenusage-mux-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}

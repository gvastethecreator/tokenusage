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

    [Fact]
    public async Task ReadsAggregateTokensAndProviderReportedCost()
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
                    timestamp = 1_786_488_925_618L,
                },
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

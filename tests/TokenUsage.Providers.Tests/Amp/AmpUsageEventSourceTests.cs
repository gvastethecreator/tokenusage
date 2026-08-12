using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;
using TokenUsage.Providers.Amp;

namespace TokenUsage.Providers.Tests.Amp;

public sealed class AmpUsageEventSourceTests
{
    [Fact]
    public async Task ReadsNumericLedgerAndMaxMergesDuplicateMessages()
    {
        using var folder = new TemporaryFolder();
        string ledgerPath = Path.Combine(folder.Path, "ledger.jsonl");
        await File.WriteAllLinesAsync(
            ledgerPath,
            [
                """
                {"to_message_id":"message-1","model":"gpt-5","credits":4.5,"timestamp":"2026-08-12T10:00:00Z","tokens":{"input":1000000,"output":50000}}
                """,
                """
                {"toMessageId":"message-1","createdAt":"2026-08-12T10:00:01Z","usage":{"inputTokens":900000,"outputTokens":100000}}
                """,
            ]);
        var source = new AmpUsageEventSource(
            "UTC",
            ledgerPathOverride: ledgerPath);

        UsageSourceReadResult result = await source.ReadAsync();
        UsageEvent usageEvent = Assert.Single(result.Events);

        Assert.Equal(UsageSourceReadStatus.Complete, result.Status);
        Assert.Equal("amp", usageEvent.AgentId.Value);
        Assert.Equal(new TokenBreakdown(1_000_000, 100_000, 0, 0, 0), usageEvent.Tokens);
        Assert.Equal(CostKind.CatalogEstimated, usageEvent.Cost.Kind);
        Assert.Equal(2.25m, usageEvent.Cost.EstimatedCostUsd);
        Assert.Equal(CoverageKind.Partial, usageEvent.Coverage);
        Assert.DoesNotContain("message-1", usageEvent.EventKey.Value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingLedgerReturnsRootUnavailable()
    {
        using var folder = new TemporaryFolder();
        var source = new AmpUsageEventSource(
            "UTC",
            ledgerPathOverride: Path.Combine(folder.Path, "missing.jsonl"));

        UsageSourceReadResult result = await source.ReadAsync();

        Assert.Equal(UsageSourceIssueKind.RootUnavailable, result.Issue);
        Assert.Empty(result.Events);
    }

    private sealed class TemporaryFolder : IDisposable
    {
        public TemporaryFolder()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "tokenusage-amp-tests",
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

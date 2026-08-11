using System.Text.Json;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;
using TokenUsage.Providers.Cursor;

namespace TokenUsage.Providers.Tests.Cursor;

public sealed class CursorUsageEventSourceTests
{
    [Fact]
    public async Task MissingCursorAndSpoolReturnsRootUnavailable()
    {
        using var corpus = new CursorCorpus(createCursorHome: false);
        CursorUsageEventSource source = corpus.CreateSource();

        UsageSourceReadResult result = await source.ReadAsync();

        Assert.False(source.IsRootAvailable);
        Assert.Equal("cursor", source.AgentId.Value);
        Assert.Equal(SourceKind.LocalLog, source.SourceKind);
        Assert.Empty(result.Events);
        Assert.Equal(UsageSourceReadStatus.NoData, result.Status);
        Assert.Equal(UsageSourceIssueKind.RootUnavailable, result.Issue);
    }

    [Fact]
    public async Task InstalledCursorWithoutHookDataReturnsEmpty()
    {
        using var corpus = new CursorCorpus();

        UsageSourceReadResult result = await corpus.CreateSource().ReadAsync();

        Assert.Equal(UsageSourceReadStatus.NoData, result.Status);
        Assert.Equal(UsageSourceIssueKind.Empty, result.Issue);
    }

    [Fact]
    public async Task ReadsNumericHookRecordAsPartialUnpricedUsage()
    {
        using var corpus = new CursorCorpus();
        corpus.WriteRecord(
            "a" + new string('0', 63),
            "2026-08-11T15:30:00Z",
            "claude-sonnet-4-6",
            100,
            20,
            40,
            5);

        UsageSourceReadResult result = await corpus.CreateSource().ReadAsync();
        UsageEvent usageEvent = Assert.Single(result.Events);

        Assert.Equal(UsageSourceReadStatus.Partial, result.Status);
        Assert.Equal(UsageSourceIssueKind.PartialScan, result.Issue);
        Assert.Equal("anthropic", usageEvent.ModelProviderId?.Value);
        Assert.Equal("claude-sonnet-4-6", usageEvent.ModelId.Value);
        Assert.Equal(new TokenBreakdown(100, 20, 0, 40, 5), usageEvent.Tokens);
        Assert.Equal(CostKind.Unavailable, usageEvent.Cost.Kind);
        Assert.Equal(CoverageKind.Unpriced, usageEvent.Coverage);
        Assert.Equal(CursorUsageEventSource.ParserVersion, usageEvent.ParserVersion);
    }

    [Fact]
    public async Task CurrentSpoolReplacesRotatedCopyWithTheSameHashedIdentity()
    {
        using var corpus = new CursorCorpus();
        string key = "b" + new string('0', 63);
        corpus.WriteRecord(key, "2026-08-11T15:30:00Z", "gpt-5", 10, 2, 0, 0, rotated: true);
        corpus.WriteRecord(key, "2026-08-11T15:31:00Z", "gpt-5", 30, 4, 5, 0);

        UsageEvent usageEvent = Assert.Single((await corpus.CreateSource().ReadAsync()).Events);

        Assert.Equal(new TokenBreakdown(30, 4, 0, 5, 0), usageEvent.Tokens);
        Assert.Equal(new DateTimeOffset(2026, 8, 11, 15, 31, 0, TimeSpan.Zero), usageEvent.OccurredAtUtc);
    }

    private sealed class CursorCorpus : IDisposable
    {
        public CursorCorpus(bool createCursorHome = true)
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "tokenusage-cursor-source-tests",
                Guid.NewGuid().ToString("N"));
            Home = Path.Combine(Root, "home");
            LocalAppData = Path.Combine(Root, "local");
            Directory.CreateDirectory(Home);
            if (createCursorHome)
            {
                Directory.CreateDirectory(Path.Combine(Home, ".cursor"));
            }
        }

        public string Root { get; }

        public string Home { get; }

        public string LocalAppData { get; }

        public CursorUsageEventSource CreateSource() => new(
            "UTC",
            Home,
            LocalAppData);

        public void WriteRecord(
            string eventKey,
            string timestamp,
            string model,
            long input,
            long output,
            long cacheRead,
            long cacheWrite,
            bool rotated = false)
        {
            string path = CursorUsagePaths.ResolveSpoolPath(LocalAppData)
                + (rotated ? ".1" : string.Empty);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, JsonSerializer.Serialize(new
            {
                version = 1,
                event_key = eventKey,
                occurred_at_utc = timestamp,
                cursor_version = "3.15.6",
                model,
                input_tokens = input,
                output_tokens = output,
                cache_read_tokens = cacheRead,
                cache_write_tokens = cacheWrite,
            }) + Environment.NewLine);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}

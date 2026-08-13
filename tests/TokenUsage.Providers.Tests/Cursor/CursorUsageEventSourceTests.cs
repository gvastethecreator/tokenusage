using System.Text.Json;
using Microsoft.Data.Sqlite;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;
using TokenUsage.Providers.Cursor;

namespace TokenUsage.Providers.Tests.Cursor;

public sealed class CursorUsageEventSourceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task DatabaseOverTheSizeCapDoesNotReadUsage()
    {
        using var corpus = new CursorCorpus();
        corpus.WriteComposer("conversation-1", 1_786_488_925_618, "composer-2.5", 10_000);
        var source = new CursorUsageEventSource(
            "UTC",
            corpus.Home,
            corpus.Roaming,
            maximumDatabaseBytes: 1);

        UsageSourceReadResult result = await source.ReadAsync();

        Assert.Equal(UsageSourceReadStatus.NoData, result.Status);
        Assert.Equal(UsageSourceIssueKind.AccessBlocked, result.Issue);
        Assert.Empty(result.Events);
    }

    [Fact]
    public async Task MissingCursorAndDatabaseReturnsRootUnavailable()
    {
        using var corpus = new CursorCorpus(createCursorHome: false);
        CursorUsageEventSource source = corpus.CreateSource();

        UsageSourceReadResult result = await source.ReadAsync();

        Assert.False(source.IsRootAvailable);
        Assert.Equal("cursor", source.AgentId.Value);
        Assert.Equal(SourceKind.LocalDatabase, source.SourceKind);
        Assert.Empty(result.Events);
        Assert.Equal(UsageSourceReadStatus.NoData, result.Status);
        Assert.Equal(UsageSourceIssueKind.RootUnavailable, result.Issue);
    }

    [Fact]
    public async Task InstalledCursorWithoutLocalStateReturnsEmpty()
    {
        using var corpus = new CursorCorpus();

        UsageSourceReadResult result = await corpus.CreateSource().ReadAsync();

        Assert.Equal(UsageSourceReadStatus.NoData, result.Status);
        Assert.Equal(UsageSourceIssueKind.Empty, result.Issue);
    }

    [Fact]
    public async Task ReadsAllowlistedComposerEstimateAsCatalogPricedUsage()
    {
        using var corpus = new CursorCorpus();
        corpus.WriteComposer(
            "conversation-1",
            checkpointMilliseconds: 1_786_488_925_618,
            model: "grok-4.5",
            estimatedContextTokens: 222_484,
            privatePrompt: "this content must not become part of the event");
        corpus.WriteRaw("composerData:malformed", "{");

        CursorUsageEventSource source = corpus.CreateSource();
        UsageSourceReadResult result = await source.ReadAsync();
        UsageEvent usageEvent = Assert.Single(result.Events);

        Assert.Equal(SourceKind.LocalDatabase, source.SourceKind);
        Assert.Equal(UsageSourceReadStatus.Partial, result.Status);
        Assert.Equal(UsageSourceIssueKind.PartialScan, result.Issue);
        Assert.Equal("xai", usageEvent.ModelProviderId?.Value);
        Assert.Equal("grok-4.5", usageEvent.ModelId.Value);
        Assert.Equal(new TokenBreakdown(222_484, 0, 0, 0, 0), usageEvent.Tokens);
        Assert.Equal(
            DateTimeOffset.FromUnixTimeMilliseconds(1_786_488_925_618),
            usageEvent.OccurredAtUtc);
        Assert.Equal(CostKind.CatalogEstimated, usageEvent.Cost.Kind);
        Assert.Equal(0.889936m, usageEvent.Cost.EstimatedCostUsd);
        Assert.Equal(CoverageKind.Partial, usageEvent.Coverage);
        Assert.Equal(CursorUsageEventSource.ParserVersion, usageEvent.ParserVersion);
    }

    [Fact]
    public async Task LeavesAutoComposerEstimatesUnpriced()
    {
        using var corpus = new CursorCorpus();
        corpus.WriteComposer("conversation-1", 1_786_488_925_618, "cursor-auto", 90_000);

        UsageEvent usageEvent = Assert.Single((await corpus.CreateSource().ReadAsync()).Events);

        Assert.Equal(CostKind.Unavailable, usageEvent.Cost.Kind);
        Assert.Equal(CoverageKind.Unpriced, usageEvent.Coverage);
        Assert.Equal(90_000, usageEvent.Tokens.Input);
    }

    [Fact]
    public async Task ReplacedComposerSnapshotKeepsIdentityAndUpdatesEstimate()
    {
        using var corpus = new CursorCorpus();
        corpus.WriteComposer("conversation-1", 1_786_488_925_618, "composer-2.5", 10_000);
        UsageEvent first = Assert.Single((await corpus.CreateSource().ReadAsync()).Events);

        corpus.WriteComposer("conversation-1", 1_786_489_325_618, "composer-2.5", 18_000);
        UsageEvent updated = Assert.Single((await corpus.CreateSource().ReadAsync()).Events);

        Assert.Equal(first.EventKey, updated.EventKey);
        Assert.Equal(18_000, updated.Tokens.Input);
        Assert.Equal(0.009m, updated.Cost.EstimatedCostUsd);
        Assert.True(updated.OccurredAtUtc > first.OccurredAtUtc);
        Assert.Equal(
            UsageSourceReadStatus.Complete,
            (await corpus.CreateSource().ReadAsync()).Status);
    }

    [Fact]
    public async Task PrefersRealTurnCountersAndPricesKnownModelsWithoutReadingText()
    {
        using var corpus = new CursorCorpus();
        corpus.WriteComposer("conversation-1", 1_786_488_925_618, "gpt-5", 90_000);
        corpus.WriteBubble(
            "conversation-1",
            "bubble-1",
            "2026-08-12T10:00:00.000Z",
            "gpt-5",
            inputTokens: 1_000_000,
            outputTokens: 100_000,
            privateText: "private prompt and response");

        UsageSourceReadResult result = await corpus.CreateSource().ReadAsync();
        UsageEvent usageEvent = Assert.Single(result.Events);

        Assert.Equal(new TokenBreakdown(1_000_000, 100_000, 0, 0, 0), usageEvent.Tokens);
        Assert.Equal(CostKind.CatalogEstimated, usageEvent.Cost.Kind);
        Assert.Equal(2.25m, usageEvent.Cost.EstimatedCostUsd);
        Assert.Equal(CoverageKind.Partial, usageEvent.Coverage);
        Assert.Equal("openai", usageEvent.ModelProviderId?.Value);
        Assert.Equal(UsageSourceReadStatus.Complete, result.Status);
        Assert.Equal(UsageSourceIssueKind.None, result.Issue);
    }

    [Fact]
    public async Task UnreadableComposerBlobDoesNotBlockCompleteWhenTurnCountersExist()
    {
        using var corpus = new CursorCorpus();
        corpus.WriteComposer("conversation-1", 1_786_488_925_618, "gpt-5", 90_000);
        corpus.WriteRaw("composerData:conversation-1", "{");
        corpus.WriteBubble(
            "conversation-1",
            "bubble-1",
            "2026-08-12T10:00:00.000Z",
            "gpt-5",
            inputTokens: 1_000_000,
            outputTokens: 100_000,
            privateText: "private prompt and response");

        UsageSourceReadResult result = await corpus.CreateSource().ReadAsync();
        UsageEvent usageEvent = Assert.Single(result.Events);

        Assert.Equal(1_100_000, usageEvent.Tokens.Total);
        Assert.Equal(CostKind.CatalogEstimated, usageEvent.Cost.Kind);
        Assert.Equal(2.25m, usageEvent.Cost.EstimatedCostUsd);
    }

    [Fact]
    public async Task RefreshReplacesStoredComposerEstimateWithPricedTurnCounters()
    {
        using var corpus = new CursorCorpus();
        using var folder = new TemporaryFolder();
        var clock = new FixedTimeProvider(
            new DateTimeOffset(2026, 8, 12, 15, 0, 0, TimeSpan.Zero));
        corpus.WriteComposer("conversation-1", 1_786_488_925_618, "gpt-5", 90_000);
        var refresh = new LocalUsageRefresh(
            folder.DatabasePath,
            corpus.CreateSource(),
            clock);

        LocalUsageRefreshResult first = await refresh.RefreshAsync();
        Assert.Equal(90_000, first.Rollups.Sum(rollup => rollup.Tokens.Total));
        Assert.Equal(0.1125m, first.Rollups.Sum(rollup => rollup.EstimatedCostUsd ?? 0m));

        corpus.WriteBubble(
            "conversation-1",
            "bubble-1",
            "2026-08-12T10:00:00.000Z",
            "gpt-5",
            inputTokens: 1_000_000,
            outputTokens: 100_000,
            privateText: "private prompt and response");
        LocalUsageRefreshResult second = await refresh.RefreshAsync();

        Assert.Equal(1, second.Rollups.Sum(rollup => rollup.EventCount));
        Assert.Equal(1_100_000, second.Rollups.Sum(rollup => rollup.Tokens.Total));
        Assert.Equal(2.25m, second.Rollups.Sum(rollup => rollup.EstimatedCostUsd ?? 0m));
    }

    [Fact]
    public async Task PricesComposerTurnsFromTheOfficialGrokRate()
    {
        using var corpus = new CursorCorpus();
        corpus.WriteComposer("conversation-1", 1_786_488_925_618, "composer-2.5", 90_000);
        corpus.WriteBubble(
            "conversation-1",
            "bubble-1",
            "2026-08-12T10:00:00.000Z",
            "composer-2.5",
            inputTokens: 1_000_000,
            outputTokens: 100_000,
            privateText: "private prompt and response");

        UsageEvent usageEvent = Assert.Single((await corpus.CreateSource().ReadAsync()).Events);

        Assert.Equal(CostKind.CatalogEstimated, usageEvent.Cost.Kind);
        Assert.Equal(0.75m, usageEvent.Cost.EstimatedCostUsd);
        Assert.Equal("composer-2.5", usageEvent.Cost.ExactPriceMatch);
        Assert.Equal(CoverageKind.Partial, usageEvent.Coverage);
    }

    [Fact]
    public async Task BuildThatWritesZeroTurnCountersFallsBackToTheConversationEstimate()
    {
        using var corpus = new CursorCorpus();
        corpus.WriteComposer("conversation-1", 1_786_488_925_618, "gpt-5", 90_000);
        for (int index = 0; index < 5; index++)
        {
            corpus.WriteBubble(
                "conversation-1",
                $"bubble-{index}",
                "2026-08-12T10:00:00.000Z",
                "gpt-5",
                inputTokens: 0,
                outputTokens: 0,
                privateText: "private prompt and response");
        }

        UsageSourceReadResult result = await corpus.CreateSource().ReadAsync();
        UsageEvent usageEvent = Assert.Single(result.Events);

        Assert.Equal(90_000, usageEvent.Tokens.Input);
        Assert.Equal(CostKind.CatalogEstimated, usageEvent.Cost.Kind);
        Assert.Equal(0.1125m, usageEvent.Cost.EstimatedCostUsd);
        Assert.Equal(CoverageKind.Partial, usageEvent.Coverage);
        Assert.Equal(UsageSourceReadStatus.Complete, result.Status);
        Assert.Equal(UsageSourceIssueKind.None, result.Issue);
    }

    [Fact]
    public async Task TurnCountersWinOverTheEstimateEvenWhenOtherTurnsReportZero()
    {
        using var corpus = new CursorCorpus();
        corpus.WriteComposer("conversation-1", 1_786_488_925_618, "gpt-5", 90_000);
        corpus.WriteBubble(
            "conversation-1",
            "bubble-empty",
            "2026-08-12T10:00:00.000Z",
            "gpt-5",
            inputTokens: 0,
            outputTokens: 0,
            privateText: "private");
        corpus.WriteBubble(
            "conversation-1",
            "bubble-counted",
            "2026-08-12T10:05:00.000Z",
            "gpt-5",
            inputTokens: 1_000_000,
            outputTokens: 100_000,
            privateText: "private");

        UsageSourceReadResult result = await corpus.CreateSource().ReadAsync();
        UsageEvent usageEvent = Assert.Single(result.Events);

        Assert.Equal(new TokenBreakdown(1_000_000, 100_000, 0, 0, 0), usageEvent.Tokens);
        Assert.Equal(CostKind.CatalogEstimated, usageEvent.Cost.Kind);
    }

    [Fact]
    public async Task SqlCutoffExcludesOldIsoBubbleAndKeepsRecentNumericBubble()
    {
        using var corpus = new CursorCorpus();
        corpus.WriteBubble(
            "old-conversation",
            "old-bubble",
            Now.AddDays(-60).ToString("O"),
            "gpt-5",
            inputTokens: 100,
            outputTokens: 10,
            privateText: "private old content");
        corpus.WriteBubble(
            "recent-conversation",
            "recent-bubble",
            Now.AddDays(-1).ToUnixTimeMilliseconds(),
            "gpt-5",
            inputTokens: 200,
            outputTokens: 20,
            privateText: "private recent content");

        UsageSourceReadResult result = await corpus.CreateSource().ReadAsync();

        UsageEvent usageEvent = Assert.Single(result.Events);
        Assert.Equal(Now.AddDays(-1), usageEvent.OccurredAtUtc);
        Assert.Equal(220, usageEvent.Tokens.Total);
        Assert.Equal(UsageSourceReadStatus.Complete, result.Status);
    }

    [Fact]
    public async Task SqlCutoffExcludesOldIsoComposerAndReadsRecentIsoComposer()
    {
        using var corpus = new CursorCorpus();
        corpus.WriteComposerWithTimestamp(
            "old-conversation",
            Now.AddDays(-60).ToString("O"),
            "gpt-5",
            100);
        corpus.WriteComposerWithTimestamp(
            "recent-conversation",
            Now.AddDays(-1).ToString("O"),
            "gpt-5",
            200);

        UsageSourceReadResult result = await corpus.CreateSource().ReadAsync();

        UsageEvent usageEvent = Assert.Single(result.Events);
        Assert.Equal(Now.AddDays(-1), usageEvent.OccurredAtUtc);
        Assert.Equal(200, usageEvent.Tokens.Total);
        Assert.Equal(UsageSourceReadStatus.Complete, result.Status);
    }

    [Fact]
    public async Task ReadsRecentNumericTextComposerTimestamp()
    {
        using var corpus = new CursorCorpus();
        corpus.WriteComposerWithTimestamp(
            "recent-conversation",
            Now.AddDays(-1).ToUnixTimeMilliseconds().ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            "gpt-5",
            200);

        UsageEvent usageEvent = Assert.Single((await corpus.CreateSource().ReadAsync()).Events);

        Assert.Equal(Now.AddDays(-1), usageEvent.OccurredAtUtc);
        Assert.Equal(200, usageEvent.Tokens.Total);
    }

    [Fact]
    public async Task ReadsRecentRealComposerTimestamp()
    {
        using var corpus = new CursorCorpus();
        corpus.WriteComposerWithTimestamp(
            "recent-conversation",
            (double)Now.AddDays(-1).ToUnixTimeMilliseconds(),
            "gpt-5",
            200);

        UsageEvent usageEvent = Assert.Single((await corpus.CreateSource().ReadAsync()).Events);

        Assert.Equal(Now.AddDays(-1), usageEvent.OccurredAtUtc);
        Assert.Equal(200, usageEvent.Tokens.Total);
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
            Roaming = Path.Combine(Root, "roaming");
            DatabasePath = CursorUsagePaths.ResolveStateDatabasePath(Roaming);
            Directory.CreateDirectory(Home);
            if (createCursorHome)
            {
                Directory.CreateDirectory(Path.Combine(Home, ".cursor"));
            }
        }

        public string Root { get; }

        public string Home { get; }

        public string Roaming { get; }

        public string DatabasePath { get; }

        public CursorUsageEventSource CreateSource() => new(
            "UTC",
            Home,
            Roaming,
            clock: new FixedTimeProvider(Now));

        public void WriteComposer(
            string composerId,
            long checkpointMilliseconds,
            string model,
            long estimatedContextTokens,
            string privatePrompt = "private")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = DatabasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false,
            };
            using var connection = new SqliteConnection(builder.ToString());
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS cursorDiskKV (
                    key TEXT UNIQUE ON CONFLICT REPLACE,
                    value BLOB
                );
                INSERT OR REPLACE INTO cursorDiskKV(key, value)
                VALUES ($key, $value);
                """;
            command.Parameters.AddWithValue("$key", $"composerData:{composerId}");
            command.Parameters.AddWithValue("$value", JsonSerializer.Serialize(new
            {
                conversationCheckpointLastUpdatedAt = checkpointMilliseconds,
                createdAt = checkpointMilliseconds - 10_000,
                modelConfig = new { modelName = model },
                promptTokenBreakdown = new
                {
                    totalUsedTokens = estimatedContextTokens,
                    categories = new[]
                    {
                        new { id = "conversation", estimatedTokens = estimatedContextTokens },
                    },
                },
                contextTokensUsed = estimatedContextTokens,
                text = privatePrompt,
                usageData = new { },
            }));
            command.ExecuteNonQuery();
        }

        public void WriteComposerWithTimestamp(
            string composerId,
            object timestamp,
            string model,
            long estimatedContextTokens) =>
            WriteRaw($"composerData:{composerId}", JsonSerializer.Serialize(new
            {
                conversationCheckpointLastUpdatedAt = timestamp,
                createdAt = timestamp,
                modelConfig = new { modelName = model },
                promptTokenBreakdown = new { totalUsedTokens = estimatedContextTokens },
                contextTokensUsed = estimatedContextTokens,
                text = "private",
            }));

        public void WriteRaw(string key, string value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = DatabasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false,
            };
            using var connection = new SqliteConnection(builder.ToString());
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS cursorDiskKV (
                    key TEXT UNIQUE ON CONFLICT REPLACE,
                    value BLOB
                );
                INSERT OR REPLACE INTO cursorDiskKV(key, value)
                VALUES ($key, $value);
                """;
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$value", value);
            command.ExecuteNonQuery();
        }

        public void WriteBubble(
            string composerId,
            string bubbleId,
            object timestamp,
            string model,
            long inputTokens,
            long outputTokens,
            string privateText)
        {
            WriteRaw($"bubbleId:{composerId}:{bubbleId}", JsonSerializer.Serialize(new
            {
                createdAt = timestamp,
                modelInfo = new { modelName = model },
                tokenCount = new { inputTokens, outputTokens },
                text = privateText,
            }));
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }

    private sealed class TemporaryFolder : IDisposable
    {
        public TemporaryFolder()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "tokenusage-cursor-refresh-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            DatabasePath = Path.Combine(Root, "usage.v1.db");
        }

        public string Root { get; }

        public string DatabasePath { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
                }
            }
            catch
            {
                // best effort
            }
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow.ToUniversalTime();
    }
}

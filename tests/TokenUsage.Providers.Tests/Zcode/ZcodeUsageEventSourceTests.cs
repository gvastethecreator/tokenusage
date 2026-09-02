using Microsoft.Data.Sqlite;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;
using TokenUsage.Providers.Zcode;

namespace TokenUsage.Providers.Tests.Zcode;

public sealed class ZcodeUsageEventSourceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void DefaultDatabaseSizeCapAllowsCurrentUsageDatabases()
    {
        Assert.True(
            ZcodeUsageEventSource.DefaultMaximumDatabaseBytes
            >= 32L * 1024 * 1024 * 1024);
    }

    [Fact]
    public async Task MissingZcodeRootReturnsRootUnavailable()
    {
        using var corpus = new ZcodeCorpus(createZcodeHome: false);
        ZcodeUsageEventSource source = corpus.CreateSource();

        UsageSourceReadResult result = await source.ReadAsync();

        Assert.False(source.IsRootAvailable);
        Assert.Equal("zcode", source.AgentId.Value);
        Assert.Equal(SourceKind.LocalDatabase, source.SourceKind);
        Assert.Empty(result.Events);
        Assert.Equal(UsageSourceReadStatus.NoData, result.Status);
        Assert.Equal(UsageSourceIssueKind.RootUnavailable, result.Issue);
    }

    [Fact]
    public async Task InstalledZcodeWithoutUsageDatabaseReturnsEmpty()
    {
        using var corpus = new ZcodeCorpus();

        UsageSourceReadResult result = await corpus.CreateSource().ReadAsync();

        Assert.True(corpus.CreateSource().IsRootAvailable);
        Assert.Equal(UsageSourceReadStatus.NoData, result.Status);
        Assert.Equal(UsageSourceIssueKind.Empty, result.Issue);
    }

    [Fact]
    public async Task DatabaseOverTheSizeCapDoesNotReadUsage()
    {
        using var corpus = new ZcodeCorpus();
        corpus.WriteUsage("usage_model_a_1", Now.AddDays(-1), "GLM-5.3",
            inputTokens: 246, outputTokens: 13);
        var source = new ZcodeUsageEventSource(
            "UTC",
            homeDirectory: corpus.Home,
            maximumDatabaseBytes: 1);

        UsageSourceReadResult result = await source.ReadAsync();

        Assert.Equal(UsageSourceReadStatus.NoData, result.Status);
        Assert.Equal(UsageSourceIssueKind.AccessBlocked, result.Issue);
        Assert.Empty(result.Events);
    }

    [Fact]
    public async Task ReadsAllowlistedPerRequestCountersAsCatalogPricedUsage()
    {
        using var corpus = new ZcodeCorpus();
        corpus.WriteUsage(
            "usage_model_message-1_0",
            Now.AddDays(-1),
            "GLM-5.3",
            inputTokens: 246,
            outputTokens: 13,
            reasoningTokens: 0,
            cacheWriteTokens: 0,
            cacheReadTokens: 192);

        UsageSourceReadResult result = await corpus.CreateSource().ReadAsync();
        UsageEvent usageEvent = Assert.Single(result.Events);

        Assert.Equal(UsageSourceReadStatus.Complete, result.Status);
        Assert.Equal(UsageSourceIssueKind.None, result.Issue);
        Assert.Equal("zai", usageEvent.ModelProviderId?.Value);
        Assert.Equal("glm-5.3", usageEvent.ModelId.Value);
        Assert.Equal(new TokenBreakdown(54, 13, 0, 192, 0), usageEvent.Tokens);
        Assert.Equal(Now.AddDays(-1), usageEvent.OccurredAtUtc);
        Assert.Equal(CostKind.CatalogEstimated, usageEvent.Cost.Kind);
        Assert.Equal(0.000183m, usageEvent.Cost.EstimatedCostUsd);
        Assert.Equal("zai-api-2026-09-02", usageEvent.Cost.CatalogVersion);
        Assert.Equal("glm-5.3", usageEvent.Cost.ExactPriceMatch);
        Assert.Equal(CoverageKind.Partial, usageEvent.Coverage);
        Assert.Equal(ZcodeUsageEventSource.ParserVersion, usageEvent.ParserVersion);
    }

    [Fact]
    public async Task SplitsReasoningFromOutputAndBothCacheCountersFromInput()
    {
        using var corpus = new ZcodeCorpus();
        corpus.WriteUsage(
            "usage_model_message-2_0",
            Now.AddHours(-2),
            "GLM-5.3",
            inputTokens: 1_000,
            outputTokens: 500,
            reasoningTokens: 200,
            cacheWriteTokens: 100,
            cacheReadTokens: 400);

        UsageEvent usageEvent = Assert.Single((await corpus.CreateSource().ReadAsync()).Events);

        Assert.Equal(new TokenBreakdown(500, 300, 200, 400, 100), usageEvent.Tokens);
        Assert.Equal(1_500, usageEvent.Tokens.Total);
        Assert.Equal(CostKind.CatalogEstimated, usageEvent.Cost.Kind);
        Assert.Equal(0.003144m, usageEvent.Cost.EstimatedCostUsd);
    }

    [Fact]
    public async Task UnknownModelLeavesCostUnavailableAndUnpriced()
    {
        using var corpus = new ZcodeCorpus();
        corpus.WriteUsage(
            "usage_model_message-3_0",
            Now.AddDays(-1),
            "GLM-9.9",
            inputTokens: 10_000,
            outputTokens: 1_000);

        UsageEvent usageEvent = Assert.Single((await corpus.CreateSource().ReadAsync()).Events);

        Assert.Equal("glm-9.9", usageEvent.ModelId.Value);
        Assert.Equal(CostKind.Unavailable, usageEvent.Cost.Kind);
        Assert.Equal(CoverageKind.Unpriced, usageEvent.Coverage);
        Assert.Equal(11_000, usageEvent.Tokens.Total);
    }

    [Fact]
    public async Task NullModelCountsUnderTheUnknownModel()
    {
        using var corpus = new ZcodeCorpus();
        corpus.WriteUsage(
            "usage_model_message-4_0",
            Now.AddDays(-1),
            modelId: null,
            inputTokens: 10_000,
            outputTokens: 1_000);

        UsageEvent usageEvent = Assert.Single((await corpus.CreateSource().ReadAsync()).Events);

        Assert.Equal("unknown", usageEvent.ModelId.Value);
        Assert.Equal(CostKind.Unavailable, usageEvent.Cost.Kind);
        Assert.Equal(CoverageKind.Unpriced, usageEvent.Coverage);
    }

    [Fact]
    public async Task PricesTurboRequestsFromTheOfficialZaiRate()
    {
        using var corpus = new ZcodeCorpus();
        corpus.WriteUsage(
            "usage_model_message-5_0",
            Now.AddDays(-1),
            "GLM-5-Turbo",
            inputTokens: 2_000_000,
            outputTokens: 1_000_000,
            reasoningTokens: 0,
            cacheWriteTokens: 0,
            cacheReadTokens: 1_000_000);

        UsageEvent usageEvent = Assert.Single((await corpus.CreateSource().ReadAsync()).Events);

        Assert.Equal("glm-5-turbo", usageEvent.ModelId.Value);
        Assert.Equal(CostKind.CatalogEstimated, usageEvent.Cost.Kind);
        Assert.Equal(5.44m, usageEvent.Cost.EstimatedCostUsd);
        Assert.Equal("glm-5-turbo", usageEvent.Cost.ExactPriceMatch);
    }

    [Fact]
    public async Task ReplacedRowKeepsIdentityAndUpdatesCounters()
    {
        using var corpus = new ZcodeCorpus();
        corpus.WriteUsage("usage_model_message-6_0", Now.AddDays(-1), "GLM-5.3",
            inputTokens: 1_000, outputTokens: 100);
        UsageEvent first = Assert.Single((await corpus.CreateSource().ReadAsync()).Events);

        corpus.WriteUsage("usage_model_message-6_0", Now.AddDays(-1), "GLM-5.3",
            inputTokens: 4_000, outputTokens: 400);
        UsageSourceReadResult updated = await corpus.CreateSource().ReadAsync();

        UsageEvent usageEvent = Assert.Single(updated.Events);
        Assert.Equal(first.EventKey, usageEvent.EventKey);
        Assert.Equal(4_400, usageEvent.Tokens.Total);
        Assert.Equal(UsageSourceReadStatus.Complete, updated.Status);
    }

    [Fact]
    public async Task SqlCutoffExcludesOldRowsAndKeepsRecentOnes()
    {
        using var corpus = new ZcodeCorpus();
        corpus.WriteUsage("usage_model_old_0", Now.AddDays(-60), "GLM-5.3",
            inputTokens: 100, outputTokens: 10);
        corpus.WriteUsage("usage_model_recent_0", Now.AddDays(-1), "GLM-5.2",
            inputTokens: 200, outputTokens: 20);

        UsageSourceReadResult result = await corpus.CreateSource().ReadAsync();

        UsageEvent usageEvent = Assert.Single(result.Events);
        Assert.Equal(Now.AddDays(-1), usageEvent.OccurredAtUtc);
        Assert.Equal("glm-5.2", usageEvent.ModelId.Value);
        Assert.Equal(UsageSourceReadStatus.Complete, result.Status);
    }

    [Fact]
    public async Task RowCapKeepsTheNewestRequestsAndMarksTheScanPartial()
    {
        using var corpus = new ZcodeCorpus();
        corpus.WriteUsage("usage_model_older_0", Now.AddDays(-3), "GLM-5.3",
            inputTokens: 100, outputTokens: 10);
        corpus.WriteUsage("usage_model_newer_0", Now.AddDays(-1), "GLM-5.3",
            inputTokens: 200, outputTokens: 20);
        var source = new ZcodeUsageEventSource(
            "UTC",
            homeDirectory: corpus.Home,
            maximumRows: 1,
            clock: new FixedTimeProvider(Now));

        UsageSourceReadResult result = await source.ReadAsync();

        UsageEvent usageEvent = Assert.Single(result.Events);
        Assert.Equal(Now.AddDays(-1), usageEvent.OccurredAtUtc);
        Assert.Equal(220, usageEvent.Tokens.Total);
        Assert.Equal(UsageSourceReadStatus.Partial, result.Status);
        Assert.Equal(UsageSourceIssueKind.PartialScan, result.Issue);
    }

    [Fact]
    public async Task MissingRequiredColumnReportsUnsupportedSchema()
    {
        using var corpus = new ZcodeCorpus();
        corpus.WriteUsage("usage_model_message-7_0", Now.AddDays(-1), "GLM-5.3",
            inputTokens: 1_000, outputTokens: 100);
        corpus.DropColumn("reasoning_tokens");

        UsageSourceReadResult result = await corpus.CreateSource().ReadAsync();

        Assert.Equal(UsageSourceReadStatus.NoData, result.Status);
        Assert.Equal(UsageSourceIssueKind.UnsupportedSchema, result.Issue);
        Assert.Empty(result.Events);
    }

    [Fact]
    public async Task ZeroTokenRowsReturnEmpty()
    {
        using var corpus = new ZcodeCorpus();
        corpus.WriteUsage("usage_model_zero_0", Now.AddDays(-1), "GLM-5.3",
            inputTokens: 0, outputTokens: 0);

        UsageSourceReadResult result = await corpus.CreateSource().ReadAsync();

        Assert.Equal(UsageSourceReadStatus.NoData, result.Status);
        Assert.Equal(UsageSourceIssueKind.Empty, result.Issue);
        Assert.Empty(result.Events);
    }

    [Fact]
    public async Task OversizedCountersAreRejected()
    {
        using var corpus = new ZcodeCorpus();
        corpus.WriteUsage("usage_model_huge_0", Now.AddDays(-1), "GLM-5.3",
            inputTokens: 40_000_000, outputTokens: 100);

        UsageSourceReadResult result = await corpus.CreateSource().ReadAsync();

        Assert.Equal(UsageSourceReadStatus.NoData, result.Status);
        Assert.Equal(UsageSourceIssueKind.AccessBlocked, result.Issue);
    }

    [Fact]
    public async Task RefreshReplacesStoredCountersOnTheNextScan()
    {
        using var corpus = new ZcodeCorpus();
        using var folder = new TemporaryFolder();
        var clock = new FixedTimeProvider(Now);
        corpus.WriteUsage("usage_model_message-8_0", Now.AddDays(-1), "GLM-5.3",
            inputTokens: 1_000, outputTokens: 100);
        var refresh = new LocalUsageRefresh(
            folder.DatabasePath,
            corpus.CreateSource(),
            clock);

        LocalUsageRefreshResult first = await refresh.RefreshAsync();
        Assert.Equal(1_100, first.Rollups.Sum(rollup => rollup.Tokens.Total));
        Assert.Equal(0.00184m, first.Rollups.Sum(rollup => rollup.EstimatedCostUsd ?? 0m));

        corpus.WriteUsage("usage_model_message-8_0", Now.AddDays(-1), "GLM-5.3",
            inputTokens: 2_000, outputTokens: 200);
        LocalUsageRefreshResult second = await refresh.RefreshAsync();

        Assert.Equal(1, second.Rollups.Sum(rollup => rollup.EventCount));
        Assert.Equal(2_200, second.Rollups.Sum(rollup => rollup.Tokens.Total));
        Assert.Equal(0.00368m, second.Rollups.Sum(rollup => rollup.EstimatedCostUsd ?? 0m));
    }

    private sealed class ZcodeCorpus : IDisposable
    {
        public ZcodeCorpus(bool createZcodeHome = true)
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "tokenusage-zcode-source-tests",
                Guid.NewGuid().ToString("N"));
            Home = Path.Combine(Root, "home");
            ZcodeHome = ZcodeUsagePaths.ResolveZcodeHome(Home);
            DatabasePath = ZcodeUsagePaths.ResolveDatabasePath(ZcodeHome);
            Directory.CreateDirectory(Home);
            if (createZcodeHome)
            {
                Directory.CreateDirectory(ZcodeHome);
            }
        }

        public string Root { get; }

        public string Home { get; }

        public string ZcodeHome { get; }

        public string DatabasePath { get; }

        public ZcodeUsageEventSource CreateSource() => new(
            "UTC",
            homeDirectory: Home,
            clock: new FixedTimeProvider(Now));

        public void WriteUsage(
            string id,
            DateTimeOffset startedAt,
            string? modelId,
            long inputTokens,
            long outputTokens,
            long reasoningTokens = 0,
            long cacheWriteTokens = 0,
            long cacheReadTokens = 0)
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
            // Neighbouring columns and tables hold private content. The reader's
            // allowlist must keep pulling only the numeric counters above.
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS model_usage (
                    id TEXT PRIMARY KEY,
                    logical_request_id TEXT,
                    attempt_index INTEGER,
                    session_id TEXT,
                    turn_id TEXT,
                    provider_id TEXT,
                    model_id TEXT,
                    status TEXT,
                    started_at INTEGER,
                    completed_at INTEGER,
                    error_message TEXT,
                    raw_usage_json TEXT,
                    input_tokens INTEGER,
                    output_tokens INTEGER,
                    reasoning_tokens INTEGER,
                    cache_creation_input_tokens INTEGER,
                    cache_read_input_tokens INTEGER,
                    computed_total_tokens INTEGER
                );
                CREATE TABLE IF NOT EXISTS part (
                    id TEXT PRIMARY KEY,
                    data TEXT
                );
                INSERT OR REPLACE INTO part(id, data)
                VALUES ('part-1', 'private transcript content that must never be read');
                INSERT OR REPLACE INTO model_usage(
                    id, logical_request_id, attempt_index, session_id, provider_id,
                    model_id, status, started_at, completed_at, error_message,
                    raw_usage_json, input_tokens, output_tokens, reasoning_tokens,
                    cache_creation_input_tokens, cache_read_input_tokens, computed_total_tokens)
                VALUES (
                    $id, 'req-1', 0, 'sess-1', 'builtin:zai-coding-plan',
                    $model_id, 'completed', $started_at, $started_at, 'private failure text',
                    '{"inputTokens":1}', $input_tokens, $output_tokens, $reasoning_tokens,
                    $cache_write_tokens, $cache_read_tokens,
                    $input_tokens + $output_tokens);
                """;
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$model_id", modelId is null ? DBNull.Value : modelId);
            command.Parameters.AddWithValue("$started_at", startedAt.ToUnixTimeMilliseconds());
            command.Parameters.AddWithValue("$input_tokens", inputTokens);
            command.Parameters.AddWithValue("$output_tokens", outputTokens);
            command.Parameters.AddWithValue("$reasoning_tokens", reasoningTokens);
            command.Parameters.AddWithValue("$cache_write_tokens", cacheWriteTokens);
            command.Parameters.AddWithValue("$cache_read_tokens", cacheReadTokens);
            command.ExecuteNonQuery();
        }

        public void DropColumn(string columnName)
        {
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = DatabasePath,
                Mode = SqliteOpenMode.ReadWrite,
                Pooling = false,
            };
            using var connection = new SqliteConnection(builder.ToString());
            connection.Open();
            List<string> columns = [];
            using (SqliteCommand list = connection.CreateCommand())
            {
                list.CommandText = "PRAGMA table_info(model_usage)";
                using SqliteDataReader reader = list.ExecuteReader();
                while (reader.Read())
                {
                    columns.Add(reader.GetString(1));
                }
            }

            string recreated = string.Join(", ", columns
                .Where(column => !string.Equals(column, columnName, StringComparison.Ordinal))
                .Select(column => column switch
                {
                    "id" => "id TEXT PRIMARY KEY",
                    "model_id" => "model_id TEXT",
                    _ => $"{column} INTEGER",
                }));
            using SqliteCommand command = connection.CreateCommand();
            // SQLite rebuilds the table to drop a column: rename, recreate
            // without it, copy, and remove the old copy.
            command.CommandText = "ALTER TABLE model_usage RENAME TO model_usage_old; "
                + $"CREATE TABLE model_usage ({recreated}); "
                + "INSERT INTO model_usage SELECT "
                + string.Join(", ", columns
                    .Where(column => !string.Equals(column, columnName, StringComparison.Ordinal)))
                + " FROM model_usage_old; DROP TABLE model_usage_old;";
            command.ExecuteNonQuery();
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
                "tokenusage-zcode-refresh-tests",
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

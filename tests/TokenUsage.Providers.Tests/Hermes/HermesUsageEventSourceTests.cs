using Microsoft.Data.Sqlite;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;
using TokenUsage.Providers.Hermes;

namespace TokenUsage.Providers.Tests.Hermes;

public sealed class HermesUsageEventSourceTests
{
    [Fact]
    public async Task ReadsAggregateSessionColumnsWithoutMessagesTable()
    {
        using var folder = new TemporaryFolder();
        string databasePath = Path.Combine(folder.Path, "state.db");
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        };
        using (var connection = new SqliteConnection(builder.ToString()))
        {
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE sessions (
                    id TEXT PRIMARY KEY,
                    model TEXT,
                    billing_provider TEXT,
                    input_tokens INTEGER,
                    output_tokens INTEGER,
                    reasoning_tokens INTEGER,
                    cache_read_tokens INTEGER,
                    cache_write_tokens INTEGER,
                    actual_cost_usd REAL,
                    started_at INTEGER,
                    ended_at INTEGER,
                    title TEXT
                );
                INSERT INTO sessions VALUES (
                    'session-private-id',
                    'claude-sonnet-4-6',
                    'anthropic',
                    100,
                    20,
                    5,
                    30,
                    10,
                    0.125,
                    1786528800,
                    1786528860,
                    'private title'
                );
                INSERT INTO sessions (id, input_tokens, output_tokens, started_at)
                VALUES ('empty-session', 0, 0, 1786528800);
                """;
            command.ExecuteNonQuery();
        }

        var source = new HermesUsageEventSource(
            "UTC",
            hermesHomeOverride: folder.Path);
        UsageSourceReadResult result = await source.ReadAsync();
        UsageEvent usageEvent = Assert.Single(result.Events);

        Assert.True(source.IsRootAvailable);

        Assert.Equal(UsageSourceReadStatus.Complete, result.Status);
        Assert.Equal("hermes", usageEvent.AgentId.Value);
        Assert.Equal("anthropic", usageEvent.ModelProviderId?.Value);
        Assert.Equal(new TokenBreakdown(100, 15, 5, 30, 10), usageEvent.Tokens);
        Assert.Equal(CostKind.ProviderReported, usageEvent.Cost.Kind);
        Assert.Equal(0.125m, usageEvent.Cost.ReportedCostUsd);
        Assert.DoesNotContain("session-private-id", usageEvent.EventKey.Value, StringComparison.Ordinal);

        using var corruptConnection = new SqliteConnection(builder.ToString());
        corruptConnection.Open();
        using SqliteCommand corrupt = corruptConnection.CreateCommand();
        corrupt.CommandText = "UPDATE sessions SET input_tokens = -1 WHERE id = 'empty-session';";
        corrupt.ExecuteNonQuery();
        Assert.Equal(UsageSourceReadStatus.Partial, (await source.ReadAsync()).Status);
        corrupt.CommandText = "UPDATE sessions SET input_tokens = 0, output_tokens = 'invalid' WHERE id = 'empty-session';";
        corrupt.ExecuteNonQuery();
        UsageSourceReadResult malformed = await source.ReadAsync();
        Assert.Equal(UsageSourceReadStatus.Partial, malformed.Status);
        Assert.Equal(usageEvent.EventKey, Assert.Single(malformed.Events).EventKey);
        corrupt.CommandText = "UPDATE sessions SET output_tokens = 0, actual_cost_usd = 0.5 WHERE id = 'empty-session';";
        corrupt.ExecuteNonQuery();
        UsageSourceReadResult costOnly = await source.ReadAsync();
        Assert.Equal(UsageSourceReadStatus.Complete, costOnly.Status);
        UsageEvent charged = Assert.Single(costOnly.Events, row => row.Tokens.Total == 0);
        Assert.Equal(0.5m, charged.Cost.ReportedCostUsd);
    }

    [Fact]
    public async Task ModelRoutesAndAuxiliaryUsageReplaceTheStoredAggregateWhileResidualCanGrowAgain()
    {
        using var folder = new TemporaryFolder();
        string databasePath = Path.Combine(folder.Path, "state.db");
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        };
        using var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE sessions (
                id TEXT PRIMARY KEY, model TEXT, billing_provider TEXT,
                input_tokens INTEGER, output_tokens INTEGER, reasoning_tokens INTEGER,
                started_at INTEGER
            );
            INSERT INTO sessions VALUES ('changing', 'gpt-6.1-sol', 'openai', 150, 30, 6, 1786528800);
            INSERT INTO sessions VALUES ('unrelated', 'unknown', NULL, 5, 0, 0, 1786528800);
            """;
        command.ExecuteNonQuery();
        var source = new HermesUsageEventSource("UTC", hermesHomeOverride: folder.Path);
        UsageRepository repository = await UsageRepository.OpenAsync(Path.Combine(folder.Path, "usage.db"));
        UsageSourceReadResult baseline = await source.ReadAsync();
        UsageEvent original = Assert.Single(baseline.Events, row => row.ModelId.Value == "gpt-6.1-sol");
        // Seed the previous parser's inclusive-output observation, as an existing installation has it.
        var oldAggregate = new UsageEvent(original.EventKey, original.AgentId, original.ModelProviderId,
            original.ModelId, original.OccurredAtUtc, "UTC", new TokenBreakdown(150, 30, 6, 0, 0),
            original.Cost, "hermes-state/3", original.Coverage);
        await repository.UpsertAgentEventsAsync(source.AgentId,
            baseline.Events.Select(row => row.EventKey == original.EventKey ? oldAggregate : row));

        command.CommandText = """
            CREATE TABLE session_model_usage (
                session_id TEXT, model TEXT, billing_provider TEXT, billing_base_url TEXT,
                billing_mode TEXT, task TEXT, input_tokens INTEGER, output_tokens INTEGER,
                reasoning_tokens INTEGER, cache_read_tokens INTEGER, cache_write_tokens INTEGER,
                actual_cost_usd REAL, first_seen REAL, last_seen REAL,
                PRIMARY KEY (session_id, model, billing_provider, billing_base_url, billing_mode, task)
            );
            INSERT INTO session_model_usage VALUES
              ('changing', 'gpt-6.1-sol', 'openai', '', '', '', 100, 20, 4, 0, 0, 0, 1786528800, 1786528860),
              ('changing', 'gemini-2.5-pro', 'google', '', '', '', 50, 10, 2, 0, 0, 0, 1786528800, 1786528860),
              ('changing', 'gemini-3.6-flash', 'google', '', '', 'title_generation', 10, 2, 0, 0, 0, 0,
                1786528800, 1786528860);
            """;
        command.ExecuteNonQuery();
        UsageSourceReadResult detailed = await source.ReadAsync();
        Assert.Equal(UsageSourceReadStatus.Complete, detailed.Status);
        Assert.Equal(5, detailed.Events.Count);
        UsageEvent residual = Assert.Single(detailed.Events, row => row.EventKey == original.EventKey);
        Assert.Equal(0, residual.Tokens.Total);
        Assert.Equal(CoverageKind.Partial, residual.Coverage);
        Assert.Equal(12, Assert.Single(detailed.Events, row => row.ModelId.Value == "gemini-3.6-flash").Tokens.Total);
        await repository.UpsertAgentEventsAsync(source.AgentId, detailed.Events);
        IReadOnlyList<DailyUsageRollup> rollups = await repository.QueryDailyRollupsAsync(
            new DateOnly(2026, 8, 12), new DateOnly(2026, 8, 12));
        Assert.Equal(197, rollups.Sum(row => row.Tokens.Total));

        command.CommandText = "UPDATE sessions SET input_tokens = 200, output_tokens = 40, reasoning_tokens = 8 WHERE id = 'changing';";
        command.ExecuteNonQuery();
        UsageSourceReadResult grown = await source.ReadAsync();
        Assert.Equal(detailed.Events.Select(row => row.EventKey), grown.Events.Select(row => row.EventKey));
        Assert.Equal(new TokenBreakdown(50, 8, 2, 0, 0),
            Assert.Single(grown.Events, row => row.EventKey == original.EventKey).Tokens);
        await repository.UpsertAgentEventsAsync(source.AgentId, grown.Events);
        await repository.UpsertAgentEventsAsync(source.AgentId, grown.Events);
        rollups = await repository.QueryDailyRollupsAsync(new DateOnly(2026, 8, 12), new DateOnly(2026, 8, 12));
        Assert.Equal(257, rollups.Sum(row => row.Tokens.Total));

        // Removing a different source session is not authority to erase collected history.
        command.CommandText = "DELETE FROM sessions WHERE id = 'unrelated';";
        command.ExecuteNonQuery();
        await repository.UpsertAgentEventsAsync(source.AgentId, (await source.ReadAsync()).Events);
        rollups = await repository.QueryDailyRollupsAsync(new DateOnly(2026, 8, 12), new DateOnly(2026, 8, 12));
        Assert.Equal(257, rollups.Sum(row => row.Tokens.Total));

        // voice_chat is included in sessions, unlike title_generation above.
        command.CommandText = """
            INSERT INTO session_model_usage VALUES
              ('changing', 'gemini-2.5-pro', 'google', '', '', 'voice_chat', 20, 4, 0, 0, 0, 0,
                1786528800, 1786528860);
            UPDATE sessions SET input_tokens = 220, output_tokens = 44 WHERE id = 'changing';
            """;
        command.ExecuteNonQuery();
        UsageSourceReadResult withVoice = await source.ReadAsync();
        Assert.Equal(UsageSourceReadStatus.Complete, withVoice.Status);
        Assert.Equal(new TokenBreakdown(50, 8, 2, 0, 0),
            Assert.Single(withVoice.Events, row => row.EventKey == original.EventKey).Tokens);
        await repository.UpsertAgentEventsAsync(source.AgentId, withVoice.Events);
        rollups = await repository.QueryDailyRollupsAsync(new DateOnly(2026, 8, 12), new DateOnly(2026, 8, 12));
        Assert.Equal(281, rollups.Sum(row => row.Tokens.Total));

        // Residual normalization must not hide corrupt source counters behind MAX(0, ...).
        command.CommandText = "UPDATE sessions SET input_tokens = -1 WHERE id = 'changing';";
        command.ExecuteNonQuery();
        Assert.Equal(UsageSourceReadStatus.Partial, (await source.ReadAsync()).Status);
    }

    [Fact]
    public async Task MissingRootReturnsRootUnavailable()
    {
        using var folder = new TemporaryFolder();
        string missing = Path.Combine(folder.Path, "missing");
        var source = new HermesUsageEventSource(
            "UTC",
            hermesHomeOverride: missing);

        UsageSourceReadResult result = await source.ReadAsync();

        Assert.False(source.IsRootAvailable);
        Assert.Equal(UsageSourceIssueKind.RootUnavailable, result.Issue);
        Assert.Empty(result.Events);
    }

    [Fact]
    public async Task HomeFolderWithoutStateDatabaseIsNotAnInstall()
    {
        using var folder = new TemporaryFolder();
        Directory.CreateDirectory(Path.Combine(folder.Path, "skills", "other-tool"));
        File.WriteAllText(Path.Combine(folder.Path, "skills", "other-tool", "SKILL.md"), "x");
        var source = new HermesUsageEventSource(
            "UTC",
            hermesHomeOverride: folder.Path);

        UsageSourceReadResult result = await source.ReadAsync();

        Assert.False(source.IsRootAvailable);
        Assert.Equal(UsageSourceIssueKind.RootUnavailable, result.Issue);
        Assert.Empty(result.Events);
    }

    private sealed class TemporaryFolder : IDisposable
    {
        public TemporaryFolder()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "tokenusage-hermes-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}

using Microsoft.Data.Sqlite;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;
using TokenUsage.Providers.Goose;

namespace TokenUsage.Providers.Tests.Goose;

public sealed class GooseUsageEventSourceTests
{
    [Fact]
    public async Task ReadsOnlyAggregateSessionMetrics()
    {
        using var folder = new TemporaryFolder();
        string databasePath = Path.Combine(folder.Path, "sessions.db");
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
                    created_at TEXT,
                    model_config_json TEXT,
                    provider_name TEXT,
                    accumulated_input_tokens INTEGER,
                    accumulated_output_tokens INTEGER,
                    accumulated_total_tokens INTEGER,
                    accumulated_cache_read_tokens INTEGER,
                    accumulated_cache_write_tokens INTEGER,
                    accumulated_cost REAL,
                    private_conversation TEXT
                );
                INSERT INTO sessions VALUES (
                    'session-1',
                    '2026-08-12T10:00:00.0000000+00:00',
                    '{"model_name":"claude-sonnet-4-6","private":"do not retain"}',
                    'anthropic',
                    100,
                    20,
                    130,
                    30,
                    10,
                    0.25,
                    'private prompt and response'
                );
                INSERT INTO sessions (id, created_at, model_config_json,
                    accumulated_input_tokens, accumulated_output_tokens, accumulated_total_tokens)
                VALUES ('empty-session', '2026-08-12T10:00:00.0000000+00:00',
                    '{"model_name":"claude-sonnet-4-6"}', 0, 0, 0);
                """;
            command.ExecuteNonQuery();
        }

        var source = new GooseUsageEventSource(
            "UTC",
            databasePathOverride: databasePath);
        UsageSourceReadResult result = await source.ReadAsync();
        UsageEvent usageEvent = Assert.Single(result.Events);

        Assert.Equal(UsageSourceReadStatus.Complete, result.Status);
        Assert.Equal("goose", usageEvent.AgentId.Value);
        Assert.Equal("anthropic", usageEvent.ModelProviderId?.Value);
        Assert.Equal(new TokenBreakdown(60, 20, 10, 30, 10), usageEvent.Tokens);
        Assert.Equal(CostKind.ProviderReported, usageEvent.Cost.Kind);
        Assert.Equal(0.25m, usageEvent.Cost.ReportedCostUsd);
        Assert.Equal(CoverageKind.Complete, usageEvent.Coverage);

        using var corruptConnection = new SqliteConnection(builder.ToString());
        corruptConnection.Open();
        using SqliteCommand corrupt = corruptConnection.CreateCommand();
        corrupt.CommandText = "UPDATE sessions SET accumulated_input_tokens = -1 WHERE id = 'empty-session';";
        corrupt.ExecuteNonQuery();
        Assert.Equal(UsageSourceReadStatus.Partial, (await source.ReadAsync()).Status);
        corrupt.CommandText = """
            UPDATE sessions SET accumulated_input_tokens = 0, accumulated_output_tokens = 'invalid'
            WHERE id = 'empty-session';
            """;
        corrupt.ExecuteNonQuery();
        UsageSourceReadResult malformed = await source.ReadAsync();
        Assert.Equal(UsageSourceReadStatus.Partial, malformed.Status);
        Assert.Equal(usageEvent.EventKey, Assert.Single(malformed.Events).EventKey);
        corrupt.CommandText = "UPDATE sessions SET accumulated_output_tokens = 0, accumulated_cost = 0.5 WHERE id = 'empty-session';";
        corrupt.ExecuteNonQuery();
        UsageSourceReadResult costOnly = await source.ReadAsync();
        Assert.Equal(UsageSourceReadStatus.Complete, costOnly.Status);
        UsageEvent charged = Assert.Single(costOnly.Events, row => row.Tokens.Total == 0);
        Assert.Equal(0.5m, charged.Cost.ReportedCostUsd);
    }

    [Theory]
    [InlineData("gemini-3.6-flash", 1000000, 100000, 1.125)]
    [InlineData("gpt-6.1-sol", 300000, 0, null)]
    [InlineData("gemini-2.5-pro", 300000, 0, null)]
    public async Task DottedModelIdsAndAggregateContextCostsStayHonest(
        string model, long input, long output, double? expectedCost)
    {
        using var folder = new TemporaryFolder();
        string databasePath = Path.Combine(folder.Path, "sessions.db");
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
                    created_at TEXT,
                    model_config_json TEXT,
                    provider_name TEXT,
                    accumulated_input_tokens INTEGER,
                    accumulated_output_tokens INTEGER,
                    accumulated_total_tokens INTEGER,
                    accumulated_cost REAL
                );
                INSERT INTO sessions VALUES (
                    'session-gemini',
                    '2026-08-12T10:00:00.0000000+00:00',
                    $model,
                    $provider,
                    $input,
                    $output,
                    $total,
                    0
                );
                """;
            command.Parameters.AddWithValue("$model", $"{{\"model_name\":\"{model}\"}}");
            command.Parameters.AddWithValue("$provider", model.StartsWith("gpt-", StringComparison.Ordinal) ? "openai" : "google");
            command.Parameters.AddWithValue("$input", input);
            command.Parameters.AddWithValue("$output", output);
            command.Parameters.AddWithValue("$total", input + output);
            command.ExecuteNonQuery();
        }

        UsageEvent usageEvent = Assert.Single(
            (await new GooseUsageEventSource("UTC", databasePathOverride: databasePath)
                .ReadAsync()).Events);

        Assert.Equal(model, usageEvent.ModelId.Value);
        Assert.Equal(expectedCost.HasValue ? CostKind.CatalogEstimated : CostKind.Unavailable, usageEvent.Cost.Kind);
        Assert.Equal(expectedCost.HasValue ? (decimal)expectedCost.Value : (decimal?)null,
            usageEvent.Cost.EstimatedCostUsd);
    }

    [Fact]
    public async Task MissingDatabaseReturnsRootUnavailable()
    {
        using var folder = new TemporaryFolder();
        var source = new GooseUsageEventSource(
            "UTC",
            databasePathOverride: Path.Combine(folder.Path, "missing.db"));

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
                "tokenusage-goose-tests",
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

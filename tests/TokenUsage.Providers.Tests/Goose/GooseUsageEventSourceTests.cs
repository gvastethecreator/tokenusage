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
                    0.25,
                    'private prompt and response'
                );
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
        Assert.Equal(new TokenBreakdown(100, 20, 10, 0, 0), usageEvent.Tokens);
        Assert.Equal(CostKind.ProviderReported, usageEvent.Cost.Kind);
        Assert.Equal(0.25m, usageEvent.Cost.ReportedCostUsd);
        Assert.Equal(CoverageKind.Complete, usageEvent.Coverage);
    }

    [Fact]
    public async Task DottedModelIdsKeepTheirVersionAndZeroCostUsesTheCatalog()
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
                    '{"model_name":"gemini-3.6-flash"}',
                    'google',
                    1000000,
                    100000,
                    1100000,
                    0
                );
                """;
            command.ExecuteNonQuery();
        }

        UsageEvent usageEvent = Assert.Single(
            (await new GooseUsageEventSource("UTC", databasePathOverride: databasePath)
                .ReadAsync()).Events);

        Assert.Equal("gemini-3.6-flash", usageEvent.ModelId.Value);
        Assert.Equal(CostKind.CatalogEstimated, usageEvent.Cost.Kind);
        Assert.Equal(2.25m, usageEvent.Cost.EstimatedCostUsd);
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

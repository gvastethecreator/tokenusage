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
        Assert.Equal(new TokenBreakdown(100, 20, 5, 30, 10), usageEvent.Tokens);
        Assert.Equal(CostKind.ProviderReported, usageEvent.Cost.Kind);
        Assert.Equal(0.125m, usageEvent.Cost.ReportedCostUsd);
        Assert.DoesNotContain("session-private-id", usageEvent.EventKey.Value, StringComparison.Ordinal);
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

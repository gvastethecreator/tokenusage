using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using WOpenUsage.Core.Providers;
using WOpenUsage.Core.Usage;
using WOpenUsage.Providers.OpenCode;

namespace WOpenUsage.Providers.Tests.OpenCode;

public sealed class OpenCodeUsageEventSourceTests
{
    [Fact]
    public void RootDetectionDoesNotOpenDatabaseOrAuthFiles()
    {
        string root = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))).FullName;
        string trap = Path.Combine(root, "auth.json");
        File.WriteAllText(trap, "Bearer private-account@example.test");
        try
        {
            using (var locked = new FileStream(
                       trap, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var source = new OpenCodeUsageEventSource("UTC", dataDirectoryOverride: root);
                Assert.True(source.IsRootAvailable);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task OptInSmokeMatchesOpenCodeStatsWithoutPersistingCliOutput()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("WOPENUSAGE_OPENCODE_SMOKE"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        UsageSourceReadResult result = await new OpenCodeUsageEventSource("UTC").ReadAsync();
        Assert.Equal(UsageSourceReadStatus.Complete, result.Status);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "opencode",
                Arguments = "stats --pure --days 400 --tools 0",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };
        Assert.True(process.Start());
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        Task<string> standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        string output = await standardOutput;
        _ = await standardError;
        Assert.Equal(0, process.ExitCode);

        decimal expectedCost = ReadStatsValue(output, "Total Cost", currency: true);
        long expectedInput = checked((long)ReadStatsValue(output, "Input"));
        long expectedOutput = checked((long)ReadStatsValue(output, "Output"));
        long expectedCacheRead = checked((long)ReadStatsValue(output, "Cache Read"));
        long expectedCacheWrite = checked((long)ReadStatsValue(output, "Cache Write"));
        decimal actualCost = result.Events.Sum(item => item.Cost.ReportedCostUsd ?? 0m);
        long actualInput = result.Events.Sum(item => item.Tokens.Input);
        long actualOutput = result.Events.Sum(item => item.Tokens.Output);
        long actualCacheRead = result.Events.Sum(item => item.Tokens.CacheRead);
        long actualCacheWrite = result.Events.Sum(item => item.Tokens.CacheWrite);

        Assert.InRange(actualCost, expectedCost - 0.01m, expectedCost + 0.01m);
        AssertApproximately(actualInput, expectedInput);
        AssertApproximately(actualOutput, expectedOutput);
        AssertApproximately(actualCacheRead, expectedCacheRead);
        AssertApproximately(actualCacheWrite, expectedCacheWrite);
    }

    [Fact]
    public async Task MissingRootReturnsNoData()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var source = new OpenCodeUsageEventSource("UTC", dataDirectoryOverride: root);

        UsageSourceReadResult result = await source.ReadAsync();

        Assert.False(source.IsRootAvailable);
        Assert.Equal("opencode", source.AgentId.Value);
        Assert.Equal(SourceKind.LocalDatabase, source.SourceKind);
        Assert.Empty(result.Events);
        Assert.Equal(UsageSourceReadStatus.NoData, result.Status);
        Assert.Equal(UsageSourceIssueKind.RootUnavailable, result.Issue);
    }

    [Fact]
    public async Task ReadsCurrentSessionAggregatesAndKeepsZeroCost()
    {
        using var corpus = new OpenCodeCorpus();
        corpus.CreateCurrentDatabase((
            "session-a", 1_784_694_600_000L, "OpenAI/GPT-5", 0m, 100L, 20L, 5L, 30L, 7L));

        OpenCodeUsageEventSource source = corpus.CreateSource();
        UsageSourceReadResult result = await source.ReadAsync();
        UsageEvent usageEvent = Assert.Single(result.Events);

        Assert.True(source.IsRootAvailable);
        Assert.Equal(UsageSourceReadStatus.Complete, result.Status);
        Assert.Equal("openai", usageEvent.ModelProviderId?.Value);
        Assert.Equal("gpt-5", usageEvent.ModelId.Value);
        Assert.Equal(new TokenBreakdown(100, 20, 5, 30, 7), usageEvent.Tokens);
        Assert.Equal(CostKind.ProviderReported, usageEvent.Cost.Kind);
        Assert.Equal(0m, usageEvent.Cost.ReportedCostUsd);
        Assert.Equal(64, usageEvent.EventKey.Value.Length);
        Assert.Equal(usageEvent.EventKey, Assert.Single((await corpus.CreateSource().ReadAsync()).Events).EventKey);
    }

    [Fact]
    public async Task LegacyDatabaseMessagesWinOverLegacyJson()
    {
        using var corpus = new OpenCodeCorpus();
        corpus.CreateLegacyDatabase(("message-a", "session-a", 1_784_694_000_000L, Message("message-a", "db-model", 10, 2, 3, 4, 5, 0m)));
        corpus.WriteJsonSession("session-a", Message("json-a", "json-model", 800, 80, 0, 0, 0, 8m));

        UsageSourceReadResult result = await corpus.CreateSource().ReadAsync();
        UsageEvent usageEvent = Assert.Single(result.Events);

        Assert.Equal(UsageSourceReadStatus.Complete, result.Status);
        Assert.Equal("db-model", usageEvent.ModelId.Value);
        Assert.Equal(new TokenBreakdown(10, 2, 3, 4, 5), usageEvent.Tokens);
        Assert.Equal(0m, usageEvent.Cost.ReportedCostUsd);
    }

    [Fact]
    public async Task ReadsLegacyJsonOnlyForSessionsAbsentFromDatabase()
    {
        using var corpus = new OpenCodeCorpus();
        corpus.WriteJsonSession("session-json", Message("message-json", "anthropic/claude", 21, 5, 2, 8, 1, null));
        File.WriteAllText(Path.Combine(corpus.Root, "auth.json"), "{not valid json and forbidden}");

        UsageSourceReadResult result = await corpus.CreateSource().ReadAsync();
        UsageEvent usageEvent = Assert.Single(result.Events);

        Assert.Equal(UsageSourceReadStatus.Complete, result.Status);
        Assert.Equal(new TokenBreakdown(21, 5, 2, 8, 1), usageEvent.Tokens);
        Assert.Equal(CostKind.Unavailable, usageEvent.Cost.Kind);
        Assert.Equal(CoverageKind.Unpriced, usageEvent.Coverage);
    }

    [Fact]
    public async Task MatchingProviderPrefixIsRemovedFromTheStableModelId()
    {
        using var corpus = new OpenCodeCorpus();
        string message = JsonSerializer.Serialize(new
        {
            id = "message-provider",
            role = "assistant",
            time = new { created = 1_784_694_000_000L },
            providerID = "openai",
            modelID = "openai/gpt-5",
            cost = 0m,
            tokens = new { input = 10, output = 2 },
        });
        corpus.WriteJsonSession("session-provider", message);

        UsageEvent usageEvent = Assert.Single((await corpus.CreateSource().ReadAsync()).Events);

        Assert.Equal("openai", usageEvent.ModelProviderId?.Value);
        Assert.Equal("gpt-5", usageEvent.ModelId.Value);
    }

    [Fact]
    public async Task UnknownSchemaMalformedJsonAndLimitsReturnPartial()
    {
        using var unknown = new OpenCodeCorpus();
        unknown.CreateUnknownDatabase();
        UsageSourceReadResult unknownResult = await unknown.CreateSource().ReadAsync();
        Assert.Equal(UsageSourceReadStatus.Partial, unknownResult.Status);
        Assert.Equal(UsageSourceIssueKind.UnsupportedSchema, unknownResult.Issue);

        using var malformed = new OpenCodeCorpus();
        malformed.WriteJsonSession("broken", "{\"id\":");
        Assert.Equal(UsageSourceReadStatus.Partial, (await malformed.CreateSource().ReadAsync()).Status);

        using var limited = new OpenCodeCorpus();
        limited.WriteJsonSession("one", Message("one", "model", 1, 1, 0, 0, 0, 1m));
        Assert.Equal(UsageSourceReadStatus.Partial, (await limited.CreateSource(maximumFiles: 1).ReadAsync()).Status);
    }

    [Fact]
    public async Task ObservesCancellationAndFallsBackFromInvalidConfiguredRoot()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var corpus = new OpenCodeCorpus();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => corpus.CreateSource().ReadAsync(cancellation.Token));

        var source = new OpenCodeUsageEventSource("UTC", homeDirectory: corpus.Root, dataDirectoryOverride: "\0bad");
        Assert.Equal(UsageSourceReadStatus.NoData, (await source.ReadAsync()).Status);
    }

    [Fact]
    public async Task LegacyStepFinishSuppliesMissingMessageUsageWithoutReadingText()
    {
        using var corpus = new OpenCodeCorpus();
        string message = JsonSerializer.Serialize(new
        {
            id = "message-step",
            role = "assistant",
            time = new { created = 1_784_694_000_000L },
            modelID = "openai/gpt-5",
            text = "private fixture text",
        });
        corpus.WriteJsonSession("session-step", message);
        corpus.WriteStepFinish(
            "message-step",
            JsonSerializer.Serialize(new
            {
                type = "step-finish",
                cost = 0.25m,
                tokens = new
                {
                    input = 40,
                    output = 6,
                    reasoning = 2,
                    cache = new { read = 10, write = 1 },
                },
                text = "must stay unread",
            }));

        UsageEvent usageEvent = Assert.Single((await corpus.CreateSource().ReadAsync()).Events);

        Assert.Equal(new TokenBreakdown(40, 6, 2, 10, 1), usageEvent.Tokens);
        Assert.Equal(0.25m, usageEvent.Cost.ReportedCostUsd);
    }

    [Fact]
    public async Task LegacyJsonUsesTheLatestStepFinish()
    {
        using var corpus = new OpenCodeCorpus();
        corpus.WriteJsonSession(
            "session-latest",
            JsonSerializer.Serialize(new
            {
                id = "message-latest",
                role = "assistant",
                time = new { created = 1_784_694_000_000L },
                modelID = "openai/gpt-5",
            }));
        corpus.WriteStepFinish(
            "message-latest",
            JsonSerializer.Serialize(new { type = "step-finish", cost = 1m, tokens = new { input = 10, output = 1 } }),
            "part-1.json");
        corpus.WriteStepFinish(
            "message-latest",
            JsonSerializer.Serialize(new { type = "step-finish", cost = 2m, tokens = new { input = 20, output = 2 } }),
            "part-2.json");

        UsageEvent usageEvent = Assert.Single((await corpus.CreateSource().ReadAsync()).Events);

        Assert.Equal(new TokenBreakdown(20, 2, 0, 0, 0), usageEvent.Tokens);
        Assert.Equal(2m, usageEvent.Cost.ReportedCostUsd);
    }

    [Fact]
    public async Task LegacyDatabaseStepFinishSuppliesMissingMessageUsage()
    {
        using var corpus = new OpenCodeCorpus();
        string message = JsonSerializer.Serialize(new
        {
            id = "message-db-step",
            role = "assistant",
            modelID = "openai/gpt-5",
            text = "private fixture text",
        });
        corpus.CreateLegacyDatabase((
            "message-db-step",
            "session-db-step",
            1_784_694_000_000L,
            message));
        corpus.CreateLegacyPart(
            "part-db-step",
            "message-db-step",
            1_784_694_000_100L,
            JsonSerializer.Serialize(new
            {
                type = "step-finish",
                cost = 0m,
                tokens = new
                {
                    input = 70,
                    output = 9,
                    reasoning = 3,
                    cache = new { read = 12, write = 2 },
                },
            }));

        UsageEvent usageEvent = Assert.Single((await corpus.CreateSource().ReadAsync()).Events);

        Assert.Equal(new TokenBreakdown(70, 9, 3, 12, 2), usageEvent.Tokens);
        Assert.Equal(0m, usageEvent.Cost.ReportedCostUsd);
    }

    [Fact]
    public async Task ReadOnlyScannerSeesCommittedWalRows()
    {
        using var corpus = new OpenCodeCorpus();
        using var writer = new SqliteConnection(
            $"Data Source={Path.Combine(corpus.Root, "opencode.db")};Pooling=False");
        writer.Open();
        using (SqliteCommand command = writer.CreateCommand())
        {
            command.CommandText =
                "PRAGMA journal_mode=WAL;"
                + "CREATE TABLE session (id TEXT PRIMARY KEY, time_updated INTEGER, model TEXT, cost REAL, tokens_input INTEGER, tokens_output INTEGER, tokens_reasoning INTEGER, tokens_cache_read INTEGER, tokens_cache_write INTEGER);"
                + "INSERT INTO session VALUES ('wal-session',1784694600000,'openai/gpt-5',0,10,2,0,3,0);";
            command.ExecuteNonQuery();
        }

        UsageSourceReadResult result = await corpus.CreateSource().ReadAsync();

        Assert.Single(result.Events);
        Assert.Equal(UsageSourceReadStatus.Complete, result.Status);
    }

    [Fact]
    public async Task RowBudgetMarksTheSnapshotPartial()
    {
        using var corpus = new OpenCodeCorpus();
        corpus.CreateCurrentDatabase(
            ("session-a", 1_784_694_600_000L, "model-a", 0m, 1L, 1L, 0L, 0L, 0L),
            ("session-b", 1_784_694_600_001L, "model-b", 0m, 1L, 1L, 0L, 0L, 0L));

        UsageSourceReadResult result = await corpus.CreateSource(maximumRows: 1).ReadAsync();

        Assert.Single(result.Events);
        Assert.Equal(UsageSourceReadStatus.Partial, result.Status);
    }

    private static string Message(string id, string model, long input, long output, long reasoning, long cacheRead, long cacheWrite, decimal? cost) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["id"] = id,
            ["role"] = "assistant",
            ["time"] = new { created = 1_784_694_000_000L },
            ["modelID"] = model,
            ["cost"] = cost,
            ["tokens"] = new { input, output, reasoning, cache = new { read = cacheRead, write = cacheWrite } },
            ["text"] = "must never be retained",
        });

    private static decimal ReadStatsValue(string output, string label, bool currency = false)
    {
        string clean = Regex.Replace(output, "\\u001B\\[[0-?]*[ -/]*[@-~]", string.Empty);
        Match match = Regex.Match(
            clean,
            $@"{Regex.Escape(label)}\s+{(currency ? @"\$" : string.Empty)}(?<value>[0-9]+(?:\.[0-9]+)?)(?<suffix>[KMB]?)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        Assert.True(match.Success, $"OpenCode stats did not expose {label}.");
        decimal value = decimal.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture);
        decimal multiplier = match.Groups["suffix"].Value.ToUpperInvariant() switch
        {
            "K" => 1_000m,
            "M" => 1_000_000m,
            "B" => 1_000_000_000m,
            _ => 1m,
        };
        return value * multiplier;
    }

    private static void AssertApproximately(long actual, long expected)
    {
        // The CLI rounds large values to one decimal place (for example, 1.8M).
        long tolerance = Math.Max(1, checked((long)Math.Ceiling(expected * 0.06m)));
        Assert.InRange(actual, expected - tolerance, expected + tolerance);
    }

    private sealed class OpenCodeCorpus : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "wopenusage-opencode", Guid.NewGuid().ToString("N"));

        public OpenCodeCorpus() => Directory.CreateDirectory(_root);

        public OpenCodeUsageEventSource CreateSource(
            int maximumFiles = 100,
            long maximumFileBytes = 1024 * 1024,
            int maximumRows = 1_000) =>
            new(
                "UTC",
                dataDirectoryOverride: _root,
                maximumFiles: maximumFiles,
                maximumFileBytes: maximumFileBytes,
                maximumRows: maximumRows);

        public void CreateCurrentDatabase(params (string Id, long Updated, string Model, decimal? Cost, long Input, long Output, long Reasoning, long CacheRead, long CacheWrite)[] rows)
        {
            using var connection = new SqliteConnection($"Data Source={Path.Combine(_root, "opencode.db")};Pooling=False");
            connection.Open();
            using SqliteCommand create = connection.CreateCommand();
            create.CommandText = "CREATE TABLE session (id TEXT PRIMARY KEY, time_updated INTEGER, model TEXT, cost REAL, tokens_input INTEGER, tokens_output INTEGER, tokens_reasoning INTEGER, tokens_cache_read INTEGER, tokens_cache_write INTEGER)";
            create.ExecuteNonQuery();
            foreach (var row in rows)
            {
                using SqliteCommand insert = connection.CreateCommand();
                insert.CommandText = "INSERT INTO session VALUES ($id,$time,$model,$cost,$input,$output,$reasoning,$read,$write)";
                insert.Parameters.AddWithValue("$id", row.Id);
                insert.Parameters.AddWithValue("$time", row.Updated);
                insert.Parameters.AddWithValue("$model", row.Model);
                insert.Parameters.AddWithValue("$cost", row.Cost is null ? DBNull.Value : row.Cost.Value);
                insert.Parameters.AddWithValue("$input", row.Input);
                insert.Parameters.AddWithValue("$output", row.Output);
                insert.Parameters.AddWithValue("$reasoning", row.Reasoning);
                insert.Parameters.AddWithValue("$read", row.CacheRead);
                insert.Parameters.AddWithValue("$write", row.CacheWrite);
                insert.ExecuteNonQuery();
            }
        }

        public string Root => _root;

        public void CreateLegacyDatabase(params (string Id, string SessionId, long Created, string Data)[] rows)
        {
            using var connection = new SqliteConnection($"Data Source={Path.Combine(_root, "opencode.db")};Pooling=False");
            connection.Open();
            using SqliteCommand create = connection.CreateCommand();
            create.CommandText = "CREATE TABLE message (id TEXT PRIMARY KEY, session_id TEXT, time_created INTEGER, data TEXT)";
            create.ExecuteNonQuery();
            foreach (var row in rows)
            {
                using SqliteCommand insert = connection.CreateCommand();
                insert.CommandText = "INSERT INTO message VALUES ($id,$session,$time,$data)";
                insert.Parameters.AddWithValue("$id", row.Id);
                insert.Parameters.AddWithValue("$session", row.SessionId);
                insert.Parameters.AddWithValue("$time", row.Created);
                insert.Parameters.AddWithValue("$data", row.Data);
                insert.ExecuteNonQuery();
            }
        }

        public void CreateUnknownDatabase()
        {
            using var connection = new SqliteConnection($"Data Source={Path.Combine(_root, "opencode.db")};Pooling=False");
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE future_schema (secret TEXT)";
            command.ExecuteNonQuery();
        }

        public void CreateLegacyPart(
            string id,
            string messageId,
            long created,
            string data)
        {
            using var connection = new SqliteConnection(
                $"Data Source={Path.Combine(_root, "opencode.db")};Pooling=False");
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                "CREATE TABLE part (id TEXT PRIMARY KEY, message_id TEXT, time_created INTEGER, data TEXT);"
                + "INSERT INTO part VALUES ($id,$message,$time,$data);";
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$message", messageId);
            command.Parameters.AddWithValue("$time", created);
            command.Parameters.AddWithValue("$data", data);
            command.ExecuteNonQuery();
        }

        public void WriteJsonSession(string sessionId, string message)
        {
            string sessions = Directory.CreateDirectory(Path.Combine(_root, "storage", "session", "project")).FullName;
            File.WriteAllText(Path.Combine(sessions, sessionId + ".json"), JsonSerializer.Serialize(new { id = sessionId }));
            string messages = Directory.CreateDirectory(Path.Combine(_root, "storage", "message", sessionId)).FullName;
            File.WriteAllText(Path.Combine(messages, "message.json"), message);
        }

        public void WriteStepFinish(string messageId, string part, string fileName = "part.json")
        {
            string parts = Directory.CreateDirectory(
                Path.Combine(_root, "storage", "part", messageId)).FullName;
            File.WriteAllText(Path.Combine(parts, fileName), part);
        }

        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}

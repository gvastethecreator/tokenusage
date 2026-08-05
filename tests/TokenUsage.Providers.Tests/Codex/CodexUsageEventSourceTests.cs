using System.Text.Json;
using Microsoft.Data.Sqlite;
using WOpenUsage.Core.Providers;
using WOpenUsage.Core.Usage;
using WOpenUsage.Providers.Codex;

namespace WOpenUsage.Providers.Tests.Codex;

public sealed class CodexUsageEventSourceTests
{
    [Fact]
    public void RootDetectionDoesNotReadFiles()
    {
        using var corpus = new CodexCorpus();
        string trap = Path.Combine(corpus.Root, "auth.json");
        File.WriteAllText(trap, "Bearer private-account@example.test");

        using var locked = new FileStream(
            trap,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);
        var source = new CodexUsageEventSource("UTC", codexHomeOverride: corpus.Root);

        Assert.True(source.IsRootAvailable);
    }

    [Fact]
    public async Task MissingCodexDataIsReportedAsNoData()
    {
        string home = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var source = new CodexUsageEventSource("UTC", homeDirectory: home);

        UsageSourceReadResult result = await source.ReadAsync();

        Assert.False(source.IsRootAvailable);
        Assert.Equal("codex", source.AgentId.Value);
        Assert.Equal(SourceKind.LocalLog, source.SourceKind);
        Assert.Equal(CodexUsageEventSource.ParserVersion, source.EventParserVersion);
        Assert.Equal(35, source.ReconciliationWindowDays);
        Assert.Empty(result.Events);
        Assert.Equal(UsageSourceReadStatus.NoData, result.Status);
        Assert.Equal(UsageSourceIssueKind.RootUnavailable, result.Issue);
    }

    [Fact]
    public async Task ReadsContentFreeCountersAndPricesKnownModels()
    {
        using var corpus = new CodexCorpus();
        corpus.WriteSession(
            "session-a",
            Context("gpt-5.5"),
            Usage(
                "2026-07-27T12:01:00Z",
                input: 1_000,
                cachedInput: 200,
                output: 100,
                reasoningOutput: 40,
                cacheWriteInput: 0,
                privateText: "private fixture content"));

        UsageSourceReadResult result = await corpus.CreateSource().ReadAsync();
        UsageEvent usageEvent = Assert.Single(result.Events);

        Assert.Equal(UsageSourceReadStatus.Complete, result.Status);
        Assert.Equal("gpt-5.5", usageEvent.ModelId.Value);
        Assert.Equal(new TokenBreakdown(800, 60, 40, 200, 0), usageEvent.Tokens);
        Assert.Equal(CostKind.CatalogEstimated, usageEvent.Cost.Kind);
        Assert.Equal(0.0071m, usageEvent.Cost.EstimatedCostUsd);
        Assert.Equal(CodexPricingCatalog.Version, usageEvent.Cost.CatalogVersion);
        Assert.Equal(CoverageKind.Partial, usageEvent.Coverage);
        Assert.Equal(64, usageEvent.EventKey.Value.Length);
    }

    [Fact]
    public async Task KeepsOneLatestCumulativeCounterPerSession()
    {
        using var corpus = new CodexCorpus();
        corpus.WriteSession(
            "session-models",
            Context("gpt-5.6-terra"),
            Usage("2026-07-27T12:01:00Z", 100, 20, 10, 2),
            Context("gpt-5.6-luna"),
            Usage(
                "2026-07-27T12:02:00Z",
                200,
                40,
                20,
                4,
                totalInput: 300,
                totalCachedInput: 60,
                totalOutput: 30,
                totalReasoningOutput: 6));

        UsageSourceReadResult result = await corpus.CreateSource().ReadAsync();
        UsageEvent usageEvent = Assert.Single(result.Events);

        Assert.Equal(UsageSourceReadStatus.Complete, result.Status);
        Assert.Equal("gpt-5.6-luna", usageEvent.ModelId.Value);
        Assert.Equal(new TokenBreakdown(240, 24, 6, 60, 0), usageEvent.Tokens);
        Assert.Equal(CostKind.CatalogEstimated, usageEvent.Cost.Kind);
    }

    [Fact]
    public async Task UsesStateIndexModelAndReadsOnlyTheBoundedFileTail()
    {
        using var corpus = new CodexCorpus();
        string path = corpus.WriteSession(
            "indexed-session",
            JsonSerializer.Serialize(new { ignored = new string('x', 16 * 1024) }),
            Usage("2026-07-27T12:01:00Z", 100, 20, 10, 2));
        corpus.WriteStateIndex((path, "gpt-5.4-mini"));

        UsageEvent usageEvent = Assert.Single((await corpus.CreateSource(
            maximumTailBytes: 2 * 1024).ReadAsync()).Events);

        Assert.Equal("gpt-5.4-mini", usageEvent.ModelId.Value);
        Assert.Equal(CostKind.CatalogEstimated, usageEvent.Cost.Kind);
        Assert.Equal("gpt-5.4-mini", usageEvent.Cost.ExactPriceMatch);
    }

    [Fact]
    public async Task OfficialDailyTotalsUseTheBoundedLocalModelSample()
    {
        using var corpus = new CodexCorpus();
        corpus.WriteSession(
            "official-session",
            Context("gpt-5.4-mini"),
            Usage("2026-07-27T12:01:00Z", 100, 20, 10, 2));
        var usage = new CodexTokenUsageSnapshot(
            new CodexUsageSummary(null, null, null, null, null),
            [new CodexUsageDailyBucket(new DateOnly(2026, 7, 27), 1_000)]);
        CodexUsageEventSource source = corpus.CreateSource(
            clientFactory: new StubFactory(new StubClient(usage)));

        UsageSourceReadResult result = await source.ReadAsync();
        UsageEvent usageEvent = Assert.Single(result.Events);

        Assert.Equal(SourceKind.OfficialLocalApi, source.SourceKind);
        Assert.Equal(UsageSourceReadStatus.Complete, result.Status);
        Assert.Equal("gpt-5.4-mini", usageEvent.ModelId.Value);
        Assert.Equal(1_000, usageEvent.Tokens.Total);
        Assert.Equal(CostKind.CatalogEstimated, usageEvent.Cost.Kind);
        Assert.Equal(CoverageKind.Partial, usageEvent.Coverage);
    }

    [Fact]
    public async Task UnknownModelsKeepTokensWithoutInventingCost()
    {
        using var corpus = new CodexCorpus();
        corpus.WriteSession(
            "session-future",
            Context("gpt-future"),
            Usage("2026-07-27T12:01:00Z", 100, 20, 10, 2));

        UsageEvent usageEvent = Assert.Single((await corpus.CreateSource().ReadAsync()).Events);

        Assert.Equal(110, usageEvent.Tokens.Total);
        Assert.Equal(CostKind.Unavailable, usageEvent.Cost.Kind);
        Assert.Equal(CoverageKind.Unpriced, usageEvent.Coverage);
    }

    [Fact]
    public async Task ReadsArchivedSessionsAndKeepsEventKeysStable()
    {
        using var corpus = new CodexCorpus();
        corpus.WriteSession(
            "archived-a",
            archived: true,
            Context("gpt-5.6-sol"),
            Usage("2026-07-27T12:01:00Z", 100, 20, 10, 2));
        CodexUsageEventSource source = corpus.CreateSource();

        UsageEvent first = Assert.Single((await source.ReadAsync()).Events);
        UsageEvent second = Assert.Single((await source.ReadAsync()).Events);

        Assert.Equal(first.EventKey, second.EventKey);
    }

    [Fact]
    public async Task MalformedUsageReturnsPartialWithoutDroppingValidCounters()
    {
        using var corpus = new CodexCorpus();
        corpus.WriteSession(
            "session-partial",
            Context("gpt-5.5"),
            "{\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\"",
            Usage("2026-07-27T12:01:00Z", 100, 20, 10, 2));

        UsageSourceReadResult result = await corpus.CreateSource().ReadAsync();

        Assert.Single(result.Events);
        Assert.Equal(UsageSourceReadStatus.Partial, result.Status);
        Assert.Equal(UsageSourceIssueKind.UnsupportedSchema, result.Issue);
    }

    [Fact]
    public async Task ScanLimitsReturnPartialAndCancellationIsObserved()
    {
        using var corpus = new CodexCorpus();
        corpus.WriteSession(
            "a",
            Context("gpt-5.5"),
            Usage("2026-07-27T12:01:00Z", 100, 20, 10, 2));
        corpus.WriteSession(
            "b",
            Context("gpt-5.5"),
            Usage("2026-07-27T12:02:00Z", 100, 20, 10, 2));
        CodexUsageEventSource source = corpus.CreateSource(maximumFiles: 1);

        UsageSourceReadResult result = await source.ReadAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Equal(UsageSourceReadStatus.Partial, result.Status);
        Assert.Single(result.Events);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => source.ReadAsync(cancellation.Token));
    }

    [Fact]
    public void PricingAppliesThePublishedGpt55LongContextMultiplier()
    {
        CostObservation cost = CodexPricingCatalog.Resolve(
            "gpt-5.5",
            new TokenBreakdown(272_001, 100, 0, 0, 0));

        Assert.Equal(CostKind.CatalogEstimated, cost.Kind);
        Assert.Equal(2.72451m, cost.EstimatedCostUsd);
    }

    [Fact]
    public void PricingIncludesThePublishedGpt54MiniRates()
    {
        CostObservation cost = CodexPricingCatalog.Resolve(
            "gpt-5.4-mini",
            new TokenBreakdown(80, 8, 2, 20, 0));

        Assert.Equal(CostKind.CatalogEstimated, cost.Kind);
        Assert.Equal(0.000107m, cost.EstimatedCostUsd);
        Assert.Equal("gpt-5.4-mini", cost.ExactPriceMatch);
    }

    private static string Context(string model) => JsonSerializer.Serialize(new
    {
        timestamp = "2026-07-27T12:00:00Z",
        type = "turn_context",
        payload = new
        {
            model,
            cwd = "private-project-path",
            summary = "private fixture summary",
        },
    });

    private static string Usage(
        string timestamp,
        long input,
        long cachedInput,
        long output,
        long reasoningOutput,
        long cacheWriteInput = 0,
        string? privateText = null,
        long? totalInput = null,
        long? totalCachedInput = null,
        long? totalOutput = null,
        long? totalReasoningOutput = null,
        long? totalCacheWriteInput = null) => JsonSerializer.Serialize(new
        {
            timestamp,
            type = "event_msg",
            payload = new
            {
                type = "token_count",
                info = new
                {
                    last_token_usage = new
                    {
                        input_tokens = input,
                        cached_input_tokens = cachedInput,
                        cache_write_input_tokens = cacheWriteInput,
                        output_tokens = output,
                        reasoning_output_tokens = reasoningOutput,
                        total_tokens = checked(input + output),
                    },
                    total_token_usage = new
                    {
                        input_tokens = totalInput ?? input,
                        cached_input_tokens = totalCachedInput ?? cachedInput,
                        cache_write_input_tokens = totalCacheWriteInput ?? cacheWriteInput,
                        output_tokens = totalOutput ?? output,
                        reasoning_output_tokens = totalReasoningOutput ?? reasoningOutput,
                        total_tokens = checked((totalInput ?? input) + (totalOutput ?? output)),
                    },
                },
                text = privateText,
            },
        });

    private sealed class CodexCorpus : IDisposable
    {
        private readonly string _path = Path.Combine(
            Path.GetTempPath(),
            "wopenusage-codex-corpus",
            Guid.NewGuid().ToString("N"));

        public CodexCorpus() => Directory.CreateDirectory(Path.Combine(_path, "sessions"));

        public string Root => _path;

        public CodexUsageEventSource CreateSource(
            int maximumFiles = 100,
            long maximumTailBytes = 64 * 1024,
            ICodexQuotaClientFactory? clientFactory = null) => new(
            "UTC",
            codexHomeOverride: _path,
            maximumFiles: maximumFiles,
            maximumTailBytes: maximumTailBytes,
            clientFactory: clientFactory);

        public string WriteSession(string id, params string[] lines) =>
            WriteSession(id, false, lines);

        public string WriteSession(string id, bool archived, params string[] lines)
        {
            string root = Directory.CreateDirectory(Path.Combine(
                _path,
                archived ? "archived_sessions" : "sessions")).FullName;
            string path = Path.Combine(root, $"{id}.jsonl");
            File.WriteAllLines(path, lines);
            return path;
        }

        public void WriteStateIndex(params (string Path, string? Model)[] sessions)
        {
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(_path, "state_5.sqlite"),
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false,
            };
            using var connection = new SqliteConnection(builder.ToString());
            connection.Open();
            using (SqliteCommand create = connection.CreateCommand())
            {
                create.CommandText =
                    "CREATE TABLE threads (rollout_path TEXT NOT NULL, model TEXT NULL);";
                create.ExecuteNonQuery();
            }

            foreach ((string path, string? model) in sessions)
            {
                using SqliteCommand insert = connection.CreateCommand();
                insert.CommandText =
                    "INSERT INTO threads (rollout_path, model) VALUES ($path, $model);";
                insert.Parameters.AddWithValue("$path", path);
                insert.Parameters.AddWithValue("$model", (object?)model ?? DBNull.Value);
                insert.ExecuteNonQuery();
            }
        }

        public void Dispose() => Directory.Delete(_path, recursive: true);
    }

    private sealed class StubFactory(StubClient client) : ICodexQuotaClientFactory
    {
        public ValueTask<CodexClientAvailability> DetectAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(CodexClientAvailability.Available);
        }

        public Task<ICodexQuotaClient> CreateAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<ICodexQuotaClient>(client);
        }
    }

    private sealed class StubClient(CodexTokenUsageSnapshot usage) : ICodexQuotaClient
    {
        public Task HandshakeAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task<CodexTokenUsageSnapshot> ReadTokenUsageAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(usage);
        }

        public Task<CodexAccountStatus> ReadAccountStatusAsync(
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<CodexRateLimitsSnapshot> ReadRateLimitsAsync(
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

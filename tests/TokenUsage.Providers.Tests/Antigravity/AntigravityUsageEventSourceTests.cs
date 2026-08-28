using Microsoft.Data.Sqlite;
using TokenUsage.App.Services;
using TokenUsage.App.ViewModels.Dashboard;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;
using TokenUsage.Providers.Antigravity;

namespace TokenUsage.Providers.Tests.Antigravity;

public sealed class AntigravityUsageEventSourceTests
{
    [Fact]
    public async Task ReadsTokenMetadataAndAppliesExactPublicRates()
    {
        using var corpus = new AntigravityCorpus();
        corpus.Insert(
            index: 1,
            timestamp: new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero),
            model: "Gemini 3.6 Flash (High)",
            input: 100,
            output: 20,
            cacheRead: 30);
        corpus.Insert(
            index: 2,
            timestamp: new DateTimeOffset(2026, 8, 3, 13, 0, 0, TimeSpan.Zero),
            model: "Claude Sonnet 4.6 (Thinking)",
            input: 200,
            output: 10,
            cacheRead: 40);

        AntigravityUsageEventSource source = corpus.CreateSource();
        UsageSourceReadResult result = await source.ReadAsync();

        Assert.True(source.IsRootAvailable);
        Assert.Equal(SourceKind.LocalDatabase, source.SourceKind);
        Assert.Equal(UsageSourceReadStatus.Complete, result.Status);
        Assert.Collection(
            result.Events,
            gemini =>
            {
                Assert.Equal("gemini-3.6-flash", gemini.ModelId.Value);
                Assert.Equal("google", gemini.ModelProviderId?.Value);
                Assert.Equal(new TokenBreakdown(100, 20, 0, 30, 0), gemini.Tokens);
                Assert.Equal(0.000305m, gemini.Cost.EstimatedCostUsd);
                Assert.Equal(AntigravityPricingCatalog.Version, gemini.Cost.CatalogVersion);
                Assert.Equal(CoverageKind.Partial, gemini.Coverage);
            },
            claude =>
            {
                Assert.Equal("claude-sonnet-4-6", claude.ModelId.Value);
                Assert.Equal("anthropic", claude.ModelProviderId?.Value);
                Assert.Equal(new TokenBreakdown(200, 10, 0, 40, 0), claude.Tokens);
                Assert.Equal(0.000762m, claude.Cost.EstimatedCostUsd);
            });
        Assert.Equal(
            result.Events[0].EventKey,
            (await corpus.CreateSource().ReadAsync()).Events[0].EventKey);
    }

    [Fact]
    public async Task NamedModelsOutsideTheOriginalAllowListKeepTheirIdAndPrice()
    {
        using var corpus = new AntigravityCorpus();
        corpus.Insert(
            index: 1,
            timestamp: new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero),
            model: "Gemini 3 Pro",
            input: 1_000_000,
            output: 100_000,
            cacheRead: 0);
        corpus.Insert(
            index: 2,
            timestamp: new DateTimeOffset(2026, 8, 3, 13, 0, 0, TimeSpan.Zero),
            model: "Gemini 3 Flash",
            input: 1_000_000,
            output: 100_000,
            cacheRead: 0);

        IReadOnlyList<UsageEvent> events = (await corpus.CreateSource().ReadAsync()).Events;

        UsageEvent pro = Assert.Single(events, item => item.ModelId.Value == "gemini-3-pro");
        UsageEvent flash = Assert.Single(events, item => item.ModelId.Value == "gemini-3-flash");
        Assert.Equal("google", pro.ModelProviderId?.Value);
        Assert.Equal(CostKind.CatalogEstimated, pro.Cost.Kind);
        Assert.Equal(5.8m, pro.Cost.EstimatedCostUsd);
        Assert.Equal(0.8m, flash.Cost.EstimatedCostUsd);
        Assert.DoesNotContain(
            events,
            item => item.ModelId.Value.Contains("unknown", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MalformedMetadataKeepsValidEventsAndMarksPartial()
    {
        using var corpus = new AntigravityCorpus();
        corpus.Insert(
            index: 1,
            timestamp: new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero),
            model: "Gemini 3.6 Flash (High)",
            input: 10,
            output: 2,
            cacheRead: 0);
        corpus.InsertRaw(2, [0xff], TimestampPayload(
            new DateTimeOffset(2026, 8, 3, 13, 0, 0, TimeSpan.Zero)));

        UsageSourceReadResult result = await corpus.CreateSource().ReadAsync();

        Assert.Single(result.Events);
        Assert.Equal(UsageSourceReadStatus.Partial, result.Status);
        Assert.Equal(UsageSourceIssueKind.PartialScan, result.Issue);
    }

    [Fact]
    public async Task LocalDatabaseEventFlowsIntoTheSpendDonut()
    {
        using var corpus = new AntigravityCorpus();
        corpus.Insert(
            index: 1,
            timestamp: new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero),
            model: "Gemini 3.6 Flash (High)",
            input: 100,
            output: 20,
            cacheRead: 30);
        var coordinator = new LocalUsageCoordinator(
            corpus.UsageDatabasePath,
            corpus.CreateSource(),
            new FixedTimeProvider(
                new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero)));

        LocalUsageCard card = await coordinator.RefreshAsync(key => key);

        SpendSlice slice = Assert.Single(card.SpendBreakdown.AgentSlices);
        Assert.Equal("antigravity", slice.ProviderId);
        Assert.Equal(0.000305d, slice.Amount, precision: 9);
        Assert.Contains(
            card.SpendBreakdown.Models,
            model => model.AgentId == "antigravity"
                     && model.ModelName == "gemini-3.6-flash");
    }

    [Fact]
    public async Task MissingRootIsReportedWithoutInspectingOtherGeminiData()
    {
        string home = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var source = new AntigravityUsageEventSource("UTC", homeDirectory: home);

        UsageSourceReadResult result = await source.ReadAsync();

        Assert.False(source.IsRootAvailable);
        Assert.Empty(result.Events);
        Assert.Equal(UsageSourceIssueKind.RootUnavailable, result.Issue);
    }

    private static byte[] UsagePayload(string model, long input, long output, long cacheRead)
    {
        byte[] tokenBlock = Message(
            VarintField(1, 1234),
            VarintField(2, input),
            VarintField(3, output),
            VarintField(5, cacheRead));
        return Message(BytesField(7, tokenBlock), BytesField(21, System.Text.Encoding.UTF8.GetBytes(model)));
    }

    private static byte[] TimestampPayload(DateTimeOffset timestamp)
    {
        byte[] value = Message(VarintField(1, timestamp.ToUnixTimeSeconds()));
        return Message(BytesField(10, value));
    }

    private static byte[] VarintField(int number, long value) =>
        Message(Varint((ulong)(number << 3)), Varint((ulong)value));

    private static byte[] BytesField(int number, byte[] value) =>
        Message(Varint((ulong)((number << 3) | 2)), Varint((ulong)value.Length), value);

    private static byte[] Varint(ulong value)
    {
        var bytes = new List<byte>();
        do
        {
            byte current = (byte)(value & 0x7f);
            value >>= 7;
            if (value != 0)
            {
                current |= 0x80;
            }

            bytes.Add(current);
        }
        while (value != 0);

        return bytes.ToArray();
    }

    private static byte[] Message(params byte[][] fields) => fields.SelectMany(value => value).ToArray();

    private sealed class AntigravityCorpus : IDisposable
    {
        private readonly string _home = Path.Combine(
            Path.GetTempPath(),
            "tokenusage-antigravity-corpus",
            Guid.NewGuid().ToString("N"));
        private readonly string _databasePath;

        public AntigravityCorpus()
        {
            string conversations = Directory.CreateDirectory(
                Path.Combine(_home, ".gemini", "antigravity-ide", "conversations")).FullName;
            _databasePath = Path.Combine(conversations, "session.db");
            using var connection = new SqliteConnection($"Data Source={_databasePath};Pooling=False");
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE gen_metadata (idx INTEGER PRIMARY KEY, data BLOB, size INTEGER);
                CREATE TABLE steps (idx INTEGER PRIMARY KEY, step_payload BLOB);
                """;
            command.ExecuteNonQuery();
        }

        public AntigravityUsageEventSource CreateSource() => new(
            "UTC",
            homeDirectory: _home,
            maximumFiles: 10,
            maximumRows: 100);

        public string UsageDatabasePath => Path.Combine(_home, "usage.v1.db");

        public void Insert(
            int index,
            DateTimeOffset timestamp,
            string model,
            long input,
            long output,
            long cacheRead) => InsertRaw(
                index,
                UsagePayload(model, input, output, cacheRead),
                TimestampPayload(timestamp));

        public void InsertRaw(int index, byte[] metadata, byte[] stepPayload)
        {
            using var connection = new SqliteConnection($"Data Source={_databasePath};Pooling=False");
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO gen_metadata (idx, data, size) VALUES ($idx, $data, $size);
                INSERT INTO steps (idx, step_payload) VALUES ($idx, $payload);
                """;
            command.Parameters.AddWithValue("$idx", index);
            command.Parameters.AddWithValue("$data", metadata);
            command.Parameters.AddWithValue("$size", metadata.Length);
            command.Parameters.AddWithValue("$payload", stepPayload);
            command.ExecuteNonQuery();
        }

        public void Dispose() => Directory.Delete(_home, recursive: true);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow.ToUniversalTime();
    }
}

using System.Text.Json;
using Microsoft.Data.Sqlite;
using TokenUsage.Core.Automation;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;
using TokenUsage.Providers.Codex;

namespace TokenUsage.Providers.Tests.Codex;

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

    [Theory]
    [InlineData("gpt-5.5", "gpt-5.5", 0.0071)]
    [InlineData("gpt-6-astra", "gpt-6-astra", 0.0132)]
    [InlineData("openai/gpt-6-astra-max", "gpt-6-astra", 0.0132)]
    public async Task ReadsContentFreeCountersAndPricesKnownModels(
        string model, string expectedModel, decimal expectedCost)
    {
        using var corpus = new CodexCorpus();
        corpus.WriteSession(
            "session-a",
            Context(model),
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
        Assert.Equal(expectedModel, usageEvent.ModelId.Value);
        Assert.Equal(new TokenBreakdown(800, 60, 40, 200, 0), usageEvent.Tokens);
        Assert.Equal(CostKind.CatalogEstimated, usageEvent.Cost.Kind);
        Assert.Equal(expectedCost, usageEvent.Cost.EstimatedCostUsd);
        Assert.Equal(CodexPricingCatalog.Version, usageEvent.Cost.CatalogVersion);
        Assert.Equal(CoverageKind.Partial, usageEvent.Coverage);
        Assert.Equal(64, usageEvent.EventKey.Value.Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PricesUsageAtItsDateAcrossTheSolPromotionCutoff(bool useCheckpoints)
    {
        using var corpus = new CodexCorpus();
        string promoPath = corpus.WriteSession("promo", Context("gpt-5.6-sol"),
            Usage("2026-11-21T12:00:00Z", 100_000, 0, 10_000, 0));
        string listPath = corpus.WriteSession("list", Context("gpt-5.6-sol"),
            Usage("2026-11-22T12:00:00Z", 100_000, 0, 10_000, 0));
        DateTimeOffset observedAt = new(2026, 11, 23, 0, 0, 0, TimeSpan.Zero);
        File.SetLastWriteTimeUtc(promoPath, observedAt.UtcDateTime);
        File.SetLastWriteTimeUtc(listPath, observedAt.UtcDateTime);
        var usage = new CodexTokenUsageSnapshot(
            new CodexUsageSummary(null, null, null, null, null), []);
        CodexUsageEventSource source = corpus.CreateSource(
            clientFactory: useCheckpoints ? new StubFactory(new StubClient(usage)) : null,
            checkpointPath: useCheckpoints ? Path.Combine(corpus.Root, "usage.json") : null,
            clock: new FixedTimeProvider(observedAt));

        UsageSourceReadResult first = await source.ReadAsync();
        Assert.Collection(first.Events.OrderBy(item => item.OccurredAtUtc),
            item => Assert.Equal(0.6m, item.Cost.EstimatedCostUsd),
            item =>
            {
                Assert.Equal(CostKind.Unavailable, item.Cost.Kind);
                Assert.Null(item.Cost.EstimatedCostUsd);
            });

        // Reusing persisted counters must reproduce the same historical prices.
        UsageSourceReadResult second = await source.ReadAsync();
        Assert.Equal(first.Events, second.Events);
    }

    [Fact]
    public async Task KeepsObservedModelForEachDeltaInsteadOfAssigningCumulativeToLastModel()
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
        Assert.Equal(UsageSourceReadStatus.Complete, result.Status);
        Assert.Collection(result.Events,
            item => { Assert.Equal("gpt-5.6-terra", item.ModelId.Value); Assert.Equal(110, item.Tokens.Total); },
            item => { Assert.Equal("gpt-5.6-luna", item.ModelId.Value); Assert.Equal(220, item.Tokens.Total); });
        Assert.All(result.Events, item => Assert.Equal(UsageTimePrecision.Timestamp, item.TimePrecision));
    }

    [Fact]
    public async Task StateIndexCurrentModelDoesNotInventHistoricalObservationIdentity()
    {
        using var corpus = new CodexCorpus();
        string path = corpus.WriteSession(
            "indexed-session",
            JsonSerializer.Serialize(new { ignored = new string('x', 16 * 1024) }),
            Usage("2026-07-27T12:01:00Z", 100, 20, 10, 2));
        corpus.WriteStateIndex((path, "gpt-5.4-mini"));

        UsageEvent usageEvent = Assert.Single((await corpus.CreateSource().ReadAsync()).Events);

        Assert.Equal("unknown", usageEvent.ModelId.Value);
        Assert.Equal(CostKind.Unavailable, usageEvent.Cost.Kind);
        Assert.Null(usageEvent.ObservedModelId);
    }

    [Fact]
    public async Task StateIndexAcceptsWindowsExtendedLengthSessionPaths()
    {
        using var corpus = new CodexCorpus();
        string path = corpus.WriteSession(
            "extended-path-session",
            Usage("2026-07-27T12:01:00Z", 100, 20, 10, 2));
        corpus.WriteStateIndex(($@"\\?\{path}", "gpt-5.6-sol"));

        UsageEvent usageEvent = Assert.Single((await corpus.CreateSource().ReadAsync()).Events);

        Assert.Equal(110, usageEvent.Tokens.Total);
        Assert.Equal("unknown", usageEvent.ModelId.Value);
    }

    [Fact]
    public async Task StateIndexDoesNotHideARecentlyCreatedSession()
    {
        using var corpus = new CodexCorpus();
        string indexed = corpus.WriteSession(
            "indexed-session",
            Context("gpt-5.6-sol"),
            Usage("2026-07-27T12:01:00Z", 100, 20, 10, 2));
        corpus.WriteSession(
            "new-session",
            Context("gpt-5.6-luna"),
            Usage("2026-07-27T12:02:00Z", 200, 40, 20, 4));
        corpus.WriteStateIndex((indexed, "gpt-5.6-sol"));

        UsageEvent[] events = (await corpus.CreateSource().ReadAsync()).Events
            .OrderBy(usageEvent => usageEvent.ModelId.Value, StringComparer.Ordinal)
            .ToArray();

        Assert.Collection(
            events,
            usageEvent => Assert.Equal("gpt-5.6-luna", usageEvent.ModelId.Value),
            usageEvent => Assert.Equal("gpt-5.6-sol", usageEvent.ModelId.Value));
    }

    [Fact]
    public async Task OfficialDailyTotalsRemainSeparateFromObservedModelTokens()
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
            clientFactory: new StubFactory(new StubClient(usage)),
            clock: new FixedTimeProvider(new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero)));

        UsageSourceReadResult result = await source.ReadAsync();
        UsageEvent usageEvent = Assert.Single(result.Events);

        Assert.Equal(SourceKind.OfficialLocalApi, source.SourceKind);
        Assert.Equal(UsageSourceReadStatus.Complete, result.Status);
        Assert.Equal("gpt-5.4-mini", usageEvent.ModelId.Value);
        Assert.Equal(110, usageEvent.Tokens.Total);
        Assert.Equal(1_000, Assert.Single(result.AccountAggregates).Tokens);
        Assert.Equal(CostKind.CatalogEstimated, usageEvent.Cost.Kind);
        Assert.Equal(CoverageKind.Partial, usageEvent.Coverage);
    }

    [Fact]
    public async Task AccountDayDoesNotInventEventTime()
    {
        using var corpus = new CodexCorpus();
        DateTimeOffset observedAt = new(2026, 7, 28, 4, 0, 0, TimeSpan.Zero);
        var usage = new CodexTokenUsageSnapshot(
            new CodexUsageSummary(null, null, null, null, null),
            [new CodexUsageDailyBucket(new DateOnly(2026, 7, 28), 1_000)]);
        CodexUsageEventSource source = corpus.CreateSource(
            clientFactory: new StubFactory(new StubClient(usage)),
            clock: new FixedTimeProvider(observedAt));

        UsageSourceReadResult result = await source.ReadAsync();
        Assert.Empty(result.Events);
        AccountUsageAggregate account = Assert.Single(result.AccountAggregates);
        Assert.Equal(new DateOnly(2026, 7, 28), account.ProviderDate);
        Assert.Equal(observedAt, account.ObservedAtUtc);
    }

    [Fact]
    public async Task OfficialAccountHistoryIsRetainedWithoutLocalModelOrTimeAllocation()
    {
        using var corpus = new CodexCorpus();
        var usage = new CodexTokenUsageSnapshot(
            new CodexUsageSummary(null, null, null, null, null),
            [
                new CodexUsageDailyBucket(new DateOnly(2025, 9, 17), 28_206_001),
                new CodexUsageDailyBucket(new DateOnly(2026, 7, 27), 1_000),
            ]);
        CodexUsageEventSource source = corpus.CreateSource(
            clientFactory: new StubFactory(new StubClient(usage)),
            clock: new FixedTimeProvider(new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero)));

        UsageSourceReadResult result = await source.ReadAsync();

        Assert.Empty(result.Events);
        Assert.Equal(2, result.AccountAggregates.Count);
        Assert.Equal(28_207_001, result.AccountAggregates.Sum(item => item.Tokens));
    }

    [Fact]
    public async Task OfficialUsageKeepsRecentLocalTotalsUntilDailyBucketsSettle()
    {
        using var corpus = new CodexCorpus();
        corpus.WriteSession(
            "latest-official-day",
            Context("gpt-5.6-sol"),
            Usage(
                "2026-07-27T22:01:00Z",
                input: 280,
                cachedInput: 40,
                output: 20,
                reasoningOutput: 4));
        corpus.WriteSession(
            "newer-local-day",
            Context("gpt-5.6-sol"),
            Usage(
                "2026-07-28T12:01:00Z",
                input: 450,
                cachedInput: 90,
                output: 50,
                reasoningOutput: 10));
        var usage = new CodexTokenUsageSnapshot(
            new CodexUsageSummary(null, null, null, null, null),
            [new CodexUsageDailyBucket(new DateOnly(2026, 7, 27), 100)]);
        CodexUsageEventSource source = corpus.CreateSource(
            clientFactory: new StubFactory(new StubClient(usage)),
            clock: new FixedTimeProvider(new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero)));

        UsageSourceReadResult result = await source.ReadAsync();
        UsageEvent[] events = result.Events
            .OrderBy(usageEvent => usageEvent.OccurredAtUtc)
            .ToArray();

        Assert.Equal(UsageSourceReadStatus.Complete, result.Status);
        Assert.Collection(
            events,
            usageEvent => Assert.Equal(300, usageEvent.Tokens.Total),
            usageEvent => Assert.Equal(500, usageEvent.Tokens.Total));
        Assert.All(events, usageEvent => Assert.Equal(CoverageKind.Partial, usageEvent.Coverage));
    }

    [Fact]
    public async Task IncrementalScanAttributesCumulativeDeltasToTheirActualDay()
    {
        using var corpus = new CodexCorpus();
        string sessionPath = corpus.WriteSession(
            "session-across-midnight",
            Context("gpt-5.6-sol"),
            Usage(
                "2026-07-27T22:01:00Z",
                input: 90,
                cachedInput: 20,
                output: 10,
                reasoningOutput: 2,
                totalInput: 90,
                totalCachedInput: 20,
                totalOutput: 10,
                totalReasoningOutput: 2),
            Usage(
                "2026-07-27T23:59:00Z",
                input: 180,
                cachedInput: 40,
                output: 20,
                reasoningOutput: 4,
                totalInput: 280,
                totalCachedInput: 60,
                totalOutput: 20,
                totalReasoningOutput: 4),
            Usage(
                "2026-07-28T00:01:00Z",
                input: 190,
                cachedInput: 50,
                output: 10,
                reasoningOutput: 2,
                totalInput: 470,
                totalCachedInput: 110,
                totalOutput: 30,
                totalReasoningOutput: 6));
        var usage = new CodexTokenUsageSnapshot(
            new CodexUsageSummary(null, null, null, null, null),
            [new CodexUsageDailyBucket(new DateOnly(2026, 7, 27), 100)]);
        CodexUsageEventSource source = corpus.CreateSource(
            clientFactory: new StubFactory(new StubClient(usage)),
            checkpointPath: Path.Combine(corpus.Root, "codex-usage.v1.json"),
            clock: new FixedTimeProvider(new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero)));

        UsageSourceReadResult first = await source.ReadAsync();
        Assert.Collection(
            first.Events.OrderBy(usageEvent => usageEvent.OccurredAtUtc),
            usageEvent => Assert.Equal(100, usageEvent.Tokens.Total),
            usageEvent => { Assert.Equal(200, usageEvent.Tokens.Total); Assert.Equal(UsageTimePrecision.Interval, usageEvent.TimePrecision); Assert.Equal("unknown", usageEvent.ModelId.Value); Assert.Equal(UsageRecordKind.IntervalDelta, usageEvent.DetailMetadata.RecordKind); },
            usageEvent => Assert.Equal(200, usageEvent.Tokens.Total));

        File.AppendAllLines(
            sessionPath,
            [Usage(
                "2026-07-28T01:01:00Z",
                input: 40,
                cachedInput: 10,
                output: 10,
                reasoningOutput: 2,
                totalInput: 510,
                totalCachedInput: 120,
                totalOutput: 40,
                totalReasoningOutput: 8)]);
        UsageSourceReadResult second = await source.ReadAsync();

        Assert.Collection(
            second.Events.OrderBy(usageEvent => usageEvent.OccurredAtUtc),
            usageEvent => Assert.Equal(100, usageEvent.Tokens.Total),
            usageEvent => Assert.Equal(200, usageEvent.Tokens.Total),
            usageEvent => Assert.Equal(200, usageEvent.Tokens.Total),
            usageEvent => Assert.Equal(50, usageEvent.Tokens.Total));
    }

    [Fact]
    public async Task IncrementalScanSkipsReplayedParentUsageUntilChildStartsLiveWork()
    {
        using var corpus = new CodexCorpus();
        string sessionPath = corpus.WriteSession(
            "child-session",
            ChildSessionMeta("2026-07-28T10:00:00Z"),
            Context("gpt-5.6-sol"),
            Usage(
                "2026-07-28T10:00:01Z",
                input: 900,
                cachedInput: 200,
                output: 100,
                reasoningOutput: 20,
                totalInput: 900,
                totalCachedInput: 200,
                totalOutput: 100,
                totalReasoningOutput: 20));
        var usage = new CodexTokenUsageSnapshot(
            new CodexUsageSummary(null, null, null, null, null),
            [new CodexUsageDailyBucket(new DateOnly(2026, 7, 28), 0)]);
        CodexUsageEventSource source = corpus.CreateSource(
            clientFactory: new StubFactory(new StubClient(usage)),
            checkpointPath: Path.Combine(corpus.Root, "codex-usage.v1.json"),
            clock: new FixedTimeProvider(new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero)));

        Assert.Empty((await source.ReadAsync()).Events);

        File.AppendAllLines(
            sessionPath,
            [
                TaskStarted("2026-07-28T10:05:00Z", 1_785_233_100),
                Usage(
                    "2026-07-28T10:05:01Z",
                    input: 50,
                    cachedInput: 10,
                    output: 5,
                    reasoningOutput: 1,
                    totalInput: 950,
                    totalCachedInput: 210,
                    totalOutput: 105,
                    totalReasoningOutput: 21),
            ]);

        UsageEvent usageEvent = Assert.Single((await source.ReadAsync()).Events);
        Assert.Equal(55, usageEvent.Tokens.Total);
    }

    [Fact]
    public async Task FirstTurnOfAResumedSessionUsesLastUsageNotCarriedCumulative()
    {
        using var corpus = new CodexCorpus();
        corpus.WriteSession(
            "session-resume",
            Context("gpt-5.6-sol"),
            Usage(
                "2026-07-27T12:01:00Z",
                input: 40,
                cachedInput: 10,
                output: 10,
                reasoningOutput: 2,
                totalInput: 10_040,
                totalCachedInput: 2_010,
                totalOutput: 1_010,
                totalReasoningOutput: 202));

        UsageEvent usageEvent = Assert.Single((await corpus.CreateSource().ReadAsync()).Events);

        Assert.Equal(new TokenBreakdown(30, 8, 2, 10, 0), usageEvent.Tokens);
    }

    [Fact]
    public async Task StaleRegressedCumulativeSnapshotDoesNotRebillTheSession()
    {
        using var corpus = new CodexCorpus();
        corpus.WriteSession(
            "session-stale",
            Context("gpt-5.6-sol"),
            Usage(
                "2026-07-27T12:01:00Z",
                input: 90,
                cachedInput: 20,
                output: 10,
                reasoningOutput: 2,
                totalInput: 90,
                totalCachedInput: 20,
                totalOutput: 10,
                totalReasoningOutput: 2),
            Usage(
                "2026-07-27T12:01:01Z",
                input: 80,
                cachedInput: 18,
                output: 9,
                reasoningOutput: 1,
                totalInput: 80,
                totalCachedInput: 18,
                totalOutput: 9,
                totalReasoningOutput: 1),
            Usage(
                "2026-07-27T12:01:02Z",
                input: 50,
                cachedInput: 10,
                output: 5,
                reasoningOutput: 1,
                totalInput: 140,
                totalCachedInput: 30,
                totalOutput: 15,
                totalReasoningOutput: 3));

        IReadOnlyList<UsageEvent> events = (await corpus.CreateSource().ReadAsync()).Events;
        long total = events.Sum(usageEvent => usageEvent.Tokens.Total);

        Assert.Equal(155, total);
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

    [Theory]
    [InlineData("response_item", "agent_message")]
    [InlineData("event_msg", "agent_message")]
    [InlineData("event_msg", "item_completed")]
    public async Task OversizedNonUsageMessagesKeepTheScanComplete(string recordType, string payloadType)
    {
        using var corpus = new CodexCorpus();
        string oversizedContent = JsonSerializer.Serialize(new
        {
            type = recordType,
            payload = new { type = payloadType, text = "token_count " + new string('x', 70 * 1024) },
        });
        corpus.WriteSession(
            "session-large-content",
            oversizedContent,
            Context("gpt-5.6-sol"),
            Usage("2026-07-27T12:01:00Z", 100, 20, 10, 2));
        CodexUsageEventSource source = corpus.CreateSource(
            checkpointPath: Path.Combine(corpus.Root, "codex-usage.v1.json"),
            clock: new FixedTimeProvider(
                new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero)));

        UsageSourceReadResult result = await source.ReadAsync();

        Assert.Single(result.Events);
        Assert.Equal(UsageSourceReadStatus.Complete, result.Status);
        Assert.Equal(UsageSourceIssueKind.None, result.Issue);
    }

    [Fact]
    public async Task InvalidLastBreakdownKeepsTheValidCumulativeCounter()
    {
        using var corpus = new CodexCorpus();
        corpus.WriteSession(
            "session-partial-breakdown",
            Context("gpt-5.5"),
            UsageWithInvalidLastBreakdown());

        UsageSourceReadResult result = await corpus.CreateSource().ReadAsync();
        UsageEvent usageEvent = Assert.Single(result.Events);

        Assert.Equal(new TokenBreakdown(80, 18, 2, 20, 0), usageEvent.Tokens);
        // Every cumulative token is counted; only precision is incomplete.
        Assert.Equal(UsageSourceReadStatus.Complete, result.Status);
        Assert.Equal(UsageSourceIssueKind.None, result.Issue);
        Assert.Equal(UsageTimePrecision.Unknown, usageEvent.TimePrecision);
        Assert.Equal(UsageRecordKind.Snapshot, usageEvent.DetailMetadata.RecordKind);
        Assert.Equal(1, usageEvent.DetailMetadata.RepresentationRevision);
        Assert.Equal("unknown", usageEvent.ModelId.Value);
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
    public void PricingAppliesThePublishedGpt54LongContextMultiplier()
    {
        CostObservation cost = CodexPricingCatalog.Resolve(
            "gpt-5.4",
            new TokenBreakdown(272_001, 100, 0, 0, 0));

        Assert.Equal(CostKind.CatalogEstimated, cost.Kind);
        Assert.Equal(1.362255m, cost.EstimatedCostUsd);
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

    [Fact]
    public async Task ObservationReplayPreservesResetSplitAndAccountSeparationAcrossModelChanges()
    {
        using var corpus = new CodexCorpus();
        string path = corpus.WriteSession("split", Context("gpt-5.6-sol"),
            Usage("2026-07-27T09:00:00Z", 600, 0, 0, 0),
            Context("gpt-5.6-luna"),
            Usage("2026-07-27T13:00:00Z", 400, 0, 0, 0, totalInput: 1000));
        string checkpoint = Path.Combine(corpus.Root, "checkpoint.json");
        var source = corpus.CreateSource(checkpointPath: checkpoint);
        UsageSourceReadResult beforeCommit = await source.ReadAsync();
        Assert.False(File.Exists(checkpoint));
        // An uncommitted read must replay the same numeric identities after restart.
        UsageSourceReadResult replay = await corpus.CreateSource(checkpointPath: checkpoint).ReadAsync();
        Assert.Equal(beforeCommit.Events, replay.Events);
        var repository = await UsageRepository.OpenAsync(Path.Combine(corpus.Root, "usage.db"));
        var refresh = new LocalUsageRefresh(repository.DatabasePath, source,
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero)));
        await using (var failure = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={repository.DatabasePath};Pooling=False"))
        {
            await failure.OpenAsync();
            await using var command = failure.CreateCommand();
            command.CommandText = "CREATE TRIGGER fail_ingest BEFORE INSERT ON usage_event BEGIN SELECT RAISE(ABORT, 'synthetic failure'); END;";
            await command.ExecuteNonQueryAsync();
            await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => refresh.RefreshAsync());
            Assert.False(File.Exists(checkpoint));
            command.CommandText = "DROP TRIGGER fail_ingest;";
            await command.ExecuteNonQueryAsync();
        }
        await refresh.RefreshAsync();
        Assert.True(File.Exists(checkpoint));
        Assert.NotNull(beforeCommit.Checkpoint);
        await Assert.ThrowsAsync<IOException>(() => beforeCommit.Checkpoint.PersistAsync(() => Task.FromResult(true)));
        await repository.IngestAsync(replay.Events);
        await repository.UpsertAccountUsageAsync([new(new AgentId("codex"), new DateOnly(2026, 7, 27), 5_000,
            new DateTimeOffset(2026, 7, 28, 0, 0, 0, TimeSpan.Zero))]);
        var query = new TokenUsage.Core.Automation.UsageReportQuery(Path.Combine(corpus.Root, "usage.db"));
        var from = new DateTimeOffset(2026, 7, 27, 0, 0, 0, TimeSpan.Zero);
        var a = await query.ReadExactAsync(from, from.AddHours(11));
        var b = await query.ReadExactAsync(from.AddHours(11), from.AddDays(1));
        Assert.Equal(600, a.Totals.Tokens.Total);
        Assert.Equal("gpt-5.6-sol", Assert.Single(a.Models).ModelId.Value);
        Assert.Equal(400, b.Totals.Tokens.Total);
        Assert.Equal("gpt-5.6-luna", Assert.Single(b.Models).ModelId.Value);
        var daily = await query.ReadAsync(new DateOnly(2026, 7, 27), new DateOnly(2026, 7, 27));
        Assert.Equal(1_000, daily.Totals.Tokens.Total);
        Assert.Equal(5_000, Assert.Single(daily.AccountUsage).Tokens);
        Directory.CreateDirectory(Path.Combine(corpus.Root, "archived_sessions"));
        File.Move(path, Path.Combine(corpus.Root, "archived_sessions", "split.jsonl"));
        Assert.Equal(source.SourceAuthority,
            new CodexUsageEventSource("UTC", codexHomeOverride: Path.Combine(corpus.Root, ".")).SourceAuthority);
        Assert.NotEqual(source.SourceAuthority,
            new CodexUsageEventSource("UTC", codexHomeOverride: Path.Combine(corpus.Root, "separate-profile")).SourceAuthority);
        Assert.Equal(replay.Events, (await source.ReadAsync()).Events);
        string json = await File.ReadAllTextAsync(checkpoint);
        Assert.Contains(source.SourceAuthority.Value, json, StringComparison.Ordinal);
        var differentProfile = new CodexUsageEventSource("UTC", codexHomeOverride: Path.Combine(corpus.Root, "other-profile"), checkpointPath: checkpoint);
        await Assert.ThrowsAsync<InvalidDataException>(() => differentProfile.ReadAsync());
        Assert.Equal(json, await File.ReadAllTextAsync(checkpoint));
        Assert.DoesNotContain("private-project-path", json);
        Assert.DoesNotContain("private fixture summary", json);
    }

    [Fact]
    public async Task TrimmedSessionRetainsEarlierObservationsAndResumesFromItsWatermark()
    {
        using var corpus = new CodexCorpus();
        string path = corpus.WriteSession("trim", Context("gpt-5.6-sol"),
            Usage("2026-07-27T09:00:00Z", 600, 0, 0, 0),
            Usage("2026-07-27T13:00:00Z", 400, 0, 0, 0, totalInput: 1000));
        string checkpoint = Path.Combine(corpus.Root, "trim-checkpoint.json");
        UsageSourceReadResult first = await corpus.CreateSource(checkpointPath: checkpoint).ReadAsync();
        await first.Checkpoint!.PersistAsync(() => Task.FromResult(true));
        await File.WriteAllTextAsync(path, Context("gpt-5.6-sol") + "\n"
            + Usage("2026-07-27T13:00:00Z", 400, 0, 0, 0, totalInput: 1000) + "\n");
        Assert.Equal(first.Events, (await corpus.CreateSource(checkpointPath: checkpoint).ReadAsync()).Events);
        await File.AppendAllTextAsync(path, Usage("2026-07-27T14:00:00Z", 200, 0, 0, 0, totalInput: 1200) + "\n");
        UsageSourceReadResult resumed = await corpus.CreateSource(checkpointPath: checkpoint).ReadAsync();
        Assert.Equal(3, resumed.Events.Count);
        Assert.Equal(1200, resumed.Events.Sum(item => item.Tokens.Total));
        Assert.Equal(first.Events.Select(item => item.EventKey), resumed.Events.Take(2).Select(item => item.EventKey));
    }

    [Fact]
    public async Task LargeObservationCheckpointSurvivesRestartWithoutDroppingOrDuplicatingUsage()
    {
        using var corpus = new CodexCorpus();
        const int count = 150_000;
        var start = new DateTimeOffset(2026, 7, 26, 0, 0, 0, TimeSpan.Zero);
        string session = corpus.WriteSession("large", Context("gpt-5.6-sol"));
        using (var writer = File.AppendText(session))
            for (int index = 0; index < count; index++)
                writer.WriteLine(Usage(start.AddSeconds(index).ToString("O"), 1, 0, 0, 0, totalInput: index + 1));

        string checkpoint = Path.Combine(corpus.Root, "large-checkpoint.json");
        var first = await corpus.CreateSource(checkpointPath: checkpoint).ReadAsync();
        await first.Checkpoint!.PersistAsync(() => Task.FromResult(true));
        Assert.Equal(count, first.Events.Count);
        Assert.True(new FileInfo(checkpoint).Length > 32 * 1024 * 1024);
        var replay = await corpus.CreateSource(checkpointPath: checkpoint).ReadAsync();
        Assert.Equal(first.Events, replay.Events);
        Assert.Equal(count, replay.Events.Sum(item => item.Tokens.Total));
    }

    [Theory]
    [InlineData(3)]
    public async Task LegacyCheckpointMigrationReplaysObservationsAndPreservesOriginalBytes(int legacyVersion)
    {
        using var corpus = new CodexCorpus();
        corpus.WriteSession("migrate", Context("gpt-5.6-sol"), Usage("2026-07-27T09:00:00Z", 600, 0, 0, 0));
        string path = Path.Combine(corpus.Root, "migration.json");
        var first = await corpus.CreateSource(checkpointPath: path).ReadAsync();
        await first.Checkpoint!.PersistAsync(() => Task.FromResult(true));
        var old = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        old["schemaVersion"] = legacyVersion;
        old.AsObject().Remove("sourceAuthority");
        foreach (var file in old["files"]!.AsArray()) file!.AsObject().Remove("authorityPathHash");
        string legacyBytes = old.ToJsonString();
        await File.WriteAllTextAsync(path, legacyBytes);
        var migrated = await corpus.CreateSource(checkpointPath: path).ReadAsync();
        await migrated.Checkpoint!.PersistAsync(() => Task.FromResult(true));
        var expected = legacyVersion == 2 ? first.Events : first.Events.Select(row => new UsageEvent(
            row.EventKey, row.AgentId, row.ModelProviderId, row.ModelId, row.OccurredAtUtc,
            row.GroupingTimeZoneId, row.Tokens, row.Cost, row.ParserVersion, row.Coverage,
            row.TimePrecision, row.IntervalStartedAtUtc, row.ObservedModelId, row.ReasoningEffort, row.ServiceTier,
            new UsageDetailMetadata(row.DetailMetadata.SourceInstance, row.DetailMetadata.RecordKind))).ToArray();
        Assert.Equal(expected, migrated.Events);
        Assert.Equal(UsageSourceReadStatus.Complete, migrated.Status);
        Assert.Equal(UsageSourceIssueKind.None, migrated.Issue);
        Assert.Equal(600, migrated.Events.Sum(row => row.Tokens.Total));
        Assert.Equal(legacyBytes, await File.ReadAllTextAsync(path + ".pre-v4"));
        var upgraded = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        Assert.Equal(corpus.CreateSource().SourceAuthority.Value, upgraded["sourceAuthority"]!.GetValue<string>());
    }

    [Fact]
    public async Task CleanupMigratesLegacyHistoryAndMergesNewSessionsWithoutDuplicates()
    {
        using var corpus = new CodexCorpus();
        string retained = corpus.WriteSession("retained", Context("gpt-5.6-sol"),
            Usage("2026-07-27T09:00:00Z", 600, 0, 0, 0),
            Usage("2026-07-27T13:00:00Z", 100, 0, 0, 0, totalInput: 1000));
        string deleted = corpus.WriteSession("deleted", Context("gpt-5.6-sol"),
            Usage("2026-07-27T09:30:00Z", 200, 0, 0, 0));
        string checkpoint = Path.Combine(corpus.Root, "cleanup.json");
        var first = await corpus.CreateSource(checkpointPath: checkpoint).ReadAsync();
        await first.Checkpoint!.PersistAsync(() => Task.FromResult(true));
        var legacy = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(checkpoint))!;
        legacy["schemaVersion"] = 3;
        legacy.AsObject().Remove("sourceAuthority");
        foreach (var file in legacy["files"]!.AsArray()) file!.AsObject().Remove("authorityPathHash");
        string legacyBytes = legacy.ToJsonString();
        await File.WriteAllTextAsync(checkpoint, legacyBytes);
        string database = Path.Combine(corpus.Root, "usage.db");
        UsageRepository repository = await UsageRepository.OpenAsync(database);
        UsageEvent[] history = first.Events.Select(row => new UsageEvent(
            row.EventKey, row.AgentId, row.ModelProviderId, row.ModelId, row.OccurredAtUtc,
            row.GroupingTimeZoneId, row.Tokens, row.Cost, row.ParserVersion, row.Coverage,
            row.TimePrecision, row.IntervalStartedAtUtc, row.ObservedModelId, row.ReasoningEffort,
            row.ServiceTier)).ToArray();
        await repository.IngestAsync(history);
        File.Delete(deleted);
        corpus.WriteStateIndex((retained, "gpt-5.6-sol"),
            (Path.Combine(corpus.Root, "sessions", "already-cleaned.jsonl"), "gpt-5.6-sol"));
        string added = corpus.WriteSession("new", Context("gpt-5.6-sol"),
            Usage("2026-07-27T14:00:00Z", 300, 0, 0, 0));
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero));
        var refresh = new LocalUsageRefresh(database, corpus.CreateSource(checkpointPath: checkpoint), clock);
        var merged = await refresh.RefreshAsync();
        Assert.Equal(UsageSourceReadStatus.Complete, merged.OverallStatus);
        Assert.Equal(1500, merged.Rollups.Sum(row => row.Tokens.Total));
        Assert.Equal(legacyBytes, await File.ReadAllTextAsync(checkpoint + ".pre-v4"));
        var upgraded = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(checkpoint))!;
        Assert.Equal(4, upgraded["schemaVersion"]!.GetValue<int>());
        Assert.NotNull(upgraded["sourceAuthority"]);

        // After migration even a complete source cleanup must keep the saved history.
        File.Delete(retained);
        File.Delete(added);
        File.Delete(Path.Combine(corpus.Root, "state_5.sqlite"));
        string recreated = corpus.WriteSession("after-cleanup", Context("gpt-5.6-sol"),
            Usage("2026-07-27T15:00:00Z", 400, 0, 0, 0));
        var restarted = new LocalUsageRefresh(database, corpus.CreateSource(checkpointPath: checkpoint), clock);
        Assert.Equal(1900, (await restarted.RefreshAsync()).Rollups.Sum(row => row.Tokens.Total));
        File.Copy(recreated, Path.Combine(Directory.CreateDirectory(Path.Combine(corpus.Root, "archived_sessions")).FullName,
            Path.GetFileName(recreated)));
        Assert.Equal(1900, (await restarted.RefreshAsync()).Rollups.Sum(row => row.Tokens.Total));
        var stored = await repository.QueryUsageEventsAsync(clock.GetUtcNow().AddDays(-2), clock.GetUtcNow());
        Assert.Equal(5, stored.Count);
        foreach (UsageEvent row in history)
        {
            UsageEvent preserved = Assert.Single(stored, item => item.EventKey == row.EventKey);
            Assert.Equal(row.Tokens, preserved.Tokens);
            Assert.Equal(row.Cost, preserved.Cost);
            Assert.Equal(UsageRecordKind.Unknown, preserved.DetailMetadata.RecordKind);
            Assert.Equal(UsageComponentAvailability.Unknown, preserved.DetailMetadata.Input);
        }
    }

    [Fact]
    public async Task ComponentEvidenceDistinguishesMissingCountersAndSurvivesCheckpointedDeltas()
    {
        using var corpus = new CodexCorpus();
        var first = System.Text.Json.Nodes.JsonNode.Parse(Usage("2026-07-27T09:00:00Z", 600, 0, 10, 0))!;
        foreach (string part in new[] { "last_token_usage", "total_token_usage" })
            foreach (string component in new[] { "cached_input_tokens", "cache_write_input_tokens", "reasoning_output_tokens" })
                first["payload"]!["info"]![part]!.AsObject().Remove(component);
        var second = System.Text.Json.Nodes.JsonNode.Parse(Usage("2026-07-27T10:00:00Z", 100, 0, 0, 0,
            totalInput: 700, totalOutput: 10))!;
        second["payload"]!["info"]!.AsObject().Remove("last_token_usage");
        string session = corpus.WriteSession("component-evidence", Context("gpt-5.6-sol"), first.ToJsonString(), second.ToJsonString());
        string checkpoint = Path.Combine(corpus.Root, "components.json");
        UsageSourceReadResult initial = await corpus.CreateSource(checkpointPath: checkpoint).ReadAsync();
        Assert.Equal(2, initial.Events.Count);
        foreach (UsageEvent row in initial.Events)
        {
            Assert.Equal(UsageComponentAvailability.Unknown, row.DetailMetadata.Input);
            Assert.Equal(UsageComponentAvailability.Unknown, row.DetailMetadata.Output);
            Assert.Equal(UsageComponentAvailability.Unknown, row.DetailMetadata.CacheRead);
            Assert.Equal(UsageComponentAvailability.Unknown, row.DetailMetadata.CacheWrite);
            Assert.Equal(UsageComponentAvailability.Unknown, row.DetailMetadata.Reasoning);
        }
        await initial.Checkpoint!.PersistAsync(() => Task.FromResult(true));
        var third = System.Text.Json.Nodes.JsonNode.Parse(Usage("2026-07-27T11:00:00Z", 100, 0, 0, 0,
            totalInput: 800, totalOutput: 10))!;
        third["payload"]!["info"]!.AsObject().Remove("last_token_usage");
        await File.AppendAllTextAsync(session, third.ToJsonString() + "\n");
        UsageSourceReadResult continued = await corpus.CreateSource(checkpointPath: checkpoint).ReadAsync();
        Assert.Equal(3, continued.Events.Count);
        UsageEvent measured = continued.Events[^1];
        Assert.Equal(UsageRecordKind.IntervalDelta, measured.DetailMetadata.RecordKind);
        Assert.Equal(UsageComponentAvailability.Measured, measured.DetailMetadata.Input);
        Assert.Equal(UsageComponentAvailability.Measured, measured.DetailMetadata.Output);
        Assert.Equal(UsageComponentAvailability.Measured, measured.DetailMetadata.CacheRead);
        Assert.Equal(UsageComponentAvailability.Measured, measured.DetailMetadata.CacheWrite);
        Assert.Equal(UsageComponentAvailability.Measured, measured.DetailMetadata.Reasoning);
        Assert.Equal(0, measured.Tokens.CacheRead);
        Assert.Equal(810, continued.Events.Sum(row => row.Tokens.Total));
        await continued.Checkpoint!.PersistAsync(() => Task.FromResult(true));
        Assert.Equal(continued.Events, (await corpus.CreateSource(checkpointPath: checkpoint).ReadAsync()).Events);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReclassifiedComponentsConserveInclusiveDeltasAndUseOnlyProvenSplits(bool hasMatchingLast)
    {
        using var corpus = new CodexCorpus();
        string session = corpus.WriteSession("reclassified", Context("gpt-5.6-sol"),
            Usage("2026-07-27T09:00:00Z", 600, 0, 100, 0));
        string checkpoint = Path.Combine(corpus.Root, "reclassified.json");
        UsageSourceReadResult first = await corpus.CreateSource(checkpointPath: checkpoint).ReadAsync();
        await first.Checkpoint!.PersistAsync(() => Task.FromResult(true));
        var next = System.Text.Json.Nodes.JsonNode.Parse(Usage("2026-07-27T10:00:00Z", 100, 25, 20, 5,
            totalInput: 700, totalCachedInput: 200, totalOutput: 120, totalReasoningOutput: 100))!;
        if (!hasMatchingLast) next["payload"]!["info"]!.AsObject().Remove("last_token_usage");
        await File.AppendAllTextAsync(session, next.ToJsonString() + "\n");
        var source = corpus.CreateSource(checkpointPath: checkpoint);
        UsageSourceReadResult read = await source.ReadAsync();
        Assert.Equal(2, read.Events.Count);
        UsageEvent delta = read.Events[1];
        Assert.Equal(120, delta.Tokens.Total);
        Assert.Equal(hasMatchingLast ? new TokenBreakdown(75, 15, 5, 25, 0)
            : new TokenBreakdown(100, 20, 0, 0, 0), delta.Tokens);
        UsageComponentAvailability availability = hasMatchingLast ? UsageComponentAvailability.Measured : UsageComponentAvailability.Unknown;
        Assert.Equal(availability, delta.DetailMetadata.Input);
        Assert.Equal(availability, delta.DetailMetadata.Output);
        Assert.Equal(availability, delta.DetailMetadata.Reasoning);
        Assert.Equal(availability, delta.DetailMetadata.CacheRead);
        Assert.Equal(availability, delta.DetailMetadata.CacheWrite);
        Assert.Equal(hasMatchingLast ? UsageRecordKind.Unknown : UsageRecordKind.IntervalDelta, delta.DetailMetadata.RecordKind);
        string database = Path.Combine(corpus.Root, "reclassified.db");
        var refresh = new LocalUsageRefresh(database, source,
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero)));
        for (int attempt = 0; attempt < 2; attempt++)
        {
            Assert.Equal(820, (await refresh.RefreshAsync()).Rollups.Sum(row => row.Tokens.Total));
            Assert.Equal(read.Events, (await corpus.CreateSource(checkpointPath: checkpoint).ReadAsync()).Events);
        }
    }

    [Fact]
    public async Task CopiedLegacyCheckpointRemainsUnboundAcrossCommittedReplays()
    {
        using var original = new CodexCorpus();
        using var copy = new CodexCorpus();
        foreach (var corpus in new[] { original, copy })
            corpus.WriteSession("copied", Context("gpt-5.6-sol"), Usage("2026-07-27T09:00:00Z", 600, 0, 0, 0));
        string path = Path.Combine(original.Root, "legacy.json");
        var first = await original.CreateSource(checkpointPath: path).ReadAsync();
        await first.Checkpoint!.PersistAsync(() => Task.FromResult(true));
        var old = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        old["schemaVersion"] = 3;
        old.AsObject().Remove("sourceAuthority");
        foreach (var file in old["files"]!.AsArray()) file!.AsObject().Remove("authorityPathHash");
        await File.WriteAllTextAsync(path, old.ToJsonString());
        string originalHash = old["files"]![0]!["pathHash"]!.GetValue<string>();
        for (int attempt = 0; attempt < 2; attempt++)
        {
            var replay = await copy.CreateSource(checkpointPath: path).ReadAsync();
            Assert.Null(replay.SourceInstance);
            await replay.Checkpoint!.PersistAsync(() => Task.FromResult(true));
            var saved = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(path))!;
            Assert.Null(saved["sourceAuthority"]);
            Assert.Equal(originalHash, saved["files"]![0]!["authorityPathHash"]!.GetValue<string>());
            Assert.Equal(600, replay.Events.Sum(item => item.Tokens.Total));
        }
        string unboundCheckpoint = await File.ReadAllTextAsync(path);
        string database = Path.Combine(copy.Root, "unbound-usage.db");
        var refresh = new LocalUsageRefresh(database, copy.CreateSource(checkpointPath: path),
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero)));
        LocalUsageRefreshResult result = await refresh.RefreshAsync();
        Assert.Equal(UsageSourceIssueKind.UnresolvedHistory, Assert.Single(result.SourceDiagnostics).Issue);
        Assert.Empty(result.Rollups);
        Assert.Equal(unboundCheckpoint, await File.ReadAllTextAsync(path));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StalePreparedReadCannotReplaceNewerDurableUsage(bool publishCheckpoint)
    {
        using var corpus = new CodexCorpus();
        string path = corpus.WriteSession("race", Context("gpt-5.6-sol"), Usage("2026-07-27T09:00:00Z", 100, 0, 0, 0));
        string checkpoint = Path.Combine(corpus.Root, "race-checkpoint.json");
        var source = corpus.CreateSource(checkpointPath: checkpoint);
        UsageSourceReadResult old = await source.ReadAsync();
        await File.AppendAllTextAsync(path, Usage("2026-07-27T10:00:00Z", 200, 0, 0, 0, totalInput: 300) + Environment.NewLine);
        UsageSourceReadResult newer = await source.ReadAsync();
        var repository = await UsageRepository.OpenAsync(Path.Combine(corpus.Root, "race.db"));
        DateOnly day = new(2026, 7, 27);
        await newer.Checkpoint!.PersistAsync(async () =>
        {
            await repository.StoreSourceObservationsAsync(source.AgentId, source.SourceAuthority,
                source.EventParserVersion, day, day, newer.Events, complete: publishCheckpoint);
            return publishCheckpoint;
        });
        Assert.Equal(publishCheckpoint, File.Exists(checkpoint));
        UsageDataRevision revision = await repository.ReadDataRevisionAsync();
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero));
        var staleRefresh = new LocalUsageRefresh(repository.DatabasePath, new PreparedSource(source, old), clock);
        await Assert.ThrowsAsync<IOException>(() => staleRefresh.RefreshAsync());
        Assert.Equal(revision, await repository.ReadDataRevisionAsync());
        Assert.Equal(300, (await repository.QueryDailyRollupsAsync(day, day)).Sum(row => row.Tokens.Total));
        Assert.Equal(300, (await new LocalUsageRefresh(repository.DatabasePath, source, clock).RefreshAsync()).Rollups.Sum(row => row.Tokens.Total));
    }

    private sealed class PreparedSource(CodexUsageEventSource source, UsageSourceReadResult result) : ISourceScopedUsageEventSource
    {
        public AgentId AgentId => source.AgentId;
        public SourceKind SourceKind => source.SourceKind;
        public UsageSourceInstanceId SourceInstance => source.SourceAuthority;
        public string EventParserVersion => source.EventParserVersion;
        public int ReconciliationWindowDays => source.ReconciliationWindowDays;
        public Task<UsageSourceReadResult> ReadAsync(CancellationToken cancellationToken = default) => Task.FromResult(result);
    }

    private static string Context(string model, string cwd = "private-project-path") => JsonSerializer.Serialize(new
    {
        timestamp = "2026-07-27T12:00:00Z",
        type = "turn_context",
        payload = new
        {
            model,
            cwd,
            summary = "private fixture summary",
        },
    });

    private static string ChildSessionMeta(string timestamp) => JsonSerializer.Serialize(new
    {
        timestamp,
        type = "session_meta",
        payload = new
        {
            id = "child-session",
            parent_thread_id = "parent-session",
            thread_source = "subagent",
        },
    });

    private static string TaskStarted(string timestamp, long startedAt) => JsonSerializer.Serialize(new
    {
        timestamp,
        type = "event_msg",
        payload = new
        {
            type = "task_started",
            started_at = startedAt,
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

    private static string UsageWithInvalidLastBreakdown() => JsonSerializer.Serialize(new
    {
        timestamp = "2026-07-27T12:01:00Z",
        type = "event_msg",
        payload = new
        {
            type = "token_count",
            info = new
            {
                last_token_usage = new
                {
                    input_tokens = 0,
                    cached_input_tokens = 0,
                    cache_write_input_tokens = 0,
                    output_tokens = 0,
                    reasoning_output_tokens = 0,
                    total_tokens = 22_719,
                },
                total_token_usage = new
                {
                    input_tokens = 100,
                    cached_input_tokens = 20,
                    cache_write_input_tokens = 0,
                    output_tokens = 20,
                    reasoning_output_tokens = 2,
                    total_tokens = 120,
                },
            },
        },
    });

    private sealed class CodexCorpus : IDisposable
    {
        private readonly string _path = Path.Combine(
            Path.GetTempPath(),
            "tokenusage-codex-corpus",
            Guid.NewGuid().ToString("N"));

        public CodexCorpus() => Directory.CreateDirectory(Path.Combine(_path, "sessions"));

        public string Root => _path;

        public CodexUsageEventSource CreateSource(
            int maximumFiles = 100,
            ICodexQuotaClientFactory? clientFactory = null,
            string? checkpointPath = null,
            TimeProvider? clock = null) => new(
            "UTC",
            codexHomeOverride: _path,
            maximumFiles: maximumFiles,
            clientFactory: clientFactory,
            checkpointPath: checkpointPath,
            clock: clock ?? new FixedTimeProvider(new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero)));

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

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

}

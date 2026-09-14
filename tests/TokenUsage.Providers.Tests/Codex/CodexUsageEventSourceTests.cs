using System.Text.Json;
using Microsoft.Data.Sqlite;
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
            item => Assert.Equal(0.8m, item.Cost.EstimatedCostUsd));

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
    [InlineData(false)]
    [InlineData(true)]
    public async Task OversizedMessageContentDoesNotPreventAuthoritativeUsageCollection(bool eventMessage)
    {
        using var corpus = new CodexCorpus();
        string oversizedContent = JsonSerializer.Serialize(new
        {
            type = eventMessage ? "event_msg" : "response_item",
            payload = new { type = "agent_message", text = "token_count " + new string('x', 70 * 1024) },
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
        // Every cumulative token is counted; only attribution/precision is incomplete.
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
    public void Gpt56SolUsesTheOfficialPromotionalRate()
    {
        CostObservation cost = CodexPricingCatalog.Resolve(
            "gpt-5.6-sol",
            new TokenBreakdown(1_000_000, 100_000, 0, 0, 0));

        Assert.Equal(11m, cost.EstimatedCostUsd);
        Assert.Equal("gpt-5.6-sol", cost.ExactPriceMatch);
        Assert.Equal(CodexPricingCatalog.Version, cost.CatalogVersion);
    }

    [Fact]
    public void Gpt56LunaUsesTheOfficialLongContextRateAboveThePublishedLine()
    {
        CostObservation cost = CodexPricingCatalog.Resolve(
            "gpt-5.6-luna",
            new TokenBreakdown(1_000_000, 100_000, 0, 0, 0));

        Assert.Equal(0.58m, cost.EstimatedCostUsd);
        Assert.Equal("gpt-5.6-luna", cost.ExactPriceMatch);
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
    [InlineData(2)]
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
        Assert.Equal(legacyVersion == 2 ? UsageSourceReadStatus.Complete : UsageSourceReadStatus.Partial, migrated.Status);
        Assert.Equal(legacyVersion == 2 ? UsageSourceIssueKind.None : UsageSourceIssueKind.UnresolvedHistory, migrated.Issue);
        Assert.Equal(600, migrated.Events.Sum(row => row.Tokens.Total));
        Assert.Equal(legacyBytes, await File.ReadAllTextAsync(path + ".pre-v4"));
        var upgraded = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        Assert.Equal(corpus.CreateSource().SourceAuthority.Value, upgraded["sourceAuthority"]!.GetValue<string>());
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

    [Fact]
    public async Task ConsentOffLeavesSessionIdentityUnassignedAndOmitsForbiddenFields()
    {
        using var corpus = new CodexCorpus();
        string checkpoint = Path.Combine(corpus.Root, "session-off.json");
        corpus.WriteSession(
            "session-off",
            SessionMeta("visible-native-id"),
            Context("gpt-5.6-sol"),
            Usage("2026-07-27T12:01:00Z", 80, 20, 10, 2, privateText: "private fixture content"));
        UsageSourceReadResult result = await corpus.CreateSource(
            checkpointPath: checkpoint,
            attributionConsent: new StaticConsent(DisabledConsent()),
            attributionKeys: TestKeys()).ReadAsync();
        await result.Checkpoint!.PersistAsync(() => Task.FromResult(true));

        Assert.NotEmpty(result.Events);
        Assert.Empty(result.SessionLinks);
        string json = await File.ReadAllTextAsync(checkpoint);
        AssertCanaryAbsent(json);
        Assert.DoesNotContain("sessionKey", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConsentOnPersistsOpaqueSessionAndParentKeysWithoutNativeIdentifiers()
    {
        using var corpus = new CodexCorpus();
        string checkpoint = Path.Combine(corpus.Root, "session-on.json");
        string database = Path.Combine(corpus.Root, "session-on.db");
        corpus.WriteSession(
            "session-on",
            SessionMeta("visible-native-id", "parent-session"),
            Context("gpt-5.6-sol"),
            TaskStarted("2026-07-27T12:00:30Z", 1_785_153_630),
            Usage("2026-07-27T12:01:00Z", 80, 20, 10, 2, privateText: "private fixture content"));
        var keys = TestKeys();
        CodexUsageEventSource source = corpus.CreateSource(
            checkpointPath: checkpoint,
            attributionConsent: new StaticConsent(EnabledConsent()),
            attributionKeys: keys);
        UsageSourceReadResult result = await source.ReadAsync();
        Assert.NotEmpty(result.Events);
        UsageSessionLink link = Assert.Single(result.SessionLinks);
        Assert.Equal(keys.Derive(OpaqueKeyDomains.CodexSession, "codex", "visible-native-id"), link.SessionKey);
        Assert.Equal(keys.Derive(OpaqueKeyDomains.CodexParent, "codex", "parent-session"), link.ParentSessionKey);
        Assert.Equal(1, link.ConsentEpoch);
        UsageRepository repository = await UsageRepository.OpenAsync(database);
        DateOnly day = new(2026, 7, 27);
        await result.Checkpoint!.PersistAsync(async () =>
        {
            await repository.StoreSourceObservationsAsync(
                source.AgentId,
                source.SourceAuthority,
                source.EventParserVersion,
                day,
                day,
                result.Events,
                complete: true,
                sessionLinks: result.SessionLinks);
            return true;
        });
        string json = await File.ReadAllTextAsync(checkpoint);
        AssertCanaryAbsent(json);
        await using var connection = new SqliteConnection($"Data Source={database};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT session_key, parent_session_key FROM session_attribution;";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(link.SessionKey.Value, reader.GetString(0));
        Assert.Equal(link.ParentSessionKey!.Value, reader.GetString(1));
        AssertCanaryAbsent(reader.GetString(0) + reader.GetString(1));
    }

    [Fact]
    public async Task ParentReferenceResolvesToTheParentSessionKey()
    {
        using var corpus = new CodexCorpus();
        var keys = TestKeys();
        corpus.WriteSession(
            "parent",
            SessionMeta("parent-id"),
            Context("gpt-5.6-sol"),
            Usage("2026-07-27T12:01:00Z", 80, 20, 10, 2));
        corpus.WriteSession(
            "child",
            SessionMeta("child-id", "parent-id"),
            Context("gpt-5.6-sol"),
            TaskStarted("2026-07-27T12:01:30Z", 1_785_153_690),
            Usage("2026-07-27T12:02:00Z", 40, 0, 5, 0));
        UsageSourceReadResult result = await corpus.CreateSource(
            attributionConsent: new StaticConsent(EnabledConsent()),
            attributionKeys: keys).ReadAsync();
        UsageSessionLink parent = Assert.Single(result.SessionLinks, link => link.ParentSessionKey is null);
        UsageSessionLink child = Assert.Single(result.SessionLinks, link => link.ParentSessionKey is not null);
        Assert.Equal(parent.SessionKey, child.ParentSessionKey);
        Assert.Equal(keys.Derive(OpaqueKeyDomains.CodexSession, "codex", "parent-id"), parent.SessionKey);
    }

    [Fact]
    public async Task EnableAfterCheckpointDoesNotBackfillOldObservations()
    {
        using var corpus = new CodexCorpus();
        string checkpoint = Path.Combine(corpus.Root, "admit.json");
        corpus.WriteSession(
            "old",
            SessionMeta("old-session"),
            Context("gpt-5.6-sol"),
            Usage("2026-07-27T12:01:00Z", 80, 20, 10, 2));
        var consent = new AttributionConsentStore(Path.Combine(corpus.Root, "consent.json"));
        CodexUsageEventSource source = corpus.CreateSource(
            checkpointPath: checkpoint,
            attributionConsent: consent,
            attributionKeys: TestKeys());
        UsageSourceReadResult before = await source.ReadAsync();
        await before.Checkpoint!.PersistAsync(() => Task.FromResult(true));
        Assert.Empty(before.SessionLinks);
        await consent.EnableAsync(AttributionCapability.CodexSession);
        UsageSourceReadResult after = await source.ReadAsync();
        Assert.Equal(before.Events.Count, after.Events.Count);
        Assert.Empty(after.SessionLinks);
    }

    [Fact]
    public async Task NewObservationAfterEnableGetsALinkWithoutSilentBackfill()
    {
        using var corpus = new CodexCorpus();
        string checkpoint = Path.Combine(corpus.Root, "new-admit.json");
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero));
        var consent = new AttributionConsentStore(Path.Combine(corpus.Root, "new-consent.json"), clock);
        corpus.WriteSession(
            "old",
            SessionMeta("old-session"),
            Context("gpt-5.6-sol"),
            Usage("2026-07-27T12:01:00Z", 80, 20, 10, 2));
        CodexUsageEventSource source = corpus.CreateSource(
            checkpointPath: checkpoint,
            clock: clock,
            attributionConsent: consent,
            attributionKeys: TestKeys());
        UsageSourceReadResult before = await source.ReadAsync();
        await before.Checkpoint!.PersistAsync(() => Task.FromResult(true));
        await consent.EnableAsync(AttributionCapability.CodexSession);
        corpus.WriteSession(
            "fresh",
            SessionMeta("new-session"),
            Context("gpt-5.6-sol"),
            Usage("2026-07-28T13:00:00Z", 40, 0, 5, 0));
        UsageSourceReadResult after = await source.ReadAsync();
        UsageSessionLink link = Assert.Single(after.SessionLinks);
        Assert.Equal(TestKeys().Derive(OpaqueKeyDomains.CodexSession, "codex", "new-session"), link.SessionKey);
        Assert.Equal(before.Events.Count + 1, after.Events.Count);
    }

    [Fact]
    public async Task ExplicitBackfillLinksOnlyTheSelectedRangeAndIsIdempotent()
    {
        using var corpus = new CodexCorpus();
        string checkpoint = Path.Combine(corpus.Root, "backfill.json");
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero));
        var consent = new AttributionConsentStore(Path.Combine(corpus.Root, "backfill-consent.json"), clock);
        corpus.WriteSession(
            "old",
            SessionMeta("old-session"),
            Context("gpt-5.6-sol"),
            Usage("2026-07-27T12:01:00Z", 80, 20, 10, 2));
        corpus.WriteSession(
            "outside",
            SessionMeta("other-session"),
            Context("gpt-5.6-sol"),
            Usage("2026-07-26T12:01:00Z", 20, 0, 0, 0));
        CodexUsageEventSource source = corpus.CreateSource(
            checkpointPath: checkpoint,
            clock: clock,
            attributionConsent: consent,
            attributionKeys: TestKeys());
        UsageSourceReadResult before = await source.ReadAsync();
        await before.Checkpoint!.PersistAsync(() => Task.FromResult(true));
        await consent.EnableAsync(AttributionCapability.CodexSession);
        source.SessionAttributionBackfillFrom = new DateOnly(2026, 7, 27);
        source.SessionAttributionBackfillTo = new DateOnly(2026, 7, 27);
        UsageSourceReadResult first = await source.ReadAsync();
        UsageSessionLink link = Assert.Single(first.SessionLinks);
        Assert.Equal(TestKeys().Derive(OpaqueKeyDomains.CodexSession, "codex", "old-session"), link.SessionKey);
        UsageSourceReadResult second = await source.ReadAsync();
        Assert.Single(second.SessionLinks);
        Assert.Equal(link.SessionKey, Assert.Single(second.SessionLinks).SessionKey);
        await first.Checkpoint!.PersistAsync(() => Task.FromResult(true));
        source.SessionAttributionBackfillFrom = null;
        source.SessionAttributionBackfillTo = null;
        UsageSourceReadResult afterClear = await source.ReadAsync();
        Assert.Single(afterClear.SessionLinks);
        await afterClear.Checkpoint!.PersistAsync(() => Task.FromResult(true));
        CodexUsageEventSource restarted = corpus.CreateSource(
            checkpointPath: checkpoint,
            clock: clock,
            attributionConsent: consent,
            attributionKeys: TestKeys());
        UsageSourceReadResult afterRestart = await restarted.ReadAsync();
        Assert.Single(afterRestart.SessionLinks);
        Assert.Equal(link.SessionKey, Assert.Single(afterRestart.SessionLinks).SessionKey);
        await consent.EnableAsync(AttributionCapability.CodexProject);
        restarted.ProjectAttributionBackfillFrom = new DateOnly(2026, 7, 27);
        restarted.ProjectAttributionBackfillTo = new DateOnly(2026, 7, 27);
        UsageSourceReadResult projectBackfill = await restarted.ReadAsync();
        Assert.Single(projectBackfill.SessionLinks);
        Assert.Single(projectBackfill.ProjectLinks);
        await projectBackfill.Checkpoint!.PersistAsync(() => Task.FromResult(true));
        restarted.ProjectAttributionBackfillFrom = null;
        restarted.ProjectAttributionBackfillTo = null;
        UsageSourceReadResult afterProjectClear = await restarted.ReadAsync();
        Assert.Single(afterProjectClear.SessionLinks);
        Assert.Single(afterProjectClear.ProjectLinks);
        await afterProjectClear.Checkpoint!.PersistAsync(() => Task.FromResult(true));
        CodexUsageEventSource third = corpus.CreateSource(
            checkpointPath: checkpoint,
            clock: clock,
            attributionConsent: consent,
            attributionKeys: TestKeys());
        UsageSourceReadResult afterBothRestart = await third.ReadAsync();
        Assert.Single(afterBothRestart.SessionLinks);
        Assert.Single(afterBothRestart.ProjectLinks);
        await consent.RevokeAsync(AttributionCapability.CodexSession);
        await consent.CompletePurgeAsync(AttributionCapability.CodexSession);
        await consent.EnableAsync(AttributionCapability.CodexSession);
        UsageSourceReadResult reenabled = await third.ReadAsync();
        Assert.Empty(reenabled.SessionLinks);
        Assert.Single(reenabled.ProjectLinks);
    }

    [Fact]
    public async Task McpAndAllowlistedToolsFollowAdmissionLifecycleWithoutChangingTokens()
    {
        using var corpus = new CodexCorpus();
        string checkpoint = Path.Combine(corpus.Root, "mcp-admit.json");
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero));
        var consent = new AttributionConsentStore(Path.Combine(corpus.Root, "mcp-consent.json"), clock);
        string sessionPath = corpus.WriteSession(
            "ops",
            SessionMeta("ops-session"),
            Context("gpt-5.6-sol"),
            Usage("2026-07-27T12:01:00Z", 500, 0, 140, 0, privateText: "secret-arg"),
            McpBegin("call-mcp-1", "example-server", "list_things"),
            McpEnd("call-mcp-1", "example-server", "list_things", ok: true),
            McpBegin("call-mcp-2", "example-server", "get_thing"),
            McpEnd("call-mcp-2", "example-server", "get_thing", ok: false),
            ItemStarted("i-read", "Read"),
            ItemCompleted("i-read", "Read"),
            ItemStarted("i-edit", "Edit"),
            ItemCompleted("i-edit", "Edit"),
            ItemStarted("i-other", "Shell"));
        CodexUsageEventSource source = corpus.CreateSource(
            checkpointPath: checkpoint,
            clock: clock,
            attributionConsent: consent,
            attributionKeys: TestKeys());
        UsageSourceReadResult disabled = await source.ReadAsync();
        await disabled.Checkpoint!.PersistAsync(() => Task.FromResult(true));
        Assert.Empty(disabled.OperationFacts);
        Assert.Equal(640, disabled.Events.Sum(item => item.Tokens.Total));
        await consent.EnableAsync(AttributionCapability.CodexMcp);
        UsageSourceReadResult enabled = await source.ReadAsync();
        Assert.Empty(enabled.OperationFacts);
        Assert.Equal(640, enabled.Events.Sum(item => item.Tokens.Total));
        source.McpAttributionBackfillFrom = new DateOnly(2026, 7, 27);
        source.McpAttributionBackfillTo = new DateOnly(2026, 7, 27);
        UsageSourceReadResult backfill = await source.ReadAsync();
        Assert.Equal(4, backfill.OperationFacts.Count);
        Assert.Equal(1, backfill.OperationFacts.Count(item => item.Kind == UsageOperationKind.Tool && item.Tool == "Read"));
        Assert.Equal(1, backfill.OperationFacts.Count(item => item.Kind == UsageOperationKind.Tool && item.Tool == "Edit"));
        Assert.Equal(2, backfill.OperationFacts.Count(item => item.Kind == UsageOperationKind.Mcp));
        Assert.DoesNotContain(backfill.OperationFacts, item => item.Tool == "Shell");
        Assert.All(backfill.OperationFacts, item => AssertCanaryAbsent(item.Tool + (item.Server ?? "")));
        await backfill.Checkpoint!.PersistAsync(() => Task.FromResult(true));
        await File.AppendAllTextAsync(
            sessionPath,
            ItemStarted("i-read", "Read") + Environment.NewLine + ItemCompleted("i-read", "Read") + Environment.NewLine);
        source.McpAttributionBackfillFrom = null;
        source.McpAttributionBackfillTo = null;
        UsageSourceReadResult replay = await source.ReadAsync();
        Assert.Equal(4, replay.OperationFacts.Count);
        await replay.Checkpoint!.PersistAsync(() => Task.FromResult(true));
        CodexUsageEventSource restarted = corpus.CreateSource(
            checkpointPath: checkpoint,
            clock: clock,
            attributionConsent: consent,
            attributionKeys: TestKeys());
        UsageSourceReadResult afterRestart = await restarted.ReadAsync();
        Assert.Equal(4, afterRestart.OperationFacts.Count);
        Assert.Equal(640, afterRestart.Events.Sum(item => item.Tokens.Total));
        string database = Path.Combine(corpus.Root, "ops.v1.db");
        UsageRepository repository = await UsageRepository.OpenAsync(database);
        DateOnly day = new(2026, 7, 27);
        await repository.StoreSourceObservationsAsync(
            restarted.AgentId,
            afterRestart.SourceInstance ?? restarted.SourceInstance,
            restarted.EventParserVersion,
            day,
            day,
            afterRestart.Events,
            complete: true,
            operations: afterRestart.OperationFacts);
        Assert.Equal(4, await repository.CountOperationFactsAsync(AttributionCapability.CodexMcp));
        Assert.Equal(640, (await repository.QueryDailyRollupsAsync(day, day)).Sum(row => row.Tokens.Total));
        await repository.StoreSourceObservationsAsync(
            restarted.AgentId,
            afterRestart.SourceInstance ?? restarted.SourceInstance,
            restarted.EventParserVersion,
            day,
            day,
            afterRestart.Events,
            complete: true,
            operations: afterRestart.OperationFacts);
        Assert.Equal(4, await repository.CountOperationFactsAsync(AttributionCapability.CodexMcp));
        IReadOnlyList<UsageOperationRankedRow> ranking = await repository.ReadOperationRankingAsync(
            new DateTimeOffset(2026, 7, 27, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 28, 0, 0, 0, TimeSpan.Zero),
            AttributionCapability.CodexMcp,
            afterRestart.OperationFacts[0].ConsentEpoch);
        Assert.Equal(4, ranking.Sum(row => row.InvocationCount));
        Assert.Equal(1, ranking.Where(row => row.Kind == UsageOperationKind.Tool && row.Tool == "Read").Sum(row => row.InvocationCount));
        Assert.Equal(1, ranking.Where(row => row.Kind == UsageOperationKind.Tool && row.Tool == "Edit").Sum(row => row.InvocationCount));
        Assert.Equal(2, ranking.Where(row => row.Kind == UsageOperationKind.Mcp).Sum(row => row.InvocationCount));
        Assert.Equal(1, ranking.Sum(row => row.ErrorCount));
        await repository.PurgeOperationFactsAsync(AttributionCapability.CodexMcp);
        Assert.Equal(0, await repository.CountOperationFactsAsync(AttributionCapability.CodexMcp));
        Assert.Equal(640, (await repository.QueryDailyRollupsAsync(day, day)).Sum(row => row.Tokens.Total));
        await consent.RevokeAsync(AttributionCapability.CodexMcp);
        await consent.CompletePurgeAsync(AttributionCapability.CodexMcp);
        restarted.ClearStoredOperationAttribution(AttributionCapability.CodexMcp);
        await consent.EnableAsync(AttributionCapability.CodexMcp);
        UsageSourceReadResult reenabled = await restarted.ReadAsync();
        Assert.Empty(reenabled.OperationFacts);
        Assert.Equal(640, reenabled.Events.Sum(item => item.Tokens.Total));
    }

    [Fact]
    public async Task SpawnCommandsAndFilesUseSeparateCapabilities()
    {
        using var corpus = new CodexCorpus();
        string checkpoint = Path.Combine(corpus.Root, "ops-extra.json");
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero));
        var consent = new AttributionConsentStore(Path.Combine(corpus.Root, "ops-extra-consent.json"), clock);
        corpus.WriteSession(
            "extra",
            SessionMeta("spawn-session"),
            Context("gpt-5.6-sol"),
            Usage("2026-07-27T12:01:00Z", 120, 0, 0, 0),
            SpawnBegin("spawn-1", "reviewer"),
            SpawnEnd("spawn-1", "reviewer"),
            ExecBegin("cmd-search", ["rg", "secret-arg"]),
            ExecEnd("cmd-search", ["rg", "secret-arg"], exitCode: 0),
            ExecBegin("cmd-git", ["git", "status"]),
            ExecEnd("cmd-git", ["git", "status"], exitCode: 0),
            ExecBegin("cmd-test", ["dotnet", "test"]),
            ExecEnd("cmd-test", ["dotnet", "test"], exitCode: 0),
            ExecBegin("cmd-unknown", ["mystery-wrapper", "secret-arg"]),
            ExecEnd("cmd-unknown", ["mystery-wrapper", "secret-arg"], exitCode: 0),
            PatchBegin("patch-1", "src/alpha.rs", "src/beta.rs"),
            PatchEnd("patch-1", "src/alpha.rs", "src/beta.rs"));
        CodexUsageEventSource source = corpus.CreateSource(
            checkpointPath: checkpoint,
            clock: clock,
            attributionConsent: consent,
            attributionKeys: TestKeys());
        UsageSourceReadResult before = await source.ReadAsync();
        await before.Checkpoint!.PersistAsync(() => Task.FromResult(true));
        await consent.EnableAsync(AttributionCapability.CodexSkills);
        await consent.EnableAsync(AttributionCapability.CodexCommands);
        await consent.EnableAsync(AttributionCapability.CodexFiles);
        source.SkillsAttributionBackfillFrom = source.CommandsAttributionBackfillFrom = source.FilesAttributionBackfillFrom = new DateOnly(2026, 7, 27);
        source.SkillsAttributionBackfillTo = source.CommandsAttributionBackfillTo = source.FilesAttributionBackfillTo = new DateOnly(2026, 7, 27);
        UsageSourceReadResult admitted = await source.ReadAsync();
        Assert.Equal(1, admitted.OperationFacts.Count(item => item.Kind == UsageOperationKind.Spawn));
        Assert.Equal("search", Assert.Single(admitted.OperationFacts, item => item.Kind == UsageOperationKind.Command && item.Tool == "search").Tool);
        Assert.Equal("git", Assert.Single(admitted.OperationFacts, item => item.Kind == UsageOperationKind.Command && item.Tool == "git").Tool);
        Assert.Equal("test", Assert.Single(admitted.OperationFacts, item => item.Kind == UsageOperationKind.Command && item.Tool == "test").Tool);
        Assert.Equal("unknown", Assert.Single(admitted.OperationFacts, item => item.Kind == UsageOperationKind.Command && item.Tool == "unknown").Tool);
        Assert.Equal(4, admitted.OperationFacts.Count(item => item.Kind == UsageOperationKind.Command));
        Assert.Equal(2, admitted.OperationFacts.Count(item => item.Kind == UsageOperationKind.File));
        Assert.All(admitted.OperationFacts, item => Assert.DoesNotContain("secret-arg", item.Tool, StringComparison.Ordinal));
        Assert.All(admitted.OperationFacts, item => Assert.DoesNotContain("src/alpha.rs", item.Server ?? "", StringComparison.Ordinal));
        Assert.All(admitted.OperationFacts, item => Assert.DoesNotContain("alpha.rs", item.Server ?? "", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExplicitProjectBackfillLinksCheckpointedRangeAndIsIdempotent()
    {
        using var corpus = new CodexCorpus();
        string checkpoint = Path.Combine(corpus.Root, "project-backfill.json");
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero));
        var consent = new AttributionConsentStore(Path.Combine(corpus.Root, "project-backfill-consent.json"), clock);
        corpus.WriteSession(
            "kept",
            SessionMeta("kept-session", cwd: "workspace-kept"),
            Context("gpt-5.6-sol", cwd: "workspace-kept"),
            Usage("2026-07-27T12:01:00Z", 80, 20, 10, 2));
        corpus.WriteSession(
            "outside",
            SessionMeta("outside-session", cwd: "workspace-outside"),
            Context("gpt-5.6-sol", cwd: "workspace-outside"),
            Usage("2026-07-26T12:01:00Z", 20, 0, 0, 0));
        var keys = TestKeys();
        CodexUsageEventSource source = corpus.CreateSource(
            checkpointPath: checkpoint,
            clock: clock,
            attributionConsent: consent,
            attributionKeys: keys);
        UsageSourceReadResult before = await source.ReadAsync();
        await before.Checkpoint!.PersistAsync(() => Task.FromResult(true));
        Assert.Empty(before.ProjectLinks);
        await consent.EnableAsync(AttributionCapability.CodexProject);
        UsageSourceReadResult afterEnable = await source.ReadAsync();
        Assert.Empty(afterEnable.ProjectLinks);
        source.ProjectAttributionBackfillFrom = new DateOnly(2026, 7, 27);
        source.ProjectAttributionBackfillTo = new DateOnly(2026, 7, 27);
        UsageSourceReadResult first = await source.ReadAsync();
        UsageProjectLink link = Assert.Single(first.ProjectLinks);
        Assert.True(OpaqueWorkspaceFingerprint.TryFingerprint("workspace-kept", out string fingerprint));
        Assert.Equal(keys.Derive(OpaqueKeyDomains.CodexProject, "codex", fingerprint), link.ProjectKey);
        Assert.Equal(ProjectMappingKind.Observed, link.MappingKind);
        UsageSourceReadResult second = await source.ReadAsync();
        Assert.Single(second.ProjectLinks);
        Assert.Equal(link.ProjectKey, Assert.Single(second.ProjectLinks).ProjectKey);
        Assert.DoesNotContain("workspace-kept", await File.ReadAllTextAsync(checkpoint), StringComparison.Ordinal);
        Assert.DoesNotContain("workspace-outside", await File.ReadAllTextAsync(checkpoint), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExplicitProjectBackfillOnTwoDaysSurvivesRestartAndDoesNotOverlap()
    {
        using var corpus = new CodexCorpus();
        string checkpoint = Path.Combine(corpus.Root, "project-two-days.json");
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero));
        var consent = new AttributionConsentStore(Path.Combine(corpus.Root, "project-two-days-consent.json"), clock);
        corpus.WriteSession(
            "day-one",
            SessionMeta("day-one-session", cwd: "workspace-one"),
            Context("gpt-5.6-sol", cwd: "workspace-one"),
            Usage("2026-07-26T12:01:00Z", 40, 0, 0, 0));
        corpus.WriteSession(
            "day-two",
            SessionMeta("day-two-session", cwd: "workspace-two"),
            Context("gpt-5.6-sol", cwd: "workspace-two"),
            Usage("2026-07-27T12:01:00Z", 80, 20, 10, 2));
        var keys = TestKeys();
        CodexUsageEventSource source = corpus.CreateSource(
            checkpointPath: checkpoint,
            clock: clock,
            attributionConsent: consent,
            attributionKeys: keys);
        UsageSourceReadResult before = await source.ReadAsync();
        await before.Checkpoint!.PersistAsync(() => Task.FromResult(true));
        await consent.EnableAsync(AttributionCapability.CodexProject);
        source.ProjectAttributionBackfillFrom = new DateOnly(2026, 7, 26);
        source.ProjectAttributionBackfillTo = new DateOnly(2026, 7, 26);
        UsageSourceReadResult firstDay = await source.ReadAsync();
        Assert.Single(firstDay.ProjectLinks);
        await firstDay.Checkpoint!.PersistAsync(() => Task.FromResult(true));
        source.ProjectAttributionBackfillFrom = null;
        source.ProjectAttributionBackfillTo = null;
        CodexUsageEventSource restarted = corpus.CreateSource(
            checkpointPath: checkpoint,
            clock: clock,
            attributionConsent: consent,
            attributionKeys: keys);
        UsageSourceReadResult afterRestart = await restarted.ReadAsync();
        Assert.Single(afterRestart.ProjectLinks);
        restarted.ProjectAttributionBackfillFrom = new DateOnly(2026, 7, 27);
        restarted.ProjectAttributionBackfillTo = new DateOnly(2026, 7, 27);
        UsageSourceReadResult secondDay = await restarted.ReadAsync();
        Assert.Equal(2, secondDay.ProjectLinks.Count);
        await secondDay.Checkpoint!.PersistAsync(() => Task.FromResult(true));
        restarted.ProjectAttributionBackfillFrom = new DateOnly(2026, 7, 26);
        restarted.ProjectAttributionBackfillTo = new DateOnly(2026, 7, 26);
        UsageSourceReadResult repeatFirst = await restarted.ReadAsync();
        Assert.Equal(2, repeatFirst.ProjectLinks.Count);
        Assert.Equal(2, before.Events.Count);
    }

    [Fact]
    public async Task InvalidNativeSessionIdStaysUnassigned()
    {
        using var corpus = new CodexCorpus();
        corpus.WriteSession(
            "session-invalid",
            SessionMeta(@"bad/id:with\\slash"),
            Context("gpt-5.6-sol"),
            Usage("2026-07-27T12:01:00Z", 40, 0, 5, 0));
        UsageSourceReadResult result = await corpus.CreateSource(
            attributionConsent: new StaticConsent(EnabledConsent()),
            attributionKeys: TestKeys()).ReadAsync();
        Assert.Single(result.Events);
        Assert.Empty(result.SessionLinks);
    }

    [Fact]
    public async Task ProjectConsentOffLeavesProjectLinksEmptyWithoutPersistingPaths()
    {
        using var corpus = new CodexCorpus();
        string checkpoint = Path.Combine(corpus.Root, "project-off.json");
        corpus.WriteSession(
            "project-off",
            SessionMeta("visible-native-id", cwd: "workspace-a"),
            Context("gpt-5.6-sol", cwd: "workspace-a"),
            Usage("2026-07-27T12:01:00Z", 80, 20, 10, 2, privateText: "private fixture content"));
        UsageSourceReadResult result = await corpus.CreateSource(
            checkpointPath: checkpoint,
            attributionConsent: new StaticConsent(EnabledConsent()),
            attributionKeys: TestKeys()).ReadAsync();
        await result.Checkpoint!.PersistAsync(() => Task.FromResult(true));

        Assert.NotEmpty(result.Events);
        Assert.Empty(result.ProjectLinks);
        string json = await File.ReadAllTextAsync(checkpoint);
        AssertCanaryAbsent(json);
        Assert.DoesNotContain("workspace-a", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TwoWorkspacesInOneSessionKeepEventLevelObservedProjectKeys()
    {
        using var corpus = new CodexCorpus();
        string checkpoint = Path.Combine(corpus.Root, "project-on.json");
        string database = Path.Combine(corpus.Root, "project-on.db");
        corpus.WriteSession(
            "project-on",
            SessionMeta("visible-native-id", cwd: "workspace-a"),
            Context("gpt-5.6-sol", cwd: "workspace-a"),
            Usage("2026-07-27T12:01:00Z", 80, 20, 10, 2, privateText: "private fixture content"),
            Context("gpt-5.6-sol", cwd: "workspace-b"),
            Usage(
                "2026-07-27T12:02:00Z",
                80,
                20,
                10,
                2,
                privateText: "private fixture content",
                totalInput: 160,
                totalCachedInput: 40,
                totalOutput: 20,
                totalReasoningOutput: 4));
        var keys = TestKeys();
        CodexUsageEventSource source = corpus.CreateSource(
            checkpointPath: checkpoint,
            attributionConsent: new MapConsent(EnabledConsent(), EnabledProjectConsent()),
            attributionKeys: keys);
        UsageSourceReadResult result = await source.ReadAsync();
        Assert.Equal(2, result.Events.Count);
        Assert.Equal(2, result.ProjectLinks.Count);
        Assert.True(OpaqueWorkspaceFingerprint.TryFingerprint("workspace-a", out string firstFingerprint));
        Assert.True(OpaqueWorkspaceFingerprint.TryFingerprint("workspace-b", out string secondFingerprint));
        OpaqueAttributionKey firstKey = keys.Derive(OpaqueKeyDomains.CodexProject, "codex", firstFingerprint);
        OpaqueAttributionKey secondKey = keys.Derive(OpaqueKeyDomains.CodexProject, "codex", secondFingerprint);
        Assert.NotEqual(firstKey, secondKey);
        Assert.Equal(firstKey, result.ProjectLinks[0].ProjectKey);
        Assert.Equal(secondKey, result.ProjectLinks[1].ProjectKey);
        Assert.All(result.ProjectLinks, link => Assert.Equal(ProjectMappingKind.Observed, link.MappingKind));
        UsageRepository repository = await UsageRepository.OpenAsync(database);
        DateOnly day = new(2026, 7, 27);
        await result.Checkpoint!.PersistAsync(async () =>
        {
            await repository.StoreSourceObservationsAsync(
                source.AgentId,
                source.SourceAuthority,
                source.EventParserVersion,
                day,
                day,
                result.Events,
                complete: true,
                projectLinks: result.ProjectLinks);
            return true;
        });
        string json = await File.ReadAllTextAsync(checkpoint);
        AssertCanaryAbsent(json);
        Assert.DoesNotContain("workspace-a", json, StringComparison.Ordinal);
        Assert.DoesNotContain("workspace-b", json, StringComparison.Ordinal);
        await using var connection = new SqliteConnection($"Data Source={database};Pooling=False");
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT project_key, mapping_kind FROM project_attribution ORDER BY event_key;";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        var stored = new List<(string Key, string Kind)>();
        while (await reader.ReadAsync())
        {
            stored.Add((reader.GetString(0), reader.GetString(1)));
        }

        Assert.Equal(2, stored.Count);
        Assert.Contains(stored, row => row.Key == firstKey.Value && row.Kind == "observed");
        Assert.Contains(stored, row => row.Key == secondKey.Value && row.Kind == "observed");
        Assert.All(stored, row => AssertCanaryAbsent(row.Key + row.Kind));
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

    private static string SessionMeta(
        string id,
        string? parentThreadId = null,
        string cwd = "private-project-path") => JsonSerializer.Serialize(new
    {
        timestamp = "2026-07-27T12:00:00Z",
        type = "session_meta",
        payload = new
        {
            id,
            parent_thread_id = parentThreadId,
            cwd,
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

    private static string McpBegin(string callId, string server, string tool) => JsonSerializer.Serialize(new
    {
        timestamp = "2026-07-27T12:02:00Z",
        type = "event_msg",
        payload = new
        {
            type = "mcp_tool_call_begin",
            call_id = callId,
            invocation = new { server, tool, arguments = new { secret = "secret-arg" } },
        },
    });

    private static string McpEnd(string callId, string server, string tool, bool ok) => JsonSerializer.Serialize(new
    {
        timestamp = "2026-07-27T12:02:01Z",
        type = "event_msg",
        payload = new
        {
            type = "mcp_tool_call_end",
            call_id = callId,
            invocation = new { server, tool },
            result = ok ? new object() : new { Err = "tool failed" },
        },
    });

    private static string ItemStarted(string id, string tool) => JsonSerializer.Serialize(new
    {
        timestamp = "2026-07-27T12:03:00Z",
        type = "event_msg",
        payload = new
        {
            type = "item_started",
            item = new { type = "DynamicToolCall", id, tool, arguments = new { path = "secret-arg" } },
        },
    });

    private static string ItemCompleted(string id, string tool) => JsonSerializer.Serialize(new
    {
        timestamp = "2026-07-27T12:03:01Z",
        type = "event_msg",
        payload = new
        {
            type = "item_completed",
            item = new { type = "DynamicToolCall", id, tool, status = "completed" },
        },
    });

    private static string SpawnBegin(string callId, string role) => JsonSerializer.Serialize(new
    {
        timestamp = "2026-07-27T12:04:00Z",
        type = "event_msg",
        payload = new
        {
            type = "collab_agent_spawn_begin",
            call_id = callId,
            sender_thread_id = "thread-parent",
            agent_role = role,
            prompt = "secret-arg",
        },
    });

    private static string SpawnEnd(string callId, string role) => JsonSerializer.Serialize(new
    {
        timestamp = "2026-07-27T12:04:01Z",
        type = "event_msg",
        payload = new
        {
            type = "collab_agent_spawn_end",
            call_id = callId,
            sender_thread_id = "thread-parent",
            new_agent_role = role,
            prompt = "secret-arg",
        },
    });

    private static string ExecBegin(string callId, string[] command) => JsonSerializer.Serialize(new
    {
        timestamp = "2026-07-27T12:05:00Z",
        type = "event_msg",
        payload = new
        {
            type = "exec_command_begin",
            call_id = callId,
            command,
            cwd = "private-project-path",
        },
    });

    private static string ExecEnd(string callId, string[] command, int exitCode) => JsonSerializer.Serialize(new
    {
        timestamp = "2026-07-27T12:05:01Z",
        type = "event_msg",
        payload = new
        {
            type = "exec_command_end",
            call_id = callId,
            command,
            exit_code = exitCode,
            status = "completed",
        },
    });

    private static string PatchBegin(string callId, params string[] paths) => JsonSerializer.Serialize(new
    {
        timestamp = "2026-07-27T12:06:00Z",
        type = "event_msg",
        payload = new
        {
            type = "patch_apply_begin",
            call_id = callId,
            changes = paths.ToDictionary(path => path, _ => new { unified_diff = "secret-arg" }),
        },
    });

    private static string PatchEnd(string callId, params string[] paths) => JsonSerializer.Serialize(new
    {
        timestamp = "2026-07-27T12:06:01Z",
        type = "event_msg",
        payload = new
        {
            type = "patch_apply_end",
            call_id = callId,
            success = true,
            changes = paths.ToDictionary(path => path, _ => new { unified_diff = "secret-arg" }),
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
            TimeProvider? clock = null,
            IAttributionConsentSource? attributionConsent = null,
            IOpaqueKeyDeriver? attributionKeys = null) => new(
            "UTC",
            codexHomeOverride: _path,
            maximumFiles: maximumFiles,
            clientFactory: clientFactory,
            checkpointPath: checkpointPath,
            clock: clock ?? new FixedTimeProvider(new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero)),
            attributionConsent: attributionConsent,
            attributionKeys: attributionKeys);

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

    private static AttributionConsent EnabledConsent(long epoch = 1) =>
        new(AttributionCapability.CodexSession, AttributionConsentState.Enabled, epoch, DateTimeOffset.UnixEpoch);

    private static AttributionConsent EnabledProjectConsent(long epoch = 1) =>
        new(AttributionCapability.CodexProject, AttributionConsentState.Enabled, epoch, DateTimeOffset.UnixEpoch);

    private static AttributionConsent DisabledConsent() =>
        new(AttributionCapability.CodexSession, AttributionConsentState.Disabled, 0, DateTimeOffset.UnixEpoch);

    private static HmacOpaqueKeyDeriver TestKeys() =>
        new(Enumerable.Repeat((byte)0x4B, 32).ToArray());

    private static void AssertCanaryAbsent(string text)
    {
        Assert.DoesNotContain("visible-native-id", text, StringComparison.Ordinal);
        Assert.DoesNotContain("parent-session", text, StringComparison.Ordinal);
        Assert.DoesNotContain("private-project-path", text, StringComparison.Ordinal);
        Assert.DoesNotContain("private fixture content", text, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-arg", text, StringComparison.Ordinal);
        Assert.DoesNotContain("cwd", text, StringComparison.Ordinal);
    }

    private sealed class StaticConsent(AttributionConsent consent) : IAttributionConsentSource
    {
        public Task<AttributionConsent> LoadAsync(
            AttributionCapability capability,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(consent);
    }

    private sealed class MapConsent(params AttributionConsent[] consents) : IAttributionConsentSource
    {
        public Task<AttributionConsent> LoadAsync(
            AttributionCapability capability,
            CancellationToken cancellationToken = default)
        {
            AttributionConsent? match = consents.FirstOrDefault(item => item.Capability.Value == capability.Value);
            return Task.FromResult(match ?? new AttributionConsent(
                capability,
                AttributionConsentState.Disabled,
                0,
                DateTimeOffset.UnixEpoch));
        }
    }
}

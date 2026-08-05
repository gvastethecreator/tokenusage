using System.Security.Cryptography;
using System.Text;
using TokenUsage.Core.Automation;
using TokenUsage.Core.Cache;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;

namespace TokenUsage.Core.Tests.Automation;

public sealed class AutomationQueryTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task UsageQueryReadsAndSummarizesTheSharedRepository()
    {
        using var folder = new TemporaryFolder();
        UsageRepository repository = await UsageRepository.OpenAsync(folder.DatabasePath);
        await repository.IngestAsync(
        [
            CreateUsageEvent("reported", CostObservation.ProviderReported(0.25m)),
            CreateUsageEvent("unpriced", CostObservation.Unavailable()),
        ]);

        UsageSummary result = await new UsageQuery(folder.DatabasePath).ReadAsync(
            new DateOnly(2026, 7, 25),
            new DateOnly(2026, 7, 25));

        Assert.Equal(2, result.EventCount);
        Assert.Equal(300, result.TotalTokens);
        Assert.Equal(0.25m, result.ReportedCostUsd);
        Assert.Null(result.EstimatedCostUsd);
        Assert.Equal(150, result.UnpricedTokens);
    }

    [Fact]
    public async Task UsageReportQueryGroupsDurableUsageByAgentModelAndDay()
    {
        using var folder = new TemporaryFolder();
        UsageRepository repository = await UsageRepository.OpenAsync(folder.DatabasePath);
        await repository.IngestAsync(
        [
            CreateUsageEvent("codex-reported", CostObservation.ProviderReported(0.25m)),
            CreateUsageEvent(
                "opencode-estimated",
                CostObservation.CatalogEstimated(0.50m, "fixture", "claude-test"),
                agentId: "opencode",
                modelProviderId: "anthropic",
                modelId: "claude-test"),
            CreateUsageEvent(
                "codex-unpriced",
                CostObservation.Unavailable(),
                occurredAtUtc: Now.AddDays(-1)),
        ]);

        var query = new UsageReportQuery(folder.DatabasePath);
        UsageReport report = await query.ReadAsync(
            new DateOnly(2026, 7, 24),
            new DateOnly(2026, 7, 25));
        UsageReport codexOnly = await query.ReadAsync(
            new DateOnly(2026, 7, 24),
            new DateOnly(2026, 7, 25),
            new AgentId("codex"));

        Assert.Equal(3, report.Totals.EventCount);
        Assert.Equal(450, report.Totals.Tokens.Total);
        Assert.Equal(0.25m, report.Totals.ReportedCostUsd);
        Assert.Equal(0.50m, report.Totals.EstimatedCostUsd);
        Assert.Equal(150, report.Totals.UnpricedTokens);
        Assert.Equal(66.7m, report.Totals.PriceCoveragePercent);
        Assert.Equal(["opencode", "codex"], report.Agents.Select(item => item.AgentId.Value));
        Assert.Equal(2, report.Models.Count);
        Assert.Equal(
            [new DateOnly(2026, 7, 24), new DateOnly(2026, 7, 25)],
            report.Days.Select(item => item.Date));
        Assert.Equal(2, codexOnly.Totals.EventCount);
        Assert.Single(codexOnly.Agents);
        Assert.Equal("codex", codexOnly.Agents[0].AgentId.Value);
    }

    [Fact]
    public async Task LimitsQueryReturnsOnlyTheSelectedCachedProvider()
    {
        using var folder = new TemporaryFolder();
        var clock = new FixedTimeProvider(Now);
        ProviderSnapshot first = CreateSnapshot("first", 10m);
        ProviderSnapshot selected = CreateSnapshot("selected", 20m);
        var firstStore = new SnapshotStore(Path.Combine(folder.Root, "first.json"), clock);
        var selectedStore = new SnapshotStore(Path.Combine(folder.Root, "selected.json"), clock);
        await firstStore.UpsertLastGoodAsync(first);
        await selectedStore.UpsertLastGoodAsync(selected);
        var host = new ProviderRefreshHost(
        [
            new ProviderRefreshRegistration(new StubProvider(first), firstStore),
            new ProviderRefreshRegistration(new StubProvider(selected), selectedStore),
        ], clock);
        var query = new LimitsQuery(host);

        IReadOnlyList<ProviderSnapshot> result = await query.ReadAsync(
            selected.ProviderId,
            forceRefresh: false);
        IReadOnlyList<ProviderSnapshot> missing = await query.ReadAsync(
            new ProviderId("missing"),
            forceRefresh: false);

        ProviderSnapshot loaded = Assert.Single(result);
        Assert.Equal(selected.ProviderId, loaded.ProviderId);
        Assert.Equal(selected.DisplayName, loaded.DisplayName);
        Assert.Empty(missing);
    }

    private static UsageEvent CreateUsageEvent(
        string key,
        CostObservation cost,
        string agentId = "codex",
        string modelProviderId = "openai",
        string modelId = "gpt-test",
        DateTimeOffset? occurredAtUtc = null) =>
        new(
            new UsageEventKey(Convert.ToHexStringLower(
                SHA256.HashData(Encoding.UTF8.GetBytes(key)))),
            new AgentId(agentId),
            new ModelProviderId(modelProviderId),
            new ModelId(modelId),
            occurredAtUtc ?? Now,
            "UTC",
            new TokenBreakdown(100, 25, 5, 20, 0),
            cost,
            "fixture/1",
            cost.Kind == CostKind.Unavailable
                ? CoverageKind.Unpriced
                : CoverageKind.Complete);

    private static ProviderSnapshot CreateSnapshot(string providerId, decimal used) =>
        new(
            new ProviderId(providerId),
            "Provider " + providerId,
            "Sample",
            Now,
            Now.AddSeconds(-30),
            "UTC",
            [
                new ProgressMetricSnapshot(
                    new MetricId("session"),
                    used,
                    100m,
                    Now.AddHours(4),
                    new DataProvenance(
                        SourceKind.Synthetic,
                        MeasurementKind.ProviderReported,
                        "fixture/1")),
            ],
            CoverageKind.Complete,
            1);

    private sealed class StubProvider(ProviderSnapshot snapshot) : IProviderRuntime
    {
        public ProviderDescriptor Descriptor { get; } =
            new(snapshot.ProviderId, snapshot.DisplayName);

        public ValueTask<ProviderDetection> DetectAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<ProviderDetection>(new ProviderDetection.Available());

        public Task<ProviderOutcome> RefreshAsync(
            RefreshContext context,
            CancellationToken cancellationToken) =>
            Task.FromResult<ProviderOutcome>(new ProviderOutcome.Success(snapshot));
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class TemporaryFolder : IDisposable
    {
        public TemporaryFolder()
        {
            Root = Path.Combine(Path.GetTempPath(), "wou-query-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            DatabasePath = Path.Combine(Root, "usage.db");
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
            }
        }
    }
}

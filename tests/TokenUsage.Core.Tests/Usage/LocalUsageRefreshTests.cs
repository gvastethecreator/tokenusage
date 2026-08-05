using System.Security.Cryptography;
using System.Text;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;

namespace TokenUsage.Core.Tests.Usage;

public sealed class LocalUsageRefreshTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 22, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RefreshIngestsSourceEventsAndReturnsStructuredRollupsWithoutUiTypes()
    {
        using var folder = new TemporaryFolder();
        var clock = new FixedTimeProvider(Now);
        var source = new ScriptedUsageEventSource(
            new AgentId("synthetic"),
            SourceKind.Synthetic,
            new UsageSourceReadResult(
                [
                    CreateEvent(
                        "synthetic",
                        "evt-1",
                        new DateTimeOffset(2026, 7, 20, 12, 0, 0, TimeSpan.Zero),
                        input: 100,
                        output: 50,
                        CostObservation.ProviderReported(1.25m)),
                    CreateEvent(
                        "synthetic",
                        "evt-2",
                        new DateTimeOffset(2026, 7, 21, 12, 0, 0, TimeSpan.Zero),
                        input: 200,
                        output: 80,
                        CostObservation.CatalogEstimated(0.40m, "cat/1", "exact")),
                ],
                UsageSourceReadStatus.Complete));

        var refresh = new LocalUsageRefresh(folder.DatabasePath, source, clock);
        LocalUsageRefreshResult result = await refresh.RefreshAsync();

        Assert.Equal(SourceKind.Synthetic, result.SourceKind);
        Assert.Equal(UsageSourceReadStatus.Complete, result.OverallStatus);
        Assert.Equal(2, result.Rollups.Sum(rollup => rollup.EventCount));
        Assert.Equal(430, result.Rollups.Sum(rollup => rollup.Tokens.Total));
        Assert.Single(result.SourceDiagnostics);
        Assert.Equal("synthetic", result.SourceDiagnostics[0].AgentId.Value);
        Assert.False(result.HasMultipleRealSources);
        Assert.True(result.FromInclusive <= result.ToInclusive);
        Assert.Equal(new DateOnly(2026, 7, 22), result.ToInclusive);
    }

    [Fact]
    public async Task RefreshWindowedSnapshotReconcilesOnlyAuthoritativeWindow()
    {
        using var folder = new TemporaryFolder();
        var clock = new FixedTimeProvider(Now);
        var source = new ScriptedWindowedSource(
            new AgentId("claude"),
            eventParserVersion: "test/1",
            reconciliationWindowDays: 7,
            new UsageSourceReadResult(
                [
                    CreateEvent(
                        "claude",
                        "win-1",
                        new DateTimeOffset(2026, 7, 21, 10, 0, 0, TimeSpan.Zero),
                        input: 10,
                        output: 5,
                        CostObservation.ProviderReported(0.10m)),
                ],
                UsageSourceReadStatus.Complete));

        var refresh = new LocalUsageRefresh(folder.DatabasePath, source, clock);
        LocalUsageRefreshResult first = await refresh.RefreshAsync();
        Assert.Equal(1, first.Rollups.Sum(r => r.EventCount));

        var updated = new ScriptedWindowedSource(
            new AgentId("claude"),
            eventParserVersion: "test/1",
            reconciliationWindowDays: 7,
            new UsageSourceReadResult(
                [
                    CreateEvent(
                        "claude",
                        "win-1",
                        new DateTimeOffset(2026, 7, 21, 10, 0, 0, TimeSpan.Zero),
                        input: 20,
                        output: 10,
                        CostObservation.ProviderReported(0.20m)),
                ],
                UsageSourceReadStatus.Complete));
        var refresh2 = new LocalUsageRefresh(folder.DatabasePath, updated, clock);
        LocalUsageRefreshResult second = await refresh2.RefreshAsync();

        Assert.Equal(1, second.Rollups.Sum(r => r.EventCount));
        Assert.Equal(30, second.Rollups.Sum(r => r.Tokens.Total));
        Assert.Equal(0.20m, second.Rollups.Sum(r => r.ReportedCostUsd ?? 0m));
    }

    [Fact]
    public async Task OneBrokenSourceDoesNotDiscardOtherLocalUsage()
    {
        using var folder = new TemporaryFolder();
        var clock = new FixedTimeProvider(Now);
        var healthy = new ScriptedUsageEventSource(
            new AgentId("healthy"),
            SourceKind.LocalDatabase,
            new UsageSourceReadResult(
                [
                    CreateEvent(
                        "healthy",
                        "healthy-event",
                        new DateTimeOffset(2026, 7, 21, 12, 0, 0, TimeSpan.Zero),
                        input: 12,
                        output: 3,
                        CostObservation.ProviderReported(0.25m)),
                ],
                UsageSourceReadStatus.Complete));
        var broken = new ThrowingUsageEventSource(new AgentId("broken"));

        var refresh = new LocalUsageRefresh(folder.DatabasePath, [broken, healthy], clock);
        LocalUsageRefreshResult result = await refresh.RefreshAsync();

        Assert.Equal(UsageSourceReadStatus.Partial, result.OverallStatus);
        Assert.Equal(SourceKind.LocalLog, result.SourceKind);
        Assert.True(result.HasMultipleRealSources);
        Assert.Equal(15, result.Rollups.Sum(rollup => rollup.Tokens.Total));
        UsageSourceDiagnostic diagnostic = Assert.Single(
            result.SourceDiagnostics,
            item => item.AgentId.Value == "broken");
        Assert.Equal(UsageSourceReadStatus.NoData, diagnostic.Status);
        Assert.Equal(UsageSourceIssueKind.AccessBlocked, diagnostic.Issue);
    }

    private static UsageEvent CreateEvent(
        string agentId,
        string identity,
        DateTimeOffset occurredAtUtc,
        long input,
        long output,
        CostObservation cost)
    {
        string eventKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))
            .ToLowerInvariant();
        CoverageKind coverage = cost.Kind == CostKind.Unavailable
            ? CoverageKind.Unpriced
            : CoverageKind.Complete;
        return new UsageEvent(
            new UsageEventKey(eventKey),
            new AgentId(agentId),
            new ModelProviderId("test"),
            new ModelId("model"),
            occurredAtUtc,
            "UTC",
            new TokenBreakdown(input, output, 0, 0, 0),
            cost,
            "test/1",
            coverage);
    }

    private sealed class TemporaryFolder : IDisposable
    {
        public TemporaryFolder()
        {
            Root = Path.Combine(Path.GetTempPath(), "wou-local-refresh-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            DatabasePath = Path.Combine(Root, "usage.v1.db");
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
                // best effort
            }
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow.ToUniversalTime();

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }

    private sealed class ScriptedUsageEventSource : IUsageEventSource
    {
        private readonly UsageSourceReadResult _result;

        public ScriptedUsageEventSource(
            AgentId agentId,
            SourceKind sourceKind,
            UsageSourceReadResult result)
        {
            AgentId = agentId;
            SourceKind = sourceKind;
            _result = result;
        }

        public AgentId AgentId { get; }

        public SourceKind SourceKind { get; }

        public Task<UsageSourceReadResult> ReadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_result);
    }

    private sealed class ScriptedWindowedSource : IWindowedSnapshotUsageEventSource
    {
        private readonly UsageSourceReadResult _result;

        public ScriptedWindowedSource(
            AgentId agentId,
            string eventParserVersion,
            int reconciliationWindowDays,
            UsageSourceReadResult result)
        {
            AgentId = agentId;
            EventParserVersion = eventParserVersion;
            ReconciliationWindowDays = reconciliationWindowDays;
            _result = result;
        }

        public AgentId AgentId { get; }

        public SourceKind SourceKind => SourceKind.LocalLog;

        public string EventParserVersion { get; }

        public int ReconciliationWindowDays { get; }

        public Task<UsageSourceReadResult> ReadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_result);
    }

    private sealed class ThrowingUsageEventSource(AgentId agentId) : IUsageEventSource
    {
        public AgentId AgentId { get; } = agentId;

        public SourceKind SourceKind => SourceKind.LocalLog;

        public Task<UsageSourceReadResult> ReadAsync(
            CancellationToken cancellationToken = default) =>
            throw new IOException("Synthetic provider failure.");
    }
}

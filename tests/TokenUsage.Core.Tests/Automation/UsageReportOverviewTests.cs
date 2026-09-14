using System.Security.Cryptography;
using System.Text;
using TokenUsage.Core.Automation;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;

namespace TokenUsage.Core.Tests.Automation;

public sealed class UsageReportOverviewTests
{
    private static readonly DateTimeOffset OccurredAt =
        new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

    private static readonly UsageDetailMetadata MeasuredInput = new(
        input: UsageComponentAvailability.Measured,
        cacheRead: UsageComponentAvailability.Measured,
        cacheWrite: UsageComponentAvailability.Measured);

    [Fact]
    public async Task OracleFixtureHasTwoAttributedSessionsAndMeasuredZero()
    {
        using var folder = new TemporaryFolder();
        (UsageReport report, IReadOnlyList<UsageOverviewObservation> observations) =
            await SeedOracleAsync(folder.DatabasePath, writeLinks: true);

        UsageReportOverview overview = UsageReportOverview.Build(
            report,
            observations,
            new UsageReportOverviewRequest(true, report.CacheComposition));

        Assert.Equal(640, overview.Tokens.Total);
        Assert.Equal(0.006m, overview.ReportedCostUsd);
        Assert.Null(overview.EstimatedCostUsd);
        Assert.Equal(40, overview.UnpricedTokens);
        Assert.Equal(5, overview.ObservationCount);
        Assert.Equal(UsageOverviewFactKind.Measured, overview.SessionAvailability);
        Assert.Equal(2, overview.AttributedSessionCount);
        Assert.Equal(1, overview.RootSessionCount);
        Assert.Equal(1, overview.DelegatedSessionCount);
        Assert.Equal(40, overview.UnassignedTokens);
        Assert.Equal(0.003m, overview.AttributedReportedCostPerSession);
        Assert.Null(overview.AttributedEstimatedCostPerSession);
        Assert.Equal(UsageOverviewFactKind.Measured, overview.CacheShareAvailability);
        Assert.Equal(0m, overview.CacheSharePercent);
        Assert.Equal("measured-input-cache-share/v1", overview.CacheShareMethod);
        Assert.Equal(UsageOverviewFactKind.Unavailable, overview.RequestCountAvailability);
        Assert.Equal("finality-not-established", overview.RequestCountReason);

        UsageOverviewRankedRow modelA = Assert.Single(overview.Models, row => row.ModelId?.Value == "model-a");
        UsageOverviewRankedRow modelB = Assert.Single(overview.Models, row => row.ModelId?.Value == "model-b");
        Assert.Equal(440, modelA.Tokens.Total);
        Assert.Equal(200, modelB.Tokens.Total);
        Assert.Equal(2, modelA.AttributedSessionCount);
        Assert.Equal(2, modelB.AttributedSessionCount);
        Assert.Equal(40, modelA.UnpricedTokens);
        Assert.Equal(0, modelB.UnpricedTokens);
        Assert.Equal(0.002m, modelB.ReportedCostUsd);
        Assert.Equal(2, modelB.ObservationCount);

        Assert.Equal(2, overview.Projects.Count(row => !row.IsUnassigned && row.Tokens.Total == 300));
        UsageOverviewRankedRow unassigned = Assert.Single(overview.Projects, row => row.IsUnassigned);
        Assert.Equal(40, unassigned.Tokens.Total);
        Assert.Equal(0, overview.Models.Count(row => row.IsOther));

        UsageReportSnapshotV2.Document snapshot = UsageReportSnapshotV2.Create(
            OccurredAt,
            new DateOnly(2026, 9, 13),
            new DateOnly(2026, 9, 13),
            1,
            agentId: null,
            report,
            overview: overview);
        string json = UsageReportSnapshotV2.Render(snapshot, "json");
        Assert.Contains("\"attributedSessionCount\": \"2\"", json, StringComparison.Ordinal);
        Assert.Contains("\"0.003\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("sessionKey", json, StringComparison.Ordinal);
        Assert.DoesNotContain("projectKey", json, StringComparison.Ordinal);
        Assert.Equal("finality-not-established", snapshot.Overview!.RequestCountReason);
        Assert.Null(snapshot.Overview.RequestCount);
    }

    [Fact]
    public async Task ModelBFilterKeepsBothSessionsAndMeasuredZero()
    {
        using var folder = new TemporaryFolder();
        (UsageReport report, IReadOnlyList<UsageOverviewObservation> observations) =
            await SeedOracleAsync(folder.DatabasePath, writeLinks: true);
        UsageReportSelection selection = new() { Models = [new ModelId("model-b")] };
        UsageReport filtered = UsageReportQuery.Select(report, selection);
        UsageReportOverview overview = UsageReportOverview.Build(
            filtered,
            UsageReportOverview.Filter(observations, selection),
            new UsageReportOverviewRequest(true, filtered.CacheComposition));

        Assert.Equal(200, overview.Tokens.Total);
        Assert.Equal(0.002m, overview.ReportedCostUsd);
        Assert.Equal(0, overview.UnpricedTokens);
        Assert.Equal(2, overview.AttributedSessionCount);
        Assert.Equal(1, overview.RootSessionCount);
        Assert.Equal(1, overview.DelegatedSessionCount);
        Assert.Equal(0, overview.UnassignedTokens);
        Assert.Equal(0.001m, overview.AttributedReportedCostPerSession);
        Assert.Equal(["model-b"], overview.Models.Select(row => row.ModelId?.Value));
        Assert.Equal(200, Assert.Single(overview.Models).Tokens.Total);
        Assert.Contains(overview.Projects, row => row.Tokens.Total == 200 && row.ObservationCount == 1);
        Assert.Contains(overview.Projects, row => row.Tokens.Total == 0 && row.ObservationCount == 1);
    }

    [Fact]
    public async Task DisabledAttributionDoesNotInventZeroSessions()
    {
        using var folder = new TemporaryFolder();
        (UsageReport report, IReadOnlyList<UsageOverviewObservation> observations) =
            await SeedOracleAsync(folder.DatabasePath, writeLinks: true);
        UsageReportOverview overview = UsageReportOverview.Build(
            report,
            observations,
            new UsageReportOverviewRequest(false, report.CacheComposition));

        Assert.Equal(UsageOverviewFactKind.Disabled, overview.SessionAvailability);
        Assert.Null(overview.AttributedSessionCount);
        Assert.Null(overview.RootSessionCount);
        Assert.Null(overview.DelegatedSessionCount);
        Assert.Null(overview.AttributedReportedCostPerSession);
        Assert.Equal(640, overview.UnassignedTokens);
        Assert.Equal(640, overview.Tokens.Total);
        Assert.Equal(UsageOverviewFactKind.Unavailable, overview.RequestCountAvailability);
    }

    [Fact]
    public void OtherRowUnionsSessionIdentitiesInsteadOfSummingModelCounts()
    {
        OpaqueAttributionKey session = Derive("session-shared");
        var observations = new List<UsageOverviewObservation>();
        for (int index = 0; index < 9; index++)
        {
            observations.Add(Observation(
                "rank-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "model-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                10,
                0.001m,
                session,
                parent: null,
                project: Derive("project-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        }

        UsageReport report = UsageReportQuery.Build(observations.Select(row => new DailyUsageRollup(
            new DateOnly(2026, 9, 13),
            "UTC",
            row.AgentId,
            row.Host,
            row.ModelId,
            row.Tokens,
            row.ReportedCostUsd,
            null,
            0,
            0,
            1,
            CoverageKind.Complete)));
        UsageReportOverview overview = UsageReportOverview.Build(
            report,
            observations,
            new UsageReportOverviewRequest(true, null),
            rankedCount: 8);

        Assert.Equal(1, overview.AttributedSessionCount);
        Assert.Equal(UsageOverviewFactKind.Unknown, overview.CacheShareAvailability);
        Assert.Null(overview.CacheSharePercent);
        UsageOverviewRankedRow other = Assert.Single(overview.Models, row => row.IsOther);
        Assert.Equal(10, other.Tokens.Total);
        Assert.Equal(1, other.OtherCount);
        Assert.Equal(1, other.AttributedSessionCount);
    }

    private static async Task<(UsageReport Report, IReadOnlyList<UsageOverviewObservation> Observations)> SeedOracleAsync(
        string databasePath,
        bool writeLinks)
    {
        UsageRepository repository = await UsageRepository.OpenAsync(databasePath);
        UsageEvent row1 = Event("oracle-1", "model-a", 100, CostObservation.ProviderReported(0.001m));
        UsageEvent row2 = Event("oracle-2", "model-b", 200, CostObservation.ProviderReported(0.002m));
        UsageEvent row3 = Event("oracle-3", "model-a", 300, CostObservation.ProviderReported(0.003m));
        UsageEvent row4 = Event("oracle-4", "model-a", 40, CostObservation.Unavailable());
        UsageEvent row5 = Event("oracle-5", "model-b", 0, CostObservation.ProviderReported(0m));
        await repository.IngestAsync([row1, row2, row3, row4, row5]);
        OpaqueAttributionKey session1 = Derive("session-s1");
        OpaqueAttributionKey session2 = Derive("session-s2");
        OpaqueAttributionKey project1 = Derive("project-p1");
        OpaqueAttributionKey project2 = Derive("project-p2");
        if (writeLinks)
        {
            await repository.ReplaceSessionLinksAsync(
            [
                new UsageSessionLink(row1.EventKey, session1, parentSessionKey: null, 1),
                new UsageSessionLink(row2.EventKey, session1, parentSessionKey: null, 1),
                new UsageSessionLink(row3.EventKey, session2, session1, 1),
                new UsageSessionLink(row5.EventKey, session2, session1, 1),
            ]);
            await repository.ReplaceProjectLinksAsync(
            [
                new UsageProjectLink(row1.EventKey, project1, 1, ProjectMappingKind.Observed),
                new UsageProjectLink(row2.EventKey, project1, 1, ProjectMappingKind.Observed),
                new UsageProjectLink(row3.EventKey, project2, 1, ProjectMappingKind.Observed),
                new UsageProjectLink(row5.EventKey, project2, 1, ProjectMappingKind.Observed),
            ]);
        }

        UsageReport report = await new UsageReportQuery(databasePath).ReadAsync(
            new DateOnly(2026, 9, 13),
            new DateOnly(2026, 9, 13),
            includeConfigurations: true);
        IReadOnlyList<UsageOverviewObservation> observations = await repository.ReadOverviewObservationsAsync(
            new DateOnly(2026, 9, 13),
            new DateOnly(2026, 9, 13),
            writeLinks ? 1 : 0,
            cursorSessionEpoch: 0,
            writeLinks ? 1 : 0);
        return (report, observations);
    }

    private static UsageOverviewObservation Observation(
        string id,
        string model,
        long tokens,
        decimal reported,
        OpaqueAttributionKey? session,
        OpaqueAttributionKey? parent,
        OpaqueAttributionKey? project) =>
        new(
            EventKey(id),
            new AgentId("codex"),
            new ModelProviderId("openai"),
            new ModelId(model),
            new TokenBreakdown(tokens, 0, 0, 0, 0),
            CostKind.ProviderReported,
            reported,
            null,
            session,
            parent,
            project,
            project is null ? null : ProjectMappingKind.Observed,
            ObservedModelId: null,
            ReasoningEffort: null,
            ServiceTier: null);

    private static UsageEvent Event(string id, string model, long tokens, CostObservation cost) =>
        new(
            EventKey(id),
            new AgentId("codex"),
            new ModelProviderId("openai"),
            new ModelId(model),
            OccurredAt,
            "UTC",
            new TokenBreakdown(tokens, 0, 0, 0, 0),
            cost,
            "fixture/1",
            cost.Kind == CostKind.Unavailable ? CoverageKind.Unpriced : CoverageKind.Complete,
            UsageTimePrecision.Timestamp,
            detailMetadata: MeasuredInput);

    private static UsageEventKey EventKey(string id) =>
        new(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))).ToLowerInvariant());

    private static OpaqueAttributionKey Derive(string identifier) =>
        new HmacOpaqueKeyDeriver(Enumerable.Repeat((byte)0x22, 32).ToArray())
            .Derive(OpaqueKeyDomains.CodexSession, "codex", identifier);

    private sealed class TemporaryFolder : IDisposable
    {
        public TemporaryFolder()
        {
            Root = Path.Combine(Path.GetTempPath(), "tokenusage-overview-" + Guid.NewGuid().ToString("N"));
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
            }
        }
    }
}

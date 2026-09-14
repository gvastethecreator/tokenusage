using System.Globalization;
using TokenUsage.Core.Automation;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;

namespace TokenUsage.Core.Tests.Usage;

public sealed class UsageReportSnapshotV2Tests
{
    [Fact]
    public void SnapshotOmitsAttributionAndKeepsInvariantStrings()
    {
        UsageReport report = UsageReportQuery.Build(
        [
            new DailyUsageRollup(
                new DateOnly(2026, 7, 22),
                "UTC",
                new AgentId("codex"),
                new ModelProviderId("openai"),
                new ModelId("gpt-5"),
                new TokenBreakdown(9_007_199_254_740_993, 0, 0, 0, 0),
                1.25m,
                null,
                0,
                0,
                1,
                CoverageKind.Complete),
        ]);
        UsageReportSnapshotV2.Document snapshot = UsageReportSnapshotV2.Create(
            new DateTimeOffset(2026, 7, 22, 15, 0, 0, TimeSpan.Zero),
            new DateOnly(2026, 7, 22),
            new DateOnly(2026, 7, 22),
            1,
            agentId: null,
            report);
        string json = UsageReportSnapshotV2.Render(snapshot, "json");
        Assert.Contains("\"schemaVersion\": \"tokenusage.report.v2\"", json, StringComparison.Ordinal);
        Assert.Contains("9007199254740993", json, StringComparison.Ordinal);
        Assert.Contains("\"1.25\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("sessionKey", json, StringComparison.Ordinal);
        Assert.DoesNotContain("projectKey", json, StringComparison.Ordinal);
        Assert.DoesNotContain("alias", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(json, UsageReportSnapshotV2.Render(snapshot, "json"));
    }

    [Fact]
    public void MoneyKeepsExactDecimalDigitsAndRoundTrips()
    {
        UsageReport report = UsageReportQuery.Build(
        [
            new DailyUsageRollup(
                new DateOnly(2026, 9, 13),
                "UTC",
                new AgentId("codex"),
                new ModelProviderId("openai"),
                new ModelId("gpt-5"),
                new TokenBreakdown(100, 0, 0, 0, 0),
                0.00000049m,
                0m,
                0,
                0,
                1,
                CoverageKind.Complete),
        ]);
        UsageReportSnapshotV2.Document snapshot = UsageReportSnapshotV2.Create(
            new DateTimeOffset(2026, 9, 13, 15, 0, 0, TimeSpan.Zero),
            new DateOnly(2026, 9, 13),
            new DateOnly(2026, 9, 13),
            1,
            agentId: null,
            report);
        Assert.Equal("0.00000049", snapshot.Totals.ReportedCost?.Amount);
        Assert.Equal("0", snapshot.Totals.EstimatedCost?.Amount);
        Assert.Equal(0.00000049m, decimal.Parse(snapshot.Totals.ReportedCost!.Amount, CultureInfo.InvariantCulture));
        Assert.Equal(0m, decimal.Parse(snapshot.Totals.EstimatedCost!.Amount, CultureInfo.InvariantCulture));
        UsageReport longFraction = UsageReportQuery.Build(
        [
            new DailyUsageRollup(
                new DateOnly(2026, 9, 13),
                "UTC",
                new AgentId("codex"),
                new ModelProviderId("openai"),
                new ModelId("gpt-5"),
                new TokenBreakdown(1, 0, 0, 0, 0),
                0.00000049001m,
                null,
                0,
                0,
                1,
                CoverageKind.Complete),
        ]);
        Assert.Equal(
            "0.00000049001",
            UsageReportSnapshotV2.Create(
                new DateTimeOffset(2026, 9, 13, 15, 0, 0, TimeSpan.Zero),
                new DateOnly(2026, 9, 13),
                new DateOnly(2026, 9, 13),
                1,
                agentId: null,
                longFraction).Totals.ReportedCost?.Amount);
        Assert.Null(UsageReportSnapshotV2.Create(
            new DateTimeOffset(2026, 9, 13, 15, 0, 0, TimeSpan.Zero),
            new DateOnly(2026, 9, 13),
            new DateOnly(2026, 9, 13),
            1,
            agentId: null,
            UsageReportQuery.Build(
            [
                new DailyUsageRollup(
                    new DateOnly(2026, 9, 13),
                    "UTC",
                    new AgentId("codex"),
                    new ModelProviderId("openai"),
                    new ModelId("gpt-5"),
                    new TokenBreakdown(1, 0, 0, 0, 0),
                    null,
                    null,
                    0,
                    0,
                    1,
                    CoverageKind.Unpriced),
            ])).Totals.ReportedCost);
        UsageReportSnapshotV2.Document compared = UsageReportSnapshotV2.Create(
            new DateTimeOffset(2026, 9, 13, 15, 0, 0, TimeSpan.Zero),
            new DateOnly(2026, 9, 13),
            new DateOnly(2026, 9, 13),
            1,
            agentId: null,
            report,
            comparison: UsageReportQuery.Build(
            [
                new DailyUsageRollup(
                    new DateOnly(2026, 9, 13),
                    "UTC",
                    new AgentId("codex"),
                    new ModelProviderId("openai"),
                    new ModelId("gpt-5"),
                    new TokenBreakdown(50, 0, 0, 0, 0),
                    2.5m,
                    null,
                    0,
                    0,
                    1,
                    CoverageKind.Complete),
            ]));
        Assert.Equal("100", snapshot.Totals.Tokens.Total);
        Assert.Equal("50", compared.Comparison?.Totals.Tokens.Total);
        Assert.Contains("0.00000049", UsageReportSnapshotV2.WriteCsv(snapshot), StringComparison.Ordinal);
        Assert.Contains("comparison", UsageReportSnapshotV2.WriteHtml(compared), StringComparison.Ordinal);
    }

    [Fact]
    public void SessionAndProjectExportUsesEphemeralIdsWithoutPersistentKeys()
    {
        var sessionKey = new OpaqueAttributionKey(new string('a', 64));
        var parentKey = new OpaqueAttributionKey(new string('b', 64));
        IReadOnlyList<UsageReportSnapshotV2.AttributionPopulationRow> sessions = UsageReportSnapshotV2.MapSessions(
        [
            new UsageSessionContribution(
                sessionKey,
                parentKey,
                new TokenBreakdown(10, 0, 0, 0, 0),
                new TokenBreakdown(40, 0, 0, 0, 0),
                1,
                2,
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch,
                IsUnassigned: false),
            new UsageSessionContribution(
                parentKey,
                ParentSessionKey: null,
                new TokenBreakdown(30, 0, 0, 0, 0),
                new TokenBreakdown(40, 0, 0, 0, 0),
                1,
                2,
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch,
                IsUnassigned: false),
            new UsageSessionContribution(
                SessionKey: null,
                ParentSessionKey: null,
                new TokenBreakdown(5, 0, 0, 0, 0),
                new TokenBreakdown(5, 0, 0, 0, 0),
                1,
                1,
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch,
                IsUnassigned: true),
        ]);
        Assert.Contains(sessions, row => row.ExportId == "s1" && row.ParentExportId == "s2");
        Assert.Contains(sessions, row => row.State == "unassigned");
        UsageReport report = UsageReportQuery.Build(
        [
            new DailyUsageRollup(
                new DateOnly(2026, 9, 13),
                "UTC",
                new AgentId("codex"),
                new ModelProviderId("openai"),
                new ModelId("gpt-5"),
                new TokenBreakdown(45, 0, 0, 0, 0),
                1m,
                null,
                0,
                0,
                1,
                CoverageKind.Complete),
        ]);
        string json = UsageReportSnapshotV2.Render(
            UsageReportSnapshotV2.Create(
                new DateTimeOffset(2026, 9, 13, 15, 0, 0, TimeSpan.Zero),
                new DateOnly(2026, 9, 13),
                new DateOnly(2026, 9, 13),
                1,
                agentId: null,
                report,
                sessions: [
                    new UsageSessionContribution(
                        sessionKey,
                        ParentSessionKey: null,
                        new TokenBreakdown(45, 0, 0, 0, 0),
                        new TokenBreakdown(45, 0, 0, 0, 0),
                        1,
                        1,
                        DateTimeOffset.UnixEpoch,
                        DateTimeOffset.UnixEpoch,
                        IsUnassigned: false),
                ]),
            "json");
        Assert.Contains("\"exportId\": \"s1\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain(sessionKey.Value, json, StringComparison.Ordinal);
        Assert.DoesNotContain("sessionKey", json, StringComparison.Ordinal);
        Assert.Contains("session", UsageReportSnapshotV2.WriteCsv(
            UsageReportSnapshotV2.Create(
                new DateTimeOffset(2026, 9, 13, 15, 0, 0, TimeSpan.Zero),
                new DateOnly(2026, 9, 13),
                new DateOnly(2026, 9, 13),
                1,
                agentId: null,
                report,
                sessions: [
                    new UsageSessionContribution(
                        sessionKey,
                        ParentSessionKey: null,
                        new TokenBreakdown(45, 0, 0, 0, 0),
                        new TokenBreakdown(45, 0, 0, 0, 0),
                        1,
                        1,
                        DateTimeOffset.UnixEpoch,
                        DateTimeOffset.UnixEpoch,
                        IsUnassigned: false),
                ])), StringComparison.Ordinal);
    }

    [Fact]
    public void OperationsOmitPersistentKeysAndFileHmac()
    {
        UsageReport report = UsageReportQuery.Build(
        [
            new DailyUsageRollup(
                new DateOnly(2026, 9, 13),
                "UTC",
                new AgentId("codex"),
                new ModelProviderId("openai"),
                new ModelId("gpt-5"),
                new TokenBreakdown(100, 0, 0, 0, 0),
                1m,
                null,
                0,
                0,
                1,
                CoverageKind.Complete),
        ]);
        string fileHmac = new string('a', 64);
        string json = UsageReportSnapshotV2.Render(
            UsageReportSnapshotV2.Create(
                new DateTimeOffset(2026, 9, 13, 15, 0, 0, TimeSpan.Zero),
                new DateOnly(2026, 9, 13),
                new DateOnly(2026, 9, 13),
                1,
                agentId: null,
                report,
                operations:
                [
                    new UsageOperationRankedRow(
                        "tool:null:Read:0",
                        UsageOperationKind.Tool,
                        "Read",
                        null,
                        1,
                        1,
                        0,
                        0,
                        OutcomesAvailable: true),
                    new UsageOperationRankedRow(
                        "file:" + fileHmac + ":patch:1",
                        UsageOperationKind.File,
                        "patch",
                        fileHmac,
                        2,
                        2,
                        0,
                        0,
                        OutcomesAvailable: true),
                ]),
            "json");
        Assert.Contains("\"exportId\": \"o1\"", json, StringComparison.Ordinal);
        Assert.Contains("\"kind\": \"tool\"", json, StringComparison.Ordinal);
        Assert.Contains("\"tool\": \"Read\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain(fileHmac, json, StringComparison.Ordinal);
        Assert.DoesNotContain("operation_key", json, StringComparison.Ordinal);
        Assert.DoesNotContain("arguments", json, StringComparison.Ordinal);
        Assert.DoesNotContain("src/alpha.rs", json, StringComparison.Ordinal);
    }
}

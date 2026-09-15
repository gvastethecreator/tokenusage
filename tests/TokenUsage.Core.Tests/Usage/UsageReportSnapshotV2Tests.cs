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

    [Fact]
    public void DerivedActivityAndWorkflowExportKeepMethodPopulationsAndUnits()
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
        UsageReportSnapshotV2.Document snapshot = UsageReportSnapshotV2.Create(
            new DateTimeOffset(2026, 9, 13, 15, 0, 0, TimeSpan.Zero),
            new DateOnly(2026, 9, 13),
            new DateOnly(2026, 9, 13),
            1,
            agentId: null,
            report,
            operations:
            [
                Ranked("file:hmac:patch:0", UsageOperationKind.File, "patch", 4),
                Ranked("tool:null:Read:1", UsageOperationKind.Tool, "Read", 1),
                Ranked("command:null:test:2", UsageOperationKind.Command, "test", 1),
                Ranked("spawn:null:reviewer:3", UsageOperationKind.Spawn, "reviewer", 1),
                Ranked("mcp:example:list:4", UsageOperationKind.Mcp, "list_things", 2, "example-server"),
            ],
            workflow: new UsageWorkflowSummary(
                UsageWorkflowIndicators.MethodVersion,
                UsageWorkflowIndicators.FirstEditUnavailableReason,
                1,
                0,
                4,
                1,
                0,
                0,
                UsageWorkflowIndicators.CostUnavailable,
                [],
                [],
                0,
                0,
                0));
        Assert.Equal("4", snapshot.DerivedActivity?.Edit);
        Assert.Equal("1", snapshot.DerivedActivity?.Read);
        Assert.Equal("1", snapshot.DerivedActivity?.Test);
        Assert.Equal("1", snapshot.DerivedActivity?.Delegate);
        Assert.Equal("2", snapshot.DerivedActivity?.Unknown);
        Assert.Equal("9", snapshot.DerivedActivity?.Eligible);
        Assert.Equal(UsageDerivedActivity.MethodVersion, snapshot.DerivedActivity?.Method);
        Assert.Equal(UsageDerivedActivity.CountUnit, snapshot.DerivedActivity?.Unit);
        Assert.Equal("1", snapshot.Workflow?.SameFileVerificationSeparated);
        Assert.Equal("4", snapshot.Workflow?.EligibleFileEdits);
        Assert.Equal("1", snapshot.Workflow?.EligibleVerifications);
        string json = UsageReportSnapshotV2.Render(snapshot, "json");
        Assert.Contains("derived-activity/v1", json, StringComparison.Ordinal);
        Assert.Contains("workflow-indicators/v1", json, StringComparison.Ordinal);
        Assert.Contains("\"edit\": \"4\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("filesConsentEpoch", json, StringComparison.Ordinal);
        string csv = UsageReportSnapshotV2.WriteCsv(snapshot);
        Assert.Contains("derived-activity/v1", csv, StringComparison.Ordinal);
        Assert.Contains("invocations", csv, StringComparison.Ordinal);
        Assert.Contains(",edit,4,", csv, StringComparison.Ordinal);
        Assert.Contains("eligible-file-edits", csv, StringComparison.Ordinal);
        string derivedEdit = csv.Split('\n').Single(line =>
            line.StartsWith("derived-activity", StringComparison.Ordinal)
            && line.Contains(",edit,", StringComparison.Ordinal));
        string[] derivedFields = derivedEdit.TrimEnd('\r').Split(',');
        Assert.Equal(string.Empty, derivedFields[4]);
        Assert.Equal(UsageDerivedActivity.MethodVersion, derivedFields[9]);
        Assert.Equal(UsageDerivedActivity.CountUnit, derivedFields[10]);
        Assert.Equal("edit", derivedFields[11]);
        Assert.Equal("4", derivedFields[12]);
        Assert.Equal("9", derivedFields[13]);
        string html = UsageReportSnapshotV2.WriteHtml(snapshot);
        Assert.Contains("<td>edit</td>", html, StringComparison.Ordinal);
        Assert.Contains("<td>read</td>", html, StringComparison.Ordinal);
        Assert.Contains("derived-activity/v1", html, StringComparison.Ordinal);
        Assert.Contains("eligible-file-edits", html, StringComparison.Ordinal);
        Assert.Contains("same-file-verification-separated", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<th>Events</th>", html.Substring(html.IndexOf("Derived activity", StringComparison.Ordinal)), StringComparison.Ordinal);
    }

    [Fact]
    public void HtmlRendersFrozenSelectionAndPricingLimits()
    {
        UsageReport report = UsageReportQuery.Build(
        [
            new DailyUsageRollup(
                new DateOnly(2026, 9, 13),
                "UTC",
                new AgentId("codex"),
                new ModelProviderId("openai"),
                new ModelId("model-a"),
                new TokenBreakdown(441, 0, 0, 0, 0),
                0.004049m,
                null,
                40,
                1,
                3,
                CoverageKind.Partial),
            new DailyUsageRollup(
                new DateOnly(2026, 9, 13),
                "UTC",
                new AgentId("codex"),
                new ModelProviderId("openai"),
                new ModelId("model-b"),
                new TokenBreakdown(200, 0, 0, 0, 0),
                0.002m,
                null,
                0,
                0,
                2,
                CoverageKind.Complete),
        ]);
        UsageReportSnapshotV2.Document snapshot = UsageReportSnapshotV2.Create(
            new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero),
            new DateOnly(2026, 9, 6),
            new DateOnly(2026, 9, 15),
            10,
            agentId: null,
            report,
            selection: new UsageReportSelection
            {
                Search = "model-a",
                Agents = [new AgentId("codex")],
                ModelProviders = [new ModelProviderId("openai")],
                Models = [new ModelId("model-a"), new ModelId("model-b")],
            });
        string html = UsageReportSnapshotV2.WriteHtml(snapshot);
        Assert.Contains("<h2>Selection</h2>", html, StringComparison.Ordinal);
        Assert.Contains("<dt>Search</dt>", html, StringComparison.Ordinal);
        Assert.Contains("model-a", html, StringComparison.Ordinal);
        Assert.Contains("<dt>Tools</dt>", html, StringComparison.Ordinal);
        Assert.Contains("<dt>Hosts</dt>", html, StringComparison.Ordinal);
        Assert.Contains("Unpriced tokens", html, StringComparison.Ordinal);
        Assert.Contains("Priced coverage", html, StringComparison.Ordinal);
        Assert.Contains(">40<", html, StringComparison.Ordinal);
        Assert.Contains("overflow-x:auto", html, StringComparison.Ordinal);
        Assert.DoesNotContain("table,thead,tbody,tr,th,td{display:block}", html, StringComparison.Ordinal);
        Assert.Contains("codex/openai/model-a", html, StringComparison.Ordinal);
        Assert.Contains("codex/openai/model-b", html, StringComparison.Ordinal);
        Assert.Contains("not a subscription invoice", html, StringComparison.Ordinal);
        string json = UsageReportSnapshotV2.Render(snapshot, "json");
        Assert.Contains("\"0.004049\"", json, StringComparison.Ordinal);
        Assert.Contains("\"0.002\"", json, StringComparison.Ordinal);

        UsageReportSnapshotV2.Document agentScoped = UsageReportSnapshotV2.Create(
            new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero),
            new DateOnly(2026, 9, 6),
            new DateOnly(2026, 9, 15),
            10,
            new AgentId("codex"),
            UsageReportQuery.Build([]));
        string agentHtml = UsageReportSnapshotV2.WriteHtml(agentScoped);
        Assert.Equal("codex", agentScoped.Selection.Agent);
        Assert.Equal(0, agentScoped.Selection.Tools?.Count ?? 0);
        Assert.Contains("<dt>Agent</dt><dd>codex</dd>", agentHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("<dt>Tools</dt><dd>All tools</dd>", agentHtml, StringComparison.Ordinal);

        UsageReportSnapshotV2.Selection bSelection = snapshot.Selection with
        {
            From = "2026-08-01",
            To = "2026-08-31",
        };
        UsageReportSnapshotV2.Document compared = snapshot with
        {
            Comparison = new UsageReportSnapshotV2.ComparisonSide(
                "B",
                bSelection,
                snapshot.Totals,
                snapshot.Models),
        };
        string comparedHtml = UsageReportSnapshotV2.WriteHtml(compared);
        Assert.Contains("<h2>Selection A</h2>", comparedHtml, StringComparison.Ordinal);
        Assert.Contains("<h2>Selection B</h2>", comparedHtml, StringComparison.Ordinal);
        Assert.Contains("2026-08-01", comparedHtml, StringComparison.Ordinal);
        Assert.Contains("2026-08-31", comparedHtml, StringComparison.Ordinal);
        Assert.Contains(snapshot.Selection.From, comparedHtml, StringComparison.Ordinal);
        string comparedJson = UsageReportSnapshotV2.Render(compared, "json");
        Assert.Contains("\"0.004049\"", comparedJson, StringComparison.Ordinal);
    }

    [Fact]
    public void HtmlShowsEstimatedCostWhenReportedIsUnavailable()
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
                null,
                1.5m,
                0,
                0,
                1,
                CoverageKind.Complete),
        ]);
        string html = UsageReportSnapshotV2.WriteHtml(UsageReportSnapshotV2.Create(
            new DateTimeOffset(2026, 9, 13, 15, 0, 0, TimeSpan.Zero),
            new DateOnly(2026, 9, 13),
            new DateOnly(2026, 9, 13),
            1,
            agentId: null,
            report));
        Assert.Contains("Estimated USD", html, StringComparison.Ordinal);
        Assert.Contains(">1.5<", html, StringComparison.Ordinal);
    }

    private static UsageOperationRankedRow Ranked(
        string id,
        UsageOperationKind kind,
        string tool,
        int count,
        string? server = null) =>
        new(
            id,
            kind,
            tool,
            server,
            count,
            count,
            0,
            0,
            OutcomesAvailable: true);
}

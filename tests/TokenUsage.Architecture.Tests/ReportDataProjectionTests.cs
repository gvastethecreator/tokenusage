using TokenUsage.App.ViewModels.Reports;
using TokenUsage.Core.Automation;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;

namespace TokenUsage.Architecture.Tests;

public sealed class ReportDataProjectionTests
{
    [Fact]
    public void ActiveDaysDeduplicateDatesAcrossRollupsAndKeepMoreThanThirtyModels()
    {
        var first = new DateOnly(2026, 9, 1);
        var rows = Enumerable.Range(0, 35).Select(index => Rollup(first, "codex", $"model-{index}", 100)).ToList();
        rows.Add(Rollup(first, "codex", "model-0", 20));
        rows.Add(Rollup(first.AddDays(1), "codex", "model-0", 0));
        rows.Add(Rollup(first.AddDays(2), "codex", "model-0", 50));
        rows.Add(Rollup(first, "opencode", "model-0", 10));
        rows.Add(Rollup(first.AddDays(1), "opencode", "model-0", 0));
        UsageReport report = UsageReportQuery.Build(rows);

        Assert.Equal(36, report.Models.Count);
        Assert.Equal(39, report.ModelDays.Count);
        Assert.Equal(120, report.ModelDays.Single(day => day.Date == first
            && day.AgentId.Value == "codex" && day.ModelId.Value == "model-0").Metrics.Tokens.Total);
        var active = ReportDataProjection.ActiveModelDays(report);
        Assert.Equal(2, active["codex/openai/model-0"]);
        Assert.Equal(1, active["opencode/openai/model-0"]);
        Assert.Equal(2, ReportDataProjection.ActiveProviderDays(report)["codex"]);
        Assert.Equal(1, ReportDataProjection.ActiveProviderDays(report)["opencode"]);
    }

    [Fact]
    public void NumericSortingUsesOriginalValuesKeepsTiesStableAndUnknownPricesLast()
    {
        (string Name, decimal? Cost)[] rows = [("unknown", null), ("two", 2), ("ten", 10), ("equal", 2), ("free", 0)];
        var descending = new ReportSortState(ReportSortColumn.Cost, true);
        Assert.Equal(["ten", "two", "equal", "free", "unknown"],
            ReportDataProjection.Order(rows, descending, row => row.Name, row => row.Cost).Select(row => row.Name));
        Assert.Equal(["free", "two", "equal", "ten", "unknown"],
            ReportDataProjection.Order(rows, descending.Toggle(ReportSortColumn.Cost), row => row.Name, row => row.Cost).Select(row => row.Name));
        Assert.False(descending.Toggle(ReportSortColumn.Name).Descending);
        Assert.True(descending.Toggle(ReportSortColumn.Date).Descending);
    }

    [Fact]
    public void ModelShadesFollowMetricTotalsAndDoNotDependOnInputOrder()
    {
        (string Id, decimal? Total)[] values = [("large", 100), ("small", 10), ("equal", 10), ("reserve", null)];
        var shades = ReportDataProjection.ModelShades("#80C0F0", values);
        var reordered = ReportDataProjection.ModelShades("#80C0F0", values.Reverse());
        Assert.Equal("#80C0F0", shades["large"]);
        Assert.Equal(shades["small"], shades["equal"]);
        Assert.True(Convert.ToInt32(shades["reserve"][1..3], 16) < Convert.ToInt32(shades["small"][1..3], 16));
        Assert.All(shades, pair => Assert.Equal(pair.Value, reordered[pair.Key]));
        Assert.Equal("Luna Reserve", ReportDataProjection.ModelName("gpt-reserve"));
        var unknown = new UsageReportMetrics(1, new TokenBreakdown(10, 0, 0, 0, 0), null, null, 10, 1, CoverageKind.Unpriced);
        Assert.Null(ReportDataProjection.KnownCost(unknown));
        Assert.Equal(0, ReportDataProjection.KnownCost(unknown with { ReportedCostUsd = 0 }));
    }

    private static DailyUsageRollup Rollup(DateOnly date, string provider, string model, long tokens) =>
        new(date, "UTC", new AgentId(provider), new ModelProviderId("openai"), new ModelId(model),
            new TokenBreakdown(tokens, 0, 0, 0, 0), 0, null, 0, 0, 1, CoverageKind.Complete);
}

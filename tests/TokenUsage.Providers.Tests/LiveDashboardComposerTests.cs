using TokenUsage.App.ViewModels;
using TokenUsage.App.ViewModels.Dashboard;
using TokenUsage.App.ViewModels.Sample;

namespace TokenUsage.Providers.Tests;

public sealed class LiveDashboardComposerTests
{
    [Fact]
    public void LocalSpendFlowsIntoTheTopLevelLiveSummary()
    {
        SpendSlice[] slices =
        [
            new("claude", "Claude", 4.25, "$4.25"),
            new("grok", "Grok Build", 1.75, "$1.75"),
        ];
        var localUsage = new LocalUsageCard(
            "Local usage",
            "Local logs",
            "Last 30 days",
            "",
            [],
            [],
            new LocalUsageSpendBreakdown(
                "Spend",
                "2 agents",
                "$6.00 USD",
                "Total $6.00 across 2 agents",
                slices,
                [],
                "$6.00"),
            []);

        DashboardSnapshot result = LiveDashboardComposer.Create(
            [],
            localUsage,
            [],
            "Live",
            Summarize);

        Assert.True(result.HasSpend);
        Assert.Equal("$6.00", result.TotalSpendAmount);
        Assert.Equal("$6", result.DonutCenterAmount);
        Assert.Equal("Last 30 days", result.PeriodLabel);
        Assert.Equal(slices, result.SpendSlices);
    }

    [Fact]
    public void QuotaOnlyDashboardKeepsTheFallbackPeriod()
    {
        DashboardSnapshot result = LiveDashboardComposer.Create(
            [],
            localUsage: null,
            [],
            "Updated now",
            Summarize);

        Assert.False(result.HasSpend);
        Assert.Equal("Updated now", result.PeriodLabel);
        Assert.Empty(result.SpendSlices);
    }

    [Fact]
    public void AccountSpendJoinsLocalSpendWithoutDuplicatingAProvider()
    {
        var localUsage = new LocalUsageCard(
            "Local usage",
            "Local logs",
            "Last 30 days",
            "",
            [],
            [],
            new LocalUsageSpendBreakdown(
                "Spend",
                "1 agent",
                "$4.00",
                "Local spend",
                [new SpendSlice("claude", "Claude", 4, "$4.00")],
                []),
            []);

        DashboardSnapshot result = LiveDashboardComposer.Create(
            [],
            localUsage,
            [
                new SpendSlice("vercel-ai-gateway", "Vercel AI Gateway", 6, "$6.00"),
                new SpendSlice("claude", "Claude account", 99, "$99.00"),
            ],
            "Live",
            Summarize);

        Assert.Equal(2, result.SpendSlices.Count);
        Assert.Equal(10d, result.SpendSlices.Sum(slice => slice.Amount));
        Assert.Equal("$10.00", result.TotalSpendAmount);
        Assert.Equal("2 providers, $10.00", result.SpendAccessibleName);
    }

    private static DashboardSpendSummary Summarize(IReadOnlyList<SpendSlice> slices)
    {
        double total = slices.Sum(slice => slice.Amount);
        return new DashboardSpendSummary(
            total.ToString("$0.00", System.Globalization.CultureInfo.InvariantCulture),
            total.ToString("$0", System.Globalization.CultureInfo.InvariantCulture),
            $"{slices.Count} providers, {total.ToString("$0.00", System.Globalization.CultureInfo.InvariantCulture)}");
    }
}

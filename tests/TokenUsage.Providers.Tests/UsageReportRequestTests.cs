using TokenUsage.App.ViewModels.Reports;

namespace TokenUsage.Providers.Tests;

public sealed class UsageReportRequestTests
{
    [Fact]
    public void GlobalDefaultsMatchTheCompactDashboardContract()
    {
        UsageReportRequest request = UsageReportRequest.Global;

        Assert.Equal(UsageReportScope.Global, request.Scope);
        Assert.Null(request.ProviderId);
        Assert.Equal(30, request.WindowDays);
        Assert.Equal(UsageReportMetric.Cost, request.Metric);
        Assert.Equal(UsageReportBreakdown.Model, request.Breakdown);
    }

    [Fact]
    public void ProviderRequestRequiresProviderAndKeepsContext()
    {
        var request = new UsageReportRequest(
            UsageReportScope.Provider,
            " codex ",
            90,
            UsageReportMetric.Tokens,
            UsageReportBreakdown.Source);

        Assert.Equal("codex", request.ProviderId);
        Assert.Equal(90, request.WindowDays);
        Assert.Equal(UsageReportMetric.Tokens, request.Metric);
        Assert.Equal(UsageReportBreakdown.Source, request.Breakdown);
        Assert.Throws<ArgumentException>(() => new UsageReportRequest(
            UsageReportScope.Provider));
    }

    [Fact]
    public void FocusDateAlwaysOpensTheDayBreakdown()
    {
        DateOnly date = new(2026, 8, 8);

        var request = new UsageReportRequest(focusDate: date);

        Assert.Equal(date, request.FocusDate);
        Assert.Equal(UsageReportBreakdown.Day, request.Breakdown);
    }
}

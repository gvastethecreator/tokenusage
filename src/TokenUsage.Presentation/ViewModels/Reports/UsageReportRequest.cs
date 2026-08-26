namespace TokenUsage.App.ViewModels.Reports;

public enum UsageReportScope
{
    Global,
    Provider,
    Compare,
}

public enum UsageReportCompareAxis
{
    Providers,
    Periods,
    Cycles,
}

public sealed record UsageReportRequest
{
    public static UsageReportRequest Global { get; } = new();

    public UsageReportRequest(
        UsageReportScope scope = UsageReportScope.Global,
        string? providerId = null,
        int windowDays = 30,
        UsageReportMetric metric = UsageReportMetric.Cost,
        UsageReportBreakdown breakdown = UsageReportBreakdown.Model,
        DateOnly? focusDate = null)
    {
        if (!Enum.IsDefined(scope))
        {
            throw new ArgumentOutOfRangeException(nameof(scope));
        }

        if (windowDays is not (1 or 3 or 7 or 30 or 90))
        {
            throw new ArgumentOutOfRangeException(nameof(windowDays));
        }

        if (!Enum.IsDefined(metric))
        {
            throw new ArgumentOutOfRangeException(nameof(metric));
        }

        if (!Enum.IsDefined(breakdown))
        {
            throw new ArgumentOutOfRangeException(nameof(breakdown));
        }

        if (scope == UsageReportScope.Provider
            && string.IsNullOrWhiteSpace(providerId))
        {
            throw new ArgumentException(
                "A provider report requires a provider ID.",
                nameof(providerId));
        }

        Scope = scope;
        ProviderId = string.IsNullOrWhiteSpace(providerId) ? null : providerId.Trim();
        WindowDays = windowDays;
        Metric = metric;
        Breakdown = focusDate is null ? breakdown : UsageReportBreakdown.Day;
        FocusDate = focusDate;
    }

    public UsageReportScope Scope { get; }

    public string? ProviderId { get; }

    public int WindowDays { get; }

    public UsageReportMetric Metric { get; }

    public UsageReportBreakdown Breakdown { get; }

    public DateOnly? FocusDate { get; }
}

public sealed class UsageReportRequestedEventArgs : EventArgs
{
    public UsageReportRequestedEventArgs(UsageReportRequest request) =>
        Request = request ?? throw new ArgumentNullException(nameof(request));

    public UsageReportRequest Request { get; }
}

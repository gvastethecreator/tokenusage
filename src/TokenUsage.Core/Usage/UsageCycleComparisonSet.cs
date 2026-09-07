using TokenUsage.Core.Automation;

namespace TokenUsage.Core.Usage;

public sealed record UsageCycleComparisonEntry(string Id, string ProviderId, string ProviderName,
    string Label, DateTimeOffset FromUtc, DateTimeOffset ToUtc, UsageReport Report,
    UsageReportCycleObservation Observation);

public static class UsageCycleComparisonSet
{
    public static TimeSpan MatchedDuration(IReadOnlyList<(string Id, TimeSpan Duration)> cycles)
    {
        ArgumentNullException.ThrowIfNull(cycles);
        if (cycles.Count is < 2 or > 4 || cycles.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != cycles.Count)
            throw new ArgumentException("Select two to four distinct cycles.", nameof(cycles));
        TimeSpan duration = cycles.Min(item => item.Duration);
        return duration > TimeSpan.Zero ? duration : TimeSpan.Zero;
    }
}

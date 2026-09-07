using TokenUsage.Core.Usage;

namespace TokenUsage.App.ViewModels.Reports;

public sealed record UsageReportResetMarker(int DayIndex, QuotaResetRecord Reset);

public static class UsageReportResetMarkers
{
    public static IReadOnlyList<UsageReportResetMarker> Calendar(
        IEnumerable<QuotaResetRecord> resets, IEnumerable<string> providers,
        DateOnly start, int days, TimeZoneInfo zone)
    {
        var included = providers.ToHashSet(StringComparer.Ordinal);
        return resets.Where(reset => included.Contains(reset.ProviderId))
            .Select(reset => new UsageReportResetMarker(
                DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(reset.OccurredAtUtc, zone).DateTime).DayNumber - start.DayNumber, reset))
            .Where(marker => marker.DayIndex >= 0 && marker.DayIndex < days)
            .OrderBy(marker => marker.Reset.OccurredAtUtc).ToArray();
    }

    public static IReadOnlyList<UsageReportResetMarker> Elapsed(
        IEnumerable<QuotaResetRecord> resets, string provider, DateTimeOffset start, DateTimeOffset end) =>
        resets.Where(reset => reset.ProviderId == provider && reset.OccurredAtUtc >= start && reset.OccurredAtUtc < end)
            .OrderBy(reset => reset.OccurredAtUtc)
            .Select(reset => new UsageReportResetMarker((int)(reset.OccurredAtUtc - start).TotalDays, reset)).ToArray();
}

using TokenUsage.Core.Usage;

namespace TokenUsage.App.ViewModels.Reports;

public sealed record UsageReportResetMarker(int DayIndex, QuotaResetRecord Reset);

public enum UsageReportResetKind { Weekly, Session, Manual, ResetCredit, Observed }

public sealed record UsageReportTrendReset(UsageReportResetKind Kind, string Text);

public static class UsageReportResetMarkers
{
    public static string LabelResourceKey(UsageReportResetKind kind) => kind switch
    {
        UsageReportResetKind.Weekly => "UsageReportResetKindWeekly",
        UsageReportResetKind.Session => "UsageReportResetKindSession",
        UsageReportResetKind.Manual => "UsageReportResetKindManual",
        UsageReportResetKind.ResetCredit => "UsageReportResetKindResetCredit",
        _ => "UsageReportResetKindObserved",
    };

    // A drop in usage is not evidence of a manual reset. Keep unknown causes explicit.
    public static UsageReportResetKind Classify(QuotaResetRecord reset) => reset.Cause switch
    {
        QuotaResetCause.Manual => UsageReportResetKind.Manual,
        QuotaResetCause.ResetCredit => UsageReportResetKind.ResetCredit,
        QuotaResetCause.Scheduled when reset.WindowDurationMinutes == 10_080m => UsageReportResetKind.Weekly,
        QuotaResetCause.Scheduled when reset.WindowDurationMinutes is > 0m and < 1_440m => UsageReportResetKind.Session,
        _ => UsageReportResetKind.Observed,
    };

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

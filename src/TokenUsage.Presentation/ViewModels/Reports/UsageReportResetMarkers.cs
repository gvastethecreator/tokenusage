using TokenUsage.Core.Usage;

namespace TokenUsage.App.ViewModels.Reports;

public sealed record UsageReportResetMarker(int DayIndex, QuotaResetRecord Reset);

public enum UsageReportResetKind { Weekly, Session, Manual, ResetCredit, Observed }

public readonly record struct UsageReportResetMark(int DayIndex, UsageReportResetKind Kind, int StackIndex);

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

    public static IReadOnlyList<UsageReportResetMark> Pack(IReadOnlyList<UsageReportResetMarker> markers)
    {
        ArgumentNullException.ThrowIfNull(markers);
        return markers.GroupBy(marker => marker.DayIndex)
            .SelectMany(day => day.Select(marker => Classify(marker.Reset)).Distinct().Order()
                .Select((kind, stack) => new UsageReportResetMark(day.Key, kind, stack)))
            .ToArray();
    }

    public static IReadOnlyList<UsageReportResetMark> PackDays(IReadOnlyList<UsageReportTrendDay> days)
    {
        ArgumentNullException.ThrowIfNull(days);
        return days.SelectMany((day, index) => day.Resets.Select(reset => reset.Kind).Distinct().Order()
                .Select((kind, stack) => new UsageReportResetMark(index, kind, stack)))
            .ToArray();
    }

    public static int MaxStack(IReadOnlyList<UsageReportResetMark> packed) =>
        packed.Count == 0 ? 0 : packed.Max(mark => mark.StackIndex) + 1;

    public static double RailTop(int stackIndex) => 2 + stackIndex * 12;

    public static double TopPaddingFor(IReadOnlyList<UsageReportResetMark> packed) =>
        8 + MaxStack(packed) * 12;
}

using TokenUsage.App.ViewModels.Reports;
using TokenUsage.Core.Usage;

namespace TokenUsage.Providers.Tests;

public sealed class UsageReportResetMarkersTests
{
    [Theory]
    [InlineData(QuotaResetCause.Scheduled, 10080, UsageReportResetKind.Weekly)]
    [InlineData(QuotaResetCause.Scheduled, 300, UsageReportResetKind.Session)]
    [InlineData(QuotaResetCause.Manual, 10080, UsageReportResetKind.Manual)]
    [InlineData(QuotaResetCause.ResetCredit, 300, UsageReportResetKind.ResetCredit)]
    [InlineData(QuotaResetCause.Unknown, 10080, UsageReportResetKind.Observed)]
    [InlineData(QuotaResetCause.Scheduled, 43200, UsageReportResetKind.Observed)]
    [InlineData(QuotaResetCause.Scheduled, 0, UsageReportResetKind.Observed)]
    public void MarkerKindUsesTheReportedCauseAndDuration(QuotaResetCause cause, int minutes, UsageReportResetKind expected)
    {
        var reset = Reset(DateTimeOffset.UnixEpoch) with { Cause = cause, WindowDurationMinutes = minutes };
        Assert.Equal(expected, UsageReportResetMarkers.Classify(reset));
    }

    [Fact]
    public void CalendarUsesDisplayZoneAndKeepsMultipleResetsWithoutIncludingOtherProviders()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("test-minus-three", TimeSpan.FromHours(-3), "test", "test");
        DateTimeOffset midnight = new(2026, 9, 7, 0, 0, 0, TimeSpan.Zero);
        var markers = UsageReportResetMarkers.Calendar(
            [Reset(midnight.AddHours(1)), Reset(midnight.AddHours(2)), Reset(midnight.AddHours(4)),
             Reset(midnight.AddHours(1)) with { ProviderId = "other" }],
            ["codex"], new DateOnly(2026, 9, 6), 1, zone);

        Assert.Equal(2, markers.Count);
        Assert.All(markers, marker => Assert.Equal(0, marker.DayIndex));
        Assert.Equal(midnight.AddHours(1), markers[0].Reset.OccurredAtUtc);
    }

    [Fact]
    public void ElapsedMarkersUseCycleOriginAndExcludeTheNextCycleBoundary()
    {
        DateTimeOffset start = new(2026, 9, 7, 18, 0, 0, TimeSpan.Zero);
        var markers = UsageReportResetMarkers.Elapsed(
            [Reset(start.AddTicks(-1)), Reset(start), Reset(start.AddDays(1)), Reset(start.AddDays(2))],
            "codex", start, start.AddDays(2));

        Assert.Collection(markers,
            marker => Assert.Equal(0, marker.DayIndex),
            marker => Assert.Equal(1, marker.DayIndex));
        Assert.Empty(UsageReportResetMarkers.Elapsed([], "codex", start, start.AddDays(2)));
    }

    private static QuotaResetRecord Reset(DateTimeOffset instant) => new(
        "codex", "quota.primary", instant, instant, instant.AddDays(-1), instant.AddMinutes(-1),
        50, 0, instant, instant.AddDays(1), 1440, QuotaResetDetectionKind.Scheduled,
        QuotaResetCause.Scheduled, QuotaChangeEvidenceKind.ExpectedBoundaryCrossed);
}

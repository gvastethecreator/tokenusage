using TokenUsage.App.Controls;

namespace TokenUsage.Architecture.Tests;

public sealed class UsageTrendGeometryTests
{
    [Fact]
    public void ScaleRoundsUpPastTheObservedPeak()
    {
        UsageTrendScale scale = UsageTrendGeometry.CreateScale(2_850, 4);

        Assert.True(scale.Maximum >= 2_850);
        Assert.Equal(0, scale.Ticks[0]);
        Assert.Equal(scale.Maximum, scale.Ticks[^1]);
    }

    [Fact]
    public void CompactChartsKeepOnlyBoundsToPreventYAxisOverlap()
    {
        Assert.Equal(
            [0d, 100d],
            UsageTrendGeometry.SelectTicksForHeight([0, 25, 50, 75, 100], height: 56));
        Assert.Equal(
            [0d, 25d, 50d, 75d, 100d],
            UsageTrendGeometry.SelectTicksForHeight([0, 25, 50, 75, 100], height: 120));
    }

    [Fact]
    public void HoverRefreshesOnlyWhenTheVisibleDataPointChanges()
    {
        Assert.False(UsageTrendGeometry.ShouldRefreshHover(12, 12, isVisible: true));
        Assert.True(UsageTrendGeometry.ShouldRefreshHover(12, 13, isVisible: true));
        Assert.True(UsageTrendGeometry.ShouldRefreshHover(12, 12, isVisible: false));
    }

    [Fact]
    public void MonotoneCurveKeepsControlPointsInsideEachSegmentRange()
    {
        UsageTrendPath path = UsageTrendGeometry.CreatePath(
            [0, 80, 5, 60, 0],
            width: 400,
            height: 200,
            maximum: 100);

        Assert.Equal(4, path.Segments.Count);
        foreach (UsageTrendSegment segment in path.Segments)
        {
            double minimum = Math.Min(segment.From.Y, segment.To.Y);
            double maximum = Math.Max(segment.From.Y, segment.To.Y);
            Assert.InRange(segment.Control1.Y, minimum, maximum);
            Assert.InRange(segment.Control2.Y, minimum, maximum);
        }
    }
}

using TokenUsage.App.Controls;
using TokenUsage.Core.Appearance;

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

    [Fact]
    public void SingleValuePathKeepsThePointInsideThePlotForAMarker()
    {
        UsageTrendPath path = UsageTrendGeometry.CreatePath(
            [42],
            width: 400,
            height: 200,
            maximum: 100);

        UsageTrendPoint point = Assert.Single(path.Points);
        Assert.Empty(path.Segments);
        Assert.Equal(200, point.X);
        Assert.InRange(point.Y, 8, 200);
    }

    [Fact]
    public void OverlaidBarsShareBaselinesAndDrawSmallValuesInFrontWithoutSummingPeriods()
    {
        IReadOnlyList<double>[] values = [new double[] { 4_000, 40 }, new double[] { 40, 4 }];
        UsageTrendScale scale = UsageTrendGeometry.CreateScale(UsageTrendLayouts.Peak(values, stacked: false));
        Assert.Equal(scale.Normalize(4_000) / 100, scale.Normalize(40), 12);
        Assert.Equal(4_040, UsageTrendLayouts.Peak(values, stacked: true));
        Assert.Equal([4_040d, 44d], UsageTrendLayouts.Bands(values, independent: false)[1].Upper);
        Assert.Equal(values[1], UsageTrendLayouts.Bands(values, independent: true)[1].Upper);
        var bars = UsageTrendLayouts.Bars(values, 2, 400, 200, scale.Maximum);
        Assert.Equal(4, bars.Count);
        Assert.Equal(bars[0].Height / 100, bars[1].Height, 12);
        Assert.Equal(bars[0].X, bars[1].X);
        Assert.Equal(bars[0].Y + bars[0].Height, bars[1].Y + bars[1].Height, 10);
        Assert.True(bars[2].X > bars[1].X);
        Assert.Empty(UsageTrendLayouts.Bars([new double[] { 0, double.NaN }], 2, 400, 200, 100));
        var reversed = UsageTrendLayouts.Bars(values.Reverse().ToArray(), 2, 400, 200, 5000);
        Assert.Equal(1, reversed[0].SeriesIndex);
        Assert.Equal(0, reversed[1].SeriesIndex);
        Assert.Equal(reversed[0].Width, reversed[1].Width);

        UsageTrendScale small = UsageTrendGeometry.CreateScale(4000, emphasizeSmallValues: true);
        Assert.True(small.Normalize(40) > scale.Normalize(40));
        Assert.Equal(0, small.Normalize(0));
        Assert.Equal(1, small.Normalize(small.Maximum));
        var path = UsageTrendGeometry.CreatePath([40, double.NaN, 4000], 400, 200,
            small.Maximum, bottomPadding: 10, emphasizeSmallValues: true);
        Assert.Equal(190 - small.Normalize(40) * 182, path.Points[0].Y, 10);
        Assert.True(double.IsNaN(path.Points[1].Y));
        var timed = UsageTrendLayouts.Bars([Enumerable.Repeat(1d, 12).ToArray()], 12, 240, 100, 1);
        Assert.Equal(12, timed.Count);
        Assert.True(timed[^1].X + timed[^1].Width <= 240);
    }

    [Fact]
    public void StraightAndStepPathsHaveDistinctGeometryAndKeepMissingPricesAsGaps()
    {
        var line = UsageTrendGeometry.CreatePath([0, 100], 400, 200, 100, style: ReportChartStyle.Line);
        var step = UsageTrendGeometry.CreatePath([0, 100], 400, 200, 100, style: ReportChartStyle.Step);
        Assert.Single(line.Segments);
        Assert.Equal(line.Points[0], line.Segments[0].Control1);
        Assert.Equal(2, step.Segments.Count);
        Assert.Equal(step.Segments[0].From.Y, step.Segments[0].To.Y);
        Assert.Equal(step.Segments[1].From.X, step.Segments[1].To.X);
        var unknown = UsageTrendGeometry.CreatePath([1, double.NaN, 2], 400, 200, 100);
        Assert.True(double.IsNaN(unknown.Points[1].Y));
    }

    [Fact]
    public void ZeroValuesStayOnThePaddedBaselineInsteadOfBelowTheAxis()
    {
        UsageTrendPath path = UsageTrendGeometry.CreatePath(
            [0, 100],
            width: 400,
            height: 200,
            maximum: 100,
            topPadding: 8,
            bottomPadding: 10);

        Assert.Equal(190, path.Points[0].Y);
        Assert.Equal(8, path.Points[1].Y);
        Assert.All(path.Segments, segment =>
        {
            Assert.InRange(segment.Control1.Y, 8, 190);
            Assert.InRange(segment.Control2.Y, 8, 190);
        });
    }
}

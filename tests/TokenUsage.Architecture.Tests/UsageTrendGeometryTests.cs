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
            [10, 80, 5, 60, 15],
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
    public void StackedBarsSumTheDayWhileComparisonBarsStayOverlaid()
    {
        IReadOnlyList<double>[] values = [new double[] { 4_000, 40 }, new double[] { 40, 4 }];
        UsageTrendScale stackedScale = UsageTrendGeometry.CreateScale(UsageTrendLayouts.Peak(values, stacked: true));
        var stacked = UsageTrendLayouts.Bars(values, 2, 400, 200, stackedScale.Maximum, stacked: true);
        Assert.Equal(4, stacked.Count);
        Assert.Equal(stacked[0].Y, stacked[1].Y + stacked[1].Height, 10);
        Assert.False(stacked[0].IsTop);
        Assert.True(stacked[1].IsTop);
        Assert.Equal(stackedScale.Normalize(4_040) * 182, stacked[0].Height + stacked[1].Height, 10);
        double[][] shares = [[60, 25], [40, 75]];
        var percentage = UsageTrendLayouts.Bars(shares, 2, 400, 200, 100, stacked: true);
        Assert.All(percentage.Where(bar => bar.IsTop), bar => Assert.Equal(8, bar.Y, 10));

        // Comparison periods overlay: summing two periods would invent a total that never happened.
        UsageTrendScale scale = UsageTrendGeometry.CreateScale(UsageTrendLayouts.Peak(values, stacked: false));
        Assert.Equal(scale.Normalize(4_000) / 100, scale.Normalize(40), 12);
        Assert.Equal(4_040, UsageTrendLayouts.Peak(values, stacked: true));
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
        var daily = UsageTrendLayouts.Bars([new double[] { 1, 1, 1 }], 3, 360, 100, 1);
        Assert.Equal(3, daily.Count);
        Assert.True(daily[^1].X + daily[^1].Width <= 360);
        var stubs = UsageTrendLayouts.EmptyDayStubs([new double[] { 10, 0, double.NaN }], 3, 300, 200, top: 8, bottom: 10);
        UsageTrendBaselineStub stub = Assert.Single(stubs);
        Assert.Equal(1, stub.DayIndex);
        Assert.Equal(2, stub.Height);
        Assert.Equal(188, stub.Y);
        Assert.Empty(UsageTrendLayouts.EmptyDayStubs([new double[] { 10, double.NaN }], 2, 200, 100));
        Assert.Empty(UsageTrendLayouts.EmptyDayStubs([new double[] { 4, 5 }], 2, 200, 100));
        Assert.Equal(12, UsageTrendLayouts.EmptyDayStubs([Enumerable.Repeat(0d, 12).ToArray()], 12, 240, 100).Count);
        Assert.Single(UsageTrendLayouts.EmptyDayStubs([new double[] { 0 }], 1, 240, 100));
    }

    [Fact]
    public void PathsContinueAcrossUnaccountedDays()
    {
        var unknown = UsageTrendGeometry.CreatePath([1, double.NaN, 2], 400, 200, 100);
        Assert.True(double.IsNaN(unknown.Points[1].Y));
        UsageTrendSegment bridge = Assert.Single(unknown.Segments);
        Assert.Equal(unknown.Points[0], bridge.From);
        Assert.Equal(unknown.Points[2], bridge.To);
        var unknownLine = UsageTrendGeometry.CreatePath(
            [1, double.NaN, double.NaN, 2], 400, 200, 100);
        Assert.True(double.IsNaN(unknownLine.Points[1].Y));
        Assert.True(double.IsNaN(unknownLine.Points[2].Y));
        UsageTrendSegment lineBridge = Assert.Single(unknownLine.Segments);
        Assert.Equal(unknownLine.Points[0], lineBridge.From);
        Assert.Equal(unknownLine.Points[3], lineBridge.To);
        var idle = UsageTrendGeometry.CreatePath([10, 0, 20], 400, 200, 100);
        Assert.Equal(200, idle.Points[1].Y);
        UsageTrendSegment idleBridge = Assert.Single(idle.Segments);
        Assert.Equal(idle.Points[0], idleBridge.From);
        Assert.Equal(idle.Points[2], idleBridge.To);
        var trailing = UsageTrendGeometry.CreatePath([10, 20, 0], 400, 200, 100);
        Assert.Equal(2, trailing.Segments.Count);
        Assert.Equal(trailing.Points[^1].X, trailing.Segments[^1].To.X);
        Assert.Equal(trailing.Points[1].Y, trailing.Segments[^1].To.Y);
        Assert.Empty(UsageTrendGeometry.CreatePath([0, 0, double.NaN], 400, 200, 100).Segments);
        Assert.Empty(UsageTrendGeometry.CreatePath([double.NaN, 4], 400, 200, 100).Segments);
        Assert.True(UsageTrendGeometry.IsAccounted(1));
        Assert.False(UsageTrendGeometry.IsAccounted(0));
        Assert.False(UsageTrendGeometry.IsAccounted(double.NaN));
        Assert.Equal(UsageTrendPointKind.Measured, UsageTrendGeometry.ClassifyValue(1));
        Assert.Equal(UsageTrendPointKind.Unobserved, UsageTrendGeometry.ClassifyValue(0));
        Assert.Equal(UsageTrendPointKind.Unavailable, UsageTrendGeometry.ClassifyValue(double.NaN));
    }

    [Fact]
    public void PointKindsDistinguishInteriorGapsTrailingZerosMeasuredZerosAndUnavailable()
    {
        UsageTrendPointKind[] interiorKinds =
        [
            UsageTrendPointKind.Measured,
            UsageTrendPointKind.Unobserved,
            UsageTrendPointKind.Measured,
        ];
        var interior = UsageTrendGeometry.CreatePath(
            [10, 0, 20], 400, 200, 100, pointKinds: interiorKinds);
        UsageTrendSegment interiorSpan = Assert.Single(interior.Segments);
        Assert.Equal(UsageTrendSpanKind.Unobserved, interiorSpan.SpanKind);
        Assert.False(interiorSpan.IsTrailingContinuation);
        Assert.Equal(interior.Points[0], interiorSpan.From);
        Assert.Equal(interior.Points[2], interiorSpan.To);

        UsageTrendPointKind[] trailingKinds =
        [
            UsageTrendPointKind.Measured,
            UsageTrendPointKind.Measured,
            UsageTrendPointKind.Unobserved,
        ];
        var trailing = UsageTrendGeometry.CreatePath(
            [10, 20, 0], 400, 200, 100, pointKinds: trailingKinds);
        Assert.Equal(2, trailing.Segments.Count);
        Assert.Equal(UsageTrendSpanKind.Observed, trailing.Segments[0].SpanKind);
        Assert.Equal(UsageTrendSpanKind.Unobserved, trailing.Segments[1].SpanKind);
        Assert.True(trailing.Segments[^1].IsTrailingContinuation);
        Assert.Equal(trailing.Points[^1].X, trailing.Segments[^1].To.X);
        Assert.Equal(trailing.Points[1].Y, trailing.Segments[^1].To.Y);

        UsageTrendPointKind[] zeroKinds =
        [
            UsageTrendPointKind.Measured,
            UsageTrendPointKind.Measured,
        ];
        var zero = UsageTrendGeometry.CreatePath(
            [0, 100], 400, 200, 100, topPadding: 8, bottomPadding: 10, pointKinds: zeroKinds);
        Assert.Equal(190, zero.Points[0].Y);
        UsageTrendSegment zeroSpan = Assert.Single(zero.Segments);
        Assert.Equal(UsageTrendSpanKind.Observed, zeroSpan.SpanKind);
        Assert.Equal(zero.Points[0], zeroSpan.From);
        Assert.Equal(zero.Points[1], zeroSpan.To);

        UsageTrendPointKind[] unavailableKinds =
        [
            UsageTrendPointKind.Measured,
            UsageTrendPointKind.Unavailable,
            UsageTrendPointKind.Measured,
        ];
        var unavailable = UsageTrendGeometry.CreatePath(
            [1, double.NaN, 2], 400, 200, 100, pointKinds: unavailableKinds);
        UsageTrendSegment unavailableSpan = Assert.Single(unavailable.Segments);
        Assert.Equal(UsageTrendSpanKind.Unavailable, unavailableSpan.SpanKind);
        Assert.True(double.IsNaN(unavailable.Points[1].Y));
        Assert.Empty(UsageTrendGeometry.CreatePath(
            [0, 0, 0], 400, 200, 100, pointKinds:
            [
                UsageTrendPointKind.Unobserved,
                UsageTrendPointKind.Unobserved,
                UsageTrendPointKind.Unobserved,
            ]).Segments);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DailyVerticesShareOneFadeAcrossConsecutiveAbsentDays(bool trailing)
    {
        UsageTrendPointKind[] kinds = [UsageTrendPointKind.Measured, UsageTrendPointKind.Unobserved,
            UsageTrendPointKind.Unobserved, UsageTrendPointKind.Unobserved,
            trailing ? UsageTrendPointKind.Unobserved : UsageTrendPointKind.Measured];
        UsageTrendPath path = UsageTrendGeometry.CreatePath([50, 50, 50, 50, 50], 400, 200, 100,
            pointKinds: kinds, includeCarriedVertices: true);
        Assert.Equal(4, path.Segments.Count);
        UsageTrendFade fade = Assert.IsType<UsageTrendFade>(path.Segments[0].Fade);
        Assert.All(path.Segments, segment => Assert.Equal(fade, segment.Fade));
        Assert.Equal(1, fade.OpacityAt(0));
        Assert.InRange(fade.OpacityAt(50), UsageTrendFade.DimOpacity, 1);
        Assert.Equal(UsageTrendFade.DimOpacity, fade.OpacityAt(100));
        Assert.Equal(UsageTrendFade.DimOpacity, fade.OpacityAt(200));
        Assert.Equal(UsageTrendFade.DimOpacity, fade.OpacityAt(300));
        Assert.Equal(trailing ? UsageTrendFade.DimOpacity : 1, fade.OpacityAt(400));
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

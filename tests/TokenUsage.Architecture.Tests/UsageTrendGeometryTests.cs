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
        Assert.Equal(3, UsageTrendLayouts.BarSlots(ReportChartStyle.Bars, 3));
        Assert.Equal(0, UsageTrendLayouts.BarSlots(ReportChartStyle.TwoHourBars, 0));
        Assert.Equal(36, UsageTrendLayouts.BarSlots(ReportChartStyle.TwoHourBars, 3));
        var timed = UsageTrendLayouts.Bars(
            [Enumerable.Repeat(1d, 36).ToArray()],
            UsageTrendLayouts.BarSlots(ReportChartStyle.TwoHourBars, 3),
            360,
            100,
            1);
        Assert.Equal(36, timed.Count);
        Assert.True(timed[^1].X + timed[^1].Width <= 360);
        var daily = UsageTrendLayouts.Bars([new double[] { 1, 1, 1 }], UsageTrendLayouts.BarSlots(ReportChartStyle.Bars, 3), 360, 100, 1);
        Assert.Equal(3, daily.Count);
        Assert.True(timed[0].Width < daily[0].Width);
        var stubs = UsageTrendLayouts.EmptyDayStubs([new double[] { 10, 0, double.NaN }], 3, 300, 200, top: 8, bottom: 10);
        UsageTrendBaselineStub stub = Assert.Single(stubs);
        Assert.Equal(1, stub.DayIndex);
        Assert.Equal(2, stub.Height);
        Assert.Equal(188, stub.Y);
        Assert.Empty(UsageTrendLayouts.EmptyDayStubs([new double[] { 10, double.NaN }], 2, 200, 100));
        Assert.Empty(UsageTrendLayouts.EmptyDayStubs([new double[] { 4, 5 }], 2, 200, 100));
        Assert.Equal(12, UsageTrendLayouts.EmptyDayStubs([Enumerable.Repeat(0d, 12).ToArray()], 12, 240, 100).Count);
        Assert.Equal(12, UsageTrendLayouts.BarSlots(ReportChartStyle.TwoHourBars, 1));
        var oneDayTimed = UsageTrendLayouts.Bars(
            [Enumerable.Repeat(1d, 12).ToArray()],
            UsageTrendLayouts.BarSlots(ReportChartStyle.TwoHourBars, 1),
            240,
            100,
            1);
        Assert.Equal(12, oneDayTimed.Count);
        Assert.True(oneDayTimed[^1].X + oneDayTimed[^1].Width <= 240);
        Assert.Single(UsageTrendLayouts.EmptyDayStubs([new double[] { 0 }], 1, 240, 100));
    }

    [Fact]
    public void StraightAndStepPathsHaveDistinctGeometryAndContinueAcrossUnaccountedDays()
    {
        var line = UsageTrendGeometry.CreatePath([20, 100], 400, 200, 100, style: ReportChartStyle.Line);
        var step = UsageTrendGeometry.CreatePath([20, 100], 400, 200, 100, style: ReportChartStyle.Step);
        Assert.Single(line.Segments);
        Assert.Equal(line.Points[0], line.Segments[0].Control1);
        Assert.Equal(2, step.Segments.Count);
        Assert.Equal(step.Segments[0].From.Y, step.Segments[0].To.Y);
        Assert.Equal(step.Segments[1].From.X, step.Segments[1].To.X);
        var unknown = UsageTrendGeometry.CreatePath([1, double.NaN, 2], 400, 200, 100);
        Assert.True(double.IsNaN(unknown.Points[1].Y));
        UsageTrendSegment bridge = Assert.Single(unknown.Segments);
        Assert.Equal(unknown.Points[0], bridge.From);
        Assert.Equal(unknown.Points[2], bridge.To);
        var unknownLine = UsageTrendGeometry.CreatePath(
            [1, double.NaN, double.NaN, 2], 400, 200, 100, style: ReportChartStyle.Line);
        Assert.True(double.IsNaN(unknownLine.Points[1].Y));
        Assert.True(double.IsNaN(unknownLine.Points[2].Y));
        UsageTrendSegment lineBridge = Assert.Single(unknownLine.Segments);
        Assert.Equal(unknownLine.Points[0], lineBridge.From);
        Assert.Equal(unknownLine.Points[3], lineBridge.To);
        var idle = UsageTrendGeometry.CreatePath([10, 0, 20], 400, 200, 100, style: ReportChartStyle.Line);
        Assert.Equal(200, idle.Points[1].Y);
        UsageTrendSegment idleBridge = Assert.Single(idle.Segments);
        Assert.Equal(idle.Points[0], idleBridge.From);
        Assert.Equal(idle.Points[2], idleBridge.To);
        var trailing = UsageTrendGeometry.CreatePath([10, 20, 0], 400, 200, 100, style: ReportChartStyle.Line);
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
            [10, 0, 20], 400, 200, 100, style: ReportChartStyle.Line, pointKinds: interiorKinds);
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
            [10, 20, 0], 400, 200, 100, style: ReportChartStyle.Line, pointKinds: trailingKinds);
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
            [0, 100], 400, 200, 100, topPadding: 8, bottomPadding: 10, style: ReportChartStyle.Line, pointKinds: zeroKinds);
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

    [Fact]
    public void StackedAreaContinuesEachSeriesBeforeStackingSoBandsDoNotInvert()
    {
        IReadOnlyList<double>[] values = [[100, 200], [50, 0]];
        IReadOnlyList<UsageTrendPointKind>?[] kinds =
        [
            [UsageTrendPointKind.Measured, UsageTrendPointKind.Measured],
            [UsageTrendPointKind.Measured, UsageTrendPointKind.Unobserved],
        ];
        IReadOnlyList<UsageTrendBand> bands = UsageTrendLayouts.Bands(values, independent: false, kinds);
        Assert.Equal([100d, 200d], bands[0].Upper);
        Assert.Equal([100d, 200d], bands[1].Lower);
        Assert.Equal([150d, 250d], bands[1].Upper);
        Assert.True(bands[1].Upper[1] >= bands[0].Upper[1]);
        Assert.Equal(250, UsageTrendLayouts.Peak(values, stacked: true, kinds));
        Assert.Equal(200, UsageTrendLayouts.Peak(values, stacked: true));

        UsageTrendPath upper = UsageTrendGeometry.CreatePath(
            bands[1].Upper,
            400,
            200,
            250,
            style: ReportChartStyle.Line,
            pointKinds: kinds[1],
            includeCarriedVertices: true);
        UsageTrendSegment span = Assert.Single(upper.Segments);
        Assert.Equal(UsageTrendSpanKind.Unobserved, span.SpanKind);
        Assert.True(span.IsTrailingContinuation);
        Assert.Equal(upper.Points[0], span.From);
        Assert.Equal(upper.Points[1], span.To);
        UsageTrendPath lower = UsageTrendGeometry.CreatePath(
            bands[1].Lower,
            400,
            200,
            250,
            style: ReportChartStyle.Line,
            pointKinds: kinds[1],
            includeCarriedVertices: true);
        Assert.True(span.To.Y < lower.Segments[^1].To.Y);
    }

    [Fact]
    public void PercentageAreaReplacesMeasuredCompositionAndCarriesOnlyWholeEmptyDays()
    {
        IReadOnlyList<double>[] values = [[100, 0, 0, 0, 0], [0, 100, 0, 100, 0]];
        IReadOnlyList<UsageTrendPointKind>?[] kinds =
        [
            [UsageTrendPointKind.Measured, UsageTrendPointKind.Unobserved, UsageTrendPointKind.Unobserved,
                UsageTrendPointKind.Unobserved, UsageTrendPointKind.Unobserved],
            [UsageTrendPointKind.Unobserved, UsageTrendPointKind.Measured, UsageTrendPointKind.Unobserved,
                UsageTrendPointKind.Measured, UsageTrendPointKind.Measured],
        ];
        IReadOnlyList<UsageTrendBand> bands = UsageTrendLayouts.Bands(values, false, kinds, percentage: true);
        Assert.Equal([100d, 0, 0, 0, 0], bands[0].Upper);
        Assert.Equal([100d, 0, 0, 0, 0], bands[1].Lower);
        Assert.Equal([100d, 100, 100, 100, 0], bands[1].Upper);
        UsageTrendPath upper = UsageTrendGeometry.CreatePath(bands[1].Upper, 400, 200, 100,
            topPadding: 8, bottomPadding: 10, pointKinds: kinds[1], includeCarriedVertices: true);
        UsageTrendPath lower = UsageTrendGeometry.CreatePath(bands[1].Lower, 400, 200, 100,
            topPadding: 8, bottomPadding: 10, pointKinds: kinds[1], includeCarriedVertices: true);
        Assert.True(upper.Points[1].Y < lower.Points[1].Y);
        Assert.Equal(190, upper.Points[4].Y);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DailyAreaVerticesShareOneFadeAcrossConsecutiveAbsentDays(bool trailing)
    {
        UsageTrendPointKind[] kinds = [UsageTrendPointKind.Measured, UsageTrendPointKind.Unobserved,
            UsageTrendPointKind.Unobserved, UsageTrendPointKind.Unobserved,
            trailing ? UsageTrendPointKind.Unobserved : UsageTrendPointKind.Measured];
        UsageTrendPath path = UsageTrendGeometry.CreatePath([50, 50, 50, 50, 50], 400, 200, 100,
            style: ReportChartStyle.Area, pointKinds: kinds, includeCarriedVertices: true);
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

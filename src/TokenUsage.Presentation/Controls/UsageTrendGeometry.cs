using TokenUsage.Core.Appearance;

namespace TokenUsage.App.Controls;

public readonly record struct UsageTrendPoint(double X, double Y);

public readonly record struct UsageTrendSegment(
    UsageTrendPoint From,
    UsageTrendPoint Control1,
    UsageTrendPoint Control2,
    UsageTrendPoint To);

public sealed record UsageTrendPath(
    IReadOnlyList<UsageTrendPoint> Points,
    IReadOnlyList<UsageTrendSegment> Segments);

public readonly record struct UsageTrendScale(
    double Maximum,
    IReadOnlyList<double> Ticks)
{
    public double Normalize(double value)
    {
        if (Maximum <= 0)
        {
            return 0;
        }

        double fraction = Math.Clamp(
            double.IsFinite(value) ? value : 0,
            0,
            Maximum) / Maximum;
        return fraction;
    }
}

public static class UsageTrendGeometry
{
    public static bool ShouldRefreshHover(
        int? currentIndex,
        int nextIndex,
        bool isVisible) =>
        !isVisible || currentIndex != nextIndex;

    public static IReadOnlyList<double> SelectTicksForHeight(
        IReadOnlyList<double> ticks,
        double height)
    {
        ArgumentNullException.ThrowIfNull(ticks);
        if (ticks.Count <= 2 || height >= 80)
        {
            return ticks;
        }

        return [ticks[0], ticks[^1]];
    }

    public static UsageTrendScale CreateScale(double peak, int targetTickCount = 4)
    {
        if (!double.IsFinite(peak) || peak <= 0 || targetTickCount <= 0)
        {
            return new UsageTrendScale(0, [0]);
        }

        double rawStep = peak / targetTickCount;
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(rawStep)));
        double normalized = rawStep / magnitude;
        double step = (normalized > 5 ? 10 : normalized > 2 ? 5 : normalized > 1 ? 2 : 1)
            * magnitude;
        double maximum = Math.Ceiling(peak / step) * step;
        var ticks = new List<double>();
        for (double value = 0; value <= maximum + (step * 0.000001); value += step)
        {
            ticks.Add(value);
        }

        return new UsageTrendScale(maximum, ticks);
    }

    public static UsageTrendPath CreatePath(
        IReadOnlyList<double> values,
        double width,
        double height,
        double maximum,
        double topPadding = 8,
        double bottomPadding = 0,
        ReportChartStyle style = ReportChartStyle.Smooth)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (!double.IsFinite(width) || width <= 0
            || !double.IsFinite(height) || height <= 0)
        {
            return new UsageTrendPath([], []);
        }

        double baseline = Math.Max(0, height - Math.Max(0, bottomPadding));
        double usableHeight = Math.Max(0, baseline - Math.Max(0, topPadding));
        double step = values.Count <= 1 ? 0 : width / (values.Count - 1);
        UsageTrendPoint[] points = values
            .Select((value, index) => new UsageTrendPoint(
                values.Count == 1 ? width / 2 : index * step,
                !double.IsFinite(value) ? double.NaN : maximum <= 0
                    ? baseline
                    : baseline - (Math.Clamp(value, 0, maximum) / maximum * usableHeight)))
            .ToArray();
        if (points.Length < 2)
        {
            return new UsageTrendPath(points, []);
        }

        if (style is ReportChartStyle.Line or ReportChartStyle.Area)
        {
            return new UsageTrendPath(points, points.Zip(points.Skip(1),
                (from, to) => new UsageTrendSegment(from, from, to, to)).ToArray());
        }
        if (style == ReportChartStyle.Step)
        {
            var steps = points.Zip(points.Skip(1), (from, to) =>
            {
                var corner = new UsageTrendPoint(to.X, from.Y);
                return new[] { new UsageTrendSegment(from, from, corner, corner),
                    new UsageTrendSegment(corner, corner, to, to) };
            }).SelectMany(segments => segments).ToArray();
            return new UsageTrendPath(points, steps);
        }

        double[] tangents = CreateMonotoneTangents(points);
        var segments = new UsageTrendSegment[points.Length - 1];
        for (int index = 0; index < segments.Length; index++)
        {
            UsageTrendPoint from = points[index];
            UsageTrendPoint to = points[index + 1];
            double deltaX = to.X - from.X;
            segments[index] = new UsageTrendSegment(
                from,
                new UsageTrendPoint(
                    from.X + (deltaX / 3),
                    from.Y + ((tangents[index] * deltaX) / 3)),
                new UsageTrendPoint(
                    to.X - (deltaX / 3),
                    to.Y - ((tangents[index + 1] * deltaX) / 3)),
                to);
        }

        return new UsageTrendPath(points, segments);
    }

    private static double[] CreateMonotoneTangents(UsageTrendPoint[] points)
    {
        var slopes = new double[points.Length - 1];
        for (int index = 0; index < slopes.Length; index++)
        {
            double deltaX = points[index + 1].X - points[index].X;
            slopes[index] = deltaX == 0 || !double.IsFinite(points[index].Y) || !double.IsFinite(points[index + 1].Y)
                ? 0
                : (points[index + 1].Y - points[index].Y) / deltaX;
        }

        var tangents = new double[points.Length];
        tangents[0] = slopes[0];
        tangents[^1] = slopes[^1];
        for (int index = 1; index < points.Length - 1; index++)
        {
            double previous = slopes[index - 1];
            double next = slopes[index];
            tangents[index] = previous * next <= 0 ? 0 : (previous + next) / 2;
        }

        for (int index = 0; index < slopes.Length; index++)
        {
            double slope = slopes[index];
            if (slope == 0)
            {
                tangents[index] = 0;
                tangents[index + 1] = 0;
                continue;
            }

            double a = tangents[index] / slope;
            double b = tangents[index + 1] / slope;
            double magnitude = (a * a) + (b * b);
            if (magnitude <= 9)
            {
                continue;
            }

            double scale = 3 / Math.Sqrt(magnitude);
            tangents[index] = scale * a * slope;
            tangents[index + 1] = scale * b * slope;
        }

        return tangents;
    }
}

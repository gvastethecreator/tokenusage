using TokenUsage.Core.Appearance;

namespace TokenUsage.App.Controls;

public enum UsageTrendPointKind
{
    Unobserved,
    Measured,
    Unavailable,
}

public enum UsageTrendSpanKind
{
    Observed,
    Unobserved,
    Unavailable,
}

public readonly record struct UsageTrendPoint(double X, double Y);

public readonly record struct UsageTrendFade(double StartX, double DimStartX, double DimEndX, double EndX)
{
    public const double DimOpacity = 70d / 255;

    public double OpacityAt(double x)
    {
        if (x < DimStartX && DimStartX > StartX)
            return 1 - (1 - DimOpacity) * Math.Clamp((x - StartX) / (DimStartX - StartX), 0, 1);
        if (x > DimEndX && EndX > DimEndX)
            return DimOpacity + (1 - DimOpacity) * Math.Clamp((x - DimEndX) / (EndX - DimEndX), 0, 1);
        return DimOpacity;
    }
}

public readonly record struct UsageTrendSegment(
    UsageTrendPoint From,
    UsageTrendPoint Control1,
    UsageTrendPoint Control2,
    UsageTrendPoint To,
    UsageTrendSpanKind SpanKind = UsageTrendSpanKind.Observed,
    bool IsTrailingContinuation = false,
    UsageTrendFade? Fade = null);

public sealed record UsageTrendPath(
    IReadOnlyList<UsageTrendPoint> Points,
    IReadOnlyList<UsageTrendSegment> Segments);

public readonly record struct UsageTrendScale(
    double Maximum,
    IReadOnlyList<double> Ticks,
    bool EmphasizeSmallValues = false)
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
        return EmphasizeSmallValues ? Math.Sqrt(fraction) : fraction;
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

    public static UsageTrendPointKind ClassifyValue(double value) =>
        !double.IsFinite(value)
            ? UsageTrendPointKind.Unavailable
            : value > 0 ? UsageTrendPointKind.Measured : UsageTrendPointKind.Unobserved;

    public static UsageTrendPointKind KindAt(
        IReadOnlyList<double> values,
        IReadOnlyList<UsageTrendPointKind>? kinds,
        int index)
    {
        ArgumentNullException.ThrowIfNull(values);
        if ((uint)index >= (uint)values.Count)
        {
            return UsageTrendPointKind.Unobserved;
        }

        return kinds is { Count: > 0 } && index < kinds.Count
            ? kinds[index]
            : ClassifyValue(values[index]);
    }

    public static UsageTrendScale CreateScale(double peak, int targetTickCount = 4, bool emphasizeSmallValues = false)
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

        return emphasizeSmallValues
            ? new UsageTrendScale(maximum, Enumerable.Range(0, 5).Select(index => maximum * Math.Pow(index / 4d, 2)).ToArray(), true)
            : new UsageTrendScale(maximum, ticks);
    }

    public static IReadOnlyList<double> ContinueMeasuredValues(
        IReadOnlyList<double> values,
        IReadOnlyList<UsageTrendPointKind>? kinds)
    {
        ArgumentNullException.ThrowIfNull(values);
        var continued = new double[values.Count];
        double last = 0;
        bool hasMeasured = false;
        for (int index = 0; index < values.Count; index++)
        {
            if (KindAt(values, kinds, index) == UsageTrendPointKind.Measured
                && double.IsFinite(values[index]))
            {
                last = Math.Max(0, values[index]);
                hasMeasured = true;
                continued[index] = last;
            }
            else
            {
                continued[index] = hasMeasured ? last : 0;
            }
        }

        return continued;
    }

    public static UsageTrendPath CreatePath(
        IReadOnlyList<double> values,
        double width,
        double height,
        double maximum,
        double topPadding = 8,
        double bottomPadding = 0,
        ReportChartStyle style = ReportChartStyle.Smooth,
        bool emphasizeSmallValues = false,
        IReadOnlyList<UsageTrendPointKind>? pointKinds = null,
        bool includeCarriedVertices = false)
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
                    : baseline - (new UsageTrendScale(maximum, [], emphasizeSmallValues).Normalize(value) * usableHeight)))
            .ToArray();
        UsageTrendPointKind[] kinds = ResolveKinds(values, pointKinds);
        DrawnVertex[] drawn = CreateDrawnVertices(values, points, kinds, includeCarriedVertices);
        if (drawn.Length < 2)
        {
            return new UsageTrendPath(points, []);
        }

        UsageTrendPoint[] vertices = drawn.Select(item => item.Point).ToArray();
        UsageTrendSegment Segment(DrawnVertex from, DrawnVertex to, UsageTrendPoint control1, UsageTrendPoint control2)
        {
            UsageTrendSpanKind span = SpanKindBetween(from, to, kinds);
            return new(from.Point, control1, control2, to.Point, span, to.IsTrailing,
                span == UsageTrendSpanKind.Observed ? null : FadeBetween(from, to, points, kinds));
        }

        if (style is ReportChartStyle.Line or ReportChartStyle.Area)
        {
            return new UsageTrendPath(points, drawn.Zip(drawn.Skip(1),
                (from, to) => Segment(from, to, from.Point, to.Point)).ToArray());
        }
        if (style == ReportChartStyle.Step)
        {
            var steps = drawn.Zip(drawn.Skip(1), (from, to) =>
            {
                var corner = new UsageTrendPoint(to.Point.X, from.Point.Y);
                UsageTrendSegment segment = Segment(from, to, from.Point, to.Point);
                return new[]
                {
                    segment with { Control1 = from.Point, Control2 = corner, To = corner },
                    segment with { From = corner, Control1 = corner, Control2 = to.Point },
                };
            }).SelectMany(segments => segments).ToArray();
            return new UsageTrendPath(points, steps);
        }

        double[] tangents = CreateMonotoneTangents(vertices);
        var segments = new UsageTrendSegment[drawn.Length - 1];
        for (int index = 0; index < segments.Length; index++)
        {
            DrawnVertex from = drawn[index];
            DrawnVertex to = drawn[index + 1];
            double deltaX = to.Point.X - from.Point.X;
            segments[index] = Segment(
                from,
                to,
                new UsageTrendPoint(
                    from.Point.X + (deltaX / 3),
                    from.Point.Y + ((tangents[index] * deltaX) / 3)),
                new UsageTrendPoint(
                    to.Point.X - (deltaX / 3),
                    to.Point.Y - ((tangents[index + 1] * deltaX) / 3)));
        }

        return new UsageTrendPath(points, segments);
    }

    public static bool IsAccounted(double value) => double.IsFinite(value) && value > 0;

    private readonly record struct DrawnVertex(UsageTrendPoint Point, int Index, bool IsTrailing);

    private static UsageTrendFade FadeBetween(
        DrawnVertex from, DrawnVertex to, UsageTrendPoint[] points, UsageTrendPointKind[] kinds)
    {
        int start = from.Index;
        while (start > 0 && kinds[start] != UsageTrendPointKind.Measured) start--;
        int end = to.Index;
        while (end < kinds.Length - 1 && kinds[end] != UsageTrendPointKind.Measured) end++;
        int lastAbsent = kinds[end] == UsageTrendPointKind.Measured ? end - 1 : end;
        return new(points[start].X, points[Math.Min(start + 1, end)].X,
            points[lastAbsent].X, points[end].X);
    }

    private static UsageTrendPointKind[] ResolveKinds(
        IReadOnlyList<double> values,
        IReadOnlyList<UsageTrendPointKind>? pointKinds)
    {
        var kinds = new UsageTrendPointKind[values.Count];
        for (int index = 0; index < values.Count; index++)
        {
            kinds[index] = KindAt(values, pointKinds, index);
        }

        return kinds;
    }

    private static DrawnVertex[] CreateDrawnVertices(
        IReadOnlyList<double> values,
        UsageTrendPoint[] points,
        UsageTrendPointKind[] kinds,
        bool includeCarriedVertices)
    {
        var drawn = new List<DrawnVertex>();
        if (includeCarriedVertices)
        {
            int firstMeasured = Array.IndexOf(kinds, UsageTrendPointKind.Measured);
            if (firstMeasured < 0)
            {
                return [];
            }

            for (int index = firstMeasured; index < values.Count; index++)
            {
                if (!double.IsFinite(points[index].Y))
                {
                    continue;
                }

                bool trailing = kinds[index] != UsageTrendPointKind.Measured
                    && !HasMeasuredAfter(kinds, index);
                drawn.Add(new DrawnVertex(points[index], index, trailing));
            }

            return [.. drawn];
        }

        for (int index = 0; index < values.Count; index++)
        {
            if (kinds[index] == UsageTrendPointKind.Measured)
            {
                drawn.Add(new DrawnVertex(points[index], index, false));
            }
        }

        if (drawn.Count == 0)
        {
            return [];
        }

        if (kinds[^1] != UsageTrendPointKind.Measured && drawn[^1].Point.X < points[^1].X)
        {
            drawn.Add(new DrawnVertex(
                new UsageTrendPoint(points[^1].X, drawn[^1].Point.Y),
                values.Count - 1,
                true));
        }

        return [.. drawn];
    }

    private static bool HasMeasuredAfter(UsageTrendPointKind[] kinds, int index)
    {
        for (int next = index + 1; next < kinds.Length; next++)
        {
            if (kinds[next] == UsageTrendPointKind.Measured)
            {
                return true;
            }
        }

        return false;
    }

    private static UsageTrendSpanKind SpanKindBetween(
        DrawnVertex from,
        DrawnVertex to,
        UsageTrendPointKind[] kinds)
    {
        if (to.IsTrailing)
        {
            return UsageTrendSpanKind.Unobserved;
        }

        UsageTrendPointKind fromKind = kinds[from.Index];
        UsageTrendPointKind toKind = kinds[to.Index];
        if (fromKind == UsageTrendPointKind.Unobserved || toKind == UsageTrendPointKind.Unobserved)
        {
            return UsageTrendSpanKind.Unobserved;
        }

        if (fromKind == UsageTrendPointKind.Unavailable || toKind == UsageTrendPointKind.Unavailable)
        {
            return UsageTrendSpanKind.Unavailable;
        }

        UsageTrendSpanKind kind = UsageTrendSpanKind.Observed;
        int start = Math.Min(from.Index, to.Index);
        int end = Math.Max(from.Index, to.Index);
        for (int index = start + 1; index < end; index++)
        {
            if (kinds[index] == UsageTrendPointKind.Unobserved)
            {
                return UsageTrendSpanKind.Unobserved;
            }

            if (kinds[index] == UsageTrendPointKind.Unavailable)
            {
                kind = UsageTrendSpanKind.Unavailable;
            }
        }

        return kind;
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

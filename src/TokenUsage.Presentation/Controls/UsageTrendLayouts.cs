using TokenUsage.Core.Appearance;

namespace TokenUsage.App.Controls;

public readonly record struct UsageTrendBar(int SeriesIndex, int DayIndex, double X, double Y, double Width, double Height);
public readonly record struct UsageTrendBaselineStub(int DayIndex, double X, double Y, double Width, double Height);
public sealed record UsageTrendBand(IReadOnlyList<double> Lower, IReadOnlyList<double> Upper);

public static class UsageTrendLayouts
{
    public static int BarSlots(ReportChartStyle style, int days) =>
        days <= 0 ? 0 : days * (style == ReportChartStyle.TwoHourBars ? 12 : 1);

    public static double Peak(
        IReadOnlyList<IReadOnlyList<double>> series,
        bool stacked,
        IReadOnlyList<IReadOnlyList<UsageTrendPointKind>?>? kinds = null)
    {
        if (stacked && kinds is not null)
        {
            IReadOnlyList<double>[] continued = [.. series.Select((values, index) =>
                index < kinds.Count && kinds[index] is { Count: > 0 } seriesKinds
                    ? UsageTrendGeometry.ContinueMeasuredValues(values, seriesKinds)
                    : values)];
            return Peak(continued, stacked: true);
        }

        int count = series.Select(values => values.Count).DefaultIfEmpty(0).Max();
        return Enumerable.Range(0, count).Select(day => stacked
            ? series.Sum(values => day < values.Count && double.IsFinite(values[day]) ? Math.Max(0, values[day]) : 0)
            : series.Select(values => day < values.Count && double.IsFinite(values[day]) ? Math.Max(0, values[day]) : 0).DefaultIfEmpty(0).Max())
            .DefaultIfEmpty(0).Max();
    }

    public static IReadOnlyList<UsageTrendBar> Bars(IReadOnlyList<IReadOnlyList<double>> series,
        int days, double width, double height, double maximum, double top = 8, double bottom = 10,
        bool emphasizeSmallValues = false)
    {
        if (days <= 0 || series.Count == 0 || width <= 0 || height <= 0 || maximum <= 0) return [];
        double dayWidth = width / days;
        double groupWidth = dayWidth * 0.8;
        double barWidth = groupWidth * 0.88;
        var scale = new UsageTrendScale(maximum, [], emphasizeSmallValues);
        double baseline = height - bottom;
        double available = Math.Max(0, baseline - top);
        var bars = new List<UsageTrendBar>();
        for (int day = 0; day < days; day++)
        foreach (int index in Enumerable.Range(0, series.Count)
            .OrderByDescending(index => day < series[index].Count ? series[index][day] : 0))
        {
            double value = day < series[index].Count ? series[index][day] : 0;
            if (!double.IsFinite(value) || value <= 0) continue;
            double barHeight = scale.Normalize(value) * available;
            bars.Add(new(index, day, day * dayWidth + dayWidth * 0.1,
                baseline - barHeight, barWidth, barHeight));
        }
        return bars;
    }

    public static IReadOnlyList<UsageTrendBaselineStub> EmptyDayStubs(
        IReadOnlyList<IReadOnlyList<double>> series, int days, double width, double height,
        double top = 8, double bottom = 10)
    {
        if (days <= 0 || series.Count == 0 || width <= 0 || height <= 0) return [];
        double dayWidth = width / days;
        double barWidth = dayWidth * 0.8 * 0.88;
        double baseline = height - bottom;
        var stubs = new List<UsageTrendBaselineStub>();
        for (int day = 0; day < days; day++)
        {
            bool hasBar = false;
            bool hasZero = false;
            bool hasUnknown = false;
            foreach (IReadOnlyList<double> values in series)
            {
                if (day >= values.Count) continue;
                double value = values[day];
                if (!double.IsFinite(value)) hasUnknown = true;
                else if (value > 0) hasBar = true;
                else hasZero = true;
            }
            if (hasBar || (!hasZero && hasUnknown)) continue;
            stubs.Add(new(day, day * dayWidth + dayWidth * 0.1, baseline - 2, barWidth, 2));
        }
        return stubs;
    }

    public static IReadOnlyList<UsageTrendBand> Bands(
        IReadOnlyList<IReadOnlyList<double>> series,
        bool independent,
        IReadOnlyList<IReadOnlyList<UsageTrendPointKind>?>? kinds = null,
        bool percentage = false)
    {
        int days = series.Select(values => values.Count).DefaultIfEmpty(0).Max();
        bool[] measuredDays = percentage && !independent
            ? [.. Enumerable.Range(0, days).Select(day => series.Select((values, index) =>
                day < values.Count && UsageTrendGeometry.KindAt(values,
                    kinds is not null && index < kinds.Count ? kinds[index] : null, day)
                    == UsageTrendPointKind.Measured).Any(measured => measured))]
            : [];
        var cumulative = new double[days];
        var bands = new List<UsageTrendBand>();
        for (int seriesIndex = 0; seriesIndex < series.Count; seriesIndex++)
        {
            IReadOnlyList<double> values = series[seriesIndex];
            IReadOnlyList<UsageTrendPointKind>? seriesKinds = kinds is not null && seriesIndex < kinds.Count
                ? kinds[seriesIndex]
                : null;
            bool continueSeries = !independent && seriesKinds is { Count: > 0 };
            IReadOnlyList<double> source = continueSeries
                ? UsageTrendGeometry.ContinueMeasuredValues(values, seriesKinds)
                : values;
            double[] lower = independent ? new double[days] : (double[])cumulative.Clone();
            double[] upper = new double[days];
            double lastShare = 0;
            for (int day = 0; day < days; day++)
            {
                double value = day < source.Count ? source[day] : 0;
                if (percentage && !independent)
                {
                    // A measured day replaces the complete composition. Carry only
                    // across days with no measurements, never into another series' share.
                    if (measuredDays[day])
                        lastShare = day < values.Count
                            && UsageTrendGeometry.KindAt(values, seriesKinds, day) == UsageTrendPointKind.Measured
                            && double.IsFinite(values[day]) ? Math.Max(0, values[day]) : 0;
                    value = lastShare;
                }
                if (continueSeries)
                {
                    upper[day] = lower[day] + Math.Max(0, value);
                    cumulative[day] += Math.Max(0, value);
                }
                else
                {
                    upper[day] = double.IsFinite(value) ? lower[day] + Math.Max(0, value) : double.NaN;
                    if (double.IsFinite(value)) cumulative[day] += Math.Max(0, value);
                }
            }
            bands.Add(new(lower, upper));
        }
        return bands;
    }
}

namespace TokenUsage.App.Controls;

public readonly record struct UsageTrendBar(int SeriesIndex, int DayIndex, double X, double Y, double Width, double Height);
public sealed record UsageTrendBand(IReadOnlyList<double> Lower, IReadOnlyList<double> Upper);

public static class UsageTrendLayouts
{
    public static double Peak(IReadOnlyList<IReadOnlyList<double>> series, bool stacked)
    {
        int count = series.Select(values => values.Count).DefaultIfEmpty(0).Max();
        return Enumerable.Range(0, count).Select(day => stacked
            ? series.Sum(values => day < values.Count && double.IsFinite(values[day]) ? Math.Max(0, values[day]) : 0)
            : series.Select(values => day < values.Count && double.IsFinite(values[day]) ? Math.Max(0, values[day]) : 0).DefaultIfEmpty(0).Max())
            .DefaultIfEmpty(0).Max();
    }

    public static IReadOnlyList<UsageTrendBar> Bars(IReadOnlyList<IReadOnlyList<double>> series,
        int days, double width, double height, double maximum, double top = 8, double bottom = 10)
    {
        if (days <= 0 || series.Count == 0 || width <= 0 || height <= 0 || maximum <= 0) return [];
        double dayWidth = width / days;
        double groupWidth = dayWidth * 0.8;
        double slot = groupWidth / series.Count;
        double barWidth = slot * 0.88;
        double baseline = height - bottom;
        double available = Math.Max(0, baseline - top);
        var bars = new List<UsageTrendBar>();
        for (int day = 0; day < days; day++)
        for (int index = 0; index < series.Count; index++)
        {
            double value = day < series[index].Count ? series[index][day] : 0;
            if (!double.IsFinite(value) || value <= 0) continue;
            double barHeight = Math.Clamp(value / maximum, 0, 1) * available;
            bars.Add(new(index, day, day * dayWidth + dayWidth * 0.1 + index * slot,
                baseline - barHeight, barWidth, barHeight));
        }
        return bars;
    }

    public static IReadOnlyList<UsageTrendBand> Bands(IReadOnlyList<IReadOnlyList<double>> series, bool independent)
    {
        int days = series.Select(values => values.Count).DefaultIfEmpty(0).Max();
        var cumulative = new double[days];
        var bands = new List<UsageTrendBand>();
        foreach (var values in series)
        {
            double[] lower = independent ? new double[days] : (double[])cumulative.Clone();
            double[] upper = new double[days];
            for (int day = 0; day < days; day++)
            {
                double value = day < values.Count ? values[day] : 0;
                upper[day] = double.IsFinite(value) ? lower[day] + Math.Max(0, value) : double.NaN;
                if (double.IsFinite(value)) cumulative[day] += Math.Max(0, value);
            }
            bands.Add(new(lower, upper));
        }
        return bands;
    }
}

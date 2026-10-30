namespace TokenUsage.App.Controls;

public readonly record struct UsageTrendBar(int SeriesIndex, int DayIndex, double X, double Y, double Width, double Height, bool IsTop = true);
public readonly record struct UsageTrendBaselineStub(int DayIndex, double X, double Y, double Width, double Height);

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

    /// <summary>
    /// Stacked bars add every series into one day total, so the bar height is the day and a
    /// percentage day reaches 100%. Comparison charts keep overlaid bars: summing two periods
    /// would invent a total that never happened.
    /// </summary>
    public static IReadOnlyList<UsageTrendBar> Bars(IReadOnlyList<IReadOnlyList<double>> series,
        int days, double width, double height, double maximum, double top = 8, double bottom = 10,
        bool emphasizeSmallValues = false, bool stacked = false)
    {
        if (days <= 0 || series.Count == 0 || width <= 0 || height <= 0 || maximum <= 0) return [];
        double dayWidth = width / days;
        double groupWidth = dayWidth * 0.8;
        double barWidth = groupWidth * 0.88;
        var scale = new UsageTrendScale(maximum, [], emphasizeSmallValues);
        double baseline = height - bottom;
        double available = Math.Max(0, baseline - top);
        var bars = new List<UsageTrendBar>();
        double ValueAt(int index, int day) => day < series[index].Count ? series[index][day] : 0;
        for (int day = 0; day < days; day++)
        {
            double x = day * dayWidth + dayWidth * 0.1;
            if (stacked)
            {
                int topIndex = Enumerable.Range(0, series.Count)
                    .LastOrDefault(index => double.IsFinite(ValueAt(index, day)) && ValueAt(index, day) > 0, -1);
                double cumulative = 0;
                for (int index = 0; index < series.Count; index++)
                {
                    double value = ValueAt(index, day);
                    if (!double.IsFinite(value) || value <= 0) continue;
                    double lower = scale.Normalize(cumulative) * available;
                    cumulative += value;
                    double upper = scale.Normalize(cumulative) * available;
                    bars.Add(new(index, day, x, baseline - upper, barWidth, upper - lower, index == topIndex));
                }

                continue;
            }

            foreach (int index in Enumerable.Range(0, series.Count).OrderByDescending(index => ValueAt(index, day)))
            {
                double value = ValueAt(index, day);
                if (!double.IsFinite(value) || value <= 0) continue;
                double barHeight = scale.Normalize(value) * available;
                bars.Add(new(index, day, x, baseline - barHeight, barWidth, barHeight));
            }
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
}

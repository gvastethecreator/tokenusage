using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using TokenUsage.App.ViewModels;
using TokenUsage.App.ViewModels.Reports;
using Windows.Foundation;
using Windows.UI;

namespace TokenUsage.App.Controls;

/// <summary>Shared drawing for the report charts: redraw on data, size, and theme changes.</summary>
public abstract class ReportChart : Canvas
{
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data), typeof(object), typeof(ReportChart), new PropertyMetadata(null, (d, _) => ((ReportChart)d).Redraw()));

    protected ReportChart()
    {
        SizeChanged += (_, _) => Redraw();
        ActualThemeChanged += (_, _) => Redraw();
    }

    public object? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    private bool Dark => ActualTheme == ElementTheme.Dark;
    protected Color Text => Dark ? Color.FromArgb(255, 242, 242, 242) : Color.FromArgb(255, 17, 17, 17);
    protected Color Muted => Dark ? Color.FromArgb(255, 156, 156, 156) : Color.FromArgb(255, 107, 107, 107);
    protected Color Faint => Dark ? Color.FromArgb(255, 70, 70, 70) : Color.FromArgb(255, 200, 200, 200);
    protected Color GridLine => Dark ? Color.FromArgb(26, 255, 255, 255) : Color.FromArgb(18, 0, 0, 0);
    protected Color Warning => Dark ? Color.FromArgb(255, 227, 169, 58) : Color.FromArgb(255, 138, 90, 0);
    protected Color Critical => Dark ? Color.FromArgb(255, 240, 128, 143) : Color.FromArgb(255, 176, 42, 66);

    protected Color ToneColor(ReportTone tone) => tone switch
    {
        ReportTone.Critical => Critical,
        ReportTone.Warning or ReportTone.Up => Warning,
        _ => Muted,
    };

    private void Redraw()
    {
        Children.Clear();
        if (Data is not null && ActualWidth > 20 && ActualHeight > 20) Draw(ActualWidth, ActualHeight);
    }

    protected abstract void Draw(double width, double height);

    protected void Line(double x1, double y1, double x2, double y2, Color color, double thickness = 1, bool dashed = false) =>
        Children.Add(new Line
        {
            X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = new SolidColorBrush(color), StrokeThickness = thickness,
            StrokeDashArray = dashed ? [4, 3] : null, IsHitTestVisible = false,
        });

    protected void Polyline(IEnumerable<Point> points, Color color, double thickness)
    {
        var line = new Polyline
        {
            Stroke = new SolidColorBrush(color), StrokeThickness = thickness, StrokeLineJoin = PenLineJoin.Round,
            IsHitTestVisible = false,
        };
        foreach (Point point in points) line.Points.Add(point);
        Children.Add(line);
    }

    protected void Box(double x, double y, double width, double height, Color color, double radius = 2)
    {
        var box = new Rectangle
        {
            Width = Math.Max(0, width), Height = Math.Max(0, height), Fill = new SolidColorBrush(color),
            RadiusX = radius, RadiusY = radius, IsHitTestVisible = false,
        };
        SetLeft(box, x);
        SetTop(box, y);
        Children.Add(box);
    }

    protected void Label(string text, double x, double y, Color color, TextAlignment align = TextAlignment.Left, double width = 80)
    {
        var block = new TextBlock
        {
            Text = text, FontSize = 11, Foreground = new SolidColorBrush(color), Width = width,
            TextAlignment = align, TextTrimming = TextTrimming.CharacterEllipsis, IsHitTestVisible = false,
        };
        SetLeft(block, align switch { TextAlignment.Center => x - width / 2, TextAlignment.Right => x - width, _ => x });
        SetTop(block, y);
        Children.Add(block);
    }

    protected static Color Hex(string hex) => Color.FromArgb(255,
        Convert.ToByte(hex.Substring(1, 2), 16), Convert.ToByte(hex.Substring(3, 2), 16), Convert.ToByte(hex.Substring(5, 2), 16));
}

/// <summary>Used quota across the cycle: earlier cycles, the current one, and its projection to the reset.</summary>
public sealed class PaceChart : ReportChart
{
    protected override void Draw(double width, double height)
    {
        if (Data is not ReportPace pace) return;
        const double left = 34, top = 6, bottom = 18;
        double X(double fraction) => left + fraction * (width - left - 4);
        double Y(double used) => height - bottom - Math.Clamp(used, 0, 100) / 100 * (height - bottom - top);
        foreach (double mark in new[] { 0d, 50, 100 })
        {
            Line(left, Y(mark), width - 4, Y(mark), mark == 100 ? Faint : GridLine, 1, mark == 100);
            Label(mark.ToString("0'%'", System.Globalization.CultureInfo.CurrentCulture), left - 6, Y(mark) - 8, Muted, TextAlignment.Right, 32);
        }
        foreach (ReportCurve curve in pace.Previous)
        foreach (ReportPoint[] run in Runs(curve.Points))
            Polyline(run.Select(point => new Point(X(point.Elapsed), Y(point.Used))), Faint, 1.5);
        (double elapsed, double used) = (pace.Current.Points[^1].Elapsed, pace.Current.Points[^1].Used);
        if (pace.ProjectedAtReset is double projected)
        {
            // Stop where the quota runs out instead of drawing past 100%.
            double end = projected > 100 ? elapsed * 100 / used : 1;
            Line(X(elapsed), Y(used), X(end), Y(Math.Min(projected, 100)), ToneColor(pace.Tone), 2, dashed: true);
            if (projected > 100) Dot(X(end), Y(100), ToneColor(pace.Tone));
        }
        foreach (ReportPoint[] run in Runs(pace.Current.Points))
            Polyline(run.Select(point => new Point(X(point.Elapsed), Y(point.Used))), Text, 2.5);
        Dot(X(elapsed), Y(used), Text);
    }

    // A drop in used quota is a reset or an account switch inside the window: start a new line
    // there rather than drawing usage going backwards.
    private static IEnumerable<ReportPoint[]> Runs(IReadOnlyList<ReportPoint> points)
    {
        var run = new List<ReportPoint>();
        foreach (ReportPoint point in points)
        {
            if (run.Count > 0 && point.Used < run[^1].Used - 5)
            {
                yield return run.ToArray();
                run.Clear();
            }
            run.Add(point);
        }
        if (run.Count > 0) yield return run.ToArray();
    }

    private void Dot(double x, double y, Color color)
    {
        var dot = new Ellipse { Width = 8, Height = 8, Fill = new SolidColorBrush(color), IsHitTestVisible = false };
        SetLeft(dot, x - 4);
        SetTop(dot, y - 4);
        Children.Add(dot);
    }
}

/// <summary>Tokens per day stacked by agent, with the previous period's day totals as a dashed line.</summary>
public sealed class DailyTokensChart : ReportChart
{
    protected override void Draw(double width, double height)
    {
        if (Data is not ReportDaily daily || daily.Days.Count == 0) return;
        const double left = 44, top = 8, bottom = 20;
        double max = Math.Max(1, daily.Days.Max(day => Math.Max(day.Total, day.PreviousTotal))) * 1.1;
        double slot = (width - left) / daily.Days.Count, bar = Math.Max(2, slot * 0.7);
        double Y(double value) => height - bottom - value / max * (height - bottom - top);
        for (int step = 0; step <= 3; step++)
        {
            double value = max / 1.1 * step / 3;
            Line(left, Y(value), width, Y(value), GridLine);
            Label(UsageValueFormatter.CompactTokens(value), left - 6, Y(value) - 8, Muted, TextAlignment.Right, 40);
        }
        Color[] colors = daily.Series.Select(series => Hex(series.ColorHex)).ToArray();
        int labelEvery = Math.Max(1, daily.Days.Count / 6);
        for (int index = 0; index < daily.Days.Count; index++)
        {
            ReportDay day = daily.Days[index];
            double x = left + index * slot + (slot - bar) / 2, baseline = 0;
            for (int series = 0; series < day.Tokens.Count; series++)
            {
                long value = day.Tokens[series];
                if (value <= 0) continue;
                Box(x, Y(baseline + value), bar, Y(baseline) - Y(baseline + value), colors[series], 1);
                baseline += value;
            }
            if (index % labelEvery == 0)
                Label(day.Date.ToString("d MMM", System.Globalization.CultureInfo.CurrentCulture),
                    left + index * slot + slot / 2, height - bottom + 4, Muted, TextAlignment.Center, 60);
        }
        Points(daily, left, slot, Y);
    }

    private void Points(ReportDaily daily, double left, double slot, Func<double, double> y)
    {
        var line = new Polyline
        {
            Stroke = new SolidColorBrush(Muted), StrokeThickness = 1.5, StrokeDashArray = [4, 3], IsHitTestVisible = false,
        };
        for (int index = 0; index < daily.Days.Count; index++)
            line.Points.Add(new Point(left + index * slot + slot / 2, y(daily.Days[index].PreviousTotal)));
        Children.Add(line);
    }
}

/// <summary>From the previous total to the current one, one bar per agent and model that changed most.</summary>
public sealed class WaterfallChart : ReportChart
{
    protected override void Draw(double width, double height)
    {
        if (Data is not IReadOnlyList<ReportStep> steps || steps.Count < 2) return;
        const double top = 18, bottom = 34;
        double running = 0, high = 0;
        var spans = new List<(double From, double To)>();
        foreach (ReportStep step in steps)
        {
            (double from, double to) = step.IsTotal ? (0d, (double)step.Tokens) : (running, running + step.Tokens);
            running = step.IsTotal ? step.Tokens : to;
            spans.Add((from, to));
            high = Math.Max(high, Math.Max(from, to));
        }
        // Totals start at a shared floor so the changes stay visible next to large totals.
        double floor = Math.Max(0, spans.Where((_, i) => !steps[i].IsTotal).Select(span => Math.Min(span.From, span.To))
            .DefaultIfEmpty(0).Min() * 0.85);
        double Y(double value) => height - bottom - (Math.Max(value, floor) - floor) / Math.Max(1, high - floor) * (height - bottom - top);
        double slot = width / steps.Count, bar = slot * 0.6;
        for (int index = 0; index < steps.Count; index++)
        {
            ReportStep step = steps[index];
            (double from, double to) = spans[index];
            double x = index * slot + (slot - bar) / 2, upper = Y(Math.Max(from, to)), lower = Y(Math.Min(from, to));
            Color color = step.IsTotal ? Faint : step.Tokens > 0 ? Warning : Muted;
            Box(x, upper, bar, Math.Max(2, lower - upper), color);
            string value = step.IsTotal ? UsageValueFormatter.CompactTokens(step.Tokens)
                : (step.Tokens > 0 ? "+" : "−") + UsageValueFormatter.CompactTokens(Math.Abs(step.Tokens));
            Label(value, x + bar / 2, upper - 16, Text, TextAlignment.Center, slot);
            Label(step.Label, x + bar / 2, height - bottom + 4, Muted, TextAlignment.Center, slot);
            if (step.Detail.Length > 0) Label(step.Detail, x + bar / 2, height - bottom + 17, Muted, TextAlignment.Center, slot);
        }
    }
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using TokenUsage.App.ViewModels.Reports;
using TokenUsage.Core.Appearance;
using Windows.Foundation;
using Windows.UI;
using XamlPath = Microsoft.UI.Xaml.Shapes.Path;

namespace TokenUsage.App.Controls;

public sealed partial class UsageTrendChart
{
    private void RenderSeries(UsageReportTrendDataset data, double width, double height, UsageTrendScale scale)
    {
        IReadOnlyList<double>[] values = data.Series.Select(series => series.Values).ToArray();
        if (data.Style == ReportChartStyle.Bars || data.Days.Count == 1)
        {
            foreach (UsageTrendBar item in UsageTrendLayouts.Bars(values, data.Days.Count, width, height, scale.Maximum))
            {
                var bar = new Rectangle
                {
                    Width = item.Width, Height = item.Height,
                    Fill = SeriesBrush(data.Series[item.SeriesIndex]),
                    Stroke = _accessibilitySettings.HighContrast ? TextBrushProxy.Background : null,
                    StrokeThickness = _accessibilitySettings.HighContrast ? 1 : 0,
                    IsHitTestVisible = false,
                };
                Canvas.SetLeft(bar, item.X);
                Canvas.SetTop(bar, item.Y);
                PlotCanvas.Children.Add(bar);
            }
            return;
        }

        if (data.Style == ReportChartStyle.Area)
        {
            var bands = UsageTrendLayouts.Bands(values, data.IsComparison);
            for (int index = 0; index < bands.Count; index++)
            {
                UsageTrendPath upper = Path(bands[index].Upper, ReportChartStyle.Line);
                UsageTrendPath lower = Path(bands[index].Lower, ReportChartStyle.Line);
                var geometry = new PathGeometry();
                // A missing price leaves a gap, never a fabricated zero-cost area.
                int from = 0;
                while (from < upper.Points.Count)
                {
                    while (from < upper.Points.Count && !double.IsFinite(upper.Points[from].Y)) from++;
                    int to = from;
                    while (to + 1 < upper.Points.Count && double.IsFinite(upper.Points[to + 1].Y)) to++;
                    if (from >= upper.Points.Count) break;
                    var figure = new PathFigure { StartPoint = ToPoint(upper.Points[from]), IsClosed = true, IsFilled = true };
                    for (int point = from + 1; point <= to; point++)
                        figure.Segments.Add(new LineSegment { Point = ToPoint(upper.Points[point]) });
                    for (int point = to; point >= from; point--)
                        figure.Segments.Add(new LineSegment { Point = ToPoint(lower.Points[point]) });
                    geometry.Figures.Add(figure);
                    from = to + 1;
                }
                PlotCanvas.Children.Add(new XamlPath
                {
                    Data = geometry, Fill = SeriesBrush(data.Series[index]),
                    Opacity = data.IsComparison ? 0.25 : 0.72, IsHitTestVisible = false,
                });
                AddLine(upper, data.Series[index], index);
            }
            return;
        }

        for (int index = 0; index < data.Series.Count; index++)
            AddLine(Path(values[index], data.Style), data.Series[index], index);

        UsageTrendPath Path(IReadOnlyList<double> source, ReportChartStyle style) =>
            UsageTrendGeometry.CreatePath(source, width, height, scale.Maximum, TopPadding, BottomPadding, style);
    }

    private Brush SeriesBrush(UsageReportTrendSeries series) => _accessibilitySettings.HighContrast
        ? TextBrushProxy.Background : new SolidColorBrush(ProviderColorPalette.Parse(series.ColorHex));

    private void AddLine(UsageTrendPath path, UsageReportTrendSeries series, int seriesIndex)
    {
        var geometry = new PathGeometry();
        PathFigure? figure = null;
        UsageTrendPoint? last = null;
        foreach (UsageTrendSegment segment in path.Segments)
        {
            if (!double.IsFinite(segment.From.Y) || !double.IsFinite(segment.To.Y))
            {
                figure = null;
                last = null;
                continue;
            }
            if (figure is null || last != segment.From)
            {
                figure = new PathFigure { StartPoint = ToPoint(segment.From), IsClosed = false, IsFilled = false };
                geometry.Figures.Add(figure);
            }
            figure.Segments.Add(new BezierSegment
            {
                Point1 = ToPoint(segment.Control1), Point2 = ToPoint(segment.Control2), Point3 = ToPoint(segment.To),
            });
            last = segment.To;
        }

        var line = new XamlPath
        {
            Data = geometry, Stroke = SeriesBrush(series), StrokeThickness = 2,
            StrokeLineJoin = PenLineJoin.Round, IsHitTestVisible = false,
        };
        if (_accessibilitySettings.HighContrast && seriesIndex > 0)
            line.StrokeDashArray = seriesIndex % 2 == 0 ? [2, 3] : [6, 3];
        PlotCanvas.Children.Add(line);
        for (int index = 0; index < path.Points.Count; index++)
        {
            UsageTrendPoint point = path.Points[index];
            if (!double.IsFinite(point.Y)) continue;
            bool isolated = (index == 0 || !double.IsFinite(path.Points[index - 1].Y))
                && (index == path.Points.Count - 1 || !double.IsFinite(path.Points[index + 1].Y));
            if (!isolated) continue;
            var marker = new Ellipse { Width = 4, Height = 4, Fill = SeriesBrush(series), IsHitTestVisible = false };
            Canvas.SetLeft(marker, point.X - 2);
            Canvas.SetTop(marker, point.Y - 2);
            PlotCanvas.Children.Add(marker);
        }
    }

    private static Point ToPoint(UsageTrendPoint point) => new(point.X, point.Y);

    private void BuildLegend(UsageReportTrendDataset data)
    {
        LegendContent.Children.Clear();
        LegendContent.Visibility = data.Series.Any(series => series.ModelId is not null)
            ? Visibility.Visible : Visibility.Collapsed;
        if (LegendContent.Visibility != Visibility.Visible) return;
        foreach (var series in data.Series)
        {
            double total = series.Values.Any(double.IsFinite)
                ? series.Values.Where(double.IsFinite).Sum() : double.NaN;
            // No-data zeros cannot turn an unknown model price into a known zero.
            if (series.Values.Any(double.IsNaN) && total == 0) total = double.NaN;
            string value = data.Metric == UsageReportMetric.Share
                ? "" : FormatValue(total, data.Metric);
            LegendContent.Children.Add(CreateHoverRow(series, value));
        }
    }
}

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
        IReadOnlyList<double>[] values = data.Series.Select(series => data.Style == ReportChartStyle.TwoHourBars ? series.TimeValues : series.Values).ToArray();
        if (data.Style is ReportChartStyle.Bars or ReportChartStyle.TwoHourBars || data.Days.Count == 1)
        {
            foreach (UsageTrendBar item in UsageTrendLayouts.Bars(values, data.Days.Count * (data.Style == ReportChartStyle.TwoHourBars ? 12 : 1),
                width, height, scale.Maximum, top: IsPreview ? 2 : TopPadding, bottom: IsPreview ? 2 : BottomPadding, emphasizeSmallValues: scale.EmphasizeSmallValues))
            {
                var bar = new Border
                {
                    Width = item.Width, Height = item.Height,
                    Background = IsPreview ? SeriesBrush(data.Series[item.SeriesIndex]) : BarBrush(data.Series[item.SeriesIndex]),
                    CornerRadius = IsPreview ? new CornerRadius(1, 1, 0, 0) : new CornerRadius(4, 4, 0, 0),
                    BorderBrush = _accessibilitySettings.HighContrast ? TextBrushProxy.Background : null,
                    BorderThickness = new Thickness(_accessibilitySettings.HighContrast ? 1 : 0),
                    UseLayoutRounding = false,
                    IsHitTestVisible = false,
                };
                Canvas.SetLeft(bar, item.X);
                Canvas.SetTop(bar, item.Y);
                _seriesCanvas.Children.Add(bar);
            }
            if (!IsPreview)
            {
                IReadOnlyList<double>[] stubSeries = data.Style == ReportChartStyle.TwoHourBars
                    ? data.Series.Select(series => series.Values).ToArray()
                    : values;
                foreach (UsageTrendBaselineStub stub in UsageTrendLayouts.EmptyDayStubs(
                    stubSeries, data.Days.Count, width, height, top: TopPadding, bottom: BottomPadding))
                {
                    var hairline = new Border
                    {
                        Width = stub.Width,
                        Height = stub.Height,
                        Background = TextBrushProxy.Background,
                        Opacity = 0.35,
                        IsHitTestVisible = false,
                    };
                    Canvas.SetLeft(hairline, stub.X);
                    Canvas.SetTop(hairline, stub.Y);
                    _seriesCanvas.Children.Add(hairline);
                }
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
                _seriesCanvas.Children.Add(new XamlPath
                {
                    Data = geometry, Fill = AreaBrush(data.Series[index]), UseLayoutRounding = false,
                    Opacity = data.IsComparison ? 0.25 : 0.72, IsHitTestVisible = false,
                });
                AddLine(upper, data.Series[index], index);
            }
            return;
        }

        for (int index = 0; index < data.Series.Count; index++)
            AddLine(Path(values[index], data.Style), data.Series[index], index,
                fillToBaseline: !IsPreview && data.Series.Count == 1 ? height - BottomPadding : null);

        UsageTrendPath Path(IReadOnlyList<double> source, ReportChartStyle style) =>
            UsageTrendGeometry.CreatePath(source, width, height, scale.Maximum, IsPreview ? 2 : TopPadding, IsPreview ? 2 : BottomPadding, style, scale.EmphasizeSmallValues);
    }

    private static UsageReportResetKind[] ResetKinds(UsageReportTrendDataset data) =>
        data.Days.SelectMany(day => day.Resets).Select(reset => reset.Kind).Distinct().Order().ToArray();

    private void RenderResetMarkers(UsageReportTrendDataset data, double width)
    {
        var packed = UsageReportResetMarkers.PackDays(data.Days);
        foreach (UsageReportResetMark mark in packed)
        {
            double x = data.Style is ReportChartStyle.Bars or ReportChartStyle.TwoHourBars || data.Days.Count == 1
                ? (mark.DayIndex + 0.5) * width / data.Days.Count
                : mark.DayIndex * width / Math.Max(1, data.Days.Count - 1);
            Shape marker = CreateResetSymbol(mark.Kind);
            Canvas.SetLeft(marker, Math.Clamp(x - 5, 0, Math.Max(0, width - 10)));
            Canvas.SetTop(marker, UsageReportResetMarkers.RailTop(mark.StackIndex));
            PlotCanvas.Children.Add(marker);
        }
    }

    private Brush ResetBrush(UsageReportResetKind kind) => kind switch
    {
        UsageReportResetKind.Weekly => WeeklyResetBrushProxy.Background,
        UsageReportResetKind.Session => SessionResetBrushProxy.Background,
        UsageReportResetKind.Manual => ManualResetBrushProxy.Background,
        UsageReportResetKind.ResetCredit => ResetCreditBrushProxy.Background,
        _ => TextBrushProxy.Background,
    };

    private Polygon CreateResetSymbol(UsageReportResetKind kind)
    {
        var symbol = new Polygon { Points = [new(0, 1), new(10, 1), new(5, 9)] };
        symbol.Width = 10;
        symbol.Height = 10;
        symbol.Fill = ResetBrush(kind);
        symbol.IsHitTestVisible = false;
        return symbol;
    }

    private void BuildResetLegend(UsageReportTrendDataset data)
    {
        var kinds = IsPreview ? [] : ResetKinds(data);
        ResetLegend.Visibility = kinds.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        ResetLegend.ItemsSource = kinds.Select(kind =>
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            Shape symbol = CreateResetSymbol(kind);
            symbol.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(symbol);
            row.Children.Add(new TextBlock { Text = GetString(UsageReportResetMarkers.LabelResourceKey(kind)),
                Foreground = TextBrushProxy.Background, FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
            return row;
        }).ToArray();
    }

    private Brush BarBrush(UsageReportTrendSeries series)
    {
        if (_accessibilitySettings.HighContrast) return SeriesBrush(series);
        Color color = ProviderColorPalette.Parse(series.ColorHex);
        return new LinearGradientBrush
        {
            StartPoint = new Point(0, 0), EndPoint = new Point(0, 1),
            GradientStops =
            {
                new GradientStop { Color = color, Offset = 0 },
                new GradientStop { Color = Color.FromArgb(255, (byte)(color.R * 0.78), (byte)(color.G * 0.78), (byte)(color.B * 0.78)), Offset = 1 },
            },
        };
    }

    private Brush AreaBrush(UsageReportTrendSeries series)
    {
        if (_accessibilitySettings.HighContrast) return SeriesBrush(series);
        Color color = ProviderColorPalette.Parse(series.ColorHex);
        return new LinearGradientBrush
        {
            StartPoint = new Point(0, 0), EndPoint = new Point(0, 1),
            GradientStops =
            {
                new GradientStop { Color = color, Offset = 0 },
                new GradientStop { Color = Color.FromArgb(45, color.R, color.G, color.B), Offset = 1 },
            },
        };
    }

    private Brush SeriesBrush(UsageReportTrendSeries series) => _accessibilitySettings.HighContrast
        ? TextBrushProxy.Background : new SolidColorBrush(ProviderColorPalette.Parse(series.ColorHex));

    private void AddLine(UsageTrendPath path, UsageReportTrendSeries series, int seriesIndex, double? fillToBaseline = null)
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

        if (fillToBaseline is double baseline && !_accessibilitySettings.HighContrast)
        {
            // Copy the exact curve into the fill: preserve gaps and never invent samples.
            var fillGeometry = new PathGeometry();
            foreach (var source in geometry.Figures)
            {
                var fill = new PathFigure { StartPoint = source.StartPoint, IsClosed = true, IsFilled = true };
                foreach (BezierSegment segment in source.Segments)
                    fill.Segments.Add(new BezierSegment { Point1 = segment.Point1, Point2 = segment.Point2, Point3 = segment.Point3 });
                var end = ((BezierSegment)source.Segments[^1]).Point3;
                fill.Segments.Add(new LineSegment { Point = new Point(end.X, baseline) });
                fill.Segments.Add(new LineSegment { Point = new Point(source.StartPoint.X, baseline) });
                fillGeometry.Figures.Add(fill);
            }
            _seriesCanvas.Children.Add(new XamlPath { Data = fillGeometry, Fill = AreaBrush(series), Opacity = 0.18,
                UseLayoutRounding = false, IsHitTestVisible = false });
        }

        var line = new XamlPath
        {
            Data = geometry, Stroke = SeriesBrush(series), StrokeThickness = IsPreview ? 2 : 2.25,
            StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round, UseLayoutRounding = false, IsHitTestVisible = false,
        };
        if (_accessibilitySettings.HighContrast && seriesIndex > 0)
            line.StrokeDashArray = seriesIndex % 2 == 0 ? [2, 3] : [6, 3];
        _seriesCanvas.Children.Add(line);
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
            _seriesCanvas.Children.Add(marker);
        }
    }

    private static Point ToPoint(UsageTrendPoint point) => new(point.X, point.Y);

    private void BuildLegend(UsageReportTrendDataset data)
    {
        var items = new List<Grid>();
        LegendContent.ItemsSource = items;
        LegendContent.Visibility = data.IsComparison || data.Series.Any(series => series.ModelId is not null)
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
            Grid row = CreateHoverRow(series, value);
            ((TextBlock)row.Children[2]).Text = series.LegendName ?? series.Name;
            items.Add(row);
        }
        LegendContent.ItemsSource = items.ToArray();
    }
}

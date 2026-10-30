using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using TokenUsage.App.ViewModels.Reports;
using Windows.Foundation;
using Windows.UI;
using XamlPath = Microsoft.UI.Xaml.Shapes.Path;

namespace TokenUsage.App.Controls;

public sealed partial class UsageTrendChart
{
    private void RenderSeries(UsageReportTrendDataset data, double width, double height, UsageTrendScale scale)
    {
        IReadOnlyList<double>[] values = data.Series.Select(series => series.Values).ToArray();
        // A single day has no line to draw, so it shows as a bar.
        if (data.Days.Count == 1)
        {
            foreach (UsageTrendBar item in UsageTrendLayouts.Bars(values, data.Days.Count,
                width, height, scale.Maximum, top: TopPadding, bottom: BottomPadding,
                emphasizeSmallValues: scale.EmphasizeSmallValues, stacked: !data.IsComparison))
            {
                // A one-pixel seam keeps adjacent stacked segments readable when two providers share a hue family.
                double seam = !item.IsTop && item.Height > 3 ? 1 : 0;
                var bar = new Border
                {
                    Width = item.Width, Height = item.Height - seam,
                    Background = BarBrush(data.Series[item.SeriesIndex]),
                    CornerRadius = !item.IsTop ? new CornerRadius(0) : new CornerRadius(4, 4, 0, 0),
                    BorderBrush = IsHighContrast ? TextBrushProxy.Background : null,
                    BorderThickness = new Thickness(IsHighContrast ? 1 : 0),
                    UseLayoutRounding = false,
                    IsHitTestVisible = false,
                };
                Canvas.SetLeft(bar, item.X);
                Canvas.SetTop(bar, item.Y + seam);
                BarColumn(item.DayIndex).Children.Add(bar);
            }
            foreach (UsageTrendBaselineStub stub in UsageTrendLayouts.EmptyDayStubs(
                values, data.Days.Count, width, height, top: TopPadding, bottom: BottomPadding))
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
            return;
        }

        for (int index = 0; index < data.Series.Count; index++)
            AddLine(UsageTrendGeometry.CreatePath(values[index], width, height, scale.Maximum, TopPadding, BottomPadding,
                    scale.EmphasizeSmallValues, data.Series[index].PointKinds),
                data.Series[index], index, fillToBaseline: data.Series.Count == 1 ? height - BottomPadding : null);
    }

    private static UsageReportResetKind[] ResetKinds(UsageReportTrendDataset data) =>
        data.Days.SelectMany(day => day.Resets).Select(reset => reset.Kind).Distinct().Order().ToArray();

    private void RenderResetMarkers(UsageReportTrendDataset data, double width)
    {
        var packed = UsageReportResetMarkers.PackDays(data.Days);
        foreach (UsageReportResetMark mark in packed)
        {
            double x = data.Days.Count == 1
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
        UsageReportResetKind.Scheduled => ScheduledResetBrushProxy.Background,
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
        var kinds = ResetKinds(data);
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
        if (IsHighContrast) return SeriesBrush(series);
        if (_fillBrushes.TryGetValue((series.ColorHex, false), out Brush? cached)) return cached;
        Color color = ProviderColorPalette.Parse(series.ColorHex);
        return _fillBrushes[(series.ColorHex, false)] = new LinearGradientBrush
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
        if (IsHighContrast) return SeriesBrush(series);
        if (_fillBrushes.TryGetValue((series.ColorHex, true), out Brush? cached)) return cached;
        Color color = ProviderColorPalette.Parse(series.ColorHex);
        return _fillBrushes[(series.ColorHex, true)] = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0), EndPoint = new Point(0, 1),
            GradientStops =
            {
                new GradientStop { Color = color, Offset = 0 },
                new GradientStop { Color = Color.FromArgb(45, color.R, color.G, color.B), Offset = 1 },
            },
        };
    }

    private Brush SeriesBrush(UsageReportTrendSeries series) => IsHighContrast
        ? TextBrushProxy.Background : new SolidColorBrush(ProviderColorPalette.Parse(series.ColorHex));

    private void AddLine(UsageTrendPath path, UsageReportTrendSeries series, int seriesIndex, double? fillToBaseline = null)
    {
        if (fillToBaseline is double baseline && !IsHighContrast)
        {
            foreach (IGrouping<(UsageTrendSpanKind Kind, UsageTrendFade? Fade), UsageTrendSegment> group in path.Segments
                .Where(segment => double.IsFinite(segment.From.Y) && double.IsFinite(segment.To.Y))
                .GroupBy(segment => (segment.SpanKind, segment.Fade)))
            {
                var fillGeometry = new PathGeometry();
                PathFigure? fill = null;
                UsageTrendPoint? last = null;
                foreach (UsageTrendSegment segment in group)
                {
                    if (fill is null || last != segment.From)
                    {
                        fill = new PathFigure { StartPoint = ToPoint(segment.From), IsClosed = true, IsFilled = true };
                        fillGeometry.Figures.Add(fill);
                    }
                    fill.Segments.Add(new BezierSegment
                    {
                        Point1 = ToPoint(segment.Control1),
                        Point2 = ToPoint(segment.Control2),
                        Point3 = ToPoint(segment.To),
                    });
                    last = segment.To;
                }

                foreach (PathFigure figure in fillGeometry.Figures)
                {
                    if (figure.Segments.Count == 0) continue;
                    var end = ((BezierSegment)figure.Segments[^1]).Point3;
                    figure.Segments.Add(new LineSegment { Point = new Point(end.X, baseline) });
                    figure.Segments.Add(new LineSegment { Point = new Point(figure.StartPoint.X, baseline) });
                }

                _seriesCanvas.Children.Add(new XamlPath
                {
                    Data = fillGeometry,
                    Fill = group.Key.Fade is null ? AreaBrush(series)
                        : FadeBrush(series, group.Key.Fade, group.Min(segment => segment.From.X), group.Max(segment => segment.To.X)),
                    Opacity = 0.18,
                    UseLayoutRounding = false,
                    IsHitTestVisible = false,
                });
            }
        }

        foreach (UsageTrendSegment segment in path.Segments)
        {
            if (!double.IsFinite(segment.From.Y) || !double.IsFinite(segment.To.Y)) continue;
            var figure = new PathFigure { StartPoint = ToPoint(segment.From), IsClosed = false, IsFilled = false };
            figure.Segments.Add(new BezierSegment
            {
                Point1 = ToPoint(segment.Control1),
                Point2 = ToPoint(segment.Control2),
                Point3 = ToPoint(segment.To),
            });
            var line = new XamlPath
            {
                Data = new PathGeometry { Figures = { figure } },
                Stroke = FadeBrush(series, segment.Fade, segment.From.X, segment.To.X),
                StrokeThickness = 2.25,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                UseLayoutRounding = false,
                IsHitTestVisible = false,
            };
            if (IsHighContrast && (segment.SpanKind != UsageTrendSpanKind.Observed || seriesIndex > 0))
            {
                line.StrokeDashArray = segment.SpanKind == UsageTrendSpanKind.Observed
                    ? (seriesIndex % 2 == 0 ? [2, 3] : [6, 3])
                    : [4, 4];
            }
            _seriesCanvas.Children.Add(line);
        }

        for (int index = 0; index < path.Points.Count; index++)
        {
            UsageTrendPoint point = path.Points[index];
            if (!double.IsFinite(point.Y)) continue;
            if (UsageTrendGeometry.KindAt(series.Values, series.PointKinds, index)
                != UsageTrendPointKind.Measured)
            {
                continue;
            }
            bool isolated = (index == 0 || UsageTrendGeometry.KindAt(series.Values, series.PointKinds, index - 1)
                    != UsageTrendPointKind.Measured)
                && (index == path.Points.Count - 1 || UsageTrendGeometry.KindAt(series.Values, series.PointKinds, index + 1)
                    != UsageTrendPointKind.Measured);
            if (!isolated) continue;
            var marker = new Ellipse { Width = 4, Height = 4, Fill = SeriesBrush(series), IsHitTestVisible = false };
            Canvas.SetLeft(marker, point.X - 2);
            Canvas.SetTop(marker, point.Y - 2);
            _seriesCanvas.Children.Add(marker);
        }
    }

    private Brush FadeBrush(UsageReportTrendSeries series, UsageTrendFade? fade, double fromX, double toX)
    {
        Brush solid = SeriesBrush(series);
        if (fade is not UsageTrendFade run || IsHighContrast)
            return solid;
        Color color = ProviderColorPalette.Parse(series.ColorHex);
        Color At(double x) => Color.FromArgb((byte)Math.Round(color.A * run.OpacityAt(x)), color.R, color.G, color.B);
        if (toX <= fromX) return new SolidColorBrush(At(fromX));
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0),
        };
        foreach (double x in new[] { fromX, run.DimStartX, run.DimEndX, toX }
            .Where(x => x >= fromX && x <= toX).Distinct().Order())
            brush.GradientStops.Add(new GradientStop { Color = At(x), Offset = (x - fromX) / (toX - fromX) });

        return brush;
    }

    private static Point ToPoint(UsageTrendPoint point) => new(point.X, point.Y);

    private void BuildLegend(UsageReportTrendDataset data)
    {
        bool spans = HasUnobservedSpans(data);
        SpanNote.Text = spans ? GetString("UsageReportChartUnobservedLegend") : string.Empty;
        SpanNote.Visibility = spans ? Visibility.Visible : Visibility.Collapsed;
        var items = new List<Grid>();
        LegendContent.Visibility = data.IsComparison || data.Series.Any(series => series.ModelId is not null)
            ? Visibility.Visible : Visibility.Collapsed;
        if (LegendContent.Visibility != Visibility.Visible)
        {
            LegendContent.ItemsSource = items;
            return;
        }
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

    private static bool HasUnobservedSpans(UsageReportTrendDataset data) =>
        data.Series.Any(series => series.PointKinds.Any(kind => kind == UsageTrendPointKind.Unobserved)
            || (series.PointKinds.Count == 0 && series.Values.Any(value => UsageTrendGeometry.ClassifyValue(value) == UsageTrendPointKind.Unobserved)));
}

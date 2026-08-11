using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.Windows.ApplicationModel.Resources;
using Windows.Foundation;
using Windows.System;
using Windows.UI;
using Windows.UI.ViewManagement;
using TokenUsage.App.ViewModels.Reports;
using XamlPath = Microsoft.UI.Xaml.Shapes.Path;

namespace TokenUsage.App.Controls;

public sealed partial class UsageTrendChart : UserControl
{
    private const double TopPadding = 8;
    private readonly ResourceLoader _resources = new();
    private readonly AccessibilitySettings _accessibilitySettings = new();
    private Line? _crosshair;
    private int? _hoverIndex;

    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data),
        typeof(UsageReportTrendDataset),
        typeof(UsageTrendChart),
        new PropertyMetadata(UsageReportTrendDataset.Empty, OnDataChanged));

    public static readonly DependencyProperty PlotHeightProperty = DependencyProperty.Register(
        nameof(PlotHeight),
        typeof(double),
        typeof(UsageTrendChart),
        new PropertyMetadata(260d));

    public UsageTrendChart()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        ActualThemeChanged += OnActualThemeChanged;
        GotFocus += OnGotFocus;
        AutomationProperties.SetName(this, GetString("UsageReportChartAutomationName"));
    }

    public UsageReportTrendDataset Data
    {
        get => (UsageReportTrendDataset)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public double PlotHeight
    {
        get => (double)GetValue(PlotHeightProperty);
        set => SetValue(PlotHeightProperty, value);
    }

    internal void DismissHover() => HideHover();

    private static void OnDataChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args) =>
        ((UsageTrendChart)dependencyObject).Rebuild();

    private void OnLoaded(object sender, RoutedEventArgs e) => Rebuild();

    private void OnActualThemeChanged(FrameworkElement sender, object args) => Rebuild();

    private void OnPlotSizeChanged(object sender, SizeChangedEventArgs e) => Rebuild();

    private void Rebuild()
    {
        double width = PlotCanvas.ActualWidth;
        double height = PlotCanvas.ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        PlotCanvas.Children.Clear();
        YAxisCanvas.Children.Clear();
        _crosshair = null;
        HoverCard.Visibility = Visibility.Collapsed;

        UsageReportTrendDataset data = Data ?? UsageReportTrendDataset.Empty;
        bool hasSeries = data.Days.Count > 0 && data.Series.Count > 0;
        EmptyText.Visibility = hasSeries ? Visibility.Collapsed : Visibility.Visible;
        UpdateDateLabels(data);
        if (!hasSeries)
        {
            return;
        }

        double peak = data.Series
            .SelectMany(series => series.Values)
            .DefaultIfEmpty(0)
            .Max();
        UsageTrendScale scale = data.Metric == UsageReportMetric.Share
            ? new UsageTrendScale(100, [0, 25, 50, 75, 100])
            : UsageTrendGeometry.CreateScale(peak);
        Brush gridBrush = GridBrushProxy.Background;
        Brush textBrush = TextBrushProxy.Background;

        foreach (double tick in UsageTrendGeometry.SelectTicksForHeight(scale.Ticks, height))
        {
            double y = scale.Maximum == 0
                ? height
                : height - (tick / scale.Maximum * (height - TopPadding));
            PlotCanvas.Children.Add(new Line
            {
                X1 = 0,
                X2 = width,
                Y1 = y,
                Y2 = y,
                Stroke = gridBrush,
                StrokeThickness = 1,
                IsHitTestVisible = false,
            });

            var label = new TextBlock
            {
                Text = FormatValue(tick, data.Metric),
                Foreground = textBrush,
                FontSize = 11,
                IsHitTestVisible = false,
            };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(label, Math.Max(0, 52 - label.DesiredSize.Width));
            Canvas.SetTop(label, Math.Clamp(y - (label.DesiredSize.Height / 2), 0, height - 18));
            YAxisCanvas.Children.Add(label);
        }

        var visuals = data.Series
            .Select((series, index) => CreateSeriesVisual(series, index, width, height, scale.Maximum))
            .ToArray();
        foreach (SeriesVisual visual in visuals)
        {
            PlotCanvas.Children.Add(visual.Area);
        }
        foreach (SeriesVisual visual in visuals)
        {
            PlotCanvas.Children.Add(visual.Line);
        }

        _crosshair = new Line
        {
            Y1 = TopPadding,
            Y2 = height,
            Stroke = textBrush,
            StrokeThickness = 1,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };
        PlotCanvas.Children.Add(_crosshair);
    }

    private SeriesVisual CreateSeriesVisual(
        UsageReportTrendSeries series,
        int seriesIndex,
        double width,
        double height,
        double maximum)
    {
        UsageTrendPath path = UsageTrendGeometry.CreatePath(
            series.Values,
            width,
            height,
            maximum,
            TopPadding);
        Color color = ProviderColorPalette.Parse(series.ColorHex);
        bool highContrast = _accessibilitySettings.HighContrast;
        Brush stroke = highContrast
            ? TextBrushProxy.Background
            : new SolidColorBrush(color);
        Brush fill = highContrast
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(18, 255, 255, 255))
            : CreateAreaFill(color);

        var line = new XamlPath
        {
            Data = CreateLineGeometry(path),
            Fill = null,
            Stroke = stroke,
            StrokeThickness = 2,
            StrokeLineJoin = PenLineJoin.Round,
            IsHitTestVisible = false,
        };
        if (highContrast && seriesIndex > 0)
        {
            line.StrokeDashArray = seriesIndex % 2 == 0 ? [2, 3] : [6, 3];
        }

        return new SeriesVisual(
            new XamlPath
            {
                Data = CreateAreaGeometry(path, height),
                Fill = fill,
                IsHitTestVisible = false,
            },
            line);
    }

    private static LinearGradientBrush CreateAreaFill(Color color)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
        };
        brush.GradientStops.Add(new GradientStop
        {
            Color = Windows.UI.Color.FromArgb(58, color.R, color.G, color.B),
            Offset = 0,
        });
        brush.GradientStops.Add(new GradientStop
        {
            Color = Windows.UI.Color.FromArgb(18, color.R, color.G, color.B),
            Offset = 0.58,
        });
        brush.GradientStops.Add(new GradientStop
        {
            Color = Windows.UI.Color.FromArgb(0, color.R, color.G, color.B),
            Offset = 1,
        });
        return brush;
    }

    private static PathGeometry CreateLineGeometry(UsageTrendPath path)
    {
        var geometry = new PathGeometry();
        UsageTrendPoint? first = path.Points.Count == 0 ? null : path.Points[0];
        if (first is null)
        {
            return geometry;
        }

        var figure = new PathFigure
        {
            StartPoint = ToPoint(first.Value),
            IsClosed = false,
            IsFilled = false,
        };
        AppendSegments(figure, path);
        geometry.Figures.Add(figure);
        return geometry;
    }

    private static PathGeometry CreateAreaGeometry(UsageTrendPath path, double height)
    {
        var geometry = new PathGeometry();
        if (path.Points.Count == 0)
        {
            return geometry;
        }

        UsageTrendPoint first = path.Points[0];
        UsageTrendPoint last = path.Points[^1];
        var figure = new PathFigure
        {
            StartPoint = new Point(first.X, height),
            IsClosed = true,
            IsFilled = true,
        };
        figure.Segments.Add(new LineSegment { Point = ToPoint(first) });
        AppendSegments(figure, path);
        figure.Segments.Add(new LineSegment { Point = new Point(last.X, height) });
        geometry.Figures.Add(figure);
        return geometry;
    }

    private static void AppendSegments(PathFigure figure, UsageTrendPath path)
    {
        if (path.Segments.Count == 0 && path.Points.Count == 1)
        {
            figure.Segments.Add(new LineSegment { Point = ToPoint(path.Points[0]) });
            return;
        }

        foreach (UsageTrendSegment segment in path.Segments)
        {
            figure.Segments.Add(new BezierSegment
            {
                Point1 = ToPoint(segment.Control1),
                Point2 = ToPoint(segment.Control2),
                Point3 = ToPoint(segment.To),
            });
        }
    }

    private static Point ToPoint(UsageTrendPoint point) => new(point.X, point.Y);

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (Data.Days.Count == 0 || PlotCanvas.ActualWidth <= 0)
        {
            return;
        }

        double x = e.GetCurrentPoint(PlotCanvas).Position.X;
        double fraction = Math.Clamp(x / PlotCanvas.ActualWidth, 0, 1);
        int index = (int)Math.Round(fraction * (Data.Days.Count - 1));
        ShowHover(index);
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e) => HideHover();

    private void OnGotFocus(object sender, RoutedEventArgs e)
    {
        if (Data.Days.Count > 0)
        {
            ShowHover(_hoverIndex ?? Data.Days.Count - 1);
        }
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (Data.Days.Count == 0)
        {
            return;
        }

        int current = _hoverIndex ?? Data.Days.Count - 1;
        int next = e.Key switch
        {
            VirtualKey.Left => Math.Max(0, current - 1),
            VirtualKey.Right => Math.Min(Data.Days.Count - 1, current + 1),
            VirtualKey.Home => 0,
            VirtualKey.End => Data.Days.Count - 1,
            _ => current,
        };
        if (next != current || e.Key is VirtualKey.Home or VirtualKey.End)
        {
            ShowHover(next);
            e.Handled = true;
        }
    }

    private void ShowHover(int index)
    {
        if (index < 0 || index >= Data.Days.Count || PlotCanvas.ActualWidth <= 0)
        {
            return;
        }
        if (!UsageTrendGeometry.ShouldRefreshHover(
            _hoverIndex,
            index,
            HoverCard.Visibility == Visibility.Visible))
        {
            return;
        }

        _hoverIndex = index;
        double x = Data.Days.Count <= 1
            ? PlotCanvas.ActualWidth / 2
            : index * PlotCanvas.ActualWidth / (Data.Days.Count - 1);
        if (_crosshair is not null)
        {
            _crosshair.X1 = x;
            _crosshair.X2 = x;
            _crosshair.Visibility = Visibility.Visible;
        }

        BuildHoverContent(index);
        double absoluteX = 62 + x;
        double cardX = absoluteX > ActualWidth * 0.62
            ? absoluteX - HoverCard.Width - 8
            : absoluteX + 8;
        HoverTransform.X = Math.Clamp(cardX, 0, Math.Max(0, ActualWidth - HoverCard.Width));
        HoverTransform.Y = 8;
        HoverCard.Visibility = Visibility.Visible;
    }

    private void BuildHoverContent(int index)
    {
        HoverContent.Children.Clear();
        HoverContent.Children.Add(new TextBlock
        {
            Text = Data.Days[index].Date.ToString("D", System.Globalization.CultureInfo.CurrentCulture),
            Foreground = TextBrushProxy.Background,
            FontSize = 12,
            TextWrapping = TextWrapping.WrapWholeWords,
        });

        double total = 0;
        foreach (UsageReportTrendSeries series in Data.Series)
        {
            double value = index < series.Values.Count ? series.Values[index] : 0;
            total += value;
            HoverContent.Children.Add(CreateHoverRow(series, FormatValue(value, Data.Metric)));
        }

        var separator = new Border
        {
            Height = 1,
            Background = GridBrushProxy.Background,
            Margin = new Thickness(0, 2, 0, 0),
        };
        HoverContent.Children.Add(separator);
        HoverContent.Children.Add(CreateHoverRow(
            new UsageReportTrendSeries(
                string.Empty,
                GetString("UsageReportChartTotal"),
                "#6B7280",
                []),
            FormatValue(total, Data.Metric),
            showMark: false));
    }

    private Grid CreateHoverRow(
        UsageReportTrendSeries series,
        string value,
        bool showMark = true)
    {
        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (showMark)
        {
            var mark = new ProviderMarkImage
            {
                ProviderId = series.ProviderId,
                Width = 14,
                Height = 14,
                VerticalAlignment = VerticalAlignment.Center,
            };
            row.Children.Add(mark);
        }

        var name = new TextBlock
        {
            Text = series.Name,
            Foreground = TextBrushProxy.Background,
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(name, 1);
        row.Children.Add(name);
        var amount = new TextBlock
        {
            Text = value,
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(amount, 2);
        row.Children.Add(amount);
        return row;
    }

    private void HideHover()
    {
        HoverCard.Visibility = Visibility.Collapsed;
        if (_crosshair is not null)
        {
            _crosshair.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateDateLabels(UsageReportTrendDataset data)
    {
        FirstDayLabel.Text = data.Days.Count == 0 ? string.Empty : data.Days[0].Label;
        MiddleDayLabel.Text = data.Days.Count == 0
            ? string.Empty
            : data.Days[data.Days.Count / 2].Label;
        LastDayLabel.Text = data.Days.Count == 0 ? string.Empty : data.Days[^1].Label;
    }

    private static string FormatValue(double value, UsageReportMetric metric) =>
        metric switch
        {
            UsageReportMetric.Cost => UsageReportViewModel.FormatCompactUsd(value),
            UsageReportMetric.Share => string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                "{0:0.#}%",
                value),
            _ => UsageReportViewModel.FormatCompactTokens(value),
        };

    private string GetString(string key)
    {
        string value = _resources.GetString(key);
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"The resource '{key}' is missing.")
            : value;
    }

    private sealed record SeriesVisual(XamlPath Area, XamlPath Line);
}

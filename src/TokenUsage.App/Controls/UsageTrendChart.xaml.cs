using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
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
    private const double BottomPadding = 10;
    private readonly ResourceLoader _resources = new();
    private readonly AccessibilitySettings _accessibilitySettings = new();
    private Line? _crosshair;
    private TranslateTransform? _crosshairTransform;
    private Storyboard? _hoverMotionStoryboard;
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

    public static readonly DependencyProperty YAxisWidthProperty = DependencyProperty.Register(
        nameof(YAxisWidth),
        typeof(GridLength),
        typeof(UsageTrendChart),
        new PropertyMetadata(new GridLength(54), OnAxisWidthChanged));

    public static readonly DependencyProperty YAxisGapProperty = DependencyProperty.Register(
        nameof(YAxisGap),
        typeof(GridLength),
        typeof(UsageTrendChart),
        new PropertyMetadata(new GridLength(8), OnAxisWidthChanged));

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

    public GridLength YAxisWidth
    {
        get => (GridLength)GetValue(YAxisWidthProperty);
        set => SetValue(YAxisWidthProperty, value);
    }

    public GridLength YAxisGap
    {
        get => (GridLength)GetValue(YAxisGapProperty);
        set => SetValue(YAxisGapProperty, value);
    }

    internal void DismissHover() => HideHover();

    private static void OnDataChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args) =>
        ((UsageTrendChart)dependencyObject).Rebuild();

    private static void OnAxisWidthChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        _ = args;
        ((UsageTrendChart)dependencyObject).Rebuild();
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => Rebuild();

    private void OnActualThemeChanged(FrameworkElement sender, object args) => Rebuild();

    private void OnPlotSizeChanged(object sender, SizeChangedEventArgs e) => Rebuild();

    private double GetAxisWidth() =>
        YAxisWidth.IsAbsolute ? YAxisWidth.Value : 54;

    private double GetAxisGap() =>
        YAxisGap.IsAbsolute ? YAxisGap.Value : 8;

    private void Rebuild()
    {
        StopHoverMotion();
        double width = PlotCanvas.ActualWidth;
        double height = PlotCanvas.ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        PlotCanvas.Children.Clear();
        YAxisCanvas.Children.Clear();
        PlotCanvas.Clip = new RectangleGeometry
        {
            Rect = new Rect(0, 0, width, height),
        };
        _crosshair = null;
        _crosshairTransform = null;
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
            double baseline = height - BottomPadding;
            double y = scale.Maximum == 0
                ? baseline
                : baseline - (tick / scale.Maximum * (baseline - TopPadding));
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
            double labelColumnWidth = YAxisCanvas.ActualWidth > 0
                ? YAxisCanvas.ActualWidth
                : GetAxisWidth();
            Canvas.SetLeft(
                label,
                Math.Max(0, labelColumnWidth - label.DesiredSize.Width));
            Canvas.SetTop(label, Math.Clamp(y - (label.DesiredSize.Height / 2), 0, height - 18));
            YAxisCanvas.Children.Add(label);
        }

        if (data.Days.Count == 1)
        {
            AddSingleDayBars(data, width, height, scale.Maximum);
        }
        else
        {
            var visuals = data.Series
                .Select((series, index) => CreateSeriesVisual(
                    series,
                    index,
                    width,
                    height,
                    scale.Maximum))
                .ToArray();
            foreach (SeriesVisual visual in visuals)
            {
                PlotCanvas.Children.Add(visual.Area);
            }
            foreach (SeriesVisual visual in visuals)
            {
                PlotCanvas.Children.Add(visual.Line);
            }
        }

        _crosshairTransform = new TranslateTransform();
        _crosshair = new Line
        {
            X1 = 0,
            X2 = 0,
            Y1 = TopPadding,
            Y2 = height - BottomPadding,
            RenderTransform = _crosshairTransform,
            Stroke = textBrush,
            StrokeThickness = 1,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };
        PlotCanvas.Children.Add(_crosshair);
    }

    private void AddSingleDayBars(
        UsageReportTrendDataset data,
        double width,
        double height,
        double maximum)
    {
        int count = data.Series.Count;
        double gap = count <= 4 ? 8 : 4;
        double barWidth = Math.Clamp(
            (Math.Min(width * 0.78, count * 48d) - (Math.Max(0, count - 1) * gap)) / count,
            6,
            44);
        double groupWidth = count * barWidth + Math.Max(0, count - 1) * gap;
        double groupLeft = Math.Max(0, (width - groupWidth) / 2);

        for (int index = 0; index < count; index++)
        {
            UsageReportTrendSeries series = data.Series[index];
            double value = series.Values.Count == 0 ? 0 : Math.Max(0, series.Values[0]);
            double baseline = height - BottomPadding;
            double availableHeight = Math.Max(0, baseline - TopPadding);
            double barHeight = maximum <= 0
                ? 0
                : value / maximum * availableHeight;
            if (value > 0)
            {
                barHeight = Math.Max(3, barHeight);
            }

            Color color = ProviderColorPalette.Parse(series.ColorHex);
            Brush fill = _accessibilitySettings.HighContrast
                ? TextBrushProxy.Background
                : new SolidColorBrush(color);
            var bar = new Rectangle
            {
                Width = barWidth,
                Height = barHeight,
                Fill = fill,
                RadiusX = 3,
                RadiusY = 3,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(bar, groupLeft + index * (barWidth + gap));
            Canvas.SetTop(bar, baseline - barHeight);
            PlotCanvas.Children.Add(bar);
        }
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
            TopPadding,
            BottomPadding);
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
                Data = CreateAreaGeometry(path, height - BottomPadding),
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

        bool animatePosition = HoverCard.Visibility == Visibility.Visible
            && _crosshair?.Visibility == Visibility.Visible;
        _hoverIndex = index;
        double x = Data.Days.Count <= 1
            ? PlotCanvas.ActualWidth / 2
            : index * PlotCanvas.ActualWidth / (Data.Days.Count - 1);
        if (_crosshair is not null)
        {
            _crosshair.Visibility = Visibility.Visible;
        }

        BuildHoverContent(index);
        double absoluteX = GetAxisWidth() + GetAxisGap() + x;
        double cardX = absoluteX > ActualWidth * 0.62
            ? absoluteX - HoverCard.Width - 8
            : absoluteX + 8;
        HoverTransform.Y = 8;
        HoverCard.Visibility = Visibility.Visible;
        MoveHoverVisuals(
            x,
            Math.Clamp(cardX, 0, Math.Max(0, ActualWidth - HoverCard.Width)),
            animatePosition);
    }

    private void MoveHoverVisuals(double crosshairX, double cardX, bool animate)
    {
        if (_crosshairTransform is null)
        {
            HoverTransform.X = cardX;
            return;
        }

        double currentCrosshairX = _crosshairTransform.X;
        double currentCardX = HoverTransform.X;
        StopHoverMotion();
        _crosshairTransform.X = currentCrosshairX;
        HoverTransform.X = currentCardX;
        if (!animate || !MotionSettings.AreAnimationsEnabled())
        {
            _crosshairTransform.X = crosshairX;
            HoverTransform.X = cardX;
            return;
        }

        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var crosshairAnimation = new DoubleAnimation
        {
            From = currentCrosshairX,
            To = crosshairX,
            Duration = MotionSettings.ChartHoverDuration,
            EasingFunction = easing,
        };
        Storyboard.SetTarget(crosshairAnimation, _crosshairTransform);
        Storyboard.SetTargetProperty(crosshairAnimation, nameof(TranslateTransform.X));

        var cardAnimation = new DoubleAnimation
        {
            From = currentCardX,
            To = cardX,
            Duration = MotionSettings.ChartHoverDuration,
            EasingFunction = easing,
        };
        Storyboard.SetTarget(cardAnimation, HoverTransform);
        Storyboard.SetTargetProperty(cardAnimation, nameof(TranslateTransform.X));

        var storyboard = new Storyboard();
        storyboard.Children.Add(crosshairAnimation);
        storyboard.Children.Add(cardAnimation);
        storyboard.Completed += (_, _) =>
        {
            if (!ReferenceEquals(_hoverMotionStoryboard, storyboard))
            {
                return;
            }

            storyboard.Stop();
            _crosshairTransform.X = crosshairX;
            HoverTransform.X = cardX;
            _hoverMotionStoryboard = null;
        };
        _hoverMotionStoryboard = storyboard;
        storyboard.Begin();
    }

    private void BuildHoverContent(int index)
    {
        HoverContent.Children.Clear();
        HoverContent.Children.Add(new TextBlock
        {
            Text = Data.Days[index].HoverText
                ?? Data.Days[index].Date.ToString(
                    "D",
                    System.Globalization.CultureInfo.CurrentCulture),
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
        StopHoverMotion();
        HoverCard.Visibility = Visibility.Collapsed;
        if (_crosshair is not null)
        {
            _crosshair.Visibility = Visibility.Collapsed;
        }
    }

    private void StopHoverMotion()
    {
        Storyboard? storyboard = _hoverMotionStoryboard;
        _hoverMotionStoryboard = null;
        storyboard?.Stop();
    }

    private void UpdateDateLabels(UsageReportTrendDataset data)
    {
        if (data.Days.Count == 0)
        {
            FirstDayLabel.Text = string.Empty;
            MiddleDayLabel.Text = string.Empty;
            LastDayLabel.Text = string.Empty;
            return;
        }

        if (data.Days.Count == 1)
        {
            FirstDayLabel.Text = string.Empty;
            MiddleDayLabel.Text = data.Days[0].Label;
            LastDayLabel.Text = string.Empty;
            return;
        }

        if (data.Days.Count == 2)
        {
            FirstDayLabel.Text = data.Days[0].Label;
            MiddleDayLabel.Text = string.Empty;
            LastDayLabel.Text = data.Days[1].Label;
            return;
        }

        FirstDayLabel.Text = data.Days[0].Label;
        MiddleDayLabel.Text = data.Days[data.Days.Count / 2].Label;
        LastDayLabel.Text = data.Days[^1].Label;
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

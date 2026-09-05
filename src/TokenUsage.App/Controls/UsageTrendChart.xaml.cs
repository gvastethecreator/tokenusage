using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using TokenUsage.Core.Appearance;
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


    public static readonly DependencyProperty IsPreviewProperty = DependencyProperty.Register(
        nameof(IsPreview), typeof(bool), typeof(UsageTrendChart),
        new PropertyMetadata(false, OnDataChanged));

    public bool IsPreview
    {
        get => (bool)GetValue(IsPreviewProperty);
        set => SetValue(IsPreviewProperty, value);
    }

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
        Unloaded += (_, _) => HideHover();
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
        HideHover();
        double width = PlotCanvas.ActualWidth;
        double height = PlotCanvas.ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        PlotCanvas.IsHitTestVisible = !IsPreview;
        DateLabels.Visibility = IsPreview ? Visibility.Collapsed : Visibility.Visible;
        PlotCanvas.Children.Clear();
        YAxisCanvas.Children.Clear();
        PlotCanvas.Clip = new RectangleGeometry
        {
            Rect = new Rect(0, 0, width, height),
        };
        _crosshair = null;
        HoverCard.Visibility = Visibility.Collapsed;

        UsageReportTrendDataset data = Data ?? UsageReportTrendDataset.Empty;
        bool hasSeries = data.Days.Count > 0 && data.Series.Count > 0;
        EmptyText.Visibility = hasSeries ? Visibility.Collapsed : Visibility.Visible;
        UpdateDateLabels(data);
        BuildLegend(data);
        if (!hasSeries)
        {
            return;
        }

        UsageTrendScale scale = data.Metric == UsageReportMetric.Share
            ? new UsageTrendScale(100, [0, 25, 50, 75, 100])
            : UsageTrendGeometry.CreateScale(UsageTrendLayouts.Peak(
                data.Series.Select(series => series.Values).ToArray(),
                data.Style == ReportChartStyle.Area && !data.IsComparison));
        Brush gridBrush = GridBrushProxy.Background;
        Brush textBrush = TextBrushProxy.Background;

        foreach (double tick in UsageTrendGeometry.SelectTicksForHeight(scale.Ticks, height))
        {
            double baseline = height - BottomPadding;
            double y = scale.Maximum == 0
                ? baseline
                : baseline - (scale.Normalize(tick) * (baseline - TopPadding));
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

            if (IsPreview) continue;
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

        RenderSeries(data, width, height, scale);

        if (IsPreview) return;
        _crosshair = new Line
        {
            X1 = 0,
            X2 = 0,
            Y1 = TopPadding,
            Y2 = height - BottomPadding,
            Stroke = textBrush,
            StrokeThickness = 1,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };
        PlotCanvas.Children.Add(_crosshair);
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

    private string FormatValue(double value, UsageReportMetric metric) =>
        metric switch
        {
            _ when !double.IsFinite(value) => GetString("UsageReportUnpricedLabel"),
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
}

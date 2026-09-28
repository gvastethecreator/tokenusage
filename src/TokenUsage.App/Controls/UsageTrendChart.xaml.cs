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
using TokenUsage.App.ViewModels;
using TokenUsage.App.ViewModels.Reports;
using XamlPath = Microsoft.UI.Xaml.Shapes.Path;

namespace TokenUsage.App.Controls;

public sealed partial class UsageTrendChart : UserControl
{
    // Room for the reset rail, computed once per rebuild; hover and marks read it many times.
    private double _topPadding = 8;
    private double TopPadding => _topPadding;
    private bool _rebuildPending;
    private (UsageReportTrendDataset Data, ElementTheme Theme, bool HighContrast)? _legendKey;
    private readonly Dictionary<(string Color, bool Area), Brush> _fillBrushes = [];
    private const double BottomPadding = 10;
    private readonly ResourceLoader _resources = new();
    private Windows.UI.ViewManagement.UISettings? _themeSettings;
    private readonly Windows.UI.ViewManagement.AccessibilitySettings _accessibilitySettings = new();
    private bool IsHighContrast => _accessibilitySettings.HighContrast;
    private Line? _crosshair;
    private Canvas _seriesCanvas = new();
    private Microsoft.UI.Composition.InsetClip? _entranceClip;
    private UsageReportTrendDataset? _lastAnimatedData;


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
        Unloaded += OnUnloaded;
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

    internal bool IsCaptureMode { get; set; }

    internal void DismissHover() { HideHover(); FinishEntrance(); }

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

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_themeSettings is null)
        {
            _themeSettings = new Windows.UI.ViewManagement.UISettings();
            _themeSettings.ColorValuesChanged += OnSystemThemeChanged;
        }
        Rebuild();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_themeSettings is not null)
        {
            _themeSettings.ColorValuesChanged -= OnSystemThemeChanged;
            _themeSettings = null;
        }
        HideHover();
        FinishEntrance();
    }

    private void OnSystemThemeChanged(Windows.UI.ViewManagement.UISettings sender, object args) =>
        _ = DispatcherQueue.TryEnqueue(() => { if (IsLoaded) Rebuild(); });

    private void OnActualThemeChanged(FrameworkElement sender, object args) => Rebuild();

    // A window drag raises several size changes per frame; draw once for the latest size.
    // Capture needs the chart drawn at its capture width before the bitmap is taken.
    private void OnPlotSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (IsCaptureMode)
        {
            Rebuild();
            return;
        }

        if (_rebuildPending) return;
        _rebuildPending = true;
        _ = DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            _rebuildPending = false;
            if (IsLoaded) Rebuild();
        });
    }

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

        ChartLayoutRoot.RowSpacing = IsPreview ? 0 : 6;
        PlotCanvas.IsHitTestVisible = !IsPreview;
        DateLabels.Visibility = IsPreview ? Visibility.Collapsed : Visibility.Visible;
        FinishEntrance();
        PlotCanvas.Children.Clear();
        YAxisCanvas.Children.Clear();
        PlotCanvas.Clip = new RectangleGeometry
        {
            Rect = new Rect(0, 0, width, height),
        };
        _crosshair = null;
        HoverCard.Visibility = Visibility.Collapsed;

        UsageReportTrendDataset data = Data ?? UsageReportTrendDataset.Empty;
        _topPadding = IsPreview ? 8 : UsageReportResetMarkers.TopPaddingFor(UsageReportResetMarkers.PackDays(data.Days));
        _fillBrushes.Clear();
        bool hasSeries = data.UnavailableText is null && data.Days.Count > 0 && data.Series.Count > 0;
        EmptyText.Text = data.UnavailableText ?? GetString("UsageTrendEmptyText");
        AutomationProperties.SetHelpText(this, data.UnavailableText ?? string.Empty);
        EmptyText.Visibility = hasSeries ? Visibility.Collapsed : Visibility.Visible;
        HoverCard.Width = data.IsComparison ? Math.Min(400, Math.Max(260, ActualWidth)) : 260;
        UpdateDateLabels(data);
        UpdateDateTicks(data, width);
        // Legends depend on the data and theme only; a resize keeps the rows it already has.
        var legendKey = (data, ActualTheme, IsHighContrast);
        if (_legendKey != legendKey)
        {
            _legendKey = legendKey;
            BuildLegend(data);
            BuildResetLegend(data);
        }
        if (!hasSeries)
        {
            return;
        }

        IReadOnlyList<double>[] scaleValues = [.. data.Series.Select(series =>
            data.Style == ReportChartStyle.TwoHourBars ? series.TimeValues : series.Values)];
        bool stackedArea = data.Style == ReportChartStyle.Area && data.Days.Count > 1 && !data.IsComparison;
        bool stackedBars = (data.Style is ReportChartStyle.Bars or ReportChartStyle.TwoHourBars || data.Days.Count == 1)
            && !data.IsComparison;
        UsageTrendScale scale = data.Metric == UsageReportMetric.Share
            ? new UsageTrendScale(100, [0, 25, 50, 75, 100])
            : UsageTrendGeometry.CreateScale(UsageTrendLayouts.Peak(
                scaleValues,
                stackedArea || stackedBars,
                stackedArea
                    ? [.. data.Series.Select(series => (IReadOnlyList<UsageTrendPointKind>?)series.PointKinds)]
                    : null),
                emphasizeSmallValues: data.EmphasizeSmallValues);
        Brush gridBrush = GridBrushProxy.Background;
        Brush textBrush = TextBrushProxy.Background;

        foreach (double tick in UsageTrendGeometry.SelectTicksForHeight(scale.Ticks, height))
        {
            double baseline = height - (IsPreview ? 2 : BottomPadding);
            double y = scale.Maximum == 0
                ? baseline
                : baseline - (scale.Normalize(tick) * (baseline - (IsPreview ? 2 : TopPadding)));
            PlotCanvas.Children.Add(new Line
            {
                X1 = 0,
                X2 = width,
                Y1 = y,
                Y2 = y,
                Stroke = gridBrush,
                StrokeThickness = 1,
                // Solid hairlines one step off the surface: a grid, not a threshold.
                Opacity = tick == 0 || IsHighContrast ? 1 : 0.45,
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

        _seriesCanvas = new Canvas { Width = width, Height = height, IsHitTestVisible = false };
        PlotCanvas.Children.Add(_seriesCanvas);
        ResetBarColumns(data);
        RenderSeries(data, width, height, scale);
        if (!IsPreview) RenderResetMarkers(data, width);
        RenderReferenceMarks(data, width, height, scale);
        bool animate = !IsPreview && !IsCaptureMode && !ReferenceEquals(_lastAnimatedData, data) && MotionSettings.AreAnimationsEnabled();
        if (animate && _barColumns.Count > 0)
        {
            StartColumnGrow(height - BottomPadding);
        }
        else if (animate)
        {
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(_seriesCanvas);
            _entranceClip = visual.Compositor.CreateInsetClip();
            visual.Clip = _entranceClip;
            var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
            animation.Duration = TimeSpan.FromMilliseconds(320);
            animation.InsertKeyFrame(0, (float)width);
            animation.InsertKeyFrame(1, 0, visual.Compositor.CreateCubicBezierEasingFunction(
                new System.Numerics.Vector2(0.16f, 1), new System.Numerics.Vector2(0.3f, 1)));
            _entranceClip.StartAnimation("RightInset", animation);
        }
        _lastAnimatedData = data;

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

    private void FinishEntrance()
    {
        FinishColumnGrow();
        if (_entranceClip is null) return;
        _entranceClip.StopAnimation("RightInset");
        _entranceClip.RightInset = 0;
        _entranceClip = null;
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
        MiddleDayLabel.Text = data.Days.Count < WeeklyTickMinimumDays ? data.Days[data.Days.Count / 2].Label : string.Empty;
        LastDayLabel.Text = data.Days[^1].Label;
    }

    private const int WeeklyTickMinimumDays = 10;

    // Longer ranges get a label every seven days, under its day, so the reader can find a week
    // without counting bars. Labels that would crowd the first or last date are skipped.
    private void UpdateDateTicks(UsageReportTrendDataset data, double width)
    {
        DateTicksCanvas.Children.Clear();
        if (IsPreview || data.Days.Count < WeeklyTickMinimumDays || width <= 0) return;
        bool slots = data.Style is ReportChartStyle.Bars or ReportChartStyle.TwoHourBars;
        const double edgeClearance = 64;
        for (int index = 7; index < data.Days.Count - 1; index += 7)
        {
            double x = slots ? (index + 0.5) * width / data.Days.Count : index * width / (data.Days.Count - 1);
            if (x < edgeClearance || x > width - edgeClearance) continue;
            var label = new TextBlock
            {
                Text = data.Days[index].Label,
                Foreground = TextBrushProxy.Background,
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Opacity = 0.85,
            };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(label, x - label.DesiredSize.Width / 2);
            DateTicksCanvas.Children.Add(label);
        }
    }

    private string FormatValue(
        double value,
        UsageReportMetric metric,
        UsageTrendPointKind? kind = null,
        bool exact = false) =>
        kind == UsageTrendPointKind.Unobserved
            ? GetString("UsageReportChartUnobserved")
            : metric switch
        {
            _ when kind == UsageTrendPointKind.Unavailable || !double.IsFinite(value) => GetString("UsageReportUnpricedLabel"),
            UsageReportMetric.Cost => exact
                ? UsageValueFormatter.DetailUsd(value)
                : UsageValueFormatter.AxisUsd(value),
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

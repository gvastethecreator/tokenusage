using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI.ViewManagement;
using TokenUsage.App.ViewModels.Dashboard;
using TokenUsage.App.ViewModels.Sample;
using XamlPath = Microsoft.UI.Xaml.Shapes.Path;

namespace TokenUsage.App.Controls;

public sealed class ProviderInvokedEventArgs : EventArgs
{
    public ProviderInvokedEventArgs(string providerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ProviderId = providerId;
    }

    public string ProviderId { get; }
}

public sealed partial class SpendDonutChart : UserControl
{
    private const double InnerRadiusRatio = 0.618;
    private const double GapWidth = 1.6;
    private const double RibbonCornerRadius = 4;

    private readonly List<ArcVisual> _arcVisuals = [];
    private readonly AccessibilitySettings _accessibilitySettings = new();
    private readonly Dictionary<string, string> _customColors = new(StringComparer.Ordinal);
    private SpendDonutArc[] _arcs = [];
    private SliceVisualState[] _sliceVisualState = [];
    private Storyboard? _storyboard;
    private bool _isLoaded;
    private int _lastRevealToken = int.MinValue;

    public static readonly DependencyProperty AccessibleNameProperty =
        DependencyProperty.Register(
            nameof(AccessibleName),
            typeof(string),
            typeof(SpendDonutChart),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty CenterValueProperty =
        DependencyProperty.Register(
            nameof(CenterValue),
            typeof(string),
            typeof(SpendDonutChart),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty CenterFontSizeProperty =
        DependencyProperty.Register(
            nameof(CenterFontSize),
            typeof(double),
            typeof(SpendDonutChart),
            new PropertyMetadata(12d));

    public static readonly DependencyProperty CenterMaxWidthProperty =
        DependencyProperty.Register(
            nameof(CenterMaxWidth),
            typeof(double),
            typeof(SpendDonutChart),
            new PropertyMetadata(78d));

    public static readonly DependencyProperty RevealProgressProperty =
        DependencyProperty.Register(
            nameof(RevealProgress),
            typeof(double),
            typeof(SpendDonutChart),
            new PropertyMetadata(0d, OnRevealProgressChanged));

    public static readonly DependencyProperty SlicesProperty =
        DependencyProperty.Register(
            nameof(Slices),
            typeof(IReadOnlyList<SpendSlice>),
            typeof(SpendDonutChart),
            new PropertyMetadata(null, OnSlicesChanged));

    public SpendDonutChart()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        ActualThemeChanged += OnActualThemeChanged;
    }

    public string AccessibleName
    {
        get => (string)GetValue(AccessibleNameProperty);
        set => SetValue(AccessibleNameProperty, value);
    }

    public string CenterValue
    {
        get => (string)GetValue(CenterValueProperty);
        set => SetValue(CenterValueProperty, value);
    }

    public double CenterFontSize
    {
        get => (double)GetValue(CenterFontSizeProperty);
        set => SetValue(CenterFontSizeProperty, value);
    }

    public double CenterMaxWidth
    {
        get => (double)GetValue(CenterMaxWidthProperty);
        set => SetValue(CenterMaxWidthProperty, value);
    }

    public double RevealProgress
    {
        get => (double)GetValue(RevealProgressProperty);
        set => SetValue(RevealProgressProperty, value);
    }

    public IReadOnlyList<SpendSlice>? Slices
    {
        get => (IReadOnlyList<SpendSlice>?)GetValue(SlicesProperty);
        set => SetValue(SlicesProperty, value);
    }

    public event EventHandler<ProviderInvokedEventArgs>? ProviderInvoked;

    public void PlayReveal(int token)
    {
        if (token == _lastRevealToken)
        {
            return;
        }

        _lastRevealToken = token;
        BeginReveal();
    }

    private void BeginReveal()
    {
        _storyboard?.Stop();

        if (!MotionSettings.AreAnimationsEnabled() || Slices is null || Slices.Count == 0)
        {
            RevealProgress = 1;
            return;
        }

        RevealProgress = 0;
        var animation = new DoubleAnimation
        {
            To = 1,
            Duration = MotionSettings.DonutRevealDuration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            EnableDependentAnimation = true,
        };

        Storyboard.SetTarget(animation, this);
        Storyboard.SetTargetProperty(animation, nameof(RevealProgress));

        _storyboard = new Storyboard();
        _storyboard.Children.Add(animation);
        _storyboard.Begin();
    }

    private static void OnRevealProgressChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args) =>
        ((SpendDonutChart)dependencyObject).UpdateArcVisuals();

    private static void OnSlicesChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        var chart = (SpendDonutChart)dependencyObject;
        SliceVisualState[] nextState = chart.Slices is null
            ? []
            : chart.Slices.Select(slice => new SliceVisualState(
                slice.ProviderId,
                slice.Amount,
                slice.ColorHex)).ToArray();
        if (nextState.SequenceEqual(chart._sliceVisualState))
        {
            return;
        }

        chart._sliceVisualState = nextState;
        chart._arcs = SpendDonutGeometry.CreateArcs(
            nextState.Select(slice =>
                new SpendDonutInput(slice.ProviderId, slice.Amount))).ToArray();
        chart._customColors.Clear();
        if (chart.Slices is not null)
        {
            foreach (SpendSlice slice in chart.Slices)
            {
                if (slice.ColorHex is not null)
                {
                    chart._customColors[slice.ProviderId] = slice.ColorHex;
                }
            }
        }
        chart.RebuildArcVisuals();
        chart.RevealProgress = MotionSettings.AreAnimationsEnabled() ? 0 : 1;
        chart.UpdateArcVisuals();
        if (chart._isLoaded)
        {
            _ = chart.DispatcherQueue.TryEnqueue(chart.BeginReveal);
        }
    }

    private void OnChartSizeChanged(object sender, SizeChangedEventArgs e) => UpdateArcVisuals();

    private void RebuildArcVisuals()
    {
        ArcCanvas.Children.Clear();
        _arcVisuals.Clear();

        foreach (SpendDonutArc arc in _arcs)
        {
            var path = new XamlPath
            {
                Visibility = Visibility.Collapsed,
            };
            var shadowPath = new XamlPath
            {
                IsHitTestVisible = false,
                RenderTransform = new TranslateTransform { Y = 0.75 },
                Visibility = Visibility.Collapsed,
            };
            if (!_accessibilitySettings.HighContrast
                && _customColors.TryGetValue(arc.ProviderId, out string? colorHex))
            {
                path.Fill = ProviderColorPalette.CreateGradient(colorHex);
            }
            else
            {
                path.SetBinding(
                    Shape.FillProperty,
                    new Binding
                    {
                        Source = ResolveBrushProxy(arc.ProviderId),
                        Path = new PropertyPath(nameof(Border.Background)),
                    });
            }
            shadowPath.SetBinding(
                Shape.FillProperty,
                new Binding
                {
                    Source = ShadowBrushProxy,
                    Path = new PropertyPath(nameof(Border.Background)),
                });
            shadowPath.SetBinding(
                UIElement.OpacityProperty,
                new Binding
                {
                    Source = ShadowBrushProxy,
                    Path = new PropertyPath(nameof(Opacity)),
                });
            AutomationProperties.SetAccessibilityView(shadowPath, AccessibilityView.Raw);
            SpendSlice? slice = Slices?.FirstOrDefault(candidate => string.Equals(
                candidate.ProviderId,
                arc.ProviderId,
                StringComparison.Ordinal));
            if (slice is not null)
            {
                double total = Slices!.Sum(candidate => Math.Max(0, candidate.Amount));
                double share = total <= 0 ? 0 : slice.Amount * 100 / total;
                string name = $"{slice.ProviderName}: {slice.LegendAmountText} · {share:0.#}%";
                AutomationProperties.SetAccessibilityView(path, AccessibilityView.Content);
                AutomationProperties.SetName(path, name);
                AutomationProperties.SetHelpText(path, name);
                ToolTipService.SetToolTip(path, CreateSliceToolTip(slice, share));
                path.Opacity = 0.9;
                path.PointerEntered += (_, _) => path.Opacity = 1;
                path.PointerExited += (_, _) => path.Opacity = 0.9;
                path.Tapped += (_, _) => ProviderInvoked?.Invoke(
                    this,
                    new ProviderInvokedEventArgs(slice.ProviderId));
            }
            else
            {
                AutomationProperties.SetAccessibilityView(path, AccessibilityView.Raw);
            }
            ArcCanvas.Children.Add(shadowPath);
            ArcCanvas.Children.Add(path);
            _arcVisuals.Add(new ArcVisual(path, shadowPath));
        }

        UpdateArcVisuals();
    }

    private void UpdateArcVisuals()
    {
        double size = Math.Min(ArcCanvas.ActualWidth, ArcCanvas.ActualHeight);
        if (size <= 0 || _arcVisuals.Count != _arcs.Length)
        {
            return;
        }

        double outerRadius = size / 2;
        double innerRadius = outerRadius * InnerRadiusRatio;
        double halfGapRadians = (GapWidth / outerRadius) / 2;
        double progress = Math.Clamp(RevealProgress, 0, 1);
        var center = new Point(ArcCanvas.ActualWidth / 2, ArcCanvas.ActualHeight / 2);

        for (int index = 0; index < _arcs.Length; index++)
        {
            SpendDonutArc arc = _arcs[index];
            ArcVisual visual = _arcVisuals[index];
            double endFraction = arc.StartFraction
                + ((arc.EndFraction - arc.StartFraction) * progress);
            double start = (-Math.PI / 2)
                + (arc.StartFraction * 2 * Math.PI)
                + halfGapRadians;
            double end = (-Math.PI / 2)
                + (endFraction * 2 * Math.PI)
                - halfGapRadians;
            double sweep = end - start;

            if (sweep <= 0.001 || innerRadius <= 0)
            {
                visual.Path.Visibility = Visibility.Collapsed;
                visual.ShadowPath.Visibility = Visibility.Collapsed;
                continue;
            }

            visual.Path.Visibility = Visibility.Visible;
            visual.ShadowPath.Visibility = Visibility.Visible;
            visual.Path.Data = CreateRoundedRibbonGeometry(
                center,
                innerRadius,
                outerRadius,
                start,
                end);
            visual.ShadowPath.Data = CreateRoundedRibbonGeometry(
                center,
                innerRadius,
                outerRadius,
                start,
                end);
        }
    }

    private static ToolTip CreateSliceToolTip(SpendSlice slice, double share)
    {
        var values = new Grid { ColumnSpacing = 14 };
        values.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        });
        values.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        values.Children.Add(new TextBlock
        {
            Text = slice.LegendAmountText,
            TextWrapping = TextWrapping.WrapWholeWords,
        });
        var percent = new TextBlock
        {
            Text = $"{share:0.#}%",
            FontFamily = new FontFamily("Consolas"),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextAlignment = TextAlignment.Right,
        };
        Grid.SetColumn(percent, 1);
        values.Children.Add(percent);

        var content = new StackPanel
        {
            MaxWidth = 240,
            Spacing = 5,
        };
        content.Children.Add(new TextBlock
        {
            Text = slice.ProviderName,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.WrapWholeWords,
        });
        content.Children.Add(values);
        return new ToolTip
        {
            Content = content,
            MaxWidth = 264,
            Placement = Microsoft.UI.Xaml.Controls.Primitives.PlacementMode.Bottom,
        };
    }

    private static PathGeometry CreateRoundedRibbonGeometry(
        Point center,
        double innerRadius,
        double outerRadius,
        double start,
        double end)
    {
        double sweep = end - start;
        double thickness = outerRadius - innerRadius;
        double cornerRadius = Math.Min(
            RibbonCornerRadius,
            Math.Min(
                thickness / 2,
                Math.Max(0, (sweep * innerRadius) / 2.2)));
        double outerInset = cornerRadius / outerRadius;
        double innerInset = cornerRadius / innerRadius;

        Point outerStartCorner = PolarPoint(center, outerRadius, start);
        Point outerEndCorner = PolarPoint(center, outerRadius, end);
        Point innerStartCorner = PolarPoint(center, innerRadius, start);
        Point innerEndCorner = PolarPoint(center, innerRadius, end);

        var figure = new PathFigure
        {
            IsClosed = true,
            IsFilled = true,
            StartPoint = PolarPoint(center, outerRadius, start + outerInset),
        };
        figure.Segments.Add(new ArcSegment
        {
            Point = PolarPoint(center, outerRadius, end - outerInset),
            Size = new Size(outerRadius, outerRadius),
            IsLargeArc = sweep - (2 * outerInset) > Math.PI,
            SweepDirection = SweepDirection.Clockwise,
        });
        figure.Segments.Add(new QuadraticBezierSegment
        {
            Point1 = outerEndCorner,
            Point2 = PolarPoint(center, outerRadius - cornerRadius, end),
        });
        figure.Segments.Add(new LineSegment
        {
            Point = PolarPoint(center, innerRadius + cornerRadius, end),
        });
        figure.Segments.Add(new QuadraticBezierSegment
        {
            Point1 = innerEndCorner,
            Point2 = PolarPoint(center, innerRadius, end - innerInset),
        });
        figure.Segments.Add(new ArcSegment
        {
            Point = PolarPoint(center, innerRadius, start + innerInset),
            Size = new Size(innerRadius, innerRadius),
            IsLargeArc = sweep - (2 * innerInset) > Math.PI,
            SweepDirection = SweepDirection.Counterclockwise,
        });
        figure.Segments.Add(new QuadraticBezierSegment
        {
            Point1 = innerStartCorner,
            Point2 = PolarPoint(center, innerRadius + cornerRadius, start),
        });
        figure.Segments.Add(new LineSegment
        {
            Point = PolarPoint(center, outerRadius - cornerRadius, start),
        });
        figure.Segments.Add(new QuadraticBezierSegment
        {
            Point1 = outerStartCorner,
            Point2 = figure.StartPoint,
        });

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    private static Point PolarPoint(Point center, double radius, double angle) =>
        new(
            center.X + (radius * Math.Cos(angle)),
            center.Y + (radius * Math.Sin(angle)));

    private Border ResolveBrushProxy(string providerId) => providerId switch
    {
        "antigravity" => AntigravityBrushProxy,
        "claude" => ClaudeBrushProxy,
        "codex" => CodexBrushProxy,
        "grok" => GrokBrushProxy,
        "opencode" => OpenCodeBrushProxy,
        _ => FallbackBrushProxy,
    };

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        if (Slices is { Count: > 0 } && RevealProgress < 1)
        {
            BeginReveal();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = false;
        _storyboard?.Stop();
        _storyboard = null;
        RevealProgress = Slices is { Count: > 0 } ? 1 : 0;
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args) => RebuildArcVisuals();

    private sealed record ArcVisual(XamlPath Path, XamlPath ShadowPath);

    private sealed record SliceVisualState(
        string ProviderId,
        double Amount,
        string? ColorHex);
}

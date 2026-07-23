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
using WOpenUsage.App.ViewModels.Sample;
using XamlPath = Microsoft.UI.Xaml.Shapes.Path;

namespace WOpenUsage.App.Controls;

public sealed partial class SpendDonutChart : UserControl
{
    private const double InnerRadiusRatio = 0.618;
    private const double GapWidth = 1.6;

    private readonly List<ArcVisual> _arcVisuals = [];
    private readonly AccessibilitySettings _accessibilitySettings = new();
    private readonly Dictionary<string, string> _customColors = new(StringComparer.Ordinal);
    private SpendDonutArc[] _arcs = [];
    private Storyboard? _storyboard;
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

    public static readonly DependencyProperty RevealProgressProperty =
        DependencyProperty.Register(
            nameof(RevealProgress),
            typeof(double),
            typeof(SpendDonutChart),
            new PropertyMetadata(0d, OnRevealProgressChanged));

    public static readonly DependencyProperty SlicesProperty =
        DependencyProperty.Register(
            nameof(Slices),
            typeof(IReadOnlyList<SampleSpendSlice>),
            typeof(SpendDonutChart),
            new PropertyMetadata(null, OnSlicesChanged));

    public SpendDonutChart()
    {
        InitializeComponent();
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

    public double RevealProgress
    {
        get => (double)GetValue(RevealProgressProperty);
        set => SetValue(RevealProgressProperty, value);
    }

    public IReadOnlyList<SampleSpendSlice>? Slices
    {
        get => (IReadOnlyList<SampleSpendSlice>?)GetValue(SlicesProperty);
        set => SetValue(SlicesProperty, value);
    }

    public void PlayReveal(int token)
    {
        if (token == _lastRevealToken)
        {
            return;
        }

        _lastRevealToken = token;
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
        chart._arcs = chart.Slices is null
            ? []
            : SpendDonutGeometry.CreateArcs(
                chart.Slices.Select(slice =>
                    new SpendDonutInput(slice.ProviderId, slice.Amount))).ToArray();
        chart._customColors.Clear();
        if (chart.Slices is not null)
        {
            foreach (SampleSpendSlice slice in chart.Slices)
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
    }

    private void OnChartSizeChanged(object sender, SizeChangedEventArgs e) => UpdateArcVisuals();

    private void RebuildArcVisuals()
    {
        ArcCanvas.Children.Clear();
        _arcVisuals.Clear();

        foreach (SpendDonutArc arc in _arcs)
        {
            var figure = new PathFigure
            {
                IsClosed = false,
                IsFilled = false,
            };
            var segment = new ArcSegment
            {
                SweepDirection = SweepDirection.Clockwise,
            };
            figure.Segments.Add(segment);

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);

            var shadowFigure = new PathFigure
            {
                IsClosed = false,
                IsFilled = false,
            };
            var shadowSegment = new ArcSegment
            {
                SweepDirection = SweepDirection.Clockwise,
            };
            shadowFigure.Segments.Add(shadowSegment);

            var shadowGeometry = new PathGeometry();
            shadowGeometry.Figures.Add(shadowFigure);

            var path = new XamlPath
            {
                Data = geometry,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeStartLineCap = PenLineCap.Round,
                Visibility = Visibility.Collapsed,
            };
            var shadowPath = new XamlPath
            {
                Data = shadowGeometry,
                IsHitTestVisible = false,
                RenderTransform = new TranslateTransform { Y = 0.75 },
                StrokeEndLineCap = PenLineCap.Round,
                StrokeStartLineCap = PenLineCap.Round,
                Visibility = Visibility.Collapsed,
            };
            if (!_accessibilitySettings.HighContrast
                && _customColors.TryGetValue(arc.ProviderId, out string? colorHex))
            {
                path.Stroke = ProviderColorPalette.CreateGradient(colorHex);
            }
            else
            {
                path.SetBinding(
                    Shape.StrokeProperty,
                    new Binding
                    {
                        Source = ResolveBrushProxy(arc.ProviderId),
                        Path = new PropertyPath(nameof(Border.Background)),
                    });
            }
            shadowPath.SetBinding(
                Shape.StrokeProperty,
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
            AutomationProperties.SetAccessibilityView(path, AccessibilityView.Raw);
            ArcCanvas.Children.Add(shadowPath);
            ArcCanvas.Children.Add(path);
            _arcVisuals.Add(
                new ArcVisual(
                    path,
                    shadowPath,
                    figure,
                    segment,
                    shadowFigure,
                    shadowSegment));
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
        double strokeThickness = outerRadius - innerRadius;
        double radius = innerRadius + (strokeThickness / 2);
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

            if (sweep <= 0.001 || radius <= 0)
            {
                visual.Path.Visibility = Visibility.Collapsed;
                visual.ShadowPath.Visibility = Visibility.Collapsed;
                continue;
            }

            visual.Path.Visibility = Visibility.Visible;
            visual.ShadowPath.Visibility = Visibility.Visible;
            visual.Path.StrokeThickness = strokeThickness;
            visual.ShadowPath.StrokeThickness = strokeThickness;
            visual.Figure.StartPoint = PolarPoint(center, radius, start);
            visual.Segment.Point = PolarPoint(center, radius, end);
            visual.Segment.Size = new Size(radius, radius);
            visual.Segment.IsLargeArc = sweep > Math.PI;
            visual.ShadowFigure.StartPoint = visual.Figure.StartPoint;
            visual.ShadowSegment.Point = visual.Segment.Point;
            visual.ShadowSegment.Size = visual.Segment.Size;
            visual.ShadowSegment.IsLargeArc = visual.Segment.IsLargeArc;
        }
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

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _storyboard?.Stop();
        _storyboard = null;
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args) => RebuildArcVisuals();

    private sealed record ArcVisual(
        XamlPath Path,
        XamlPath ShadowPath,
        PathFigure Figure,
        ArcSegment Segment,
        PathFigure ShadowFigure,
        ArcSegment ShadowSegment);
}

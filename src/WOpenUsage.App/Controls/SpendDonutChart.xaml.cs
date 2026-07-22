using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
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

    private static readonly Dictionary<string, string> PaletteResourceKeys =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["antigravity"] = "ProviderAntigravityBrush",
            ["claude"] = "ProviderClaudeBrush",
            ["codex"] = "ProviderCodexBrush",
            ["grok"] = "ProviderGrokBrush",
            ["opencode"] = "ProviderOpenCodeBrush",
        };

    private readonly AccessibilitySettings _accessibilitySettings = new();
    private readonly List<ArcVisual> _arcVisuals = [];
    private readonly Dictionary<string, Brush> _brandBrushes;
    private readonly Brush _fallbackBrush;
    private readonly Brush _highContrastBrush;
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
        _brandBrushes = PaletteResourceKeys.ToDictionary(
            pair => pair.Key,
            pair => (Brush)Application.Current.Resources[pair.Value],
            StringComparer.Ordinal);
        _fallbackBrush = (Brush)Application.Current.Resources["ProviderFallbackBrush"];
        _highContrastBrush = (Brush)Application.Current.Resources["ProviderHighContrastBrush"];
        ActualThemeChanged += OnActualThemeChanged;
        Unloaded += OnUnloaded;
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

            var path = new XamlPath
            {
                Data = geometry,
                Stroke = ResolveBrush(arc.ProviderId),
                StrokeEndLineCap = PenLineCap.Round,
                StrokeStartLineCap = PenLineCap.Round,
                Visibility = Visibility.Collapsed,
            };
            AutomationProperties.SetAccessibilityView(path, AccessibilityView.Raw);
            ArcCanvas.Children.Add(path);
            _arcVisuals.Add(new ArcVisual(path, figure, segment));
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
                continue;
            }

            visual.Path.Visibility = Visibility.Visible;
            visual.Path.StrokeThickness = strokeThickness;
            visual.Figure.StartPoint = PolarPoint(center, radius, start);
            visual.Segment.Point = PolarPoint(center, radius, end);
            visual.Segment.Size = new Size(radius, radius);
            visual.Segment.IsLargeArc = sweep > Math.PI;
        }
    }

    private void ApplyArcBrushes()
    {
        for (int index = 0; index < _arcs.Length && index < _arcVisuals.Count; index++)
        {
            _arcVisuals[index].Path.Stroke = ResolveBrush(_arcs[index].ProviderId);
        }
    }

    private static Point PolarPoint(Point center, double radius, double angle) =>
        new(
            center.X + (radius * Math.Cos(angle)),
            center.Y + (radius * Math.Sin(angle)));

    private Brush ResolveBrush(string providerId)
    {
        if (_accessibilitySettings.HighContrast)
        {
            return _highContrastBrush;
        }

        return _brandBrushes.TryGetValue(providerId, out Brush? brush)
            ? brush
            : _fallbackBrush;
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        ApplyArcBrushes();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _storyboard?.Stop();
        _storyboard = null;
    }

    private sealed record ArcVisual(
        XamlPath Path,
        PathFigure Figure,
        ArcSegment Segment);
}

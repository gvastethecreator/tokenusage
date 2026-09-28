using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;

namespace TokenUsage.App.Controls;

/// <summary>
/// A static ranking bar for report cards. ProgressBar sizes its indicator from its own width;
/// inside an ItemsRepeater that width can follow the indicator back, layout never settles, and
/// WinUI terminates the process with a layout cycle. Star columns split the width without
/// reading it, so the bar cannot feed its own measure. Growth is a compositor scale on the
/// fill, so the layout is final immediately and the motion only explains the change.
/// </summary>
public sealed partial class ReportShareBar : Grid
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(ReportShareBar), new PropertyMetadata(0d, OnValueChanged));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(ReportShareBar), new PropertyMetadata(null, OnBrushChanged));

    public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(
        nameof(Track), typeof(Brush), typeof(ReportShareBar), new PropertyMetadata(null, OnBrushChanged));

    /// <summary>When set, the fill takes the provider's chart color so a bar and its chart series match.</summary>
    public static readonly DependencyProperty ProviderIdProperty = DependencyProperty.Register(
        nameof(ProviderId), typeof(string), typeof(ReportShareBar), new PropertyMetadata(null, OnBrushChanged));

    private static readonly Windows.UI.ViewManagement.AccessibilitySettings Accessibility = new();
    private readonly Border _track = new();
    private readonly Border _fill = new();
    private double _pendingFrom = double.NaN;
    private object? _valueItem;
    private Microsoft.UI.System.ThemeSettings? _themeSettings;

    public ReportShareBar()
    {
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        SetColumnSpan(_track, 2);
        Children.Add(_track);
        Children.Add(_fill);
        IsHitTestVisible = false;
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        SizeChanged += (_, args) => UpdateCorners(args.NewSize.Height);
        Loaded += (_, _) =>
        {
            // AccessibilitySettings events are unavailable to desktop WinUI; ThemeSettings reports
            // high contrast changes that leave ActualTheme unchanged.
            if (_themeSettings is null && XamlRoot?.ContentIslandEnvironment is { } environment)
            {
                _themeSettings = Microsoft.UI.System.ThemeSettings.CreateForWindowId(environment.AppWindowId);
                _themeSettings.Changed += OnSystemThemeChanged;
            }
            ApplyBrushes();
            if (!double.IsNaN(_pendingFrom)) Grow(_pendingFrom);
        };
        Unloaded += (_, _) =>
        {
            if (_themeSettings is not null) _themeSettings.Changed -= OnSystemThemeChanged;
            _themeSettings = null;
        };
        ActualThemeChanged += (_, _) => ApplyBrushes();
    }

    /// <summary>Filled share from 0 to 100.</summary>
    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public Brush? Fill
    {
        get => (Brush?)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public Brush? Track
    {
        get => (Brush?)GetValue(TrackProperty);
        set => SetValue(TrackProperty, value);
    }

    public string? ProviderId
    {
        get => (string?)GetValue(ProviderIdProperty);
        set => SetValue(ProviderIdProperty, value);
    }

    protected override AutomationPeer? OnCreateAutomationPeer() => null;

    private static double Clamp(object value) =>
        value is double number && double.IsFinite(number) ? Math.Clamp(number, 0, 100) : 0;

    private static void OnValueChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var bar = (ReportShareBar)sender;
        double value = Clamp(args.NewValue);
        double previous = Clamp(args.OldValue);
        bar.ColumnDefinitions[0].Width = new GridLength(value, GridUnitType.Star);
        bar.ColumnDefinitions[1].Width = new GridLength(100 - value, GridUnitType.Star);
        // A tiny nonzero share still shows a sliver so it never reads as zero.
        bar._fill.MinWidth = value > 0 ? 2 : 0;
        bar._fill.Visibility = value > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (value <= 0) return;
        // A recycled row that now shows another item has no previous length of its own; growing
        // from the old item's length would read as a change in the data.
        object? item = bar.DataContext;
        bool sameItem = bar._valueItem is null || ReferenceEquals(bar._valueItem, item);
        bar._valueItem = item;
        double from = sameItem ? previous / value : 1;
        if (bar.IsLoaded) bar.Grow(from);
        else bar._pendingFrom = from;
    }

    // Scale from the previous length to the new one, anchored at the start edge. The new layout
    // is already in place, so an interrupted or disabled animation still ends in the true state.
    private void Grow(double from)
    {
        _pendingFrom = double.NaN;
        Visual visual = ElementCompositionPreview.GetElementVisual(_fill);
        visual.StopAnimation("Scale.X");
        visual.Scale = Vector3.One;
        if (!MotionSettings.AreAnimationsEnabled() || MotionSettings.IsCapturing || Math.Abs(from - 1) < 0.001) return;
        Compositor compositor = visual.Compositor;
        visual.CenterPoint = Vector3.Zero;
        ScalarKeyFrameAnimation animation = compositor.CreateScalarKeyFrameAnimation();
        animation.Duration = MotionSettings.ReportBarGrowDuration;
        animation.InsertKeyFrame(0, (float)Math.Clamp(from, 0, 50));
        animation.InsertKeyFrame(1, 1, compositor.CreateCubicBezierEasingFunction(
            new Vector2(0.16f, 1), new Vector2(0.3f, 1)));
        visual.StartAnimation("Scale.X", animation);
    }

    private static void OnBrushChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((ReportShareBar)sender).ApplyBrushes();

    private void OnSystemThemeChanged(Microsoft.UI.System.ThemeSettings sender, object args) =>
        DispatcherQueue.TryEnqueue(ApplyBrushes);

    private void ApplyBrushes()
    {
        _fill.Background = !Accessibility.HighContrast && ProviderId is { Length: > 0 } providerId
            ? new SolidColorBrush(ProviderColorPalette.Parse(ProviderColorPalette.GetEffectiveHex(providerId, null)))
            : Fill;
        _track.Background = Track;
    }

    private void UpdateCorners(double height)
    {
        var radius = new CornerRadius(Math.Max(0, height / 2));
        _track.CornerRadius = radius;
        _fill.CornerRadius = radius;
    }
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using TokenUsage.Core.Usage;

namespace TokenUsage.App.Controls;

public sealed partial class AnimatedProgressBar : UserControl
{
    private Storyboard? _storyboard;
    private int _lastRevealToken = int.MinValue;

    public static readonly DependencyProperty AutomationNameProperty =
        DependencyProperty.Register(
            nameof(AutomationName),
            typeof(string),
            typeof(AnimatedProgressBar),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty TargetValueProperty =
        DependencyProperty.Register(
            nameof(TargetValue),
            typeof(double),
            typeof(AnimatedProgressBar),
            new PropertyMetadata(0d, OnValueChanged));

    public static readonly DependencyProperty RemainingValueProperty =
        DependencyProperty.Register(
            nameof(RemainingValue),
            typeof(double),
            typeof(AnimatedProgressBar),
            new PropertyMetadata(0d, OnValueChanged));

    public AnimatedProgressBar()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        ActualThemeChanged += OnActualThemeChanged;
    }

    public string AutomationName
    {
        get => (string)GetValue(AutomationNameProperty);
        set => SetValue(AutomationNameProperty, value);
    }

    public double TargetValue
    {
        get => (double)GetValue(TargetValueProperty);
        set => SetValue(TargetValueProperty, value);
    }

    public double RemainingValue
    {
        get => (double)GetValue(RemainingValueProperty);
        set => SetValue(RemainingValueProperty, value);
    }

    public void PlayReveal(int token)
    {
        if (token == _lastRevealToken)
        {
            return;
        }

        _lastRevealToken = token;
        _storyboard?.Stop();

        double target = SpendDonutGeometry.ClampPercent(TargetValue);
        UpdateForeground(SpendDonutGeometry.ClampPercent(RemainingValue));
        if (!MotionSettings.AreAnimationsEnabled() || target <= 0)
        {
            FillBar.Value = target;
            return;
        }

        FillBar.Value = 0;
        var animation = new DoubleAnimation
        {
            To = target,
            Duration = MotionSettings.QuotaRevealDuration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            EnableDependentAnimation = true,
        };

        Storyboard.SetTarget(animation, FillBar);
        Storyboard.SetTargetProperty(animation, nameof(ProgressBar.Value));

        _storyboard = new Storyboard();
        _storyboard.Children.Add(animation);
        _storyboard.Begin();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _storyboard?.Stop();
        _storyboard = null;
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => ApplyCurrentValues();

    private static void OnValueChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        _ = args;
        if (dependencyObject is AnimatedProgressBar progressBar)
        {
            progressBar.ApplyCurrentValues();
        }
    }

    private void ApplyCurrentValues()
    {
        if (FillBar is null)
        {
            return;
        }

        FillBar.Value = SpendDonutGeometry.ClampPercent(TargetValue);
        UpdateForeground(SpendDonutGeometry.ClampPercent(RemainingValue));
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args) =>
        UpdateForeground(SpendDonutGeometry.ClampPercent(RemainingValue));

    private void UpdateForeground(double remainingPercent)
    {
        FillBar.Foreground = QuotaUsageLevelPolicy.Evaluate((decimal)remainingPercent) switch
        {
            QuotaUsageLevel.Healthy => HealthyBrushProxy.Background,
            QuotaUsageLevel.Caution => CautionBrushProxy.Background,
            QuotaUsageLevel.Warning => WarningBrushProxy.Background,
            QuotaUsageLevel.Critical => CriticalBrushProxy.Background,
            _ => throw new InvalidOperationException("Unknown quota usage level."),
        };
    }
}

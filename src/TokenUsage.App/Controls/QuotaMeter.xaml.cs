using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TokenUsage.Core.Usage;

namespace TokenUsage.App.Controls;

public sealed partial class QuotaMeter : UserControl
{
    public static readonly DependencyProperty AutomationNameProperty =
        DependencyProperty.Register(
            nameof(AutomationName),
            typeof(string),
            typeof(QuotaMeter),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty TargetValueProperty =
        DependencyProperty.Register(
            nameof(TargetValue),
            typeof(double),
            typeof(QuotaMeter),
            new PropertyMetadata(0d, OnValueChanged));

    public static readonly DependencyProperty RemainingValueProperty =
        DependencyProperty.Register(
            nameof(RemainingValue),
            typeof(double),
            typeof(QuotaMeter),
            new PropertyMetadata(0d, OnValueChanged));

    public static readonly DependencyProperty IsBarProperty = DependencyProperty.Register(
        nameof(IsBar), typeof(bool), typeof(QuotaMeter), new PropertyMetadata(false, OnValueChanged));

    public bool IsBar
    {
        get => (bool)GetValue(IsBarProperty);
        set => SetValue(IsBarProperty, value);
    }

    public QuotaMeter()
    {
        InitializeComponent();
        Loaded += OnLoaded;
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

    private void OnLoaded(object sender, RoutedEventArgs e) => ApplyCurrentValues();

    private static void OnValueChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        _ = args;
        if (dependencyObject is QuotaMeter progressBar)
        {
            progressBar.ApplyCurrentValues();
        }
    }

    private void ApplyCurrentValues()
    {
        if (FillRing is null)
        {
            return;
        }

        FillRing.Visibility = IsBar ? Visibility.Collapsed : Visibility.Visible;
        FillBar.Visibility = IsBar ? Visibility.Visible : Visibility.Collapsed;
        FillRing.Value = FillBar.Value = SpendDonutGeometry.ClampPercent(TargetValue);
        UpdateForeground(SpendDonutGeometry.ClampPercent(RemainingValue));
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args) =>
        UpdateForeground(SpendDonutGeometry.ClampPercent(RemainingValue));

    private void UpdateForeground(double remainingPercent)
    {
        FillBar.Foreground = FillRing.Foreground = QuotaUsageLevelPolicy.Evaluate((decimal)remainingPercent) switch
        {
            QuotaUsageLevel.Healthy => HealthyBrushProxy.Background,
            QuotaUsageLevel.Caution => CautionBrushProxy.Background,
            QuotaUsageLevel.Warning => WarningBrushProxy.Background,
            QuotaUsageLevel.Critical => CriticalBrushProxy.Background,
            _ => throw new InvalidOperationException("Unknown quota usage level."),
        };
    }
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace WOpenUsage.App.Controls;

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
            new PropertyMetadata(0d));

    public AnimatedProgressBar()
    {
        InitializeComponent();
        Unloaded += OnUnloaded;
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

    public void PlayReveal(int token)
    {
        if (token == _lastRevealToken)
        {
            return;
        }

        _lastRevealToken = token;
        _storyboard?.Stop();

        double target = SpendDonutGeometry.ClampPercent(TargetValue);
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
}

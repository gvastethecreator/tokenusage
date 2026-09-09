using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;
using Windows.UI.ViewManagement;
using System.Diagnostics;

namespace TokenUsage.App.Controls;

internal sealed class ThemeSwitchTransition
{
    private Storyboard? _storyboard;
    private RotateTransform? _rotation;
    private readonly List<(SolidColorBrush Brush, Color From, Color To)> _colors = [];
    private readonly Stopwatch _clock = new();

    public void Apply(FrameworkElement root, FrameworkElement logo, ElementTheme theme)
    {
        if (root.RequestedTheme == theme) return;
        bool animate = root.XamlRoot is not null && MotionSettings.AreAnimationsEnabled()
            && !new AccessibilitySettings().HighContrast;
        var previous = animate ? Capture(root).ToArray() : [];
        double angle = _rotation?.Angle ?? 0;
        Stop();
        root.RequestedTheme = theme;
        if (!animate) return;
        root.UpdateLayout();

        var storyboard = new Storyboard();
        foreach (var (element, property, name, color) in previous)
        {
            if (element.GetValue(property) is not SolidColorBrush brush
                || brush.Color == color) continue;
            // Use the animation layer so stopping restores the original theme-resource
            // expression. Never mutate shared brushes or replace local XAML values.
            var transitionBrush = new SolidColorBrush(color) { Opacity = brush.Opacity };
            _colors.Add((transitionBrush, color, brush.Color));
            var animation = new ObjectAnimationUsingKeyFrames
            {
                Duration = MotionSettings.ThemeSwitchDuration,
            };
            animation.KeyFrames.Add(new DiscreteObjectKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero), Value = transitionBrush,
            });
            Storyboard.SetTarget(animation, element);
            Storyboard.SetTargetProperty(animation, name);
            storyboard.Children.Add(animation);
        }

        _rotation ??= new RotateTransform();
        logo.RenderTransformOrigin = new Windows.Foundation.Point(.5, .5);
        logo.RenderTransform = _rotation;
        var spin = new DoubleAnimation
        {
            From = angle, To = angle == 0 ? 360 : Math.Ceiling(angle / 360) * 360,
            Duration = MotionSettings.ThemeSwitchDuration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(spin, _rotation);
        Storyboard.SetTargetProperty(spin, "Angle");
        storyboard.Children.Add(spin);
        storyboard.Completed += (_, _) =>
        {
            if (ReferenceEquals(_storyboard, storyboard)) Stop();
        };
        _storyboard = storyboard;
        _clock.Restart();
        CompositionTarget.Rendering += OnRendering;
        storyboard.Begin();
    }

    public void Stop()
    {
        CompositionTarget.Rendering -= OnRendering;
        _clock.Reset();
        _colors.Clear();
        _storyboard?.Stop();
        _storyboard = null;
        if (_rotation is not null) _rotation.Angle = 0;
    }

    private void OnRendering(object? sender, object args)
    {
        double progress = Math.Clamp(_clock.Elapsed.TotalMilliseconds / MotionSettings.ThemeSwitchDuration.TotalMilliseconds, 0, 1);
        double eased = 1 - Math.Pow(1 - progress, 3);
        foreach (var (brush, from, to) in _colors)
            brush.Color = Color.FromArgb(Blend(from.A, to.A), Blend(from.R, to.R), Blend(from.G, to.G), Blend(from.B, to.B));
        byte Blend(byte from, byte to) => (byte)Math.Round(from + (to - from) * eased);
    }

    private static IEnumerable<(DependencyObject Element, DependencyProperty Property, string Name, Color Color)> Capture(DependencyObject element)
    {
        (DependencyProperty Property, string Name)[] properties = element switch
        {
            Control => [(Control.BackgroundProperty, "Background"), (Control.ForegroundProperty, "Foreground"), (Control.BorderBrushProperty, "BorderBrush")],
            Border => [(Border.BackgroundProperty, "Background"), (Border.BorderBrushProperty, "BorderBrush")],
            Panel => [(Panel.BackgroundProperty, "Background")],
            TextBlock => [(TextBlock.ForegroundProperty, "Foreground")],
            ContentPresenter => [(ContentPresenter.BackgroundProperty, "Background"), (ContentPresenter.ForegroundProperty, "Foreground"), (ContentPresenter.BorderBrushProperty, "BorderBrush")],
            Shape => [(Shape.FillProperty, "Fill"), (Shape.StrokeProperty, "Stroke")],
            _ => [],
        };
        foreach (var (property, name) in properties)
            if (element.GetValue(property) is SolidColorBrush brush)
                yield return (element, property, name, brush.Color);
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
            foreach (var item in Capture(VisualTreeHelper.GetChild(element, index))) yield return item;
    }
}

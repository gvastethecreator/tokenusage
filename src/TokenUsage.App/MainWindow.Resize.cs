using System.Diagnostics;
using Microsoft.UI.Xaml.Media;
using TokenUsage.App.Controls;
using TokenUsage.Platform.Windows.Placement;

namespace TokenUsage.App;

public sealed partial class MainWindow
{
    private void ApplyWindowBounds(PlatformRect bounds, uint dpi)
    {
        RootPage.MeasureRoot.Width = bounds.Width * 96d / dpi;
        RootPage.MeasureRoot.Height = bounds.Height * 96d / dpi;
        MoveTo(bounds);
    }

    private void AnimateWindowBounds(PlatformRect target, uint dpi)
    {
        if (_resizeTarget == target) return;
        StopWindowResize();
        var position = AppWindow.Position;
        var size = AppWindow.Size;
        _resizeFrom = new PlatformRect(position.X, position.Y, position.X + size.Width, position.Y + size.Height);
        _resizeDpi = dpi;
        if (_resizeFrom == target)
        {
            ApplyWindowBounds(target, dpi);
            return;
        }
        _resizeTarget = target;
        _resizeStarted = Stopwatch.GetTimestamp();
        ApplyWindowBounds(_resizeFrom, dpi);
        CompositionTarget.Rendering += OnWindowResizeFrame;
    }

    private void OnWindowResizeFrame(object? sender, object e)
    {
        if (_resizeTarget is not { } target) return;
        if (_disposed || !_isFlyoutVisible) { StopWindowResize(); return; }
        double progress = MotionSettings.AreAnimationsEnabled()
            ? Math.Clamp(Stopwatch.GetElapsedTime(_resizeStarted).TotalMilliseconds / MotionSettings.ViewTransitionDuration.TotalMilliseconds, 0, 1)
            : 1;
        double eased = 1 - Math.Pow(1 - progress, 3);
        int Interpolate(int from, int to) => (int)Math.Round(from + (to - from) * eased);
        ApplyWindowBounds(new PlatformRect(
            Interpolate(_resizeFrom.Left, target.Left), Interpolate(_resizeFrom.Top, target.Top),
            Interpolate(_resizeFrom.Right, target.Right), Interpolate(_resizeFrom.Bottom, target.Bottom)), _resizeDpi);
        if (progress >= 1) StopWindowResize();
    }

    private void StopWindowResize()
    {
        CompositionTarget.Rendering -= OnWindowResizeFrame;
        _resizeTarget = null;
    }
}

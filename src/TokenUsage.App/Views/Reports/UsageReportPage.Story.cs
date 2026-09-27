using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using TokenUsage.App.Controls;

namespace TokenUsage.App.Views.Reports;

// Motion for the narrated report surfaces. Layout is always final before any animation starts;
// motion only explains that the numbers changed, and the Windows animation setting skips it.
public sealed partial class UsageReportPage
{
    private static readonly TimeSpan HighlightRevealDuration = TimeSpan.FromMilliseconds(320);
    private static readonly TimeSpan HighlightRevealStagger = TimeSpan.FromMilliseconds(55);
    private object? _highlightSource;
    private readonly HashSet<int> _revealedHighlights = [];

    // Reveal each tile once per data change. Scrolling can re-prepare the same element,
    // and replaying the entrance there would read as the data changing again.
    private void OnHighlightPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (!ReferenceEquals(sender.ItemsSource, _highlightSource))
        {
            _highlightSource = sender.ItemsSource;
            _revealedHighlights.Clear();
        }

        if (!_revealedHighlights.Add(args.Index) || !MotionSettings.AreAnimationsEnabled() || _isCapturing) return;
        UIElement element = args.Element;
        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        Visual visual = ElementCompositionPreview.GetElementVisual(element);
        Compositor compositor = visual.Compositor;
        CompositionEasingFunction easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1), new Vector2(0.3f, 1));
        TimeSpan delay = HighlightRevealStagger * args.Index;

        ScalarKeyFrameAnimation fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, 0);
        fade.InsertKeyFrame(1, 1, easing);
        fade.Duration = HighlightRevealDuration;
        fade.DelayTime = delay;
        fade.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;

        Vector3KeyFrameAnimation rise = compositor.CreateVector3KeyFrameAnimation();
        rise.InsertKeyFrame(0, new Vector3(0, 10, 0));
        rise.InsertKeyFrame(1, Vector3.Zero, easing);
        rise.Duration = HighlightRevealDuration;
        rise.DelayTime = delay;
        rise.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;

        visual.StartAnimation("Opacity", fade);
        visual.StartAnimation("Translation", rise);
    }

    // The token mix bar grows from its start edge when the mix changes.
    private void RevealTokenMix()
    {
        Visual visual = ElementCompositionPreview.GetElementVisual(TokenMixBar);
        visual.StopAnimation("Scale.X");
        visual.Scale = Vector3.One;
        if (!MotionSettings.AreAnimationsEnabled() || _isCapturing || !TokenMixBar.IsLoaded) return;
        Compositor compositor = visual.Compositor;
        visual.CenterPoint = Vector3.Zero;
        ScalarKeyFrameAnimation grow = compositor.CreateScalarKeyFrameAnimation();
        grow.InsertKeyFrame(0, 0);
        grow.InsertKeyFrame(1, 1, compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1), new Vector2(0.3f, 1)));
        grow.Duration = MotionSettings.ReportBarGrowDuration;
        visual.StartAnimation("Scale.X", grow);
    }
}

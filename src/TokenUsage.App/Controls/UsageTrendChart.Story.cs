using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using TokenUsage.App.ViewModels.Reports;
using TokenUsage.Core.Appearance;

namespace TokenUsage.App.Controls;

// The narrated layer of the trend chart: bars grow from the baseline day by day, the hovered
// day stays in focus while the others step back, and two reference marks (the average day and
// the peak) give the reader something to compare against without reading every bar.
public sealed partial class UsageTrendChart
{
    private const double DimmedColumnOpacity = 0.4;
    private readonly Dictionary<int, Canvas> _barColumns = [];
    private Canvas? _marksLayer;
    private int _slotsPerDay = 1;
    private int? _emphasizedDay;

    private Canvas BarColumn(int slot)
    {
        if (_barColumns.TryGetValue(slot, out Canvas? column)) return column;
        column = new Canvas { IsHitTestVisible = false };
        // Composition owns this column's visual (scale reveal), so the hover fade is an implicit
        // composition animation too; XAML OpacityTransition cannot share the visual.
        if (MotionSettings.AreAnimationsEnabled())
        {
            Visual visual = ElementCompositionPreview.GetElementVisual(column);
            ScalarKeyFrameAnimation fade = visual.Compositor.CreateScalarKeyFrameAnimation();
            fade.Target = "Opacity";
            fade.InsertExpressionKeyFrame(1, "this.FinalValue");
            fade.Duration = TimeSpan.FromMilliseconds(160);
            ImplicitAnimationCollection implicitAnimations = visual.Compositor.CreateImplicitAnimationCollection();
            implicitAnimations["Opacity"] = fade;
            visual.ImplicitAnimations = implicitAnimations;
        }
        _barColumns[slot] = column;
        _seriesCanvas.Children.Add(column);
        return column;
    }

    private void ResetBarColumns(UsageReportTrendDataset data)
    {
        _barColumns.Clear();
        _marksLayer = null;
        _emphasizedDay = null;
        _slotsPerDay = data.Style == ReportChartStyle.TwoHourBars ? 12 : 1;
    }

    // Columns rise from the baseline with a short left-to-right stagger. The bars already sit
    // at their final size; the scale only explains that new numbers arrived.
    private void StartColumnGrow(double baseline)
    {
        foreach ((int slot, Canvas column) in _barColumns)
        {
            Visual visual = ElementCompositionPreview.GetElementVisual(column);
            Compositor compositor = visual.Compositor;
            visual.CenterPoint = new Vector3(0, (float)baseline, 0);
            ScalarKeyFrameAnimation grow = compositor.CreateScalarKeyFrameAnimation();
            grow.InsertKeyFrame(0, 0);
            grow.InsertKeyFrame(1, 1, compositor.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0.9f), new Vector2(0.3f, 1)));
            grow.Duration = MotionSettings.ReportChartGrowDuration;
            grow.DelayTime = MotionSettings.ReportChartGrowStagger * (slot / _slotsPerDay);
            grow.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
            visual.StartAnimation("Scale.Y", grow);
        }

        // The average line and peak label describe finished bars, so they arrive after them.
        if (_marksLayer is null || _barColumns.Count == 0) return;
        Visual marks = ElementCompositionPreview.GetElementVisual(_marksLayer);
        ScalarKeyFrameAnimation fade = marks.Compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, 0);
        fade.InsertKeyFrame(1, 1);
        fade.Duration = TimeSpan.FromMilliseconds(200);
        fade.DelayTime = MotionSettings.ReportChartGrowStagger * (_barColumns.Keys.Max() / _slotsPerDay)
            + MotionSettings.ReportChartGrowDuration * 0.7;
        fade.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
        marks.StartAnimation("Opacity", fade);
    }

    private void FinishColumnGrow()
    {
        if (_marksLayer is not null)
        {
            Visual marks = ElementCompositionPreview.GetElementVisual(_marksLayer);
            marks.StopAnimation("Opacity");
            marks.Opacity = 1;
        }

        foreach (Canvas column in _barColumns.Values)
        {
            Visual visual = ElementCompositionPreview.GetElementVisual(column);
            visual.StopAnimation("Scale.Y");
            visual.Scale = Vector3.One;
        }
    }

    /// <summary>Moves the chart's day focus to <paramref name="date"/> and shows its hover card.</summary>
    internal bool RevealDay(DateOnly date)
    {
        int index = Data.Days.ToList().FindIndex(day => day.Date == date);
        if (index < 0 || Visibility != Visibility.Visible) return false;
        CancelPendingHover();
        _hoverIndex = index;
        StartBringIntoView(new BringIntoViewOptions { AnimationDesired = MotionSettings.AreAnimationsEnabled(), VerticalAlignmentRatio = 0.3 });
        if (FocusState == FocusState.Unfocused) Focus(FocusState.Programmatic);
        ShowHover(index, animate: false);
        return true;
    }

    // Moving between days only changes the day left and the day entered; entering or leaving
    // the chart changes every column. Skipping the rest keeps two-hour charts (360 columns) cheap.
    private void EmphasizeDay(int? day)
    {
        if (day == _emphasizedDay) return;
        int? previous = _emphasizedDay;
        _emphasizedDay = day;
        foreach ((int slot, Canvas column) in _barColumns)
        {
            int columnDay = slot / _slotsPerDay;
            if (previous is not null && day is not null && columnDay != previous && columnDay != day) continue;
            Visual visual = ElementCompositionPreview.GetElementVisual(column);
            float opacity = day is null || columnDay == day ? 1f : (float)DimmedColumnOpacity;
            if (IsCaptureMode)
            {
                ImplicitAnimationCollection? fade = visual.ImplicitAnimations;
                visual.ImplicitAnimations = null;
                visual.Opacity = opacity;
                visual.ImplicitAnimations = fade;
            }
            else
            {
                visual.Opacity = opacity;
            }
        }
    }

    // Average per active day and the peak day, drawn only where they are honest: daily bars or
    // lines of one metric. Shares, comparisons, and two-hour buckets have no single "day total".
    private void RenderReferenceMarks(UsageReportTrendDataset data, double width, double height, UsageTrendScale scale)
    {
        if (IsPreview || data.IsComparison || data.Metric == UsageReportMetric.Share
            || data.Style == ReportChartStyle.TwoHourBars || data.Days.Count < 3 || scale.Maximum <= 0) return;
        // A day total only exists on screen when series stack (bars, area) or there is one series;
        // independent lines would put the average of a sum next to lines it does not describe.
        bool showsDayTotal = data.Style is ReportChartStyle.Bars or ReportChartStyle.Area || data.Series.Count == 1;
        if (!showsDayTotal) return;

        double[] totals = [.. Enumerable.Range(0, data.Days.Count).Select(day => data.Series.Sum(series =>
            day < series.Values.Count && double.IsFinite(series.Values[day]) ? Math.Max(0, series.Values[day]) : 0))];
        double[] active = [.. totals.Where(total => total > 0)];
        if (active.Length < 2) return;

        double baseline = height - BottomPadding;
        double available = baseline - TopPadding;
        double average = active.Average();
        double averageY = baseline - scale.Normalize(average) * available;
        _marksLayer = new Canvas { IsHitTestVisible = false };
        PlotCanvas.Children.Add(_marksLayer);
        _marksLayer.Children.Add(new Line
        {
            X1 = 0, X2 = width, Y1 = averageY, Y2 = averageY,
            Stroke = TextBrushProxy.Background, StrokeThickness = 1, StrokeDashArray = [4, 3],
            Opacity = IsHighContrast ? 1 : 0.75, IsHitTestVisible = false,
        });

        Windows.Foundation.Rect? peakRect = null;
        if (data.Style is ReportChartStyle.Bars || data.Days.Count == 1)
        {
            int peak = Array.IndexOf(totals, totals.Max());
            double peakX = (peak + 0.5) * width / data.Days.Count;
            double peakY = baseline - scale.Normalize(totals[peak]) * available;
            peakRect = AddMarkLabel(FormatValue(totals[peak], data.Metric, exact: true), peakX, peakY, alignRight: false);
        }

        string averageText = string.Format(System.Globalization.CultureInfo.CurrentCulture,
            GetString("UsageReportChartAverageFormat"), FormatValue(average, data.Metric, exact: true));
        Windows.Foundation.Rect averageRect = AddMarkLabel(averageText, width, averageY, alignRight: true);
        if (peakRect is { } occupied && Intersects(occupied, averageRect))
        {
            // The peak sits over the right edge; move the average label to the left edge instead.
            _marksLayer.Children.RemoveAt(_marksLayer.Children.Count - 1);
            AddMarkLabel(averageText, 0, averageY, alignRight: false, anchorLeft: true);
        }
    }

    private static bool Intersects(Windows.Foundation.Rect a, Windows.Foundation.Rect b) =>
        a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;

    private Windows.Foundation.Rect AddMarkLabel(string text, double x, double y, bool alignRight, bool anchorLeft = false)
    {
        var label = new Border
        {
            Padding = new Thickness(5, 1, 5, 2),
            CornerRadius = new CornerRadius(4),
            Background = MarkLabelBrushProxy.Background,
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = text, FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = TextBrushProxy.Background,
            },
        };
        label.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        double left = anchorLeft ? x : alignRight ? x - label.DesiredSize.Width : x - label.DesiredSize.Width / 2;
        left = Math.Clamp(left, 0, Math.Max(0, PlotCanvas.ActualWidth - label.DesiredSize.Width));
        double top = Math.Max(0, y - label.DesiredSize.Height - 3);
        Canvas.SetLeft(label, left);
        Canvas.SetTop(label, top);
        _marksLayer!.Children.Add(label);
        return new Windows.Foundation.Rect(left, top, label.DesiredSize.Width, label.DesiredSize.Height);
    }
}

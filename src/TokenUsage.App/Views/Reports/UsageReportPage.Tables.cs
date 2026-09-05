using System.Diagnostics;
using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TokenUsage.App.Controls;
using TokenUsage.App.ViewModels.Reports;
using Windows.Foundation;

namespace TokenUsage.App.Views.Reports;

public sealed partial class UsageReportPage
{
    private readonly Dictionary<string, (double Offset, long Started)> _rowMotions = new(StringComparer.Ordinal);

    private void OnSortClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag }) return;
        string[] parts = tag.Split(':');
        if (parts.Length != 2 || !Enum.TryParse(parts[0], out UsageReportBreakdown table)
            || !Enum.TryParse(parts[1], out ReportSortColumn column)) return;
        ItemsRepeater repeater = table switch
        {
            UsageReportBreakdown.Source => SourceBreakdownRows,
            UsageReportBreakdown.Day => DayBreakdownRows,
            _ => ModelBreakdownRows,
        };
        bool animate = MotionSettings.AreAnimationsEnabled();
        long now = Stopwatch.GetTimestamp();
        var previous = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (FrameworkElement row in RealizedRows(repeater))
        {
            if (row.Tag is not string id) continue;
            double y = row.TransformToVisual(repeater).TransformPoint(new Point()).Y;
            string key = table + ":" + id;
            if (_rowMotions.TryGetValue(key, out var motion))
            {
                double t = Math.Clamp(Stopwatch.GetElapsedTime(motion.Started, now).TotalMilliseconds / 240, 0, 1);
                y += motion.Offset * Math.Pow(1 - t, 3);
            }
            previous[id] = y;
        }

        ViewModel.Sort(table, column);
        repeater.UpdateLayout();
        foreach (FrameworkElement row in RealizedRows(repeater))
        {
            if (row.Tag is not string id) continue;
            string key = table + ":" + id;
            var visual = ElementCompositionPreview.GetElementVisual(row);
            ElementCompositionPreview.SetIsTranslationEnabled(row, true);
            double y = row.TransformToVisual(repeater).TransformPoint(new Point()).Y;
            double offset = previous.TryGetValue(id, out double oldY) ? oldY - y : 0;
            visual.StopAnimation("Translation.Y");
            visual.Properties.InsertVector3("Translation", Vector3.Zero);
            if (!animate || Math.Abs(offset) < 0.5)
            {
                _rowMotions.Remove(key);
                continue;
            }
            var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
            animation.Duration = MotionSettings.ReportSwitchDuration;
            animation.InsertKeyFrame(0, (float)offset);
            animation.InsertKeyFrame(1, 0, visual.Compositor.CreateCubicBezierEasingFunction(
                new Vector2(1f / 3, 1), new Vector2(2f / 3, 1)));
            visual.StartAnimation("Translation.Y", animation);
            _rowMotions[key] = (offset, now);
        }
        UpdateSortHeaders();
    }

    private static IEnumerable<FrameworkElement> RealizedRows(ItemsRepeater repeater)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(repeater); i++)
            if (VisualTreeHelper.GetChild(repeater, i) is FrameworkElement { Tag: string } row) yield return row;
    }

    private void UpdateSortHeaders() => Visit(ReportCaptureRoot);

    private void Visit(DependencyObject parent)
    {
        if (parent is Button { Tag: string tag, Content: StackPanel content } header)
        {
            string[] parts = tag.Split(':');
            if (parts.Length == 2 && Enum.TryParse(parts[0], out UsageReportBreakdown table)
                && Enum.TryParse(parts[1], out ReportSortColumn column)
                && content.Children.Count == 2 && content.Children[0] is TextBlock title
                && content.Children[1] is TextBlock arrow)
            {
                ReportSortState sort = ViewModel.GetSort(table);
                bool active = sort.Column == column;
                arrow.Text = active ? sort.Descending ? "↓" : "↑" : "";
                string state = GetString(active
                    ? sort.Descending ? "UsageReportSortDescending" : "UsageReportSortAscending"
                    : "UsageReportSortNone");
                AutomationProperties.SetName(header, title.Text);
                AutomationProperties.SetItemStatus(header, state);
                ToolTipService.SetToolTip(header, title.Text + " · " + state);
            }
        }
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) Visit(VisualTreeHelper.GetChild(parent, i));
    }

    private void OnTableRowPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Control row) VisualStateManager.GoToState(row, "PointerOver", false);
    }

    private void OnTableRowPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Control row) VisualStateManager.GoToState(row, "Normal", false);
    }
}

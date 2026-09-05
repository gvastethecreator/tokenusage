using System.Numerics;
using Microsoft.UI.Composition;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TokenUsage.App.ViewModels.Reports;
using TokenUsage.Core.Appearance;
using Windows.System;

namespace TokenUsage.App.Controls;

public sealed partial class UsageTrendChart
{
    private DispatcherQueueTimer? _hoverTimer;
    private int? _hoverIndex;
    private int? _pendingHoverIndex;
    private int? _displayedHoverIndex;
    private UsageReportTrendDataset? _hoverContentData;
    private TextBlock? _hoverDate;
    private TextBlock? _hoverTotal;
    private readonly List<TextBlock> _hoverAmounts = [];
    private const int MaximumHoverRows = 8;

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (Data.Days.Count == 0 || PlotCanvas.ActualWidth <= 0) return;
        double x = e.GetCurrentPoint(PlotCanvas).Position.X;
        double fraction = Math.Clamp(x / PlotCanvas.ActualWidth, 0, 1);
        int index = Data.Style is ReportChartStyle.Bars or ReportChartStyle.TwoHourBars
            ? Math.Min(Data.Days.Count - 1, (int)(fraction * Data.Days.Count))
            : (int)Math.Round(fraction * (Data.Days.Count - 1));
        _hoverIndex = index; // Logical selection is immediate; only the visual movement is coalesced.
        _pendingHoverIndex = index;
        if (!MotionSettings.AreAnimationsEnabled())
        {
            CancelPendingHover();
            ShowHover(index, animate: false);
            return;
        }
        if (_hoverTimer is null)
        {
            _hoverTimer = DispatcherQueue.CreateTimer();
            _hoverTimer.Interval = TimeSpan.FromMilliseconds(40);
            _hoverTimer.IsRepeating = false;
            _hoverTimer.Tick += (_, _) =>
            {
                int? pending = _pendingHoverIndex;
                _pendingHoverIndex = null;
                if (pending is int next && IsLoaded) ShowHover(next, animate: true);
            };
        }
        if (!_hoverTimer.IsRunning) _hoverTimer.Start();
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e) => HideHover();

    private void OnGotFocus(object sender, RoutedEventArgs e)
    {
        if (Data.Days.Count > 0)
        {
            CancelPendingHover();
            ShowHover(_hoverIndex ?? Data.Days.Count - 1, animate: false);
        }
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape) { HideHover(); e.Handled = true; return; }
        if (Data.Days.Count == 0) return;
        int current = _hoverIndex ?? Data.Days.Count - 1;
        int next = e.Key switch
        {
            VirtualKey.Left => Math.Max(0, current - 1),
            VirtualKey.Right => Math.Min(Data.Days.Count - 1, current + 1),
            VirtualKey.Home => 0,
            VirtualKey.End => Data.Days.Count - 1,
            _ => current,
        };
        if (e.Key is VirtualKey.Left or VirtualKey.Right or VirtualKey.Home or VirtualKey.End)
        {
            CancelPendingHover();
            ShowHover(next, animate: false);
            e.Handled = true;
        }
    }

    private void ShowHover(int index, bool animate)
    {
        if (index < 0 || index >= Data.Days.Count || PlotCanvas.ActualWidth <= 0) return;
        _hoverIndex = index;
        if (!UsageTrendGeometry.ShouldRefreshHover(_displayedHoverIndex, index,
            HoverCard.Visibility == Visibility.Visible)) return;
        bool wasVisible = HoverCard.Visibility == Visibility.Visible;
        _displayedHoverIndex = index;
        BuildHoverContent(index);
        double x = Data.Style is ReportChartStyle.Bars or ReportChartStyle.TwoHourBars || Data.Days.Count == 1
            ? (index + 0.5) * PlotCanvas.ActualWidth / Data.Days.Count
            : index * PlotCanvas.ActualWidth / (Data.Days.Count - 1);
        double absoluteX = GetAxisWidth() + GetAxisGap() + x;
        double cardX = absoluteX > ActualWidth * 0.62
            ? absoluteX - HoverCard.Width - 8 : absoluteX + 8;
        HoverTransform.Y = 8;
        HoverCard.Visibility = Visibility.Visible;
        if (_crosshair is not null)
        {
            _crosshair.Visibility = Visibility.Visible;
            MoveVisual(_crosshair, x, animate && wasVisible);
        }
        MoveVisual(HoverCard, Math.Clamp(cardX, 0, Math.Max(0, ActualWidth - HoverCard.Width)),
            animate && wasVisible);
    }

    private static void MoveVisual(UIElement element, double x, bool animate)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        Visual visual = ElementCompositionPreview.GetElementVisual(element);
        if (!animate || !MotionSettings.AreAnimationsEnabled())
        {
            visual.StopAnimation("Translation.X");
            visual.Properties.InsertVector3("Translation", new Vector3((float)x, 0, 0));
            return;
        }
        var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
        animation.Duration = MotionSettings.ChartHoverDuration;
        animation.StopBehavior = AnimationStopBehavior.SetToFinalValue;
        animation.InsertExpressionKeyFrame(0, "this.StartingValue");
        animation.InsertKeyFrame(1, (float)x,
            visual.Compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1), new Vector2(0.3f, 1)));
        // Replacing this animation samples its current composited value; no stop/reset or queued storyboards.
        visual.StartAnimation("Translation.X", animation);
    }

    private void BuildHoverContent(int index)
    {
        if (!ReferenceEquals(_hoverContentData, Data))
        {
            _hoverContentData = Data;
            HoverContent.Children.Clear();
            _hoverAmounts.Clear();
            _hoverDate = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.WrapWholeWords };
            HoverContent.Children.Add(_hoverDate);
            foreach (var series in Data.Series.Take(MaximumHoverRows))
            {
                Grid row = CreateHoverRow(series, "");
                _hoverAmounts.Add((TextBlock)row.Children[^1]);
                HoverContent.Children.Add(row);
            }
            if (Data.Series.Count > MaximumHoverRows)
                HoverContent.Children.Add(new TextBlock
                {
                    Text = string.Format(System.Globalization.CultureInfo.CurrentCulture,
                        GetString("UsageReportMoreModelsFormat"), Data.Series.Count - MaximumHoverRows),
                    FontSize = 11, TextWrapping = TextWrapping.WrapWholeWords,
                });
            if (!Data.IsComparison)
            {
                HoverContent.Children.Add(new Border { Height = 1, Background = GridBrushProxy.Background });
                _hoverTotal = new TextBlock { FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
                HoverContent.Children.Add(_hoverTotal);
            }
            else _hoverTotal = null;
        }
        _hoverDate!.Text = Data.Days[index].HoverText
            ?? Data.Days[index].Date.ToString("D", System.Globalization.CultureInfo.CurrentCulture);
        double total = 0;
        bool hasUnknown = false;
        for (int i = 0; i < Data.Series.Count; i++)
        {
            double value = index < Data.Series[i].Values.Count ? Data.Series[i].Values[index] : 0;
            if (double.IsFinite(value)) total += value; else hasUnknown = true;
            if (i < _hoverAmounts.Count) _hoverAmounts[i].Text = FormatValue(value, Data.Metric);
        }
        if (_hoverTotal is not null)
            _hoverTotal.Text = GetString("UsageReportChartTotal") + "  " + FormatValue(total, Data.Metric)
                + (hasUnknown ? " · " + GetString("UsageReportKnownOnly") : "");
        AutomationProperties.SetHelpText(this, _hoverDate.Text + ". " + string.Join(". ",
            Data.Series.Select((series, i) => series.Name + ": "
                + FormatValue(index < series.Values.Count ? series.Values[index] : 0, Data.Metric))));
    }

    private Grid CreateHoverRow(UsageReportTrendSeries series, string value)
    {
        var row = new Grid { ColumnSpacing = 7 };
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.Children.Add(new Border
        {
            Width = 8, Height = 8, CornerRadius = new CornerRadius(2),
            Background = SeriesBrush(series), VerticalAlignment = VerticalAlignment.Center,
        });
        FrameworkElement mark = series.IsReserve ? new TablerIcon { Kind = "moon", Width = 14, Height = 14 }
            : new ProviderMarkImage { ProviderId = series.ProviderId, Width = 14, Height = 14 };
        Grid.SetColumn(mark, 1);
        row.Children.Add(mark);
        var name = new TextBlock
        {
            Text = series.Name, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(name, series.Name);
        Grid.SetColumn(name, 2);
        row.Children.Add(name);
        var amount = new TextBlock
        {
            Text = value, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(amount, 3);
        row.Children.Add(amount);
        return row;
    }

    private void CancelPendingHover()
    {
        _hoverTimer?.Stop();
        _pendingHoverIndex = null;
    }

    private void HideHover()
    {
        CancelPendingHover();
        _displayedHoverIndex = null;
        _hoverIndex = null;
        if (HoverCard.Visibility == Visibility.Visible)
            ElementCompositionPreview.GetElementVisual(HoverCard).StopAnimation("Translation.X");
        HoverCard.Visibility = Visibility.Collapsed;
        if (_crosshair is not null)
        {
            if (_crosshair.Visibility == Visibility.Visible)
                ElementCompositionPreview.GetElementVisual(_crosshair).StopAnimation("Translation.X");
            _crosshair.Visibility = Visibility.Collapsed;
        }
    }
}

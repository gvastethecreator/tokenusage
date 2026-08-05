using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using TokenUsage.App.ViewModels.Sample;
using Windows.UI.ViewManagement;

using TokenUsage.App.ViewModels.Dashboard;

namespace TokenUsage.App.Controls;

public sealed partial class UsageHeatmap : UserControl
{
    private const int ColumnCount = 7;
    private readonly AccessibilitySettings _accessibilitySettings = new();
    private readonly List<CellVisual> _visuals = [];
    private Storyboard? _storyboard;
    private int _lastRevealToken = int.MinValue;

    public static readonly DependencyProperty DataProperty =
        DependencyProperty.Register(
            nameof(Data),
            typeof(UsageHeatmapModel),
            typeof(UsageHeatmap),
            new PropertyMetadata(UsageHeatmapModel.Empty, OnDataChanged));

    public UsageHeatmap()
    {
        InitializeComponent();
        ActualThemeChanged += OnActualThemeChanged;
        Unloaded += OnUnloaded;
    }

    public UsageHeatmapModel Data
    {
        get => (UsageHeatmapModel)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public void PlayReveal(int token)
    {
        if (token == _lastRevealToken)
        {
            return;
        }

        _lastRevealToken = token;
        _storyboard?.Stop();
        _storyboard = null;
        if (!MotionSettings.AreAnimationsEnabled())
        {
            foreach (CellVisual visual in _visuals)
            {
                visual.Element.Opacity = visual.TargetOpacity;
            }

            return;
        }

        var storyboard = new Storyboard();
        for (int index = 0; index < _visuals.Count; index++)
        {
            CellVisual visual = _visuals[index];
            visual.Element.Opacity = 0;
            var animation = new DoubleAnimation
            {
                To = visual.TargetOpacity,
                BeginTime = TimeSpan.FromMilliseconds(index * 10),
                Duration = TimeSpan.FromMilliseconds(180),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                EnableDependentAnimation = true,
            };
            Storyboard.SetTarget(animation, visual.Element);
            Storyboard.SetTargetProperty(animation, nameof(Opacity));
            storyboard.Children.Add(animation);
        }

        _storyboard = storyboard;
        storyboard.Begin();
    }

    private static void OnDataChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args) =>
        ((UsageHeatmap)dependencyObject).BuildCells();

    private void OnActualThemeChanged(FrameworkElement sender, object args) => BuildCells();

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _storyboard?.Stop();
        _storyboard = null;
    }

    private void BuildCells()
    {
        if (CellGrid is null)
        {
            return;
        }

        _storyboard?.Stop();
        _visuals.Clear();
        CellGrid.Children.Clear();
        CellGrid.ColumnDefinitions.Clear();
        CellGrid.RowDefinitions.Clear();
        for (int column = 0; column < ColumnCount; column++)
        {
            CellGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        }

        int rowCount = (int)Math.Ceiling(Data.Cells.Count / (double)ColumnCount);
        for (int row = 0; row < rowCount; row++)
        {
            CellGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(20) });
        }

        bool highContrast = _accessibilitySettings.HighContrast;
        for (int index = 0; index < Data.Cells.Count; index++)
        {
            UsageHeatmapCell cell = Data.Cells[index];
            double targetOpacity = highContrast || cell.Level == 0
                ? 1
                : 0.3 + (cell.Level * 0.175);
            var element = new Border
            {
                Width = 16,
                Height = 16,
                Margin = new Thickness(2),
                CornerRadius = new CornerRadius(3),
                Background = cell.HasActivity
                    ? ActiveBrushProxy.Background
                    : EmptyBrushProxy.Background,
                BorderBrush = StrokeBrushProxy.Background,
                BorderThickness = new Thickness(highContrast ? 1 : 0.5),
                Opacity = targetOpacity,
                IsHitTestVisible = true,
            };
            AutomationProperties.SetAccessibilityView(element, AccessibilityView.Content);
            AutomationProperties.SetAutomationId(element, cell.AutomationId);
            AutomationProperties.SetName(element, cell.AccessibleName);
            ToolTipService.SetToolTip(element, cell.AccessibleName);
            Grid.SetColumn(element, index % ColumnCount);
            Grid.SetRow(element, index / ColumnCount);
            CellGrid.Children.Add(element);
            _visuals.Add(new CellVisual(element, targetOpacity));
        }
    }

    private sealed record CellVisual(Border Element, double TargetOpacity);
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System.Globalization;
using TokenUsage.App.ViewModels.Sample;
using Windows.UI.ViewManagement;

using TokenUsage.App.ViewModels.Dashboard;

namespace TokenUsage.App.Controls;

public sealed class UsageHeatmapDayInvokedEventArgs : EventArgs
{
    public UsageHeatmapDayInvokedEventArgs(UsageHeatmapCell cell) =>
        Cell = cell ?? throw new ArgumentNullException(nameof(cell));

    public UsageHeatmapCell Cell { get; }
}

public sealed partial class UsageHeatmap : UserControl
{
    private const int ColumnCount = 7;
    private readonly AccessibilitySettings _accessibilitySettings = new();
    private readonly List<CellVisual> _visuals = [];
    private Storyboard? _storyboard;
    private ToolTip? _openToolTip;
    private int _lastRevealToken = int.MinValue;

    public static readonly DependencyProperty DataProperty =
        DependencyProperty.Register(
            nameof(Data),
            typeof(UsageHeatmapModel),
            typeof(UsageHeatmap),
            new PropertyMetadata(UsageHeatmapModel.Empty, OnDataChanged));

    public static readonly DependencyProperty CellWidthProperty =
        DependencyProperty.Register(
            nameof(CellWidth),
            typeof(double),
            typeof(UsageHeatmap),
            new PropertyMetadata(24d, OnLayoutChanged));

    public static readonly DependencyProperty CellHeightProperty =
        DependencyProperty.Register(
            nameof(CellHeight),
            typeof(double),
            typeof(UsageHeatmap),
            new PropertyMetadata(24d, OnLayoutChanged));

    public static readonly DependencyProperty ShowDayLabelsProperty =
        DependencyProperty.Register(
            nameof(ShowDayLabels),
            typeof(bool),
            typeof(UsageHeatmap),
            new PropertyMetadata(false, OnLayoutChanged));

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

    public double CellWidth
    {
        get => (double)GetValue(CellWidthProperty);
        set => SetValue(CellWidthProperty, value);
    }

    public double CellHeight
    {
        get => (double)GetValue(CellHeightProperty);
        set => SetValue(CellHeightProperty, value);
    }

    public bool ShowDayLabels
    {
        get => (bool)GetValue(ShowDayLabelsProperty);
        set => SetValue(ShowDayLabelsProperty, value);
    }

    public event EventHandler<UsageHeatmapDayInvokedEventArgs>? DayInvoked;

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

    private static void OnLayoutChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args) =>
        ((UsageHeatmap)dependencyObject).BuildCells();

    private void OnActualThemeChanged(FrameworkElement sender, object args) => BuildCells();

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _storyboard?.Stop();
        _storyboard = null;
        CloseOpenToolTip();
    }

    private void BuildCells()
    {
        if (CellGrid is null)
        {
            return;
        }

        _storyboard?.Stop();
        CloseOpenToolTip();
        _visuals.Clear();
        CellGrid.Children.Clear();
        CellGrid.ColumnDefinitions.Clear();
        CellGrid.RowDefinitions.Clear();
        for (int column = 0; column < ColumnCount; column++)
        {
            CellGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CellWidth) });
        }

        int rowCount = (int)Math.Ceiling(Data.Cells.Count / (double)ColumnCount);
        for (int row = 0; row < rowCount; row++)
        {
            CellGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(CellHeight) });
        }

        bool highContrast = _accessibilitySettings.HighContrast;
        for (int index = 0; index < Data.Cells.Count; index++)
        {
            UsageHeatmapCell cell = Data.Cells[index];
            double targetOpacity = highContrast || cell.Level == 0
                ? 1
                : 0.3 + (cell.Level * 0.175);
            var swatch = new Border
            {
                Width = Math.Max(6, CellWidth - 4),
                Height = Math.Max(6, CellHeight - 4),
                CornerRadius = new CornerRadius(4),
                Background = cell.HasActivity
                    ? ActiveBrushProxy.Background
                    : CreateInactiveBrush(),
                BorderBrush = StrokeBrushProxy.Background,
                BorderThickness = new Thickness(highContrast ? 1 : 0.5),
                IsHitTestVisible = true,
            };
            if (ShowDayLabels)
            {
                var content = new Grid();
                content.Children.Add(new TextBlock
                {
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(7, 5, 4, 3),
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                    FontSize = 11,
                    Foreground = cell.HasActivity
                        ? ActiveTextBrushProxy.Background
                        : EmptyTextBrushProxy.Background,
                    Text = cell.Date.Day == 1
                        ? cell.Date.ToString("d MMM", CultureInfo.CurrentCulture)
                        : cell.Date.Day.ToString(CultureInfo.CurrentCulture),
                });

                if (cell.ActiveProviderIds.Count > 0)
                {
                    var providers = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 3,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Bottom,
                        Margin = new Thickness(7, 0, 4, 5),
                    };
                    foreach (string providerId in cell.ActiveProviderIds.Take(4))
                    {
                        providers.Children.Add(new ProviderColorSwatch
                        {
                            Width = 5,
                            Height = 5,
                            ProviderId = providerId,
                        });
                    }

                    content.Children.Add(providers);
                }

                swatch.Child = content;
            }
            var element = new Button
            {
                Width = CellWidth,
                Height = CellHeight,
                Padding = new Thickness(2),
                Background = null,
                BorderThickness = new Thickness(0),
                Content = swatch,
                CornerRadius = new CornerRadius(3),
                Opacity = targetOpacity,
                IsTabStop = true,
                UseSystemFocusVisuals = true,
            };
            AutomationProperties.SetAccessibilityView(element, AccessibilityView.Content);
            AutomationProperties.SetAutomationId(element, cell.AutomationId);
            AutomationProperties.SetName(element, cell.AccessibleName);
            AutomationProperties.SetHelpText(element, cell.AccessibleName);
            var toolTip = new ToolTip
            {
                Placement = PlacementMode.Top,
                Content = CreateToolTipContent(cell),
            };
            ToolTipService.SetToolTip(element, toolTip);
            bool pointerOver = false;
            bool hasFocus = false;

            void OpenToolTip()
            {
                CloseOpenToolTip();
                _openToolTip = toolTip;
                toolTip.IsOpen = true;
                swatch.BorderThickness = new Thickness(2);
                element.Opacity = 1;
            }

            void CloseToolTipIfInactive()
            {
                if (pointerOver || hasFocus)
                {
                    return;
                }

                toolTip.IsOpen = false;
                if (ReferenceEquals(_openToolTip, toolTip))
                {
                    _openToolTip = null;
                }

                swatch.BorderThickness = new Thickness(highContrast ? 1 : 0.5);
                element.Opacity = targetOpacity;
            }

            element.PointerEntered += (_, _) =>
            {
                pointerOver = true;
                OpenToolTip();
            };
            element.PointerExited += (_, _) =>
            {
                pointerOver = false;
                CloseToolTipIfInactive();
            };
            element.GotFocus += (_, _) =>
            {
                hasFocus = true;
                OpenToolTip();
            };
            element.LostFocus += (_, _) =>
            {
                hasFocus = false;
                CloseToolTipIfInactive();
            };
            element.Click += (_, _) =>
                DayInvoked?.Invoke(this, new UsageHeatmapDayInvokedEventArgs(cell));
            Grid.SetColumn(element, index % ColumnCount);
            Grid.SetRow(element, index / ColumnCount);
            CellGrid.Children.Add(element);
            _visuals.Add(new CellVisual(element, targetOpacity));
        }
    }

    private FrameworkElement CreateToolTipContent(UsageHeatmapCell cell)
    {
        if (cell.Tooltip is null)
        {
            return new TextBlock
            {
                MaxWidth = 320,
                Text = string.IsNullOrWhiteSpace(cell.TooltipText)
                    ? cell.AccessibleName
                    : cell.TooltipText,
                TextWrapping = TextWrapping.WrapWholeWords,
            };
        }

        var content = new StackPanel
        {
            MinWidth = 270,
            MaxWidth = 340,
            Spacing = 8,
        };
        content.Children.Add(new TextBlock
        {
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Text = cell.Tooltip.Title,
        });

        int summaryRowCount = Math.Min(3, cell.Tooltip.Rows.Count);
        content.Children.Add(CreateToolTipRows(cell.Tooltip.Rows.Take(summaryRowCount)));
        if (cell.Tooltip.Rows.Count > summaryRowCount)
        {
            content.Children.Add(new Border
            {
                Height = 1,
                Background = StrokeBrushProxy.Background,
            });
            content.Children.Add(CreateToolTipRows(cell.Tooltip.Rows.Skip(summaryRowCount)));
        }

        return content;
    }

    private Grid CreateToolTipRows(IEnumerable<UsageHeatmapTooltipRow> rows)
    {
        var grid = new Grid
        {
            ColumnSpacing = 18,
            RowSpacing = 5,
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        });

        int rowIndex = 0;
        foreach (UsageHeatmapTooltipRow row in rows)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = new TextBlock
            {
                Foreground = SecondaryTextBrushProxy.Background,
                Text = row.Label,
            };
            var value = new TextBlock
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                Text = row.Value,
                TextAlignment = TextAlignment.Right,
                TextWrapping = TextWrapping.WrapWholeWords,
            };
            Grid.SetRow(label, rowIndex);
            Grid.SetRow(value, rowIndex);
            Grid.SetColumn(value, 1);
            grid.Children.Add(label);
            grid.Children.Add(value);
            rowIndex++;
        }

        return grid;
    }

    private void CloseOpenToolTip()
    {
        if (_openToolTip is null)
        {
            return;
        }

        _openToolTip.IsOpen = false;
        _openToolTip = null;
    }

    private Brush CreateInactiveBrush()
    {
        if (EmptyBrushProxy.Background is not SolidColorBrush empty
            || StrokeBrushProxy.Background is not SolidColorBrush stroke)
        {
            return EmptyBrushProxy.Background;
        }

        var brush = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(1, 1),
        };
        for (int stripe = 0; stripe < 5; stripe++)
        {
            double start = stripe / 5d;
            double lineEnd = Math.Min(1d, start + 0.035d);
            double fillEnd = Math.Min(1d, start + 0.2d);
            brush.GradientStops.Add(new GradientStop { Color = stroke.Color, Offset = start });
            brush.GradientStops.Add(new GradientStop { Color = stroke.Color, Offset = lineEnd });
            brush.GradientStops.Add(new GradientStop { Color = empty.Color, Offset = lineEnd });
            brush.GradientStops.Add(new GradientStop { Color = empty.Color, Offset = fillEnd });
        }

        return brush;
    }

    private sealed record CellVisual(Button Element, double TargetOpacity);
}

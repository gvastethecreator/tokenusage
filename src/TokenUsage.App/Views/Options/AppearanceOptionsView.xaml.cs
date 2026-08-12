using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TokenUsage.App.ViewModels.Surfaces;

namespace TokenUsage.App.Views.Options;

public sealed partial class AppearanceOptionsView : UserControl
{
    private AppearanceSurfaceViewModel? _viewModel;
    private PersonalizationSurfaceViewModel? _personalizationViewModel;
    private bool _isInitialized;

    public AppearanceOptionsView()
    {
        InitializeComponent();
        Loaded += (_, _) => ApplyLayout(ActualWidth);
        SizeChanged += (_, args) => ApplyLayout(args.NewSize.Width);
        _isInitialized = true;
    }

    public AppearanceSurfaceViewModel? ViewModel
    {
        get => _viewModel;
        set
        {
            _viewModel = value ?? throw new ArgumentNullException(nameof(value));
            if (_isInitialized)
            {
                Bindings.Update();
            }
        }
    }

    public PersonalizationSurfaceViewModel? PersonalizationViewModel
    {
        get => _personalizationViewModel;
        set
        {
            _personalizationViewModel = value ?? throw new ArgumentNullException(nameof(value));
            if (_isInitialized)
            {
                Bindings.Update();
            }
        }
    }

    public UIElement PrimaryAction => AppearanceThemeSelector;

    public UIElement PersonalizationPrimaryAction => PersonalizationView.PrimaryAction;

    public void ApplyLayout(double width)
    {
        bool wide = width >= 360d;
        Position(AppearanceThemeGroup, row: 0, column: 0, columnSpan: wide ? 1 : 2);
        Position(
            AppearanceDensityGroup,
            row: wide ? 0 : 1,
            column: wide ? 1 : 0,
            columnSpan: wide ? 1 : 2);
        Position(
            AppearanceUsageGroup,
            row: wide ? 1 : 3,
            column: 0,
            columnSpan: wide ? 1 : 2);
        Position(
            AppearanceResetGroup,
            row: wide ? 1 : 4,
            column: wide ? 1 : 0,
            columnSpan: wide ? 1 : 2);
        Position(
            AppearanceVisualizationGroup,
            row: wide ? 2 : 5,
            column: 0,
            columnSpan: wide ? 1 : 2);
        Position(
            AppearanceTransparencyGroup,
            row: wide ? 2 : 2,
            column: wide ? 1 : 0,
            columnSpan: wide ? 1 : 2);
    }

    private static void Position(
        FrameworkElement element,
        int row,
        int column,
        int columnSpan)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        Grid.SetColumnSpan(element, columnSpan);
        element.Margin = row == 0
            ? new Thickness(0)
            : new Thickness(0, 9, 0, 0);
    }
}

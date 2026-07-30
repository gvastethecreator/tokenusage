using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WOpenUsage.App.ViewModels.Surfaces;

namespace WOpenUsage.App.Views.Options;

public sealed partial class AppearanceOptionsView : UserControl
{
    private AppearanceSurfaceViewModel? _viewModel;
    private bool _isInitialized;

    public AppearanceOptionsView()
    {
        InitializeComponent();
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

    public UIElement PrimaryAction => AppearanceThemeSelector;

    public void ApplyLayout(double width) =>
        _ = VisualStateManager.GoToState(
            this,
            width >= 360d ? "WideAppearanceLayout" : "NarrowAppearanceLayout",
            useTransitions: false);
}

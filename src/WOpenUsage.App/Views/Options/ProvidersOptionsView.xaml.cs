using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WOpenUsage.App.ViewModels.Surfaces;

namespace WOpenUsage.App.Views.Options;

public sealed partial class ProvidersOptionsView : UserControl
{
    private OptionsNavigationViewModel? _viewModel;
    private bool _isInitialized;

    public ProvidersOptionsView()
    {
        InitializeComponent();
        _isInitialized = true;
    }

    public OptionsNavigationViewModel? ViewModel
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

    public UIElement PrimaryAction => OptionsProviderStatusButton;
}

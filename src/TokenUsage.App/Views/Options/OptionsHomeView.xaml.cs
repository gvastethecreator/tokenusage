using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TokenUsage.App.ViewModels.Surfaces;

namespace TokenUsage.App.Views.Options;

public sealed partial class OptionsHomeView : UserControl
{
    private OptionsNavigationViewModel? _viewModel;
    private bool _isInitialized;

    public OptionsHomeView()
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

    public UIElement PrimaryAction => OptionsGeneralButton;
}

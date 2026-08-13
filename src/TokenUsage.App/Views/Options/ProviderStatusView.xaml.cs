using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TokenUsage.App.ViewModels.Surfaces;

namespace TokenUsage.App.Views.Options;

public sealed partial class ProviderStatusView : UserControl
{
    private ProviderStatusSurfaceViewModel? _viewModel;
    private bool _isInitialized;

    public ProviderStatusView()
    {
        InitializeComponent();
        _isInitialized = true;
    }

    public ProviderStatusSurfaceViewModel? ViewModel
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

    public UIElement PrimaryAction => ProviderStatusRefreshButton;

    private void OnProviderStatusMoreButtonClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null)
        {
            ViewModel.IsAdditionalProvidersExpanded =
                !ViewModel.IsAdditionalProvidersExpanded;
        }
    }

    private void OnCredentialEditorLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is ProviderCredentialEditor editor)
        {
            editor.Host = ViewModel;
        }
    }
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WOpenUsage.App.ViewModels;
using WOpenUsage.App.ViewModels.Surfaces;
using WOpenUsage.Core.Appearance;

namespace WOpenUsage.App.Views.Options;

public sealed partial class OptionsView : UserControl
{
    private OptionsSurfaceViewModel? _viewModel;
    private bool _isInitialized;

    public OptionsView()
    {
        InitializeComponent();
        _isInitialized = true;
    }

    public OptionsSurfaceViewModel? ViewModel
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

    public UIElement GetPrimaryAction(OptionsSection section) => section switch
    {
        OptionsSection.General => GeneralView.PrimaryAction,
        OptionsSection.Appearance => AppearanceView.PrimaryAction,
        OptionsSection.Personalization => PersonalizationView.PrimaryAction,
        OptionsSection.Providers => ProvidersView.PrimaryAction,
        OptionsSection.ProviderStatus => ProviderStatusView.PrimaryAction,
        _ => HomeView.PrimaryAction,
    };

    public void ApplyAppearance(AppearanceSettings settings, double width)
    {
        ArgumentNullException.ThrowIfNull(settings);
        OptionsStack.Spacing = settings.Density == AppDensityMode.Compact ? 8 : 12;
        AppearanceView.ApplyLayout(width);
    }
}

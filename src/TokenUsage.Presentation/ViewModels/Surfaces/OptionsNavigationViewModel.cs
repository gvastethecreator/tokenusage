using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TokenUsage.App.ViewModels.Surfaces;

public sealed partial class OptionsNavigationViewModel : ObservableObject
{
    [ObservableProperty]
    public partial OptionsSection ActiveSection { get; private set; } = OptionsSection.Home;

    public event EventHandler? CloseRequested;

    public void Open() => ActiveSection = OptionsSection.Home;

    [RelayCommand]
    private void NavigateBack()
    {
        if (ActiveSection == OptionsSection.Home)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        ActiveSection = ActiveSection == OptionsSection.ProviderStatus
            ? OptionsSection.Providers
            : OptionsSection.Home;
    }

    [RelayCommand]
    private void ShowProviders() => ActiveSection = OptionsSection.Providers;

    [RelayCommand]
    private void ShowProviderStatus() => ActiveSection = OptionsSection.ProviderStatus;
}

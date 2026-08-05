using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TokenUsage.App.ViewModels.Surfaces;

public sealed partial class OptionsNavigationViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHome))]
    [NotifyPropertyChangedFor(nameof(IsGeneral))]
    [NotifyPropertyChangedFor(nameof(IsAppearance))]
    [NotifyPropertyChangedFor(nameof(IsPersonalization))]
    [NotifyPropertyChangedFor(nameof(IsProviders))]
    [NotifyPropertyChangedFor(nameof(IsProviderStatus))]
    public partial OptionsSection ActiveSection { get; private set; } = OptionsSection.Home;

    public event EventHandler? CloseRequested;

    public bool IsHome => ActiveSection == OptionsSection.Home;

    public bool IsGeneral => ActiveSection == OptionsSection.General;

    public bool IsAppearance => ActiveSection == OptionsSection.Appearance;

    public bool IsPersonalization => ActiveSection == OptionsSection.Personalization;

    public bool IsProviders => ActiveSection == OptionsSection.Providers;

    public bool IsProviderStatus => ActiveSection == OptionsSection.ProviderStatus;

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
    private void ShowGeneral() => ActiveSection = OptionsSection.General;

    [RelayCommand]
    private void ShowAppearance() => ActiveSection = OptionsSection.Appearance;

    [RelayCommand]
    private void ShowPersonalization() => ActiveSection = OptionsSection.Personalization;

    [RelayCommand]
    private void ShowProviders() => ActiveSection = OptionsSection.Providers;

    [RelayCommand]
    private void ShowProviderStatus() => ActiveSection = OptionsSection.ProviderStatus;
}

namespace TokenUsage.App.ViewModels.Surfaces;

public sealed class OptionsSurfaceViewModel
{
    public OptionsSurfaceViewModel(
        OptionsNavigationViewModel navigation,
        GeneralOptionsViewModel general,
        AppearanceSurfaceViewModel appearance,
        PersonalizationSurfaceViewModel personalization,
        ProviderStatusSurfaceViewModel providerStatus)
    {
        Navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        General = general ?? throw new ArgumentNullException(nameof(general));
        Appearance = appearance ?? throw new ArgumentNullException(nameof(appearance));
        Personalization = personalization
            ?? throw new ArgumentNullException(nameof(personalization));
        ProviderStatus = providerStatus ?? throw new ArgumentNullException(nameof(providerStatus));
    }

    public OptionsNavigationViewModel Navigation { get; }

    public GeneralOptionsViewModel General { get; }

    public AppearanceSurfaceViewModel Appearance { get; }

    public PersonalizationSurfaceViewModel Personalization { get; }

    public ProviderStatusSurfaceViewModel ProviderStatus { get; }
}

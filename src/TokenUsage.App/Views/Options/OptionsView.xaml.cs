using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TokenUsage.App.ViewModels;
using TokenUsage.App.ViewModels.Surfaces;
using TokenUsage.Core.Appearance;
using Microsoft.Windows.ApplicationModel.Resources;
using Windows.ApplicationModel;

namespace TokenUsage.App.Views.Options;

public sealed partial class OptionsView : UserControl
{
    private readonly ResourceLoader _resources = new();
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
        OptionsSection.Personalization => AppearanceView.PersonalizationPrimaryAction,
        OptionsSection.ProviderStatus => ProviderStatusView.PrimaryAction,
        _ => GeneralView.PrimaryAction,
    };

    public string VersionText
    {
        get
        {
            PackageVersion version = Package.Current.Id.Version;
            return string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                GetString("AboutVersionFormat"),
                $"{version.Major}.{version.Minor}.{version.Build}");
        }
    }

    public void ApplyAppearance(AppearanceSettings settings, double width)
    {
        ArgumentNullException.ThrowIfNull(settings);
        OptionsStack.Spacing = settings.Density == AppDensityMode.Compact ? 8 : 12;
        AppearanceView.ApplyLayout(width);
    }

    private string GetString(string key)
    {
        string value = _resources.GetString(key);
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"The resource '{key}' is missing.")
            : value;
    }
}

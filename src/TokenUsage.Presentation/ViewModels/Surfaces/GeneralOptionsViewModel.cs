using CommunityToolkit.Mvvm.ComponentModel;
using TokenUsage.App.Localization;
using TokenUsage.App.ViewModels.Sample;

namespace TokenUsage.App.ViewModels.Surfaces;

public sealed partial class GeneralOptionsViewModel : ObservableObject
{
    private bool _isInitializing = true;
    private readonly Func<string, bool> _requiresLanguageRestart;

    public GeneralOptionsViewModel(
        Func<string, string> getString,
        string activeLanguageTag,
        Func<string, bool> requiresLanguageRestart)
    {
        ArgumentNullException.ThrowIfNull(getString);
        ArgumentException.ThrowIfNullOrWhiteSpace(activeLanguageTag);
        _requiresLanguageRestart = requiresLanguageRestart
            ?? throw new ArgumentNullException(nameof(requiresLanguageRestart));
        LanguageOptions =
        [
            new(AppLanguageCatalog.EnglishUnitedStates, getString("LanguageEnglish")),
            new(AppLanguageCatalog.SpanishSpain, getString("LanguageSpanish")),
        ];
        SampleScenarios =
        [
            new(SampleScenario.Normal, getString("SampleScenarioNormal")),
            new(SampleScenario.NearLimit, getString("SampleScenarioNearLimit")),
            new(SampleScenario.Partial, getString("SampleScenarioPartial")),
            new(SampleScenario.Stale, getString("SampleScenarioStale")),
            new(SampleScenario.Error, getString("SampleScenarioError")),
        ];
        SelectedLanguage = LanguageOptions.Single(option => string.Equals(
            option.LanguageTag,
            activeLanguageTag,
            StringComparison.OrdinalIgnoreCase));
        SelectedSampleScenario = SampleScenarios[0];
        _isInitializing = false;
    }

    public event EventHandler? SampleModeChanged;

    public event EventHandler? SampleScenarioChanged;

    public IReadOnlyList<AppLanguageOption> LanguageOptions { get; }

    public IReadOnlyList<SampleScenarioOption> SampleScenarios { get; }

    [ObservableProperty]
    public partial bool CloseWhenInactive { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSampleScenarioEnabled))]
    public partial bool IsSampleModeEnabled { get; set; }

    [ObservableProperty]
    public partial SampleScenarioOption SelectedSampleScenario { get; set; }

    [ObservableProperty]
    public partial AppLanguageOption SelectedLanguage { get; set; }

    [ObservableProperty]
    public partial bool IsLanguageRestartRequired { get; private set; }

    [ObservableProperty]
    public partial bool IsLanguageRestartErrorVisible { get; private set; }

    public bool IsSampleScenarioEnabled => IsSampleModeEnabled;

    public string PendingLanguageTag => SelectedLanguage.LanguageTag;

    public void ReportLanguageRestartFailure() => IsLanguageRestartErrorVisible = true;

    partial void OnSelectedLanguageChanged(AppLanguageOption value)
    {
        if (value is null)
        {
            return;
        }

        IsLanguageRestartRequired = _requiresLanguageRestart(value.LanguageTag);
        IsLanguageRestartErrorVisible = false;
    }

    partial void OnIsSampleModeEnabledChanged(bool value)
    {
        if (!_isInitializing)
        {
            SampleModeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    partial void OnSelectedSampleScenarioChanged(SampleScenarioOption value)
    {
        if (!_isInitializing && value is not null)
        {
            SampleScenarioChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

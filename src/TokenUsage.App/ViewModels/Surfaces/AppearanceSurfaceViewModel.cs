using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using TokenUsage.Core.Appearance;

namespace TokenUsage.App.ViewModels.Surfaces;

public sealed partial class AppearanceSurfaceViewModel : ObservableObject
{
    private readonly object _saveSync = new();
    private readonly AppearanceSession _session;
    private readonly Func<string, string> _getString;
    private Task _pendingSave = Task.CompletedTask;
    private int _saveVersion;
    private bool _isApplyingSettings;

    public AppearanceSurfaceViewModel(
        AppearanceSession session,
        Func<string, string> getString)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _getString = getString ?? throw new ArgumentNullException(nameof(getString));
        IsBusy = true;
        _isApplyingSettings = true;
        ThemeOptions =
        [
            new(AppThemeMode.System, _getString("AppearanceThemeSystem")),
            new(AppThemeMode.Light, _getString("AppearanceThemeLight")),
            new(AppThemeMode.Dark, _getString("AppearanceThemeDark")),
        ];
        DensityOptions =
        [
            new(AppDensityMode.Regular, _getString("AppearanceDensityRegular")),
            new(AppDensityMode.Compact, _getString("AppearanceDensityCompact")),
        ];
        UsageDisplayOptions =
        [
            new(UsageDisplayMode.Remaining, _getString("AppearanceUsageRemaining")),
            new(UsageDisplayMode.Used, _getString("AppearanceUsageUsed")),
        ];
        ResetTimeDisplayOptions =
        [
            new(ResetTimeDisplayMode.Relative, _getString("AppearanceResetRelative")),
            new(ResetTimeDisplayMode.Exact, _getString("AppearanceResetExact")),
        ];
        DashboardVisualizationOptions =
        [
            new(DashboardVisualizationMode.List, _getString("AppearanceVisualizationList")),
            new(DashboardVisualizationMode.Donut, _getString("AppearanceVisualizationDonut")),
            new(DashboardVisualizationMode.Heatmap, _getString("AppearanceVisualizationHeatmap")),
        ];
        SelectedTheme = ThemeOptions[0];
        SelectedDensity = DensityOptions[0];
        SelectedUsageDisplay = UsageDisplayOptions[0];
        SelectedResetTimeDisplay = ResetTimeDisplayOptions[0];
        SelectedDashboardVisualization = DashboardVisualizationOptions[0];
        _isApplyingSettings = false;
        Initialization = InitializeAsync();
    }

    public event EventHandler<AppearanceSettings>? SettingsChanged;

    public Task Initialization { get; }

    public IReadOnlyList<AppearanceOption<AppThemeMode>> ThemeOptions { get; }

    public IReadOnlyList<AppearanceOption<AppDensityMode>> DensityOptions { get; }

    public IReadOnlyList<AppearanceOption<UsageDisplayMode>> UsageDisplayOptions { get; }

    public IReadOnlyList<AppearanceOption<ResetTimeDisplayMode>> ResetTimeDisplayOptions { get; }

    public IReadOnlyList<AppearanceOption<DashboardVisualizationMode>> DashboardVisualizationOptions { get; }

    [ObservableProperty]
    public partial AppearanceSettings Settings { get; private set; } = AppearanceSettings.Default;

    [ObservableProperty]
    public partial AppearanceOption<AppThemeMode> SelectedTheme { get; set; }

    [ObservableProperty]
    public partial AppearanceOption<AppDensityMode> SelectedDensity { get; set; }

    [ObservableProperty]
    public partial bool IncreaseTransparency { get; set; }

    [ObservableProperty]
    public partial AppearanceOption<UsageDisplayMode> SelectedUsageDisplay { get; set; }

    [ObservableProperty]
    public partial AppearanceOption<ResetTimeDisplayMode> SelectedResetTimeDisplay { get; set; }

    [ObservableProperty]
    public partial AppearanceOption<DashboardVisualizationMode> SelectedDashboardVisualization { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditable))]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStatusVisible))]
    public partial string StatusText { get; private set; } = string.Empty;

    public bool IsEditable => _session.IsEditable && !IsBusy;

    public bool IsStatusVisible => !string.IsNullOrWhiteSpace(StatusText);

    public Task WaitForPendingSaveAsync()
    {
        lock (_saveSync)
        {
            return _pendingSave;
        }
    }

    partial void OnSelectedThemeChanged(AppearanceOption<AppThemeMode> value) =>
        QueueSave();

    partial void OnSelectedDensityChanged(AppearanceOption<AppDensityMode> value) =>
        QueueSave();

    partial void OnIncreaseTransparencyChanged(bool value) => QueueSave();

    partial void OnSelectedUsageDisplayChanged(AppearanceOption<UsageDisplayMode> value) =>
        QueueSave();

    partial void OnSelectedResetTimeDisplayChanged(
        AppearanceOption<ResetTimeDisplayMode> value) => QueueSave();

    partial void OnSelectedDashboardVisualizationChanged(
        AppearanceOption<DashboardVisualizationMode> value) => QueueSave();

    private async Task InitializeAsync()
    {
        try
        {
            await _session.InitializeAsync().ConfigureAwait(true);
            ApplySettings(_session.Settings);
            StatusText = _session.LastLoadKind switch
            {
                AppearanceSessionLoadKind.Corrupt
                    when _session.QuarantineFileName is string name =>
                    string.Format(
                        CultureInfo.CurrentCulture,
                        _getString("AppearanceRecoveredFormat"),
                        name),
                AppearanceSessionLoadKind.UnsupportedVersion
                    when _session.UnsupportedSchemaVersion is int version =>
                    string.Format(
                        CultureInfo.CurrentCulture,
                        _getString("AppearanceNewerVersionFormat"),
                        version),
                AppearanceSessionLoadKind.Unavailable => _getString("AppearanceUnavailable"),
                _ => string.Empty,
            };
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsEditable));
        }
    }

    private void ApplySettings(AppearanceSettings settings)
    {
        _isApplyingSettings = true;
        try
        {
            SelectedTheme = ThemeOptions.Single(option => option.Value == settings.Theme);
            SelectedDensity = DensityOptions.Single(option => option.Value == settings.Density);
            IncreaseTransparency = settings.IncreaseTransparency;
            SelectedUsageDisplay = UsageDisplayOptions.Single(
                option => option.Value == settings.UsageDisplay);
            SelectedResetTimeDisplay = ResetTimeDisplayOptions.Single(
                option => option.Value == settings.ResetTimeDisplay);
            SelectedDashboardVisualization = DashboardVisualizationOptions.Single(
                option => option.Value == settings.DashboardVisualization);
            Settings = settings;
            SettingsChanged?.Invoke(this, settings);
        }
        finally
        {
            _isApplyingSettings = false;
        }
    }

    private void QueueSave()
    {
        if (_isApplyingSettings
            || _session.IsReadOnly
            || SelectedTheme is null
            || SelectedDensity is null
            || SelectedUsageDisplay is null
            || SelectedResetTimeDisplay is null
            || SelectedDashboardVisualization is null)
        {
            return;
        }

        var settings = new AppearanceSettings(
            SelectedTheme.Value,
            SelectedDensity.Value,
            IncreaseTransparency,
            SelectedUsageDisplay.Value,
            SelectedResetTimeDisplay.Value,
            SelectedDashboardVisualization.Value);
        Settings = settings;
        SettingsChanged?.Invoke(this, settings);
        IsBusy = true;
        int version = ++_saveVersion;
        lock (_saveSync)
        {
            _pendingSave = SaveAfterAsync(_pendingSave, settings, version);
        }
    }

    private async Task SaveAfterAsync(
        Task previous,
        AppearanceSettings settings,
        int version)
    {
        await previous.ConfigureAwait(true);
        await Initialization.ConfigureAwait(true);
        AppearanceSessionSaveKind kind = await _session
            .SaveAsync(settings)
            .ConfigureAwait(true);
        StatusText = kind switch
        {
            AppearanceSessionSaveKind.RefusedUnsupportedVersion
                when _session.UnsupportedSchemaVersion is int schemaVersion =>
                string.Format(
                    CultureInfo.CurrentCulture,
                    _getString("AppearanceNewerVersionFormat"),
                    schemaVersion),
            AppearanceSessionSaveKind.Failed => _getString("AppearanceSaveFailed"),
            _ => string.Empty,
        };
        if (version == _saveVersion)
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsEditable));
        }
    }
}

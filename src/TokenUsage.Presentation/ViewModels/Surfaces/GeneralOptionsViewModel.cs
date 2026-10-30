using CommunityToolkit.Mvvm.ComponentModel;
using TokenUsage.App.ViewModels.Sample;
using TokenUsage.Core.Usage;

namespace TokenUsage.App.ViewModels.Surfaces;

public sealed record DataCollectionRefreshOption(int Minutes, string Label);

public sealed partial class GeneralOptionsViewModel : ObservableObject
{
    private bool _isInitializing = true;
    private readonly DataCollectionSettingsStore? _dataCollectionSettings;
    private readonly Func<string, string> _getString;

    public GeneralOptionsViewModel(
        Func<string, string> getString,
        DataCollectionSettingsStore? dataCollectionSettings = null)
    {
        ArgumentNullException.ThrowIfNull(getString);
        _getString = getString;
        _dataCollectionSettings = dataCollectionSettings;
        DataCollectionRefreshOptions =
        [
            new(0, getString("DataCollectionRefreshManual")),
            new(15, getString("DataCollectionRefresh15Minutes")),
            new(30, getString("DataCollectionRefresh30Minutes")),
            new(60, getString("DataCollectionRefresh60Minutes")),
        ];
        SelectedDataCollectionRefresh = DataCollectionRefreshOptions[0];
        SampleScenarios =
        [
            new(SampleScenario.Normal, getString("SampleScenarioNormal")),
            new(SampleScenario.NearLimit, getString("SampleScenarioNearLimit")),
            new(SampleScenario.Partial, getString("SampleScenarioPartial")),
            new(SampleScenario.Stale, getString("SampleScenarioStale")),
            new(SampleScenario.Error, getString("SampleScenarioError")),
        ];
        SelectedSampleScenario = SampleScenarios[0];
        _isInitializing = false;
        Initialization = _dataCollectionSettings is not null
            ? InitializeAsync()
            : Task.CompletedTask;
    }

    public event EventHandler? SampleModeChanged;

    public event EventHandler? SampleScenarioChanged;

    public event EventHandler? BackgroundCollectionChanged;

    public event EventHandler? ClaudeLimitsChanged;

    public event EventHandler? DataCollectionRefreshChanged;

    public IReadOnlyList<SampleScenarioOption> SampleScenarios { get; }

    public IReadOnlyList<DataCollectionRefreshOption> DataCollectionRefreshOptions { get; }

    public Task Initialization { get; }

    [ObservableProperty]
    public partial bool IsBackgroundCollectionEnabled { get; set; } = true;

    /// <summary>
    /// Whether TokenUsage may read Claude account limits. The TokenUsage consent document
    /// is the source of truth, so the switch reads it back instead of keeping its own copy.
    /// </summary>
    [ObservableProperty]
    public partial bool IsClaudeLimitsEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsClaudeLimitsAvailable { get; private set; }

    [ObservableProperty]
    public partial string ClaudeLimitsStatus { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial DataCollectionRefreshOption SelectedDataCollectionRefresh { get; set; }

    private async Task InitializeAsync()
    {
        try
        {
            _isInitializing = true;
            if (_dataCollectionSettings is not null)
            {
                DataCollectionSettings settings = await _dataCollectionSettings.LoadAsync()
                    .ConfigureAwait(true);
                IsBackgroundCollectionEnabled = settings.BackgroundCollection;
                DataCollectionRefreshOption? match = DataCollectionRefreshOptions.FirstOrDefault(
                    option => option.Minutes == settings.OpenRefreshMinutes);
                if (match is not null)
                {
                    SelectedDataCollectionRefresh = match;
                }
            }
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException
                                           or TimeoutException)
        {
            // A settings file that cannot be read keeps the defaults.
        }
        finally
        {
            _isInitializing = false;
        }
    }

    private Func<bool>? _isClaudeLimitsEnabled;
    private Action<bool>? _applyClaudeLimits;
    private bool _isSyncingClaudeLimits;

    public void BindClaudeLimits(
        bool isAvailable,
        Func<bool> isEnabled,
        Action<bool> apply)
    {
        ArgumentNullException.ThrowIfNull(isEnabled);
        ArgumentNullException.ThrowIfNull(apply);
        _isClaudeLimitsEnabled = isEnabled;
        _applyClaudeLimits = apply;
        IsClaudeLimitsAvailable = isAvailable;
        SyncClaudeLimits();
    }

    private void SyncClaudeLimits()
    {
        _isSyncingClaudeLimits = true;
        try
        {
            IsClaudeLimitsEnabled = _isClaudeLimitsEnabled?.Invoke() ?? false;
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException or System.Text.Json.JsonException
                                           or InvalidOperationException)
        {
            IsClaudeLimitsEnabled = false;
            ClaudeLimitsStatus = _getString("ClaudeLimitsUpdateFailed");
        }
        finally
        {
            _isSyncingClaudeLimits = false;
        }
    }

    partial void OnIsClaudeLimitsEnabledChanged(bool value)
    {
        if (_isSyncingClaudeLimits || _applyClaudeLimits is null)
        {
            return;
        }

        try
        {
            _applyClaudeLimits(value);
            ClaudeLimitsStatus = string.Empty;
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException
                                           or InvalidDataException
                                           or InvalidOperationException
                                           or System.Text.Json.JsonException)
        {
            ClaudeLimitsStatus = _getString("ClaudeLimitsUpdateFailed");
            SyncClaudeLimits();
            return;
        }

        ClaudeLimitsChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnIsBackgroundCollectionEnabledChanged(bool value)
    {
        if (_isInitializing)
        {
            return;
        }

        BackgroundCollectionChanged?.Invoke(this, EventArgs.Empty);
        if (_dataCollectionSettings is not null)
        {
            _ = SaveDataCollectionAsync();
        }
    }

    partial void OnSelectedDataCollectionRefreshChanged(DataCollectionRefreshOption value)
    {
        if (_isInitializing || value is null)
        {
            return;
        }

        DataCollectionRefreshChanged?.Invoke(this, EventArgs.Empty);
        if (_dataCollectionSettings is not null)
        {
            _ = SaveDataCollectionAsync();
        }
    }

    private async Task SaveDataCollectionAsync()
    {
        try
        {
            await _dataCollectionSettings!.SaveAsync(new DataCollectionSettings(
                IsBackgroundCollectionEnabled,
                SelectedDataCollectionRefresh?.Minutes ?? 0)).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException
                                           or TimeoutException)
        {
            // A failed save keeps the last stored preference.
        }
    }

    [ObservableProperty]
    public partial bool CloseWhenInactive { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSampleScenarioEnabled))]
    public partial bool IsSampleModeEnabled { get; set; }

    [ObservableProperty]
    public partial SampleScenarioOption SelectedSampleScenario { get; set; }

    public bool IsSampleScenarioEnabled => IsSampleModeEnabled;

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

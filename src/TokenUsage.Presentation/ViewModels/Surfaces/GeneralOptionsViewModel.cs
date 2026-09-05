using CommunityToolkit.Mvvm.ComponentModel;
using TokenUsage.App.ViewModels.Sample;
using TokenUsage.Core.Usage;

namespace TokenUsage.App.ViewModels.Surfaces;

public sealed record DataCollectionRefreshOption(int Minutes, string Label);

public sealed partial class GeneralOptionsViewModel : ObservableObject
{
    private bool _isInitializing = true;
    private readonly DataCollectionSettingsStore? _dataCollectionSettings;

    public GeneralOptionsViewModel(
        Func<string, string> getString,
        DataCollectionSettingsStore? dataCollectionSettings = null)
    {
        ArgumentNullException.ThrowIfNull(getString);
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

    public event EventHandler? DataCollectionRefreshChanged;

    public IReadOnlyList<SampleScenarioOption> SampleScenarios { get; }

    public IReadOnlyList<DataCollectionRefreshOption> DataCollectionRefreshOptions { get; }

    public Task Initialization { get; }

    [ObservableProperty]
    public partial bool IsBackgroundCollectionEnabled { get; set; } = true;

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

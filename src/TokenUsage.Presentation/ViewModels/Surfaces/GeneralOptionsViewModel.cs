using CommunityToolkit.Mvvm.ComponentModel;
using TokenUsage.App.ViewModels.Sample;
using TokenUsage.Core.Usage;

namespace TokenUsage.App.ViewModels.Surfaces;

public sealed record DataCollectionRefreshOption(int Minutes, string Label);

public sealed record AttributionBackfillSourceOption(AttributionCapability Capability, string Label);

public sealed partial class GeneralOptionsViewModel : ObservableObject
{
    private bool _isInitializing = true;
    private readonly DataCollectionSettingsStore? _dataCollectionSettings;
    private readonly AttributionConsentStore? _attributionConsent;
    private readonly AttributionAliasStore? _attributionAliases;
    private readonly string? _usageDatabasePath;
    private readonly Func<AttributionCapability, CancellationToken, Task>? _clearAttributionDerivedStores;

    private readonly Func<string, string> _getString;
    private readonly Func<DateOnly, DateOnly, AttributionCapability, CancellationToken, Task>? _runAttributionBackfill;

    public GeneralOptionsViewModel(
        Func<string, string> getString,
        DataCollectionSettingsStore? dataCollectionSettings = null,
        AttributionConsentStore? attributionConsent = null,
        string? usageDatabasePath = null,
        Func<AttributionCapability, CancellationToken, Task>? clearAttributionDerivedStores = null,
        AttributionAliasStore? attributionAliases = null,
        Func<DateOnly, DateOnly, AttributionCapability, CancellationToken, Task>? runAttributionBackfill = null)
    {
        ArgumentNullException.ThrowIfNull(getString);
        _getString = getString;
        _dataCollectionSettings = dataCollectionSettings;
        _attributionConsent = attributionConsent;
        _attributionAliases = attributionAliases;
        _usageDatabasePath = usageDatabasePath;
        _clearAttributionDerivedStores = clearAttributionDerivedStores;
        _runAttributionBackfill = runAttributionBackfill;
        DataCollectionRefreshOptions =
        [
            new(0, getString("DataCollectionRefreshManual")),
            new(15, getString("DataCollectionRefresh15Minutes")),
            new(30, getString("DataCollectionRefresh30Minutes")),
            new(60, getString("DataCollectionRefresh60Minutes")),
        ];
        SelectedDataCollectionRefresh = DataCollectionRefreshOptions[0];
        AttributionBackfillSources =
        [
            new(AttributionCapability.CodexSession, getString("AttributionBackfillCodexSessions")),
            new(AttributionCapability.CodexProject, getString("AttributionBackfillCodexProjects")),
            new(AttributionCapability.CursorSession, getString("AttributionBackfillCursorSessions")),
            new(AttributionCapability.CodexMcp, getString("AttributionBackfillCodexMcp")),
            new(AttributionCapability.CodexSkills, getString("AttributionBackfillCodexSkills")),
            new(AttributionCapability.CodexCommands, getString("AttributionBackfillCodexCommands")),
            new(AttributionCapability.CodexFiles, getString("AttributionBackfillCodexFiles")),
        ];
        SelectedAttributionBackfillSource = AttributionBackfillSources[0];
        DateTimeOffset today = DateTimeOffset.UtcNow;
        AttributionBackfillToDate = today;
        AttributionBackfillFromDate = today.AddDays(-30);
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
        Initialization = _dataCollectionSettings is not null || _attributionConsent is not null
            ? InitializeAsync()
            : Task.CompletedTask;
    }

    public event EventHandler? SampleModeChanged;

    public event EventHandler? SampleScenarioChanged;

    public event EventHandler? BackgroundCollectionChanged;

    public event EventHandler? DataCollectionRefreshChanged;

    public event EventHandler? CodexSessionAttributionChanged;

    public event EventHandler? CodexProjectAttributionChanged;

    public event EventHandler? CursorSessionAttributionChanged;

    public event EventHandler? CodexMcpAttributionChanged;

    public event EventHandler? CodexSkillsAttributionChanged;

    public event EventHandler? CodexCommandsAttributionChanged;

    public event EventHandler? CodexFilesAttributionChanged;

    public AttributionConsentStore? AttributionConsent => _attributionConsent;

    public AttributionAliasStore? AttributionAliases => _attributionAliases;

    public IReadOnlyList<SampleScenarioOption> SampleScenarios { get; }

    public IReadOnlyList<DataCollectionRefreshOption> DataCollectionRefreshOptions { get; }

    public IReadOnlyList<AttributionBackfillSourceOption> AttributionBackfillSources { get; }

    public Task Initialization { get; }

    [ObservableProperty]
    public partial bool IsBackgroundCollectionEnabled { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRunAttributionBackfill))]
    public partial bool IsCodexSessionAttributionEnabled { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRunAttributionBackfill))]
    public partial bool IsCodexProjectAttributionEnabled { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRunAttributionBackfill))]
    public partial bool IsCursorSessionAttributionEnabled { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRunAttributionBackfill))]
    public partial bool IsCodexMcpAttributionEnabled { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRunAttributionBackfill))]
    public partial bool IsCodexSkillsAttributionEnabled { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRunAttributionBackfill))]
    public partial bool IsCodexCommandsAttributionEnabled { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRunAttributionBackfill))]
    public partial bool IsCodexFilesAttributionEnabled { get; set; }

    [ObservableProperty]
    public partial DataCollectionRefreshOption SelectedDataCollectionRefresh { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRunAttributionBackfill))]
    public partial AttributionBackfillSourceOption? SelectedAttributionBackfillSource { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRunAttributionBackfill))]
    public partial DateTimeOffset? AttributionBackfillFromDate { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRunAttributionBackfill))]
    public partial DateTimeOffset? AttributionBackfillToDate { get; set; }

    [ObservableProperty]
    public partial string AttributionBackfillStatus { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRunAttributionBackfill))]
    public partial bool IsAttributionBackfillRunning { get; set; }

    public bool CanRunAttributionBackfill =>
        !IsAttributionBackfillRunning
        && _runAttributionBackfill is not null
        && _attributionConsent is not null
        && SelectedAttributionBackfillSource is { } source
        && AttributionBackfillFromDate is { } from
        && AttributionBackfillToDate is { } to
        && DateOnly.FromDateTime(from.UtcDateTime) <= DateOnly.FromDateTime(to.UtcDateTime)
        && SourceConsentEnabled(source.Capability);

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

            if (_attributionConsent is not null)
            {
                IsCodexSessionAttributionEnabled = await LoadCapabilityAsync(AttributionCapability.CodexSession)
                    .ConfigureAwait(true);
                IsCodexProjectAttributionEnabled = await LoadCapabilityAsync(AttributionCapability.CodexProject)
                    .ConfigureAwait(true);
                IsCursorSessionAttributionEnabled = await LoadCapabilityAsync(AttributionCapability.CursorSession)
                    .ConfigureAwait(true);
                IsCodexMcpAttributionEnabled = await LoadCapabilityAsync(AttributionCapability.CodexMcp)
                    .ConfigureAwait(true);
                IsCodexSkillsAttributionEnabled = await LoadCapabilityAsync(AttributionCapability.CodexSkills)
                    .ConfigureAwait(true);
                IsCodexCommandsAttributionEnabled = await LoadCapabilityAsync(AttributionCapability.CodexCommands)
                    .ConfigureAwait(true);
                IsCodexFilesAttributionEnabled = await LoadCapabilityAsync(AttributionCapability.CodexFiles)
                    .ConfigureAwait(true);
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

    partial void OnIsCodexSessionAttributionEnabledChanged(bool value)
    {
        if (_isInitializing || _attributionConsent is null)
        {
            return;
        }

        _ = ApplyCapabilityAsync(AttributionCapability.CodexSession, value);
    }

    partial void OnIsCodexProjectAttributionEnabledChanged(bool value)
    {
        if (_isInitializing || _attributionConsent is null)
        {
            return;
        }

        _ = ApplyCapabilityAsync(AttributionCapability.CodexProject, value);
    }

    partial void OnIsCursorSessionAttributionEnabledChanged(bool value)
    {
        if (_isInitializing || _attributionConsent is null)
        {
            return;
        }

        _ = ApplyCapabilityAsync(AttributionCapability.CursorSession, value);
    }

    partial void OnIsCodexMcpAttributionEnabledChanged(bool value)
    {
        if (_isInitializing || _attributionConsent is null)
        {
            return;
        }

        _ = ApplyCapabilityAsync(AttributionCapability.CodexMcp, value);
    }

    partial void OnIsCodexSkillsAttributionEnabledChanged(bool value)
    {
        if (_isInitializing || _attributionConsent is null)
        {
            return;
        }

        _ = ApplyCapabilityAsync(AttributionCapability.CodexSkills, value);
    }

    partial void OnIsCodexCommandsAttributionEnabledChanged(bool value)
    {
        if (_isInitializing || _attributionConsent is null)
        {
            return;
        }

        _ = ApplyCapabilityAsync(AttributionCapability.CodexCommands, value);
    }

    partial void OnIsCodexFilesAttributionEnabledChanged(bool value)
    {
        if (_isInitializing || _attributionConsent is null)
        {
            return;
        }

        _ = ApplyCapabilityAsync(AttributionCapability.CodexFiles, value);
    }

    public async Task RunAttributionBackfillAsync()
    {
        if (!CanRunAttributionBackfill
            || _runAttributionBackfill is null
            || _attributionConsent is null
            || SelectedAttributionBackfillSource is not { } source
            || AttributionBackfillFromDate is not { } fromDate
            || AttributionBackfillToDate is not { } toDate)
        {
            return;
        }

        DateOnly from = DateOnly.FromDateTime(fromDate.UtcDateTime);
        DateOnly to = DateOnly.FromDateTime(toDate.UtcDateTime);
        IsAttributionBackfillRunning = true;
        AttributionBackfillStatus = string.Empty;
        try
        {
            AttributionConsent consent = await _attributionConsent.LoadAsync(source.Capability)
                .ConfigureAwait(true);
            if (!consent.AllowsLinks)
            {
                AttributionBackfillStatus = _getString("AttributionBackfillConsentRequired");
                return;
            }

            await _runAttributionBackfill(from, to, source.Capability, CancellationToken.None)
                .ConfigureAwait(true);
            AttributionConsent published = await _attributionConsent.LoadAsync(source.Capability)
                .ConfigureAwait(true);
            AttributionBackfillStatus = published.AllowsLinks
                ? string.Format(
                    System.Globalization.CultureInfo.CurrentCulture,
                    _getString("AttributionBackfillCompletedFormat"),
                    from.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                    to.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture))
                : _getString("AttributionBackfillConsentRequired");
        }
        catch (Exception exception) when (exception is IOException
                                               or UnauthorizedAccessException
                                               or TimeoutException
                                               or InvalidOperationException
                                               or OperationCanceledException)
        {
            AttributionBackfillStatus = _getString("AttributionBackfillFailed");
        }
        finally
        {
            IsAttributionBackfillRunning = false;
        }
    }

    private bool SourceConsentEnabled(AttributionCapability capability)
    {
        if (capability.Value == AttributionCapability.CodexSession.Value)
        {
            return IsCodexSessionAttributionEnabled;
        }

        if (capability.Value == AttributionCapability.CodexProject.Value)
        {
            return IsCodexProjectAttributionEnabled;
        }

        if (capability.Value == AttributionCapability.CursorSession.Value)
        {
            return IsCursorSessionAttributionEnabled;
        }

        if (capability.Value == AttributionCapability.CodexMcp.Value)
        {
            return IsCodexMcpAttributionEnabled;
        }

        if (capability.Value == AttributionCapability.CodexSkills.Value)
        {
            return IsCodexSkillsAttributionEnabled;
        }

        if (capability.Value == AttributionCapability.CodexCommands.Value)
        {
            return IsCodexCommandsAttributionEnabled;
        }

        return capability.Value == AttributionCapability.CodexFiles.Value
            && IsCodexFilesAttributionEnabled;
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

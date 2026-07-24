using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Windows.ApplicationModel.Resources;
using System.ComponentModel;
using System.Data.Common;
using WOpenUsage.App.Localization;
using WOpenUsage.App.Services;
using WOpenUsage.App.ViewModels.Dashboard;
using WOpenUsage.App.ViewModels.Sample;
using WOpenUsage.Core.Appearance;
using WOpenUsage.Core.Cache;
using WOpenUsage.Core.Layout;
using WOpenUsage.Core.Providers;
using WOpenUsage.Runtime.Windows.Codex;
using WOpenUsage.Runtime.Windows.VercelAiGateway;

namespace WOpenUsage.App.ViewModels;

public partial class FlyoutViewModel : ObservableObject
{
    private readonly ResourceLoader _resources = new();
    private readonly SampleDashboardSession _sampleSession;
    private readonly LiveDashboardSession _liveSession;
    private readonly AppearanceSession _appearanceSession;
    private readonly DashboardLayoutEditor _layoutEditor;
    private readonly Task _dashboardLayoutInitialization;
    private readonly Task _appearanceInitialization;
    private CancellationTokenSource? _refreshCancellation;
    private FlyoutSurfaceState _resultSurface = FlyoutSurfaceState.Loading;
    private SampleScenario? _activeScenario;
    private bool _hasPublishedDashboard;
    private DateTimeOffset? _publishedObservedAtUtc;
    private DateTimeOffset? _retryAtUtc;
    private bool _hasLocalUsage;
    private LocalUsageCard? _rawLocalUsage;
    private ProviderOutcome? _lastCodexOutcome;
    private ProviderSnapshot? _lastCodexSnapshot;
    private DashboardLayout _dashboardLayout = DashboardLayout.Empty;
    private DashboardSnapshot? _rawDashboard;
    private readonly HashSet<string> _expandedDashboardMetricProviders = new(StringComparer.Ordinal);
    private bool _isApplyingAppearance;

    public FlyoutViewModel(
        SampleRefreshCoordinator sampleRefreshCoordinator,
        ProviderRefreshHost liveRefreshHost,
        LocalUsageCoordinator localUsageCoordinator,
        DashboardLayoutStore dashboardLayoutStore,
        AppearanceSettingsStore appearanceSettingsStore,
        VercelGatewayRefreshCoordinator vercelGatewayCoordinator)
    {
        ArgumentNullException.ThrowIfNull(sampleRefreshCoordinator);
        ArgumentNullException.ThrowIfNull(liveRefreshHost);
        ArgumentNullException.ThrowIfNull(localUsageCoordinator);
        ArgumentNullException.ThrowIfNull(dashboardLayoutStore);
        ArgumentNullException.ThrowIfNull(appearanceSettingsStore);
        _sampleSession = new SampleDashboardSession(sampleRefreshCoordinator);
        _liveSession = new LiveDashboardSession(liveRefreshHost, localUsageCoordinator);
        _appearanceSession = new AppearanceSession(appearanceSettingsStore);
        _layoutEditor = new DashboardLayoutEditor(dashboardLayoutStore);
        Vercel = new VercelGatewaySettingsViewModel(
            vercelGatewayCoordinator
                ?? throw new ArgumentNullException(nameof(vercelGatewayCoordinator)),
            GetString);
        Vercel.PropertyChanged += OnVercelPropertyChanged;
        LanguageOptions =
        [
            new(AppLanguageCatalog.EnglishUnitedStates, GetString("LanguageEnglish")),
            new(AppLanguageCatalog.SpanishSpain, GetString("LanguageSpanish")),
        ];
        SelectedLanguage = LanguageOptions.Single(option => string.Equals(
            option.LanguageTag,
            AppLanguageRuntime.ActiveLanguageTag,
            StringComparison.OrdinalIgnoreCase));
        IsAppearanceBusy = true;
        ThemeOptions =
        [
            new(AppThemeMode.System, GetString("AppearanceThemeSystem")),
            new(AppThemeMode.Light, GetString("AppearanceThemeLight")),
            new(AppThemeMode.Dark, GetString("AppearanceThemeDark")),
        ];
        DensityOptions =
        [
            new(AppDensityMode.Regular, GetString("AppearanceDensityRegular")),
            new(AppDensityMode.Compact, GetString("AppearanceDensityCompact")),
        ];
        UsageDisplayOptions =
        [
            new(UsageDisplayMode.Remaining, GetString("AppearanceUsageRemaining")),
            new(UsageDisplayMode.Used, GetString("AppearanceUsageUsed")),
        ];
        ResetTimeDisplayOptions =
        [
            new(ResetTimeDisplayMode.Relative, GetString("AppearanceResetRelative")),
            new(ResetTimeDisplayMode.Exact, GetString("AppearanceResetExact")),
        ];
        SelectedTheme = ThemeOptions[0];
        SelectedDensity = DensityOptions[0];
        SelectedUsageDisplay = UsageDisplayOptions[0];
        SelectedResetTimeDisplay = ResetTimeDisplayOptions[0];
        SampleScenarios =
        [
            new(SampleScenario.Normal, GetString("SampleScenarioNormal")),
            new(SampleScenario.NearLimit, GetString("SampleScenarioNearLimit")),
            new(SampleScenario.Partial, GetString("SampleScenarioPartial")),
            new(SampleScenario.Stale, GetString("SampleScenarioStale")),
            new(SampleScenario.Error, GetString("SampleScenarioError")),
        ];

        SelectedSampleScenario = SampleScenarios[0];
        UnavailableTitle = GetString("CodexUnavailableTitle");
        UnavailableBody = GetString("CodexUnavailableBody");
        RetryButtonText = GetString("SampleRetry");
        RetryAutomationName = GetString("CodexRetry");
        RebuildSamplePreview();
        IsDashboardLayoutBusy = true;
        _dashboardLayoutInitialization = InitializeDashboardLayoutAsync();
        _appearanceInitialization = InitializeAppearanceAsync();
        _ = RefreshDashboardAsync(scenario: null, forceRefresh: false);
        _ = Vercel.InitializeAsync();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoading))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(IsOptions))]
    [NotifyPropertyChangedFor(nameof(IsSample))]
    [NotifyPropertyChangedFor(nameof(IsSampleUnavailable))]
    [NotifyPropertyChangedFor(nameof(IsSampleContext))]
    [NotifyPropertyChangedFor(nameof(IsLiveLoading))]
    [NotifyPropertyChangedFor(nameof(IsSampleLoading))]
    [NotifyPropertyChangedFor(nameof(IsCardSurface))]
    [NotifyPropertyChangedFor(nameof(IsUsageSurface))]
    [NotifyPropertyChangedFor(nameof(IsLocalUsageVisible))]
    [NotifyPropertyChangedFor(nameof(IsLiveDataStateVisible))]
    [NotifyPropertyChangedFor(nameof(IsSampleDataStateVisible))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenOptionsCommand))]
    [NotifyCanExecuteChangedFor(nameof(CloseOptionsCommand))]
    public partial FlyoutSurfaceState SurfaceState { get; set; } = FlyoutSurfaceState.Empty;

    [ObservableProperty]
    public partial bool CloseWhenInactive { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOptionsHome))]
    [NotifyPropertyChangedFor(nameof(IsGeneralOptionsSection))]
    [NotifyPropertyChangedFor(nameof(IsAppearanceOptionsSection))]
    [NotifyPropertyChangedFor(nameof(IsPersonalizationOptionsSection))]
    [NotifyPropertyChangedFor(nameof(IsProvidersOptionsSection))]
    [NotifyPropertyChangedFor(nameof(IsVercelOptionsSection))]
    [NotifyPropertyChangedFor(nameof(IsProviderStatusOptionsSection))]
    public partial OptionsSection ActiveOptionsSection { get; set; } = OptionsSection.Home;

    [ObservableProperty]
    public partial AppearanceSettings Appearance { get; private set; } = AppearanceSettings.Default;

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
    [NotifyPropertyChangedFor(nameof(IsAppearanceEditable))]
    public partial bool IsAppearanceBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAppearanceStatusVisible))]
    public partial string AppearanceStatusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial AppLanguageOption SelectedLanguage { get; set; }

    [ObservableProperty]
    public partial bool IsLanguageRestartRequired { get; set; }

    [ObservableProperty]
    public partial bool IsLanguageRestartErrorVisible { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSampleScenarioEnabled))]
    [NotifyPropertyChangedFor(nameof(IsSampleContext))]
    [NotifyPropertyChangedFor(nameof(IsLiveLoading))]
    [NotifyPropertyChangedFor(nameof(IsSampleLoading))]
    [NotifyPropertyChangedFor(nameof(DashboardHeading))]
    [NotifyPropertyChangedFor(nameof(IsLiveDataStateVisible))]
    [NotifyPropertyChangedFor(nameof(IsSampleDataStateVisible))]
    [NotifyPropertyChangedFor(nameof(SampleDataStateText))]
    [NotifyPropertyChangedFor(nameof(SampleDataStateAutomationId))]
    [NotifyPropertyChangedFor(nameof(IsLocalUsageVisible))]
    public partial bool IsSampleModeEnabled { get; set; }

    [ObservableProperty]
    public partial SampleScenarioOption SelectedSampleScenario { get; set; }

    [ObservableProperty]
    public partial DashboardSnapshot ActiveSample { get; set; } = null!;

    [ObservableProperty]
    public partial LocalUsageCard LocalUsage { get; set; } = new(
        "",
        "",
        "",
        "",
        [],
        [],
        new("", "", "", "", [], []),
        []);

    [ObservableProperty]
    public partial IReadOnlyList<ProviderStatusRow> ProviderStatuses { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDashboardLayoutProviders))]
    [NotifyPropertyChangedFor(nameof(AreAllDashboardProvidersHidden))]
    public partial IReadOnlyList<DashboardProviderLayoutRow> DashboardLayoutProviders { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDashboardLayoutStatusVisible))]
    public partial string DashboardLayoutStatusText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDashboardLayoutEditable))]
    [NotifyPropertyChangedFor(nameof(CanUndoDashboardLayout))]
    public partial bool IsDashboardLayoutBusy { get; set; }

    [ObservableProperty]
    public partial int SampleRevealToken { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyPropertyChangedFor(nameof(IsRefreshing))]
    public partial bool IsSampleRefreshing { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LiveDataStateText))]
    [NotifyPropertyChangedFor(nameof(SampleDataStateText))]
    [NotifyPropertyChangedFor(nameof(SampleDataStateAutomationId))]
    public partial SampleDataState CurrentSampleDataState { get; set; } = SampleDataState.Idle;

    [ObservableProperty]
    public partial string UnavailableTitle { get; set; }

    [ObservableProperty]
    public partial string UnavailableBody { get; set; }

    [ObservableProperty]
    public partial string RetryButtonText { get; set; }

    [ObservableProperty]
    public partial string RetryAutomationName { get; set; }

    public bool IsLoading => SurfaceState == FlyoutSurfaceState.Loading;

    public bool IsEmpty => SurfaceState == FlyoutSurfaceState.Empty;

    public bool IsOptions => SurfaceState == FlyoutSurfaceState.Options;

    public bool IsSample => SurfaceState == FlyoutSurfaceState.Sample;

    public bool IsSampleUnavailable => SurfaceState == FlyoutSurfaceState.SampleUnavailable;

    public bool IsSampleContext => IsSampleModeEnabled && !IsOptions;

    public bool IsLiveLoading => IsLoading && !IsSampleModeEnabled;

    public bool IsSampleLoading => IsLoading && IsSampleModeEnabled;

    public bool IsCardSurface => !IsSample;

    public bool IsUsageSurface => !IsOptions;

    public bool IsOptionsHome => ActiveOptionsSection == OptionsSection.Home;

    public bool IsGeneralOptionsSection => ActiveOptionsSection == OptionsSection.General;

    public bool IsAppearanceOptionsSection => ActiveOptionsSection == OptionsSection.Appearance;

    public bool IsPersonalizationOptionsSection => ActiveOptionsSection == OptionsSection.Personalization;

    public bool IsProvidersOptionsSection => ActiveOptionsSection == OptionsSection.Providers;

    public bool IsVercelOptionsSection => ActiveOptionsSection == OptionsSection.Vercel;

    public bool IsProviderStatusOptionsSection => ActiveOptionsSection == OptionsSection.ProviderStatus;

    public bool IsLocalUsageVisible =>
        _hasLocalUsage && !IsSampleModeEnabled && IsUsageSurface;

    public bool IsSampleScenarioEnabled => IsSampleModeEnabled;

    public bool IsRefreshing => IsSampleRefreshing || Vercel.IsBusy;

    public bool HasDashboardLayoutProviders => DashboardLayoutProviders.Count > 0;

    public bool AreAllDashboardProvidersHidden =>
        DashboardLayoutProviders.Count > 0
        && DashboardLayoutProviders.All(provider => !provider.IsVisible);

    public bool IsDashboardLayoutStatusVisible =>
        !string.IsNullOrWhiteSpace(DashboardLayoutStatusText);

    public bool IsDashboardLayoutEditable =>
        _layoutEditor.IsEditable && !IsDashboardLayoutBusy;

    public bool CanUndoDashboardLayout =>
        IsDashboardLayoutEditable && _layoutEditor.CanUndo;

    public bool IsAppearanceEditable => _appearanceSession.IsEditable;

    public bool IsAppearanceStatusVisible =>
        !string.IsNullOrWhiteSpace(AppearanceStatusText);

    public string DashboardLayoutResetTitle => GetString("DashboardLayoutResetTitle");

    public string DashboardLayoutResetBody => GetString("DashboardLayoutResetBody");

    public string DashboardLayoutResetConfirm => GetString("DashboardLayoutResetConfirm");

    public string DashboardLayoutResetCancel => GetString("DashboardLayoutResetCancel");

    public string DashboardHeading => GetString(
        IsSampleModeEnabled ? "SampleTotalSpendHeading" : "LiveDashboardHeading");

    public bool IsLiveDataStateVisible => !IsSampleModeEnabled && IsSample;

    public bool IsSampleDataStateVisible => IsSampleModeEnabled && IsSample;

    public string LiveDataStateText => _hasLocalUsage && ActiveSample.HasSpend
        ? ActiveSample.PeriodLabel
        : CodexLiveStateFormatter.Format(
            CurrentSampleDataState,
            IsSampleModeEnabled,
            _publishedObservedAtUtc,
            _retryAtUtc,
            _liveSession.Clock.GetUtcNow(),
            GetString);

    public string SampleDataStateText => GetString(CurrentSampleDataState switch
    {
        SampleDataState.CacheRefreshing => "SampleStateCacheRefreshing",
        SampleDataState.StaleCacheRefreshing => "SampleStateStaleCacheRefreshing",
        SampleDataState.Fresh => "SampleStateFresh",
        SampleDataState.Partial => "SampleStatePartial",
        SampleDataState.Stale => "SampleStateStale",
        SampleDataState.Error => "SampleStateError",
        SampleDataState.Throttled => "SampleStateThrottled",
        SampleDataState.NotSaved => "SampleStateNotSaved",
        _ => "SamplePeriodNormal",
    });

    public string SampleDataStateAutomationId => CurrentSampleDataState switch
    {
        SampleDataState.CacheRefreshing => "SampleStateCacheRefreshing",
        SampleDataState.StaleCacheRefreshing => "SampleStateStaleCacheRefreshing",
        SampleDataState.Fresh => "SampleStateFresh",
        SampleDataState.Partial => "SampleStatePartial",
        SampleDataState.Stale => "SampleStateStale",
        SampleDataState.Error => "SampleStateError",
        SampleDataState.Throttled => "SampleStateThrottled",
        SampleDataState.NotSaved => "SampleStateNotSaved",
        _ => "SampleStateIdle",
    };

    public IReadOnlyList<SampleScenarioOption> SampleScenarios { get; }

    public IReadOnlyList<AppLanguageOption> LanguageOptions { get; }

    public IReadOnlyList<AppearanceOption<AppThemeMode>> ThemeOptions { get; }

    public IReadOnlyList<AppearanceOption<AppDensityMode>> DensityOptions { get; }

    public IReadOnlyList<AppearanceOption<UsageDisplayMode>> UsageDisplayOptions { get; }

    public IReadOnlyList<AppearanceOption<ResetTimeDisplayMode>> ResetTimeDisplayOptions { get; }

    public VercelGatewaySettingsViewModel Vercel { get; }

    public string PendingLanguageTag => SelectedLanguage.LanguageTag;

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private Task RefreshAsync() => IsSampleModeEnabled
        ? RefreshDashboardAsync(SelectedSampleScenario.Value, forceRefresh: true)
        : Task.WhenAll(
            RefreshDashboardAsync(scenario: null, forceRefresh: true),
            Vercel.RefreshAsync(forceRefresh: true));

    private bool CanRefresh() => !IsLoading && !IsRefreshing;

    [RelayCommand(CanExecute = nameof(CanOpenOptions))]
    private void OpenOptions()
    {
        ActiveOptionsSection = OptionsSection.Home;
        SurfaceState = FlyoutSurfaceState.Options;
    }

    private bool CanOpenOptions() => !IsOptions;

    [RelayCommand(CanExecute = nameof(CanCloseOptions))]
    private void CloseOptions()
    {
        SurfaceState = _resultSurface;
    }

    private bool CanCloseOptions() => IsOptions;

    [RelayCommand]
    private void NavigateBackOptions()
    {
        if (ActiveOptionsSection == OptionsSection.Home)
        {
            CloseOptions();
            return;
        }

        ActiveOptionsSection = ActiveOptionsSection is OptionsSection.Vercel or OptionsSection.ProviderStatus
            ? OptionsSection.Providers
            : OptionsSection.Home;
    }

    [RelayCommand]
    private void ShowGeneralOptions() => ActiveOptionsSection = OptionsSection.General;

    [RelayCommand]
    private void ShowAppearanceOptions() => ActiveOptionsSection = OptionsSection.Appearance;

    [RelayCommand]
    private void ShowPersonalizationOptions() => ActiveOptionsSection = OptionsSection.Personalization;

    [RelayCommand]
    private void ShowProvidersOptions() => ActiveOptionsSection = OptionsSection.Providers;

    [RelayCommand]
    private void ShowVercelOptions() => ActiveOptionsSection = OptionsSection.Vercel;

    [RelayCommand]
    private void ShowProviderStatusOptions() => ActiveOptionsSection = OptionsSection.ProviderStatus;

    partial void OnSelectedLanguageChanged(AppLanguageOption value)
    {
        if (value is null)
        {
            return;
        }

        IsLanguageRestartRequired = AppLanguageRuntime.RequiresRestart(value.LanguageTag);
        IsLanguageRestartErrorVisible = false;
    }

    partial void OnSelectedThemeChanged(AppearanceOption<AppThemeMode> value) =>
        QueueAppearanceSave();

    partial void OnSelectedDensityChanged(AppearanceOption<AppDensityMode> value) =>
        QueueAppearanceSave();

    partial void OnIncreaseTransparencyChanged(bool value) =>
        QueueAppearanceSave();

    partial void OnSelectedUsageDisplayChanged(AppearanceOption<UsageDisplayMode> value) =>
        QueueAppearanceSave();

    partial void OnSelectedResetTimeDisplayChanged(
        AppearanceOption<ResetTimeDisplayMode> value) =>
        QueueAppearanceSave();

    private void QueueAppearanceSave()
    {
        if (_isApplyingAppearance
            || IsAppearanceBusy
            || _appearanceSession.IsReadOnly
            || SelectedTheme is null
            || SelectedDensity is null
            || SelectedUsageDisplay is null
            || SelectedResetTimeDisplay is null)
        {
            return;
        }

        Appearance = CreateAppearanceSettings();
        if (_rawDashboard is not null)
        {
            PublishActiveDashboard(_rawDashboard);
        }

        IsAppearanceBusy = true;
        _ = SaveAppearanceAsync(Appearance);
    }

    private AppearanceSettings CreateAppearanceSettings() => new(
        SelectedTheme.Value,
        SelectedDensity.Value,
        IncreaseTransparency,
        SelectedUsageDisplay.Value,
        SelectedResetTimeDisplay.Value);

    private async Task InitializeAppearanceAsync()
    {
        try
        {
            await _appearanceSession.InitializeAsync().ConfigureAwait(true);
            ApplyAppearanceSettings(_appearanceSession.Settings);
            AppearanceStatusText = _appearanceSession.LastLoadKind switch
            {
                AppearanceSessionLoadKind.Corrupt when _appearanceSession.QuarantineFileName is string name =>
                    string.Format(
                        System.Globalization.CultureInfo.CurrentCulture,
                        GetString("AppearanceRecoveredFormat"),
                        name),
                AppearanceSessionLoadKind.UnsupportedVersion when _appearanceSession.UnsupportedSchemaVersion is int version =>
                    string.Format(
                        System.Globalization.CultureInfo.CurrentCulture,
                        GetString("AppearanceNewerVersionFormat"),
                        version),
                AppearanceSessionLoadKind.Unavailable => GetString("AppearanceUnavailable"),
                _ => string.Empty,
            };
        }
        finally
        {
            IsAppearanceBusy = false;
            OnPropertyChanged(nameof(IsAppearanceEditable));
        }
    }

    private void ApplyAppearanceSettings(AppearanceSettings settings)
    {
        _isApplyingAppearance = true;
        try
        {
            SelectedTheme = ThemeOptions.Single(option => option.Value == settings.Theme);
            SelectedDensity = DensityOptions.Single(option => option.Value == settings.Density);
            IncreaseTransparency = settings.IncreaseTransparency;
            SelectedUsageDisplay = UsageDisplayOptions.Single(
                option => option.Value == settings.UsageDisplay);
            SelectedResetTimeDisplay = ResetTimeDisplayOptions.Single(
                option => option.Value == settings.ResetTimeDisplay);
            Appearance = settings;
        }
        finally
        {
            _isApplyingAppearance = false;
        }
    }

    private async Task SaveAppearanceAsync(AppearanceSettings settings)
    {
        await _appearanceInitialization;
        try
        {
            AppearanceSessionSaveKind kind = await _appearanceSession
                .SaveAsync(settings)
                .ConfigureAwait(true);
            AppearanceStatusText = kind switch
            {
                AppearanceSessionSaveKind.RefusedUnsupportedVersion
                    when _appearanceSession.UnsupportedSchemaVersion is int version =>
                    string.Format(
                        System.Globalization.CultureInfo.CurrentCulture,
                        GetString("AppearanceNewerVersionFormat"),
                        version),
                AppearanceSessionSaveKind.Failed => GetString("AppearanceSaveFailed"),
                _ => string.Empty,
            };
        }
        finally
        {
            IsAppearanceBusy = false;
            OnPropertyChanged(nameof(IsAppearanceEditable));
        }
    }

    public void ReportLanguageRestartFailure()
    {
        IsLanguageRestartErrorVisible = true;
    }

    partial void OnIsSampleModeEnabledChanged(bool value)
    {
        CancelRefresh();
        _hasPublishedDashboard = false;
        _activeScenario = null;
        _resultSurface = FlyoutSurfaceState.Loading;
        if (value)
        {
            RebuildSamplePreview();
        }

        _ = RefreshDashboardAsync(
            value ? SelectedSampleScenario.Value : null,
            forceRefresh: false);
    }

    partial void OnSelectedSampleScenarioChanged(SampleScenarioOption value)
    {
        if (value is null)
        {
            return;
        }

        if (IsSampleModeEnabled)
        {
            _ = RefreshDashboardAsync(value.Value, forceRefresh: true);
        }
        else
        {
            RebuildSamplePreview();
        }
    }

    private void OnVercelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(Vercel.IsBusy), StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(IsRefreshing));
            RefreshCommand.NotifyCanExecuteChanged();
        }

        if (string.Equals(e.PropertyName, nameof(Vercel.ProviderCard), StringComparison.Ordinal)
            || string.Equals(e.PropertyName, nameof(Vercel.State), StringComparison.Ordinal)
            || string.Equals(e.PropertyName, nameof(Vercel.IsConfigured), StringComparison.Ordinal))
        {
            RebuildProviderStatuses();
        }

        if (string.Equals(e.PropertyName, nameof(Vercel.ProviderCard), StringComparison.Ordinal)
            && !IsSampleModeEnabled)
        {
            if (!PublishCombinedLiveDashboard(reveal: true) && _hasLocalUsage)
            {
                PublishActiveDashboard(new DashboardSnapshot(
                    SampleScenario.Normal,
                    string.Empty,
                    GetString("LiveDashboardPeriod"),
                    string.Empty,
                    [],
                    []));
                _hasPublishedDashboard = true;
                _resultSurface = FlyoutSurfaceState.Sample;
                ApplyResultSurfaceIfVisible();
            }
        }
    }

    private async Task RefreshDashboardAsync(
        SampleScenario? scenario,
        bool forceRefresh)
    {
        _refreshCancellation?.Cancel();
        _sampleSession.Cancel();
        _liveSession.Cancel();
        var cancellation = new CancellationTokenSource();
        _refreshCancellation = cancellation;
        IsSampleRefreshing = true;

        if (!_hasPublishedDashboard)
        {
            _resultSurface = FlyoutSurfaceState.Loading;
            ApplyResultSurfaceIfVisible();
        }

        try
        {
            if (scenario is SampleScenario sampleScenario)
            {
                await _sampleSession.RunAsync(
                    sampleScenario,
                    forceRefresh,
                    GetString,
                    OnSampleSessionChanged,
                    cancellation.Token).ConfigureAwait(true);
            }
            else
            {
                await _liveSession.RunAsync(
                    forceRefresh,
                    GetString,
                    Vercel,
                    OnLiveSessionChanged,
                    cancellation.Token).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_refreshCancellation, cancellation))
            {
                IsSampleRefreshing = false;
                _refreshCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private void OnSampleSessionChanged(SampleDashboardSession session)
    {
        _activeScenario = session.ActiveScenario;
        _publishedObservedAtUtc = session.PublishedObservedAtUtc;
        _retryAtUtc = session.RetryAtUtc;
        CurrentSampleDataState = session.DataState;
        if (session.LastDashboard is not null)
        {
            PublishActiveDashboard(session.LastDashboard);
            _hasPublishedDashboard = true;
            _resultSurface = FlyoutSurfaceState.Sample;
            ApplyResultSurfaceIfVisible();
            SampleRevealToken++;
        }
        else if (!session.HasPublished)
        {
            PublishUnavailable(outcome: null);
        }
    }

    private void OnLiveSessionChanged(LiveDashboardSession session)
    {
        _lastCodexSnapshot = session.LastCodexSnapshot;
        _lastCodexOutcome = session.LastCodexOutcome;
        _publishedObservedAtUtc = session.PublishedObservedAtUtc;
        _retryAtUtc = session.RetryAtUtc;
        CurrentSampleDataState = session.DataState;
        if (session.RawLocalUsage is not null)
        {
            _rawLocalUsage = session.RawLocalUsage;
            LocalUsage = ApplyDashboardLayoutToLocalUsage(session.RawLocalUsage);
            _hasLocalUsage = session.HasLocalUsage;
            RebuildProviderStatuses();
            OnPropertyChanged(nameof(IsLocalUsageVisible));
        }

        if (session.LastCodexSnapshot is null
            && session.HasLocalUsage
            && Vercel.ProviderCard is null
            && (session.RawLocalUsage is null
                || session.RawLocalUsage.SpendBreakdown.AgentSlices.Count == 0))
        {
            if (!session.HasPublished && _lastCodexOutcome is not null)
            {
                PublishUnavailable(_lastCodexOutcome);
            }

            return;
        }

        if (PublishCombinedLiveDashboard(reveal: true))
        {
            _hasPublishedDashboard = true;
            _resultSurface = FlyoutSurfaceState.Sample;
            ApplyResultSurfaceIfVisible();
        }
        else if (session.LastCodexOutcome is not null
            && session.LastCodexSnapshot is null)
        {
            PublishUnavailable(session.LastCodexOutcome);
        }
    }

    private void PublishCachedDashboard(
        SampleScenario? scenario,
        CacheFirstEvent.CachePublished cache)
    {
        if (scenario is null)
        {
            ProviderSnapshot? vercelCached = cache.Snapshots.FirstOrDefault(candidate =>
                string.Equals(
                    candidate.ProviderId.Value,
                    "vercel-ai-gateway",
                    StringComparison.Ordinal));
            if (vercelCached is not null)
            {
                Vercel.ApplyHostCacheSnapshot(vercelCached);
            }
        }

        ProviderSnapshot? snapshot = cache.Snapshots.FirstOrDefault(candidate =>
            string.Equals(candidate.ProviderId.Value, "codex", StringComparison.Ordinal));
        if (snapshot is null)
        {
            if (scenario is null
                && (Vercel.ProviderCard is not null
                    || (_hasLocalUsage && _rawLocalUsage is not null)))
            {
                PublishCombinedLiveDashboard(reveal: !_hasPublishedDashboard);
                return;
            }

            if (!_hasPublishedDashboard)
            {
                _resultSurface = FlyoutSurfaceState.Loading;
                ApplyResultSurfaceIfVisible();
            }

            return;
        }

        bool reveal = !_hasPublishedDashboard || _activeScenario != scenario;
        PublishDashboard(scenario, snapshot, reveal);
        bool isStale = SnapshotFreshness.IsStale(snapshot, GetClock(scenario));
        SetDataState(isStale
            ? SampleDataState.StaleCacheRefreshing
            : SampleDataState.CacheRefreshing);
    }

    private void PublishProviderOutcome(
        SampleScenario? scenario,
        CacheFirstEvent.ProviderCompleted provider)
    {
        if (scenario is null)
        {
            _lastCodexOutcome = provider.Outcome;
        }

        ProviderSnapshot? snapshot = provider.Outcome switch
        {
            ProviderOutcome.Success success => success.Snapshot,
            ProviderOutcome.PartialSuccess partial => partial.Snapshot,
            ProviderOutcome.Throttled throttled => throttled.LastGood,
            ProviderOutcome.TransientFailure failure => failure.LastGood,
            ProviderOutcome.ContractFailure failure => failure.LastGood,
            _ => null,
        };

        _retryAtUtc = provider.Outcome switch
        {
            ProviderOutcome.Throttled throttled => throttled.RetryAtUtc,
            ProviderOutcome.TransientFailure failure => failure.RetryAtUtc,
            ProviderOutcome.ContractFailure failure => failure.RetryAtUtc,
            _ => null,
        };

        if (snapshot is null)
        {
            PublishUnavailable(provider.Outcome);
            return;
        }

        PublishDashboard(scenario, snapshot, reveal: true);
        if (provider.Outcome is ProviderOutcome.Throttled)
        {
            SetDataState(SampleDataState.Throttled);
        }
        else if (provider.Outcome is ProviderOutcome.TransientFailure
            or ProviderOutcome.ContractFailure)
        {
            SetDataState(SampleDataState.Error);
        }
        else if (provider.CacheStatus is not CacheUpdateStatus.Updated)
        {
            SetDataState(SampleDataState.NotSaved);
        }
        else if (provider.Outcome is ProviderOutcome.PartialSuccess)
        {
            SetDataState(SampleDataState.Partial);
        }
        else if (SnapshotFreshness.IsStale(snapshot, GetClock(scenario)))
        {
            SetDataState(SampleDataState.Stale);
        }
        else
        {
            SetDataState(SampleDataState.Fresh);
        }
    }

    private void PublishDashboard(
        SampleScenario? scenario,
        ProviderSnapshot snapshot,
        bool reveal)
    {
        _publishedObservedAtUtc = snapshot.SourceObservedAtUtc;
        if (scenario is SampleScenario sampleScenario)
        {
            PublishActiveDashboard(
                SampleDashboardProjector.Create(sampleScenario, snapshot, GetString));
        }
        else
        {
            _lastCodexSnapshot = snapshot;
            if (!PublishCombinedLiveDashboard(reveal))
            {
                return;
            }
        }

        _activeScenario = scenario;
        _hasPublishedDashboard = true;
        _resultSurface = FlyoutSurfaceState.Sample;
        ApplyResultSurfaceIfVisible();
        if (reveal && scenario is not null)
        {
            SampleRevealToken++;
        }
    }

    private bool PublishCombinedLiveDashboard(bool reveal)
    {
        var providers = new List<ProviderCard>();
        if (_lastCodexSnapshot is not null)
        {
            providers.AddRange(CodexDashboardProjector.Create(
                _lastCodexSnapshot,
                _liveSession.Clock,
                GetString).Providers);
        }

        if (Vercel.ProviderCard is ProviderCard vercelCard)
        {
            providers.Add(vercelCard);
        }

        IReadOnlyList<SpendSlice> spendSlices = _hasLocalUsage && _rawLocalUsage is not null
            ? _rawLocalUsage.SpendBreakdown.AgentSlices
            : [];
        IReadOnlyList<SpendSlice> additionalSpendSlices = Vercel.SpendSlice is { } vercelSpend
            ? [vercelSpend]
            : [];
        if (providers.Count == 0 && spendSlices.Count == 0 && additionalSpendSlices.Count == 0)
        {
            return false;
        }

        PublishActiveDashboard(LiveDashboardComposer.Create(
            providers,
            _hasLocalUsage ? _rawLocalUsage : null,
            additionalSpendSlices,
            GetString("LiveDashboardPeriod"),
            CreateDashboardSpendSummary));
        _activeScenario = null;
        _hasPublishedDashboard = true;
        _resultSurface = FlyoutSurfaceState.Sample;
        ApplyResultSurfaceIfVisible();
        if (reveal)
        {
            SampleRevealToken++;
        }

        return true;
    }

    private void PublishUnavailable(ProviderOutcome? outcome)
    {
        if (!IsSampleModeEnabled)
        {
            _lastCodexOutcome = outcome;
        }

        _hasPublishedDashboard = false;
        if (!IsSampleModeEnabled)
        {
            _lastCodexSnapshot = null;
            if (PublishCombinedLiveDashboard(reveal: false))
            {
                SetDataState(SampleDataState.Unavailable);
                return;
            }
        }

        _resultSurface = FlyoutSurfaceState.SampleUnavailable;
        SetDataState(SampleDataState.Unavailable);

        if (IsSampleModeEnabled)
        {
            UnavailableTitle = GetString("SampleUnavailableTitleValue");
            UnavailableBody = GetString("SampleUnavailableBodyValue");
            RetryButtonText = GetString("SampleRetry");
            RetryAutomationName = GetString("SampleRetryAutomationName");
        }
        else if (outcome is ProviderOutcome.NotConfigured)
        {
            UnavailableTitle = GetString("CodexNotConfiguredTitle");
            UnavailableBody = GetString("CodexNotConfiguredBody");
            RetryButtonText = GetString("SampleRetry");
            RetryAutomationName = GetString("CodexRetry");
        }
        else if (outcome is ProviderOutcome.UnsupportedAccount)
        {
            UnavailableTitle = GetString("CodexUnsupportedTitle");
            UnavailableBody = GetString("CodexUnsupportedBody");
            RetryButtonText = GetString("SampleRetry");
            RetryAutomationName = GetString("CodexRetry");
        }
        else
        {
            UnavailableTitle = GetString("CodexUnavailableTitle");
            UnavailableBody = GetString("CodexUnavailableBody");
            RetryButtonText = GetString("SampleRetry");
            RetryAutomationName = GetString("CodexRetry");
        }

        ApplyResultSurfaceIfVisible();
    }

    private void SetDataState(SampleDataState state)
    {
        CurrentSampleDataState = state;
        RebuildProviderStatuses();
        OnPropertyChanged(nameof(LiveDataStateText));
    }

    private void RebuildProviderStatuses()
    {
        var statuses = new List<ProviderStatusRow>
        {
            new(
                "codex",
                GetString("LocalUsageAgentCodex"),
                _lastCodexOutcome is ProviderOutcome.NotConfigured
                    ? GetString("ProviderStatusRootMissing")
                    : _lastCodexOutcome is null && !_hasPublishedDashboard
                        ? GetString("ProviderStatusRootPending")
                        : GetString("ProviderStatusRootDetected"),
                GetString(_lastCodexOutcome switch
                {
                    ProviderOutcome.NotConfigured => "ProviderStatusRecoveryOpenTool",
                    ProviderOutcome.UnsupportedAccount or ProviderOutcome.PolicyBlocked =>
                        "ProviderStatusRecoveryUnavailable",
                    ProviderOutcome.ContractFailure => "ProviderStatusRecoveryUpdate",
                    ProviderOutcome.Throttled or ProviderOutcome.TransientFailure =>
                        "ProviderStatusRecoveryRetry",
                    _ => "ProviderStatusRecoveryRefresh",
                }),
                [
                    new(
                        GetString("ProviderStatusQuota"),
                        CurrentSampleDataState switch
                        {
                            _ when _lastCodexOutcome is ProviderOutcome.NotConfigured =>
                                GetString("ProviderStatusNotConfigured"),
                            _ when _lastCodexOutcome is ProviderOutcome.UnsupportedAccount =>
                                GetString("ProviderStatusUnsupported"),
                            _ when _lastCodexOutcome is ProviderOutcome.PolicyBlocked =>
                                GetString("ProviderStatusBlocked"),
                            _ when _lastCodexOutcome is ProviderOutcome.ContractFailure =>
                                GetString("ProviderStatusContractChanged"),
                            _ when _lastCodexOutcome is ProviderOutcome.Throttled
                                or ProviderOutcome.TransientFailure =>
                                GetString("ProviderStatusPartial"),
                            SampleDataState.Partial => GetString("ProviderStatusPartial"),
                            SampleDataState.Fresh or SampleDataState.CacheRefreshing
                                or SampleDataState.StaleCacheRefreshing or SampleDataState.Stale
                                or SampleDataState.NotSaved => GetString("ProviderStatusAvailable"),
                            _ => GetString("ProviderStatusUnavailable"),
                        },
                        "ProviderStatus.codex.Quota"),
                    new(GetString("ProviderStatusUsage"), GetString("ProviderStatusUnavailable"), "ProviderStatus.codex.Usage"),
                    new(GetString("ProviderStatusSpend"), GetString("ProviderStatusUnavailable"), "ProviderStatus.codex.Spend"),
                    new(GetString("ProviderStatusCoverage"), GetString("CodexUsageMissing"), "ProviderStatus.codex.Coverage"),
                ],
                "ProviderStatus.codex"),
        };
        statuses.Add(Vercel.CreateStatusRow());
        statuses.AddRange(LocalUsage.ProviderStatuses);
        ProviderStatuses = statuses;
    }

    public void RefreshRelativeTime()
    {
        if (IsSampleModeEnabled)
        {
            return;
        }

        if (_retryAtUtc is DateTimeOffset retryAtUtc
            && retryAtUtc <= _liveSession.Clock.GetUtcNow().ToUniversalTime()
            && !IsSampleRefreshing)
        {
            _retryAtUtc = null;
            _ = RefreshDashboardAsync(scenario: null, forceRefresh: false);
            return;
        }

        if (_publishedObservedAtUtc is not null)
        {
            OnPropertyChanged(nameof(LiveDataStateText));
        }

        if (_rawDashboard is not null
            && Appearance.ResetTimeDisplay == ResetTimeDisplayMode.Relative)
        {
            PublishActiveDashboard(_rawDashboard);
        }
    }

    private void ApplyResultSurfaceIfVisible()
    {
        if (!IsOptions)
        {
            SurfaceState = _resultSurface;
        }
    }

    private void CancelRefresh()
    {
        _sampleSession.Cancel();
        _liveSession.Cancel();
        _refreshCancellation?.Cancel();
        _refreshCancellation = null;
        IsSampleRefreshing = false;
    }

    private void RebuildSamplePreview()
    {
        if (SelectedSampleScenario is null)
        {
            return;
        }

        PublishActiveDashboard(
            SampleDashboardCatalog.Create(SelectedSampleScenario.Value, GetString));
        CurrentSampleDataState = SampleDataState.Idle;
    }

    public Task MoveDashboardProviderAsync(string providerId, int offset)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        if (offset is not (-1 or 1))
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        return MutateDashboardLayoutAsync(layout =>
            MoveCurrentDashboardProvider(layout, new ProviderId(providerId), offset));
    }

    public Task SetDashboardProviderVisibleAsync(string providerId, bool isVisible)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        var id = new ProviderId(providerId);
        return MutateDashboardLayoutAsync(layout => layout.SetProviderVisible(id, isVisible));
    }

    public Task SetDashboardProviderHighlightedAsync(string providerId, bool isHighlighted)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        var id = new ProviderId(providerId);
        return MutateDashboardLayoutAsync(layout =>
            layout.SetProviderHighlighted(id, isHighlighted));
    }

    public Task SetDashboardProviderColorAsync(string providerId, string colorHex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(colorHex);
        return MutateDashboardLayoutAsync(layout => layout.SetProviderColor(
            new ProviderId(providerId),
            colorHex));
    }

    public Task MoveDashboardMetricAsync(
        string providerId,
        string metricId,
        int offset)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(metricId);
        if (offset is not (-1 or 1))
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        return MutateDashboardLayoutAsync(layout =>
            MoveCurrentDashboardMetric(
                layout,
                new ProviderId(providerId),
                new MetricId(metricId),
                offset));
    }

    public Task SetDashboardMetricVisibleAsync(
        string providerId,
        string metricId,
        bool isVisible)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(metricId);
        return MutateDashboardLayoutAsync(layout => layout.SetMetricVisible(
            new ProviderId(providerId),
            new MetricId(metricId),
            isVisible));
    }

    public Task SetDashboardMetricHighlightedAsync(
        string providerId,
        string metricId,
        bool isHighlighted)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(metricId);
        return MutateDashboardLayoutAsync(layout =>
        {
            var provider = new ProviderId(providerId);
            var metric = new MetricId(metricId);
            ProviderLayoutPreference currentProvider = layout.Providers.Single(item =>
                item.ProviderId == provider);
            MetricLayoutPreference currentMetric = currentProvider.Metrics.Single(item =>
                item.MetricId == metric);
            DashboardLayout next = layout.SetMetricHighlighted(provider, metric, isHighlighted);

            if (isHighlighted && !currentMetric.IsHighlighted && next.Equals(layout))
            {
                DashboardLayoutStatusText = GetString("DashboardMetricHighlightLimitReached");
            }

            return next;
        });
    }

    public Task SetDashboardMetricOnDemandAsync(
        string providerId,
        string metricId,
        bool isOnDemand)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(metricId);
        return MutateDashboardLayoutAsync(layout => layout.SetMetricOnDemand(
            new ProviderId(providerId),
            new MetricId(metricId),
            isOnDemand));
    }

    public Task ResetDashboardLayoutAsync() =>
        MutateDashboardLayoutAsync(_ => DashboardLayout.Empty);

    public async Task UndoDashboardLayoutAsync()
    {
        await _dashboardLayoutInitialization;
        if (IsDashboardLayoutBusy || _rawDashboard is null)
        {
            return;
        }

        IsDashboardLayoutBusy = true;
        try
        {
            DashboardLayoutEditorSaveKind kind = await _layoutEditor
                .UndoAsync()
                .ConfigureAwait(true);
            ApplyLayoutEditorSave(kind);
            if (kind is DashboardLayoutEditorSaveKind.Saved)
            {
                _dashboardLayout = _layoutEditor.Layout;
                PublishActiveDashboard(_rawDashboard);
            }
        }
        finally
        {
            IsDashboardLayoutBusy = false;
            OnPropertyChanged(nameof(CanUndoDashboardLayout));
            OnPropertyChanged(nameof(IsDashboardLayoutEditable));
        }
    }

    private async Task InitializeDashboardLayoutAsync()
    {
        try
        {
            await _layoutEditor.InitializeAsync().ConfigureAwait(true);
            _dashboardLayout = _layoutEditor.Layout;
            DashboardLayoutStatusText = _layoutEditor.LastLoadKind switch
            {
                DashboardLayoutEditorLoadKind.Corrupt when _layoutEditor.QuarantineFileName is string name =>
                    string.Format(
                        System.Globalization.CultureInfo.CurrentCulture,
                        GetString("DashboardLayoutRecoveredFormat"),
                        name),
                DashboardLayoutEditorLoadKind.UnsupportedVersion
                    when _layoutEditor.UnsupportedSchemaVersion is int version =>
                    string.Format(
                        System.Globalization.CultureInfo.CurrentCulture,
                        GetString("DashboardLayoutNewerVersionFormat"),
                        version),
                DashboardLayoutEditorLoadKind.Unavailable => GetString("DashboardLayoutUnavailable"),
                _ => string.Empty,
            };
        }
        finally
        {
            if (_rawDashboard is not null)
            {
                PublishActiveDashboard(_rawDashboard);
            }

            IsDashboardLayoutBusy = false;
            OnPropertyChanged(nameof(IsDashboardLayoutEditable));
            OnPropertyChanged(nameof(CanUndoDashboardLayout));
        }
    }

    private async Task MutateDashboardLayoutAsync(
        Func<DashboardLayout, DashboardLayout> mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        await _dashboardLayoutInitialization;
        if (IsDashboardLayoutBusy || _rawDashboard is null)
        {
            return;
        }

        IsDashboardLayoutBusy = true;
        try
        {
            DashboardLayoutEditorSaveKind kind = await _layoutEditor
                .MutateAsync(mutation)
                .ConfigureAwait(true);
            ApplyLayoutEditorSave(kind);
            if (kind is DashboardLayoutEditorSaveKind.Saved
                or DashboardLayoutEditorSaveKind.Unchanged)
            {
                _dashboardLayout = _layoutEditor.Layout;
                if (kind is DashboardLayoutEditorSaveKind.Saved)
                {
                    DashboardLayoutStatusText = string.Empty;
                }

                PublishActiveDashboard(_rawDashboard);
            }
        }
        finally
        {
            IsDashboardLayoutBusy = false;
            OnPropertyChanged(nameof(CanUndoDashboardLayout));
            OnPropertyChanged(nameof(IsDashboardLayoutEditable));
        }
    }

    private void ApplyLayoutEditorSave(DashboardLayoutEditorSaveKind kind)
    {
        switch (kind)
        {
            case DashboardLayoutEditorSaveKind.RefusedUnsupportedVersion
                when _layoutEditor.UnsupportedSchemaVersion is int version:
                DashboardLayoutStatusText = string.Format(
                    System.Globalization.CultureInfo.CurrentCulture,
                    GetString("DashboardLayoutNewerVersionFormat"),
                    version);
                if (_rawDashboard is not null)
                {
                    PublishActiveDashboard(_rawDashboard);
                }

                break;
            case DashboardLayoutEditorSaveKind.Failed:
                DashboardLayoutStatusText = GetString("DashboardLayoutSaveFailed");
                if (_rawDashboard is not null)
                {
                    PublishActiveDashboard(_rawDashboard);
                }

                break;
        }
    }

    private DashboardLayout MoveCurrentDashboardProvider(
        DashboardLayout layout,
        ProviderId providerId,
        int offset)
    {
        int currentRowIndex = -1;
        for (int index = 0; index < DashboardLayoutProviders.Count; index++)
        {
            if (string.Equals(
                    DashboardLayoutProviders[index].ProviderId,
                    providerId.Value,
                    StringComparison.Ordinal))
            {
                currentRowIndex = index;
                break;
            }
        }

        int targetRowIndex = currentRowIndex + offset;
        if (currentRowIndex < 0
            || targetRowIndex < 0
            || targetRowIndex >= DashboardLayoutProviders.Count)
        {
            return layout;
        }

        var targetId = new ProviderId(DashboardLayoutProviders[targetRowIndex].ProviderId);
        int currentLayoutIndex = FindDashboardProviderIndex(layout, providerId);
        int targetLayoutIndex = FindDashboardProviderIndex(layout, targetId);
        while (currentLayoutIndex != targetLayoutIndex)
        {
            int step = currentLayoutIndex < targetLayoutIndex ? 1 : -1;
            layout = layout.MoveProvider(providerId, step);
            currentLayoutIndex += step;
        }

        return layout;
    }

    private static int FindDashboardProviderIndex(
        DashboardLayout layout,
        ProviderId providerId)
    {
        for (int index = 0; index < layout.Providers.Count; index++)
        {
            if (layout.Providers[index].ProviderId == providerId)
            {
                return index;
            }
        }

        throw new KeyNotFoundException($"Provider '{providerId.Value}' is absent from the dashboard layout.");
    }

    private DashboardLayout MoveCurrentDashboardMetric(
        DashboardLayout layout,
        ProviderId providerId,
        MetricId metricId,
        int offset)
    {
        DashboardProviderLayoutRow providerRow = DashboardLayoutProviders.FirstOrDefault(row =>
            string.Equals(row.ProviderId, providerId.Value, StringComparison.Ordinal))
            ?? throw new KeyNotFoundException(
                $"Provider '{providerId.Value}' is absent from the dashboard layout rows.");

        int currentRowIndex = -1;
        for (int index = 0; index < providerRow.Metrics.Count; index++)
        {
            if (string.Equals(
                    providerRow.Metrics[index].MetricId,
                    metricId.Value,
                    StringComparison.Ordinal))
            {
                currentRowIndex = index;
                break;
            }
        }

        int targetRowIndex = currentRowIndex + offset;
        if (currentRowIndex < 0
            || targetRowIndex < 0
            || targetRowIndex >= providerRow.Metrics.Count)
        {
            return layout;
        }

        var targetId = new MetricId(providerRow.Metrics[targetRowIndex].MetricId);
        ProviderLayoutPreference providerPreference = layout.Providers.Single(provider =>
            provider.ProviderId == providerId);
        int currentLayoutIndex = FindDashboardMetricIndex(providerPreference, metricId);
        int targetLayoutIndex = FindDashboardMetricIndex(providerPreference, targetId);
        while (currentLayoutIndex != targetLayoutIndex)
        {
            int step = currentLayoutIndex < targetLayoutIndex ? 1 : -1;
            layout = layout.MoveMetric(providerId, metricId, step);
            currentLayoutIndex += step;
        }

        return layout;
    }

    private static int FindDashboardMetricIndex(
        ProviderLayoutPreference provider,
        MetricId metricId)
    {
        for (int index = 0; index < provider.Metrics.Count; index++)
        {
            if (provider.Metrics[index].MetricId == metricId)
            {
                return index;
            }
        }

        throw new KeyNotFoundException(
            $"Metric '{metricId.Value}' is absent from provider '{provider.ProviderId.Value}'.");
    }

    private void PublishActiveDashboard(DashboardSnapshot dashboard)
    {
        _rawDashboard = dashboard;
        DashboardSnapshot appearanceDashboard = AppearanceDashboardProjector.Apply(
            dashboard,
            Appearance,
            GetClock(IsSampleModeEnabled ? _activeScenario : null).GetUtcNow(),
            GetString);
        DashboardLayoutProjection projection = DashboardLayoutProjector.Apply(
            appearanceDashboard,
            _dashboardLayout,
            GetString("DashboardProviderHighlightedLabel"),
            new DashboardProviderActionNameFormats(
                GetString("DashboardProviderMoveUpAutomationNameFormat"),
                GetString("DashboardProviderMoveDownAutomationNameFormat"),
                GetString("DashboardProviderVisibilityAutomationNameFormat"),
                GetString("DashboardProviderHighlightAutomationNameFormat"),
                GetString("DashboardProviderMetricsAutomationNameFormat"),
                GetString("DashboardProviderColorAutomationNameFormat")),
            new DashboardMetricActionNameFormats(
                GetString("DashboardMetricMoveUpAutomationNameFormat"),
                GetString("DashboardMetricMoveDownAutomationNameFormat"),
                GetString("DashboardMetricVisibilityAutomationNameFormat"),
                GetString("DashboardMetricHighlightAutomationNameFormat"),
                GetString("DashboardMetricAlwaysVisibleSection"),
                GetString("DashboardMetricOnDemandSection"),
                GetString("DashboardMetricMoveToAlwaysVisibleAutomationNameFormat"),
                GetString("DashboardMetricMoveToOnDemandAutomationNameFormat")),
            CreateDashboardSpendSummary);
        _dashboardLayout = projection.Layout;
        ActiveSample = projection.Dashboard;
        OnPropertyChanged(nameof(LiveDataStateText));
        if (_rawLocalUsage is not null)
        {
            LocalUsage = ApplyDashboardLayoutToLocalUsage(_rawLocalUsage);
        }
        DashboardLayoutProviders = projection.Providers
            .Select(row => row with
            {
                IsMetricsExpanded = _expandedDashboardMetricProviders.Contains(row.ProviderId),
            })
            .ToArray();
        OnPropertyChanged(nameof(AreAllDashboardProvidersHidden));
    }

    private LocalUsageCard ApplyDashboardLayoutToLocalUsage(LocalUsageCard card)
    {
        LocalUsageCard projected = DashboardLayoutProjector.ApplyToLocalUsage(card, _dashboardLayout);
        DashboardSpendSummary spend = CreateDashboardSpendSummary(projected.SpendBreakdown.AgentSlices);
        int providerCount = projected.SpendBreakdown.Models
            .Select(model => model.AgentId)
            .Distinct(StringComparer.Ordinal)
            .Count();
        string summary = string.Format(
            System.Globalization.CultureInfo.CurrentCulture,
            GetString("LocalUsageBreakdownSummaryFormat"),
            providerCount,
            projected.SpendBreakdown.Models.Count);
        string accessibleName = string.Format(
            System.Globalization.CultureInfo.CurrentCulture,
            GetString("LocalUsageBreakdownAccessibleFormat"),
            spend.TotalAmount,
            summary);

        return projected with
        {
            SpendBreakdown = projected.SpendBreakdown with
            {
                SummaryText = summary,
                TotalText = spend.TotalAmount,
                CompactTotalText = spend.CompactTotalAmount,
                AccessibleName = accessibleName,
            },
        };
    }

    private DashboardSpendSummary CreateDashboardSpendSummary(
        IReadOnlyList<SpendSlice> slices)
    {
        if (slices.Count == 0)
        {
            return new DashboardSpendSummary(string.Empty, string.Empty, string.Empty);
        }

        double total = slices.Sum(slice => slice.Amount);
        string totalText = string.Format(
            System.Globalization.CultureInfo.CurrentCulture,
            GetString("LocalUsageUsdFormat"),
            total);
        string compactTotalText = string.Format(
            System.Globalization.CultureInfo.CurrentCulture,
            GetString("LocalUsageUsdCompactFormat"),
            total);
        string details = string.Join(", ", slices.Select(slice =>
            $"{slice.ProviderName} {slice.LegendAmountText}"));
        string accessibleName = string.Format(
            System.Globalization.CultureInfo.CurrentCulture,
            GetString("SampleSpendAccessibleNameFormat"),
            totalText,
            slices.Count,
            details);
        return new DashboardSpendSummary(totalText, compactTotalText, accessibleName);
    }

    public void SetDashboardProviderMetricsExpanded(string providerId, bool isExpanded)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);

        if (isExpanded)
        {
            _expandedDashboardMetricProviders.Add(providerId);
        }
        else
        {
            _expandedDashboardMetricProviders.Remove(providerId);
        }
    }

    private void SetDashboardLayoutReadOnly(string statusText)
    {
        _layoutEditor.MarkReadOnly();
        DashboardLayoutStatusText = statusText;
        OnPropertyChanged(nameof(IsDashboardLayoutEditable));
        OnPropertyChanged(nameof(CanUndoDashboardLayout));
    }

    private TimeProvider GetClock(SampleScenario? scenario) =>
        scenario is null ? _liveSession.Clock : _sampleSession.Clock;

    private string GetString(string key)
    {
        string value = _resources.GetString(key);
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"The resource '{key}' is missing.")
            : value;
    }
}

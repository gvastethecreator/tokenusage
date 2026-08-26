using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Windows.ApplicationModel.Resources;
using TokenUsage.App.Localization;
using TokenUsage.App.Services;
using TokenUsage.App.ViewModels.Surfaces;
using TokenUsage.Core.Appearance;
using TokenUsage.Core.Credentials;
using TokenUsage.Core.Layout;
using Microsoft.UI.Dispatching;
using TokenUsage.Core.Session;
using TokenUsage.Core.Usage;
using TokenUsage.Runtime.Windows;

namespace TokenUsage.App.ViewModels;

public partial class FlyoutViewModel : ObservableObject, IDisposable
{
    private readonly ResourceLoader _resources = new();
    private FlyoutSurfaceState _resultSurface = FlyoutSurfaceState.Loading;
    private bool _disposed;

    public FlyoutViewModel(
        SampleRefreshCoordinator sampleRefreshCoordinator,
        AppSessionHost appSessionHost,
        LocalUsageCoordinator localUsageCoordinator,
        DashboardLayoutStore dashboardLayoutStore,
        AppearanceSettingsStore appearanceSettingsStore,
        QuotaResetHistoryStore quotaResetHistory,
        IManualProviderCredentialStore? manualCredentials = null,
        DataCollectionSettingsStore? dataCollectionSettings = null)
    {
        ArgumentNullException.ThrowIfNull(sampleRefreshCoordinator);
        ArgumentNullException.ThrowIfNull(appSessionHost);
        ArgumentNullException.ThrowIfNull(localUsageCoordinator);
        ArgumentNullException.ThrowIfNull(dashboardLayoutStore);
        ArgumentNullException.ThrowIfNull(appearanceSettingsStore);
        ArgumentNullException.ThrowIfNull(quotaResetHistory);

        OptionsNavigation = new OptionsNavigationViewModel();
        OptionsNavigation.PropertyChanged += OnOptionsNavigationPropertyChanged;
        OptionsNavigation.CloseRequested += OnOptionsNavigationCloseRequested;
        Personalization = new PersonalizationSurfaceViewModel(
            new DashboardLayoutEditor(dashboardLayoutStore),
            GetString);
        AppearanceOptions = new AppearanceSurfaceViewModel(
            new AppearanceSession(appearanceSettingsStore),
            GetString);
        AppearanceOptions.SettingsChanged += OnAppearanceSettingsChanged;
        GeneralOptions = new GeneralOptionsViewModel(GetString, dataCollectionSettings);
        GeneralOptions.BackgroundCollectionChanged += OnBackgroundCollectionChanged;
        GeneralOptions.DataCollectionRefreshChanged += OnDataCollectionRefreshChanged;
        ProviderStatus = new ProviderStatusSurfaceViewModel(GetString, manualCredentials);
        Options = new OptionsSurfaceViewModel(
            OptionsNavigation,
            GeneralOptions,
            AppearanceOptions,
            Personalization,
            ProviderStatus);
        Dashboard = new DashboardSurfaceViewModel(
            new SampleDashboardSession(sampleRefreshCoordinator),
            new LiveDashboardSession(
                appSessionHost,
                localUsageCoordinator,
                quotaResetHistory),
            GeneralOptions,
            AppearanceOptions,
            Personalization,
            ProviderStatus,
            GetString,
            SynchronizationContext.Current);
        Dashboard.PropertyChanged += OnDashboardPropertyChanged;
        _resultSurface = Dashboard.ResultSurface;
        _openRefreshTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _openRefreshTimer.Tick += (_, _) =>
        {
            if (!_disposed)
            {
                Dashboard.RefreshCommand.Execute(null);
            }
        };
        ApplyOpenRefreshInterval(GeneralOptions.SelectedDataCollectionRefresh?.Minutes ?? 0);
    }

    private readonly DispatcherQueueTimer _openRefreshTimer;

    private void OnBackgroundCollectionChanged(object? sender, EventArgs args) =>
        RefreshHookAutoSetup.EnsureInstalled(
            backgroundCollection: GeneralOptions.IsBackgroundCollectionEnabled);

    private void OnDataCollectionRefreshChanged(object? sender, EventArgs args) =>
        ApplyOpenRefreshInterval(GeneralOptions.SelectedDataCollectionRefresh?.Minutes ?? 0);

    private void ApplyOpenRefreshInterval(int minutes)
    {
        if (minutes <= 0)
        {
            _openRefreshTimer.Stop();
            return;
        }

        _openRefreshTimer.Interval = TimeSpan.FromMinutes(minutes);
        _openRefreshTimer.Start();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoading))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(IsOptions))]
    [NotifyPropertyChangedFor(nameof(IsSample))]
    [NotifyPropertyChangedFor(nameof(IsSampleUnavailable))]
    [NotifyPropertyChangedFor(nameof(IsCardSurface))]
    [NotifyPropertyChangedFor(nameof(IsUsageSurface))]
    [NotifyCanExecuteChangedFor(nameof(OpenOptionsCommand))]
    [NotifyCanExecuteChangedFor(nameof(CloseOptionsCommand))]
    public partial FlyoutSurfaceState SurfaceState { get; set; } = FlyoutSurfaceState.Empty;

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int LayoutRevision { get; private set; }

    public OptionsNavigationViewModel OptionsNavigation { get; }

    public GeneralOptionsViewModel GeneralOptions { get; }

    public AppearanceSurfaceViewModel AppearanceOptions { get; }

    public PersonalizationSurfaceViewModel Personalization { get; }

    public ProviderStatusSurfaceViewModel ProviderStatus { get; }

    public DashboardSurfaceViewModel Dashboard { get; }

    public OptionsSurfaceViewModel Options { get; }

    public AppearanceSettings Appearance => AppearanceOptions.Settings;

    public AppSessionHost SessionHost => Dashboard.Host;

    public OptionsSection ActiveOptionsSection => OptionsNavigation.ActiveSection;

    public bool CloseWhenInactive
    {
        get => GeneralOptions.CloseWhenInactive;
        set => GeneralOptions.CloseWhenInactive = value;
    }

    public bool IsSampleModeEnabled
    {
        get => GeneralOptions.IsSampleModeEnabled;
        set => GeneralOptions.IsSampleModeEnabled = value;
    }

    public bool IsLoading => SurfaceState == FlyoutSurfaceState.Loading;

    public bool IsEmpty => SurfaceState == FlyoutSurfaceState.Empty;

    public bool IsOptions => SurfaceState == FlyoutSurfaceState.Options;

    public bool IsSample => SurfaceState == FlyoutSurfaceState.Sample;

    public bool IsSampleUnavailable => SurfaceState == FlyoutSurfaceState.SampleUnavailable;

    public bool IsCardSurface => !IsSample;

    public bool IsUsageSurface => !IsOptions;

    public bool IsSampleContext => IsSampleModeEnabled && !IsOptions;

    public bool IsLiveLoading => IsLoading && !IsSampleModeEnabled;

    public bool IsSampleLoading => IsLoading && IsSampleModeEnabled;

    public bool IsRefreshing => Dashboard.IsRefreshing;

    public int SampleRevealToken => Dashboard.RevealToken;

    public string UnavailableTitle => Dashboard.UnavailableTitle;

    public string UnavailableBody => Dashboard.UnavailableBody;

    public string RetryButtonText => Dashboard.RetryButtonText;

    public string RetryAutomationName => Dashboard.RetryAutomationName;

    public IAsyncRelayCommand RefreshCommand => Dashboard.RefreshCommand;

    public async Task StartAsync()
    {
        await ProviderStatus.LoadManualCredentialsAsync().ConfigureAwait(true);
        await Dashboard.StartAsync().ConfigureAwait(true);
    }

    public void RefreshRelativeTime() => Dashboard.RefreshRelativeTime();

    public void SetPanelVisible(bool isVisible) => Dashboard.SetPanelVisible(isVisible);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        OptionsNavigation.PropertyChanged -= OnOptionsNavigationPropertyChanged;
        OptionsNavigation.CloseRequested -= OnOptionsNavigationCloseRequested;
        AppearanceOptions.SettingsChanged -= OnAppearanceSettingsChanged;
        Dashboard.PropertyChanged -= OnDashboardPropertyChanged;
        Dashboard.Dispose();
        GC.SuppressFinalize(this);
    }

    [RelayCommand(CanExecute = nameof(CanOpenOptions))]
    private void OpenOptions()
    {
        OptionsNavigation.Open();
        SurfaceState = FlyoutSurfaceState.Options;
    }

    private bool CanOpenOptions() => !IsOptions;

    [RelayCommand(CanExecute = nameof(CanCloseOptions))]
    private void CloseOptions() => SurfaceState = _resultSurface;

    private bool CanCloseOptions() => IsOptions;

    private void OnOptionsNavigationCloseRequested(object? sender, EventArgs e) =>
        CloseOptions();

    private void OnOptionsNavigationPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (string.Equals(
                e.PropertyName,
                nameof(OptionsNavigation.ActiveSection),
                StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(ActiveOptionsSection));
        }
    }

    private void OnAppearanceSettingsChanged(
        object? sender,
        AppearanceSettings settings) =>
        OnPropertyChanged(nameof(Appearance));

    private void OnDashboardPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Dashboard.Scope)
            or nameof(Dashboard.ProviderSummaries)
            or nameof(Dashboard.GlobalHeatmap)
            or nameof(Dashboard.SelectedProviderLimits))
        {
            LayoutRevision++;
        }

        if (string.Equals(
                e.PropertyName,
                nameof(Dashboard.ResultSurface),
                StringComparison.Ordinal))
        {
            _resultSurface = Dashboard.ResultSurface;
            if (!IsOptions)
            {
                SurfaceState = _resultSurface;
            }
        }

        if (string.Equals(e.PropertyName, nameof(Dashboard.IsRefreshing), StringComparison.Ordinal)
            || string.Equals(
                e.PropertyName,
                nameof(Dashboard.IsSessionRefreshing),
                StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(IsRefreshing));
        }

        if (string.Equals(e.PropertyName, nameof(Dashboard.RevealToken), StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(SampleRevealToken));
        }

        if (string.Equals(
                e.PropertyName,
                nameof(Dashboard.IsSampleModeEnabled),
                StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(IsSampleModeEnabled));
            OnPropertyChanged(nameof(IsSampleContext));
            OnPropertyChanged(nameof(IsLiveLoading));
            OnPropertyChanged(nameof(IsSampleLoading));
        }

        if (string.Equals(e.PropertyName, nameof(Dashboard.UnavailableTitle), StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(UnavailableTitle));
        }

        if (string.Equals(e.PropertyName, nameof(Dashboard.UnavailableBody), StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(UnavailableBody));
        }

        if (string.Equals(e.PropertyName, nameof(Dashboard.RetryButtonText), StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(RetryButtonText));
        }

        if (string.Equals(e.PropertyName, nameof(Dashboard.RetryAutomationName), StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(RetryAutomationName));
        }
    }

    private string GetString(string key)
    {
        string value = _resources.GetString(key);
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"The resource '{key}' is missing.")
            : value;
    }
}

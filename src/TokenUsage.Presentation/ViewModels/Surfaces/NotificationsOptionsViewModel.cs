using CommunityToolkit.Mvvm.ComponentModel;
using TokenUsage.Core.Alerts;

namespace TokenUsage.App.ViewModels.Surfaces;

public sealed partial class NotificationsOptionsViewModel : ObservableObject
{
    private readonly AlertSettingsStore? _store;
    private bool _initializing = true;
    private Task _pendingSave = Task.CompletedTask;

    public NotificationsOptionsViewModel(AlertSettingsStore? store = null)
    {
        _store = store;
        Initialization = InitializeAsync();
    }

    public Task Initialization { get; }
    public Task WaitForPendingAlertSaveAsync() => _pendingSave;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AreAlertControlsEnabled))]
    public partial bool AreAlertsEnabled { get; set; }
    [ObservableProperty]
    public partial double QuotaAlertThresholdPercent { get; set; } = AlertSettings.Default.QuotaThresholdPercent;
    [ObservableProperty]
    public partial bool IsQuotaThresholdAlertEnabled { get; set; } = AlertSettings.Default.QuotaThresholdEnabled;
    [ObservableProperty]
    public partial bool IsExhaustionForecastAlertEnabled { get; set; } = AlertSettings.Default.ExhaustionForecastEnabled;
    [ObservableProperty]
    public partial bool IsStaleDataAlertEnabled { get; set; } = AlertSettings.Default.StaleDataEnabled;
    [ObservableProperty]
    public partial bool IsCredentialFailureAlertEnabled { get; set; } = AlertSettings.Default.CredentialFailureEnabled;
    [ObservableProperty]
    public partial bool HasSaveError { get; private set; }
    public bool AreAlertControlsEnabled => AreAlertsEnabled;

    private async Task InitializeAsync()
    {
        try
        {
            AlertSettings settings = _store is null ? AlertSettings.Default : await _store.LoadAsync();
            AreAlertsEnabled = settings.Enabled;
            QuotaAlertThresholdPercent = settings.QuotaThresholdPercent;
            IsQuotaThresholdAlertEnabled = settings.QuotaThresholdEnabled;
            IsExhaustionForecastAlertEnabled = settings.ExhaustionForecastEnabled;
            IsStaleDataAlertEnabled = settings.StaleDataEnabled;
            IsCredentialFailureAlertEnabled = settings.CredentialFailureEnabled;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or TimeoutException)
        {
            HasSaveError = true;
        }
        finally { _initializing = false; }
    }

    partial void OnAreAlertsEnabledChanged(bool value) => QueueSave();
    partial void OnQuotaAlertThresholdPercentChanged(double value) => QueueSave();
    partial void OnIsQuotaThresholdAlertEnabledChanged(bool value) => QueueSave();
    partial void OnIsExhaustionForecastAlertEnabledChanged(bool value) => QueueSave();
    partial void OnIsStaleDataAlertEnabledChanged(bool value) => QueueSave();
    partial void OnIsCredentialFailureAlertEnabledChanged(bool value) => QueueSave();

    private void QueueSave()
    {
        if (_initializing || _store is null || !double.IsFinite(QuotaAlertThresholdPercent)
            || QuotaAlertThresholdPercent is < 1 or > 99) return;
        var snapshot = new AlertSettings(AreAlertsEnabled, (int)Math.Round(QuotaAlertThresholdPercent),
            IsQuotaThresholdAlertEnabled, IsExhaustionForecastAlertEnabled,
            IsStaleDataAlertEnabled, IsCredentialFailureAlertEnabled);
        _pendingSave = SaveAfterAsync(_pendingSave, snapshot);
    }

    private async Task SaveAfterAsync(Task previous, AlertSettings snapshot)
    {
        await previous;
        try
        {
            await _store!.SaveAsync(snapshot);
            HasSaveError = false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or TimeoutException)
        {
            HasSaveError = true;
        }
    }
}

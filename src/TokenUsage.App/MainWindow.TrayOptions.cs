using TokenUsage.App.ViewModels;
using TokenUsage.Core.Layout;
using TokenUsage.Platform.Windows.Tray;

namespace TokenUsage.App;

public sealed partial class MainWindow
{
    private readonly Dictionary<string, Func<Task>> _trayOptionActions = new(StringComparer.Ordinal);

    private IReadOnlyList<TrayMenuOption> BuildTrayOptions()
    {
        _trayOptionActions.Clear();
        var options = RootPage.ViewModel.Options;
        var general = options.General;
        var alerts = options.Notifications;
        var appearance = options.Appearance;
        var layout = options.Personalization;

        TrayMenuOption Toggle(string id, string label, Func<bool> read, Action<bool> write,
            Func<bool>? enabled = null) => ToggleAsync(id, label, read,
                value => { write(value); return Task.CompletedTask; }, enabled);

        TrayMenuOption ToggleAsync(string id, string label, Func<bool> read, Func<bool, Task> write,
            Func<bool>? enabled = null)
        {
            _trayOptionActions.Add(id, async () =>
            {
                // Re-read after the menu closes: initialization or a refresh may have completed.
                if (!_disposed && (enabled?.Invoke() ?? true)) await write(!read());
            });
            return new(id, label, read(), enabled?.Invoke() ?? true);
        }

        string Label(string key) => GetString(key.Replace('.', '/'));
        TrayMenuOption Group(string id, string resource, IReadOnlyList<TrayMenuOption> children) =>
            new(id, Label(resource), IsEnabled: children.Count > 0, Children: children);

        var generalItems = new List<TrayMenuOption>
        {
            Toggle("general.close", Label("CloseWhenInactiveLabel.Text"), () => general.CloseWhenInactive,
                value => general.CloseWhenInactive = value),
            Toggle("general.collect", Label("DataCollectionBackgroundLabel.Text"), () => general.IsBackgroundCollectionEnabled,
                value => general.IsBackgroundCollectionEnabled = value, () => general.Initialization.IsCompleted),
            Toggle("general.updates", Label("UpdateAutomaticLabel.Text"), () => options.Updates?.AutomaticUpdatesEnabled is true,
                value => { if (options.Updates is { } updates) updates.AutomaticUpdatesEnabled = value; },
                () => options.Updates?.CanChangePreference is true),
        };
        var providerItems = new List<TrayMenuOption>();
        foreach (var provider in layout.Providers)
        {
            string providerId = provider.ProviderId;
            DashboardProviderLayoutRow CurrentProvider() => layout.Providers.First(row => row.ProviderId == providerId);
            bool CanEditProvider() => layout.IsEditable && layout.Providers.Any(row => row.ProviderId == providerId);
            var children = new List<TrayMenuOption>
            {
                ToggleAsync($"provider.{providerId}.visible", Label("TrayOptionVisible"), () => CurrentProvider().IsVisible,
                    value => layout.SetProviderVisibleAsync(providerId, value), CanEditProvider),
                ToggleAsync($"provider.{providerId}.highlight", Label("TrayOptionTaskbarPreview"), () => CurrentProvider().IsHighlighted,
                    value => layout.SetProviderHighlightedAsync(providerId, value),
                    () => CanEditProvider() && (CurrentProvider().IsHighlighted
                        || layout.Providers.Count(row => row.IsHighlighted) < DashboardLayout.MaxHighlightedProviders)),
            };
            foreach (var metric in provider.Metrics)
            {
                string metricId = metric.MetricId;
                DashboardMetricLayoutRow CurrentMetric() => CurrentProvider().Metrics.First(row => row.MetricId == metricId);
                bool CanEditMetric() => CanEditProvider() && CurrentProvider().Metrics.Any(row => row.MetricId == metricId);
                string prefix = $"provider.{providerId}.metric.{metricId}";
                children.Add(new(prefix, metric.Label, Children:
                [
                    ToggleAsync(prefix + ".visible", Label("TrayOptionVisible"), () => CurrentMetric().IsVisible,
                        value => layout.SetMetricVisibleAsync(providerId, metricId, value), CanEditMetric),
                    ToggleAsync(prefix + ".highlight", Label("TrayOptionHighlighted"), () => CurrentMetric().IsHighlighted,
                        value => layout.SetMetricHighlightedAsync(providerId, metricId, value),
                        () => CanEditMetric() && (CurrentMetric().IsHighlighted
                            || CurrentProvider().Metrics.Count(row => row.IsHighlighted) < DashboardLayout.MaxHighlightedMetricsPerProvider)),
                    ToggleAsync(prefix + ".onDemand", Label("DashboardMetricOnDemandSection"), () => CurrentMetric().IsOnDemand,
                        value => layout.SetMetricOnDemandAsync(providerId, metricId, value), CanEditMetric),
                ]));
            }
            providerItems.Add(new("provider." + providerId, provider.Name, Children: children));
        }

        bool AlertsReady() => alerts.Initialization.IsCompleted;
        bool AlertRulesEnabled() => AlertsReady() && alerts.AreAlertControlsEnabled;
        return
        [
            Group("general", "OptionsGeneralTabLabel.Text", generalItems),
            Group("notifications", "OptionsNotificationsTabLabel.Text",
            [
                Toggle("alerts.enabled", Label("AlertsMasterLabel.Text"), () => alerts.AreAlertsEnabled,
                    value => alerts.AreAlertsEnabled = value, AlertsReady),
                Toggle("alerts.threshold", Label("QuotaThresholdAlertLabel.Text"), () => alerts.IsQuotaThresholdAlertEnabled,
                    value => alerts.IsQuotaThresholdAlertEnabled = value, AlertRulesEnabled),
                Toggle("alerts.forecast", Label("ExhaustionForecastAlertLabel.Text"), () => alerts.IsExhaustionForecastAlertEnabled,
                    value => alerts.IsExhaustionForecastAlertEnabled = value, AlertRulesEnabled),
                Toggle("alerts.stale", Label("StaleDataAlertLabel.Text"), () => alerts.IsStaleDataAlertEnabled,
                    value => alerts.IsStaleDataAlertEnabled = value, AlertRulesEnabled),
                Toggle("alerts.credentials", Label("CredentialFailureAlertLabel.Text"), () => alerts.IsCredentialFailureAlertEnabled,
                    value => alerts.IsCredentialFailureAlertEnabled = value, AlertRulesEnabled),
            ]),
            Group("appearance", "OptionsAppearanceTabLabel.Text",
            [
                Toggle("appearance.transparency", Label("AppearanceTransparencyLabel.Text"), () => appearance.IncreaseTransparency,
                    value => appearance.IncreaseTransparency = value, () => appearance.IsEditable),
                Toggle("appearance.popover", Label("AppearanceTrayPopoverHeading.Text"), () => appearance.ShowTrayPopover,
                    value => appearance.ShowTrayPopover = value, () => appearance.IsEditable),
                Toggle("appearance.providerName", Label("AppearanceTrayProviderNameLabel.Text"), () => appearance.ShowTrayPopoverProviderName,
                    value => appearance.ShowTrayPopoverProviderName = value, () => appearance.IsEditable),
            ]),
            Group("layout", "DashboardLayoutExpander.Header", providerItems),
        ];
    }

    private void OnTrayOptionInvoked(object? sender, TrayOptionInvokedEventArgs e)
    {
        if (_trayOptionActions.TryGetValue(e.Id, out var action))
            _ = DispatcherQueue.TryEnqueue(async () => await action());
    }
}

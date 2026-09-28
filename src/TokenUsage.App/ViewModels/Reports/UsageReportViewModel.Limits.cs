using TokenUsage.App.ViewModels.Dashboard;

namespace TokenUsage.App.ViewModels.Reports;

public sealed record UsageReportLimitItem(string ProviderId, string ProviderName, QuotaWindow Window, bool ShowsProvider)
{
    public string Title => ShowsProvider ? ProviderName + " · " + Window.Title : Window.Title;

    public string AutomationName => ShowsProvider ? ProviderName + ", " + Window.DisplayAutomationName : Window.DisplayAutomationName;
}

// Quota windows sit near the top of the report: Global lists every provider that reports them,
// Provider shows the selected one. The main panel owns the readings, so the report asks again
// whenever the panel publishes new ones instead of keeping what it saw when it opened.
public sealed partial class UsageReportViewModel
{
    public IReadOnlyList<UsageReportLimitItem> LimitItems { get; private set; } = [];

    public bool HasLimitItems => LimitItems.Count > 0;

    public void RefreshLimits()
    {
        RefreshProviderDetails();
        OnPropertyChanged(nameof(ResetCycleAvailabilityText));
    }

    private void RebuildLimitItems()
    {
        UsageReportLimitItem[] items = IsProviderScope
            ? _selectedProvider is null
                ? []
                : [.. ProviderLimits.Select(window => new UsageReportLimitItem(
                    _selectedProvider.ProviderId, _selectedProvider.Name, window, ShowsProvider: false))]
            : IsGlobalScope
                ? [.. ProviderOptions.SelectMany(option => _getProviderLimits(option.ProviderId)
                    .Select(window => new UsageReportLimitItem(option.ProviderId, option.Name, window, ShowsProvider: true)))]
                : [];
        // The panel republishes often; unchanged readings keep their bars instead of redrawing.
        if (items.SequenceEqual(LimitItems)) return;
        LimitItems = items;
        OnPropertyChanged(nameof(LimitItems));
        OnPropertyChanged(nameof(HasLimitItems));
    }
}

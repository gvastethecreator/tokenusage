using TokenUsage.App.ViewModels.Dashboard;

namespace TokenUsage.App.ViewModels.Reports;

public sealed record UsageReportLimitItem(string ProviderId, string ProviderName, QuotaWindow Window)
{
    public string Title => ProviderName + " · " + Window.Title;

    public string AutomationName => ProviderName + ", " + Window.DisplayAutomationName;
}

// Quota windows sit near the top of the report: Global lists every provider that reports them,
// Provider shows the selected one. The main panel owns the readings, so the report asks again
// whenever the panel publishes new ones instead of keeping what it saw when it opened.
public sealed partial class UsageReportViewModel
{
    private readonly Func<IReadOnlyList<CodexAccountQuota>> _getAccountQuotas;
    private readonly Func<bool> _usesCodexAccounts;
    public IReadOnlyList<CodexAccountQuota> AccountQuotas { get; private set; } = [];
    public bool UsesCodexAccounts => _usesCodexAccounts();
    public IReadOnlyList<UsageReportLimitItem> LimitItems { get; private set; } = [];

    public bool HasLimitItems => LimitItems.Count > 0 || AccountQuotas.Count > 0;

    public void RefreshLimits()
    {
        RefreshProviderDetails();
        OnPropertyChanged(nameof(ResetCycleAvailabilityText));
    }

    private void RebuildLimitItems()
    {
        AccountQuotas = IsGlobalScope || (IsProviderScope && _selectedProvider?.ProviderId == "codex")
            ? _getAccountQuotas() : [];
        OnPropertyChanged(nameof(AccountQuotas));
        OnPropertyChanged(nameof(UsesCodexAccounts));
        OnPropertyChanged(nameof(HasLimitItems));
        UsageReportLimitItem[] items = IsProviderScope
            ? _selectedProvider is null
                ? []
                : [.. ProviderLimits.Select(window => new UsageReportLimitItem(
                    _selectedProvider.ProviderId, _selectedProvider.Name, window))]
            : IsGlobalScope
                ? [.. ProviderOptions.SelectMany(option => _getProviderLimits(option.ProviderId)
                    .Select(window => new UsageReportLimitItem(option.ProviderId, option.Name, window)))]
                : [];
        // The panel republishes often; unchanged readings keep their bars instead of redrawing.
        if (items.SequenceEqual(LimitItems)) return;
        LimitItems = items;
        OnPropertyChanged(nameof(LimitItems));
        OnPropertyChanged(nameof(HasLimitItems));
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WOpenUsage.App.ViewModels.Dashboard;
using WOpenUsage.Core.Providers;

namespace WOpenUsage.App.ViewModels.Surfaces;

public sealed partial class ProviderStatusSurfaceViewModel : ObservableObject
{
    private readonly Func<string, string> _getString;
    private Func<Task>? _refresh;

    public ProviderStatusSurfaceViewModel(Func<string, string> getString)
    {
        _getString = getString ?? throw new ArgumentNullException(nameof(getString));
    }

    [ObservableProperty]
    public partial IReadOnlyList<ProviderStatusRow> Providers { get; private set; } = [];

    public void Update(
        ProviderOutcome? codexOutcome,
        bool hasPublishedDashboard,
        SampleDataState dataState,
        IReadOnlyList<ProviderStatusRow> localProviders)
    {
        ArgumentNullException.ThrowIfNull(localProviders);
        ProviderStatusRow codex = CreateCodex(
            codexOutcome,
            hasPublishedDashboard,
            dataState);
        ProviderStatusRow? localCodex = localProviders.FirstOrDefault(row => string.Equals(
            row.ProviderId,
            "codex",
            StringComparison.Ordinal));
        if (localCodex is not null)
        {
            codex = MergeCodex(codex, localCodex);
        }

        var providers = new List<ProviderStatusRow> { codex };
        providers.AddRange(localProviders.Where(row => !string.Equals(
            row.ProviderId,
            "codex",
            StringComparison.Ordinal)));
        Providers = providers;
    }

    public void BindRefresh(Func<Task> refresh)
    {
        ArgumentNullException.ThrowIfNull(refresh);
        if (_refresh is not null)
        {
            throw new InvalidOperationException("Provider status refresh is already bound.");
        }

        _refresh = refresh;
    }

    public void UnbindRefresh() => _refresh = null;

    [RelayCommand]
    private Task RefreshAsync() => _refresh?.Invoke() ?? Task.CompletedTask;

    private ProviderStatusRow CreateCodex(
        ProviderOutcome? outcome,
        bool hasPublishedDashboard,
        SampleDataState dataState) =>
        new(
            "codex",
            _getString("LocalUsageAgentCodex"),
            outcome is ProviderOutcome.NotConfigured
                ? _getString("ProviderStatusRootMissing")
                : outcome is null && !hasPublishedDashboard
                    ? _getString("ProviderStatusRootPending")
                    : _getString("ProviderStatusRootDetected"),
            _getString(outcome switch
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
                    _getString("ProviderStatusQuota"),
                    GetQuotaStatus(outcome, dataState),
                    "ProviderStatus.codex.Quota"),
                new(
                    _getString("ProviderStatusUsage"),
                    _getString("ProviderStatusUnavailable"),
                    "ProviderStatus.codex.Usage"),
                new(
                    _getString("ProviderStatusSpend"),
                    _getString("ProviderStatusUnavailable"),
                    "ProviderStatus.codex.Spend"),
                new(
                    _getString("ProviderStatusCoverage"),
                    _getString("CodexUsageMissing"),
                    "ProviderStatus.codex.Coverage"),
            ],
            "ProviderStatus.codex");

    private string GetQuotaStatus(ProviderOutcome? outcome, SampleDataState dataState) =>
        outcome switch
        {
            ProviderOutcome.NotConfigured => _getString("ProviderStatusNotConfigured"),
            ProviderOutcome.UnsupportedAccount => _getString("ProviderStatusUnsupported"),
            ProviderOutcome.PolicyBlocked => _getString("ProviderStatusBlocked"),
            ProviderOutcome.ContractFailure => _getString("ProviderStatusContractChanged"),
            ProviderOutcome.Throttled or ProviderOutcome.TransientFailure =>
                _getString("ProviderStatusPartial"),
            _ => dataState switch
            {
                SampleDataState.Partial => _getString("ProviderStatusPartial"),
                SampleDataState.Fresh or SampleDataState.CacheRefreshing
                    or SampleDataState.StaleCacheRefreshing or SampleDataState.Stale
                    or SampleDataState.NotSaved => _getString("ProviderStatusAvailable"),
                _ => _getString("ProviderStatusUnavailable"),
            },
        };

    private static ProviderStatusRow MergeCodex(
        ProviderStatusRow quotaStatus,
        ProviderStatusRow localStatus)
    {
        ProviderCapabilityRow quota = quotaStatus.Capabilities.Single(capability =>
            string.Equals(
                capability.AutomationId,
                "ProviderStatus.codex.Quota",
                StringComparison.Ordinal));
        ProviderCapabilityRow[] localCapabilities = localStatus.Capabilities
            .Where(capability => !string.Equals(
                capability.AutomationId,
                "ProviderStatus.codex.Quota",
                StringComparison.Ordinal))
            .ToArray();
        return quotaStatus with
        {
            RootState = localStatus.RootState,
            RecoveryText = localStatus.RecoveryText,
            Capabilities = new[] { quota }.Concat(localCapabilities).ToArray(),
        };
    }
}

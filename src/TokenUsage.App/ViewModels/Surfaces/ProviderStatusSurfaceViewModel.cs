using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TokenUsage.App.ViewModels.Dashboard;
using TokenUsage.Core.Providers;
using TokenUsage.Providers.Catalog;

namespace TokenUsage.App.ViewModels.Surfaces;

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
        HashSet<string> includedIds = providers
            .Select(provider => provider.ProviderId)
            .ToHashSet(StringComparer.Ordinal);
        providers.AddRange(ProviderModuleCatalog.OpenUsageEntries
            .Where(entry => entry.Stage is ProviderModuleStage.OptIn
                or ProviderModuleStage.Prepared
                or ProviderModuleStage.PolicyBlocked)
            .Where(entry => !includedIds.Contains(entry.Id.Value))
            .Select(CreateFoundationRow));
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
            "ProviderStatus.codex")
        {
            StatusKind = GetCodexStatusKind(outcome, hasPublishedDashboard, dataState),
            CompactState = GetCompactState(
                GetCodexStatusKind(outcome, hasPublishedDashboard, dataState)),
        };

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

    private ProviderStatusRow CreateFoundationRow(ProviderModuleDefinition module)
    {
        bool isBlocked = module.Stage == ProviderModuleStage.PolicyBlocked;
        bool isOptional = module.Stage == ProviderModuleStage.OptIn;
        string stage = _getString(module.Stage switch
        {
            ProviderModuleStage.PolicyBlocked => "ProviderStatusBlocked",
            ProviderModuleStage.OptIn => "ProviderStatusOptional",
            _ => "ProviderStatusPrepared",
        });
        string unavailable = _getString("ProviderStatusUnavailable");
        string CapabilityValue(params ProviderCapability[] capabilities) => isBlocked
            ? stage
            : capabilities.Any(module.Capabilities.Contains)
                ? stage
                : unavailable;

        string providerId = module.Id.Value;
        return new ProviderStatusRow(
            providerId,
            module.DisplayName,
            stage,
            _getString(module.Stage switch
            {
                ProviderModuleStage.PolicyBlocked => "ProviderStatusRecoveryPolicyContract",
                ProviderModuleStage.OptIn => "ProviderStatusRecoveryOptional",
                _ => "ProviderStatusRecoveryManualSetup",
            }),
            [
                new(
                    _getString("ProviderStatusQuota"),
                    CapabilityValue(ProviderCapability.Limits),
                    $"ProviderStatus.{providerId}.Quota"),
                new(
                    _getString("ProviderStatusUsage"),
                    CapabilityValue(ProviderCapability.Usage, ProviderCapability.LocalUsage),
                    $"ProviderStatus.{providerId}.Usage"),
                new(
                    _getString("ProviderStatusSpend"),
                    CapabilityValue(ProviderCapability.Spend),
                    $"ProviderStatus.{providerId}.Spend"),
                new(
                    _getString("ProviderStatusCoverage"),
                    stage,
                    $"ProviderStatus.{providerId}.Coverage"),
            ],
            $"ProviderStatus.{providerId}")
        {
            StatusKind = isBlocked
                ? ProviderStatusKind.Blocked
                : isOptional
                    ? ProviderStatusKind.Optional
                    : ProviderStatusKind.Prepared,
            CompactState = GetCompactState(isBlocked
                ? ProviderStatusKind.Blocked
                : isOptional
                    ? ProviderStatusKind.Optional
                    : ProviderStatusKind.Prepared),
        };
    }

    private static ProviderStatusKind GetCodexStatusKind(
        ProviderOutcome? outcome,
        bool hasPublishedDashboard,
        SampleDataState dataState) =>
        outcome switch
        {
            ProviderOutcome.NotConfigured => ProviderStatusKind.Missing,
            ProviderOutcome.UnsupportedAccount or ProviderOutcome.PolicyBlocked =>
                ProviderStatusKind.Blocked,
            ProviderOutcome.ContractFailure or ProviderOutcome.Throttled
                or ProviderOutcome.TransientFailure => ProviderStatusKind.Partial,
            null when !hasPublishedDashboard => ProviderStatusKind.Pending,
            _ when dataState == SampleDataState.Partial => ProviderStatusKind.Partial,
            _ => ProviderStatusKind.Available,
        };

    private string GetCompactState(ProviderStatusKind kind) =>
        _getString(kind switch
        {
            ProviderStatusKind.Available => "ProviderStatusSummaryAvailable",
            ProviderStatusKind.Partial => "ProviderStatusSummaryPartial",
            ProviderStatusKind.Missing => "ProviderStatusSummaryMissing",
            ProviderStatusKind.Pending => "ProviderStatusSummaryPending",
            ProviderStatusKind.Prepared => "ProviderStatusSummaryPrepared",
            ProviderStatusKind.Optional => "ProviderStatusSummaryOptional",
            ProviderStatusKind.Blocked => "ProviderStatusSummaryBlocked",
            _ => "ProviderStatusUnavailable",
        });

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
            StatusKind = localStatus.StatusKind,
            CompactState = localStatus.CompactState,
        };
    }
}

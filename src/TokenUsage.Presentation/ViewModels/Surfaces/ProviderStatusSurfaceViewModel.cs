using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Globalization;
using TokenUsage.App.ViewModels.Dashboard;
using TokenUsage.Core.Credentials;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;
using TokenUsage.Providers.Catalog;

namespace TokenUsage.App.ViewModels.Surfaces;

public readonly record struct ManualCredentialOperationResult(bool Succeeded, string StatusText);

public sealed partial class ProviderStatusSurfaceViewModel : ObservableObject
{
    private static readonly string[] PrimaryProviderIds =
    [
        "codex",
        "claude",
        "grok",
        "opencode",
        "antigravity",
        "cursor",
        "copilot",
        "zcode",
    ];

    private readonly Func<string, string> _getString;
    private readonly IManualProviderCredentialStore? _manualCredentials;
    private readonly HashSet<string> _configuredManualIds = new(StringComparer.Ordinal);
    private Func<Task>? _refresh;
    private ProviderOutcome? _codexOutcome;
    private bool _hasPublishedDashboard;
    private SampleDataState _dataState;
    private IReadOnlyList<ProviderStatusRow> _localProviders = [];
    private bool _hasSnapshot;

    public ProviderStatusSurfaceViewModel(
        Func<string, string> getString,
        IManualProviderCredentialStore? manualCredentials = null)
    {
        _getString = getString ?? throw new ArgumentNullException(nameof(getString));
        _manualCredentials = manualCredentials;
    }

    [ObservableProperty]
    public partial IReadOnlyList<ProviderStatusRow> Providers { get; private set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<ProviderStatusRow> PrimaryProviders { get; private set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<ProviderStatusRow> AdditionalProviders { get; private set; } = [];

    [ObservableProperty]
    public partial bool IsAdditionalProvidersExpanded { get; set; }

    public bool HasAdditionalProviders => AdditionalProviders.Count > 0;

    public string AdditionalProvidersToggleLabel => string.Format(
        CultureInfo.CurrentCulture,
        _getString(IsAdditionalProvidersExpanded
            ? "ProviderStatusShowLessFormat"
            : "ProviderStatusShowMoreFormat"),
        AdditionalProviders.Count);

    public string AdditionalProvidersToggleGlyph =>
        IsAdditionalProvidersExpanded ? "\uE738" : "\uE710";

    public async Task LoadManualCredentialsAsync(CancellationToken cancellationToken = default)
    {
        _configuredManualIds.Clear();
        if (_manualCredentials is null)
        {
            if (_hasSnapshot)
            {
                Project();
            }

            return;
        }

        foreach (ProviderModuleDefinition entry in ProviderModuleCatalog.ManualCredentialEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (await _manualCredentials
                    .IsConfiguredAsync(entry.Id.Value, cancellationToken)
                    .ConfigureAwait(true))
                {
                    _configuredManualIds.Add(entry.Id.Value);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // Presence checks must not block startup. Save and remove still report locker errors.
            }
        }

        if (_hasSnapshot)
        {
            Project();
        }
    }

    public async Task<ManualCredentialOperationResult> SaveManualCredentialAsync(
        string providerId,
        string apiKey,
        string? secondaryValue,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        if (_manualCredentials is null)
        {
            return new(false, _getString("ProviderCredentialNotSupported"));
        }

        ProviderModuleDefinition module;
        try
        {
            module = ProviderModuleCatalog.Get(providerId);
        }
        catch (InvalidOperationException)
        {
            return new(false, _getString("ProviderCredentialNotSupported"));
        }

        if (!module.AcceptsManualCredential)
        {
            return new(false, _getString("ProviderCredentialNotSupported"));
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new(false, _getString("ProviderCredentialMissingKey"));
        }

        string? secondary = string.IsNullOrWhiteSpace(secondaryValue)
            ? null
            : secondaryValue.Trim();
        if (module.ManualCredentialKind.RequiresSecondaryField() && secondary is null)
        {
            return new(false, _getString("ProviderCredentialMissingSecondary"));
        }

        try
        {
            await _manualCredentials
                .SaveAsync(providerId, new ManualProviderSecret(apiKey, secondary), cancellationToken)
                .ConfigureAwait(true);
        }
        catch (ArgumentException)
        {
            return new(false, _getString("ProviderCredentialMissingSecondary"));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return new(false, _getString("ProviderCredentialLocalFailure"));
        }

        _configuredManualIds.Add(providerId);
        if (_hasSnapshot)
        {
            Project();
        }

        return new(true, _getString("ProviderCredentialSaved"));
    }

    public async Task<ManualCredentialOperationResult> DeleteManualCredentialAsync(
        string providerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        if (_manualCredentials is null)
        {
            return new(false, _getString("ProviderCredentialNotSupported"));
        }

        try
        {
            _ = await _manualCredentials
                .DeleteAsync(providerId, cancellationToken)
                .ConfigureAwait(true);
        }
        catch (ArgumentException)
        {
            return new(false, _getString("ProviderCredentialNotSupported"));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return new(false, _getString("ProviderCredentialLocalFailure"));
        }

        _configuredManualIds.Remove(providerId);
        if (_hasSnapshot)
        {
            Project();
        }

        return new(true, _getString("ProviderCredentialRemoved"));
    }

    public void Update(
        ProviderOutcome? codexOutcome,
        bool hasPublishedDashboard,
        SampleDataState dataState,
        IReadOnlyList<ProviderStatusRow> localProviders)
    {
        ArgumentNullException.ThrowIfNull(localProviders);
        _codexOutcome = codexOutcome;
        _hasPublishedDashboard = hasPublishedDashboard;
        _dataState = dataState;
        _localProviders = localProviders;
        _hasSnapshot = true;
        Project();
    }

    private void Project()
    {
        ProviderOutcome? codexOutcome = _codexOutcome;
        bool hasPublishedDashboard = _hasPublishedDashboard;
        SampleDataState dataState = _dataState;
        IReadOnlyList<ProviderStatusRow> localProviders = _localProviders;
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
        providers.AddRange(ProviderModuleCatalog.Entries
            .Where(entry => !includedIds.Contains(entry.Id.Value))
            .Select(CreateCatalogRow));
        Providers = providers;
        PrimaryProviders = PrimaryProviderIds
            .Select(id => providers.FirstOrDefault(provider => string.Equals(
                provider.ProviderId,
                id,
                StringComparison.Ordinal)))
            .Where(provider => provider is not null)
            .Cast<ProviderStatusRow>()
            .ToArray();
        HashSet<string> primaryIds = PrimaryProviders
            .Select(provider => provider.ProviderId)
            .ToHashSet(StringComparer.Ordinal);
        AdditionalProviders = providers
            .Where(provider => !primaryIds.Contains(provider.ProviderId))
            .ToArray();
    }

    public string CredentialBusyText => _getString("ProviderCredentialBusy");

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

    partial void OnAdditionalProvidersChanged(IReadOnlyList<ProviderStatusRow> value)
    {
        if (value.Count == 0)
        {
            IsAdditionalProvidersExpanded = false;
        }

        OnPropertyChanged(nameof(HasAdditionalProviders));
        OnPropertyChanged(nameof(AdditionalProvidersToggleLabel));
    }

    partial void OnIsAdditionalProvidersExpandedChanged(bool value) =>
        NotifyAdditionalProvidersExpansionChanged();

    private void NotifyAdditionalProvidersExpansionChanged()
    {
        OnPropertyChanged(nameof(AdditionalProvidersToggleLabel));
        OnPropertyChanged(nameof(AdditionalProvidersToggleGlyph));
    }

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

    private ProviderStatusRow CreateCatalogRow(ProviderModuleDefinition module)
    {
        bool isActive = module.Stage == ProviderModuleStage.Active;
        bool isBlocked = module.Stage == ProviderModuleStage.PolicyBlocked;
        bool isOptional = module.Stage == ProviderModuleStage.OptIn;
        bool canConfigure = module.AcceptsManualCredential;
        bool hasSavedCredential = canConfigure && _configuredManualIds.Contains(module.Id.Value);
        string stage = _getString(module.Stage switch
        {
            ProviderModuleStage.Active => "ProviderStatusUnavailable",
            ProviderModuleStage.PolicyBlocked => "ProviderStatusBlocked",
            ProviderModuleStage.OptIn => "ProviderStatusOptional",
            _ => "ProviderStatusPrepared",
        });
        string rootState = hasSavedCredential
            ? _getString("ProviderStatusRootKeySaved")
            : stage;
        string unavailable = _getString("ProviderStatusUnavailable");
        string CapabilityValue(params ProviderCapability[] capabilities) => isActive
            ? unavailable
            : isBlocked
            ? stage
            : capabilities.Any(module.Capabilities.Contains)
                ? stage
                : unavailable;

        string providerId = module.Id.Value;
        ProviderStatusKind statusKind = isActive
            ? ProviderStatusKind.Missing
            : isBlocked
            ? ProviderStatusKind.Blocked
            : isOptional
                ? ProviderStatusKind.Optional
                : ProviderStatusKind.Prepared;
        (string secondaryLabel, string secondaryPlaceholder) = GetSecondaryField(
            providerId,
            module.ManualCredentialKind);
        bool isCopilot = string.Equals(providerId, "copilot", StringComparison.Ordinal);
        return new ProviderStatusRow(
            providerId,
            module.DisplayName,
            rootState,
            _getString(hasSavedCredential
                ? "ProviderStatusRecoveryKeySaved"
                : module.Stage switch
                {
                    ProviderModuleStage.Active => "ProviderStatusRecoveryRefresh",
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
            StatusKind = statusKind,
            CompactState = hasSavedCredential
                ? _getString("ProviderStatusSummaryKeySaved")
                : GetCompactState(statusKind),
            CanConfigure = canConfigure,
            HasSavedCredential = hasSavedCredential,
            RequiresSecondaryField = module.ManualCredentialKind.RequiresSecondaryField(),
            SecondaryFieldLabel = secondaryLabel,
            SecondaryFieldPlaceholder = secondaryPlaceholder,
            CredentialHelpText = isCopilot
                ? _getString("ProviderCredentialCopilotHelp")
                : string.Empty,
            SecretFieldLabel = isCopilot
                ? _getString("ProviderCredentialCopilotTokenLabel")
                : string.Empty,
            SecretFieldPlaceholder = isCopilot
                ? _getString("ProviderCredentialCopilotTokenPlaceholder")
                : string.Empty,
            ConfigureAutomationName = string.Format(
                CultureInfo.CurrentCulture,
                _getString("ProviderConfigureButtonAutomationFormat"),
                module.DisplayName),
        };
    }

    private (string Label, string Placeholder) GetSecondaryField(
        string providerId,
        ManualCredentialKind kind)
    {
        if (string.Equals(providerId, "copilot", StringComparison.Ordinal))
        {
            return (
                _getString("ProviderCredentialCopilotOrganizationLabel"),
                _getString("ProviderCredentialCopilotOrganizationPlaceholder"));
        }

        return kind switch
        {
            ManualCredentialKind.ApiKeyAndOptionalKeyId => (
                _getString("ProviderCredentialKeyIdLabel"),
                _getString("ProviderCredentialKeyIdPlaceholder")),
            ManualCredentialKind.ApiKeyAndOptionalOrganization
                or ManualCredentialKind.ApiKeyAndOrganization => (
                _getString("ProviderCredentialOrganizationLabel"),
                _getString("ProviderCredentialOrganizationPlaceholder")),
            ManualCredentialKind.ApiKeyAndEndpoint => (
                _getString("ProviderCredentialEndpointLabel"),
                _getString("ProviderCredentialEndpointPlaceholder")),
            _ => (string.Empty, string.Empty),
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
        _getString(ProviderStatusPolicy.CompactStateKey(kind));

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

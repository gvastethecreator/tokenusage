using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WOpenUsage.App.ViewModels.Dashboard;
using WOpenUsage.App.ViewModels.Sample;
using WOpenUsage.Core.Cache;
using WOpenUsage.Core.Providers;
using WOpenUsage.Providers.VercelAiGateway;
using WOpenUsage.Runtime.Windows.VercelAiGateway;

namespace WOpenUsage.App.ViewModels;

public enum VercelGatewayUiState
{
    Checking,
    NotConfigured,
    Connecting,
    Refreshing,
    Connected,
    Partial,
    Throttled,
    AuthenticationRejected,
    UnsupportedAccount,
    TransientFailure,
    ContractFailure,
    Disconnecting,
    Disconnected,
    CleanupPartial,
    LocalFailure,
}

public partial class VercelGatewaySettingsViewModel : ObservableObject
{
    private readonly VercelGatewayRefreshCoordinator _coordinator;
    private readonly Func<string, string> _getString;
    private CancellationTokenSource? _operationCancellation;
    private ProviderSnapshot? _lastSnapshot;

    public VercelGatewaySettingsViewModel(
        VercelGatewayRefreshCoordinator coordinator,
        Func<string, string> getString)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _getString = getString ?? throw new ArgumentNullException(nameof(getString));
        StatusText = _getString("VercelStatusChecking");
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnectFormVisible))]
    [NotifyPropertyChangedFor(nameof(IsDisconnectVisible))]
    [NotifyPropertyChangedFor(nameof(CanSubmitConnection))]
    [NotifyCanExecuteChangedFor(nameof(DisconnectCommand))]
    public partial bool IsConfigured { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmitConnection))]
    [NotifyPropertyChangedFor(nameof(IsDisconnectVisible))]
    [NotifyPropertyChangedFor(nameof(IsInputEnabled))]
    [NotifyCanExecuteChangedFor(nameof(DisconnectCommand))]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmitConnection))]
    public partial bool IsConsentAccepted { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmitConnection))]
    public partial bool HasApiKeyInput { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmitConnection))]
    [NotifyPropertyChangedFor(nameof(IsKeyIdErrorVisible))]
    public partial bool IsKeyIdInputValid { get; private set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStatusOpen))]
    public partial string StatusText { get; private set; }

    [ObservableProperty]
    public partial VercelGatewayUiState State { get; private set; } =
        VercelGatewayUiState.Checking;

    [ObservableProperty]
    public partial ProviderCard? ProviderCard { get; private set; }

    [ObservableProperty]
    public partial SpendSlice? SpendSlice { get; private set; }

    public bool IsConnectFormVisible => !IsConfigured;

    public bool IsDisconnectVisible => IsConfigured && !IsBusy;

    public bool IsInputEnabled => !IsBusy;

    public bool CanSubmitConnection =>
        !IsConfigured
        && !IsBusy
        && IsConsentAccepted
        && HasApiKeyInput
        && IsKeyIdInputValid;

    public bool IsKeyIdErrorVisible => !IsKeyIdInputValid;

    public bool IsStatusOpen => !string.IsNullOrWhiteSpace(StatusText);

    public void SetApiKeyInputPresence(string? apiKey)
    {
        HasApiKeyInput = !string.IsNullOrWhiteSpace(apiKey);
    }

    public void SetKeyIdInput(string? keyId)
    {
        if (string.IsNullOrWhiteSpace(keyId))
        {
            IsKeyIdInputValid = true;
            return;
        }

        try
        {
            _ = new VercelGatewayConnection(
                "validation-placeholder",
                keyId);
            IsKeyIdInputValid = true;
        }
        catch (ArgumentException)
        {
            IsKeyIdInputValid = false;
        }
    }

    public async Task InitializeAsync()
    {
        if (IsBusy)
        {
            return;
        }

        using CancellationTokenSource cancellation = BeginOperation();
        IsBusy = true;
        SetState(VercelGatewayUiState.Checking, "VercelStatusChecking");
        try
        {
            IsConfigured = await _coordinator.Connections
                .IsConfiguredAsync(cancellation.Token);
            if (IsConfigured)
            {
                await RefreshCoreAsync(forceRefresh: false, cancellation.Token);
            }
            else
            {
                SetState(VercelGatewayUiState.NotConfigured, "VercelStatusNotConfigured");
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsExpectedLocalFailure(exception))
        {
            SetState(VercelGatewayUiState.LocalFailure, "VercelStatusLocalFailure");
        }
        finally
        {
            EndOperation(cancellation);
        }
    }

    public Task ConnectAsync(string apiKey) => ConnectAsync(apiKey, keyId: null);

    public async Task ConnectAsync(string apiKey, string? keyId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        SetKeyIdInput(keyId);
        if (!CanSubmitConnection)
        {
            return;
        }

        using CancellationTokenSource cancellation = BeginOperation();
        IsBusy = true;
        SetState(VercelGatewayUiState.Connecting, "VercelStatusConnecting");
        try
        {
            VercelGatewayConnectResult result = string.IsNullOrWhiteSpace(keyId)
                ? await _coordinator.Connections
                    .ConnectAsync(apiKey, cancellation.Token)
                : await _coordinator.Connections
                    .ConnectAsync(apiKey, keyId, cancellation.Token);
            if (!result.IsComplete)
            {
                IsConfigured = await _coordinator.Connections
                    .IsConfiguredAsync(CancellationToken.None);
                SetState(
                    VercelGatewayUiState.CleanupPartial,
                    CleanupMessageKey(result.CacheStatus));
                return;
            }

            IsConfigured = true;
            IsConsentAccepted = false;
            HasApiKeyInput = false;
            IsKeyIdInputValid = true;
            await RefreshCoreAsync(forceRefresh: true, cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsExpectedLocalFailure(exception))
        {
            SetState(VercelGatewayUiState.LocalFailure, "VercelStatusLocalFailure");
        }
        finally
        {
            EndOperation(cancellation);
        }
    }

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private async Task DisconnectAsync()
    {
        using CancellationTokenSource cancellation = BeginOperation();
        IsBusy = true;
        SetState(VercelGatewayUiState.Disconnecting, "VercelStatusDisconnecting");
        try
        {
            VercelGatewayDisconnectResult result = await _coordinator.Connections
                .DisconnectAsync(cancellation.Token);
            IsConfigured = false;
            SpendSlice = null;
            ProviderCard = null;
            _lastSnapshot = null;
            IsConsentAccepted = false;
            HasApiKeyInput = false;
            IsKeyIdInputValid = true;
            SetState(
                result.IsComplete
                    ? VercelGatewayUiState.Disconnected
                    : VercelGatewayUiState.CleanupPartial,
                result.IsComplete
                    ? "VercelStatusDisconnected"
                    : CleanupMessageKey(result.CacheStatus));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsExpectedLocalFailure(exception))
        {
            SetState(VercelGatewayUiState.LocalFailure, "VercelStatusLocalFailure");
        }
        finally
        {
            EndOperation(cancellation);
        }
    }

    public async Task RefreshAsync(bool forceRefresh)
    {
        if (!IsConfigured || IsBusy)
        {
            return;
        }

        using CancellationTokenSource cancellation = BeginOperation();
        IsBusy = true;
        try
        {
            await RefreshCoreAsync(forceRefresh, cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsExpectedLocalFailure(exception))
        {
            SetState(VercelGatewayUiState.LocalFailure, "VercelStatusLocalFailure");
        }
        finally
        {
            EndOperation(cancellation);
        }
    }

    public ProviderStatusRow CreateStatusRow()
    {
        bool hasReport = ProviderCard is not null;
        string status = _getString(State switch
        {
            VercelGatewayUiState.AuthenticationRejected => "ProviderStatusNotConfigured",
            VercelGatewayUiState.UnsupportedAccount => "ProviderStatusUnsupported",
            VercelGatewayUiState.ContractFailure => "ProviderStatusContractChanged",
            VercelGatewayUiState.Partial or VercelGatewayUiState.Throttled
                or VercelGatewayUiState.TransientFailure => "ProviderStatusPartial",
            _ when hasReport => "ProviderStatusAvailable",
            _ => "ProviderStatusUnavailable",
        });
        ProviderCapabilityState? quotaState = _lastSnapshot?.Capabilities
            .FirstOrDefault(capability => string.Equals(
                capability.Id.Value,
                "quota.gateway.key.budget",
                StringComparison.Ordinal))
            ?.State;
        string quotaStatus = _getString(quotaState switch
        {
            ProviderCapabilityState.Available => "ProviderStatusAvailable",
            ProviderCapabilityState.NotRequested => "VercelQuotaStatusKeyIdMissing",
            ProviderCapabilityState.NotConfigured => "VercelQuotaStatusNoBudget",
            ProviderCapabilityState.Degraded => "VercelQuotaStatusDegraded",
            _ => "ProviderStatusUnavailable",
        });
        return new ProviderStatusRow(
            "vercel-ai-gateway",
            "Vercel AI Gateway",
            _getString(IsConfigured ? "ProviderStatusRootDetected" : "ProviderStatusRootMissing"),
            _getString(IsConfigured ? "ProviderStatusRecoveryRefresh" : "VercelStatusRecoveryConnect"),
            [
                new(_getString("ProviderStatusQuota"), quotaStatus, "ProviderStatus.vercel-ai-gateway.Quota"),
                new(_getString("ProviderStatusUsage"), status, "ProviderStatus.vercel-ai-gateway.Usage"),
                new(_getString("ProviderStatusSpend"), status, "ProviderStatus.vercel-ai-gateway.Spend"),
                new(
                    _getString("ProviderStatusCoverage"),
                    _lastSnapshot?.Coverage == CoverageKind.Partial
                        ? _getString("ProviderStatusPartial")
                        : hasReport
                            ? _getString("ProviderStatusComplete")
                            : _getString("ProviderStatusUnavailable"),
                    "ProviderStatus.vercel-ai-gateway.Coverage"),
            ],
            "ProviderStatus.vercel-ai-gateway");
    }

    public void Cancel()
    {
        _operationCancellation?.Cancel();
    }

    private bool CanDisconnect() => IsConfigured && !IsBusy;

    public void ApplyHostCacheSnapshot(ProviderSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        PublishSnapshot(snapshot);
        SetState(VercelGatewayUiState.Refreshing, "VercelStatusCachedRefreshing");
    }

    public Task ApplyHostProviderCompletedAsync(
        CacheFirstEvent.ProviderCompleted completed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(completed);
        return PublishOutcomeAsync(completed, cancellationToken);
    }

    private async Task RefreshCoreAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        SetState(VercelGatewayUiState.Refreshing, "VercelStatusRefreshing");
        await foreach (CacheFirstEvent refreshEvent in _coordinator.RunAsync(
                           forceRefresh,
                           cancellationToken))
        {
            switch (refreshEvent)
            {
                case CacheFirstEvent.CachePublished cache:
                    ProviderSnapshot? cached = FindSnapshot(cache.Snapshots);
                    if (cached is not null)
                    {
                        PublishSnapshot(cached);
                        SetState(VercelGatewayUiState.Refreshing, "VercelStatusCachedRefreshing");
                    }

                    break;
                case CacheFirstEvent.ProviderCompleted completed:
                    await PublishOutcomeAsync(completed, cancellationToken);
                    break;
            }
        }
    }

    private async Task PublishOutcomeAsync(
        CacheFirstEvent.ProviderCompleted completed,
        CancellationToken cancellationToken)
    {
        ProviderSnapshot? snapshot = completed.Outcome switch
        {
            ProviderOutcome.Success success => success.Snapshot,
            ProviderOutcome.PartialSuccess partial => partial.Snapshot,
            ProviderOutcome.Throttled throttled => throttled.LastGood,
            ProviderOutcome.TransientFailure failure => failure.LastGood,
            ProviderOutcome.ContractFailure failure => failure.LastGood,
            _ => null,
        };
        if (snapshot is not null)
        {
            PublishSnapshot(snapshot);
        }

        switch (completed.Outcome)
        {
            case ProviderOutcome.Success:
                SetState(
                    completed.CacheStatus == CacheUpdateStatus.Updated
                        ? VercelGatewayUiState.Connected
                        : VercelGatewayUiState.CleanupPartial,
                    completed.CacheStatus == CacheUpdateStatus.Updated
                        ? "VercelStatusConnected"
                        : "VercelStatusCacheWriteFailed");
                break;
            case ProviderOutcome.PartialSuccess:
                SetState(VercelGatewayUiState.Partial, "VercelStatusPartial");
                break;
            case ProviderOutcome.Throttled:
                SetState(VercelGatewayUiState.Throttled, "VercelStatusThrottled");
                break;
            case ProviderOutcome.TransientFailure:
                SetState(VercelGatewayUiState.TransientFailure, "VercelStatusTransientFailure");
                break;
            case ProviderOutcome.ContractFailure:
                SetState(VercelGatewayUiState.ContractFailure, "VercelStatusContractFailure");
                break;
            case ProviderOutcome.UnsupportedAccount:
                SetState(VercelGatewayUiState.UnsupportedAccount, "VercelStatusUnsupportedAccount");
                break;
            case ProviderOutcome.NotConfigured:
                IsConfigured = await _coordinator.Connections
                    .IsConfiguredAsync(cancellationToken);
                if (!IsConfigured)
                {
                    SpendSlice = null;
                    ProviderCard = null;
                    _lastSnapshot = null;
                }

                SetState(
                    IsConfigured
                        ? VercelGatewayUiState.AuthenticationRejected
                        : VercelGatewayUiState.NotConfigured,
                    IsConfigured
                        ? "VercelStatusAuthenticationRejected"
                        : "VercelStatusNotConfigured");
                break;
            default:
                SetState(VercelGatewayUiState.LocalFailure, "VercelStatusLocalFailure");
                break;
        }
    }

    private static ProviderSnapshot? FindSnapshot(IEnumerable<ProviderSnapshot> snapshots) =>
        snapshots.FirstOrDefault(snapshot => string.Equals(
            snapshot.ProviderId.Value,
            "vercel-ai-gateway",
            StringComparison.Ordinal));

    private void PublishSnapshot(ProviderSnapshot snapshot)
    {
        _lastSnapshot = snapshot;
        SpendSlice = VercelGatewayCardProjector.CreateSpendSlice(snapshot, _getString);
        ProviderCard = VercelGatewayCardProjector.Create(snapshot, _getString);
    }

    private CancellationTokenSource BeginOperation()
    {
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _operationCancellation = cancellation;
        return cancellation;
    }

    private void EndOperation(CancellationTokenSource cancellation)
    {
        if (ReferenceEquals(_operationCancellation, cancellation))
        {
            _operationCancellation = null;
            IsBusy = false;
        }
    }

    private void SetState(VercelGatewayUiState state, string messageKey)
    {
        State = state;
        StatusText = _getString(messageKey);
    }

    private static string CleanupMessageKey(VercelGatewayCacheCleanupStatus status) =>
        status switch
        {
            VercelGatewayCacheCleanupStatus.Quarantined => "VercelStatusCacheQuarantined",
            VercelGatewayCacheCleanupStatus.RefusedUnsupportedVersion =>
                "VercelStatusCacheFutureVersion",
            _ => "VercelStatusCacheCleanupFailed",
        };

    private static bool IsExpectedLocalFailure(Exception exception) => exception is
        IOException
        or UnauthorizedAccessException
        or TimeoutException
        or InvalidOperationException
        or System.Runtime.InteropServices.COMException;
}

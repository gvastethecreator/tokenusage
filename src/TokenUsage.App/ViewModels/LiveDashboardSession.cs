using TokenUsage.App.Services;
using TokenUsage.App.ViewModels.Dashboard;
using TokenUsage.App.ViewModels.Sample;
using TokenUsage.Core.Cache;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Session;
using TokenUsage.Core.Usage;

namespace TokenUsage.App.ViewModels;

/// <summary>
/// Live dashboard refresh: owns multi-provider host events and combined publish inputs.
/// </summary>
public sealed class LiveDashboardSession : IDisposable
{
    private readonly object _updateSync = new();
    private readonly AppSessionHost _host;
    private readonly LocalUsageCoordinator _localUsage;
    private readonly QuotaResetHistoryStore? _quotaResetHistory;
    private CancellationTokenSource? _localRefreshCancellation;
    private Task _pendingUpdates = Task.CompletedTask;
    private Func<string, string>? _getString;
    private Func<AppSessionUpdateEventArgs, Task>? _onProviderUpdate;
    private Action<LiveDashboardSession>? _onChanged;
    private SynchronizationContext? _synchronizationContext;
    private volatile bool _publishingEnabled;
    private bool _disposed;
    private int _refreshVersion;

    public LiveDashboardSession(
        AppSessionHost host,
        LocalUsageCoordinator localUsage,
        QuotaResetHistoryStore? quotaResetHistory = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _localUsage = localUsage ?? throw new ArgumentNullException(nameof(localUsage));
        _quotaResetHistory = quotaResetHistory;
        _host.Updated += OnSessionUpdated;
    }

    public TimeProvider Clock => _host.Clock;

    public ProviderSnapshot? LastCodexSnapshot { get; private set; }

    public ProviderOutcome? LastCodexOutcome { get; private set; }

    public LocalUsageCard? RawLocalUsage { get; private set; }

    public IReadOnlyList<DailyUsageRollup> LocalUsageRollups { get; private set; } = [];

    public bool HasLocalUsage { get; private set; }

    public SampleDataState DataState { get; private set; } = SampleDataState.Idle;

    public DateTimeOffset? PublishedObservedAtUtc { get; private set; }

    public DateTimeOffset? RetryAtUtc { get; private set; }

    public bool HasPublished { get; private set; }

    public AppSessionHost Host => _host;

    public void Bind(
        Func<string, string> getString,
        Func<AppSessionUpdateEventArgs, Task> onProviderUpdate,
        Action<LiveDashboardSession> onChanged,
        SynchronizationContext? synchronizationContext)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _getString = getString ?? throw new ArgumentNullException(nameof(getString));
        _onProviderUpdate = onProviderUpdate
            ?? throw new ArgumentNullException(nameof(onProviderUpdate));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));
        _synchronizationContext = synchronizationContext;
        Interlocked.Increment(ref _refreshVersion);
        _publishingEnabled = true;
    }

    public async Task RunAsync(
        bool forceRefresh,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_getString is null || _onProviderUpdate is null || _onChanged is null)
        {
            throw new InvalidOperationException("Bind the live dashboard session before refresh.");
        }

        _synchronizationContext ??= SynchronizationContext.Current;
        int version = Interlocked.Increment(ref _refreshVersion);
        _publishingEnabled = true;

        try
        {
            LocalUsageDashboardResult? cached = await _localUsage
                .ReadCachedDashboardAsync(_getString, cancellationToken)
                .ConfigureAwait(true);
            if (cached is not null
                && version == Volatile.Read(ref _refreshVersion)
                && _publishingEnabled)
            {
                RawLocalUsage = cached.Card;
                LocalUsageRollups = cached.Rollups;
                HasLocalUsage = true;
                HasPublished = true;
                _onChanged(this);
            }

            if (!_host.IsStarted)
            {
                await _host.StartAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await _host.RefreshAsync(
                        AppSessionRefreshReason.Manual,
                        forceRefresh,
                        providerId: null,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            await PendingUpdatesAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            await ReconcileHostStateAsync(version).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or TimeoutException)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                LastCodexOutcome = null;
                DataState = SampleDataState.Error;
                _onChanged(this);
            }
        }
    }

    public void Cancel()
    {
        _publishingEnabled = false;
        Interlocked.Increment(ref _refreshVersion);
        lock (_updateSync)
        {
            _localRefreshCancellation?.Cancel();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _publishingEnabled = false;
        Interlocked.Increment(ref _refreshVersion);
        _host.Updated -= OnSessionUpdated;
        lock (_updateSync)
        {
            _localRefreshCancellation?.Cancel();
            _localRefreshCancellation?.Dispose();
            _localRefreshCancellation = null;
        }

        _getString = null;
        _onProviderUpdate = null;
        _onChanged = null;
        _synchronizationContext = null;
        GC.SuppressFinalize(this);
    }

    private void OnSessionUpdated(object? sender, AppSessionUpdateEventArgs update)
    {
        if (_disposed)
        {
            return;
        }

        int version = Volatile.Read(ref _refreshVersion);
        lock (_updateSync)
        {
            _pendingUpdates = _pendingUpdates
                .ContinueWith(
                    _ => DispatchUpdateAsync(update, version),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default)
                .Unwrap();
        }
    }

    private Task PendingUpdatesAsync()
    {
        lock (_updateSync)
        {
            return _pendingUpdates;
        }
    }

    private Task DispatchUpdateAsync(AppSessionUpdateEventArgs update, int version)
    {
        SynchronizationContext? context = _synchronizationContext;
        if (context is null || ReferenceEquals(SynchronizationContext.Current, context))
        {
            return ApplySessionUpdateAsync(update, version);
        }

        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        context.Post(
            async _ =>
            {
                try
                {
                    await ApplySessionUpdateAsync(update, version);
                    completion.TrySetResult();
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            },
            state: null);
        return completion.Task;
    }

    private async Task ApplySessionUpdateAsync(AppSessionUpdateEventArgs update, int version)
    {
        if (_getString is null || _onProviderUpdate is null || _onChanged is null)
        {
            return;
        }

        if (version != Volatile.Read(ref _refreshVersion) || !_publishingEnabled)
        {
            return;
        }

        await _onProviderUpdate(update).ConfigureAwait(true);
        if (version != Volatile.Read(ref _refreshVersion) || !_publishingEnabled)
        {
            return;
        }

        switch (update.RefreshEvent)
        {
            case CacheFirstEvent.CachePublished cache:
                ProviderSnapshot? codex = cache.Snapshots.FirstOrDefault(candidate =>
                    string.Equals(candidate.ProviderId.Value, "codex", StringComparison.Ordinal));
                if (codex is not null)
                {
                    await ObserveResetHistoryAsync(codex).ConfigureAwait(true);
                    LastCodexSnapshot = codex;
                    PublishedObservedAtUtc = codex.SourceObservedAtUtc;
                    HasPublished = true;
                    DataState = SnapshotFreshness.IsStale(codex, Clock)
                        ? SampleDataState.StaleCacheRefreshing
                        : SampleDataState.CacheRefreshing;
                }

                PublishChanged(version);
                break;

            case CacheFirstEvent.ProviderCompleted provider when string.Equals(
                provider.ProviderId.Value,
                "vercel-ai-gateway",
                StringComparison.Ordinal):
                HasPublished = true;
                PublishChanged(version);
                break;

            case CacheFirstEvent.ProviderCompleted provider:
                ProviderSnapshot? completed = ApplyCodexCompleted(provider);
                if (completed is not null)
                {
                    await ObserveResetHistoryAsync(completed).ConfigureAwait(true);
                }
                PublishChanged(version);
                break;

            case null when update.IsFinal:
                CancellationTokenSource localCancellation = BeginLocalRefresh();
                try
                {
                    await RefreshLocalUsageAsync(
                        version,
                        _getString,
                        _ => PublishChanged(version),
                        localCancellation.Token).ConfigureAwait(true);
                    if (version == Volatile.Read(ref _refreshVersion)
                        && HasLocalUsage
                        && !HasPublished
                        && LastCodexSnapshot is null)
                    {
                        HasPublished = true;
                        PublishChanged(version);
                    }
                }
                finally
                {
                    EndLocalRefresh(localCancellation);
                }

                break;
        }
    }

    private void PublishChanged(int version)
    {
        if (_publishingEnabled && version == Volatile.Read(ref _refreshVersion))
        {
            _onChanged?.Invoke(this);
        }
    }

    private async Task ReconcileHostStateAsync(int version)
    {
        if (!_publishingEnabled || version != Volatile.Read(ref _refreshVersion))
        {
            return;
        }

        AppSessionState state = _host.Current;
        state.Outcomes.TryGetValue("codex", out ProviderOutcome? outcome);
        ProviderSnapshot? snapshot = state.Snapshots.FirstOrDefault(candidate =>
            string.Equals(candidate.ProviderId.Value, "codex", StringComparison.Ordinal));
        bool snapshotChanged = snapshot is not null
            && (LastCodexSnapshot is null
                || snapshot.SourceObservedAtUtc != LastCodexSnapshot.SourceObservedAtUtc
                || snapshot.FetchedAtUtc != LastCodexSnapshot.FetchedAtUtc);
        bool outcomeChanged = outcome is not null && !ReferenceEquals(outcome, LastCodexOutcome);
        if (!snapshotChanged && !outcomeChanged)
        {
            return;
        }

        if (outcome is not null)
        {
            LastCodexOutcome = outcome;
            RetryAtUtc = outcome switch
            {
                ProviderOutcome.Throttled throttled => throttled.RetryAtUtc,
                ProviderOutcome.TransientFailure failure => failure.RetryAtUtc,
                ProviderOutcome.ContractFailure failure => failure.RetryAtUtc,
                _ => null,
            };
        }

        if (snapshot is not null)
        {
            await ObserveResetHistoryAsync(snapshot).ConfigureAwait(true);
            LastCodexSnapshot = snapshot;
            PublishedObservedAtUtc = snapshot.SourceObservedAtUtc;
            HasPublished = true;
            DataState = outcome switch
            {
                ProviderOutcome.Throttled => SampleDataState.Throttled,
                ProviderOutcome.TransientFailure or ProviderOutcome.ContractFailure =>
                    SampleDataState.Error,
                ProviderOutcome.PartialSuccess => SampleDataState.Partial,
                _ when SnapshotFreshness.IsStale(snapshot, Clock) => SampleDataState.Stale,
                _ => SampleDataState.Fresh,
            };
        }

        PublishChanged(version);
    }

    private CancellationTokenSource BeginLocalRefresh()
    {
        lock (_updateSync)
        {
            _localRefreshCancellation?.Cancel();
            _localRefreshCancellation?.Dispose();
            _localRefreshCancellation = new CancellationTokenSource();
            return _localRefreshCancellation;
        }
    }

    private void EndLocalRefresh(CancellationTokenSource cancellation)
    {
        lock (_updateSync)
        {
            if (ReferenceEquals(_localRefreshCancellation, cancellation))
            {
                _localRefreshCancellation = null;
            }
        }

        cancellation.Dispose();
    }

    private async Task RefreshLocalUsageAsync(
        int version,
        Func<string, string> getString,
        Action<LiveDashboardSession> onChanged,
        CancellationToken cancellationToken)
    {
        try
        {
            LocalUsageDashboardResult dashboard = await _localUsage
                .RefreshDashboardAsync(getString, cancellationToken)
                .ConfigureAwait(true);
            if (version == Volatile.Read(ref _refreshVersion)
                && !cancellationToken.IsCancellationRequested)
            {
                RawLocalUsage = dashboard.Card;
                LocalUsageRollups = dashboard.Rollups;
                HasLocalUsage = true;
                onChanged(this);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            if (version == Volatile.Read(ref _refreshVersion))
            {
                RawLocalUsage = LocalUsageCardProjector.CreateUnavailable(
                    getString,
                    _localUsage.SourceKind);
                HasLocalUsage = true;
                onChanged(this);
            }
        }
    }

    private ProviderSnapshot? ApplyCodexCompleted(CacheFirstEvent.ProviderCompleted provider)
    {
        LastCodexOutcome = provider.Outcome;
        ProviderSnapshot? snapshot = provider.Outcome switch
        {
            ProviderOutcome.Success success => success.Snapshot,
            ProviderOutcome.PartialSuccess partial => partial.Snapshot,
            ProviderOutcome.Throttled throttled => throttled.LastGood,
            ProviderOutcome.TransientFailure failure => failure.LastGood,
            ProviderOutcome.ContractFailure failure => failure.LastGood,
            _ => null,
        };

        RetryAtUtc = provider.Outcome switch
        {
            ProviderOutcome.Throttled throttled => throttled.RetryAtUtc,
            ProviderOutcome.TransientFailure failure => failure.RetryAtUtc,
            ProviderOutcome.ContractFailure failure => failure.RetryAtUtc,
            _ => null,
        };

        if (snapshot is null)
        {
            DataState = SampleDataState.Error;
            return null;
        }

        LastCodexSnapshot = snapshot;
        PublishedObservedAtUtc = snapshot.SourceObservedAtUtc;
        HasPublished = true;
        DataState = provider.Outcome switch
        {
            ProviderOutcome.Throttled => SampleDataState.Throttled,
            ProviderOutcome.TransientFailure or ProviderOutcome.ContractFailure =>
                SampleDataState.Error,
            ProviderOutcome.PartialSuccess => SampleDataState.Partial,
            _ when provider.CacheStatus is not CacheUpdateStatus.Updated =>
                SampleDataState.NotSaved,
            _ when SnapshotFreshness.IsStale(snapshot, Clock) => SampleDataState.Stale,
            _ => SampleDataState.Fresh,
        };
        return snapshot;
    }

    private async Task ObserveResetHistoryAsync(ProviderSnapshot snapshot)
    {
        if (_quotaResetHistory is null)
        {
            return;
        }

        try
        {
            await _quotaResetHistory.ObserveAsync(snapshot).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or TimeoutException
            or InvalidOperationException
            or System.Security.SecurityException)
        {
            // Reset history is supplementary; a storage failure must not hide live limits.
        }
    }
}

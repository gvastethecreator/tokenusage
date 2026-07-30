using System.Collections.ObjectModel;
using WOpenUsage.Core.Alerts;
using WOpenUsage.Core.Cache;
using WOpenUsage.Core.Providers;

namespace WOpenUsage.Core.Session;

public enum AppSessionRefreshReason
{
    Initial,
    Periodic,
    Manual,
    ProviderAction,
}

public enum AppSessionStatus
{
    Idle,
    Refreshing,
    Ready,
    Failed,
    Stopped,
}

public sealed record AppSessionState(
    long Version,
    AppSessionStatus Status,
    AppSessionRefreshReason? LastRefreshReason,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<ProviderSnapshot> Snapshots,
    IReadOnlyDictionary<string, ProviderOutcome> Outcomes);

public sealed class AppSessionUpdateEventArgs : EventArgs
{
    public AppSessionUpdateEventArgs(
        AppSessionRefreshReason reason,
        AppSessionState state,
        CacheFirstEvent? refreshEvent,
        IReadOnlyList<AlertNotificationIntent> alerts,
        bool isFinal)
    {
        Reason = reason;
        State = state ?? throw new ArgumentNullException(nameof(state));
        RefreshEvent = refreshEvent;
        Alerts = alerts ?? throw new ArgumentNullException(nameof(alerts));
        IsFinal = isFinal;
    }

    public AppSessionRefreshReason Reason { get; }

    public AppSessionState State { get; }

    public CacheFirstEvent? RefreshEvent { get; }

    public IReadOnlyList<AlertNotificationIntent> Alerts { get; }

    public bool IsFinal { get; }
}

public sealed class AppSessionHost : IAsyncDisposable
{
    public static readonly TimeSpan DefaultRefreshInterval = TimeSpan.FromMinutes(5);

    private readonly object _sync = new();
    private readonly ProviderRefreshHost _refreshHost;
    private readonly AlertHost _alertHost;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _refreshInterval;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly Dictionary<string, ProviderSnapshot> _snapshots = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ProviderOutcome> _outcomes = new(StringComparer.Ordinal);
    private CancellationTokenSource? _lifetimeCancellation;
    private CancellationTokenSource? _activeRefreshCancellation;
    private Task? _periodicLoop;
    private bool _started;
    private bool _stopping;
    private bool _disposed;
    private long _version;
    private AppSessionState _current;

    public AppSessionHost(
        ProviderRefreshHost refreshHost,
        AlertHost alertHost,
        TimeProvider clock,
        TimeSpan? refreshInterval = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        _refreshHost = refreshHost ?? throw new ArgumentNullException(nameof(refreshHost));
        _alertHost = alertHost ?? throw new ArgumentNullException(nameof(alertHost));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _refreshInterval = refreshInterval ?? DefaultRefreshInterval;
        if (_refreshInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(refreshInterval),
                "Refresh interval must be positive.");
        }

        _delayAsync = delayAsync ?? ((delay, token) => Task.Delay(delay, _clock, token));
        _current = CreateState(AppSessionStatus.Idle, reason: null);
    }

    public event EventHandler<AppSessionUpdateEventArgs>? Updated;

    public TimeProvider Clock => _clock;

    public TimeSpan RefreshInterval => _refreshInterval;

    public AppSessionState Current
    {
        get
        {
            lock (_sync)
            {
                return _current;
            }
        }
    }

    public bool IsStarted
    {
        get
        {
            lock (_sync)
            {
                return _started;
            }
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        CancellationToken lifetimeToken;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started)
            {
                return;
            }

            if (_stopping)
            {
                throw new InvalidOperationException("The app session is stopping.");
            }

            _started = true;
            _lifetimeCancellation = new CancellationTokenSource();
            lifetimeToken = _lifetimeCancellation.Token;
        }

        try
        {
            using var startCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                lifetimeToken,
                cancellationToken);
            await RefreshAsync(
                    AppSessionRefreshReason.Initial,
                    forceRefresh: false,
                    providerId: null,
                    startCancellation.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            lock (_sync)
            {
                _started = false;
                _lifetimeCancellation?.Dispose();
                _lifetimeCancellation = null;
            }

            throw;
        }
        catch (Exception)
        {
            // RefreshAsync published the failed initial state. Keep cadence alive.
        }

        lock (_sync)
        {
            if (!_disposed && !lifetimeToken.IsCancellationRequested)
            {
                _periodicLoop = RunPeriodicLoopAsync(lifetimeToken);
            }
        }
    }

    public async Task RefreshAsync(
        AppSessionRefreshReason reason,
        bool forceRefresh,
        ProviderId? providerId = null,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason));
        }

        CancellationTokenSource? operationCancellation = BeginRefresh(reason, cancellationToken);
        if (operationCancellation is null)
        {
            return;
        }

        try
        {
            await _refreshLock.WaitAsync(operationCancellation.Token).ConfigureAwait(false);
            try
            {
                SetStatus(AppSessionStatus.Refreshing, reason);
                IAsyncEnumerable<CacheFirstEvent> events = providerId is null
                    ? _refreshHost.RunAsync(forceRefresh, operationCancellation.Token)
                    : _refreshHost.RunProviderAsync(
                        providerId,
                        forceRefresh,
                        operationCancellation.Token);
                await foreach (CacheFirstEvent refreshEvent in events
                                   .WithCancellation(operationCancellation.Token)
                                   .ConfigureAwait(false))
                {
                    ApplyRefreshEvent(refreshEvent);
                    IReadOnlyList<AlertNotificationIntent> alerts = await EvaluateAlertsAsync(
                            operationCancellation.Token)
                        .ConfigureAwait(false);
                    AppSessionState state = SetStatus(AppSessionStatus.Refreshing, reason);
                    Publish(new AppSessionUpdateEventArgs(
                        reason,
                        state,
                        refreshEvent,
                        alerts,
                        isFinal: false));
                }

                AppSessionState ready = SetStatus(AppSessionStatus.Ready, reason);
                Publish(new AppSessionUpdateEventArgs(
                    reason,
                    ready,
                    refreshEvent: null,
                    alerts: [],
                    isFinal: true));
            }
            catch (OperationCanceledException) when (operationCancellation.IsCancellationRequested)
            {
                bool hasSnapshots;
                lock (_sync)
                {
                    hasSnapshots = _snapshots.Count > 0;
                }

                SetStatus(
                    hasSnapshots ? AppSessionStatus.Ready : AppSessionStatus.Idle,
                    reason);
                throw;
            }
            catch
            {
                AppSessionState failed = SetStatus(AppSessionStatus.Failed, reason);
                Publish(new AppSessionUpdateEventArgs(
                    reason,
                    failed,
                    refreshEvent: null,
                    alerts: [],
                    isFinal: true));
                throw;
            }
            finally
            {
                _refreshLock.Release();
            }
        }
        finally
        {
            EndRefresh(operationCancellation);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource? lifetime;
        CancellationTokenSource? active;
        Task? periodic;
        lock (_sync)
        {
            _stopping = true;
            lifetime = _lifetimeCancellation;
            active = _activeRefreshCancellation;
            periodic = _periodicLoop;
            _periodicLoop = null;
            _started = false;
            lifetime?.Cancel();
            active?.Cancel();
        }

        try
        {
            if (periodic is not null)
            {
                try
                {
                    await periodic.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (lifetime?.IsCancellationRequested is true)
                {
                }
            }

            await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            _refreshLock.Release();
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_lifetimeCancellation, lifetime))
                {
                    _lifetimeCancellation = null;
                }

                _stopping = false;
            }

            lifetime?.Dispose();
        }

        SetStatus(AppSessionStatus.Stopped, reason: null);
    }

    public async ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        await StopAsync().ConfigureAwait(false);
        _refreshLock.Dispose();
        GC.SuppressFinalize(this);
    }

    private CancellationTokenSource? BeginRefresh(
        AppSessionRefreshReason reason,
        CancellationToken callerToken)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_stopping)
            {
                return null;
            }

            if (reason == AppSessionRefreshReason.Periodic
                && _activeRefreshCancellation is not null)
            {
                return null;
            }

            _activeRefreshCancellation?.Cancel();
            CancellationToken lifetimeToken = _lifetimeCancellation?.Token
                ?? CancellationToken.None;
            var operation = CancellationTokenSource.CreateLinkedTokenSource(
                lifetimeToken,
                callerToken);
            _activeRefreshCancellation = operation;
            return operation;
        }
    }

    private void EndRefresh(CancellationTokenSource operation)
    {
        lock (_sync)
        {
            if (ReferenceEquals(_activeRefreshCancellation, operation))
            {
                _activeRefreshCancellation = null;
            }
        }

        operation.Dispose();
    }

    private async Task RunPeriodicLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _delayAsync(_refreshInterval, cancellationToken).ConfigureAwait(false);
                await RefreshAsync(
                        AppSessionRefreshReason.Periodic,
                        forceRefresh: false,
                        providerId: null,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (OperationCanceledException)
            {
                // A manual/provider action replaced the periodic request.
            }
            catch (Exception)
            {
                // RefreshAsync already published the failed state. Keep cadence alive.
            }
        }
    }

    private void ApplyRefreshEvent(CacheFirstEvent refreshEvent)
    {
        lock (_sync)
        {
            switch (refreshEvent)
            {
                case CacheFirstEvent.CachePublished cache:
                    foreach (ProviderSnapshot snapshot in cache.Snapshots)
                    {
                        _snapshots[snapshot.ProviderId.Value] = snapshot;
                    }

                    break;

                case CacheFirstEvent.ProviderCompleted completed:
                    _outcomes[completed.ProviderId.Value] = completed.Outcome;
                    ProviderSnapshot? completedSnapshot = SelectOutcomeSnapshot(completed.Outcome);
                    if (completedSnapshot is not null)
                    {
                        _snapshots[completed.ProviderId.Value] = completedSnapshot;
                    }

                    break;
            }
        }
    }

    private async Task<IReadOnlyList<AlertNotificationIntent>> EvaluateAlertsAsync(
        CancellationToken cancellationToken)
    {
        ProviderAlertFacts[] facts;
        lock (_sync)
        {
            facts = _refreshHost.Registrations
                .Select(registration => registration.Provider.Descriptor.Id)
                .Select(providerId => CreateAlertFacts(providerId))
                .Where(candidate => candidate is not null)
                .Cast<ProviderAlertFacts>()
                .ToArray();
        }

        return await _alertHost.EvaluateAsync(
                _clock.GetUtcNow().ToUniversalTime(),
                facts,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private ProviderAlertFacts? CreateAlertFacts(ProviderId providerId)
    {
        if (_outcomes.TryGetValue(providerId.Value, out ProviderOutcome? outcome))
        {
            return AlertFactsBuilder.FromOutcome(providerId, outcome, _clock);
        }

        return _snapshots.TryGetValue(providerId.Value, out ProviderSnapshot? snapshot)
            ? AlertFactsBuilder.FromSnapshot(snapshot, _clock)
            : null;
    }

    private AppSessionState SetStatus(
        AppSessionStatus status,
        AppSessionRefreshReason? reason)
    {
        lock (_sync)
        {
            _current = CreateState(status, reason);
            return _current;
        }
    }

    private AppSessionState CreateState(
        AppSessionStatus status,
        AppSessionRefreshReason? reason) =>
        new(
            ++_version,
            status,
            reason,
            _clock.GetUtcNow().ToUniversalTime(),
            Array.AsReadOnly(_snapshots.Values
                .OrderBy(snapshot => snapshot.ProviderId.Value, StringComparer.Ordinal)
                .ToArray()),
            new ReadOnlyDictionary<string, ProviderOutcome>(
                new Dictionary<string, ProviderOutcome>(_outcomes, StringComparer.Ordinal)));

    private void Publish(AppSessionUpdateEventArgs update)
    {
        EventHandler<AppSessionUpdateEventArgs>? handlers = Updated;
        if (handlers is null)
        {
            return;
        }

        foreach (EventHandler<AppSessionUpdateEventArgs> handler in handlers.GetInvocationList())
        {
            handler(this, update);
        }
    }

    private static ProviderSnapshot? SelectOutcomeSnapshot(ProviderOutcome outcome) => outcome switch
    {
        ProviderOutcome.Success success => success.Snapshot,
        ProviderOutcome.PartialSuccess partial => partial.Snapshot,
        ProviderOutcome.Throttled throttled => throttled.LastGood,
        ProviderOutcome.TransientFailure transient => transient.LastGood,
        ProviderOutcome.ContractFailure contract => contract.LastGood,
        _ => null,
    };
}

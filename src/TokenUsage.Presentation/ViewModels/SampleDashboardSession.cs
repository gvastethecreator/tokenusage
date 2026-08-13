using TokenUsage.App.Services;
using TokenUsage.App.ViewModels.Dashboard;
using TokenUsage.App.ViewModels.Sample;
using TokenUsage.Core.Cache;
using TokenUsage.Core.Providers;

namespace TokenUsage.App.ViewModels;

/// <summary>
/// Sample-mode dashboard refresh: owns sample scenario refresh events and projected snapshots.
/// </summary>
public sealed class SampleDashboardSession
{
    private readonly SampleRefreshCoordinator _coordinator;
    private int _refreshVersion;

    public SampleDashboardSession(SampleRefreshCoordinator coordinator)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
    }

    public TimeProvider Clock => _coordinator.Clock;

    public SampleScenario? ActiveScenario { get; private set; }

    public DashboardSnapshot? LastDashboard { get; private set; }

    public ProviderSnapshot? LastSnapshot { get; private set; }

    public SampleDataState DataState { get; private set; } = SampleDataState.Idle;

    public DateTimeOffset? PublishedObservedAtUtc { get; private set; }

    public DateTimeOffset? RetryAtUtc { get; private set; }

    public bool HasPublished { get; private set; }

    public async Task RunAsync(
        SampleScenario scenario,
        bool forceRefresh,
        Func<string, string> getString,
        Action<SampleDashboardSession> onChanged,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(getString);
        ArgumentNullException.ThrowIfNull(onChanged);
        int version = ++_refreshVersion;
        ActiveScenario = scenario;

        try
        {
            await foreach (CacheFirstEvent refreshEvent in _coordinator
                               .RunAsync(scenario, forceRefresh, cancellationToken)
                               .ConfigureAwait(false))
            {
                if (version != _refreshVersion || cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                switch (refreshEvent)
                {
                    case CacheFirstEvent.CachePublished cache:
                        ProviderSnapshot? cached = cache.Snapshots.FirstOrDefault(candidate =>
                            string.Equals(
                                candidate.ProviderId.Value,
                                "codex",
                                StringComparison.Ordinal));
                        if (cached is not null)
                        {
                            Publish(scenario, cached, getString, reveal: !HasPublished);
                            DataState = SnapshotFreshness.IsStale(cached, Clock)
                                ? SampleDataState.StaleCacheRefreshing
                                : SampleDataState.CacheRefreshing;
                            onChanged(this);
                        }

                        break;
                    case CacheFirstEvent.ProviderCompleted provider:
                        ApplyProviderCompleted(scenario, provider, getString, onChanged);
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or TimeoutException)
        {
            if (version == _refreshVersion)
            {
                LastDashboard = null;
                LastSnapshot = null;
                HasPublished = false;
                DataState = SampleDataState.Error;
                onChanged(this);
            }
        }
    }

    public void Cancel() => _refreshVersion++;

    private void ApplyProviderCompleted(
        SampleScenario scenario,
        CacheFirstEvent.ProviderCompleted provider,
        Func<string, string> getString,
        Action<SampleDashboardSession> onChanged)
    {
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
            LastDashboard = null;
            LastSnapshot = null;
            HasPublished = false;
            DataState = SampleDataState.Error;
            onChanged(this);
            return;
        }

        Publish(scenario, snapshot, getString, reveal: true);
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
        onChanged(this);
    }

    private void Publish(
        SampleScenario scenario,
        ProviderSnapshot snapshot,
        Func<string, string> getString,
        bool reveal)
    {
        _ = reveal;
        LastSnapshot = snapshot;
        PublishedObservedAtUtc = snapshot.SourceObservedAtUtc;
        LastDashboard = SampleDashboardProjector.Create(scenario, snapshot, getString);
        HasPublished = true;
        ActiveScenario = scenario;
    }
}

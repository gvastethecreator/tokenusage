using WOpenUsage.App.Services;
using WOpenUsage.App.ViewModels.Dashboard;
using WOpenUsage.App.ViewModels.Sample;
using WOpenUsage.Core.Cache;
using WOpenUsage.Core.Providers;

namespace WOpenUsage.App.ViewModels;

/// <summary>
/// Live dashboard refresh: owns multi-provider host events and combined publish inputs.
/// </summary>
public sealed class LiveDashboardSession
{
    private readonly ProviderRefreshHost _host;
    private readonly LocalUsageCoordinator _localUsage;
    private int _refreshVersion;

    public LiveDashboardSession(
        ProviderRefreshHost host,
        LocalUsageCoordinator localUsage)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _localUsage = localUsage ?? throw new ArgumentNullException(nameof(localUsage));
    }

    public TimeProvider Clock => _host.Clock;

    public ProviderSnapshot? LastCodexSnapshot { get; private set; }

    public ProviderOutcome? LastCodexOutcome { get; private set; }

    public LocalUsageCard? RawLocalUsage { get; private set; }

    public bool HasLocalUsage { get; private set; }

    public SampleDataState DataState { get; private set; } = SampleDataState.Idle;

    public DateTimeOffset? PublishedObservedAtUtc { get; private set; }

    public DateTimeOffset? RetryAtUtc { get; private set; }

    public bool HasPublished { get; private set; }

    public async Task RunAsync(
        bool forceRefresh,
        Func<string, string> getString,
        VercelGatewaySettingsViewModel vercel,
        Action<LiveDashboardSession> onChanged,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(getString);
        ArgumentNullException.ThrowIfNull(vercel);
        ArgumentNullException.ThrowIfNull(onChanged);
        int version = ++_refreshVersion;
        Task localUsageRefresh = RefreshLocalUsageAsync(version, getString, onChanged, cancellationToken);

        try
        {
            await foreach (CacheFirstEvent refreshEvent in _host
                               .RunAsync(forceRefresh, cancellationToken)
                               .ConfigureAwait(false))
            {
                if (version != _refreshVersion || cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                switch (refreshEvent)
                {
                    case CacheFirstEvent.CachePublished cache:
                        ProviderSnapshot? vercelCached = cache.Snapshots.FirstOrDefault(candidate =>
                            string.Equals(
                                candidate.ProviderId.Value,
                                "vercel-ai-gateway",
                                StringComparison.Ordinal));
                        if (vercelCached is not null)
                        {
                            vercel.ApplyHostCacheSnapshot(vercelCached);
                        }

                        ProviderSnapshot? codex = cache.Snapshots.FirstOrDefault(candidate =>
                            string.Equals(
                                candidate.ProviderId.Value,
                                "codex",
                                StringComparison.Ordinal));
                        if (codex is not null)
                        {
                            LastCodexSnapshot = codex;
                            PublishedObservedAtUtc = codex.SourceObservedAtUtc;
                            HasPublished = true;
                            DataState = SnapshotFreshness.IsStale(codex, Clock)
                                ? SampleDataState.StaleCacheRefreshing
                                : SampleDataState.CacheRefreshing;
                        }

                        onChanged(this);
                        break;

                    case CacheFirstEvent.ProviderCompleted provider
                        when string.Equals(
                            provider.ProviderId.Value,
                            "vercel-ai-gateway",
                            StringComparison.Ordinal):
                        await vercel.ApplyHostProviderCompletedAsync(provider, cancellationToken)
                            .ConfigureAwait(false);
                        HasPublished = true;
                        onChanged(this);
                        break;

                    case CacheFirstEvent.ProviderCompleted provider:
                        ApplyCodexCompleted(provider);
                        onChanged(this);
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
                LastCodexOutcome = null;
                DataState = SampleDataState.Error;
                onChanged(this);
            }
        }
        finally
        {
            await localUsageRefresh.ConfigureAwait(false);
            if (version == _refreshVersion
                && HasLocalUsage
                && !HasPublished
                && LastCodexSnapshot is null)
            {
                HasPublished = true;
                onChanged(this);
            }
        }
    }

    public void Cancel() => _refreshVersion++;

    private async Task RefreshLocalUsageAsync(
        int version,
        Func<string, string> getString,
        Action<LiveDashboardSession> onChanged,
        CancellationToken cancellationToken)
    {
        try
        {
            LocalUsageCard card = await _localUsage
                .RefreshAsync(getString, cancellationToken)
                .ConfigureAwait(false);
            if (version == _refreshVersion && !cancellationToken.IsCancellationRequested)
            {
                RawLocalUsage = card;
                HasLocalUsage = true;
                onChanged(this);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            if (version == _refreshVersion)
            {
                RawLocalUsage = LocalUsageCardProjector.CreateUnavailable(
                    getString,
                    _localUsage.SourceKind);
                HasLocalUsage = true;
                onChanged(this);
            }
        }
    }

    private void ApplyCodexCompleted(CacheFirstEvent.ProviderCompleted provider)
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
            return;
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
    }
}

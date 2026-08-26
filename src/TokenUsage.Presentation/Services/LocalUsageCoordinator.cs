using TokenUsage.App.ViewModels;
using TokenUsage.App.ViewModels.Dashboard;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;

namespace TokenUsage.App.Services;

public sealed record LocalUsageDashboardResult(
    LocalUsageCard Card,
    IReadOnlyList<DailyUsageRollup> Rollups);

/// <summary>
/// App adapter: domain local-usage refresh plus UI card projection.
/// </summary>
public sealed class LocalUsageCoordinator
{
    private readonly LocalUsageRefresh _refresh;

    public LocalUsageCoordinator(
        string databasePath,
        IUsageEventSource source,
        TimeProvider clock)
        : this(databasePath, [source], clock)
    {
    }

    public LocalUsageCoordinator(
        string databasePath,
        IReadOnlyList<IUsageEventSource> sources,
        TimeProvider clock)
    {
        _refresh = new LocalUsageRefresh(databasePath, sources, clock);
    }

    public LocalUsageCoordinator(LocalUsageRefresh refresh)
    {
        _refresh = refresh ?? throw new ArgumentNullException(nameof(refresh));
    }

    public SourceKind SourceKind => _refresh.SourceKind;

    /// <summary>
    /// Presence-only provider card. This answers "which tools are installed" before the
    /// first scan, so a fresh install never reports an absent tool as detected.
    /// </summary>
    public LocalUsageCard DetectProviders(Func<string, string> getString)
    {
        ArgumentNullException.ThrowIfNull(getString);
        return LocalUsageCardProjector.CreateDetectionOnly(
            _refresh.DetectSources(),
            _refresh.Today,
            getString,
            _refresh.SourceKind,
            _refresh.HasMultipleRealSources);
    }

    public async Task<LocalUsageCard> RefreshAsync(
        Func<string, string> getString,
        CancellationToken cancellationToken = default)
        => (await RefreshDashboardAsync(getString, cancellationToken).ConfigureAwait(false)).Card;

    /// <summary>
    /// Refreshes and projects on a worker thread. SQLite has no real asynchronous path, so its
    /// commands run on whichever thread starts them; awaiting this from the UI thread without a
    /// worker would hold the panel while the store is opened, migrated, and queried.
    /// </summary>
    public Task<LocalUsageDashboardResult> RefreshDashboardAsync(
        Func<string, string> getString,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(getString);
        return Task.Run(
            () => RefreshDashboardCoreAsync(getString, cancellationToken),
            cancellationToken);
    }

    private async Task<LocalUsageDashboardResult> RefreshDashboardCoreAsync(
        Func<string, string> getString,
        CancellationToken cancellationToken)
    {
        LocalUsageRefreshResult result = await _refresh
            .RefreshAsync(cancellationToken)
            .ConfigureAwait(false);
        return new LocalUsageDashboardResult(
            LocalUsageCardProjector.Create(
                result.Rollups,
                result.ToInclusive,
                getString,
                result.SourceKind,
                result.OverallStatus,
                hasMultipleRealSources: result.HasMultipleRealSources,
                sourceDiagnostics: result.SourceDiagnostics),
            result.Rollups);
    }

    public async Task<LocalUsageCard?> ReadCachedAsync(
        Func<string, string> getString,
        CancellationToken cancellationToken = default)
        => (await ReadCachedDashboardAsync(getString, cancellationToken).ConfigureAwait(false))?.Card;

    public Task<LocalUsageDashboardResult?> ReadCachedDashboardAsync(
        Func<string, string> getString,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(getString);
        return Task.Run(
            () => ReadCachedDashboardCoreAsync(getString, cancellationToken),
            cancellationToken);
    }

    private async Task<LocalUsageDashboardResult?> ReadCachedDashboardCoreAsync(
        Func<string, string> getString,
        CancellationToken cancellationToken)
    {
        LocalUsageRefreshResult? result = await _refresh
            .ReadCachedAsync(cancellationToken)
            .ConfigureAwait(false);
        return result is null
            ? null
            : new LocalUsageDashboardResult(
                LocalUsageCardProjector.Create(
                    result.Rollups,
                    result.ToInclusive,
                    getString,
                    result.SourceKind,
                    result.OverallStatus,
                    hasMultipleRealSources: result.HasMultipleRealSources,
                    sourceDiagnostics: result.SourceDiagnostics),
                result.Rollups);
    }

    public Task<LocalUsageRefreshResult> RefreshDomainAsync(
        CancellationToken cancellationToken = default) =>
        Task.Run(() => _refresh.RefreshAsync(cancellationToken), cancellationToken);

    /// <summary>
    /// Exact tokens one agent spent since an arbitrary UTC instant, on a worker thread like
    /// every other store read.
    /// </summary>
    public Task<long> SumTokensSinceAsync(
        AgentId agentId,
        DateTimeOffset fromInclusiveUtc,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () => _refresh.SumTokensSinceAsync(agentId, fromInclusiveUtc, cancellationToken),
            cancellationToken);
}

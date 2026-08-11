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

    public async Task<LocalUsageCard> RefreshAsync(
        Func<string, string> getString,
        CancellationToken cancellationToken = default)
        => (await RefreshDashboardAsync(getString, cancellationToken).ConfigureAwait(false)).Card;

    public async Task<LocalUsageDashboardResult> RefreshDashboardAsync(
        Func<string, string> getString,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(getString);
        LocalUsageRefreshResult result = await _refresh
            .RefreshAsync(cancellationToken)
            .ConfigureAwait(false);
        return new LocalUsageDashboardResult(
            LocalUsageCardProjector.Create(
                result.Rollups,
                getString,
                result.SourceKind,
                result.OverallStatus,
                hasMultipleRealSources: result.HasMultipleRealSources,
                today: result.ToInclusive,
                sourceDiagnostics: result.SourceDiagnostics),
            result.Rollups);
    }

    public async Task<LocalUsageCard?> ReadCachedAsync(
        Func<string, string> getString,
        CancellationToken cancellationToken = default)
        => (await ReadCachedDashboardAsync(getString, cancellationToken).ConfigureAwait(false))?.Card;

    public async Task<LocalUsageDashboardResult?> ReadCachedDashboardAsync(
        Func<string, string> getString,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(getString);
        LocalUsageRefreshResult? result = await _refresh
            .ReadCachedAsync(cancellationToken)
            .ConfigureAwait(false);
        return result is null
            ? null
            : new LocalUsageDashboardResult(
                LocalUsageCardProjector.Create(
                    result.Rollups,
                    getString,
                    result.SourceKind,
                    result.OverallStatus,
                    hasMultipleRealSources: result.HasMultipleRealSources,
                    today: result.ToInclusive,
                    sourceDiagnostics: result.SourceDiagnostics),
                result.Rollups);
    }

    public Task<LocalUsageRefreshResult> RefreshDomainAsync(
        CancellationToken cancellationToken = default) =>
        _refresh.RefreshAsync(cancellationToken);
}

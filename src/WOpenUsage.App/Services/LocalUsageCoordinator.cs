using WOpenUsage.App.ViewModels;
using WOpenUsage.App.ViewModels.Dashboard;
using WOpenUsage.Core.Providers;
using WOpenUsage.Core.Usage;

namespace WOpenUsage.App.Services;

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
    {
        ArgumentNullException.ThrowIfNull(getString);
        LocalUsageRefreshResult result = await _refresh
            .RefreshAsync(cancellationToken)
            .ConfigureAwait(false);
        return LocalUsageCardProjector.Create(
            result.Rollups,
            getString,
            result.SourceKind,
            result.OverallStatus,
            hasMultipleRealSources: result.HasMultipleRealSources,
            today: result.ToInclusive,
            sourceDiagnostics: result.SourceDiagnostics);
    }

    public Task<LocalUsageRefreshResult> RefreshDomainAsync(
        CancellationToken cancellationToken = default) =>
        _refresh.RefreshAsync(cancellationToken);
}

using WOpenUsage.App.ViewModels;
using WOpenUsage.App.ViewModels.Sample;
using WOpenUsage.Core.Providers;
using WOpenUsage.Core.Usage;

namespace WOpenUsage.App.Services;

public sealed class LocalUsageCoordinator
{
    private readonly string _databasePath;
    private readonly IReadOnlyList<IUsageEventSource> _sources;
    private readonly TimeProvider _clock;

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
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Count == 0 || sources.Any(source => source is null))
        {
            throw new ArgumentException("At least one usage source is required.", nameof(sources));
        }

        _databasePath = databasePath;
        _sources = sources;
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public SourceKind SourceKind => _sources.All(source => source.SourceKind == SourceKind.LocalLog)
        ? SourceKind.LocalLog
        : _sources[0].SourceKind;

    public async Task<LocalUsageCard> RefreshAsync(
        Func<string, string> getString,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(getString);
        UsageRepository repository = await UsageRepository.OpenAsync(
            _databasePath,
            cancellationToken).ConfigureAwait(false);
        UsageSourceReadResult[] readResults = await Task.WhenAll(
            _sources.Select(source => source.ReadAsync(cancellationToken))).ConfigureAwait(false);
        UsageEvent[] events = readResults.SelectMany(result => result.Events).ToArray();
        string[] groupingTimeZoneIds = events
            .Select(usageEvent => usageEvent.GroupingTimeZoneId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (groupingTimeZoneIds.Length > 1)
        {
            throw new InvalidDataException(
                "Local usage sources must use one grouping time zone per refresh.");
        }

        string groupingTimeZoneId = groupingTimeZoneIds.Length == 0
            ? TimeZoneInfo.Local.Id
            : groupingTimeZoneIds[0];
        TimeZoneInfo groupingTimeZone = TimeZoneInfo.FindSystemTimeZoneById(groupingTimeZoneId);
        DateOnly today = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(_clock.GetUtcNow(), groupingTimeZone).DateTime);

        for (int index = 0; index < _sources.Count; index++)
        {
            IUsageEventSource source = _sources[index];
            UsageSourceReadResult result = readResults[index];
            if (source is ISnapshotUsageEventSource snapshotSource)
            {
                if (result.Status == UsageSourceReadStatus.Complete)
                {
                    await repository.ReplaceAgentEventsAsync(
                        snapshotSource.AgentId,
                        result.Events,
                        cancellationToken).ConfigureAwait(false);
                }
            }
            else if (source is IWindowedSnapshotUsageEventSource windowedSource)
            {
                ArgumentOutOfRangeException.ThrowIfLessThan(
                    windowedSource.ReconciliationWindowDays,
                    1);
                DateOnly reconcileFrom = today.AddDays(
                    -(windowedSource.ReconciliationWindowDays - 1));
                UsageEvent[] eventsInWindow = result.Events.Where(usageEvent =>
                {
                    DateOnly eventDate = DateOnly.FromDateTime(
                        TimeZoneInfo.ConvertTime(
                            usageEvent.OccurredAtUtc,
                            groupingTimeZone).DateTime);
                    return eventDate >= reconcileFrom && eventDate <= today;
                }).ToArray();
                bool isAuthoritative = result.Status == UsageSourceReadStatus.Complete
                    || (result.Status == UsageSourceReadStatus.NoData
                        && result.Issue == UsageSourceIssueKind.Empty);
                if (isAuthoritative)
                {
                    await repository.ReconcileAgentEventRangeAsync(
                        windowedSource.AgentId,
                        windowedSource.EventParserVersion,
                        reconcileFrom,
                        today,
                        eventsInWindow,
                        cancellationToken).ConfigureAwait(false);
                }
                else if (eventsInWindow.Length > 0)
                {
                    await repository.UpsertAgentEventsAsync(
                        windowedSource.AgentId,
                        eventsInWindow,
                        cancellationToken).ConfigureAwait(false);
                }
            }
            else
            {
                await repository.IngestAsync(result.Events, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        UsageSourceReadStatus readStatus = readResults.Any(
            result => result.Status == UsageSourceReadStatus.Partial)
                ? UsageSourceReadStatus.Partial
                : readResults.All(result => result.Status == UsageSourceReadStatus.NoData)
                    ? UsageSourceReadStatus.NoData
                    : _sources.Count > 1 && readResults.Any(
                        result => result.Status == UsageSourceReadStatus.NoData)
                        ? UsageSourceReadStatus.Partial
                    : UsageSourceReadStatus.Complete;

        await repository.ApplyRetentionAsync(
                _clock.GetUtcNow().ToUniversalTime(),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyList<DailyUsageRollup> rollups = await repository.QueryDailyRollupsAsync(
            Min(today.AddDays(-34), new DateOnly(today.Year, today.Month, 1)),
            today,
            cancellationToken).ConfigureAwait(false);
        return LocalUsageCardProjector.Create(
            rollups,
            getString,
            SourceKind,
            readStatus,
            hasMultipleRealSources: SourceKind == SourceKind.LocalLog && _sources.Count > 1,
            today: today,
            sourceDiagnostics: _sources.Select((source, index) =>
                new UsageSourceDiagnostic(
                    source.AgentId,
                    readResults[index].Status,
                    readResults[index].Issue,
                    source is ISnapshotUsageEventSource
                        or IWindowedSnapshotUsageEventSource)).ToArray());
    }

    private static DateOnly Min(DateOnly left, DateOnly right) =>
        left <= right ? left : right;
}

using WOpenUsage.App.ViewModels;
using WOpenUsage.App.ViewModels.Sample;
using WOpenUsage.Core.Providers;
using WOpenUsage.Core.Usage;

namespace WOpenUsage.App.Services;

public sealed class LocalUsageCoordinator
{
    private readonly string _databasePath;
    private readonly IUsageEventSource _source;
    private readonly TimeProvider _clock;

    public LocalUsageCoordinator(
        string databasePath,
        IUsageEventSource source,
        TimeProvider clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _databasePath = databasePath;
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public SourceKind SourceKind => _source.SourceKind;

    public async Task<LocalUsageCard> RefreshAsync(
        Func<string, string> getString,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(getString);
        UsageRepository repository = await UsageRepository.OpenAsync(
            _databasePath,
            cancellationToken).ConfigureAwait(false);
        UsageSourceReadResult readResult = await _source.ReadAsync(cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyList<UsageEvent> events = readResult.Events;
        await repository.IngestAsync(events, cancellationToken).ConfigureAwait(false);

        string groupingTimeZoneId = events.Count == 0
            ? TimeZoneInfo.Local.Id
            : events[0].GroupingTimeZoneId;
        TimeZoneInfo groupingTimeZone = TimeZoneInfo.FindSystemTimeZoneById(groupingTimeZoneId);
        DateOnly today = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(_clock.GetUtcNow(), groupingTimeZone).DateTime);
        await repository.ApplyRetentionAsync(
                _clock.GetUtcNow().ToUniversalTime(),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyList<DailyUsageRollup> rollups = _source.SourceKind == SourceKind.LocalLog
            ? await repository.QueryDailyRollupsByAgentAsync(
                today.AddDays(-29),
                today,
                new AgentId("claude"),
                cancellationToken).ConfigureAwait(false)
            : await repository.QueryDailyRollupsAsync(
                today.AddDays(-29),
                today,
                cancellationToken).ConfigureAwait(false);
        return LocalUsageCardProjector.Create(
            rollups,
            getString,
            _source.SourceKind,
            readResult.Status);
    }
}

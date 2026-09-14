using Microsoft.Data.Sqlite;
using TokenUsage.Core.Providers;

namespace TokenUsage.Core.Usage;

public sealed record UsageEventPageCursor(UsageDataRevision Revision, DateTimeOffset FromUtc,
    DateTimeOffset ToUtc, AgentId? Agent, DateTimeOffset AfterUtc, AgentId AfterAgent,
    ModelId AfterModel, UsageEventKey AfterKey);

public sealed record UsageEventPage(IReadOnlyList<UsageEvent> Events,
    UsageDataRevision Revision, UsageEventPageCursor? Next);

public sealed class UsageDataChangedException(UsageDataRevision expected, UsageDataRevision actual)
    : InvalidOperationException("Usage changed while reading pages. Start again from the first page.")
{
    public UsageDataRevision Expected { get; } = expected;
    public UsageDataRevision Actual { get; } = actual;
}

public sealed partial class UsageRepository
{
    public async Task<UsageEventPage> ReadUsageEventPageAsync(DateTimeOffset fromInclusiveUtc,
        DateTimeOffset toExclusiveUtc, AgentId? agentId = null, int pageSize = 100,
        UsageEventPageCursor? cursor = null, CancellationToken cancellationToken = default)
    {
        UtcTimestamp.Require(fromInclusiveUtc, nameof(fromInclusiveUtc));
        UtcTimestamp.Require(toExclusiveUtc, nameof(toExclusiveUtc));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(toExclusiveUtc, fromInclusiveUtc);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pageSize, 500);
        if (cursor is not null && (cursor.FromUtc != fromInclusiveUtc || cursor.ToUtc != toExclusiveUtc
            || cursor.Agent != agentId || cursor.AfterUtc < fromInclusiveUtc || cursor.AfterUtc >= toExclusiveUtc
            || cursor.AfterUtc.Offset != TimeSpan.Zero))
            throw new ArgumentException("The page cursor does not belong to this selection.", nameof(cursor));

        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        await using SqliteTransaction transaction = connection.BeginTransaction(deferred: true);
        UsageDataRevision revision = await ReadDataRevisionOnAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        if (cursor is not null && cursor.Revision != revision)
            throw new UsageDataChangedException(cursor.Revision, revision);
        IReadOnlyList<UsageEvent> rows = await QueryUsageEventsOnAsync(connection, transaction,
            fromInclusiveUtc, toExclusiveUtc, agentId, includeOverlappingIntervals: false,
            pageSize: pageSize + 1, cursor: cursor, cancellationToken: cancellationToken).ConfigureAwait(false);
        UsageEvent[] page = rows.Take(pageSize).ToArray();
        UsageEventPageCursor? next = rows.Count > pageSize
            ? new(revision, fromInclusiveUtc, toExclusiveUtc, agentId, page[^1].OccurredAtUtc,
                page[^1].AgentId, page[^1].ModelId, page[^1].EventKey) : null;
        return new(page, revision, next);
    }
}

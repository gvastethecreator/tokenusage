using TokenUsage.Core.Providers;

namespace TokenUsage.Core.Usage;

public sealed record LocalUsageRefreshResult
{
    public LocalUsageRefreshResult(
        IReadOnlyList<DailyUsageRollup> rollups,
        DateOnly fromInclusive,
        DateOnly toInclusive,
        SourceKind sourceKind,
        UsageSourceReadStatus overallStatus,
        IReadOnlyList<UsageSourceDiagnostic> sourceDiagnostics,
        bool hasMultipleRealSources)
    {
        ArgumentNullException.ThrowIfNull(rollups);
        ArgumentNullException.ThrowIfNull(sourceDiagnostics);
        if (!Enum.IsDefined(sourceKind))
        {
            throw new ArgumentOutOfRangeException(nameof(sourceKind));
        }

        if (!Enum.IsDefined(overallStatus))
        {
            throw new ArgumentOutOfRangeException(nameof(overallStatus));
        }

        if (fromInclusive > toInclusive)
        {
            throw new ArgumentException(
                "FromInclusive cannot be after ToInclusive.",
                nameof(fromInclusive));
        }

        Rollups = rollups;
        FromInclusive = fromInclusive;
        ToInclusive = toInclusive;
        SourceKind = sourceKind;
        OverallStatus = overallStatus;
        SourceDiagnostics = sourceDiagnostics;
        HasMultipleRealSources = hasMultipleRealSources;
    }

    public IReadOnlyList<DailyUsageRollup> Rollups { get; }

    public DateOnly FromInclusive { get; }

    public DateOnly ToInclusive { get; }

    public SourceKind SourceKind { get; }

    public UsageSourceReadStatus OverallStatus { get; }

    public IReadOnlyList<UsageSourceDiagnostic> SourceDiagnostics { get; }

    public bool HasMultipleRealSources { get; }
}

/// <summary>
/// Domain refresh for local usage sources: ingest/reconcile, retention, and rollup query.
/// Presentation stays in App/CLI adapters.
/// </summary>
public sealed class LocalUsageRefresh
{
    private static readonly TimeSpan RetentionMinInterval = TimeSpan.FromHours(6);
    private readonly string _databasePath;
    private readonly IReadOnlyList<IUsageEventSource> _sources;
    private readonly TimeProvider _clock;

    public LocalUsageRefresh(
        string databasePath,
        IUsageEventSource source,
        TimeProvider clock)
        : this(databasePath, [source], clock)
    {
    }

    public LocalUsageRefresh(
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

    public SourceKind SourceKind => HasMultipleRealSources
        ? SourceKind.LocalLog
        : _sources[0].SourceKind;

    public bool HasMultipleRealSources => _sources.Count > 1
        && _sources.All(source => source.SourceKind is
            SourceKind.OfficialLocalApi or SourceKind.LocalLog or SourceKind.LocalDatabase);

    /// <summary>
    /// Today in the grouping zone daily rollups use. A caller that projects a card without a
    /// refresh result takes the date from here, so no surface has to fall back to the machine
    /// clock on its own.
    /// </summary>
    public DateOnly Today => DateOnly.FromDateTime(
        TimeZoneInfo.ConvertTime(_clock.GetUtcNow(), TimeZoneInfo.Local).DateTime);

    /// <summary>
    /// Presence probe for every configured source. This reads no usage file and needs no
    /// store, so a first install can separate "the tool is not installed" from
    /// "the tool is installed and not scanned yet" before any scan runs.
    /// </summary>
    public IReadOnlyList<UsageSourceDiagnostic> DetectSources() => _sources
        .Select(source => new UsageSourceDiagnostic(
            source.AgentId,
            UsageSourceReadStatus.NoData,
            ProbeRoot(source),
            RetainsLastReliableSnapshot: false))
        .ToArray();

    /// <summary>
    /// Exact token total one agent recorded since an arbitrary UTC instant. Quota windows
    /// reset mid-day, so the daily rollups cannot answer "spent this cycle"; this reads the
    /// stored events instead. A missing store means zero recorded usage, not an error.
    /// </summary>
    public async Task<long> SumTokensSinceAsync(
        AgentId agentId,
        DateTimeOffset fromInclusiveUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agentId);
        if (!File.Exists(_databasePath))
        {
            return 0;
        }

        UsageRepository repository;
        try
        {
            repository = await UsageRepository.OpenReadOnlyAsync(
                _databasePath,
                cancellationToken).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            return 0;
        }
        catch (UsageSchemaTooOldException)
        {
            repository = await UsageRepository.OpenAsync(
                _databasePath,
                cancellationToken).ConfigureAwait(false);
        }

        DateTimeOffset toExclusiveUtc = _clock.GetUtcNow();
        if (toExclusiveUtc <= fromInclusiveUtc)
        {
            return 0;
        }

        IReadOnlyList<UsageEvent> events = await repository.QueryUsageEventsAsync(
            fromInclusiveUtc,
            toExclusiveUtc,
            agentId,
            cancellationToken).ConfigureAwait(false);
        return events.Sum(usageEvent => usageEvent.Tokens.Total);
    }

    public async Task<LocalUsageRefreshResult?> ReadCachedAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_databasePath))
        {
            return null;
        }

        UsageRepository repository;
        try
        {
            repository = await UsageRepository.OpenReadOnlyAsync(
                _databasePath,
                cancellationToken).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (UsageSchemaTooOldException)
        {
            // This is TokenUsage's own durable store. Migrate it before the cache-first
            // read so an app upgrade cannot abort startup before the live refresh runs.
            repository = await UsageRepository.OpenAsync(
                _databasePath,
                cancellationToken).ConfigureAwait(false);
        }

        DateOnly today = Today;
        DateOnly fromInclusive = UsagePeriodPolicy.QueryStart(today);
        IReadOnlyList<DailyUsageRollup> rollups = await repository.QueryDailyRollupsAsync(
            fromInclusive,
            today,
            cancellationToken).ConfigureAwait(false);
        if (rollups.Count == 0)
        {
            return null;
        }

        HashSet<AgentId> cachedAgents = rollups
            .Select(rollup => rollup.AgentId)
            .ToHashSet();
        UsageSourceDiagnostic[] diagnostics = _sources
            .Select(source => CreateCachedDiagnostic(source, cachedAgents))
            .ToArray();
        UsageSourceReadStatus status = diagnostics.All(diagnostic =>
            diagnostic.Status == UsageSourceReadStatus.Complete)
                ? UsageSourceReadStatus.Complete
                : UsageSourceReadStatus.Partial;

        return new LocalUsageRefreshResult(
            rollups,
            fromInclusive,
            today,
            SourceKind,
            status,
            diagnostics,
            hasMultipleRealSources: HasMultipleRealSources);
    }

    public async Task<LocalUsageRefreshResult> RefreshAsync(
        CancellationToken cancellationToken = default)
    {
        UsageRepository repository = await UsageRepository.OpenAsync(
            _databasePath,
            cancellationToken).ConfigureAwait(false);
        UsageSourceReadResult[] readResults = await Task.WhenAll(
            _sources.Select(source => ReadSourceSafelyAsync(source, cancellationToken)))
            .ConfigureAwait(false);
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
                DateOnly reconcileFrom = UsagePeriodPolicy.ReconciliationStart(
                    today,
                    windowedSource.ReconciliationWindowDays);
                DateOnly EventDate(UsageEvent usageEvent) =>
                    DateOnly.FromDateTime(
                        TimeZoneInfo.ConvertTime(
                            usageEvent.OccurredAtUtc,
                            groupingTimeZone).DateTime);
                UsageEvent[] eventsInWindow = result.Events
                    .Where(usageEvent =>
                    {
                        DateOnly eventDate = EventDate(usageEvent);
                        return eventDate >= reconcileFrom && eventDate <= today;
                    })
                    .ToArray();
                UsageEvent[] olderEvents = result.Events
                    .Where(usageEvent =>
                    {
                        DateOnly eventDate = EventDate(usageEvent);
                        return eventDate < reconcileFrom && eventDate <= today;
                    })
                    .ToArray();
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
                    if (olderEvents.Length > 0)
                    {
                        await repository.UpsertAgentEventsAsync(
                            windowedSource.AgentId,
                            olderEvents,
                            cancellationToken).ConfigureAwait(false);
                    }
                }
                else if (eventsInWindow.Length > 0 || olderEvents.Length > 0)
                {
                    await repository.UpsertAgentEventsAsync(
                        windowedSource.AgentId,
                        eventsInWindow.Concat(olderEvents).ToArray(),
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

        // Sources that rotated parser versions stop emitting their old rows; the
        // supersession prune retires those fossils beyond the reconciliation window.
        var activeParserVersionsByAgent = _sources
            .OfType<IWindowedSnapshotUsageEventSource>()
            .GroupBy(source => source.AgentId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyCollection<string>)group
                    .Select(source => source.EventParserVersion)
                    .Distinct()
                    .ToArray());
        await repository.ApplyRetentionIfDueAsync(
                _clock.GetUtcNow().ToUniversalTime(),
                RetentionMinInterval,
                activeParserVersionsByAgent: activeParserVersionsByAgent,
                parserSupersessionDays: UsagePeriodPolicy.ReconciliationDays,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        DateOnly fromInclusive = UsagePeriodPolicy.QueryStart(today);
        IReadOnlyList<DailyUsageRollup> rollups = await repository.QueryDailyRollupsAsync(
            fromInclusive,
            today,
            cancellationToken).ConfigureAwait(false);

        UsageSourceDiagnostic[] diagnostics = _sources.Select((source, index) =>
            new UsageSourceDiagnostic(
                source.AgentId,
                readResults[index].Status,
                readResults[index].Issue,
                source is ISnapshotUsageEventSource
                    or IWindowedSnapshotUsageEventSource)).ToArray();

        return new LocalUsageRefreshResult(
            rollups,
            fromInclusive,
            today,
            SourceKind,
            readStatus,
            diagnostics,
            hasMultipleRealSources: HasMultipleRealSources);
    }

    /// <summary>
    /// Cached rollups prove past usage, not current presence. The root probe keeps an
    /// uninstalled tool from reporting a detected source while retained history stays visible.
    /// </summary>
    private static UsageSourceDiagnostic CreateCachedDiagnostic(
        IUsageEventSource source,
        HashSet<AgentId> cachedAgents)
    {
        bool hasCachedData = cachedAgents.Contains(source.AgentId);
        UsageSourceIssueKind rootIssue = ProbeRoot(source);
        if (rootIssue != UsageSourceIssueKind.Empty)
        {
            return new UsageSourceDiagnostic(
                source.AgentId,
                UsageSourceReadStatus.NoData,
                rootIssue,
                RetainsLastReliableSnapshot: hasCachedData);
        }

        return hasCachedData
            ? new UsageSourceDiagnostic(
                source.AgentId,
                UsageSourceReadStatus.Complete,
                UsageSourceIssueKind.None,
                RetainsLastReliableSnapshot: true)
            : new UsageSourceDiagnostic(
                source.AgentId,
                UsageSourceReadStatus.NoData,
                UsageSourceIssueKind.Empty,
                RetainsLastReliableSnapshot: false);
    }

    private static UsageSourceIssueKind ProbeRoot(IUsageEventSource source)
    {
        if (source is not IRootDetectingUsageEventSource rootDetecting)
        {
            return UsageSourceIssueKind.Empty;
        }

        try
        {
            return rootDetecting.IsRootAvailable
                ? UsageSourceIssueKind.Empty
                : UsageSourceIssueKind.RootUnavailable;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or System.Security.SecurityException)
        {
            return UsageSourceIssueKind.AccessBlocked;
        }
    }

    private static async Task<UsageSourceReadResult> ReadSourceSafelyAsync(
        IUsageEventSource source,
        CancellationToken cancellationToken)
    {
        try
        {
            return await source.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new UsageSourceReadResult(
                [],
                UsageSourceReadStatus.NoData,
                UsageSourceIssueKind.AccessBlocked);
        }
    }
}

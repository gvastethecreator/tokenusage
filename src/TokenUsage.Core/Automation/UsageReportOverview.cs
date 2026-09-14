using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;

namespace TokenUsage.Core.Automation;

public enum UsageOverviewFactKind
{
    Measured,
    Unavailable,
    Disabled,
    Unknown,
}

public sealed record UsageReportOverviewRequest(
    bool AttributionEnabled,
    UsageCacheComposition? Cache);

public sealed record UsageOverviewRankedRow(
    string Id,
    AgentId? AgentId,
    ModelProviderId? Host,
    ModelId? ModelId,
    OpaqueAttributionKey? ProjectKey,
    bool IsUnassigned,
    bool IsOther,
    int OtherCount,
    TokenBreakdown Tokens,
    decimal? ReportedCostUsd,
    decimal? EstimatedCostUsd,
    long UnpricedTokens,
    int ObservationCount,
    int AttributedSessionCount)
{
    internal IReadOnlyList<string> AttributedSessionKeys { get; init; } = [];
}

public sealed record UsageReportOverview(
    int ObservationCount,
    TokenBreakdown Tokens,
    decimal? ReportedCostUsd,
    decimal? EstimatedCostUsd,
    long UnpricedTokens,
    UsageOverviewFactKind SessionAvailability,
    int? AttributedSessionCount,
    int? RootSessionCount,
    int? DelegatedSessionCount,
    long UnassignedTokens,
    decimal? AttributedReportedCostPerSession,
    decimal? AttributedEstimatedCostPerSession,
    UsageOverviewFactKind CacheShareAvailability,
    string CacheShareMethod,
    decimal? CacheSharePercent,
    UsageOverviewFactKind RequestCountAvailability,
    string RequestCountReason,
    IReadOnlyList<UsageOverviewRankedRow> Models,
    IReadOnlyList<UsageOverviewRankedRow> Projects)
{
    public const int DefaultRankedCount = 8;
    public const string CacheShareMethodId = "measured-input-cache-share/v1";
    public const string RequestFinalityReason = "finality-not-established";

    public static UsageReportOverview Build(
        UsageReport report,
        IReadOnlyList<UsageOverviewObservation> observations,
        UsageReportOverviewRequest request,
        int rankedCount = DefaultRankedCount)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfLessThan(rankedCount, 1);

        HashSet<string> attributedKeys = observations
            .Where(row => row.SessionKey is not null)
            .Select(row => row.SessionKey!.Value)
            .ToHashSet(StringComparer.Ordinal);
        var sessions = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (UsageOverviewObservation row in observations)
        {
            if (row.SessionKey is null)
            {
                continue;
            }

            sessions[row.SessionKey.Value] = row.ParentSessionKey?.Value;
        }

        int root = 0;
        int delegated = 0;
        foreach ((string key, string? parent) in sessions)
        {
            if (parent is not null && attributedKeys.Contains(parent))
            {
                delegated++;
            }
            else
            {
                root++;
            }
        }

        decimal attributedReported = 0m;
        decimal attributedEstimated = 0m;
        bool hasAttributedReported = false;
        bool hasAttributedEstimated = false;
        long unassignedTokens = 0;
        foreach (UsageOverviewObservation row in observations)
        {
            if (row.SessionKey is null)
            {
                unassignedTokens = checked(unassignedTokens + row.Tokens.Total);
                continue;
            }

            if (row.ReportedCostUsd is { } reported)
            {
                attributedReported += reported;
                hasAttributedReported = true;
            }

            if (row.EstimatedCostUsd is { } estimated)
            {
                attributedEstimated += estimated;
                hasAttributedEstimated = true;
            }
        }

        UsageOverviewFactKind sessionAvailability = request.AttributionEnabled
            ? UsageOverviewFactKind.Measured
            : UsageOverviewFactKind.Disabled;
        int? attributedCount = request.AttributionEnabled ? sessions.Count : null;
        int? rootCount = request.AttributionEnabled ? root : null;
        int? delegatedCount = request.AttributionEnabled ? delegated : null;
        decimal? reportedPerSession = request.AttributionEnabled
            && attributedCount is > 0
            && hasAttributedReported
                ? attributedReported / attributedCount.Value
                : null;
        decimal? estimatedPerSession = request.AttributionEnabled
            && attributedCount is > 0
            && hasAttributedEstimated
                ? attributedEstimated / attributedCount.Value
                : null;

        (UsageOverviewFactKind cacheAvailability, decimal? cachePercent) = CacheShare(request.Cache);
        return new UsageReportOverview(
            observations.Count,
            report.Totals.Tokens,
            report.Totals.ReportedCostUsd,
            report.Totals.EstimatedCostUsd,
            report.Totals.UnpricedTokens,
            sessionAvailability,
            attributedCount,
            rootCount,
            delegatedCount,
            request.AttributionEnabled ? unassignedTokens : report.Totals.Tokens.Total,
            reportedPerSession,
            estimatedPerSession,
            cacheAvailability,
            CacheShareMethodId,
            cachePercent,
            UsageOverviewFactKind.Unavailable,
            RequestFinalityReason,
            RankByTokens(BuildModelRows(observations), rankedCount),
            RankByTokens(BuildProjectRows(observations), rankedCount));
    }

    public static IReadOnlyList<UsageOverviewObservation> Filter(
        IReadOnlyList<UsageOverviewObservation> observations,
        UsageReportSelection selection)
    {
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(selection);
        return observations
            .Where(row => selection.Includes(row.AgentId, row.Host, row.ModelId)
                && (!selection.HasConfigurationFilters
                    || selection.IncludesConfiguration(row.ObservedModelId, row.ReasoningEffort, row.ServiceTier)))
            .ToArray();
    }

    public static IReadOnlyList<UsageOverviewRankedRow> RankByTokens(
        IReadOnlyList<UsageOverviewRankedRow> rows,
        int topCount)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentOutOfRangeException.ThrowIfLessThan(topCount, 1);
        UsageOverviewRankedRow[] named = rows.Where(row => !row.IsOther).ToArray();
        UsageOverviewRankedRow[] unassigned = named.Where(row => row.IsUnassigned).ToArray();
        UsageOverviewRankedRow[] assigned = named
            .Where(row => !row.IsUnassigned)
            .OrderByDescending(row => row.Tokens.Total)
            .ThenBy(row => row.Id, StringComparer.Ordinal)
            .ToArray();
        if (assigned.Length <= topCount)
        {
            return [.. assigned, .. unassigned];
        }

        UsageOverviewRankedRow[] top = assigned.Take(topCount).ToArray();
        UsageOverviewRankedRow[] rest = assigned.Skip(topCount).ToArray();
        return [.. top, MergeOther(rest), .. unassigned];
    }

    private static UsageOverviewRankedRow[] BuildModelRows(
        IReadOnlyList<UsageOverviewObservation> observations) =>
        observations
            .GroupBy(row => (row.AgentId.Value, Host: row.Host?.Value, Model: row.ModelId.Value))
            .Select(group => Ranked(
                ReportModelId(group.Key.Item1, group.Key.Host, group.Key.Model),
                new AgentId(group.Key.Item1),
                group.Key.Host is null ? null : new ModelProviderId(group.Key.Host),
                new ModelId(group.Key.Model),
                projectKey: null,
                isUnassigned: false,
                group))
            .ToArray();

    private static UsageOverviewRankedRow[] BuildProjectRows(
        IReadOnlyList<UsageOverviewObservation> observations)
    {
        var assigned = observations
            .Where(row => row.ProjectKey is not null)
            .GroupBy(row => row.ProjectKey!.Value, StringComparer.Ordinal)
            .Select(group => Ranked(
                group.Key,
                agentId: null,
                host: null,
                modelId: null,
                new OpaqueAttributionKey(group.Key),
                isUnassigned: false,
                group))
            .ToArray();
        UsageOverviewObservation[] unassigned = observations.Where(row => row.ProjectKey is null).ToArray();
        if (unassigned.Length == 0)
        {
            return assigned;
        }

        return
        [
            .. assigned,
            Ranked(
                "unassigned",
                agentId: null,
                host: null,
                modelId: null,
                projectKey: null,
                isUnassigned: true,
                unassigned),
        ];
    }

    private static UsageOverviewRankedRow Ranked(
        string id,
        AgentId? agentId,
        ModelProviderId? host,
        ModelId? modelId,
        OpaqueAttributionKey? projectKey,
        bool isUnassigned,
        IEnumerable<UsageOverviewObservation> rows)
    {
        UsageOverviewObservation[] snapshot = rows.ToArray();
        TokenBreakdown tokens = new(0, 0, 0, 0, 0);
        decimal? reported = null;
        decimal? estimated = null;
        long unpriced = 0;
        foreach (UsageOverviewObservation row in snapshot)
        {
            tokens = Add(tokens, row.Tokens);
            if (row.ReportedCostUsd is { } reportedCost)
            {
                reported = (reported ?? 0m) + reportedCost;
            }

            if (row.EstimatedCostUsd is { } estimatedCost)
            {
                estimated = (estimated ?? 0m) + estimatedCost;
            }

            if (row.CostKind == CostKind.Unavailable)
            {
                unpriced = checked(unpriced + row.Tokens.Total);
            }
        }

        string[] sessionKeys = snapshot
            .Where(row => row.SessionKey is not null)
            .Select(row => row.SessionKey!.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return new UsageOverviewRankedRow(
            id,
            agentId,
            host,
            modelId,
            projectKey,
            isUnassigned,
            IsOther: false,
            OtherCount: 0,
            tokens,
            reported,
            estimated,
            unpriced,
            snapshot.Length,
            sessionKeys.Length)
        {
            AttributedSessionKeys = sessionKeys,
        };
    }

    private static UsageOverviewRankedRow MergeOther(UsageOverviewRankedRow[] rows)
    {
        TokenBreakdown tokens = new(0, 0, 0, 0, 0);
        decimal? reported = null;
        decimal? estimated = null;
        long unpriced = 0;
        int observations = 0;
        var sessionKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (UsageOverviewRankedRow row in rows)
        {
            tokens = Add(tokens, row.Tokens);
            if (row.ReportedCostUsd is { } reportedCost)
            {
                reported = (reported ?? 0m) + reportedCost;
            }

            if (row.EstimatedCostUsd is { } estimatedCost)
            {
                estimated = (estimated ?? 0m) + estimatedCost;
            }

            unpriced = checked(unpriced + row.UnpricedTokens);
            observations = checked(observations + row.ObservationCount);
            foreach (string key in row.AttributedSessionKeys)
            {
                sessionKeys.Add(key);
            }
        }

        return new UsageOverviewRankedRow(
            "other",
            null,
            null,
            null,
            null,
            false,
            true,
            rows.Length,
            tokens,
            reported,
            estimated,
            unpriced,
            observations,
            sessionKeys.Count)
        {
            AttributedSessionKeys = [.. sessionKeys],
        };
    }

    private static (UsageOverviewFactKind Availability, decimal? Percent) CacheShare(
        UsageCacheComposition? cache)
    {
        if (cache is null)
        {
            return (UsageOverviewFactKind.Unknown, null);
        }

        if (cache.EligibleInputTokens <= 0)
        {
            return (UsageOverviewFactKind.Unknown, null);
        }

        return (UsageOverviewFactKind.Measured, cache.CacheReadPercent);
    }

    private static string ReportModelId(string agent, string? host, string model) =>
        agent + "/" + (host ?? "") + "/" + model;

    private static TokenBreakdown Add(TokenBreakdown left, TokenBreakdown right) =>
        new(
            checked(left.Input + right.Input),
            checked(left.Output + right.Output),
            checked(left.Reasoning + right.Reasoning),
            checked(left.CacheRead + right.CacheRead),
            checked(left.CacheWrite + right.CacheWrite));
}

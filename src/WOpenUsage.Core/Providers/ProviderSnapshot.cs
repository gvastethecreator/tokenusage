namespace WOpenUsage.Core.Providers;

public sealed class ProviderSnapshot
{
    public ProviderSnapshot(
        ProviderId providerId,
        string displayName,
        string? planLabel,
        DateTimeOffset fetchedAtUtc,
        DateTimeOffset sourceObservedAtUtc,
        string timeZoneId,
        IEnumerable<MetricSnapshot> metrics,
        CoverageKind coverage,
        int adapterContractVersion)
    {
        ProviderId = providerId ?? throw new ArgumentNullException(nameof(providerId));
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        UtcTimestamp.Require(fetchedAtUtc, nameof(fetchedAtUtc));
        UtcTimestamp.Require(sourceObservedAtUtc, nameof(sourceObservedAtUtc));
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);
        ArgumentNullException.ThrowIfNull(metrics);

        if (sourceObservedAtUtc > fetchedAtUtc)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceObservedAtUtc),
                "Source observation cannot be newer than the fetch.");
        }

        if (!Enum.IsDefined(coverage))
        {
            throw new ArgumentOutOfRangeException(nameof(coverage));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(adapterContractVersion, 1);

        MetricSnapshot[] metricArray = metrics.ToArray();
        if (metricArray.Any(metric => metric is null))
        {
            throw new ArgumentException("Metrics cannot contain null values.", nameof(metrics));
        }

        string? duplicateMetric = metricArray
            .GroupBy(metric => metric.Id.Value, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)
            ?.Key;
        if (duplicateMetric is not null)
        {
            throw new ArgumentException(
                $"Metric ID '{duplicateMetric}' appears more than once.",
                nameof(metrics));
        }

        DisplayName = displayName;
        PlanLabel = string.IsNullOrWhiteSpace(planLabel) ? null : planLabel;
        FetchedAtUtc = fetchedAtUtc;
        SourceObservedAtUtc = sourceObservedAtUtc;
        TimeZoneId = timeZoneId;
        Metrics = Array.AsReadOnly(metricArray);
        Coverage = coverage;
        AdapterContractVersion = adapterContractVersion;
    }

    public ProviderId ProviderId { get; }

    public string DisplayName { get; }

    public string? PlanLabel { get; }

    public DateTimeOffset FetchedAtUtc { get; }

    public DateTimeOffset SourceObservedAtUtc { get; }

    public string TimeZoneId { get; }

    public IReadOnlyList<MetricSnapshot> Metrics { get; }

    public CoverageKind Coverage { get; }

    public int AdapterContractVersion { get; }
}

public static class SnapshotFreshness
{
    public static readonly TimeSpan DefaultMaxAge = TimeSpan.FromMinutes(10);

    public static bool IsStale(
        ProviderSnapshot snapshot,
        TimeProvider clock,
        TimeSpan? maxAge = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(clock);

        TimeSpan effectiveMaxAge = maxAge ?? DefaultMaxAge;
        if (effectiveMaxAge <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAge), "Maximum age must be positive.");
        }

        DateTimeOffset utcNow = clock.GetUtcNow().ToUniversalTime();
        return utcNow > snapshot.SourceObservedAtUtc
            && utcNow - snapshot.SourceObservedAtUtc > effectiveMaxAge;
    }
}

using TokenUsage.Core.Providers;

namespace TokenUsage.Core.Alerts;

public sealed class QuotaAlertFacts
{
    public QuotaAlertFacts(
        MetricId metricId,
        decimal remainingPercent,
        DateTimeOffset? resetsAtUtc,
        DateTimeOffset? projectedExhaustionAtUtc)
    {
        MetricId = metricId ?? throw new ArgumentNullException(nameof(metricId));
        if (remainingPercent is < 0m or > 100m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(remainingPercent),
                remainingPercent,
                "Remaining percent must be from 0 through 100.");
        }

        if (resetsAtUtc is DateTimeOffset reset)
        {
            UtcTimestamp.Require(reset, nameof(resetsAtUtc));
        }

        if (projectedExhaustionAtUtc is DateTimeOffset projectedExhaustion)
        {
            UtcTimestamp.Require(projectedExhaustion, nameof(projectedExhaustionAtUtc));
        }

        RemainingPercent = remainingPercent;
        ResetsAtUtc = resetsAtUtc;
        ProjectedExhaustionAtUtc = projectedExhaustionAtUtc;
    }

    public MetricId MetricId { get; }

    public decimal RemainingPercent { get; }

    public DateTimeOffset? ResetsAtUtc { get; }

    public DateTimeOffset? ProjectedExhaustionAtUtc { get; }
}

public sealed class ProviderAlertFacts
{
    public ProviderAlertFacts(
        ProviderId providerId,
        bool isStale,
        bool hasCredentialFailure,
        IEnumerable<QuotaAlertFacts> quotas)
    {
        ProviderId = providerId ?? throw new ArgumentNullException(nameof(providerId));
        ArgumentNullException.ThrowIfNull(quotas);

        QuotaAlertFacts[] quotaArray = quotas.ToArray();
        if (quotaArray.Any(quota => quota is null))
        {
            throw new ArgumentException("Quota facts cannot contain null values.", nameof(quotas));
        }

        string? duplicateMetric = quotaArray
            .GroupBy(quota => quota.MetricId.Value, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)
            ?.Key;
        if (duplicateMetric is not null)
        {
            throw new ArgumentException(
                $"Metric ID '{duplicateMetric}' appears more than once.",
                nameof(quotas));
        }

        IsStale = isStale;
        HasCredentialFailure = hasCredentialFailure;
        Quotas = Array.AsReadOnly(quotaArray);
    }

    public ProviderId ProviderId { get; }

    public bool IsStale { get; }

    public bool HasCredentialFailure { get; }

    public IReadOnlyList<QuotaAlertFacts> Quotas { get; }
}

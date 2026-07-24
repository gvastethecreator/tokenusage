using WOpenUsage.Core.Providers;

namespace WOpenUsage.Core.Alerts;

public sealed record AlertConditionKey
{
    public AlertConditionKey(
        ProviderId providerId,
        MetricId? metricId,
        AlertKind kind,
        DateTimeOffset? quotaWindowResetsAtUtc)
    {
        ProviderId = providerId ?? throw new ArgumentNullException(nameof(providerId));
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        bool isMetricAlert = kind is AlertKind.QuotaThreshold or AlertKind.ExhaustionForecast;
        if (isMetricAlert && metricId is null)
        {
            throw new ArgumentException("Quota alerts require a metric ID.", nameof(metricId));
        }

        if (!isMetricAlert && (metricId is not null || quotaWindowResetsAtUtc is not null))
        {
            throw new ArgumentException(
                "Provider alerts cannot carry metric or quota-window identity.",
                nameof(metricId));
        }

        if (kind == AlertKind.ExhaustionForecast && quotaWindowResetsAtUtc is null)
        {
            throw new ArgumentException(
                "Exhaustion forecasts require a quota-window reset.",
                nameof(quotaWindowResetsAtUtc));
        }

        if (quotaWindowResetsAtUtc is DateTimeOffset reset)
        {
            UtcTimestamp.Require(reset, nameof(quotaWindowResetsAtUtc));
        }

        MetricId = metricId;
        Kind = kind;
        QuotaWindowResetsAtUtc = quotaWindowResetsAtUtc;
    }

    public ProviderId ProviderId { get; }

    public MetricId? MetricId { get; }

    public AlertKind Kind { get; }

    public DateTimeOffset? QuotaWindowResetsAtUtc { get; }
}

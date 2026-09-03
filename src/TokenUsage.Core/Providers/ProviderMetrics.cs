namespace TokenUsage.Core.Providers;

public enum SourceKind
{
    OfficialLocalApi,
    OfficialRemoteApi,
    LocalLog,
    LocalDatabase,
    PrivateRemoteApi,
    ManualKey,
    Synthetic,
}

public enum MeasurementKind
{
    Measured,
    ProviderReported,
    Estimated,
    Derived,
}

public enum CoverageKind
{
    Complete,
    Partial,
    SummaryOnly,
    Unpriced,
}

public sealed record DataProvenance
{
    public DataProvenance(
        SourceKind sourceKind,
        MeasurementKind measurementKind,
        string adapterVersion)
    {
        if (!Enum.IsDefined(sourceKind))
        {
            throw new ArgumentOutOfRangeException(nameof(sourceKind));
        }

        if (!Enum.IsDefined(measurementKind))
        {
            throw new ArgumentOutOfRangeException(nameof(measurementKind));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(adapterVersion);

        SourceKind = sourceKind;
        MeasurementKind = measurementKind;
        AdapterVersion = adapterVersion;
    }

    public SourceKind SourceKind { get; }

    public MeasurementKind MeasurementKind { get; }

    public string AdapterVersion { get; }
}

public abstract class MetricSnapshot
{
    private protected MetricSnapshot(MetricId id, DataProvenance provenance)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Provenance = provenance ?? throw new ArgumentNullException(nameof(provenance));
    }

    public MetricId Id { get; }

    public DataProvenance Provenance { get; }
}

public enum ProgressResetCadence
{
    Daily,
    Weekly,
    Monthly,
    Never,
}

public enum MetricLabelSource
{
    Provider,
    Duration,
    Unknown,
}

public enum MetricLabelConfidence
{
    Exact,
    Derived,
    Unknown,
}

public enum ProviderReportedResetCause
{
    Manual,
    ResetCredit,
}

public sealed record ProviderResetEvidence
{
    public ProviderResetEvidence(
        ProviderReportedResetCause cause,
        DateTimeOffset occurredAtUtc)
    {
        if (!Enum.IsDefined(cause))
        {
            throw new ArgumentOutOfRangeException(nameof(cause));
        }

        UtcTimestamp.Require(occurredAtUtc, nameof(occurredAtUtc));
        Cause = cause;
        OccurredAtUtc = occurredAtUtc;
    }

    public ProviderReportedResetCause Cause { get; }

    public DateTimeOffset OccurredAtUtc { get; }
}

public sealed record MetricLabelEvidence
{
    public MetricLabelEvidence(
        string? providerMetricKey,
        string? providerMetricId,
        string? providerMetricName,
        MetricLabelSource source,
        MetricLabelConfidence confidence)
    {
        ValidateOptionalText(providerMetricKey, nameof(providerMetricKey));
        ValidateOptionalText(providerMetricId, nameof(providerMetricId));
        ValidateOptionalText(providerMetricName, nameof(providerMetricName));
        if (!Enum.IsDefined(source))
        {
            throw new ArgumentOutOfRangeException(nameof(source));
        }

        if (!Enum.IsDefined(confidence))
        {
            throw new ArgumentOutOfRangeException(nameof(confidence));
        }

        ProviderMetricKey = providerMetricKey;
        ProviderMetricId = providerMetricId;
        ProviderMetricName = providerMetricName;
        Source = source;
        Confidence = confidence;
    }

    public string? ProviderMetricKey { get; }

    public string? ProviderMetricId { get; }

    public string? ProviderMetricName { get; }

    public MetricLabelSource Source { get; }

    public MetricLabelConfidence Confidence { get; }

    private static void ValidateOptionalText(string? value, string paramName)
    {
        if (value is not null
            && (string.IsNullOrWhiteSpace(value)
                || value.Length > 64
                || value.Any(char.IsControl)))
        {
            throw new ArgumentException(
                "Metric label evidence must be readable and no longer than 64 characters.",
                paramName);
        }
    }
}

public sealed class ProgressMetricSnapshot : MetricSnapshot
{
    public ProgressMetricSnapshot(
        MetricId id,
        decimal used,
        decimal limit,
        DateTimeOffset? resetsAtUtc,
        DataProvenance provenance,
        string? unit = null,
        ProgressResetCadence? resetCadence = null,
        bool? isActive = null,
        string? displayName = null,
        MetricLabelEvidence? labelEvidence = null,
        ProviderResetEvidence? resetEvidence = null)
        : base(id, provenance)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(used);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(limit, 0m);
        if (resetsAtUtc is not null)
        {
            UtcTimestamp.Require(resetsAtUtc.Value, nameof(resetsAtUtc));
        }


        if (unit is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(unit);
        }

        if (resetCadence is not null && !Enum.IsDefined(resetCadence.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(resetCadence));
        }

        if (displayName is not null
            && (string.IsNullOrWhiteSpace(displayName)
                || displayName.Length > 64
                || displayName.Any(char.IsControl)))
        {
            throw new ArgumentException(
                "Metric display name must be readable and no longer than 64 characters.",
                nameof(displayName));
        }

        Used = used;
        Limit = limit;
        ResetsAtUtc = resetsAtUtc;
        Unit = unit;
        ResetCadence = resetCadence;
        IsActive = isActive;
        DisplayName = displayName;
        LabelEvidence = labelEvidence;
        ResetEvidence = resetEvidence;
    }

    public decimal Used { get; }

    public decimal Limit { get; }

    public decimal RemainingPercent => Math.Clamp((1m - (Used / Limit)) * 100m, 0m, 100m);

    public DateTimeOffset? ResetsAtUtc { get; }

    public string? Unit { get; }

    public ProgressResetCadence? ResetCadence { get; }

    public bool? IsActive { get; }

    public string? DisplayName { get; }

    public MetricLabelEvidence? LabelEvidence { get; }

    public ProviderResetEvidence? ResetEvidence { get; }
}

public sealed class ScalarMetricSnapshot : MetricSnapshot
{
    public ScalarMetricSnapshot(
        MetricId id,
        decimal value,
        string unit,
        DataProvenance provenance)
        : base(id, provenance)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(unit);

        Value = value;
        Unit = unit;
    }

    public decimal Value { get; }

    public string Unit { get; }
}

internal static class UtcTimestamp
{
    public static void Require(DateTimeOffset value, string paramName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The timestamp must use the UTC offset.", paramName);
        }
    }
}

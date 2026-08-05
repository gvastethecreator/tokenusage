namespace WOpenUsage.Core.Providers;

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
        bool? isActive = null)
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

        Used = used;
        Limit = limit;
        ResetsAtUtc = resetsAtUtc;
        Unit = unit;
        ResetCadence = resetCadence;
        IsActive = isActive;
    }

    public decimal Used { get; }

    public decimal Limit { get; }

    public decimal RemainingPercent => Math.Clamp((1m - (Used / Limit)) * 100m, 0m, 100m);

    public DateTimeOffset? ResetsAtUtc { get; }

    public string? Unit { get; }

    public ProgressResetCadence? ResetCadence { get; }

    public bool? IsActive { get; }
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

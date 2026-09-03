namespace TokenUsage.Core.Cache.Internal;

internal sealed class SnapshotCacheDocumentV1
{
    public int SchemaVersion { get; set; }

    public DateTimeOffset? WrittenAtUtc { get; set; }

    public List<SnapshotCacheProviderV1>? Snapshots { get; set; }
}

internal sealed class SnapshotCacheProviderV1
{
    public string? ProviderId { get; set; }

    public string? DisplayName { get; set; }

    public string? PlanLabel { get; set; }

    public DateTimeOffset? FetchedAtUtc { get; set; }

    public DateTimeOffset? SourceObservedAtUtc { get; set; }

    public string? TimeZoneId { get; set; }

    public string? Coverage { get; set; }

    public int? AdapterContractVersion { get; set; }

    public List<SnapshotCacheMetricV1>? Metrics { get; set; }

    public List<SnapshotCacheCapabilityV1>? Capabilities { get; set; }
}

internal sealed class SnapshotCacheCapabilityV1
{
    public string? Id { get; set; }

    public string? State { get; set; }

    public SnapshotCacheProvenanceV1? Provenance { get; set; }
}

internal sealed class SnapshotCacheMetricV1
{
    public string? Kind { get; set; }

    public string? Id { get; set; }

    public decimal? Used { get; set; }

    public decimal? Limit { get; set; }

    public DateTimeOffset? ResetsAtUtc { get; set; }

    public decimal? Value { get; set; }

    public string? Unit { get; set; }

    public string? ResetCadence { get; set; }

    public bool? IsActive { get; set; }

    public string? DisplayName { get; set; }

    public SnapshotCacheMetricLabelEvidenceV1? LabelEvidence { get; set; }

    public SnapshotCacheProvenanceV1? Provenance { get; set; }
}

internal sealed class SnapshotCacheMetricLabelEvidenceV1
{
    public string? ProviderMetricKey { get; set; }

    public string? ProviderMetricId { get; set; }

    public string? ProviderMetricName { get; set; }

    public string? Source { get; set; }

    public string? Confidence { get; set; }
}

internal sealed class SnapshotCacheProvenanceV1
{
    public string? SourceKind { get; set; }

    public string? MeasurementKind { get; set; }

    public string? AdapterVersion { get; set; }
}

namespace TokenUsage.Core.Usage;

public enum UsageRecordKind { Unknown, RequestFinal, IntervalDelta, Snapshot, DailyAggregate }

public enum UsageComponentAvailability { Unknown, Measured, Unavailable }

public sealed record UsageSourceInstanceId
{
    public UsageSourceInstanceId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length != 64 || value.Any(character =>
            character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new ArgumentException("Source instance IDs must be opaque lowercase SHA-256 hex values.", nameof(value));
        Value = value;
    }

    public string Value { get; }
}

public sealed record UsageDetailMetadata
{
    public static UsageDetailMetadata Unknown { get; } = new();

    public UsageDetailMetadata(UsageSourceInstanceId? sourceInstance = null,
        UsageRecordKind recordKind = UsageRecordKind.Unknown, long? representationRevision = null,
        UsageComponentAvailability input = UsageComponentAvailability.Unknown,
        UsageComponentAvailability output = UsageComponentAvailability.Unknown,
        UsageComponentAvailability reasoning = UsageComponentAvailability.Unknown,
        UsageComponentAvailability cacheRead = UsageComponentAvailability.Unknown,
        UsageComponentAvailability cacheWrite = UsageComponentAvailability.Unknown)
    {
        if (!Enum.IsDefined(recordKind)) throw new ArgumentOutOfRangeException(nameof(recordKind));
        if (representationRevision is <= 0) throw new ArgumentOutOfRangeException(nameof(representationRevision));
        if (!Enum.IsDefined(input) || !Enum.IsDefined(output) || !Enum.IsDefined(reasoning)
            || !Enum.IsDefined(cacheRead) || !Enum.IsDefined(cacheWrite))
            throw new ArgumentException("Token component availability must use known values.");
        SourceInstance = sourceInstance;
        RecordKind = recordKind;
        RepresentationRevision = representationRevision;
        Input = input;
        Output = output;
        Reasoning = reasoning;
        CacheRead = cacheRead;
        CacheWrite = cacheWrite;
    }

    public UsageSourceInstanceId? SourceInstance { get; }
    public UsageRecordKind RecordKind { get; }
    public long? RepresentationRevision { get; }
    public UsageComponentAvailability Input { get; }
    public UsageComponentAvailability Output { get; }
    public UsageComponentAvailability Reasoning { get; }
    public UsageComponentAvailability CacheRead { get; }
    public UsageComponentAvailability CacheWrite { get; }
}

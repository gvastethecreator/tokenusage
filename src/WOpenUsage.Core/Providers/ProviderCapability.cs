namespace WOpenUsage.Core.Providers;

public sealed record CapabilityId
{
    public CapabilityId(string value)
    {
        Value = StableId.Validate(value, nameof(value));
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public enum ProviderCapabilityState
{
    Available,
    NotRequested,
    NotConfigured,
    Degraded,
}

public sealed record ProviderCapabilitySnapshot
{
    public ProviderCapabilitySnapshot(
        CapabilityId id,
        ProviderCapabilityState state,
        DataProvenance provenance)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        if (!Enum.IsDefined(state))
        {
            throw new ArgumentOutOfRangeException(nameof(state));
        }

        State = state;
        Provenance = provenance ?? throw new ArgumentNullException(nameof(provenance));
    }

    public CapabilityId Id { get; }

    public ProviderCapabilityState State { get; }

    public DataProvenance Provenance { get; }
}

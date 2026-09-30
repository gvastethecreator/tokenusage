namespace TokenUsage.Core.Providers;

/// <summary>A provider and, when known, one opaque account identity.</summary>
public sealed record ProviderInstanceKey
{
    public ProviderInstanceKey(ProviderId providerId, string? accountKey = null)
    {
        ProviderId = providerId ?? throw new ArgumentNullException(nameof(providerId));
        if (accountKey is not null && (accountKey.Length != 64
            || accountKey.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))))
        {
            throw new ArgumentException("Account keys must be opaque SHA-256 hex values.", nameof(accountKey));
        }

        AccountKey = accountKey;
    }

    public ProviderId ProviderId { get; }
    public string? AccountKey { get; }
    public string Value => AccountKey is null ? ProviderId.Value : $"{ProviderId.Value}:{AccountKey}";
    public override string ToString() => Value;
}

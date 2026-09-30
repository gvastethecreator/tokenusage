namespace TokenUsage.Core.Providers;

public enum ProviderAccountStatus { Available, LoginRequired, IdentityChanged, Unavailable }

public sealed record ProviderAccountInfo(
    ProviderInstanceKey InstanceKey,
    int Number,
    string? Alias,
    bool IsActive,
    bool IsSelected,
    ProviderAccountStatus Status);

public interface IProviderAccountService
{
    bool WasActivated { get; }
    IReadOnlyList<ProviderAccountInfo> Accounts { get; }
    bool DiscoveryUnavailable { get; }
    Task<IReadOnlyList<ProviderAccountInfo>> DiscoverAsync(CancellationToken cancellationToken = default);
    Task SaveSelectionAsync(IReadOnlyCollection<string> accountKeys, CancellationToken cancellationToken = default);
}

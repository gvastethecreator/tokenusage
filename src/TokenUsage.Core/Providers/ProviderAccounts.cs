namespace TokenUsage.Core.Providers;

public enum ProviderAccountStatus { Available, LoginRequired, IdentityChanged, Unavailable }

public sealed record ProviderAccountInfo(
    ProviderInstanceKey InstanceKey,
    int Number,
    string? Alias,
    bool IsActive,
    bool IsSelected,
    ProviderAccountStatus Status);

/// <summary>Where a selected account keeps its own quota reset history.</summary>
public sealed record ProviderAccountHistory(ProviderAccountInfo Account, string HistoryPath);

public interface IProviderAccountService
{
    bool WasActivated { get; }
    bool IsActivated(string providerId);
    IReadOnlyList<ProviderAccountInfo> Accounts { get; }
    bool DiscoveryUnavailable { get; }
    Task<IReadOnlyList<ProviderAccountInfo>> DiscoverAsync(CancellationToken cancellationToken = default);
    Task SaveSelectionAsync(IReadOnlyCollection<string> accountKeys, CancellationToken cancellationToken = default);
    IReadOnlyList<ProviderAccountHistory> SelectedHistories();
}

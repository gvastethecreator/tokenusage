namespace TokenUsage.Core.Credentials;

public interface IManualProviderCredentialStore
{
    Task<bool> IsConfiguredAsync(
        string providerId,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        string providerId,
        ManualProviderSecret secret,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        string providerId,
        CancellationToken cancellationToken = default);

    Task<ManualProviderSecret?> ReadAsync(
        string providerId,
        CancellationToken cancellationToken = default);
}

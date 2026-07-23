using WOpenUsage.Providers.VercelAiGateway;

namespace WOpenUsage.Runtime.Windows.VercelAiGateway;

public interface IVercelGatewayCredentialStore : IVercelGatewayConnectionSource
{
    Task SaveAsync(string apiKey, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(CancellationToken cancellationToken = default);
}

public sealed class VercelGatewayCredentialStore : IVercelGatewayCredentialStore
{
    public const string ResourceName =
        "D6C94EDD-3747-465C-9A81-05DF5A4108C5/vercel-ai-gateway";
    public const string UserName = "manual";

    private readonly IVercelGatewayCredentialVault _vault;

    public VercelGatewayCredentialStore()
        : this(new WindowsVercelGatewayCredentialVault())
    {
    }

    public VercelGatewayCredentialStore(IVercelGatewayCredentialVault vault)
    {
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
    }

    public Task<bool> IsConfiguredAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool isConfigured = _vault.Contains(ResourceName, UserName);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(isConfigured);
    }

    public Task<VercelGatewayConnection?> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? apiKey = _vault.Read(ResourceName, UserName);
        cancellationToken.ThrowIfCancellationRequested();

        VercelGatewayConnection? connection = apiKey is null
            ? null
            : new VercelGatewayConnection(apiKey);
        return Task.FromResult(connection);
    }

    public Task SaveAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        cancellationToken.ThrowIfCancellationRequested();
        _vault.Write(ResourceName, UserName, apiKey);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool removed = _vault.Remove(ResourceName, UserName);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(removed);
    }
}

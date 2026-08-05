using TokenUsage.Providers.VercelAiGateway;

namespace TokenUsage.Runtime.Windows.VercelAiGateway;

public interface IVercelGatewayCredentialStore : IVercelGatewayConnectionSource
{
    Task SaveAsync(string apiKey, CancellationToken cancellationToken = default);

    Task SaveAsync(
        string apiKey,
        string keyId,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(CancellationToken cancellationToken = default);
}

public sealed class VercelGatewayCredentialStore : IVercelGatewayCredentialStore
{
    private const string KeyIdUserNamePrefix = "key-id:";

    public const string LegacyResourceName =
        "D6C94EDD-3747-465C-9A81-05DF5A4108C5/vercel-ai-gateway";
    public const string ResourceName =
        "D6C94EDD-3747-465C-9A81-05DF5A4108C5/vercel-ai-gateway/v1";
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
        bool isConfigured = _vault.FindUserNames(ResourceName).Count > 0
            || _vault.Contains(LegacyResourceName, UserName);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(isConfigured);
    }

    public Task<VercelGatewayConnection?> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<string> userNames = _vault.FindUserNames(ResourceName);
        if (userNames.Count > 1)
        {
            throw InvalidStoredConnection();
        }

        VercelGatewayConnection? connection;
        if (userNames.Count == 1)
        {
            string userName = userNames[0];
            string? apiKey = _vault.Read(ResourceName, userName);
            connection = apiKey is null
                ? throw InvalidStoredConnection()
                : CreateStoredConnection(apiKey, ParseKeyId(userName));
        }
        else
        {
            string? legacyApiKey = _vault.Read(LegacyResourceName, UserName);
            connection = legacyApiKey is null
                ? null
                : CreateStoredConnection(legacyApiKey, keyId: null);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(connection);
    }

    public Task SaveAsync(string apiKey, CancellationToken cancellationToken = default) =>
        SaveCoreAsync(apiKey, keyId: null, cancellationToken);

    public Task SaveAsync(
        string apiKey,
        string keyId,
        CancellationToken cancellationToken = default) =>
        SaveCoreAsync(apiKey, keyId, cancellationToken);

    private Task SaveCoreAsync(
        string apiKey,
        string? keyId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        if (keyId is not null)
        {
            _ = new VercelGatewayConnection(apiKey, keyId);
        }

        cancellationToken.ThrowIfCancellationRequested();
        string targetUserName = keyId is null
            ? UserName
            : KeyIdUserNamePrefix + keyId;
        IReadOnlyList<string> priorUserNames = _vault.FindUserNames(ResourceName);
        _vault.Write(ResourceName, targetUserName, apiKey);
        foreach (string priorUserName in priorUserNames)
        {
            if (!string.Equals(priorUserName, targetUserName, StringComparison.Ordinal))
            {
                _vault.Remove(ResourceName, priorUserName);
            }
        }

        _vault.Remove(LegacyResourceName, UserName);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool removed = false;
        foreach (string userName in _vault.FindUserNames(ResourceName))
        {
            removed = _vault.Remove(ResourceName, userName) || removed;
        }

        removed = _vault.Remove(LegacyResourceName, UserName) || removed;
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(removed);
    }

    private static string? ParseKeyId(string userName)
    {
        if (string.Equals(userName, UserName, StringComparison.Ordinal))
        {
            return null;
        }

        if (!userName.StartsWith(KeyIdUserNamePrefix, StringComparison.Ordinal))
        {
            throw InvalidStoredConnection();
        }

        string keyId = userName[KeyIdUserNamePrefix.Length..];
        try
        {
            _ = new VercelGatewayConnection("validation-placeholder", keyId);
            return keyId;
        }
        catch (ArgumentException)
        {
            throw InvalidStoredConnection();
        }
    }

    private static VercelGatewayConnection CreateStoredConnection(
        string apiKey,
        string? keyId)
    {
        try
        {
            return new VercelGatewayConnection(apiKey, keyId);
        }
        catch (ArgumentException)
        {
            throw InvalidStoredConnection();
        }
    }

    private static InvalidDataException InvalidStoredConnection() =>
        new("The stored Vercel connection is invalid.");
}

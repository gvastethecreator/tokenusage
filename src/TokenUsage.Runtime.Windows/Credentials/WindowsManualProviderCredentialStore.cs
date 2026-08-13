using System.Text;
using TokenUsage.Core.Credentials;
using TokenUsage.Core.Providers;
using TokenUsage.Providers.Catalog;
using TokenUsage.Providers.VercelAiGateway;
using TokenUsage.Runtime.Windows.VercelAiGateway;

namespace TokenUsage.Runtime.Windows.Credentials;

public sealed class WindowsManualProviderCredentialStore : IManualProviderCredentialStore
{
    public const string PackageIdentity = "D6C94EDD-3747-465C-9A81-05DF5A4108C5";
    public const string UserName = "manual";
    public const string VercelProviderId = "vercel-ai-gateway";

    private const string SecondaryUserPrefix = "x:";

    private readonly IWindowsCredentialVault _vault;
    private readonly IVercelGatewayCredentialStore _vercel;

    public WindowsManualProviderCredentialStore()
        : this(new WindowsCredentialVault(), new VercelGatewayCredentialStore())
    {
    }

    public WindowsManualProviderCredentialStore(
        IWindowsCredentialVault vault,
        IVercelGatewayCredentialStore vercel)
    {
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _vercel = vercel ?? throw new ArgumentNullException(nameof(vercel));
    }

    public static string ResourceName(string providerId)
    {
        _ = new ProviderId(providerId);
        return $"{PackageIdentity}/manual/{providerId}/v1";
    }

    public Task<bool> IsConfiguredAsync(
        string providerId,
        CancellationToken cancellationToken = default)
    {
        ProviderModuleDefinition module = RequireManualModule(providerId);
        cancellationToken.ThrowIfCancellationRequested();
        if (IsVercel(module))
        {
            return _vercel.IsConfiguredAsync(cancellationToken);
        }

        bool configured = _vault.FindUserNames(ResourceName(module.Id.Value)).Count > 0;
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(configured);
    }

    public async Task SaveAsync(
        string providerId,
        ManualProviderSecret secret,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(secret);
        ProviderModuleDefinition module = RequireManualModule(providerId);
        if (module.ManualCredentialKind.RequiresSecondaryField() && secret.SecondaryValue is null)
        {
            throw new ArgumentException(
                "This provider needs an additional connection value.",
                nameof(secret));
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (IsVercel(module))
        {
            if (secret.SecondaryValue is null)
            {
                await _vercel.SaveAsync(secret.ApiKey, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await _vercel
                    .SaveAsync(secret.ApiKey, secret.SecondaryValue, cancellationToken)
                    .ConfigureAwait(false);
            }

            return;
        }

        string resource = ResourceName(module.Id.Value);
        string targetUserName = CreateUserName(secret.SecondaryValue);
        IReadOnlyList<string> priorUserNames = _vault.FindUserNames(resource);
        _vault.Write(resource, targetUserName, secret.ApiKey);
        foreach (string priorUserName in priorUserNames)
        {
            if (!string.Equals(priorUserName, targetUserName, StringComparison.Ordinal))
            {
                _vault.Remove(resource, priorUserName);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    public async Task<bool> DeleteAsync(
        string providerId,
        CancellationToken cancellationToken = default)
    {
        ProviderModuleDefinition module = RequireManualModule(providerId);
        cancellationToken.ThrowIfCancellationRequested();
        if (IsVercel(module))
        {
            return await _vercel.DeleteAsync(cancellationToken).ConfigureAwait(false);
        }

        string resource = ResourceName(module.Id.Value);
        bool removed = false;
        foreach (string userName in _vault.FindUserNames(resource))
        {
            removed = _vault.Remove(resource, userName) || removed;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return removed;
    }

    public async Task<ManualProviderSecret?> ReadAsync(
        string providerId,
        CancellationToken cancellationToken = default)
    {
        ProviderModuleDefinition module = RequireManualModule(providerId);
        cancellationToken.ThrowIfCancellationRequested();
        if (IsVercel(module))
        {
            VercelGatewayConnection? connection = await _vercel
                .ReadAsync(cancellationToken)
                .ConfigureAwait(false);
            return connection is null
                ? null
                : new ManualProviderSecret(connection.ApiKey, connection.KeyId);
        }

        string resource = ResourceName(module.Id.Value);
        IReadOnlyList<string> userNames = _vault.FindUserNames(resource);
        if (userNames.Count > 1)
        {
            throw InvalidStoredConnection();
        }

        if (userNames.Count == 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }

        string userName = userNames[0];
        string? apiKey = _vault.Read(resource, userName);
        cancellationToken.ThrowIfCancellationRequested();
        return apiKey is null
            ? throw InvalidStoredConnection()
            : new ManualProviderSecret(apiKey, ParseSecondaryValue(userName));
    }

    private static ProviderModuleDefinition RequireManualModule(string providerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ProviderModuleDefinition module;
        try
        {
            module = ProviderModuleCatalog.Get(providerId);
        }
        catch (InvalidOperationException)
        {
            throw new ArgumentException("Unknown provider.", nameof(providerId));
        }

        if (!module.AcceptsManualCredential)
        {
            throw new ArgumentException(
                "This provider does not accept a manual credential.",
                nameof(providerId));
        }

        return module;
    }

    private static bool IsVercel(ProviderModuleDefinition module) =>
        string.Equals(module.Id.Value, VercelProviderId, StringComparison.Ordinal);

    private static string CreateUserName(string? secondaryValue) =>
        secondaryValue is null
            ? UserName
            : SecondaryUserPrefix + EncodeSecondaryValue(secondaryValue);

    private static string? ParseSecondaryValue(string userName)
    {
        if (string.Equals(userName, UserName, StringComparison.Ordinal))
        {
            return null;
        }

        if (!userName.StartsWith(SecondaryUserPrefix, StringComparison.Ordinal))
        {
            throw InvalidStoredConnection();
        }

        try
        {
            return DecodeSecondaryValue(userName[SecondaryUserPrefix.Length..]);
        }
        catch (FormatException)
        {
            throw InvalidStoredConnection();
        }
    }

    private static string EncodeSecondaryValue(string value)
    {
        string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        return encoded.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string DecodeSecondaryValue(string encoded)
    {
        string padded = encoded.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '=');
        return Encoding.UTF8.GetString(Convert.FromBase64String(padded));
    }

    private static InvalidDataException InvalidStoredConnection() =>
        new("The stored provider connection is invalid.");
}

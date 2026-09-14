using System.Security.Cryptography;
using TokenUsage.Core.Usage;
using TokenUsage.Platform.Windows.Credentials;

namespace TokenUsage.Runtime.Windows.Attribution;

public sealed class WindowsAttributionSecretStore
{
    public const string ResourceName =
        "D6C94EDD-3747-465C-9A81-05DF5A4108C5/attribution/hmac/v1";
    public const string UserName = "codex-session";

    private readonly IWindowsCredentialVault _vault;

    public WindowsAttributionSecretStore()
        : this(new WindowsCredentialVault())
    {
    }

    public WindowsAttributionSecretStore(IWindowsCredentialVault vault)
    {
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
    }

    public IOpaqueKeyDeriver? TryCreateDeriver()
    {
        try
        {
            string? stored = _vault.Read(ResourceName, UserName);
            byte[] key;
            if (stored is not null
                && stored.Length == 64
                && stored.All(character => character is (>= '0' and <= '9') or (>= 'a' and <= 'f') or (>= 'A' and <= 'F')))
            {
                key = Convert.FromHexString(stored);
            }
            else
            {
                key = RandomNumberGenerator.GetBytes(32);
                _vault.Write(ResourceName, UserName, Convert.ToHexString(key).ToLowerInvariant());
            }

            return new HmacOpaqueKeyDeriver(key);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException
                                               or InvalidOperationException
                                               or ArgumentException
                                               or FormatException)
        {
            return null;
        }
    }
}

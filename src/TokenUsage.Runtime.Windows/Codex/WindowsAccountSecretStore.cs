using System.Security.Cryptography;
using TokenUsage.Core.Usage;
using TokenUsage.Platform.Windows.Credentials;

namespace TokenUsage.Runtime.Windows.Codex;

internal sealed class WindowsAccountSecretStore(IWindowsCredentialVault vault)
{
    private const string Resource = "TokenUsage.QuotaAccounts";
    private const string User = "identity-hmac-v1";

    public IOpaqueKeyDeriver GetDeriver(bool wasActivated)
    {
        using var mutex = new Mutex(false, @"Local\TokenUsage.QuotaAccountIdentity.v1");
        bool acquired;
        try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(5)); }
        catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) throw new IOException("The account identity key is busy.");
        try { return ReadOrCreate(wasActivated); }
        finally { mutex.ReleaseMutex(); }
    }

    private HmacOpaqueKeyDeriver ReadOrCreate(bool wasActivated)
    {
        string? stored = vault.Read(Resource, User);
        if (stored is null)
        {
            if (wasActivated || vault.Contains(Resource, User))
                throw new IOException("The account identity key is missing or inaccessible.");
            stored = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            vault.Write(Resource, User, stored);
        }

        if (stored.Length != 64 || stored.Any(character => !Uri.IsHexDigit(character)))
            throw new IOException("The account identity key is unreadable and was preserved.");
        return new HmacOpaqueKeyDeriver(Convert.FromHexString(stored));
    }
}

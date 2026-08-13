using Windows.Security.Credentials;

namespace TokenUsage.Runtime.Windows.Credentials;

public sealed class WindowsCredentialVault : IWindowsCredentialVault
{
    private const int ElementNotFoundHResult = unchecked((int)0x80070490);

    private readonly PasswordVault _vault;

    public WindowsCredentialVault()
        : this(new PasswordVault())
    {
    }

    internal WindowsCredentialVault(PasswordVault vault)
    {
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
    }

    public IReadOnlyList<string> FindUserNames(string resource)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);

        try
        {
            string[] userNames = _vault
                .FindAllByResource(resource)
                .Select(credential => credential.UserName)
                .ToArray();
            return Array.AsReadOnly(userNames);
        }
        catch (Exception exception) when (IsElementNotFound(exception))
        {
            return Array.Empty<string>();
        }
    }

    public bool Contains(string resource, string userName)
    {
        ValidateIdentity(resource, userName);

        try
        {
            return _vault
                .FindAllByResource(resource)
                .Any(credential => string.Equals(
                    credential.UserName,
                    userName,
                    StringComparison.Ordinal));
        }
        catch (Exception exception) when (IsElementNotFound(exception))
        {
            return false;
        }
    }

    public string? Read(string resource, string userName)
    {
        ValidateIdentity(resource, userName);

        try
        {
            PasswordCredential credential = _vault.Retrieve(resource, userName);
            credential.RetrievePassword();
            return credential.Password;
        }
        catch (Exception exception) when (IsElementNotFound(exception))
        {
            return null;
        }
    }

    public void Write(string resource, string userName, string password)
    {
        ValidateIdentity(resource, userName);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        _ = Remove(resource, userName);
        _vault.Add(new PasswordCredential(resource, userName, password));
    }

    public bool Remove(string resource, string userName)
    {
        ValidateIdentity(resource, userName);

        try
        {
            PasswordCredential credential = _vault.Retrieve(resource, userName);
            _vault.Remove(credential);
            return true;
        }
        catch (Exception exception) when (IsElementNotFound(exception))
        {
            return false;
        }
    }

    private static bool IsElementNotFound(Exception exception) =>
        exception.HResult == ElementNotFoundHResult;

    private static void ValidateIdentity(string resource, string userName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
    }
}

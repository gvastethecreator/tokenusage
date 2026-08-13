namespace TokenUsage.Runtime.Windows.Credentials;

public interface IWindowsCredentialVault
{
    IReadOnlyList<string> FindUserNames(string resource);

    bool Contains(string resource, string userName);

    string? Read(string resource, string userName);

    void Write(string resource, string userName, string password);

    bool Remove(string resource, string userName);
}

using TokenUsage.Core.Credentials;
using TokenUsage.Platform.Windows.Credentials;
using TokenUsage.Providers.VercelAiGateway;
using TokenUsage.Runtime.Windows.Credentials;
using TokenUsage.Runtime.Windows.VercelAiGateway;

namespace TokenUsage.Platform.Windows.Tests.Credentials;

public sealed class WindowsManualProviderCredentialStoreTests
{
    [Fact]
    public void UsesPackageScopedManualResourceNames()
    {
        Assert.Equal(
            "D6C94EDD-3747-465C-9A81-05DF5A4108C5/manual/openrouter/v1",
            WindowsManualProviderCredentialStore.ResourceName("openrouter"));
        Assert.Equal("manual", WindowsManualProviderCredentialStore.UserName);
    }

    [Fact]
    public async Task PresenceCheckDoesNotReadSecret()
    {
        var vault = new FakeVault();
        vault.UserNamesByResource[WindowsManualProviderCredentialStore.ResourceName("openrouter")] = ["manual"];
        var store = new WindowsManualProviderCredentialStore(vault, new FakeVercelStore());

        bool configured = await store.IsConfiguredAsync("openrouter");

        Assert.True(configured);
        Assert.Equal(1, vault.FindCalls);
        Assert.Equal(0, vault.ReadCalls);
    }

    [Fact]
    public async Task SaveRoundTripsKeyAndSecondaryValue()
    {
        const string apiKey = "  key-with-significant-spacing  ";
        var vault = new FakeVault();
        var store = new WindowsManualProviderCredentialStore(vault, new FakeVercelStore());

        await store.SaveAsync("azure-openai", new ManualProviderSecret(
            apiKey,
            "https://example.openai.azure.com"));

        ManualProviderSecret secret = Assert.IsType<ManualProviderSecret>(
            await store.ReadAsync("azure-openai"));
        Assert.Equal(apiKey, secret.ApiKey);
        Assert.Equal("https://example.openai.azure.com", secret.SecondaryValue);
        Assert.Equal(1, vault.WriteCalls);
        Assert.DoesNotContain(
            apiKey,
            WindowsManualProviderCredentialStore.ResourceName("azure-openai"),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveReplacesPriorUserNameWithoutLeavingASecondCredential()
    {
        var vault = new FakeVault();
        var store = new WindowsManualProviderCredentialStore(vault, new FakeVercelStore());

        await store.SaveAsync("openrouter", new ManualProviderSecret("first-key"));
        await store.SaveAsync("openrouter", new ManualProviderSecret("second-key", "unused-meta"));

        ManualProviderSecret secret = Assert.IsType<ManualProviderSecret>(
            await store.ReadAsync("openrouter"));
        Assert.Equal("second-key", secret.ApiKey);
        Assert.Equal("unused-meta", secret.SecondaryValue);
        Assert.Single(vault.Secrets[WindowsManualProviderCredentialStore.ResourceName("openrouter")]);
    }

    [Fact]
    public async Task RejectsPolicyBlockedAndUnknownProvidersWithoutWriting()
    {
        var vault = new FakeVault();
        var vercel = new FakeVercelStore();
        var store = new WindowsManualProviderCredentialStore(vault, vercel);
        var secret = new ManualProviderSecret("private-api-key");

        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync("zai", secret));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync("claude", secret));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync("not-a-provider", secret));
        Assert.Equal(0, vault.WriteCalls);
        Assert.Equal(0, vercel.SaveCalls);
    }

    [Fact]
    public async Task VercelKeysUseTheDedicatedStore()
    {
        var vault = new FakeVault();
        var vercel = new FakeVercelStore();
        var store = new WindowsManualProviderCredentialStore(vault, vercel);
        var secret = new ManualProviderSecret("vercel-key", "key_abc-123");

        await store.SaveAsync("vercel-ai-gateway", secret);

        Assert.Equal(0, vault.WriteCalls);
        Assert.Equal(1, vercel.SaveCalls);
        Assert.Equal("vercel-key", vercel.Connection?.ApiKey);
        Assert.Equal("key_abc-123", vercel.Connection?.KeyId);
        Assert.True(await store.IsConfiguredAsync("vercel-ai-gateway"));

        ManualProviderSecret read = Assert.IsType<ManualProviderSecret>(
            await store.ReadAsync("vercel-ai-gateway"));
        Assert.Equal("vercel-key", read.ApiKey);
        Assert.Equal("key_abc-123", read.SecondaryValue);
        Assert.True(await store.DeleteAsync("vercel-ai-gateway"));
        Assert.Equal(1, vercel.DeleteCalls);
        Assert.False(await store.IsConfiguredAsync("vercel-ai-gateway"));
    }

    [Fact]
    public async Task DeleteRemovesOnlyTheRequestedProvider()
    {
        var vault = new FakeVault();
        var store = new WindowsManualProviderCredentialStore(vault, new FakeVercelStore());
        await store.SaveAsync("openrouter", new ManualProviderSecret("router-key"));
        await store.SaveAsync("openai", new ManualProviderSecret("openai-key"));

        Assert.True(await store.DeleteAsync("openrouter"));
        Assert.False(await store.IsConfiguredAsync("openrouter"));
        Assert.True(await store.IsConfiguredAsync("openai"));
    }

    [Fact]
    public async Task VaultErrorsPropagateWithoutStoreAddingCredentialValue()
    {
        const string apiKey = "credential-must-not-appear";
        var vault = new FakeVault
        {
            Exception = new InvalidOperationException("Credential Locker failed."),
        };
        var store = new WindowsManualProviderCredentialStore(vault, new FakeVercelStore());

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.SaveAsync("openrouter", new ManualProviderSecret(apiKey)));

        Assert.DoesNotContain(apiKey, error.Message, StringComparison.Ordinal);
    }

    private sealed class FakeVault : IWindowsCredentialVault
    {
        public Dictionary<string, List<string>> UserNamesByResource { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, Dictionary<string, string>> Secrets { get; } = new(StringComparer.Ordinal);

        public Exception? Exception { get; set; }

        public int FindCalls { get; private set; }

        public int ReadCalls { get; private set; }

        public int WriteCalls { get; private set; }

        public IReadOnlyList<string> FindUserNames(string resource)
        {
            FindCalls++;
            CompleteCall();
            return UserNamesByResource.TryGetValue(resource, out List<string>? names)
                ? names.ToArray()
                : [];
        }

        public bool Contains(string resource, string userName)
        {
            CompleteCall();
            return UserNamesByResource.TryGetValue(resource, out List<string>? names)
                && names.Contains(userName, StringComparer.Ordinal);
        }

        public string? Read(string resource, string userName)
        {
            ReadCalls++;
            CompleteCall();
            return Secrets.TryGetValue(resource, out Dictionary<string, string>? values)
                && values.TryGetValue(userName, out string? secret)
                ? secret
                : null;
        }

        public void Write(string resource, string userName, string password)
        {
            WriteCalls++;
            CompleteCall();
            if (!UserNamesByResource.TryGetValue(resource, out List<string>? names))
            {
                names = [];
                UserNamesByResource[resource] = names;
            }

            if (!names.Contains(userName, StringComparer.Ordinal))
            {
                names.Add(userName);
            }

            if (!Secrets.TryGetValue(resource, out Dictionary<string, string>? values))
            {
                values = new Dictionary<string, string>(StringComparer.Ordinal);
                Secrets[resource] = values;
            }

            values[userName] = password;
        }

        public bool Remove(string resource, string userName)
        {
            CompleteCall();
            bool removed = false;
            if (UserNamesByResource.TryGetValue(resource, out List<string>? names))
            {
                removed = names.RemoveAll(value =>
                    string.Equals(value, userName, StringComparison.Ordinal)) > 0;
            }

            if (Secrets.TryGetValue(resource, out Dictionary<string, string>? values))
            {
                removed = values.Remove(userName) || removed;
            }

            return removed;
        }

        private void CompleteCall()
        {
            if (Exception is not null)
            {
                throw Exception;
            }
        }
    }

    private sealed class FakeVercelStore : IVercelGatewayCredentialStore
    {
        public VercelGatewayConnection? Connection { get; private set; }

        public int SaveCalls { get; private set; }

        public int DeleteCalls { get; private set; }

        public Task<bool> IsConfiguredAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Connection is not null);
        }

        public Task<VercelGatewayConnection?> ReadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Connection);
        }

        public Task SaveAsync(string apiKey, CancellationToken cancellationToken = default) =>
            SaveAsync(apiKey, keyId: null!, cancellationToken);

        public Task SaveAsync(
            string apiKey,
            string keyId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveCalls++;
            Connection = string.IsNullOrWhiteSpace(keyId)
                ? new VercelGatewayConnection(apiKey)
                : new VercelGatewayConnection(apiKey, keyId);
            return Task.CompletedTask;
        }

        public Task<bool> DeleteAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DeleteCalls++;
            bool removed = Connection is not null;
            Connection = null;
            return Task.FromResult(removed);
        }
    }
}

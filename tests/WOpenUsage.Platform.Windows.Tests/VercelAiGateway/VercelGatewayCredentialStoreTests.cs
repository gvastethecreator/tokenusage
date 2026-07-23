using WOpenUsage.Providers.VercelAiGateway;
using WOpenUsage.Runtime.Windows.VercelAiGateway;

namespace WOpenUsage.Platform.Windows.Tests.VercelAiGateway;

public sealed class VercelGatewayCredentialStoreTests
{
    [Fact]
    public void UsesPackageScopedStableIdentity()
    {
        Assert.Equal(
            "D6C94EDD-3747-465C-9A81-05DF5A4108C5/vercel-ai-gateway/v1",
            VercelGatewayCredentialStore.ResourceName);
        Assert.Equal(
            "D6C94EDD-3747-465C-9A81-05DF5A4108C5/vercel-ai-gateway",
            VercelGatewayCredentialStore.LegacyResourceName);
        Assert.Equal("manual", VercelGatewayCredentialStore.UserName);
    }

    [Fact]
    public async Task PresenceCheckDoesNotReadSecret()
    {
        var vault = new FakeVault { ContainsResult = true, Secret = "secret-value" };
        var store = new VercelGatewayCredentialStore(vault);

        bool configured = await store.IsConfiguredAsync();

        Assert.True(configured);
        Assert.Equal(1, vault.ContainsCalls);
        Assert.Equal(0, vault.ReadCalls);
        AssertExactIdentity(vault);
    }

    [Fact]
    public async Task CurrentPresenceDoesNotProbeLegacyCredential()
    {
        var vault = new FakeVault();
        vault.UserNames.Add("manual");
        var store = new VercelGatewayCredentialStore(vault);

        bool configured = await store.IsConfiguredAsync();

        Assert.True(configured);
        Assert.Equal(1, vault.FindCalls);
        Assert.Equal(0, vault.ContainsCalls);
        Assert.Equal(0, vault.ReadCalls);
    }

    [Fact]
    public async Task ReadReturnsNullWhenCredentialIsMissing()
    {
        var vault = new FakeVault();
        var store = new VercelGatewayCredentialStore(vault);

        Assert.Null(await store.ReadAsync());
        Assert.Equal(1, vault.ReadCalls);
        AssertExactIdentity(vault);
    }

    [Fact]
    public async Task ReadPreservesCredentialValue()
    {
        const string apiKey = "  key-with-significant-spacing  ";
        var vault = new FakeVault { Secret = apiKey };
        var store = new VercelGatewayCredentialStore(vault);

        var connection = await store.ReadAsync();

        Assert.NotNull(connection);
        Assert.Equal(apiKey, connection.ApiKey);
    }

    [Fact]
    public async Task SaveWritesCurrentCredentialAndRoundTripsKeyOnlyConnection()
    {
        const string apiKey = "  key-with-significant-spacing  ";
        var vault = new FakeVault();
        var store = new VercelGatewayCredentialStore(vault);

        await store.SaveAsync(apiKey);

        Assert.Equal(apiKey, vault.WrittenSecret);
        Assert.Equal(VercelGatewayCredentialStore.ResourceName, vault.WrittenResource);
        Assert.Equal(VercelGatewayCredentialStore.UserName, vault.WrittenUserName);
        Assert.Equal(1, vault.WriteCalls);
        AssertExactIdentity(vault);

        VercelGatewayConnection connection = Assert.IsType<VercelGatewayConnection>(
            await store.ReadAsync());
        Assert.Equal(apiKey, connection.ApiKey);
        Assert.Null(connection.KeyId);
    }

    [Fact]
    public async Task SaveWithKeyIdRoundTripsOneCredentialEnvelope()
    {
        const string apiKey = "private-api-key";
        const string keyId = "key_abc-123";
        var vault = new FakeVault();
        var store = new VercelGatewayCredentialStore(vault);

        await store.SaveAsync(apiKey, keyId);

        VercelGatewayConnection connection = Assert.IsType<VercelGatewayConnection>(
            await store.ReadAsync());
        Assert.Equal(apiKey, connection.ApiKey);
        Assert.Equal(keyId, connection.KeyId);
        Assert.Equal(1, vault.WriteCalls);
    }

    [Fact]
    public async Task MultipleCurrentCredentialsFailWithoutReadingSecrets()
    {
        var vault = new FakeVault();
        vault.UserNames.Add("manual");
        vault.UserNames.Add("key-id:key_abc-123");
        var store = new VercelGatewayCredentialStore(vault);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.ReadAsync());

        Assert.Equal(0, vault.ReadCalls);
        Assert.DoesNotContain("key_abc-123", exception.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SaveRejectsMissingCredential(string? apiKey)
    {
        var vault = new FakeVault();
        var store = new VercelGatewayCredentialStore(vault);

        await Assert.ThrowsAnyAsync<ArgumentException>(() => store.SaveAsync(apiKey!));

        Assert.Equal(0, vault.WriteCalls);
    }

    [Theory]
    [InlineData("api_key_id_already-prefixed")]
    [InlineData("bad/id")]
    [InlineData("ábc")]
    public async Task SaveRejectsInvalidKeyIdBeforeVaultWrite(string keyId)
    {
        var vault = new FakeVault();
        var store = new VercelGatewayCredentialStore(vault);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.SaveAsync("private-api-key", keyId));

        Assert.Equal(0, vault.WriteCalls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DeleteReturnsVaultResult(bool removed)
    {
        var vault = new FakeVault { RemoveResult = removed };
        var store = new VercelGatewayCredentialStore(vault);

        Assert.Equal(removed, await store.DeleteAsync());
        Assert.Equal(1, vault.RemoveCalls);
        AssertExactIdentity(vault);
    }

    [Fact]
    public async Task DeleteRemovesCurrentAndLegacyCredentials()
    {
        var vault = new FakeVault { RemoveResult = true };
        vault.UserNames.Add("key-id:key_abc-123");
        var store = new VercelGatewayCredentialStore(vault);

        bool removed = await store.DeleteAsync();

        Assert.True(removed);
        Assert.Equal(2, vault.RemoveCalls);
        Assert.Equal(VercelGatewayCredentialStore.LegacyResourceName, vault.LastResource);
    }

    [Fact]
    public async Task PriorCancellationDoesNotTouchVault()
    {
        var vault = new FakeVault();
        var store = new VercelGatewayCredentialStore(vault);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.IsConfiguredAsync(cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.ReadAsync(cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.SaveAsync("secret-value", cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.DeleteAsync(cancellation.Token));

        Assert.Equal(0, vault.TotalCalls);
    }

    [Fact]
    public async Task CancellationAfterVaultCallIsObserved()
    {
        using var cancellation = new CancellationTokenSource();
        var vault = new FakeVault { AfterCall = cancellation.Cancel };
        var store = new VercelGatewayCredentialStore(vault);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.IsConfiguredAsync(cancellation.Token));

        Assert.Equal(1, vault.ContainsCalls);
    }

    [Fact]
    public async Task VaultErrorsPropagateWithoutStoreAddingCredentialValue()
    {
        const string apiKey = "credential-must-not-appear";
        var vault = new FakeVault
        {
            Exception = new InvalidOperationException("Credential Locker failed."),
        };
        var store = new VercelGatewayCredentialStore(vault);

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.SaveAsync(apiKey));

        Assert.DoesNotContain(apiKey, error.Message, StringComparison.Ordinal);
    }

    private static void AssertExactIdentity(FakeVault vault)
    {
        Assert.Equal(VercelGatewayCredentialStore.LegacyResourceName, vault.LastResource);
        Assert.Equal(VercelGatewayCredentialStore.UserName, vault.LastUserName);
    }

    private sealed class FakeVault : IVercelGatewayCredentialVault
    {
        public bool ContainsResult { get; set; }

        public string? Secret { get; set; }

        public string? WrittenSecret { get; private set; }

        public string? WrittenResource { get; private set; }

        public string? WrittenUserName { get; private set; }

        public List<string> UserNames { get; } = [];

        public bool RemoveResult { get; set; }

        public Exception? Exception { get; set; }

        public Action? AfterCall { get; set; }

        public string? LastResource { get; private set; }

        public string? LastUserName { get; private set; }

        public int ContainsCalls { get; private set; }

        public int FindCalls { get; private set; }

        public int ReadCalls { get; private set; }

        public int WriteCalls { get; private set; }

        public int RemoveCalls { get; private set; }

        public int TotalCalls => FindCalls + ContainsCalls + ReadCalls + WriteCalls + RemoveCalls;

        public IReadOnlyList<string> FindUserNames(string resource)
        {
            FindCalls++;
            LastResource = resource;
            LastUserName = null;
            CompleteCall();
            return resource == VercelGatewayCredentialStore.ResourceName
                ? UserNames.AsReadOnly()
                : Array.Empty<string>();
        }

        public bool Contains(string resource, string userName)
        {
            ContainsCalls++;
            Record(resource, userName);
            CompleteCall();
            return ContainsResult;
        }

        public string? Read(string resource, string userName)
        {
            ReadCalls++;
            Record(resource, userName);
            CompleteCall();
            return Secret;
        }

        public void Write(string resource, string userName, string password)
        {
            WriteCalls++;
            Record(resource, userName);
            WrittenSecret = password;
            WrittenResource = resource;
            WrittenUserName = userName;
            Secret = password;
            if (resource == VercelGatewayCredentialStore.ResourceName
                && !UserNames.Contains(userName, StringComparer.Ordinal))
            {
                UserNames.Add(userName);
            }
            CompleteCall();
        }

        public bool Remove(string resource, string userName)
        {
            RemoveCalls++;
            Record(resource, userName);
            CompleteCall();
            return RemoveResult;
        }

        private void Record(string resource, string userName)
        {
            LastResource = resource;
            LastUserName = userName;
        }

        private void CompleteCall()
        {
            AfterCall?.Invoke();
            if (Exception is not null)
            {
                throw Exception;
            }
        }
    }
}

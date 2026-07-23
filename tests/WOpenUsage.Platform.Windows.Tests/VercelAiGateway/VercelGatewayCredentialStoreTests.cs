using WOpenUsage.Runtime.Windows.VercelAiGateway;

namespace WOpenUsage.Platform.Windows.Tests.VercelAiGateway;

public sealed class VercelGatewayCredentialStoreTests
{
    [Fact]
    public void UsesPackageScopedStableIdentity()
    {
        Assert.Equal(
            "D6C94EDD-3747-465C-9A81-05DF5A4108C5/vercel-ai-gateway",
            VercelGatewayCredentialStore.ResourceName);
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
    public async Task SaveWritesExactCredential()
    {
        const string apiKey = "  key-with-significant-spacing  ";
        var vault = new FakeVault();
        var store = new VercelGatewayCredentialStore(vault);

        await store.SaveAsync(apiKey);

        Assert.Equal(apiKey, vault.WrittenSecret);
        Assert.Equal(1, vault.WriteCalls);
        AssertExactIdentity(vault);
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
        Assert.Equal(VercelGatewayCredentialStore.ResourceName, vault.LastResource);
        Assert.Equal(VercelGatewayCredentialStore.UserName, vault.LastUserName);
    }

    private sealed class FakeVault : IVercelGatewayCredentialVault
    {
        public bool ContainsResult { get; set; }

        public string? Secret { get; set; }

        public string? WrittenSecret { get; private set; }

        public bool RemoveResult { get; set; }

        public Exception? Exception { get; set; }

        public Action? AfterCall { get; set; }

        public string? LastResource { get; private set; }

        public string? LastUserName { get; private set; }

        public int ContainsCalls { get; private set; }

        public int ReadCalls { get; private set; }

        public int WriteCalls { get; private set; }

        public int RemoveCalls { get; private set; }

        public int TotalCalls => ContainsCalls + ReadCalls + WriteCalls + RemoveCalls;

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

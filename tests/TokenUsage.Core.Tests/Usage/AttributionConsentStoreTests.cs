using TokenUsage.Core.Usage;

namespace TokenUsage.Core.Tests.Usage;

public sealed class AttributionConsentStoreTests
{
    [Fact]
    public async Task MissingDocumentIsDisabledWithEpochZero()
    {
        using var folder = new TemporaryFolder();
        var store = new AttributionConsentStore(Path.Combine(folder.Root, AttributionConsentStore.DefaultFileName));

        AttributionConsent consent = await store.LoadAsync(AttributionCapability.CodexSession);

        Assert.Equal(AttributionConsentState.Disabled, consent.State);
        Assert.Equal(0, consent.Epoch);
        Assert.False(consent.AllowsLinks);
    }

    [Fact]
    public async Task EnableRevokePurgeAndReEnableAdvanceTheEpoch()
    {
        using var folder = new TemporaryFolder();
        var store = new AttributionConsentStore(Path.Combine(folder.Root, AttributionConsentStore.DefaultFileName));

        AttributionConsent enabled = await store.EnableAsync(AttributionCapability.CodexSession);
        Assert.Equal(AttributionConsentState.Enabled, enabled.State);
        Assert.Equal(1, enabled.Epoch);
        Assert.True(enabled.AllowsLinks);
        Assert.NotNull(enabled.EnabledAtUtc);

        AttributionConsent pending = await store.RevokeAsync(AttributionCapability.CodexSession);
        Assert.Equal(AttributionConsentState.PurgePending, pending.State);
        Assert.Equal(2, pending.Epoch);
        Assert.False(pending.AllowsLinks);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.EnableAsync(AttributionCapability.CodexSession));

        AttributionConsent purged = await store.CompletePurgeAsync(AttributionCapability.CodexSession);
        Assert.Equal(AttributionConsentState.DisabledAfterPurge, purged.State);
        Assert.Equal(2, purged.Epoch);

        AttributionConsent reenabled = await store.EnableAsync(AttributionCapability.CodexSession);
        Assert.Equal(AttributionConsentState.Enabled, reenabled.State);
        Assert.Equal(3, reenabled.Epoch);
        Assert.True(enabled.AcceptsEpoch(1));
        Assert.False(reenabled.AcceptsEpoch(1));

        AttributionConsent project = await store.EnableAsync(AttributionCapability.CodexProject);
        Assert.Equal(1, project.Epoch);
        Assert.Equal(3, (await store.LoadAsync(AttributionCapability.CodexSession)).Epoch);
    }

    [Fact]
    public async Task CrashDuringPurgeLeavesTheCapabilityDisabled()
    {
        using var folder = new TemporaryFolder();
        string path = Path.Combine(folder.Root, AttributionConsentStore.DefaultFileName);
        var store = new AttributionConsentStore(path);
        await store.EnableAsync(AttributionCapability.CodexSession);
        await store.RevokeAsync(AttributionCapability.CodexSession);

        AttributionConsent afterRestart = await new AttributionConsentStore(path)
            .LoadAsync(AttributionCapability.CodexSession);

        Assert.Equal(AttributionConsentState.PurgePending, afterRestart.State);
        Assert.False(afterRestart.AllowsLinks);
        AttributionConsent finished = await new AttributionConsentStore(path)
            .CompletePurgeAsync(AttributionCapability.CodexSession);
        Assert.Equal(AttributionConsentState.DisabledAfterPurge, finished.State);
        Assert.False(finished.AllowsLinks);
    }

    private sealed class TemporaryFolder : IDisposable
    {
        public TemporaryFolder()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "tokenusage-attribution-consent-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
                }
            }
            catch
            {
                // best effort
            }
        }
    }
}

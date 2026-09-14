using System.Security.Cryptography;
using System.Text;
using TokenUsage.Core.Usage;

namespace TokenUsage.Core.Tests.Usage;

public sealed class AttributionAliasStoreTests
{
    [Fact]
    public async Task SetLoadAndRemoveKeepAShortDisplayName()
    {
        using var folder = new TemporaryFolder();
        var store = new AttributionAliasStore(Path.Combine(folder.Root, AttributionAliasStore.DefaultFileName));
        OpaqueAttributionKey key = Key("alias-target");

        await store.SetAsync(key, "  Lab notebook  ");
        Assert.Equal("Lab notebook", await store.LoadAsync(key));
        await store.RemoveAsync(key);
        Assert.Null(await store.LoadAsync(key));
    }

    [Theory]
    [InlineData("C:/secret")]
    [InlineData(@"D:\work")]
    [InlineData("label:with-colon")]
    public async Task PathCharactersAreRejected(string label)
    {
        using var folder = new TemporaryFolder();
        var store = new AttributionAliasStore(Path.Combine(folder.Root, AttributionAliasStore.DefaultFileName));

        await Assert.ThrowsAsync<ArgumentException>(() => store.SetAsync(Key("rejected"), label));
        Assert.Null(await store.LoadAsync(Key("rejected")));
    }

    private static OpaqueAttributionKey Key(string seed) =>
        new(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed))).ToLowerInvariant());

    private sealed class TemporaryFolder : IDisposable
    {
        public TemporaryFolder()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "tokenusage-attribution-alias-tests",
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
            catch (IOException)
            {
            }
        }
    }
}

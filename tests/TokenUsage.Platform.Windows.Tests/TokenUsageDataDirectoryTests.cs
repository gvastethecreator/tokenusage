using TokenUsage.Platform.Windows.Storage;

namespace TokenUsage.Platform.Windows.Tests;

public sealed class TokenUsageDataDirectoryTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"tokenusage-data-directory-{Guid.NewGuid():N}");

    [Fact]
    public void PortableMarkerUsesOneSharedDataDirectoryForAppAndNestedCli()
    {
        string cliDirectory = Path.Combine(_root, "cli");
        Directory.CreateDirectory(cliDirectory);
        File.WriteAllText(
            Path.Combine(_root, TokenUsageDataDirectory.PortableMarkerFileName),
            "TokenUsage portable distribution");

        string appData = TokenUsageDataDirectory.Resolve(
            configuredDataDirectory: null,
            applicationBaseDirectory: _root,
            packagedDataDirectory: ThrowUnexpectedPackageLookup);
        string cliData = TokenUsageDataDirectory.Resolve(
            configuredDataDirectory: null,
            applicationBaseDirectory: cliDirectory,
            packagedDataDirectory: ThrowUnexpectedPackageLookup);

        string expected = Path.Combine(_root, TokenUsageDataDirectory.PortableDataDirectoryName);
        Assert.Equal(expected, appData);
        Assert.Equal(expected, cliData);
    }

    [Fact]
    public void ExplicitOverrideWinsOverPortableAndPackagedLocations()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(
            Path.Combine(_root, TokenUsageDataDirectory.PortableMarkerFileName),
            "TokenUsage portable distribution");
        string configured = Path.Combine(_root, "custom-data");

        string actual = TokenUsageDataDirectory.Resolve(
            configured,
            _root,
            ThrowUnexpectedPackageLookup);

        Assert.Equal(configured, actual);
    }

    [Fact]
    public void PackagedBuildUsesItsIdentityScopedDataDirectory()
    {
        Directory.CreateDirectory(_root);
        string packaged = Path.Combine(_root, "LocalState");

        string actual = TokenUsageDataDirectory.Resolve(
            configuredDataDirectory: null,
            applicationBaseDirectory: _root,
            packagedDataDirectory: () => packaged);

        Assert.Equal(packaged, actual);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static string ThrowUnexpectedPackageLookup() =>
        throw new InvalidOperationException("Package data should not be queried.");
}

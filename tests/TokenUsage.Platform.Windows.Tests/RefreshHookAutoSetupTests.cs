using TokenUsage.Runtime.Windows;
using TokenUsage.Runtime.Windows.Cursor;
using TokenUsage.Runtime.Windows.Grok;
using TokenUsage.Runtime.Windows.Zcode;

namespace TokenUsage.Platform.Windows.Tests;

public sealed class RefreshHookAutoSetupTests
{
    [Fact]
    public void DetectedProvidersGetTheirHooksInstalled()
    {
        using var root = new TemporaryRoot();
        // The ZCode and Grok installers take the provider home; the Cursor
        // installer takes the user profile root and appends ".cursor" itself.
        var zcode = new ZcodeHookInstaller(zcodeHomeOverride: root.Folder(".zcode"));
        var grok = new GrokHookInstaller(grokHomeOverride: root.Folder(".grok"));
        root.Folder(".cursor");
        var cursor = new CursorHookInstaller(root.Path);

        RefreshHookAutoSetup.EnsureInstalled(zcode, grok, cursor);

        Assert.Equal(ZcodeHookInstallationStatus.Installed, zcode.GetStatus());
        Assert.Equal(GrokHookInstallationStatus.Installed, grok.GetStatus());
        Assert.Equal(CursorHookInstallationStatus.Installed, cursor.GetRefreshStatus());
    }

    [Fact]
    public void MissingProvidersAreSkipped()
    {
        string missingRoot = Path.Combine(
            Path.GetTempPath(),
            "tokenusage-auto-setup-tests",
            Guid.NewGuid().ToString("N"));
        var zcode = new ZcodeHookInstaller(zcodeHomeOverride: Path.Combine(missingRoot, ".zcode"));
        var grok = new GrokHookInstaller(grokHomeOverride: Path.Combine(missingRoot, ".grok"));
        var cursor = new CursorHookInstaller(missingRoot);

        RefreshHookAutoSetup.EnsureInstalled(zcode, grok, cursor);

        Assert.False(zcode.IsProviderDetected);
        Assert.False(grok.IsProviderDetected);
        Assert.False(cursor.IsProviderDetected);
        Assert.Equal(ZcodeHookInstallationStatus.NotInstalled, zcode.GetStatus());
        Assert.Equal(GrokHookInstallationStatus.NotInstalled, grok.GetStatus());
        Assert.Equal(CursorHookInstallationStatus.NotInstalled, cursor.GetRefreshStatus());
        Assert.False(Directory.Exists(missingRoot));
    }

    [Fact]
    public void FailingInstallersNeverBlockTheOthers()
    {
        using var root = new TemporaryRoot();
        // The ZCode home exists, but "cli" is a file, so writing the config fails.
        string brokenZcodeHome = root.Folder(".zcode");
        File.WriteAllText(Path.Combine(brokenZcodeHome, "cli"), "not a directory");
        var failingZcode = new ZcodeHookInstaller(zcodeHomeOverride: brokenZcodeHome);
        var grok = new GrokHookInstaller(grokHomeOverride: root.Folder(".grok"));

        RefreshHookAutoSetup.EnsureInstalled(failingZcode, grok, null);

        Assert.True(failingZcode.IsProviderDetected);
        Assert.Equal(GrokHookInstallationStatus.Installed, grok.GetStatus());
    }

    [Fact]
    public void DisabledBackgroundCollectionUninstallsTheHooks()
    {
        using var root = new TemporaryRoot();
        var zcode = new ZcodeHookInstaller(zcodeHomeOverride: root.Folder(".zcode"));
        var grok = new GrokHookInstaller(grokHomeOverride: root.Folder(".grok"));
        root.Folder(".cursor");
        var cursor = new CursorHookInstaller(root.Path);

        RefreshHookAutoSetup.EnsureInstalled(zcode, grok, cursor, backgroundCollection: true);
        RefreshHookAutoSetup.EnsureInstalled(zcode, grok, cursor, backgroundCollection: false);

        Assert.Equal(ZcodeHookInstallationStatus.NotInstalled, zcode.GetStatus());
        Assert.Equal(GrokHookInstallationStatus.NotInstalled, grok.GetStatus());
        Assert.Equal(CursorHookInstallationStatus.NotInstalled, cursor.GetRefreshStatus());
    }

    private sealed class TemporaryRoot : IDisposable
    {
        public TemporaryRoot()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "tokenusage-auto-setup-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string Folder(string name)
        {
            string path = System.IO.Path.Combine(Path, name);
            Directory.CreateDirectory(path);
            return path;
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch
            {
                // best effort
            }
        }
    }
}

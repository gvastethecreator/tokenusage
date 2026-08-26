using System.Globalization;
using TokenUsage.Cli;
using TokenUsage.Runtime.Windows.Grok;

namespace TokenUsage.Cli.Tests;

public sealed class GrokCommandTests
{
    [Theory]
    [InlineData("")]
    [InlineData("install-hook|status")]
    [InlineData("nonsense")]
    public async Task InvalidArgumentsReturnTwoWithUsage(string argumentLine)
    {
        string[] arguments = argumentLine.Length == 0
            ? []
            : argumentLine.Split('|');

        int exitCode = await GrokCommand.RunAsync(
            arguments,
            new StringWriter(CultureInfo.InvariantCulture),
            new StringWriter(CultureInfo.InvariantCulture));

        Assert.Equal(2, exitCode);
    }

    [Fact]
    public async Task InstallStatusAndUninstallRoundTrip()
    {
        using var home = new TemporaryHome();
        var installer = new GrokHookInstaller(grokHomeOverride: home.GrokHome);
        var output = new StringWriter(CultureInfo.InvariantCulture);

        int installExit = await GrokCommand.RunAsync(["install-hook"], output, TextWriter.Null, installer);
        int statusExit = await GrokCommand.RunAsync(["status"], output, TextWriter.Null, installer);
        int uninstallExit = await GrokCommand.RunAsync(["uninstall-hook"], output, TextWriter.Null, installer);

        Assert.Equal(0, installExit);
        Assert.Equal(0, statusExit);
        Assert.Equal(0, uninstallExit);
        string text = output.ToString();
        Assert.Contains("installed", text, StringComparison.Ordinal);
        Assert.Contains("removed", text, StringComparison.Ordinal);
        Assert.Equal(GrokHookInstallationStatus.NotInstalled, installer.GetStatus());
        Assert.False(File.Exists(Path.Combine(
            home.GrokHome,
            "hooks",
            GrokHookInstaller.HookFileName)));
    }

    private sealed class TemporaryHome : IDisposable
    {
        public TemporaryHome()
        {
            GrokHome = Path.Combine(
                Path.GetTempPath(),
                "tokenusage-grok-command-tests",
                Guid.NewGuid().ToString("N"),
                ".grok");
        }

        public string GrokHome { get; }

        public void Dispose()
        {
            var root = new DirectoryInfo(GrokHome);
            while (root is not null && root.Name != ".grok")
            {
                root = root.Parent;
            }

            try
            {
                if (root is not null && Directory.Exists(root.Parent!.FullName))
                {
                    Directory.Delete(root.Parent.FullName, recursive: true);
                }
            }
            catch
            {
                // best effort
            }
        }
    }
}

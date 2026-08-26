using System.Text;
using TokenUsage.Cli;
using TokenUsage.Runtime.Windows.Zcode;

namespace TokenUsage.Cli.Tests;

public sealed class ZcodeCommandTests
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
        var output = new StringWriter();
        var error = new StringWriter();

        int exitCode = await ZcodeCommand.RunAsync(arguments, output, error);

        Assert.Equal(2, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Contains(ZcodeCommand.UsageText, error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InstallStatusAndUninstallRoundTrip()
    {
        using var home = new TemporaryHome();
        var installer = new ZcodeHookInstaller(zcodeHomeOverride: home.ZcodeHome);

        var output = new StringWriter();
        int installExit = await ZcodeCommand.RunAsync(
            ["install-hook"], output, new StringWriter(), installer);
        int statusExit = await ZcodeCommand.RunAsync(
            ["status"], output, new StringWriter(), installer);
        int uninstallExit = await ZcodeCommand.RunAsync(
            ["uninstall-hook"], output, new StringWriter(), installer);

        Assert.Equal(0, installExit);
        Assert.Equal(0, statusExit);
        Assert.Equal(0, uninstallExit);
        string text = output.ToString();
        Assert.Contains("installed", text, StringComparison.Ordinal);
        Assert.Contains("removed", text, StringComparison.Ordinal);
        Assert.Equal(ZcodeHookInstallationStatus.NotInstalled, installer.GetStatus());
    }

    [Fact]
    public async Task StopHookDrainsInputRunsRefreshAndStaysSilent()
    {
        using var home = new TemporaryHome();
        var installer = new ZcodeHookInstaller(zcodeHomeOverride: home.ZcodeHome);
        var payload = new StringReader(
            """{"session_id":"private","last_assistant_message":"private content"}""");
        bool refreshed = false;
        var output = new StringWriter();
        var error = new StringWriter();

        int exitCode = await ZcodeCommand.RunAsync(
            ["stop-hook"],
            output,
            error,
            installer,
            runRefresh: () =>
            {
                refreshed = true;
                return Task.FromResult(0);
            },
            standardInput: payload);

        Assert.Equal(0, exitCode);
        Assert.True(refreshed);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task StopHookWithoutRefreshDelegateStaysSilent()
    {
        using var home = new TemporaryHome();
        var installer = new ZcodeHookInstaller(zcodeHomeOverride: home.ZcodeHome);
        var output = new StringWriter();

        int exitCode = await ZcodeCommand.RunAsync(
            ["stop-hook"],
            output,
            new StringWriter(),
            installer,
            standardInput: new StringReader("{}"));

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, output.ToString());
    }

    [Fact]
    public async Task StopHookSwallowsRefreshFailures()
    {
        using var home = new TemporaryHome();
        var installer = new ZcodeHookInstaller(zcodeHomeOverride: home.ZcodeHome);
        var output = new StringWriter();
        var error = new StringWriter();

        int exitCode = await ZcodeCommand.RunAsync(
            ["stop-hook"],
            output,
            error,
            installer,
            runRefresh: () => throw new IOException("C:\\Users\\private\\secret"),
            standardInput: new StringReader("{}"));

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    private sealed class TemporaryHome : IDisposable
    {
        public TemporaryHome()
        {
            ZcodeHome = Path.Combine(
                Path.GetTempPath(),
                "tokenusage-zcode-command-tests",
                Guid.NewGuid().ToString("N"),
                ".zcode");
        }

        public string ZcodeHome { get; }

        public void Dispose()
        {
            var root = new DirectoryInfo(ZcodeHome);
            while (root is not null && root.Name != ".zcode")
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

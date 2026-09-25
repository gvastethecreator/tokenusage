using System.Globalization;
using System.Text;
using TokenUsage.Cli;
using TokenUsage.Core.Providers;
using TokenUsage.Providers.Claude;
using TokenUsage.Runtime.Windows.Claude;

namespace TokenUsage.Cli.Tests;

public sealed class ClaudeCommandTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    private static readonly string Payload =
        $$"""
        {
          "session_id": "abc123",
          "cwd": "C:/work/secret-project",
          "rate_limits": {
            "five_hour": { "used_percentage": 23.5, "resets_at": {{Now.AddHours(2).ToUnixTimeSeconds()}} },
            "seven_day": { "used_percentage": 41.2, "resets_at": {{Now.AddDays(3).ToUnixTimeSeconds()}} }
          }
        }
        """;

    [Theory]
    [InlineData("")]
    [InlineData("install-hook|status")]
    [InlineData("nonsense")]
    public async Task InvalidArgumentsReturnTwoWithUsage(string argumentLine)
    {
        using var home = new TemporaryRoot();
        string[] arguments = argumentLine.Length == 0 ? [] : argumentLine.Split('|');
        var error = new StringWriter(CultureInfo.InvariantCulture);

        int exitCode = await ClaudeCommand.RunAsync(
            arguments,
            TextWriter.Null,
            error,
            installer: home.CreateInstaller());

        Assert.Equal(2, exitCode);
        Assert.Contains(ClaudeCommand.UsageText, error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InstallStatusAndUninstallRoundTrip()
    {
        using var home = new TemporaryRoot();
        ClaudeSettingsInstaller installer = home.CreateInstaller();
        var output = new StringWriter(CultureInfo.InvariantCulture);

        Assert.Equal(0, await ClaudeCommand.RunAsync(["install-hook"], output, TextWriter.Null, installer: installer));
        Assert.Equal(0, await ClaudeCommand.RunAsync(["install-statusline"], output, TextWriter.Null, installer: installer));
        Assert.Equal(0, await ClaudeCommand.RunAsync(
            ["status"],
            output,
            TextWriter.Null,
            resolveDataDirectory: () => home.DataDirectory,
            installer: installer));

        Assert.Contains("Stop hook: installed", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("status line wrapper: installed", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("limits: no reading yet", output.ToString(), StringComparison.Ordinal);

        Assert.Equal(0, await ClaudeCommand.RunAsync(["uninstall-hook"], TextWriter.Null, TextWriter.Null, installer: installer));
        Assert.Equal(0, await ClaudeCommand.RunAsync(["uninstall-statusline"], TextWriter.Null, TextWriter.Null, installer: installer));
        Assert.Equal(ClaudeIntegrationStatus.NotInstalled, installer.GetHookStatus());
        Assert.Equal(ClaudeIntegrationStatus.NotInstalled, installer.GetStatusLineStatus());
    }

    [Fact]
    public async Task StatusLineStoresTheReadingAndPrintsTheLimits()
    {
        using var home = new TemporaryRoot();
        var output = new StringWriter(CultureInfo.InvariantCulture);

        int exitCode = await ClaudeCommand.RunAsync(
            ["statusline"],
            output,
            TextWriter.Null,
            resolveDataDirectory: () => home.DataDirectory,
            standardInput: new MemoryStream(Encoding.UTF8.GetBytes(Payload)),
            clock: new LimitsCommandTests.FixedTimeProvider(Now),
            installer: home.CreateInstaller());

        Assert.Equal(0, exitCode);
        Assert.Equal("5h 24% · 7d 41%" + Environment.NewLine, output.ToString());
        ClaudeRateLimitSnapshot stored = new ClaudeRateLimitStore(
            ClaudeRateLimitStore.DefaultPath(home.DataDirectory)).Load()!;
        Assert.Equal(Now, stored.ObservedAtUtc);
        Assert.Equal(2, stored.Windows.Count);
    }

    [Fact]
    public async Task StatusLineForwardsTheSameInputToThePreviousCommand()
    {
        using var home = new TemporaryRoot();
        ClaudeSettingsInstaller installer = home.CreateInstaller();
        home.WriteSettings("""{ "statusLine": { "type": "command", "command": "findstr secret-project" } }""");
        installer.InstallStatusLine();
        var output = new StringWriter(CultureInfo.InvariantCulture);

        int exitCode = await ClaudeCommand.RunAsync(
            ["statusline"],
            output,
            TextWriter.Null,
            resolveDataDirectory: () => home.DataDirectory,
            standardInput: new MemoryStream(Encoding.UTF8.GetBytes(Payload)),
            clock: new LimitsCommandTests.FixedTimeProvider(Now),
            installer: installer,
            forwarder: new ClaudeStatusLineForwarder(findBash: () => null));

        Assert.Equal(0, exitCode);
        // findstr echoes the matching input line, proving the previous command got the payload.
        Assert.Contains("secret-project", output.ToString(), StringComparison.Ordinal);
        Assert.NotNull(new ClaudeRateLimitStore(ClaudeRateLimitStore.DefaultPath(home.DataDirectory)).Load());
    }

    [Fact]
    public async Task StatusLineWithoutLimitsStaysQuietAndStoresNothing()
    {
        using var home = new TemporaryRoot();
        var output = new StringWriter(CultureInfo.InvariantCulture);

        int exitCode = await ClaudeCommand.RunAsync(
            ["statusline"],
            output,
            TextWriter.Null,
            resolveDataDirectory: () => home.DataDirectory,
            standardInput: new MemoryStream(Encoding.UTF8.GetBytes("""{ "session_id": "x" }""")),
            installer: home.CreateInstaller());

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.False(File.Exists(ClaudeRateLimitStore.DefaultPath(home.DataDirectory)));
    }

    [Fact]
    public async Task LimitsAccessReadsTheStoredClaudeReading()
    {
        using var home = new TemporaryRoot();
        var clock = new LimitsCommandTests.FixedTimeProvider(Now);
        new ClaudeRateLimitStore(ClaudeRateLimitStore.DefaultPath(home.DataDirectory)).Save(
            new ClaudeRateLimitSnapshot(Now, [new("five_hour", 40m, Now.AddHours(1))]));

        IReadOnlyList<ProviderSnapshot> snapshots = await LocalLimitsCliAccess.ReadAsync(
            home.DataDirectory,
            providerId: "claude",
            forceRefresh: true,
            clock,
            CancellationToken.None);

        ProviderSnapshot claude = Assert.Single(snapshots);
        Assert.Equal("claude", claude.ProviderId.Value);
        Assert.Equal(60m, claude.Metrics.OfType<ProgressMetricSnapshot>().Single().RemainingPercent);
    }

    private sealed class TemporaryRoot : IDisposable
    {
        public TemporaryRoot()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "tokenusage-claude-command-tests",
                Guid.NewGuid().ToString("N"));
            ConfigDirectory = Path.Combine(Root, ".claude");
            DataDirectory = Path.Combine(Root, "data");
            Directory.CreateDirectory(ConfigDirectory);
        }

        public string Root { get; }

        public string ConfigDirectory { get; }

        public string DataDirectory { get; }

        public ClaudeSettingsInstaller CreateInstaller() =>
            new(homeDirectory: Root, configDirectoryOverride: ConfigDirectory);

        public void WriteSettings(string json) => File.WriteAllText(
            Path.Combine(ConfigDirectory, ClaudeSettingsInstaller.SettingsFileName),
            json);

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}

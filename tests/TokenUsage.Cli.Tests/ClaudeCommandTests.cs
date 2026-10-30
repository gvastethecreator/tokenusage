using System.Globalization;
using System.Text;
using TokenUsage.Core.Cache;
using TokenUsage.Cli;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;
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
    [InlineData("install-statusline|statusline")]
    [InlineData("install-hook")]
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
    public async Task InstallAndUninstallStatusLineRoundTrip()
    {
        using var home = new TemporaryRoot();
        ClaudeSettingsInstaller installer = home.CreateInstaller();

        Assert.Equal(0, await ClaudeCommand.RunAsync(["install-statusline"], TextWriter.Null, TextWriter.Null,
            resolveDataDirectory: () => home.DataDirectory, installer: installer));
        Assert.Equal(ClaudeIntegrationStatus.Installed, installer.GetStatusLineStatus());
        var consent = new ClaudeLimitsConsentStore(ClaudeLimitsConsentStore.DefaultPath(home.DataDirectory));
        Assert.Equal(ClaudeLimitsConsentStatus.Enabled, await consent.ReadOrMigrateAsync(true));

        Assert.Equal(0, await ClaudeCommand.RunAsync(["uninstall-statusline"], TextWriter.Null, TextWriter.Null,
            resolveDataDirectory: () => home.DataDirectory, installer: installer));
        Assert.Equal(ClaudeIntegrationStatus.NotInstalled, installer.GetStatusLineStatus());
        Assert.Equal(ClaudeLimitsConsentStatus.Disabled, await consent.ReadOrMigrateAsync(false));
    }

    [Fact]
    public async Task StatusLineStoresTheReadingAndPrintsTheLimits()
    {
        using var home = new TemporaryRoot();
        home.CreateInstaller().InstallStatusLine();
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
    public async Task StatusLineFinishesWhenTheHostLeavesItsInputOpen()
    {
        // A killed host can leave the pipe open in another process; waiting for its end kept
        // status line processes alive for hours.
        using var home = new TemporaryRoot();
        home.CreateInstaller().InstallStatusLine();
        var output = new StringWriter(CultureInfo.InvariantCulture);

        int exitCode = await ClaudeCommand.RunAsync(
                ["statusline"],
                output,
                TextWriter.Null,
                resolveDataDirectory: () => home.DataDirectory,
                standardInput: new OpenPipe(Encoding.UTF8.GetBytes(Payload)),
                clock: new LimitsCommandTests.FixedTimeProvider(Now),
                installer: home.CreateInstaller())
            .WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(0, exitCode);
        Assert.Equal("5h 24% · 7d 41%" + Environment.NewLine, output.ToString());
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

        // Settings turns limits off this way; forwarding must keep working without storing.
        await new ClaudeLimitsConsentStore(ClaudeLimitsConsentStore.DefaultPath(home.DataDirectory))
            .DisableAsync(CancellationToken.None);
        new ClaudeRateLimitStore(ClaudeRateLimitStore.DefaultPath(home.DataDirectory)).Delete();
        output.GetStringBuilder().Clear();
        Assert.Equal(0, await ClaudeCommand.RunAsync(["statusline"], output, TextWriter.Null,
            resolveDataDirectory: () => home.DataDirectory,
            standardInput: new MemoryStream(Encoding.UTF8.GetBytes(Payload)), installer: installer,
            forwarder: new ClaudeStatusLineForwarder(findBash: () => null)));
        Assert.Contains("secret-project", output.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists(ClaudeRateLimitStore.DefaultPath(home.DataDirectory)));
        Assert.Equal(ClaudeIntegrationStatus.Installed, installer.GetStatusLineStatus());
    }

    [Fact]
    public async Task StatusLineWithoutConsentPreservesAnExternalWrapperAndStoresNothing()
    {
        using var home = new TemporaryRoot();
        var installer = home.CreateInstaller();
        home.WriteSettings("""{"statusLine":{"type":"command","command":"wrapper --command 'tokenusage claude statusline'"}}""");
        string settings = File.ReadAllText(installer.SettingsPath);
        var limits = new ClaudeRateLimitStore(ClaudeRateLimitStore.DefaultPath(home.DataDirectory));
        var originalReading = new ClaudeRateLimitSnapshot(Now, [new("seven_day", 76m, Now.AddDays(4))]);
        limits.Save(originalReading);
        var output = new StringWriter(CultureInfo.InvariantCulture);

        Assert.Equal(0, await ClaudeCommand.RunAsync(["statusline"], output, TextWriter.Null,
            resolveDataDirectory: () => home.DataDirectory, installer: installer,
            clock: new LimitsCommandTests.FixedTimeProvider(Now),
            standardInput: new MemoryStream(Encoding.UTF8.GetBytes(Payload))));
        Assert.Equal("5h 24% · 7d 41%" + Environment.NewLine, output.ToString());
        Assert.Equal(76m, Assert.Single(limits.Load()!.Windows).UsedPercent);
        var consent = new ClaudeLimitsConsentStore(ClaudeLimitsConsentStore.DefaultPath(home.DataDirectory));
        Assert.Equal(ClaudeLimitsConsentStatus.NotEnabled, await consent.ReadOrMigrateAsync(false));
        Assert.Equal(settings, File.ReadAllText(installer.SettingsPath));
    }

    [Fact]
    public async Task AFailedPreviousCommandPreservesItsOutputAndExitCode()
    {
        using var home = new TemporaryRoot();
        var installer = home.CreateInstaller();
        home.WriteSettings("""{"statusLine":{"type":"command","command":"echo misleading& exit /b 7"}}""");
        installer.InstallStatusLine();
        var output = new StringWriter(CultureInfo.InvariantCulture);

        Assert.Equal(7, await ClaudeCommand.RunAsync(["statusline"], output, TextWriter.Null,
            resolveDataDirectory: () => home.DataDirectory, installer: installer,
            clock: new LimitsCommandTests.FixedTimeProvider(Now),
            standardInput: new MemoryStream(Encoding.UTF8.GetBytes(Payload)),
            forwarder: new ClaudeStatusLineForwarder(findBash: () => null)));

        Assert.Equal("misleading" + Environment.NewLine, output.ToString());
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
    public async Task LimitsAccessReadsTheSharedClaudeCacheInsteadOfTheStatusLineSidecar()
    {
        using var home = new TemporaryRoot();
        var clock = new LimitsCommandTests.FixedTimeProvider(Now);
        new ClaudeRateLimitStore(ClaudeRateLimitStore.DefaultPath(home.DataDirectory)).Save(
            new ClaudeRateLimitSnapshot(Now, [new("seven_day", 76m, Now.AddDays(4))]));
        ProviderRefreshRegistration registration = LocalLimitsCliAccess.CreateLiveHost(home.DataDirectory, clock)
            .Registrations.Single(item => item.Provider.Descriptor.Id.Value == "claude");
        await registration.Store.UpsertLastGoodAsync(ClaudeRateLimitSnapshotMapper.Map(
            new(Now, [new("five_hour", 40m, Now.AddHours(1))]), Now, "UTC")!);

        IReadOnlyList<ProviderSnapshot> snapshots = await LocalLimitsCliAccess.ReadAsync(
            home.DataDirectory,
            providerId: "claude",
            forceRefresh: false,
            clock,
            CancellationToken.None);

        ProviderSnapshot claude = Assert.Single(snapshots);
        Assert.Equal("claude", claude.ProviderId.Value);
        Assert.Equal(60m, claude.Metrics.OfType<ProgressMetricSnapshot>().Single().RemainingPercent);
    }

    /// <summary>
    /// Delivers its payload, then behaves like a pipe whose writer never closes: later reads
    /// never complete and ignore cancellation, as a blocked console read does.
    /// </summary>
    private sealed class OpenPipe(byte[] payload) : Stream
    {
        private bool _sent;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_sent) return new ValueTask<int>(new TaskCompletionSource<int>().Task);
            _sent = true;
            payload.CopyTo(buffer);
            return ValueTask.FromResult(payload.Length);
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
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

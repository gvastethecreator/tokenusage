using System.Globalization;
using TokenUsage.Core.Providers;

namespace TokenUsage.Cli.Tests;

public sealed class ProviderDiagnosticsCommandTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 23, 4, 0, 0, 987, TimeSpan.Zero);

    [Fact]
    public async Task DoctorJsonMatchesVersionedGolden()
    {
        var output = new StringWriter(CultureInfo.InvariantCulture);

        int exitCode = await DoctorCommand.RunAsync(
            ["--format", "json"],
            output,
            TextWriter.Null,
            _ => Task.FromResult(CreateSnapshot()),
            new FixedTimeProvider(Now));

        Assert.Equal(0, exitCode);
        Assert.Equal(
            Normalize(await File.ReadAllTextAsync(GoldenPath("tokenusage.doctor.v1.json"))),
            Normalize(output.ToString()));
    }

    [Fact]
    public async Task HumanOutputUsesFixedOrdinalOrderUnderAnotherCulture()
    {
        using var culture = new CultureScope("es-ES");
        var doctor = new StringWriter(CultureInfo.InvariantCulture);

        await DoctorCommand.RunAsync(
            [], doctor, TextWriter.Null, _ => Task.FromResult(CreateSnapshot()),
            new FixedTimeProvider(Now));

        Assert.Equal(
            "codex-cache: present\n"
            + "codex-cli: detected\n"
            + "local-usage-amp: present\n"
            + "local-usage-antigravity: present\n"
            + "local-usage-claude: absent\n"
            + "local-usage-copilot: present\n"
            + "local-usage-cursor: absent\n"
            + "local-usage-goose: absent\n"
            + "local-usage-grok: present\n"
            + "local-usage-hermes: absent\n"
            + "local-usage-mux: absent\n"
            + "local-usage-opencode: unreadable\n"
            + "local-usage-zcode: absent\n"
            + "usage-db: present\n",
            doctor.ToString().Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("customer-secret")]
    [InlineData("--format")]
    [InlineData("--format|xml")]
    [InlineData("--format|json|--format|human")]
    public async Task InvalidArgumentsReturnTwoWithoutCallingReader(string argumentLine)
    {
        string[] arguments = argumentLine.Split('|');
        var output = new StringWriter(CultureInfo.InvariantCulture);
        var error = new StringWriter(CultureInfo.InvariantCulture);
        bool called = false;
        ProviderDiagnosticsReader reader = _ =>
        {
            called = true;
            return Task.FromResult(CreateSnapshot());
        };

        int exitCode = await DoctorCommand.RunAsync(
            arguments, output, error, reader, new FixedTimeProvider(Now));

        Assert.Equal(2, exitCode);
        Assert.False(called);
        Assert.Equal(string.Empty, output.ToString());
        Assert.DoesNotContain("customer-secret", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReaderFailureReturnsFourAndRedactsDetails()
    {
        var output = new StringWriter(CultureInfo.InvariantCulture);
        var error = new StringWriter(CultureInfo.InvariantCulture);
        ProviderDiagnosticsReader reader = _ => throw new IOException(
            "C:\\Users\\private\\auth.json Bearer secret@example.test");

        int exitCode = await DoctorCommand.RunAsync(
            [], output, error, reader, new FixedTimeProvider(Now));

        Assert.Equal(4, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Equal("Unable to run doctor checks." + Environment.NewLine, error.ToString());
    }

    [Fact]
    public async Task CallerCancellationPropagates()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        ProviderDiagnosticsReader reader = token =>
            Task.FromCanceled<ProviderDiagnosticsSnapshot>(token);

        Task<int> task = DoctorCommand.RunAsync(
            [], TextWriter.Null, TextWriter.Null, reader,
            new FixedTimeProvider(Now), cancellation.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    [Fact]
    public async Task UndefinedDoctorStatusIsRejectedWithoutWritingIt()
    {
        ProviderDiagnosticsSnapshot baseline = CreateSnapshot();
        ProviderDiagnosticsSnapshot snapshot = baseline with
        {
            Checks =
            [
                baseline.Checks[0] with { Status = (DoctorCheckStatus)99 },
                .. baseline.Checks.Skip(1),
            ],
        };
        var output = new StringWriter(CultureInfo.InvariantCulture);

        int exitCode = await DoctorCommand.RunAsync(
            ["--format", "json"], output, TextWriter.Null,
            _ => Task.FromResult(snapshot), new FixedTimeProvider(Now));

        Assert.Equal(4, exitCode);
        Assert.Equal(string.Empty, output.ToString());
    }

    internal static ProviderDiagnosticsSnapshot CreateSnapshot() =>
        new(
        [
            new("amp", "Amp",
                [ProviderCapability.LocalUsage, ProviderCapability.Spend],
                ProviderDetectionStatus.Detected, ProviderDataStatus.Present),
            new("antigravity", "Antigravity",
                [ProviderCapability.LocalUsage, ProviderCapability.Spend],
                ProviderDetectionStatus.Detected, ProviderDataStatus.Present),
            new("claude", "Claude",
                [ProviderCapability.Limits, ProviderCapability.LocalUsage, ProviderCapability.Spend],
                ProviderDetectionStatus.Detected, ProviderDataStatus.Absent),
            new("opencode", "OpenCode",
                [ProviderCapability.LocalUsage, ProviderCapability.Spend],
                ProviderDetectionStatus.Unavailable, ProviderDataStatus.Unreadable),
            new("grok", "Grok Build",
                [ProviderCapability.LocalUsage, ProviderCapability.Spend],
                ProviderDetectionStatus.Detected, ProviderDataStatus.Present),
            new("codex", "Codex",
                [
                    ProviderCapability.Limits,
                    ProviderCapability.LocalUsage,
                    ProviderCapability.Spend,
                ],
                ProviderDetectionStatus.Detected, ProviderDataStatus.Present),
            new("copilot", "GitHub Copilot",
                [ProviderCapability.Usage, ProviderCapability.LocalUsage, ProviderCapability.Spend],
                ProviderDetectionStatus.Detected, ProviderDataStatus.Present),
            new("cursor", "Cursor",
                [ProviderCapability.LocalUsage, ProviderCapability.Spend],
                ProviderDetectionStatus.Detected, ProviderDataStatus.Absent),
            new("goose", "Goose",
                [ProviderCapability.LocalUsage, ProviderCapability.Spend],
                ProviderDetectionStatus.Missing, ProviderDataStatus.Absent),
            new("hermes", "Hermes",
                [ProviderCapability.LocalUsage, ProviderCapability.Spend],
                ProviderDetectionStatus.Detected, ProviderDataStatus.Absent),
            new("mux", "Mux",
                [ProviderCapability.LocalUsage, ProviderCapability.Spend],
                ProviderDetectionStatus.Missing, ProviderDataStatus.Absent),
            new("zcode", "ZCode",
                [ProviderCapability.LocalUsage, ProviderCapability.Spend],
                ProviderDetectionStatus.Detected, ProviderDataStatus.Absent),
        ],
        [
            new("usage-db", DoctorCheckStatus.Present),
            new("local-usage-amp", DoctorCheckStatus.Present),
            new("local-usage-antigravity", DoctorCheckStatus.Present),
            new("local-usage-claude", DoctorCheckStatus.Absent),
            new("local-usage-copilot", DoctorCheckStatus.Present),
            new("local-usage-cursor", DoctorCheckStatus.Absent),
            new("local-usage-goose", DoctorCheckStatus.Absent),
            new("local-usage-opencode", DoctorCheckStatus.Unreadable),
            new("local-usage-grok", DoctorCheckStatus.Present),
            new("local-usage-hermes", DoctorCheckStatus.Absent),
            new("local-usage-mux", DoctorCheckStatus.Absent),
            new("local-usage-zcode", DoctorCheckStatus.Absent),
            new("codex-cli", DoctorCheckStatus.Detected),
            new("codex-cache", DoctorCheckStatus.Present),
        ]);

    private static string GoldenPath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Golden", fileName);

    private static string Normalize(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _originalCulture = CultureInfo.CurrentCulture;
        private readonly CultureInfo _originalUiCulture = CultureInfo.CurrentUICulture;

        public CultureScope(string cultureName)
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _originalCulture;
            CultureInfo.CurrentUICulture = _originalUiCulture;
        }
    }
}

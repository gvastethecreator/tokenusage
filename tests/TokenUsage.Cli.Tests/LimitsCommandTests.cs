using System.Globalization;
using System.Text.Json;
using TokenUsage.Cli;
using TokenUsage.Core.Providers;

namespace TokenUsage.Cli.Tests;

public sealed class LimitsCommandTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 22, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task JsonMatchesVersionedGoldenContract()
    {
        var output = new StringWriter(CultureInfo.InvariantCulture);
        var error = new StringWriter(CultureInfo.InvariantCulture);

        int exitCode = await LimitsCommand.RunAsync(
            ["--format", "json"],
            output,
            error,
            (_, _, _) => Task.FromResult<IReadOnlyList<ProviderSnapshot>>(
                [CreateCodexSnapshot(), CreateClaudeSnapshot()]),
            new FixedTimeProvider(Now));

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        string goldenPath = Path.Combine(
            AppContext.BaseDirectory,
            "Golden",
            "tokenusage.limits.v1.json");
        Assert.Equal(
            NormalizeNewlines(await File.ReadAllTextAsync(goldenPath)),
            NormalizeNewlines(output.ToString()));
    }

    [Fact]
    public async Task ProviderFilterReturnsOnlyExactProvider()
    {
        var output = new StringWriter(CultureInfo.InvariantCulture);

        int exitCode = await LimitsCommand.RunAsync(
            ["codex", "--format", "json"],
            output,
            TextWriter.Null,
            (_, _, _) => Task.FromResult<IReadOnlyList<ProviderSnapshot>>(
                [CreateClaudeSnapshot(), CreateCodexSnapshot()]),
            new FixedTimeProvider(Now));

        Assert.Equal(0, exitCode);
        using JsonDocument document = JsonDocument.Parse(output.ToString());
        JsonElement provider = Assert.Single(document.RootElement
            .GetProperty("providers")
            .EnumerateArray());
        Assert.Equal("codex", provider.GetProperty("id").GetString());
        Assert.False(provider.GetProperty("stale").GetBoolean());
        Assert.False(document.RootElement.GetProperty("stale").GetBoolean());
    }

    [Fact]
    public async Task StaleUsesTheSameSecondPublishedAsGeneratedAt()
    {
        ProviderSnapshot boundary = CreateSnapshot(
            "codex",
            "Codex",
            null,
            [
                new ScalarMetricSnapshot(
                    new MetricId("tokens"),
                    1m,
                    "tokens",
                    new DataProvenance(
                        SourceKind.LocalLog,
                        MeasurementKind.Measured,
                        "test/1")),
            ],
            observedAt: Now.AddMinutes(-10));
        var output = new StringWriter(CultureInfo.InvariantCulture);

        int exitCode = await LimitsCommand.RunAsync(
            ["--format", "json"],
            output,
            TextWriter.Null,
            (_, _, _) => Task.FromResult<IReadOnlyList<ProviderSnapshot>>([boundary]),
            new FixedTimeProvider(Now.AddMilliseconds(500)));

        Assert.Equal(0, exitCode);
        using JsonDocument document = JsonDocument.Parse(output.ToString());
        Assert.Equal(
            Now,
            document.RootElement.GetProperty("generatedAt").GetDateTimeOffset());
        Assert.False(document.RootElement.GetProperty("stale").GetBoolean());
        Assert.False(Assert.Single(document.RootElement
            .GetProperty("providers")
            .EnumerateArray())
            .GetProperty("stale")
            .GetBoolean());
    }

    [Fact]
    public async Task MissingProviderReturnsNoDataWithValidJson()
    {
        var output = new StringWriter(CultureInfo.InvariantCulture);

        int exitCode = await LimitsCommand.RunAsync(
            ["grok", "--format", "json"],
            output,
            TextWriter.Null,
            (_, _, _) => Task.FromResult<IReadOnlyList<ProviderSnapshot>>([CreateCodexSnapshot()]),
            new FixedTimeProvider(Now));

        Assert.Equal(4, exitCode);
        using JsonDocument document = JsonDocument.Parse(output.ToString());
        Assert.Empty(document.RootElement.GetProperty("providers").EnumerateArray());
        Assert.False(document.RootElement.GetProperty("stale").GetBoolean());
    }

    [Fact]
    public async Task ProviderWithoutMetricsReturnsNoUsefulData()
    {
        ProviderSnapshot empty = CreateSnapshot(
            "codex",
            "Codex",
            "Plus",
            []);
        var output = new StringWriter(CultureInfo.InvariantCulture);

        int exitCode = await LimitsCommand.RunAsync(
            ["--format", "json"],
            output,
            TextWriter.Null,
            (_, _, _) => Task.FromResult<IReadOnlyList<ProviderSnapshot>>([empty]),
            new FixedTimeProvider(Now));

        Assert.Equal(4, exitCode);
        using JsonDocument document = JsonDocument.Parse(output.ToString());
        Assert.Empty(Assert.Single(document.RootElement
            .GetProperty("providers")
            .EnumerateArray())
            .GetProperty("metrics")
            .EnumerateArray());
    }

    [Fact]
    public async Task HumanOutputUsesInvariantFormattingInSpanishCulture()
    {
        using var culture = new CultureScope("es-ES");
        var output = new StringWriter(CultureInfo.CurrentCulture);

        int exitCode = await LimitsCommand.RunAsync(
            [],
            output,
            TextWriter.Null,
            (_, _, _) => Task.FromResult<IReadOnlyList<ProviderSnapshot>>([CreateCodexSnapshot()]),
            new FixedTimeProvider(Now));

        Assert.Equal(0, exitCode);
        Assert.Equal(
            "Codex (Plus)\n"
            + "  Provider: codex\n"
            + "  Status: fresh\n"
            + "  session: 80% remaining; resets 2026-07-22T18:00:00Z\n"
            + "  spend-usd: 12.34 USD\n",
            NormalizeNewlines(output.ToString()));
    }

    [Fact]
    public async Task HumanOutputNeutralizesTerminalControlAndFormatCharacters()
    {
        ProviderSnapshot hostile = CreateSnapshot(
            "codex",
            "Codex\r\nFAKE\u001b[31m",
            "Plus\u202Ehidden",
            [
                new ScalarMetricSnapshot(
                    new MetricId("spend-usd"),
                    1m,
                    "USD\nINJECT\u001b",
                    new DataProvenance(
                        SourceKind.LocalLog,
                        MeasurementKind.Measured,
                        "test/1")),
            ]);
        var output = new StringWriter(CultureInfo.InvariantCulture);

        int exitCode = await LimitsCommand.RunAsync(
            [],
            output,
            TextWriter.Null,
            (_, _, _) => Task.FromResult<IReadOnlyList<ProviderSnapshot>>([hostile]),
            new FixedTimeProvider(Now));

        Assert.Equal(0, exitCode);
        string text = NormalizeNewlines(output.ToString());
        Assert.DoesNotContain('\r', text);
        Assert.DoesNotContain('\u001b', text);
        Assert.DoesNotContain('\u202E', text);
        Assert.DoesNotContain("\nFAKE", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\nINJECT", text, StringComparison.Ordinal);
        Assert.Contains("Codex��FAKE�[31m (Plus�hidden)", text, StringComparison.Ordinal);
        Assert.Contains("USD�INJECT�", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ForceFlagIsPassedToReader()
    {
        string? receivedProviderId = null;
        bool? receivedForce = null;

        int exitCode = await LimitsCommand.RunAsync(
            ["codex", "--force"],
            TextWriter.Null,
            TextWriter.Null,
            (providerId, force, _) =>
            {
                receivedProviderId = providerId;
                receivedForce = force;
                return Task.FromResult<IReadOnlyList<ProviderSnapshot>>([CreateCodexSnapshot()]);
            },
            new FixedTimeProvider(Now));

        Assert.Equal(0, exitCode);
        Assert.Equal("codex", receivedProviderId);
        Assert.True(receivedForce);
    }

    [Theory]
    [InlineData("--force|--force")]
    [InlineData("--format")]
    [InlineData("--format|xml")]
    [InlineData("--format|json|--format|human")]
    [InlineData("codex|claude")]
    [InlineData("Codex")]
    [InlineData("--unknown")]
    [InlineData("--token=customer-secret")]
    public async Task InvalidArgumentsReturnRedactedUsageError(string argumentLine)
    {
        string[] arguments = argumentLine.Split('|');
        var output = new StringWriter(CultureInfo.InvariantCulture);
        var error = new StringWriter(CultureInfo.InvariantCulture);
        bool readerCalled = false;

        int exitCode = await LimitsCommand.RunAsync(
            arguments,
            output,
            error,
            (_, _, _) =>
            {
                readerCalled = true;
                return Task.FromResult<IReadOnlyList<ProviderSnapshot>>([CreateCodexSnapshot()]);
            },
            new FixedTimeProvider(Now));

        Assert.Equal(2, exitCode);
        Assert.False(readerCalled);
        Assert.Equal(string.Empty, output.ToString());
        Assert.EndsWith(LimitsCommand.UsageText + Environment.NewLine, error.ToString());
        Assert.DoesNotContain("customer-secret", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReaderFailureIsRedacted()
    {
        var error = new StringWriter(CultureInfo.InvariantCulture);

        int exitCode = await LimitsCommand.RunAsync(
            [],
            TextWriter.Null,
            error,
            (_, _, _) => throw new IOException("C:\\Users\\secret\\snapshots.v1.json"),
            new FixedTimeProvider(Now));

        Assert.Equal(4, exitCode);
        Assert.Equal("Unable to read provider limits." + Environment.NewLine, error.ToString());
        Assert.DoesNotContain("secret", error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CallerCancellationPropagates()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => LimitsCommand.RunAsync(
            [],
            TextWriter.Null,
            TextWriter.Null,
            (_, _, token) => Task.FromCanceled<IReadOnlyList<ProviderSnapshot>>(token),
            new FixedTimeProvider(Now),
            cancellation.Token));
    }

    internal static ProviderSnapshot CreateCodexSnapshot() =>
        CreateSnapshot(
            "codex",
            "Codex",
            "Plus",
            [
                new ScalarMetricSnapshot(
                    new MetricId("spend-usd"),
                    12.34m,
                    "USD",
                    new DataProvenance(
                        SourceKind.LocalLog,
                        MeasurementKind.Estimated,
                        "test/1")),
                new ProgressMetricSnapshot(
                    new MetricId("session"),
                    20m,
                    100m,
                    Now.AddHours(3),
                    new DataProvenance(
                        SourceKind.OfficialLocalApi,
                        MeasurementKind.ProviderReported,
                        "test/1")),
            ]);

    internal static ProviderSnapshot CreateClaudeSnapshot() =>
        CreateSnapshot(
            "claude",
            "Claude",
            null,
            [
                new ScalarMetricSnapshot(
                    new MetricId("tokens"),
                    53_080m,
                    "tokens",
                    new DataProvenance(
                        SourceKind.LocalLog,
                        MeasurementKind.Measured,
                        "test/1")),
            ],
            observedAt: Now.AddMinutes(-20),
            coverage: CoverageKind.Partial);

    private static ProviderSnapshot CreateSnapshot(
        string providerId,
        string displayName,
        string? planLabel,
        IReadOnlyList<MetricSnapshot> metrics,
        DateTimeOffset? observedAt = null,
        CoverageKind coverage = CoverageKind.Complete) =>
        new(
            new ProviderId(providerId),
            displayName,
            planLabel,
            Now,
            observedAt ?? Now.AddMinutes(-5),
            "UTC",
            metrics,
            coverage,
            1);

    private static string NormalizeNewlines(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal);

    internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _originalCulture = CultureInfo.CurrentCulture;
        private readonly CultureInfo _originalUiCulture = CultureInfo.CurrentUICulture;

        public CultureScope(string cultureName)
        {
            CultureInfo culture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _originalCulture;
            CultureInfo.CurrentUICulture = _originalUiCulture;
        }
    }
}

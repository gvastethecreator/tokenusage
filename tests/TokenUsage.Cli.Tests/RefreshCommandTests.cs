using System.Globalization;
using System.Text.Json;
using TokenUsage.Cli;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;

namespace TokenUsage.Cli.Tests;

public sealed class RefreshCommandTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 8, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HumanOutputShowsEveryProviderOutcome()
    {
        var output = new StringWriter(CultureInfo.InvariantCulture);

        int exitCode = await RefreshCommand.RunAsync(
            [],
            output,
            TextWriter.Null,
            _ => Task.FromResult(CreateResult()),
            new FixedTimeProvider(Now));

        Assert.Equal(0, exitCode);
        string text = output.ToString();
        Assert.Contains("Local usage refresh: partial", text, StringComparison.Ordinal);
        Assert.Contains("codex: complete (none)", text, StringComparison.Ordinal);
        Assert.Contains("antigravity: partial (partial-scan)", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task JsonOutputUsesTheVersionedSanitizedContract()
    {
        var output = new StringWriter(CultureInfo.InvariantCulture);

        int exitCode = await RefreshCommand.RunAsync(
            ["--format", "json"],
            output,
            TextWriter.Null,
            _ => Task.FromResult(CreateResult()),
            new FixedTimeProvider(Now));

        Assert.Equal(0, exitCode);
        using JsonDocument document = JsonDocument.Parse(output.ToString());
        Assert.Equal(
            RefreshCommand.SchemaVersion,
            document.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal(2, document.RootElement.GetProperty("providers").GetArrayLength());
        Assert.DoesNotContain("path", output.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("--format")]
    [InlineData("--format json --format human")]
    [InlineData("customer-secret")]
    public async Task InvalidArgumentsDoNotRunTheRefresh(string argumentLine)
    {
        var error = new StringWriter(CultureInfo.InvariantCulture);
        bool called = false;

        int exitCode = await RefreshCommand.RunAsync(
            argumentLine.Split(' ', StringSplitOptions.RemoveEmptyEntries),
            TextWriter.Null,
            error,
            _ =>
            {
                called = true;
                return Task.FromResult(CreateResult());
            },
            new FixedTimeProvider(Now));

        Assert.Equal(2, exitCode);
        Assert.False(called);
        Assert.EndsWith(RefreshCommand.UsageText + Environment.NewLine, error.ToString());
        Assert.DoesNotContain("customer-secret", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefreshFailureIsRedacted()
    {
        var error = new StringWriter(CultureInfo.InvariantCulture);

        int exitCode = await RefreshCommand.RunAsync(
            [],
            TextWriter.Null,
            error,
            _ => throw new IOException("private path and content"),
            new FixedTimeProvider(Now));

        Assert.Equal(4, exitCode);
        Assert.Equal(
            "Unable to refresh local usage data." + Environment.NewLine,
            error.ToString());
    }

    private static LocalUsageRefreshResult CreateResult() => new(
        [
            new DailyUsageRollup(
                new DateOnly(2026, 8, 8),
                "UTC",
                new AgentId("codex"),
                new ModelProviderId("openai"),
                new ModelId("gpt-5.5"),
                new TokenBreakdown(10, 2, 0, 0, 0),
                null,
                0.01m,
                0,
                0,
                1,
                CoverageKind.Partial),
        ],
        new DateOnly(2026, 8, 1),
        new DateOnly(2026, 8, 8),
        SourceKind.LocalLog,
        UsageSourceReadStatus.Partial,
        [
            new UsageSourceDiagnostic(
                new AgentId("codex"),
                UsageSourceReadStatus.Complete,
                UsageSourceIssueKind.None,
                true),
            new UsageSourceDiagnostic(
                new AgentId("antigravity"),
                UsageSourceReadStatus.Partial,
                UsageSourceIssueKind.PartialScan,
                true),
        ],
        hasMultipleRealSources: true);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

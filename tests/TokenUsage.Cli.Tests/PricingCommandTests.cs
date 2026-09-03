using System.Globalization;

namespace TokenUsage.Cli.Tests;

public sealed class PricingCommandTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 3, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AuditListsOfficialSourcesAndUpcomingPromotion()
    {
        var output = new StringWriter(CultureInfo.InvariantCulture);

        int exitCode = await PricingCommand.RunAsync(
            ["audit", "--format", "json"],
            output,
            TextWriter.Null,
            new FixedTimeProvider(Now));

        Assert.Equal(0, exitCode);
        Assert.Contains("\"schemaVersion\": \"tokenusage.pricing-audit.v1\"", output.ToString());
        Assert.Contains("\"sourceId\": \"zai-model-pricing\"", output.ToString());
        Assert.Contains("\"kind\": \"promotionNearExpiry\"", output.ToString());
        Assert.DoesNotContain("ExpiredPromotionWithoutSuccessor", output.ToString());
    }

    [Theory]
    [InlineData()]
    [InlineData("refresh")]
    [InlineData("audit", "--format", "xml")]
    public async Task InvalidArgumentsReturnTwo(params string[] arguments)
    {
        var error = new StringWriter(CultureInfo.InvariantCulture);

        int exitCode = await PricingCommand.RunAsync(
            arguments,
            TextWriter.Null,
            error,
            new FixedTimeProvider(Now));

        Assert.Equal(2, exitCode);
        Assert.Contains(PricingCommand.UsageText, error.ToString());
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

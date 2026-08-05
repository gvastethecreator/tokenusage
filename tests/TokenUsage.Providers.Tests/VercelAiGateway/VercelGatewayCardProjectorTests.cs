using TokenUsage.App.ViewModels;
using TokenUsage.App.ViewModels.Dashboard;
using TokenUsage.App.ViewModels.Sample;
using TokenUsage.Core.Providers;

namespace TokenUsage.Providers.Tests.VercelAiGateway;

public sealed class VercelGatewayCardProjectorTests
{
    [Fact]
    public void PositiveAccountSpendCreatesALiveDashboardSlice()
    {
        SpendSlice slice = Assert.IsType<SpendSlice>(
            VercelGatewayCardProjector.CreateSpendSlice(Snapshot(12.34m), Strings));

        Assert.Equal("vercel-ai-gateway", slice.ProviderId);
        Assert.Equal("Vercel AI Gateway", slice.ProviderName);
        Assert.Equal(12.34d, slice.Amount);
        Assert.Equal(
            string.Format(System.Globalization.CultureInfo.CurrentCulture, "${0:0.00} USD", 12.34m),
            slice.AmountText);
        Assert.Equal(
            string.Format(System.Globalization.CultureInfo.CurrentCulture, "${0:0.00}", 12.34m),
            slice.CompactAmountText);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0d)]
    public void MissingOrZeroAccountSpendDoesNotCreateAnEmptySlice(double? amount)
    {
        ProviderSnapshot snapshot = amount is null
            ? Snapshot()
            : Snapshot((decimal)amount.Value);

        Assert.Null(VercelGatewayCardProjector.CreateSpendSlice(snapshot, Strings));
    }

    private static ProviderSnapshot Snapshot(params decimal[] spend) => new(
        new ProviderId("vercel-ai-gateway"),
        "Vercel AI Gateway",
        planLabel: null,
        new DateTimeOffset(2026, 7, 23, 12, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 7, 23, 12, 0, 0, TimeSpan.Zero),
        "UTC",
        spend.Select(value => (MetricSnapshot)new ScalarMetricSnapshot(
            new MetricId("spend.gateway.total.30d"),
            value,
            "USD",
            new DataProvenance(SourceKind.OfficialRemoteApi, MeasurementKind.ProviderReported, "test")))
            .ToArray(),
        CoverageKind.Complete,
        1);

    private static string Strings(string key) => key switch
    {
        "LocalUsageUsdFormat" => "${0:0.00} USD",
        "LocalUsageUsdCompactFormat" => "${0:0.00}",
        _ => key,
    };
}

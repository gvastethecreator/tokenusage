using System.Globalization;
using WOpenUsage.Cli;
using WOpenUsage.Core.Usage;
using WOpenUsage.Providers.Fakes;

namespace WOpenUsage.Cli.Tests;

public sealed class LocalUsageCliAccessTests
{
    [Fact]
    public async Task MissingDatabaseFailsWithoutCreatingIt()
    {
        string databasePath = Path.Combine(
            Path.GetTempPath(),
            "wopenusage-cli-tests",
            Guid.NewGuid().ToString("N"),
            "usage.v1.db");

        await Assert.ThrowsAsync<FileNotFoundException>(() => LocalUsageCliAccess.ReadAsync(
            databasePath,
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 23)));

        Assert.False(File.Exists(databasePath));
        Assert.False(Directory.Exists(Path.GetDirectoryName(databasePath)!));
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("es-ES")]
    public async Task CliReadsTheSameSeparatedRollupsAsTheAppInEitherCulture(string cultureName)
    {
        using var culture = new CultureScope(cultureName);
        string directory = Path.Combine(
            Path.GetTempPath(),
            "wopenusage-cli-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string databasePath = Path.Combine(directory, "usage.v1.db");
            var clock = new FixedTimeProvider(
                new DateTimeOffset(2026, 7, 22, 15, 0, 0, TimeSpan.Zero));
            var source = new SyntheticUsageEventSource(clock, "Argentina Standard Time");
            UsageRepository appRepository = await UsageRepository.OpenAsync(databasePath);
            await appRepository.IngestAsync((await source.ReadAsync()).Events);

            UsageCliSummary summary = await LocalUsageCliAccess.ReadAsync(
                databasePath,
                new DateOnly(2026, 6, 23),
                new DateOnly(2026, 7, 22));

            Assert.Equal(3, summary.EventCount);
            Assert.Equal(1.84m, summary.ReportedCostUsd);
            Assert.Equal(0.62m, summary.EstimatedCostUsd);
            Assert.Equal(9_460, summary.UnpricedTokens);
            Assert.Equal(53_080, summary.TotalTokens);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

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

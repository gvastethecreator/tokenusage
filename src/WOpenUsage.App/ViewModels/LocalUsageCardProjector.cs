using System.Globalization;
using WOpenUsage.App.ViewModels.Sample;
using WOpenUsage.Core.Providers;
using WOpenUsage.Core.Usage;

namespace WOpenUsage.App.ViewModels;

public static class LocalUsageCardProjector
{
    public static LocalUsageCard Create(
        IReadOnlyList<DailyUsageRollup> rollups,
        Func<string, string> getString,
        SourceKind sourceKind = SourceKind.Synthetic,
        UsageSourceReadStatus readStatus = UsageSourceReadStatus.Complete)
    {
        ArgumentNullException.ThrowIfNull(rollups);
        ArgumentNullException.ThrowIfNull(getString);

        bool hasReported = false;
        bool hasEstimated = false;
        decimal reported = 0m;
        decimal estimated = 0m;
        long totalTokens = 0;
        long unpricedTokens = 0;

        checked
        {
            foreach (DailyUsageRollup rollup in rollups)
            {
                totalTokens += rollup.Tokens.Total;
                unpricedTokens += rollup.UnpricedTokens;
                if (rollup.ReportedCostUsd is decimal reportedValue)
                {
                    reported += reportedValue;
                    hasReported = true;
                }

                if (rollup.EstimatedCostUsd is decimal estimatedValue)
                {
                    estimated += estimatedValue;
                    hasEstimated = true;
                }
            }
        }

        string missing = getString("CodexUsageMissing");
        string coverage = totalTokens == 0
            ? missing
            : string.Format(
                CultureInfo.CurrentCulture,
                "{0:0.#}%",
                (totalTokens - unpricedTokens) * 100m / totalTokens);
        SampleMetric[] metrics =
        [
            new(
                getString("LocalUsageReportedCost"),
                hasReported ? FormatUsd(reported, getString) : missing,
                "UsageProductCard.ReportedCost"),
            new(
                getString("LocalUsageEstimatedCost"),
                hasEstimated ? FormatUsd(estimated, getString) : missing,
                "UsageProductCard.EstimatedCost"),
            new(
                getString("LocalUsageUnpricedTokens"),
                totalTokens == 0 ? missing : FormatCount(unpricedTokens),
                "UsageProductCard.UnpricedUsage"),
            new(
                getString("LocalUsageTotalTokens"),
                totalTokens == 0 ? missing : FormatCount(totalTokens),
                "UsageProductCard.TotalTokens"),
            new(
                getString("LocalUsageCoverage"),
                coverage,
                "UsageProductCard.CostCoverage"),
        ];

        return new LocalUsageCard(
            getString("LocalUsageTitle"),
            getString(sourceKind == SourceKind.LocalLog
                ? "LocalUsageSourceClaude"
                : "LocalUsageSourceSynthetic"),
            getString("LocalUsagePeriod30Days"),
            getString(sourceKind == SourceKind.LocalLog
                ? readStatus == UsageSourceReadStatus.Partial
                    ? "LocalUsageClaudePartialNotice"
                    : readStatus == UsageSourceReadStatus.NoData
                        ? "LocalUsageClaudeNoDataNotice"
                        : "LocalUsageClaudeNotice"
                : "LocalUsageNotice"),
            metrics);
    }

    public static LocalUsageCard CreateUnavailable(
        Func<string, string> getString,
        SourceKind sourceKind = SourceKind.Synthetic) =>
        Create([], getString, sourceKind) with
        {
            NoticeText = getString("LocalUsageUnavailable"),
        };

    private static string FormatUsd(decimal amount, Func<string, string> getString) =>
        string.Format(
            CultureInfo.CurrentCulture,
            getString("LocalUsageUsdFormat"),
            amount);

    private static string FormatCount(long value) =>
        value.ToString("N0", CultureInfo.CurrentCulture);
}

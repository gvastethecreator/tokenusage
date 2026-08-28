using System.Globalization;
using TokenUsage.App.Localization;
using TokenUsage.App.ViewModels.Dashboard;
using TokenUsage.App.ViewModels.Reports;
using TokenUsage.Core.Layout;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;

namespace TokenUsage.App.ViewModels;

public sealed record CompactDashboardProjection(
    IReadOnlyList<DashboardProviderSummary> ProviderSummaries,
    IReadOnlyList<DashboardProviderOption> ProviderOptions,
    IReadOnlyList<SpendSlice> GlobalSpendSlices,
    UsageHeatmapModel GlobalHeatmap,
    IReadOnlyList<DashboardActivitySummary> GlobalActivity,
    IReadOnlyList<QuotaWindow> GlobalProviderLimits,
    string GlobalCostText,
    string GlobalDonutCenterText,
    string GlobalFooterText,
    string GlobalTokensText,
    string? GlobalCostBreakdownText,
    string? SelectedProviderId,
    UsageHeatmapModel SelectedProviderHeatmap,
    UsageReportTrendDataset SelectedProviderTrend,
    IReadOnlyList<QuotaWindow> SelectedProviderLimits);

public static class CompactDashboardProjector
{
    public static CompactDashboardProjection Create(
        DateOnly today,
        IReadOnlyList<DailyUsageRollup> rollups,
        IReadOnlyList<string> detectedProviderIds,
        bool isSampleMode,
        DashboardSnapshot? activeSample,
        LocalUsageCard localUsage,
        string? selectedProviderId,
        Func<string, string> getString,
        Func<string, IReadOnlyList<QuotaWindow>> getProviderLimits)
    {
        ArgumentNullException.ThrowIfNull(rollups);
        ArgumentNullException.ThrowIfNull(detectedProviderIds);
        ArgumentNullException.ThrowIfNull(localUsage);
        ArgumentNullException.ThrowIfNull(getString);
        ArgumentNullException.ThrowIfNull(getProviderLimits);

        var limitsByProvider = new Dictionary<string, IReadOnlyList<QuotaWindow>>(
            StringComparer.Ordinal);
        IReadOnlyList<QuotaWindow> GetLimitsOnce(string providerId)
        {
            if (!limitsByProvider.TryGetValue(providerId, out IReadOnlyList<QuotaWindow>? limits))
            {
                limits = getProviderLimits(providerId);
                limitsByProvider.Add(providerId, limits);
            }

            return limits;
        }

        DateOnly from = UsagePeriodPolicy.RollingDisplayStart(today);
        DailyUsageRollup[] windowed = rollups
            .Where(rollup => rollup.Date >= from && rollup.Date <= today)
            .ToArray();
        DashboardProviderSummary[] summaries = isSampleMode && windowed.Length == 0
            ? CreateFallbackProviderSummaries(activeSample, getString)
            : CreateProviderSummaries(windowed, detectedProviderIds, getString);
        string? nextSelectedId = summaries.Any(summary => string.Equals(
            summary.ProviderId,
            selectedProviderId,
            StringComparison.Ordinal))
                ? selectedProviderId
                : summaries.FirstOrDefault(summary => string.Equals(
                    summary.ProviderId,
                    "codex",
                    StringComparison.Ordinal))?.ProviderId
                    ?? summaries.FirstOrDefault()?.ProviderId;
        DashboardProviderOption[] options = summaries
            .Select(summary => new DashboardProviderOption(
                summary.ProviderId,
                summary.Name,
                string.Equals(
                    summary.ProviderId,
                    nextSelectedId,
                    StringComparison.Ordinal)))
            .ToArray();
        SpendSlice[] spendSlices = summaries
            .Where(summary => summary.CostUsd > 0m)
            .Select(summary => new SpendSlice(
                summary.ProviderId,
                summary.Name,
                decimal.ToDouble(summary.CostUsd),
                summary.CostText,
                summary.ColorHex,
                summary.CostText))
            .ToArray();
        decimal totalCost = summaries.Sum(summary => summary.CostUsd);
        long totalTokens = summaries.Sum(summary => summary.TotalTokens);
        string globalCostText = summaries.Length == 0 && activeSample is not null
            ? activeSample.TotalSpendAmount
            : UsageValueFormatter.Usd(totalCost, getString);
        // The headline blends provider-reported dollars with catalog estimates;
        // the split keeps subscription costs readable apart from API-list value.
        string? globalCostBreakdownText = null;
        if (windowed.Length > 0 && totalCost > 0m && activeSample is null)
        {
            decimal reportedTotal = windowed.Sum(rollup => rollup.ReportedCostUsd ?? 0m);
            decimal estimatedTotal = windowed.Sum(rollup => rollup.EstimatedCostUsd ?? 0m);
            globalCostBreakdownText = string.Format(
                CultureInfo.CurrentCulture,
                getString("CompactGlobalCostBreakdownFormat"),
                UsageValueFormatter.Usd(reportedTotal, getString),
                UsageValueFormatter.Usd(estimatedTotal, getString));
        }

        string globalTokensText = totalTokens == 0
            ? localUsage.TotalTokensMetric.Value
            : UsageValueFormatter.CompactTokens(totalTokens);
        UsageHeatmapModel heatmap = windowed.Length == 0
            ? localUsage.Heatmap
            : UsageHeatmapProjector.Create(
                windowed,
                today,
                getString,
                "CompactUsageHeatmap");
        CompactSelectedProviderProjection selected = CreateSelectedProvider(
            nextSelectedId,
            rollups,
            today,
            getString,
            GetLimitsOnce);
        return new CompactDashboardProjection(
            summaries,
            options,
            spendSlices,
            heatmap,
            CreateActivitySummaries(windowed, today, getString),
            CreateGlobalLimits(GetLimitsOnce, getString),
            globalCostText,
            globalCostText.Replace(" USD", "\nUSD", StringComparison.Ordinal),
            string.Format(
                CultureInfo.CurrentCulture,
                getString("CompactGlobalFooterFormat"),
                globalCostText,
                summaries.Length),
            globalTokensText,
            globalCostBreakdownText,
            nextSelectedId,
            selected.Heatmap,
            selected.Trend,
            selected.Limits);
    }

    /// <summary>
    /// The global limits strip shows every provider that publishes quota
    /// windows. ZCode titles carry the provider name because Codex window
    /// titles do not.
    /// </summary>
    private static IReadOnlyList<QuotaWindow> CreateGlobalLimits(
        Func<string, IReadOnlyList<QuotaWindow>> getProviderLimits,
        Func<string, string> getString)
    {
        IReadOnlyList<QuotaWindow> codex = getProviderLimits("codex");
        IReadOnlyList<QuotaWindow> zcode = getProviderLimits("zcode");
        if (zcode.Count == 0)
        {
            return codex;
        }

        string prefix = getString("ZcodeGlobalLimitTitlePrefix");
        return codex
            .Concat(zcode.Select(window => window with
            {
                Title = prefix + window.Title,
                AutomationName = prefix + window.AutomationName,
            }))
            .ToArray();
    }

    public static CompactSelectedProviderProjection CreateSelectedProvider(
        string? providerId,
        IReadOnlyList<DailyUsageRollup> rollups,
        DateOnly today,
        Func<string, string> getString,
        Func<string, IReadOnlyList<QuotaWindow>> getProviderLimits)
    {
        ArgumentNullException.ThrowIfNull(rollups);
        ArgumentNullException.ThrowIfNull(getString);
        ArgumentNullException.ThrowIfNull(getProviderLimits);
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return CompactSelectedProviderProjection.Empty;
        }

        DailyUsageRollup[] providerRollups = rollups
            .Where(rollup => string.Equals(
                rollup.AgentId.Value,
                providerId,
                StringComparison.Ordinal))
            .ToArray();
        UsageReportTrendDay[] days = Enumerable.Range(0, 30)
            .Select(offset => today.AddDays(offset - 29))
            .Select(date => new UsageReportTrendDay(
                date,
                date.ToString("d MMM", CultureInfo.CurrentCulture)))
            .ToArray();
        var dailyTokens = providerRollups
            .GroupBy(rollup => rollup.Date)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Tokens.Total));
        return new CompactSelectedProviderProjection(
            providerRollups.Length == 0
                ? UsageHeatmapModel.Empty
                : UsageHeatmapProjector.Create(
                    providerRollups,
                    today,
                    getString,
                    $"ProviderUsageHeatmap.{providerId}"),
            providerRollups.Length == 0
                ? UsageReportTrendDataset.Empty
                : new UsageReportTrendDataset(
                    UsageReportMetric.Tokens,
                    days,
                    [
                        new UsageReportTrendSeries(
                            providerId,
                            ProviderDisplayName.Resolve(providerId, getString),
                            ProviderColorPreference.Resolve(providerId, customColorHex: null),
                            days.Select(day => (double)dailyTokens.GetValueOrDefault(day.Date, 0))
                                .ToArray()),
                    ]),
            getProviderLimits(providerId));
    }

    private static DashboardProviderSummary[] CreateProviderSummaries(
        IReadOnlyList<DailyUsageRollup> rollups,
        IReadOnlyList<string> providerIds,
        Func<string, string> getString)
    {
        var grouped = rollups
            .GroupBy(rollup => rollup.AgentId.Value, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        decimal totalCost = grouped.Values
            .SelectMany(items => items)
            .Sum(item => (item.ReportedCostUsd ?? 0m) + (item.EstimatedCostUsd ?? 0m));
        long totalTokens = grouped.Values.SelectMany(items => items).Sum(item => item.Tokens.Total);

        return providerIds
            .Select(providerId =>
            {
                DailyUsageRollup[] items = grouped.GetValueOrDefault(providerId) ?? [];
                bool hasData = items.Length > 0;
                bool hasCostData = items.Any(item =>
                    item.ReportedCostUsd is not null || item.EstimatedCostUsd is not null);
                bool isPartial = items.Any(item => item.Coverage is
                    CoverageKind.Partial or CoverageKind.SummaryOnly);
                bool hasUnpricedData = items.Any(item =>
                    item.UnpricedTokens > 0 || item.Coverage == CoverageKind.Unpriced);
                decimal cost = items.Sum(item =>
                    (item.ReportedCostUsd ?? 0m) + (item.EstimatedCostUsd ?? 0m));
                long tokens = items.Sum(item => item.Tokens.Total);
                double share = totalCost > 0
                    ? decimal.ToDouble(cost * 100m / totalCost)
                    : totalTokens > 0
                        ? (double)tokens * 100d / totalTokens
                        : 0d;
                string name = ProviderDisplayName.Resolve(providerId, getString);
                string costAccessibilityText = !hasData
                    ? getString("CodexUsageMissing")
                    : hasCostData
                        ? UsageValueFormatter.Usd(cost, getString)
                        : getString("CompactCostUnavailable");
                string costText = hasData && !hasCostData
                    ? "—"
                    : costAccessibilityText;
                string tokensText = hasData
                    ? UsageValueFormatter.CompactTokens(tokens)
                    : getString("CodexUsageMissing");
                string detailText = hasData
                    ? string.Format(CultureInfo.CurrentCulture, "{0:0.#}%", share)
                    : "—";
                return new DashboardProviderSummary(
                    providerId,
                    name,
                    cost,
                    tokens,
                    share,
                    costText,
                    tokensText,
                    detailText,
                    hasData
                        ? $"{name}: {costAccessibilityText}, {tokensText} tokens, {share:0.#}%"
                            + (hasUnpricedData
                                ? $". {getString("UsageReportCoverageUnpriced")}"
                                : string.Empty)
                        : $"{name}: {getString("ProviderStatusNoData")}",
                    ProviderColorPreference.Resolve(providerId, customColorHex: null),
                    $"CompactProvider.{providerId}",
                    share <= 0d ? 0d : Math.Max(2d, share * 4.36d),
                    hasData,
                    hasCostData,
                    isPartial,
                    hasUnpricedData);
            })
            .ToArray();
    }

    private static DashboardProviderSummary[] CreateFallbackProviderSummaries(
        DashboardSnapshot? activeSample,
        Func<string, string> getString)
    {
        ArgumentNullException.ThrowIfNull(getString);
        if (activeSample?.SpendSlices is not { Count: > 0 } slices)
        {
            return [];
        }

        double total = slices.Sum(slice => Math.Max(0, slice.Amount));
        return slices.Select(slice =>
        {
            double share = total <= 0 ? 0 : slice.Amount * 100 / total;
            decimal cost = Convert.ToDecimal(slice.Amount, CultureInfo.InvariantCulture);
            string costText = string.IsNullOrWhiteSpace(slice.LegendAmountText)
                ? UsageValueFormatter.Usd(cost, getString)
                : slice.LegendAmountText;
            return new DashboardProviderSummary(
                slice.ProviderId,
                slice.ProviderName,
                cost,
                0,
                share,
                costText,
                "—",
                string.Format(CultureInfo.CurrentCulture, "{0:0.#}%", share),
                $"{slice.ProviderName}: {costText}, {share:0.#}%",
                slice.ColorHex ?? ProviderColorPreference.Resolve(slice.ProviderId, customColorHex: null),
                $"CompactProvider.{slice.ProviderId}",
                Math.Max(2d, share * 4.36d));
        }).ToArray();
    }

    private static IReadOnlyList<DashboardActivitySummary> CreateActivitySummaries(
        IReadOnlyList<DailyUsageRollup> rollups,
        DateOnly today,
        Func<string, string> getString)
    {
        long SumSince(int days) => rollups
            .Where(item => item.Date >= today.AddDays(-(days - 1)) && item.Date <= today)
            .Sum(item => item.Tokens.Total);

        return
        [
            new(
                getString("CompactActivityToday"),
                UsageValueFormatter.CompactTokens(SumSince(1)),
                getString("CompactActivityTokens")),
            new(
                getString("CompactActivity7Days"),
                UsageValueFormatter.CompactTokens(SumSince(7)),
                getString("CompactActivityTokens")),
            new(
                getString("CompactActivity30Days"),
                UsageValueFormatter.CompactTokens(SumSince(30)),
                getString("CompactActivityTokens")),
        ];
    }
}

public sealed record CompactSelectedProviderProjection(
    UsageHeatmapModel Heatmap,
    UsageReportTrendDataset Trend,
    IReadOnlyList<QuotaWindow> Limits)
{
    public static CompactSelectedProviderProjection Empty { get; } = new(
        UsageHeatmapModel.Empty,
        UsageReportTrendDataset.Empty,
        []);
}

using System.Globalization;
using WOpenUsage.App.ViewModels.Dashboard;
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
        UsageSourceReadStatus readStatus = UsageSourceReadStatus.Complete,
        bool hasMultipleRealSources = false,
        DateOnly? today = null,
        IReadOnlyList<UsageSourceDiagnostic>? sourceDiagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(rollups);
        ArgumentNullException.ThrowIfNull(getString);

        DateOnly currentDate = today
            ?? (rollups.Count == 0
                ? DateOnly.FromDateTime(DateTime.Today)
                : rollups.Max(rollup => rollup.Date));
        DateOnly monthStart = new(currentDate.Year, currentDate.Month, 1);
        Totals totals30Days = Sum(rollups, currentDate.AddDays(-29), currentDate);
        string missing = getString("CodexUsageMissing");

        DashboardMetric[] metrics = CreateMetrics(totals30Days, missing, getString);
        LocalUsagePeriodRow[] otherPeriods =
        [
            CreatePeriod(
                "LocalUsagePeriodToday",
                "UsageProductCard.Period.Today",
                Sum(rollups, currentDate, currentDate),
                missing,
                getString),
            CreatePeriod(
                "LocalUsagePeriodYesterday",
                "UsageProductCard.Period.Yesterday",
                Sum(rollups, currentDate.AddDays(-1), currentDate.AddDays(-1)),
                missing,
                getString),
            CreatePeriod(
                "LocalUsagePeriod7Days",
                "UsageProductCard.Period.7Days",
                Sum(rollups, currentDate.AddDays(-6), currentDate),
                missing,
                getString),
            CreatePeriod(
                "LocalUsagePeriodMonth",
                "UsageProductCard.Period.Month",
                Sum(rollups, monthStart, currentDate),
                missing,
                getString),
        ];

        IReadOnlyList<DailyUsageRollup> rollups30Days = rollups
            .Where(rollup => rollup.Date >= currentDate.AddDays(-29)
                             && rollup.Date <= currentDate)
            .ToArray();
        LocalUsageSpendBreakdown breakdown = CreateBreakdown(
            rollups30Days,
            totals30Days,
            missing,
            getString);
        ProviderStatusRow[] providerStatuses = CreateProviderStatuses(
            rollups30Days,
            sourceDiagnostics ?? [],
            missing,
            getString);

        return new LocalUsageCard(
            getString("LocalUsageTitle"),
            getString(sourceKind == SourceKind.LocalLog
                ? hasMultipleRealSources
                    ? "LocalUsageSourceAgents"
                    : "LocalUsageSourceClaude"
                : "LocalUsageSourceSynthetic"),
            getString("LocalUsagePeriod30Days"),
            getString(sourceKind == SourceKind.LocalLog
                ? hasMultipleRealSources
                    ? readStatus == UsageSourceReadStatus.Partial
                        ? "LocalUsageAgentsPartialNotice"
                        : readStatus == UsageSourceReadStatus.NoData
                            ? "LocalUsageAgentsNoDataNotice"
                            : "LocalUsageAgentsNotice"
                    : readStatus == UsageSourceReadStatus.Partial
                        ? "LocalUsageClaudePartialNotice"
                        : readStatus == UsageSourceReadStatus.NoData
                            ? "LocalUsageClaudeNoDataNotice"
                            : "LocalUsageClaudeNotice"
                : "LocalUsageNotice"),
            metrics,
            otherPeriods,
            breakdown,
            providerStatuses,
            UsageHeatmapProjector.Create(rollups, currentDate, getString),
            IsNoticeImportant: sourceKind == SourceKind.LocalLog
                && readStatus != UsageSourceReadStatus.Complete);
    }

    public static LocalUsageCard CreateUnavailable(
        Func<string, string> getString,
        SourceKind sourceKind = SourceKind.Synthetic) =>
        Create([], getString, sourceKind) with
        {
            NoticeText = getString("LocalUsageUnavailable"),
            IsNoticeImportant = true,
        };

    private static DashboardMetric[] CreateMetrics(
        Totals totals,
        string missing,
        Func<string, string> getString) =>
    [
        new(
            getString("LocalUsageReportedCost"),
            totals.HasReported ? FormatUsd(totals.Reported, getString) : missing,
            "UsageProductCard.ReportedCost"),
        new(
            getString("LocalUsageEstimatedCost"),
            totals.HasEstimated ? FormatUsd(totals.Estimated, getString) : missing,
            "UsageProductCard.EstimatedCost"),
        new(
            getString("LocalUsageUnpricedTokens"),
            totals.TotalTokens == 0 ? missing : FormatCount(totals.UnpricedTokens),
            "UsageProductCard.UnpricedUsage"),
        new(
            getString("LocalUsageTotalTokens"),
            totals.TotalTokens == 0 ? missing : FormatCount(totals.TotalTokens),
            "UsageProductCard.TotalTokens"),
        new(
            getString("LocalUsageCoverage"),
            FormatCoverage(totals, missing),
            "UsageProductCard.CostCoverage"),
        new(
            getString("LocalUsageCostPerMillion"),
            FormatCostPerMillion(totals, missing, getString),
            "UsageProductCard.CostPerMillion"),
    ];

    private static LocalUsagePeriodRow CreatePeriod(
        string labelKey,
        string automationId,
        Totals totals,
        string missing,
        Func<string, string> getString)
    {
        string reported = totals.HasReported ? FormatUsd(totals.Reported, getString) : missing;
        string estimated = totals.HasEstimated ? FormatUsd(totals.Estimated, getString) : missing;
        string costText = totals.TotalTokens == 0
            ? missing
            : string.Format(
                CultureInfo.CurrentCulture,
                getString("LocalUsagePeriodCostFormat"),
                getString("LocalUsageReportedShort"),
                reported,
                getString("LocalUsageEstimatedShort"),
                estimated);
        string detailText = totals.TotalTokens == 0
            ? string.Empty
            : string.Format(
                CultureInfo.CurrentCulture,
                getString("LocalUsagePeriodDetailFormat"),
                FormatCount(totals.TotalTokens),
                FormatCoverage(totals, missing),
                FormatCostPerMillion(totals, missing, getString));
        string label = getString(labelKey);
        return new LocalUsagePeriodRow(
            label,
            costText,
            detailText,
            automationId,
            string.Format(
                CultureInfo.CurrentCulture,
                getString(totals.TotalTokens == 0
                    ? "LocalUsagePeriodEmptyAccessibleFormat"
                    : "LocalUsagePeriodAccessibleFormat"),
                label,
                costText,
                detailText));
    }

    private static LocalUsageSpendBreakdown CreateBreakdown(
        IReadOnlyList<DailyUsageRollup> rollups,
        Totals totals,
        string missing,
        Func<string, string> getString)
    {
        SpendSlice[] agentSlices = rollups
            .GroupBy(rollup => rollup.AgentId)
            .Select(group =>
            {
                Totals agentTotals = Sum(group);
                return new
                {
                    Agent = group.Key,
                    Totals = agentTotals,
                    Amount = agentTotals.Reported + agentTotals.Estimated,
                };
            })
            .Where(item => item.Amount > 0m)
            .OrderByDescending(item => item.Amount)
            .ThenBy(item => item.Agent.Value, StringComparer.Ordinal)
            .Select(item => new SpendSlice(
                item.Agent.Value,
                GetAgentName(item.Agent.Value, getString),
                decimal.ToDouble(item.Amount),
                FormatCostParts(item.Totals, missing, getString),
                CompactAmountText: FormatUsd(item.Amount, getString)))
            .ToArray();

        LocalUsageModelRow[] models = rollups
            .GroupBy(rollup => new { rollup.AgentId, rollup.ModelId })
            .Select(group =>
            {
                Totals modelTotals = Sum(group);
                return new
                {
                    group.Key.AgentId,
                    group.Key.ModelId,
                    Totals = modelTotals,
                    Amount = modelTotals.Reported + modelTotals.Estimated,
                };
            })
            .OrderByDescending(item => item.Amount)
            .ThenByDescending(item => item.Totals.TotalTokens)
            .ThenBy(item => item.AgentId.Value, StringComparer.Ordinal)
            .ThenBy(item => item.ModelId.Value, StringComparer.Ordinal)
            .Select(item =>
            {
                string agentName = GetAgentName(item.AgentId.Value, getString);
                string reported = string.Format(
                    CultureInfo.CurrentCulture,
                    getString("LocalUsageModelReportedFormat"),
                    item.Totals.HasReported ? FormatUsd(item.Totals.Reported, getString) : missing);
                string estimated = string.Format(
                    CultureInfo.CurrentCulture,
                    getString("LocalUsageModelEstimatedFormat"),
                    item.Totals.HasEstimated ? FormatUsd(item.Totals.Estimated, getString) : missing);
                string coverage = string.Format(
                    CultureInfo.CurrentCulture,
                    getString("LocalUsageModelCoverageFormat"),
                    FormatCoverage(item.Totals, missing));
                string title = string.Format(
                    CultureInfo.CurrentCulture,
                    getString("LocalUsageModelTitleFormat"),
                    agentName,
                    item.ModelId.Value);
                return new LocalUsageModelRow(
                    item.AgentId.Value,
                    agentName,
                    item.ModelId.Value,
                    reported,
                    estimated,
                    coverage,
                    $"UsageProductCard.Model.{item.AgentId.Value}.{item.ModelId.Value}",
                    title,
                    string.Format(
                        CultureInfo.CurrentCulture,
                        getString("LocalUsageModelAccessibleFormat"),
                        title,
                        reported,
                        estimated,
                        coverage));
            })
            .ToArray();

        string totalText = totals.HasAnyCost
            ? FormatUsd(totals.Reported + totals.Estimated, getString)
            : missing;
        string compactTotalText = totals.HasAnyCost
            ? string.Format(
                CultureInfo.CurrentCulture,
                getString("LocalUsageUsdCompactFormat"),
                totals.Reported + totals.Estimated)
            : missing;
        string summary = string.Format(
            CultureInfo.CurrentCulture,
            getString("LocalUsageBreakdownSummaryFormat"),
            models.Select(model => model.AgentId).Distinct(StringComparer.Ordinal).Count(),
            models.Length);
        return new LocalUsageSpendBreakdown(
            getString("LocalUsageBreakdownTitle"),
            summary,
            totalText,
            string.Format(
                CultureInfo.CurrentCulture,
                getString("LocalUsageBreakdownAccessibleFormat"),
                totalText,
                summary),
            agentSlices,
            models,
            compactTotalText);
    }

    private static string GetAgentName(string agentId, Func<string, string> getString) =>
        agentId switch
        {
            "claude" => getString("LocalUsageAgentClaude"),
            "codex" => getString("LocalUsageAgentCodex"),
            "grok" => getString("LocalUsageAgentGrok"),
            "opencode" => getString("LocalUsageAgentOpenCode"),
            _ => agentId,
        };

    private static ProviderStatusRow[] CreateProviderStatuses(
        IReadOnlyList<DailyUsageRollup> rollups,
        IReadOnlyList<UsageSourceDiagnostic> diagnostics,
        string missing,
        Func<string, string> getString) =>
        diagnostics
            .Where(item => item.AgentId.Value is "claude" or "codex" or "grok" or "opencode")
            .Select(item =>
            {
                Totals totals = Sum(rollups.Where(rollup => rollup.AgentId == item.AgentId));
                string providerId = item.AgentId.Value;
                bool isRetainedSnapshot = item.RetainsLastReliableSnapshot
                    && item.Status != UsageSourceReadStatus.Complete
                    && totals.TotalTokens > 0;
                string usage = item.Issue == UsageSourceIssueKind.UnsupportedSchema
                    ? getString("ProviderStatusUnsupportedSchema")
                    : item.Status switch
                    {
                        UsageSourceReadStatus.Complete => getString("ProviderStatusComplete"),
                        UsageSourceReadStatus.Partial => getString("ProviderStatusPartial"),
                        _ when item.Issue == UsageSourceIssueKind.RootUnavailable =>
                            getString("ProviderStatusNotConfigured"),
                        _ => getString("ProviderStatusNoData"),
                    };
                string spend = totals.HasReported
                    ? getString(isRetainedSnapshot
                        ? "ProviderStatusReportedLastReliable"
                        : "ProviderStatusReported")
                    : totals.HasEstimated
                        ? getString(isRetainedSnapshot
                            ? "ProviderStatusEstimatedLastReliable"
                            : "ProviderStatusEstimated")
                        : totals.TotalTokens > 0
                            ? getString("ProviderStatusUnavailable")
                            : missing;
                string coverage = isRetainedSnapshot
                    ? string.Format(
                        CultureInfo.CurrentCulture,
                        getString("ProviderStatusCoverageLastReliableFormat"),
                        FormatCoverage(totals, missing))
                    : FormatCoverage(totals, missing);
                string root = item.Issue == UsageSourceIssueKind.RootUnavailable
                    ? getString("ProviderStatusRootMissing")
                    : getString("ProviderStatusRootDetected");
                string recovery = item.Issue switch
                {
                    UsageSourceIssueKind.RootUnavailable => getString("ProviderStatusRecoveryOpenTool"),
                    UsageSourceIssueKind.UnsupportedSchema => getString("ProviderStatusRecoveryUpdate"),
                    UsageSourceIssueKind.PartialScan or UsageSourceIssueKind.AccessBlocked =>
                        getString("ProviderStatusRecoveryRetry"),
                    _ => getString("ProviderStatusRecoveryRefresh"),
                };
                return new ProviderStatusRow(
                    providerId,
                    GetAgentName(providerId, getString),
                    root,
                    recovery,
                    [
                        new(
                            getString("ProviderStatusQuota"),
                            getString(providerId == "grok"
                                ? "ProviderStatusBlocked"
                                : "ProviderStatusUnavailable"),
                            $"ProviderStatus.{providerId}.Quota"),
                        new(getString("ProviderStatusUsage"), usage, $"ProviderStatus.{providerId}.Usage"),
                        new(getString("ProviderStatusSpend"), spend, $"ProviderStatus.{providerId}.Spend"),
                        new(getString("ProviderStatusCoverage"), coverage, $"ProviderStatus.{providerId}.Coverage"),
                    ],
                    $"ProviderStatus.{providerId}");
            })
            .ToArray();

    private static Totals Sum(
        IEnumerable<DailyUsageRollup> rollups,
        DateOnly fromInclusive,
        DateOnly toInclusive) =>
        Sum(rollups.Where(rollup =>
            rollup.Date >= fromInclusive && rollup.Date <= toInclusive));

    private static Totals Sum(IEnumerable<DailyUsageRollup> rollups)
    {
        var totals = new Totals();
        foreach (DailyUsageRollup rollup in rollups)
        {
            totals.Add(rollup);
        }
        return totals;
    }

    private static string FormatCoverage(Totals totals, string missing) =>
        totals.TotalTokens == 0
            ? missing
            : string.Format(
                CultureInfo.CurrentCulture,
                "{0:0.#}%",
                (totals.TotalTokens - totals.UnpricedTokens) * 100m / totals.TotalTokens);

    private static string FormatCostParts(
        Totals totals,
        string missing,
        Func<string, string> getString) =>
        string.Format(
            CultureInfo.CurrentCulture,
            getString("LocalUsagePeriodCostFormat"),
            getString("LocalUsageReportedShort"),
            totals.HasReported ? FormatUsd(totals.Reported, getString) : missing,
            getString("LocalUsageEstimatedShort"),
            totals.HasEstimated ? FormatUsd(totals.Estimated, getString) : missing);

    private static string FormatCostPerMillion(
        Totals totals,
        string missing,
        Func<string, string> getString) =>
        totals.TotalTokens == 0 || !totals.HasAnyCost
            ? missing
            : string.Format(
                CultureInfo.CurrentCulture,
                getString("LocalUsageUsdPerMillionFormat"),
                (totals.Reported + totals.Estimated) * 1_000_000m / totals.TotalTokens);

    private static string FormatUsd(decimal amount, Func<string, string> getString) =>
        string.Format(
            CultureInfo.CurrentCulture,
            getString("LocalUsageUsdFormat"),
            amount);

    private static string FormatCount(long value) =>
        value.ToString("N0", CultureInfo.CurrentCulture);

    private sealed class Totals
    {
        public decimal Reported { get; private set; }
        public decimal Estimated { get; private set; }
        public long TotalTokens { get; private set; }
        public long UnpricedTokens { get; private set; }
        public bool HasReported { get; private set; }
        public bool HasEstimated { get; private set; }
        public bool HasAnyCost => HasReported || HasEstimated;

        public void Add(DailyUsageRollup rollup)
        {
            checked
            {
                TotalTokens += rollup.Tokens.Total;
                UnpricedTokens += rollup.UnpricedTokens;
                if (rollup.ReportedCostUsd is decimal reported)
                {
                    Reported += reported;
                    HasReported = true;
                }
                if (rollup.EstimatedCostUsd is decimal estimated)
                {
                    Estimated += estimated;
                    HasEstimated = true;
                }
            }
        }
    }
}

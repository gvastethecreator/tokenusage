using System.Globalization;
using TokenUsage.App.Localization;
using TokenUsage.App.ViewModels.Dashboard;
using TokenUsage.Core.Layout;
using TokenUsage.Core.Providers;

namespace TokenUsage.App.ViewModels;

/// <summary>
/// Parts shared by the cards of providers read through a saved API key (Vercel AI Gateway,
/// OpenRouter): a dollar spend limit as a quota window, and the provider summary the compact
/// panel and the tray list. Their spend is not added to the local totals, because a gateway can
/// also carry requests that a local tool already recorded.
/// </summary>
public static class ApiProviderCardParts
{
    /// <summary>Providers read through a saved key, in panel order.</summary>
    public static IReadOnlyList<string> ProviderIds { get; } = ["vercel-ai-gateway", "openrouter"];

    public static bool IsApiProvider(string? providerId) =>
        providerId is not null && ProviderIds.Contains(providerId, StringComparer.Ordinal);

    public static QuotaWindow CreateSpendLimitWindow(
        string title,
        ProgressMetricSnapshot limit,
        Func<string, string> getString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(limit);
        ArgumentNullException.ThrowIfNull(getString);
        string remaining = Format(
            getString,
            "ApiLimitRemainingFormat",
            Math.Max(0m, limit.Limit - limit.Used),
            limit.Limit);
        string used = Format(getString, "ApiLimitUsedFormat", limit.Used, limit.Limit);
        string reset = ResetText(limit.ResetCadence, getString);
        return new QuotaWindow(
            title,
            decimal.ToDouble(limit.RemainingPercent),
            remaining,
            reset,
            Format(getString, "ApiLimitAutomationFormat", title, remaining, reset),
            IsNearLimit: limit.RemainingPercent <= 20m,
            LayoutMetricId: limit.Id.Value,
            ResetAtUtc: limit.ResetsAtUtc,
            UsedText: used);
    }

    public static string ResetText(ProgressResetCadence? cadence, Func<string, string> getString)
    {
        ArgumentNullException.ThrowIfNull(getString);
        return getString(cadence switch
        {
            ProgressResetCadence.Daily => "ApiLimitResetDaily",
            ProgressResetCadence.Weekly => "ApiLimitResetWeekly",
            ProgressResetCadence.Monthly => "ApiLimitResetMonthly",
            ProgressResetCadence.Never => "ApiLimitResetNever",
            _ => "ApiLimitResetUnknown",
        });
    }

    /// <summary>
    /// A panel and tray row for an API provider. A null cost or token count shows a dash
    /// instead of zero, because the provider did not report it.
    /// </summary>
    public static DashboardProviderSummary CreateSummary(
        string providerId,
        decimal? costUsd,
        long? tokens,
        Func<string, string> getString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentNullException.ThrowIfNull(getString);
        string name = ProviderDisplayName.Resolve(providerId, getString);
        string costText = costUsd is decimal cost
            ? UsageValueFormatter.Usd(cost, getString)
            : "—";
        string tokensText = tokens is long count
            ? UsageValueFormatter.CompactTokens(count)
            : "—";
        return new DashboardProviderSummary(
            providerId,
            name,
            costUsd ?? 0m,
            tokens ?? 0L,
            0d,
            costText,
            tokensText,
            "—",
            $"{name}: {costText}, {tokensText}",
            ProviderColorPreference.Resolve(providerId, customColorHex: null),
            $"CompactProvider.{providerId}",
            0d,
            HasData: costUsd is not null || tokens is not null,
            HasCostData: costUsd is not null);
    }

    internal static IReadOnlyDictionary<string, decimal> ScalarMetrics(ProviderSnapshot snapshot) =>
        snapshot.Metrics
            .OfType<ScalarMetricSnapshot>()
            .GroupBy(metric => metric.Id.Value, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Value, StringComparer.Ordinal);

    internal static string Format(
        Func<string, string> getString,
        string key,
        params object[] values) =>
        string.Format(CultureInfo.CurrentCulture, getString(key), values);
}

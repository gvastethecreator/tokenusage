using System.Globalization;
using WOpenUsage.Core.Providers;

namespace WOpenUsage.App.ViewModels.Sample;

public static class SampleDashboardProjector
{
    public static SampleDashboardSnapshot Create(
        SampleScenario scenario,
        ProviderSnapshot codexSnapshot,
        Func<string, string> getString)
    {
        ArgumentNullException.ThrowIfNull(codexSnapshot);
        ArgumentNullException.ThrowIfNull(getString);
        if (!string.Equals(codexSnapshot.ProviderId.Value, "codex", StringComparison.Ordinal))
        {
            throw new ArgumentException("The sample overlay requires the Codex provider ID.", nameof(codexSnapshot));
        }

        SampleDashboardSnapshot baseline = SampleDashboardCatalog.Create(scenario, getString);
        ProgressMetricSnapshot progress = codexSnapshot.Metrics
            .OfType<ProgressMetricSnapshot>()
            .Single(metric => metric.Id.Value == "session");
        ScalarMetricSnapshot spend = codexSnapshot.Metrics
            .OfType<ScalarMetricSnapshot>()
            .Single(metric => metric.Id.Value == "spend-usd");

        double spendAmount = decimal.ToDouble(spend.Value);
        SampleSpendSlice[] slices = baseline.SpendSlices
            .Select(slice => slice.ProviderId == "codex"
                ? slice with
                {
                    Amount = spendAmount,
                    AmountText = Money(spendAmount, getString),
                }
                : slice)
            .ToArray();
        SampleProviderCard[] providers = baseline.Providers
            .Select(provider => provider.ProviderId == "codex"
                ? ApplyProgress(provider, progress, codexSnapshot, getString)
                : provider)
            .ToArray();
        double total = slices.Sum(slice => slice.Amount);
        string totalText = Money(total, getString);
        string details = string.Join(
            ", ",
            slices.Select(slice => $"{slice.ProviderName} {slice.AmountText}"));

        return baseline with
        {
            TotalSpendAmount = totalText,
            SpendAccessibleName = Format(
                getString,
                "SampleSpendAccessibleNameFormat",
                totalText,
                slices.Length,
                details),
            SpendSlices = slices,
            Providers = providers,
            CompactTotalSpendAmount = Format(getString, "SampleUsdCompactFormat", total),
        };
    }

    private static SampleProviderCard ApplyProgress(
        SampleProviderCard provider,
        ProgressMetricSnapshot progress,
        ProviderSnapshot snapshot,
        Func<string, string> text)
    {
        SampleQuotaWindow[] windows = provider.Windows.ToArray();
        if (windows.Length == 0)
        {
            throw new InvalidOperationException("The Codex sample card has no quota window.");
        }

        double remaining = decimal.ToDouble(progress.RemainingPercent);
        string remainingText = Format(text, "SampleRemainingFormat", remaining);
        string resetText = progress.ResetsAtUtc is DateTimeOffset resetAtUtc
            ? FormatReset(resetAtUtc - snapshot.FetchedAtUtc, text)
            : windows[0].ResetText;
        windows[0] = windows[0] with
        {
            RemainingPercent = remaining,
            RemainingText = remainingText,
            ResetText = resetText,
            AutomationName = $"{provider.Name}, {windows[0].Title}: {remainingText}. {resetText}",
            IsNearLimit = remaining <= 15d,
        };

        return provider with { Windows = windows };
    }

    private static string FormatReset(TimeSpan remaining, Func<string, string> text)
    {
        int hours = Math.Max(0, (int)Math.Ceiling(remaining.TotalHours));
        return Format(text, "SampleResetHoursFormat", hours);
    }

    private static string Format(Func<string, string> text, string key, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, text(key), args);

    private static string Money(double value, Func<string, string> text) =>
        Format(text, "SampleUsdFormat", value);
}

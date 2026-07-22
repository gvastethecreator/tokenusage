using System.Globalization;

namespace WOpenUsage.App.ViewModels.Sample;

public static class SampleDashboardCatalog
{
    public static SampleDashboardSnapshot Create(
        SampleScenario scenario,
        Func<string, string> getString)
    {
        ArgumentNullException.ThrowIfNull(getString);

        return scenario switch
        {
            SampleScenario.NearLimit => CreateNearLimit(getString),
            SampleScenario.PartialStale => CreatePartialStale(getString),
            _ => CreateNormal(getString),
        };
    }

    private static SampleDashboardSnapshot CreateNormal(Func<string, string> text)
    {
        const double total = 48.12;

        return Snapshot(
            SampleScenario.Normal,
            total,
            text,
            [
                Spend("Claude", 22.40, total),
                Spend("Codex", 12.30, total),
                Spend("Grok Build", 7.10, total),
                Spend("OpenCode", 5.92, total),
                Spend("Antigravity CLI", 0.40, total),
            ],
            [
                QuotaProvider("SampleProvider.Codex", "Codex", text("SamplePlanPlus"), text,
                    Session(62, 4, false, text), Weekly(81, 5, 2, false, text)),
                QuotaProvider("SampleProvider.Claude", "Claude", text("SamplePlanPro"), text,
                    Session(100, 5, false, text), Weekly(36, 1, 6, false, text)),
                MetricProvider("SampleProvider.GrokBuild", "Grok Build", text("SamplePlanLocal"),
                    text("SampleCapabilityLocalSpend"), null, "1.24M", "$7.10", text),
                MetricProvider("SampleProvider.OpenCode", "OpenCode", text("SamplePlanLocal"),
                    text("SampleCapabilityLocalSpend"), null, "860K", "$5.92", text),
                MetricProvider("SampleProvider.Antigravity", "Antigravity CLI", text("SamplePlanExperimental"),
                    text("SampleCapabilityLocal"), text("SampleNoticeNoQuota"), "120K", "$0.40", text),
            ]);
    }

    private static SampleDashboardSnapshot CreateNearLimit(Func<string, string> text)
    {
        const double total = 96.40;

        return Snapshot(
            SampleScenario.NearLimit,
            total,
            text,
            [
                Spend("Claude", 36.70, total),
                Spend("Codex", 23.80, total),
                Spend("Grok Build", 18.90, total),
                Spend("OpenCode", 15.80, total),
                Spend("Antigravity CLI", 1.20, total),
            ],
            [
                QuotaProvider("SampleProvider.Codex", "Codex", text("SamplePlanPlus"), text,
                    Session(8, 1, true, text), Weekly(22, 2, 4, true, text)),
                QuotaProvider("SampleProvider.Claude", "Claude", text("SamplePlanPro"), text,
                    Session(12, 2, true, text), Weekly(5, 0, 8, true, text)),
                MetricProvider("SampleProvider.GrokBuild", "Grok Build", text("SamplePlanLocal"),
                    text("SampleCapabilityLocalSpend"), text("SampleNoticeHighSpend"), "4.10M", "$18.90", text),
                MetricProvider("SampleProvider.OpenCode", "OpenCode", text("SamplePlanLocal"),
                    text("SampleCapabilityLocalSpend"), null, "2.20M", "$15.80", text),
                QuotaProvider("SampleProvider.Antigravity", "Antigravity CLI", text("SamplePlanExperimental"), text,
                    SoftBudget(11, 3, true, text)),
            ]);
    }

    private static SampleDashboardSnapshot CreatePartialStale(Func<string, string> text)
    {
        const double total = 31.05;

        return Snapshot(
            SampleScenario.PartialStale,
            total,
            text,
            [
                Spend("Claude", 12.60, total),
                Spend("Codex", 9.40, total),
                Spend("Grok Build", 4.25, total),
                Spend("OpenCode", 4.80, total),
                Spend("Antigravity CLI", 0, total),
            ],
            [
                QuotaProvider("SampleProvider.Codex", "Codex", text("SamplePlanPlus"), text,
                    text("SampleNoticeStale"), Session(62, 4, false, text), Weekly(81, 5, 2, false, text)),
                QuotaProvider("SampleProvider.Claude", "Claude", text("SamplePlanPro"), text,
                    text("SampleNoticePartial"), Weekly(36, 1, 6, false, text)),
                MetricProvider("SampleProvider.GrokBuild", "Grok Build", text("SamplePlanLocal"),
                    text("SampleCapabilityLocalSpend"), text("SampleNoticePartial"), "720K", "$4.25", text),
                MetricProvider("SampleProvider.OpenCode", "OpenCode", text("SamplePlanLocal"),
                    text("SampleCapabilityLocalSpend"), null, "640K", "$4.80", text),
                MetricProvider("SampleProvider.Antigravity", "Antigravity CLI", text("SamplePlanExperimental"),
                    text("SampleCapabilityPolicy"), text("SampleNoticePolicy"), "64K", null, text),
            ]);
    }

    private static SampleDashboardSnapshot Snapshot(
        SampleScenario scenario,
        double total,
        Func<string, string> text,
        IReadOnlyList<SampleSpendSlice> slices,
        IReadOnlyList<SampleProviderCard> providers) =>
        new(
            scenario,
            Money(total),
            text(scenario switch
            {
                SampleScenario.NearLimit => "SamplePeriodNearLimit",
                SampleScenario.PartialStale => "SamplePeriodPartialStale",
                _ => "SamplePeriodNormal",
            }),
            slices,
            providers);

    private static SampleSpendSlice Spend(string provider, double amount, double total) =>
        new(provider, Money(amount), total <= 0 ? 0 : amount / total * 100);

    private static SampleProviderCard QuotaProvider(
        string id,
        string name,
        string plan,
        Func<string, string> text,
        params SampleQuotaWindow[] windows) =>
        QuotaProvider(id, name, plan, text, null, windows);

    private static SampleProviderCard QuotaProvider(
        string id,
        string name,
        string plan,
        Func<string, string> text,
        string? notice,
        params SampleQuotaWindow[] windows) =>
        new(
            id,
            name,
            plan,
            text("SampleCapabilityQuota"),
            notice,
            windows.Select(window => window with
            {
                AutomationName = $"{name}, {window.AutomationName}",
            }).ToArray(),
            []);

    private static SampleProviderCard MetricProvider(
        string id,
        string name,
        string plan,
        string capability,
        string? notice,
        string tokens,
        string? spend,
        Func<string, string> text)
    {
        List<SampleMetric> metrics = [new(text("SampleMetricTokens"), tokens)];
        if (spend is not null)
        {
            metrics.Add(new SampleMetric(text("SampleMetricSpend"), spend));
        }

        return new SampleProviderCard(id, name, plan, capability, notice, [], metrics);
    }

    private static SampleQuotaWindow Session(
        int remaining,
        int resetHours,
        bool nearLimit,
        Func<string, string> text) =>
        Window(text("SampleWindowSession"), remaining, Format(text, "SampleResetHoursFormat", resetHours), nearLimit, text);

    private static SampleQuotaWindow Weekly(
        int remaining,
        int resetDays,
        int resetHours,
        bool nearLimit,
        Func<string, string> text) =>
        Window(text("SampleWindowWeekly"), remaining, Format(text, "SampleResetDaysHoursFormat", resetDays, resetHours), nearLimit, text);

    private static SampleQuotaWindow SoftBudget(
        int remaining,
        int resetDays,
        bool nearLimit,
        Func<string, string> text) =>
        Window(text("SampleWindowSoftBudget"), remaining, Format(text, "SampleResetDaysFormat", resetDays), nearLimit, text);

    private static SampleQuotaWindow Window(
        string title,
        int remaining,
        string reset,
        bool nearLimit,
        Func<string, string> text)
    {
        var remainingText = Format(text, "SampleRemainingFormat", remaining);
        return new SampleQuotaWindow(
            title,
            remaining,
            remainingText,
            reset,
            $"{title}: {remainingText}",
            nearLimit);
    }

    private static string Format(Func<string, string> text, string key, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, text(key), args);

    private static string Money(double value) =>
        value.ToString("$0.00", CultureInfo.InvariantCulture);
}

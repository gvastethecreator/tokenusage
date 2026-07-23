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
            SampleScenario.Partial => CreatePartial(getString),
            SampleScenario.Stale => CreateStale(getString),
            SampleScenario.Error => CreateError(getString),
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
                Spend("claude", "Claude", 22.40),
                Spend("codex", "Codex", 12.30),
                Spend("grok", "Grok Build", 7.10),
                Spend("opencode", "OpenCode", 5.92),
                Spend("antigravity", "Antigravity CLI", 0.40),
            ],
            [
                CodexProvider(SampleScenario.Normal, text("SamplePlanPlus"), text, null,
                    Session(62, 4, false, text), Weekly(81, 5, 2, false, text)),
                QuotaProvider("claude", "SampleProvider.Claude", "Claude", text("SamplePlanPro"), text,
                    Session(100, 5, false, text), Weekly(36, 1, 6, false, text)),
                MetricProvider("grok", "SampleProvider.GrokBuild", "Grok Build", text("SamplePlanLocal"),
                    text("SampleCapabilityLocalSpend"), null, 1_240_000, 7.10, text),
                MetricProvider("opencode", "SampleProvider.OpenCode", "OpenCode", text("SamplePlanLocal"),
                    text("SampleCapabilityLocalSpend"), null, 860_000, 5.92, text),
                MetricProvider("antigravity", "SampleProvider.Antigravity", "Antigravity CLI", text("SamplePlanExperimental"),
                    text("SampleCapabilityLocal"), text("SampleNoticeNoQuota"), 120_000, 0.40, text),
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
                Spend("claude", "Claude", 36.70),
                Spend("codex", "Codex", 23.80),
                Spend("grok", "Grok Build", 18.90),
                Spend("opencode", "OpenCode", 15.80),
                Spend("antigravity", "Antigravity CLI", 1.20),
            ],
            [
                CodexProvider(SampleScenario.NearLimit, text("SamplePlanPlus"), text, null,
                    Session(8, 1, true, text), Weekly(22, 2, 4, true, text)),
                QuotaProvider("claude", "SampleProvider.Claude", "Claude", text("SamplePlanPro"), text,
                    Session(12, 2, true, text), Weekly(5, 0, 8, true, text)),
                MetricProvider("grok", "SampleProvider.GrokBuild", "Grok Build", text("SamplePlanLocal"),
                    text("SampleCapabilityLocalSpend"), text("SampleNoticeHighSpend"), 4_100_000, 18.90, text),
                MetricProvider("opencode", "SampleProvider.OpenCode", "OpenCode", text("SamplePlanLocal"),
                    text("SampleCapabilityLocalSpend"), null, 2_200_000, 15.80, text),
                QuotaProvider("antigravity", "SampleProvider.Antigravity", "Antigravity CLI", text("SamplePlanExperimental"), text,
                    SoftBudget(11, 3, true, text)),
            ]);
    }

    private static SampleDashboardSnapshot CreatePartial(Func<string, string> text)
    {
        const double total = 31.05;

        return Snapshot(
            SampleScenario.Partial,
            total,
            text,
            [
                Spend("claude", "Claude", 12.60),
                Spend("codex", "Codex", 9.40),
                Spend("grok", "Grok Build", 4.25),
                Spend("opencode", "OpenCode", 4.80),
                Spend("antigravity", "Antigravity CLI", 0),
            ],
            [
                CodexProvider(SampleScenario.Partial, text("SamplePlanPlus"), text,
                    text("CodexPartialUsageNotice"), Session(62, 4, false, text), Weekly(81, 5, 2, false, text)),
                QuotaProvider("claude", "SampleProvider.Claude", "Claude", text("SamplePlanPro"), text,
                    text("SampleNoticePartial"), Weekly(36, 1, 6, false, text)),
                MetricProvider("grok", "SampleProvider.GrokBuild", "Grok Build", text("SamplePlanLocal"),
                    text("SampleCapabilityLocalSpend"), text("SampleNoticePartial"), 720_000, 4.25, text),
                MetricProvider("opencode", "SampleProvider.OpenCode", "OpenCode", text("SamplePlanLocal"),
                    text("SampleCapabilityLocalSpend"), null, 640_000, 4.80, text),
                MetricProvider("antigravity", "SampleProvider.Antigravity", "Antigravity CLI", text("SamplePlanExperimental"),
                    text("SampleCapabilityPolicy"), text("SampleNoticePolicy"), 64_000, null, text),
            ]);
    }

    private static SampleDashboardSnapshot CreateStale(Func<string, string> text)
    {
        const double total = 48.12;

        return Snapshot(
            SampleScenario.Stale,
            total,
            text,
            [
                Spend("claude", "Claude", 22.40),
                Spend("codex", "Codex", 12.30),
                Spend("grok", "Grok Build", 7.10),
                Spend("opencode", "OpenCode", 5.92),
                Spend("antigravity", "Antigravity CLI", 0.40),
            ],
            [
                CodexProvider(SampleScenario.Stale, text("SamplePlanPlus"), text,
                    text("SampleNoticeStale"), Session(62, 4, false, text), Weekly(81, 5, 2, false, text)),
                QuotaProvider("claude", "SampleProvider.Claude", "Claude", text("SamplePlanPro"), text,
                    Session(100, 5, false, text), Weekly(36, 1, 6, false, text)),
                MetricProvider("grok", "SampleProvider.GrokBuild", "Grok Build", text("SamplePlanLocal"),
                    text("SampleCapabilityLocalSpend"), null, 1_240_000, 7.10, text),
                MetricProvider("opencode", "SampleProvider.OpenCode", "OpenCode", text("SamplePlanLocal"),
                    text("SampleCapabilityLocalSpend"), null, 860_000, 5.92, text),
                MetricProvider("antigravity", "SampleProvider.Antigravity", "Antigravity CLI", text("SamplePlanExperimental"),
                    text("SampleCapabilityLocal"), text("SampleNoticeNoQuota"), 120_000, 0.40, text),
            ]);
    }

    private static SampleDashboardSnapshot CreateError(Func<string, string> text)
    {
        SampleDashboardSnapshot normal = CreateNormal(text);
        SampleProviderCard[] providers = normal.Providers
            .Select(provider => provider.ProviderId == "codex"
                ? provider with { NoticeText = text("SampleNoticeError") }
                : provider)
            .ToArray();

        return normal with
        {
            Scenario = SampleScenario.Error,
            PeriodLabel = text("SamplePeriodError"),
            Providers = providers,
        };
    }

    private static SampleDashboardSnapshot Snapshot(
        SampleScenario scenario,
        double total,
        Func<string, string> text,
        IReadOnlyList<SampleSpendSlice> slices,
        IReadOnlyList<SampleProviderCard> providers)
    {
        SampleSpendSlice[] localizedSlices = slices
            .Select(slice => slice with { AmountText = Money(slice.Amount, text) })
            .ToArray();
        string totalText = Money(total, text);
        string details = string.Join(
            ", ",
            localizedSlices.Select(slice => $"{slice.ProviderName} {slice.AmountText}"));

        return new(
            scenario,
            totalText,
            text(scenario switch
            {
                SampleScenario.NearLimit => "SamplePeriodNearLimit",
                SampleScenario.Partial => "SamplePeriodPartial",
                SampleScenario.Stale => "SamplePeriodStale",
                SampleScenario.Error => "SamplePeriodError",
                _ => "SamplePeriodNormal",
            }),
            Format(text, "SampleSpendAccessibleNameFormat", totalText, localizedSlices.Length, details),
            localizedSlices,
            providers,
            Format(text, "SampleUsdCompactFormat", total));
    }

    private static SampleSpendSlice Spend(
        string providerId,
        string provider,
        double amount) =>
        new(providerId, provider, amount, string.Empty);

    private static SampleProviderCard QuotaProvider(
        string providerId,
        string id,
        string name,
        string plan,
        Func<string, string> text,
        params SampleQuotaWindow[] windows) =>
        QuotaProvider(providerId, id, name, plan, text, null, windows);

    private static SampleProviderCard QuotaProvider(
        string providerId,
        string id,
        string name,
        string plan,
        Func<string, string> text,
        string? notice,
        params SampleQuotaWindow[] windows) =>
        WithSampleDetails(new(
            providerId,
            id,
            name,
            plan,
            text("SampleCapabilityQuota"),
            notice,
            windows.Select(window => window with
            {
                AutomationName = $"{name}, {window.AutomationName}. {window.ResetText}",
            }).ToArray(),
            []), text);

    private static SampleProviderCard MetricProvider(
        string providerId,
        string id,
        string name,
        string plan,
        string capability,
        string? notice,
        long tokens,
        double? spend,
        Func<string, string> text)
    {
        List<SampleMetric> metrics =
            [new(text("SampleMetricTokens"), CompactTokens(tokens, includeUnit: false, text))];
        if (spend is not null)
        {
            metrics.Add(new SampleMetric(text("SampleMetricSpend"), Money(spend.Value, text)));
        }

        return WithSampleDetails(
            new SampleProviderCard(providerId, id, name, plan, capability, notice, [], metrics),
            text);
    }

    private static SampleProviderCard CodexProvider(
        SampleScenario scenario,
        string plan,
        Func<string, string> text,
        string? notice,
        params SampleQuotaWindow[] windows)
    {
        SampleProviderCard provider = QuotaProvider(
            "codex",
            "SampleProvider.Codex",
            "Codex",
            plan,
            text,
            notice,
            windows);
        string?[] pace = scenario switch
        {
            SampleScenario.NearLimit =>
            [
                Format(text, "CodexPaceBehindEtaFormat", 135, Format(text, "CodexDurationHoursMinutesFormat", 2, 10)),
                Format(text, "CodexPaceBehindFormat", 118),
            ],
            SampleScenario.Partial =>
            [Format(text, "CodexPaceAheadFormat", 74), null],
            _ =>
            [
                Format(text, "CodexPaceAheadFormat", 74),
                Format(text, "CodexPaceOnTrackFormat", 96),
            ],
        };
        SampleQuotaWindow[] pacedWindows = provider.Windows
            .Select((window, index) => window with
            {
                PaceText = index < pace.Length ? pace[index] : null,
                IsPaceBehind = scenario == SampleScenario.NearLimit && index < 2,
                PaceAutomationId = index == 0 ? "CodexPace.Session" : "CodexPace.Weekly",
            })
            .ToArray();
        IReadOnlyList<SampleMetric> metrics = scenario switch
        {
            SampleScenario.NearLimit => UsageMetrics(
                CompactTokens(490_000, includeUnit: true, text),
                CompactTokens(420_000, includeUnit: true, text),
                CompactTokens(3_200_000, includeUnit: true, text),
                CompactTokens(12_400_000, includeUnit: true, text),
                text),
            SampleScenario.Partial => UsageMetrics(
                CompactTokens(0, includeUnit: true, text),
                text("CodexUsageMissing"),
                CompactTokens(820_000, includeUnit: true, text),
                text("CodexUsageMissing"),
                text),
            _ => UsageMetrics(
                CompactTokens(185_000, includeUnit: true, text),
                CompactTokens(162_000, includeUnit: true, text),
                CompactTokens(1_240_000, includeUnit: true, text),
                CompactTokens(5_820_000, includeUnit: true, text),
                text),
        };

        return provider with
        {
            CapabilityLabel = text("CodexCapabilityUsage"),
            Windows = pacedWindows,
            Metrics = [],
            SecondaryMetrics = metrics,
        };
    }

    private static SampleProviderCard WithSampleDetails(
        SampleProviderCard provider,
        Func<string, string> text)
    {
        string source = text("ProviderSourceSample");
        string observed = text("ProviderObservedNow");
        return provider with
        {
            SourceLabel = text("ProviderSourceLabel"),
            SourceValue = source,
            ObservedLabel = text("ProviderObservedLabel"),
            ObservedValue = observed,
            DetailsTooltip = Format(text, "ProviderDetailsTooltipFormat", source, observed),
            DetailsAutomationName = Format(
                text,
                "ProviderDetailsAutomationNameFormat",
                provider.Name),
        };
    }

    private static IReadOnlyList<SampleMetric> UsageMetrics(
        string today,
        string yesterday,
        string last7Days,
        string last30Days,
        Func<string, string> text) =>
        [
            new(text("CodexUsageToday"), today, "CodexUsage.Today"),
            new(text("CodexUsageYesterday"), yesterday, "CodexUsage.Yesterday"),
            new(text("CodexUsageLast7Days"), last7Days, "CodexUsage.Last7Days"),
            new(text("CodexUsageLast30Days"), last30Days, "CodexUsage.Last30Days"),
        ];

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

    private static string Money(double value, Func<string, string> text) =>
        Format(text, "SampleUsdFormat", value);

    private static string CompactTokens(
        long value,
        bool includeUnit,
        Func<string, string> text)
    {
        if (value >= 1_000_000)
        {
            return Format(
                text,
                includeUnit ? "SampleTokenMillionsFormat" : "SampleCompactMillionsFormat",
                value / 1_000_000d);
        }

        if (value >= 1_000)
        {
            return Format(
                text,
                includeUnit ? "SampleTokenThousandsFormat" : "SampleCompactThousandsFormat",
                value / 1_000d);
        }

        return includeUnit
            ? Format(text, "SampleTokenExactFormat", value)
            : value.ToString("N0", CultureInfo.CurrentCulture);
    }
}

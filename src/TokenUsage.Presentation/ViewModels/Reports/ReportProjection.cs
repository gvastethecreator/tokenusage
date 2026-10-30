using System.Globalization;
using TokenUsage.Core.Automation;
using TokenUsage.Core.Layout;
using TokenUsage.Core.Usage;
using TokenUsage.Providers.Catalog;

namespace TokenUsage.App.ViewModels.Reports;

public enum ReportTone { Neutral, Up, Down, Warning, Critical }

/// <summary>Inclusive civil-day range. The comparison period is the same length, right before it.</summary>
public sealed record ReportPeriod(DateOnly From, DateOnly To)
{
    public int Days => To.DayNumber - From.DayNumber + 1;
    public ReportPeriod Previous => new(From.AddDays(-Days), From.AddDays(-1));
    public static ReportPeriod LastDays(DateOnly today, int days) => new(today.AddDays(-(days - 1)), today);
}

public sealed record ReportKpi(string Label, string Value, string Detail, ReportTone Tone);

public sealed record ReportSeries(string AgentId, string Name, string ColorHex);

public sealed record ReportDay(DateOnly Date, IReadOnlyList<long> Tokens, long PreviousTotal)
{
    public long Total => Tokens.Sum();
}

public sealed record ReportDaily(IReadOnlyList<ReportSeries> Series, IReadOnlyList<ReportDay> Days);

public sealed record ReportStep(string Label, string Detail, long Tokens, bool IsTotal);

public sealed record ReportRow(string Name, string? ProviderId, string Tokens, string Delta, ReportTone DeltaTone,
    string Cost, string Share, string ActiveDays);

public enum ReportGrouping { Agent, Model, Day }

/// <summary>Used percent over the fraction of a cycle that had elapsed (both 0..1 and 0..100).</summary>
public readonly record struct ReportPoint(double Elapsed, double Used);

public sealed record ReportCurve(IReadOnlyList<ReportPoint> Points);

public sealed record ReportPace(string Title, string ProviderId, string Used, string Verdict, ReportTone Tone,
    ReportCurve Current, IReadOnlyList<ReportCurve> Previous, double? ProjectedAtReset);

public sealed record ReportReset(string Quota, string Cycle, string EndUsed, string Tokens, string TokensPerPercent,
    string Change, ReportTone ChangeTone);

/// <summary>One quota window's cycles with their observations and, when attributable, the tokens spent in each.</summary>
public sealed record ReportQuotaInput(string Title, string ProviderId, IReadOnlyList<QuotaResetCycle> Cycles,
    IReadOnlyDictionary<DateTimeOffset, IReadOnlyList<QuotaObservation>> Observations,
    IReadOnlyDictionary<DateTimeOffset, long>? CycleTokens);

/// <summary>
/// Turns two period reports and the recorded quota cycles into what the report shows. Pure: no
/// I/O and no UI types, so every number on the page can be checked here.
/// </summary>
public static class ReportProjection
{
    public const int WaterfallMovers = 5;

    public static IReadOnlyList<ReportKpi> Kpis(UsageReport current, UsageReport previous, ReportPeriod period,
        DateOnly today, int quotasAtRisk, int quotas, Func<string, string> text)
    {
        long tokens = current.Totals.Tokens.Total, previousTokens = previous.Totals.Tokens.Total;
        decimal cost = current.Totals.TotalCostUsd, previousCost = previous.Totals.TotalCostUsd;
        long todayTokens = current.Days.FirstOrDefault(day => day.Date == today)?.Metrics.Tokens.Total ?? 0;
        return
        [
            new(text("ReportKpiTokens"), UsageValueFormatter.CompactTokens(tokens),
                Format(text("ReportVsPreviousFormat"), Change(tokens, previousTokens)), Tone(tokens, previousTokens)),
            new(text("ReportKpiCost"), UsageValueFormatter.Usd(cost, text),
                Format(text("ReportCostDetailFormat"), Change(cost, previousCost)), Tone(cost, previousCost)),
            new(text("ReportKpiDaily"), UsageValueFormatter.CompactTokens(tokens / (double)period.Days),
                Format(text("ReportTodayFormat"), UsageValueFormatter.CompactTokens(todayTokens)), ReportTone.Neutral),
            new(text("ReportKpiRisk"), Format(text("ReportRiskValueFormat"), quotasAtRisk, quotas),
                text(quotasAtRisk == 0 ? "ReportRiskNone" : "ReportRiskSome"),
                quotasAtRisk == 0 ? ReportTone.Neutral : ReportTone.Warning),
        ];
    }

    public static ReportDaily Daily(UsageReport current, UsageReport previous, ReportPeriod period)
    {
        ReportSeries[] series = current.Agents.OrderByDescending(agent => agent.Metrics.Tokens.Total)
            .Select(agent => new ReportSeries(agent.AgentId.Value, AgentName(agent.AgentId.Value),
                ProviderColorPreference.Resolve(agent.AgentId.Value, customColorHex: null)))
            .ToArray();
        var days = new List<ReportDay>(period.Days);
        for (int index = 0; index < period.Days; index++)
        {
            DateOnly date = period.From.AddDays(index), previousDate = period.Previous.From.AddDays(index);
            long[] tokens = series.Select(item => current.AgentDays
                .Where(day => day.Date == date && day.AgentId.Value == item.AgentId)
                .Sum(day => day.Metrics.Tokens.Total)).ToArray();
            long previousTotal = previous.Days.Where(day => day.Date == previousDate).Sum(day => day.Metrics.Tokens.Total);
            days.Add(new ReportDay(date, tokens, previousTotal));
        }
        return new ReportDaily(series, days);
    }

    /// <summary>Previous total, the largest changes by agent and model, the rest, then the current total.</summary>
    public static IReadOnlyList<ReportStep> Waterfall(UsageReport current, UsageReport previous, Func<string, string> text)
    {
        static string Key(UsageModelReport model) => model.AgentId.Value + "\n" + model.ModelId.Value;
        Dictionary<string, long> now = current.Models.GroupBy(Key).ToDictionary(group => group.Key, group => group.Sum(item => item.Metrics.Tokens.Total));
        Dictionary<string, long> before = previous.Models.GroupBy(Key).ToDictionary(group => group.Key, group => group.Sum(item => item.Metrics.Tokens.Total));
        (string Key, long Delta)[] deltas = now.Keys.Union(before.Keys)
            .Select(key => (key, now.GetValueOrDefault(key) - before.GetValueOrDefault(key)))
            .Where(item => item.Item2 != 0)
            .OrderByDescending(item => Math.Abs(item.Item2)).ToArray();
        var steps = new List<ReportStep> { new(text("ReportPreviousPeriod"), "", previous.Totals.Tokens.Total, true) };
        foreach ((string key, long delta) in deltas.Take(WaterfallMovers))
        {
            string[] parts = key.Split('\n');
            steps.Add(new ReportStep(AgentName(parts[0]), parts[1], delta, false));
        }
        long rest = deltas.Skip(WaterfallMovers).Sum(item => item.Delta);
        if (rest != 0) steps.Add(new ReportStep(text("ReportOther"), "", rest, false));
        steps.Add(new ReportStep(text("ReportThisPeriod"), "", current.Totals.Tokens.Total, true));
        return steps;
    }

    public static IReadOnlyList<ReportRow> Rows(UsageReport current, UsageReport previous, ReportGrouping grouping,
        Func<string, string> text)
    {
        long total = Math.Max(1, current.Totals.Tokens.Total);
        ReportRow Row(string name, string? providerId, UsageReportMetrics metrics, long previousTokens, int activeDays) =>
            new(name, providerId, UsageValueFormatter.CompactTokens(metrics.Tokens.Total),
                previousTokens == 0 ? text("ReportNew") : Change(metrics.Tokens.Total, previousTokens),
                Tone(metrics.Tokens.Total, previousTokens), UsageValueFormatter.Usd(metrics.TotalCostUsd, text),
                UsageValueFormatter.PercentText(metrics.Tokens.Total * 100m / total),
                activeDays.ToString(CultureInfo.CurrentCulture));
        return grouping switch
        {
            ReportGrouping.Agent => current.Agents.OrderByDescending(agent => agent.Metrics.Tokens.Total).Select(agent => Row(
                AgentName(agent.AgentId.Value), agent.AgentId.Value, agent.Metrics,
                previous.Agents.Where(item => item.AgentId == agent.AgentId).Sum(item => item.Metrics.Tokens.Total),
                current.AgentDays.Count(day => day.AgentId == agent.AgentId && day.Metrics.Tokens.Total > 0))).ToArray(),
            ReportGrouping.Model => current.Models.OrderByDescending(model => model.Metrics.Tokens.Total).Select(model => Row(
                model.ModelId.Value, model.AgentId.Value, model.Metrics,
                previous.Models.Where(item => item.AgentId == model.AgentId && item.ModelId == model.ModelId).Sum(item => item.Metrics.Tokens.Total),
                current.ModelDays.Count(day => day.AgentId == model.AgentId && day.ModelId == model.ModelId && day.Metrics.Tokens.Total > 0))).ToArray(),
            _ => current.Days.OrderByDescending(day => day.Date).Select(day => Row(
                day.Date.ToString("ddd d MMM", CultureInfo.CurrentCulture), null, day.Metrics, 0, 1) with { Delta = "", DeltaTone = ReportTone.Neutral }).ToArray(),
        };
    }

    /// <summary>
    /// The current cycle against earlier ones. The projection assumes the cycle keeps its average
    /// pace so far, which is the same reading a person makes from the curve.
    /// </summary>
    public static ReportPace? Pace(ReportQuotaInput quota, DateTimeOffset nowUtc, Func<string, string> text)
    {
        QuotaResetCycle? current = quota.Cycles.FirstOrDefault(cycle => cycle.IsCurrent);
        if (current is null) return null;
        TimeSpan duration = Duration(current);
        if (duration <= TimeSpan.Zero) return null;
        DateTimeOffset start = Window(current, nowUtc).Start;
        TimeSpan elapsed = nowUtc - start;
        double fraction = Math.Clamp(elapsed / duration, 0, 1);
        double used = (double)current.UsedPercent;
        TimeSpan untilReset = (current.ExpectedResetAtUtc ?? start + duration) - nowUtc;
        string resetsIn = Format(text("ReportResetsInFormat"), DurationText(untilReset));
        double? projected = fraction >= 0.02 && used < 100 ? used / fraction : null;
        (string verdict, ReportTone tone) = used >= 100
            ? (Format(text("ReportExhaustedFormat"), resetsIn), ReportTone.Critical)
            : projected >= 100
                ? (Format(text("ReportRunsOutFormat"), DurationText(elapsed * (100 / used) - elapsed), resetsIn), ReportTone.Warning)
                : projected is double value
                    ? (Format(text("ReportOnPaceFormat"), Math.Round(value), resetsIn), ReportTone.Neutral)
                    : (resetsIn, ReportTone.Neutral);
        ReportCurve Curve(QuotaResetCycle cycle) => new(quota.Observations.GetValueOrDefault(cycle.FromUtc, [])
            .Select(point => new ReportPoint(
                Math.Clamp((point.ObservedAtUtc - Window(cycle, nowUtc).Start) / Duration(cycle), 0, 1), (double)point.UsedPercent))
            .Prepend(new ReportPoint(0, 0)).ToArray());
        ReportCurve[] previous = quota.Cycles.Where(cycle => !cycle.IsCurrent).OrderByDescending(cycle => cycle.FromUtc)
            .Take(3).Select(Curve).ToArray();
        ReportCurve currentCurve = new(Curve(current).Points.Append(new ReportPoint(fraction, used)).ToArray());
        return new ReportPace(quota.Title, quota.ProviderId, UsageValueFormatter.PercentText(current.UsedPercent),
            verdict, tone, currentCurve, previous, projected);
    }

    /// <summary>Recent cycles with tokens per quota point when local tokens belong to this quota alone.</summary>
    public static IReadOnlyList<ReportReset> Resets(ReportQuotaInput quota, int count, DateTimeOffset nowUtc,
        Func<string, string> text)
    {
        QuotaResetCycle[] cycles = quota.Cycles.OrderByDescending(cycle => cycle.FromUtc).Take(count + 1).ToArray();
        var rows = new List<ReportReset>();
        for (int index = 0; index < Math.Min(count, cycles.Length); index++)
        {
            QuotaResetCycle cycle = cycles[index];
            QuotaResetCycle? before = index + 1 < cycles.Length ? cycles[index + 1] : null;
            long? tokens = quota.CycleTokens?.GetValueOrDefault(cycle.FromUtc);
            decimal points = Math.Max(1m, cycle.UsedPercent);
            decimal change = before is null ? 0m : cycle.UsedPercent - before.UsedPercent;
            rows.Add(new ReportReset(quota.Title,
                Format(text(cycle.IsCurrent ? "ReportCycleCurrentFormat" : "ReportCycleRangeFormat"),
                    Window(cycle, nowUtc).Start.ToLocalTime().ToString("d MMM", CultureInfo.CurrentCulture),
                    cycle.ToUtc.ToLocalTime().ToString("d MMM", CultureInfo.CurrentCulture)),
                UsageValueFormatter.PercentText(cycle.UsedPercent),
                tokens is long value ? UsageValueFormatter.CompactTokens(value) : text("ReportNotAttributable"),
                tokens is long spent ? UsageValueFormatter.CompactTokens(spent / (double)points) : "—",
                before is null || cycle.IsCurrent ? "—" : Format(text("ReportPointsFormat"), change.ToString("+0;-0;0", CultureInfo.CurrentCulture)),
                before is null || cycle.IsCurrent || change == 0 ? ReportTone.Neutral : change > 0 ? ReportTone.Up : ReportTone.Down));
        }
        return rows;
    }

    /// <summary>
    /// The span a cycle covers. A reset that happened while nothing was observing leaves the
    /// stored start behind, so the provider's schedule (expected reset minus the window) wins
    /// when it starts later.
    /// </summary>
    public static (DateTimeOffset Start, DateTimeOffset End) Window(QuotaResetCycle cycle, DateTimeOffset nowUtc)
    {
        DateTimeOffset end = cycle.IsCurrent ? nowUtc : cycle.ToUtc;
        DateTimeOffset scheduled = cycle.ExpectedResetAtUtc is { } reset ? reset - Duration(cycle) : cycle.FromUtc;
        DateTimeOffset start = scheduled > cycle.FromUtc && scheduled < end ? scheduled : cycle.FromUtc;
        return (start, end);
    }

    public static TimeSpan Duration(QuotaResetCycle cycle) => cycle.WindowDurationMinutes is decimal minutes && minutes > 0
        ? TimeSpan.FromMinutes((double)minutes)
        : (cycle.ExpectedResetAtUtc ?? cycle.ToUtc) - cycle.FromUtc;

    public static string AgentName(string agentId) =>
        ProviderModuleCatalog.Entries.FirstOrDefault(entry => entry.Id.Value == agentId)?.DisplayName ?? agentId;

    private static string Change(decimal value, decimal previous) =>
        previous == 0m ? "—" : ((value / previous - 1m) * 100m).ToString("+0'%';-0'%';0'%'", CultureInfo.CurrentCulture);

    private static ReportTone Tone(decimal value, decimal previous) =>
        previous == 0m || value == previous ? ReportTone.Neutral : value > previous ? ReportTone.Up : ReportTone.Down;

    private static string DurationText(TimeSpan span)
    {
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        return span.TotalDays >= 1
            ? string.Format(CultureInfo.CurrentCulture, "{0} d {1} h", (int)span.TotalDays, span.Hours)
            : span.TotalHours >= 1
                ? string.Format(CultureInfo.CurrentCulture, "{0} h {1} m", (int)span.TotalHours, span.Minutes)
                : string.Format(CultureInfo.CurrentCulture, "{0} m", Math.Max(1, span.Minutes));
    }

    private static string Format(string format, params object[] values) =>
        string.Format(CultureInfo.CurrentCulture, format, values);
}

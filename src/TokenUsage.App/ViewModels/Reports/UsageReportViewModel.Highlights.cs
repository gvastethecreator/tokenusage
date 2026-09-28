using System.Globalization;
using Microsoft.UI.Xaml;
using TokenUsage.Core.Automation;
using TokenUsage.Core.Usage;

namespace TokenUsage.App.ViewModels.Reports;

/// <summary>One mini column in a highlight sparkline; <see cref="IsRecent"/> marks the compared window.</summary>
public sealed record UsageReportSparkBar(double Height, bool IsRecent)
{
    public double Opacity => IsRecent ? 1 : 0.38;
}

/// <summary>What a highlight opens: the chart at a day, the day table, or a model's detail.</summary>
public enum UsageReportHighlightAction
{
    RevealDay,
    OpenDays,
    OpenModel,
}

/// <summary>
/// A plain-language fact about the selected period. Every value is arithmetic on the report
/// already on screen; highlights never infer causes, savings, or productivity. Each one opens
/// the evidence behind it.
/// </summary>
public sealed record UsageReportHighlight(string Glyph, string Label, string Value, string Detail,
    UsageReportHighlightAction Action, string Target)
{
    public IReadOnlyList<UsageReportSparkBar> Bars { get; init; } = [];
    public string ActionHint { get; init; } = string.Empty;
    public bool HasBars => Bars.Count > 0;
    public string AutomationName => Label + ": " + Value + ". " + Detail;
}

/// <summary>One segment of the token mix bar. A present segment keeps a visible sliver.</summary>
public sealed record UsageTokenMixPart(string Label, string ValueText, string ShareText, double Percent)
{
    public GridLength Width => new(Math.Max(0, Percent), GridUnitType.Star);
    public double MinWidth => Percent > 0 ? 3 : 0;
    public bool IsPresent => Percent > 0;
    public string AutomationName => Label + ": " + ValueText + ", " + ShareText;
}

public sealed partial class UsageReportViewModel
{
    private static readonly UsageTokenMixPart EmptyTokenMixPart = new(string.Empty, string.Empty, string.Empty, 0);

    public IReadOnlyList<UsageReportHighlight> Highlights { get; private set; } = [];
    public bool HasHighlights => Highlights.Count > 0;
    // The busiest-day highlight already tells the chart caption's story.
    public bool ShowTrendSummary => !HasHighlights;
    public UsageTokenMixPart TokenMixCacheRead { get; private set; } = EmptyTokenMixPart;
    public UsageTokenMixPart TokenMixCacheWrite { get; private set; } = EmptyTokenMixPart;
    public UsageTokenMixPart TokenMixInput { get; private set; } = EmptyTokenMixPart;
    public UsageTokenMixPart TokenMixOutput { get; private set; } = EmptyTokenMixPart;
    public UsageTokenMixPart TokenMixReasoning { get; private set; } = EmptyTokenMixPart;
    public string TokenMixReadText { get; private set; } = string.Empty;
    public string TokenMixWrittenText { get; private set; } = string.Empty;
    public string TokenMixRatioText { get; private set; } = string.Empty;
    public bool HasTokenMixRatio => TokenMixRatioText.Length > 0;

    private void RebuildHighlights(Func<UsageReportMetrics, decimal> value, Func<decimal, string> display)
    {
        var daily = Enumerable.Range(0, RangeDayCount)
            .Select(offset => StartDate.AddDays(offset))
            .Select(date => (Date: date, Metrics: _report.Days.FirstOrDefault(day => day.Date == date)?.Metrics))
            .Select(day => (day.Date, Amount: day.Metrics is null ? 0m : value(day.Metrics)))
            .ToArray();
        decimal total = daily.Sum(day => day.Amount);
        var highlights = new List<UsageReportHighlight>();
        if (total > 0)
        {
            var peak = daily.OrderByDescending(day => day.Amount).ThenBy(day => day.Date).First();
            highlights.Add(new("", GetString("UsageHighlightPeakLabel"),
                peak.Date.ToString("ddd d MMM", CultureInfo.CurrentCulture),
                string.Format(CultureInfo.CurrentCulture, GetString("UsageHighlightPeakFormat"),
                    display(peak.Amount), FormatPercent(peak.Amount / total)),
                UsageReportHighlightAction.RevealDay, peak.Date.ToString("O", CultureInfo.InvariantCulture)));

            int activeDays = daily.Count(day => day.Amount > 0);
            highlights.Add(new("", GetString("UsageHighlightAverageLabel"),
                display(total / activeDays),
                string.Format(CultureInfo.CurrentCulture, GetString("UsageHighlightAverageFormat"),
                    activeDays, daily.Length),
                UsageReportHighlightAction.OpenDays, string.Empty));

            if (daily.Length >= 14)
            {
                var window = daily[^14..];
                decimal before = window[..7].Sum(day => day.Amount);
                decimal recent = window[7..].Sum(day => day.Amount);
                decimal tallest = window.Max(day => day.Amount);
                // Past doubling, a multiple reads better than "+203%".
                string trend = before <= 0 ? GetString("UsageHighlightTrendNewFormat")
                    : recent >= before * 2 ? string.Format(CultureInfo.CurrentCulture, GetString("UsageHighlightTrendTimesFormat"),
                        (recent / before).ToString("0.#", CultureInfo.CurrentCulture))
                    : string.Format(CultureInfo.CurrentCulture, GetString(recent >= before
                        ? "UsageHighlightTrendUpFormat" : "UsageHighlightTrendDownFormat"),
                        FormatPercent(Math.Abs(recent - before) / before));
                highlights.Add(new(recent >= before ? "" : "", GetString("UsageHighlightRecentLabel"),
                    display(recent), trend, UsageReportHighlightAction.OpenDays, string.Empty)
                {
                    Bars = window.Select((day, index) => new UsageReportSparkBar(
                        tallest > 0 ? Math.Max(2, (double)(28 * day.Amount / tallest)) : 2, index >= 7)).ToArray(),
                });
            }

            UsageReportModelRow? leader = ModelRows.OrderByDescending(row => value(row.Metrics))
                .ThenBy(row => row.Id, StringComparer.Ordinal).FirstOrDefault();
            if (leader is not null && value(leader.Metrics) > 0)
            {
                highlights.Add(new("", GetString("UsageHighlightLeaderLabel"), leader.ModelName,
                    string.Format(CultureInfo.CurrentCulture, GetString("UsageHighlightLeaderFormat"),
                        leader.ProviderName, FormatPercent(value(leader.Metrics) / total)),
                    UsageReportHighlightAction.OpenModel, leader.Id));
            }
        }

        Highlights = highlights.Select(highlight => highlight with
        {
            ActionHint = GetString(highlight.Action switch
            {
                UsageReportHighlightAction.RevealDay => "UsageHighlightRevealDayHint",
                UsageReportHighlightAction.OpenModel => "UsageHighlightOpenModelHint",
                _ => "UsageHighlightOpenDaysHint",
            }),
        }).ToArray();
        RebuildTokenMix();
        foreach (string property in new[]
        {
            nameof(Highlights), nameof(HasHighlights), nameof(ShowTrendSummary), nameof(TokenMixCacheRead), nameof(TokenMixCacheWrite),
            nameof(TokenMixInput), nameof(TokenMixOutput), nameof(TokenMixReasoning), nameof(TokenMixReadText),
            nameof(TokenMixWrittenText), nameof(TokenMixRatioText), nameof(HasTokenMixRatio),
        }) OnPropertyChanged(property);
    }

    private void RebuildTokenMix()
    {
        TokenBreakdown tokens = _report.Totals.Tokens;
        long total = tokens.Total;
        UsageTokenMixPart Part(string key, long amount) => new(GetString(key), FormatTokens(amount),
            total > 0 ? FormatPercent((decimal)amount / total) : string.Empty,
            total > 0 ? 100d * amount / total : 0);
        TokenMixCacheRead = Part("UsageDashboardCacheRead", tokens.CacheRead);
        TokenMixCacheWrite = Part("UsageDashboardCacheWrite", tokens.CacheWrite);
        TokenMixInput = Part("UsageDashboardInput", tokens.Input);
        TokenMixOutput = Part("UsageDashboardOutput", tokens.Output);
        TokenMixReasoning = Part("UsageDashboardReasoning", tokens.Reasoning);
        long read = tokens.Input + tokens.CacheRead + tokens.CacheWrite;
        long written = tokens.Output + tokens.Reasoning;
        TokenMixReadText = string.Format(CultureInfo.CurrentCulture, GetString("UsageTokenMixReadFormat"), FormatTokens(read));
        TokenMixWrittenText = string.Format(CultureInfo.CurrentCulture, GetString("UsageTokenMixWrittenFormat"), FormatTokens(written));
        // A ratio below one is not a story worth a sentence; the segment widths already say it.
        TokenMixRatioText = written > 0 && read >= written
            ? string.Format(CultureInfo.CurrentCulture, GetString("UsageTokenMixRatioFormat"),
                (read / (double)written) switch
                {
                    >= 100 => Math.Round(read / (double)written).ToString("N0", CultureInfo.CurrentCulture),
                    var ratio => ratio.ToString("0.#", CultureInfo.CurrentCulture),
                })
            : string.Empty;
    }
}

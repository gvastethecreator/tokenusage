using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using TokenUsage.Core.Automation;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;

namespace TokenUsage.App.ViewModels.Reports;

/// <summary>One quota history to show: the panel's own (one reading per provider) or a selected switcher account.</summary>
public sealed record ReportQuotaSource(string Prefix, string HistoryPath, ProviderInstanceKey? Scope,
    IReadOnlySet<string> Providers, bool TokensAttributable);

/// <summary>
/// The usage report: how much was used against the previous period and why it changed, whether
/// each quota lasts to its reset, and how recent resets compare.
/// </summary>
public sealed partial class ReportViewModel : ObservableObject
{
    // The current cycle and three before it: enough to compare, short enough to scan.
    private const int Cycles = 4;
    private readonly string _databasePath;
    private readonly Func<IReadOnlyList<ReportQuotaSource>> _getQuotaSources;
    private readonly Func<string, string> _text;
    private readonly TimeProvider _clock;
    private UsageReport? _current;
    private UsageReport? _previous;
    private int _loadVersion;

    public ReportViewModel(string databasePath, Func<IReadOnlyList<ReportQuotaSource>> getQuotaSources,
        Func<string, string> text, TimeProvider? clock = null)
    {
        _databasePath = databasePath;
        _getQuotaSources = getQuotaSources;
        _text = text;
        _clock = clock ?? TimeProvider.System;
    }

    public IReadOnlyList<int> PeriodOptions { get; } = [7, 30, 90];

    [ObservableProperty] public partial int PeriodDays { get; set; } = 30;
    [ObservableProperty] public partial ReportGrouping Grouping { get; set; } = ReportGrouping.Agent;
    [ObservableProperty] public partial string Subtitle { get; private set; } = "";
    [ObservableProperty] public partial bool IsLoading { get; private set; }
    [ObservableProperty] public partial string? ErrorText { get; private set; }
    [ObservableProperty] public partial string? NoUsageText { get; private set; }
    [ObservableProperty] public partial IReadOnlyList<ReportKpi> Kpis { get; private set; } = [];
    [ObservableProperty] public partial IReadOnlyList<ReportPace> Paces { get; private set; } = [];
    [ObservableProperty] public partial ReportDaily? Daily { get; private set; }
    [ObservableProperty] public partial IReadOnlyList<ReportStep> Steps { get; private set; } = [];
    [ObservableProperty] public partial IReadOnlyList<ReportReset> Resets { get; private set; } = [];
    [ObservableProperty] public partial IReadOnlyList<ReportRow> Rows { get; private set; } = [];

    public bool HasPaces => Paces.Count > 0;
    public bool HasResets => Resets.Count > 0;
    public bool HasError => ErrorText is not null;
    public bool HasNoUsage => NoUsageText is not null;

    partial void OnPeriodDaysChanged(int value) => _ = LoadAsync();
    partial void OnGroupingChanged(ReportGrouping value) => UpdateRows();
    partial void OnPacesChanged(IReadOnlyList<ReportPace> value) => OnPropertyChanged(nameof(HasPaces));
    partial void OnResetsChanged(IReadOnlyList<ReportReset> value) => OnPropertyChanged(nameof(HasResets));
    partial void OnErrorTextChanged(string? value) => OnPropertyChanged(nameof(HasError));
    partial void OnNoUsageTextChanged(string? value) => OnPropertyChanged(nameof(HasNoUsage));

    public async Task LoadAsync()
    {
        int version = ++_loadVersion;
        IsLoading = true;
        try
        {
            DateTimeOffset now = _clock.GetUtcNow();
            DateOnly today = DateOnly.FromDateTime(now.ToLocalTime().DateTime);
            ReportPeriod period = ReportPeriod.LastDays(today, PeriodDays);
            ReportUsage usage = await ReportUsageReader.ReadAsync(_databasePath, period);
            (UsageReport current, UsageReport previous) = (usage.Current, usage.Previous);
            IReadOnlyList<ReportQuotaInput> quotas = await ReadQuotasAsync(
                usage.IsRecorded ? new UsageReportQuery(_databasePath) : null, now);
            if (version != _loadVersion) return;

            NoUsageText = usage.IsRecorded ? null : _text("ReportNoUsageYet");
            _current = current;
            _previous = previous;
            ReportPace[] paces = quotas.Select(quota => ReportProjection.Pace(quota, now, _text))
                .OfType<ReportPace>().ToArray();
            Subtitle = string.Format(CultureInfo.CurrentCulture, _text("ReportSubtitleFormat"),
                Range(period), Range(period.Previous));
            Kpis = ReportProjection.Kpis(current, previous, period, today,
                paces.Count(pace => pace.Tone is ReportTone.Warning or ReportTone.Critical), paces.Length, _text);
            Paces = paces;
            Daily = ReportProjection.Daily(current, previous, period);
            Steps = ReportProjection.Waterfall(current, previous, _text);
            Resets = quotas.SelectMany(quota => ReportProjection.Resets(quota, Cycles, now, _text)).ToArray();
            UpdateRows();
            ErrorText = null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            if (version != _loadVersion) return;
            NoUsageText = null;
            ErrorText = _text("ReportLoadFailed");
        }
        finally
        {
            if (version == _loadVersion) IsLoading = false;
        }
    }

    private void UpdateRows()
    {
        if (_current is not null && _previous is not null)
            Rows = ReportProjection.Rows(_current, _previous, Grouping, _text);
    }

    private async Task<IReadOnlyList<ReportQuotaInput>> ReadQuotasAsync(UsageReportQuery? query, DateTimeOffset now)
    {
        var inputs = new List<ReportQuotaInput>();
        foreach (ReportQuotaSource source in _getQuotaSources())
        {
            var store = new QuotaResetHistoryStore(source.HistoryPath, _clock, source.Scope);
            QuotaHistoryReadResult read = await store.ReadAsync();
            if (read.Availability != QuotaHistoryAvailability.Available || read.History is null) continue;
            foreach (string providerId in read.History.Windows.Select(window => window.ProviderId).Distinct()
                         .Where(source.Providers.Contains))
            {
                foreach (IGrouping<string, QuotaResetCycle> metric in QuotaResetCycleQuery.Build(read.History, providerId, now)
                             .GroupBy(cycle => cycle.MetricId))
                {
                    QuotaResetCycle[] cycles = metric.OrderBy(cycle => cycle.FromUtc).ToArray();
                    QuotaResetCycle? current = cycles.LastOrDefault(cycle => cycle.IsCurrent);
                    // Day-long and longer windows decide whether work can continue; five-hour
                    // windows refill within a session and would crowd the page.
                    if (current is null || ReportProjection.Duration(current) < TimeSpan.FromDays(1)) continue;
                    bool scoped = metric.Key.Contains(".scoped.", StringComparison.Ordinal);
                    if (scoped && current.UsedPercent < 1m) continue;
                    var observations = new Dictionary<DateTimeOffset, IReadOnlyList<QuotaObservation>>();
                    foreach (QuotaResetCycle cycle in cycles.TakeLast(Cycles))
                    {
                        (DateTimeOffset from, DateTimeOffset to) = ReportProjection.Window(cycle, now);
                        if (to <= from) continue;
                        observations[cycle.FromUtc] = (await store.Journal.ReadAsync(providerId, metric.Key, from, to))
                            .Observations;
                    }
                    Dictionary<DateTimeOffset, long>? tokens = null;
                    if (source.TokensAttributable && query is not null)
                    {
                        tokens = [];
                        foreach (QuotaResetCycle cycle in cycles.TakeLast(Cycles))
                        {
                            (DateTimeOffset from, DateTimeOffset to) = ReportProjection.Window(cycle, now);
                            if (to <= from) continue;
                            tokens[cycle.FromUtc] = (await query.ReadExactAsync(from, to, new AgentId(providerId)))
                                .Totals.Tokens.Total;
                        }
                    }
                    inputs.Add(new ReportQuotaInput(Title(source.Prefix, providerId, current, scoped), providerId,
                        cycles, observations, tokens));
                }
            }
        }
        return inputs;
    }

    private string Title(string prefix, string providerId, QuotaResetCycle cycle, bool scoped)
    {
        double days = Math.Round(ReportProjection.Duration(cycle).TotalDays);
        string window = days == 7 ? _text(scoped ? "ReportWindowWeeklyScoped" : "ReportWindowWeekly")
            : string.Format(CultureInfo.CurrentCulture, _text("ReportWindowDaysFormat"), days);
        string name = prefix.Length > 0 ? prefix : ReportProjection.AgentName(providerId);
        return name + " · " + window;
    }

    private static string Range(ReportPeriod period) =>
        period.From.ToString("d MMM", CultureInfo.CurrentCulture) + " – " + period.To.ToString("d MMM", CultureInfo.CurrentCulture);
}

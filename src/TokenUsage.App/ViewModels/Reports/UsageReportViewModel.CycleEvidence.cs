using System.Globalization;
using TokenUsage.Core.Automation;
using TokenUsage.Core.Usage;

namespace TokenUsage.App.ViewModels.Reports;

public sealed partial class UsageReportViewModel
{
    private readonly Dictionary<string, int> _cycleReadingCounts = new(StringComparer.Ordinal);
    private TimeSpan _matchedCycleDuration;
    private readonly Dictionary<string, decimal?> _cycleLevels = new(StringComparer.Ordinal);

    private TimeSpan SupportedCycleDuration(UsageReportResetCycleOption option)
    {
        QuotaResetCycle? cycle = QuotaResetCycleQuery.Build(_resetHistory, option.ProviderId, _clock.GetUtcNow())
            .FirstOrDefault(item => item.MetricId == option.MetricId && item.FromUtc == option.FromUtc);
        DateTimeOffset end = cycle is null ? option.FromUtc : cycle.ObservedAtUtc < option.ToUtc ? cycle.ObservedAtUtc : option.ToUtc;
        return end > option.FromUtc ? end - option.FromUtc : TimeSpan.Zero;
    }

    private async Task<UsageReport> ReadMatchedCycleAsync(UsageReportQuery query,
        UsageReportResetCycleOption option, CancellationToken token)
    {
        _cycleLevels[option.Id] = null;
        TimeSpan duration = SupportedCycleDuration(option);
        if (IsCompareCyclesAxis) duration = _matchedCycleDuration;
        else _matchedCycleDuration = duration;
        if (duration <= TimeSpan.Zero)
        {
            _cycleReadingCounts[option.Id] = 0;
            return UsageReportQuery.Build([]) with { IsExactInterval = true };
        }
        if (QuotaResetCycleQuery.Build(_resetHistory, option.ProviderId, _clock.GetUtcNow())
            .FirstOrDefault(item => item.MetricId == option.MetricId && item.FromUtc == option.FromUtc)?.HasObservedStart != true)
            AppendCycleEvidence("UsageComparisonInferredBoundary");
        DateTimeOffset end = option.FromUtc + duration;
        UsageReport report = await Task.Run(() => query.ReadExactAsync(option.FromUtc, end, new AgentId(option.ProviderId), token), token);
        if (_resetHistoryStore is not null)
        {
            try
            {
                QuotaObservationRange readings = await _resetHistoryStore.Journal.ReadAsync(option.ProviderId, option.MetricId, option.FromUtc, end, token);
                _cycleReadingCounts[option.Id] = readings.Observations.Count;
                // An earlier reading is not the level at the matched cutoff; never interpolate quota.
                _cycleLevels[option.Id] = readings.Observations.LastOrDefault(row => row.ObservedAtUtc == end)?.UsedPercent;
                if (readings.Truncated || readings.Observations.Count == 0
                    || readings.Observations[0].ObservedAtUtc > option.FromUtc
                    || readings.Observations[^1].ObservedAtUtc < end
                    || readings.Observations.Zip(readings.Observations.Skip(1), (a, b) => b.ObservedAtUtc - a.ObservedAtUtc)
                        .Any(gap => gap > TimeSpan.FromMinutes(10)))
                    AppendCycleEvidence("UsageComparisonCycleGaps");
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
            {
                AppendCycleEvidence("UsageComparisonJournalUnavailable");
                _cycleReadingCounts[option.Id] = 0;
            }
        }
        return report;
    }

    private void AppendCycleEvidence(string resourceKey)
    {
        string message = GetString(resourceKey);
        if (!_measurementEvidence.Contains(message, StringComparison.Ordinal)) _measurementEvidence += " " + message;
    }

    private string CycleEvidenceText() => string.Format(CultureInfo.CurrentCulture,
        GetString("UsageComparisonMultiCycleEvidence"), _matchedCycleDuration.ToString("g", CultureInfo.CurrentCulture))
        + " " + string.Join("; ", _cycleReports.Select(entry => entry.Label + " ["
            + entry.FromUtc.ToString("g", CultureInfo.CurrentCulture) + ", "
            + entry.ToUtc.ToString("g", CultureInfo.CurrentCulture) + ") UTC · "
            + string.Format(CultureInfo.CurrentCulture, GetString("UsageComparisonCycleCounts"),
                entry.Report.ExcludedTimingRecords, _cycleReadingCounts.GetValueOrDefault(entry.Id))));
}

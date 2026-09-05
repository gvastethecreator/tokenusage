using System.Globalization;
using TokenUsage.Core.Automation;

namespace TokenUsage.App.ViewModels.Reports;

public enum ReportSortColumn { Name, Date, Cost, ReportedCost, EstimatedCost, Tokens, Share, Coverage, Events, ActiveDays }

public readonly record struct ReportSortState(ReportSortColumn Column, bool Descending)
{
    public ReportSortState Toggle(ReportSortColumn column) =>
        new(column, Column == column ? !Descending : column != ReportSortColumn.Name);
}

public static class ReportDataProjection
{
    public static string ModelKey(string provider, string? modelProvider, string model) =>
        $"{provider}/{modelProvider}/{model}";

    public static string ModelName(string id) => id == "gpt-reserve" ? "Luna Reserve" : id;

    public static decimal? KnownCost(UsageReportMetrics metrics) =>
        metrics.UnavailableCostEventCount > 0
        && metrics.ReportedCostUsd is null && metrics.EstimatedCostUsd is null
            ? null : metrics.TotalCostUsd;

    public static IReadOnlyDictionary<string, int> ActiveModelDays(UsageReport report) =>
        report.ModelDays.Where(day => day.Metrics.Tokens.Total > 0)
            .GroupBy(day => ModelKey(day.AgentId.Value, day.ModelProviderId?.Value, day.ModelId.Value))
            .ToDictionary(group => group.Key, group => group.Select(day => day.Date).Distinct().Count());

    public static IReadOnlyDictionary<string, int> ActiveProviderDays(UsageReport report) =>
        report.AgentDays.Where(day => day.Metrics.Tokens.Total > 0)
            .GroupBy(day => day.AgentId.Value)
            .ToDictionary(group => group.Key, group => group.Select(day => day.Date).Distinct().Count());

    public static IEnumerable<T> Order<T>(IEnumerable<T> rows, ReportSortState state,
        Func<T, string> name, Func<T, decimal?> value)
    {
        if (state.Column == ReportSortColumn.Name)
            return state.Descending
                ? rows.OrderByDescending(name, StringComparer.CurrentCultureIgnoreCase)
                : rows.OrderBy(name, StringComparer.CurrentCultureIgnoreCase);

        // Nulls remain last in both directions; LINQ preserves the order of equal keys.
        IOrderedEnumerable<T> knownFirst = rows.OrderBy(row => value(row) is null);
        return state.Descending ? knownFirst.ThenByDescending(value) : knownFirst.ThenBy(value);
    }

    public static IReadOnlyDictionary<string, string> ModelShades(
        string providerColor, IEnumerable<(string Id, decimal? Total)> models)
    {
        var ranked = models.OrderBy(model => model.Total is null)
            .ThenByDescending(model => model.Total)
            .ThenBy(model => model.Id, StringComparer.Ordinal).ToArray();
        decimal[] totals = ranked.Where(model => model.Total is not null)
            .Select(model => model.Total!.Value).Distinct().ToArray();
        int ranks = Math.Max(1, totals.Length - 1 + (ranked.Any(model => model.Total is null) ? 1 : 0));
        int r = int.Parse(providerColor.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        int g = int.Parse(providerColor.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        int b = int.Parse(providerColor.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return ranked.ToDictionary(model => model.Id, model =>
        {
            int rank = model.Total is decimal total ? Array.IndexOf(totals, total) : totals.Length;
            double factor = 1 - 0.48 * rank / ranks;
            return $"#{(int)Math.Round(r * factor):X2}{(int)Math.Round(g * factor):X2}{(int)Math.Round(b * factor):X2}";
        });
    }
}

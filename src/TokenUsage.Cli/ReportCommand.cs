using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;

namespace TokenUsage.Cli;

public delegate Task<UsageReport> UsageReportReader(
    DateOnly fromInclusive,
    DateOnly toInclusive,
    AgentId? agentId,
    CancellationToken cancellationToken);

public static class ReportCommand
{
    public const string SchemaVersion = "tokenusage.report.v1";
    public const string UsageText =
        "Usage: tokenusage report [--days 1-3650 | --from YYYY-MM-DD --to YYYY-MM-DD] [--agent id] [--format human|json]";

    public const int SuccessExitCode = 0;
    public const int InvalidUsageExitCode = 2;
    public const int NoDataExitCode = 4;

    private const int DefaultDays = 30;
    private const int MaximumDays = 3650;
    private const int TopModelCount = 10;
    private const int TopDayCount = 5;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "The CLI boundary must redact all reader failures before writing stderr.")]
    public static async Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        TextWriter standardOutput,
        TextWriter standardError,
        UsageReportReader readReport,
        TimeProvider clock,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);
        ArgumentNullException.ThrowIfNull(readReport);
        ArgumentNullException.ThrowIfNull(clock);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryParse(arguments, out ReportOptions options, out string error))
        {
            await standardError.WriteLineAsync(error).ConfigureAwait(false);
            await standardError.WriteLineAsync(UsageText).ConfigureAwait(false);
            return InvalidUsageExitCode;
        }

        DateTimeOffset generatedAt = clock.GetUtcNow().ToUniversalTime();
        generatedAt = generatedAt.AddTicks(-(generatedAt.Ticks % TimeSpan.TicksPerSecond));
        DateOnly toInclusive = options.To
            ?? DateOnly.FromDateTime(generatedAt.UtcDateTime);
        DateOnly fromInclusive = options.From
            ?? toInclusive.AddDays(-(options.Days - 1));
        int days = toInclusive.DayNumber - fromInclusive.DayNumber + 1;

        UsageReport report;
        try
        {
            report = await readReport(
                fromInclusive,
                toInclusive,
                options.AgentId,
                cancellationToken).ConfigureAwait(false);
            ValidateReport(report);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            await standardError.WriteLineAsync("Unable to read local usage report.")
                .ConfigureAwait(false);
            return NoDataExitCode;
        }

        if (options.Format == OutputFormat.Json)
        {
            ReportDocument document = CreateDocument(
                generatedAt,
                fromInclusive,
                toInclusive,
                days,
                options.AgentId,
                report);
            await standardOutput.WriteLineAsync(JsonSerializer.Serialize(document, JsonOptions))
                .ConfigureAwait(false);
        }
        else
        {
            await WriteHumanAsync(
                standardOutput,
                fromInclusive,
                toInclusive,
                options.AgentId,
                report).ConfigureAwait(false);
        }

        return HasUsefulData(report.Totals) ? SuccessExitCode : NoDataExitCode;
    }

    private static bool TryParse(
        IReadOnlyList<string> arguments,
        out ReportOptions options,
        out string error)
    {
        int days = DefaultDays;
        DateOnly? from = null;
        DateOnly? to = null;
        AgentId? agentId = null;
        OutputFormat format = OutputFormat.Human;
        bool hasDays = false;
        bool hasFrom = false;
        bool hasTo = false;
        bool hasAgent = false;
        bool hasFormat = false;

        for (int index = 0; index < arguments.Count; index++)
        {
            switch (arguments[index])
            {
                case "--days":
                    if (hasDays)
                    {
                        return Invalid("Option '--days' can be set only once.", out options, out error);
                    }

                    if (++index >= arguments.Count
                        || !int.TryParse(
                            arguments[index],
                            NumberStyles.None,
                            CultureInfo.InvariantCulture,
                            out days)
                        || days is < 1 or > MaximumDays)
                    {
                        return Invalid(
                            "Option '--days' needs an integer from 1 through 3650.",
                            out options,
                            out error);
                    }

                    hasDays = true;
                    break;

                case "--from":
                    if (hasFrom)
                    {
                        return Invalid("Option '--from' can be set only once.", out options, out error);
                    }

                    if (++index >= arguments.Count || !TryParseDate(arguments[index], out from))
                    {
                        return Invalid(
                            "Option '--from' needs a date in YYYY-MM-DD form.",
                            out options,
                            out error);
                    }

                    hasFrom = true;
                    break;

                case "--to":
                    if (hasTo)
                    {
                        return Invalid("Option '--to' can be set only once.", out options, out error);
                    }

                    if (++index >= arguments.Count || !TryParseDate(arguments[index], out to))
                    {
                        return Invalid(
                            "Option '--to' needs a date in YYYY-MM-DD form.",
                            out options,
                            out error);
                    }

                    hasTo = true;
                    break;

                case "--agent":
                    if (hasAgent)
                    {
                        return Invalid("Option '--agent' can be set only once.", out options, out error);
                    }

                    if (++index >= arguments.Count
                        || !TryParseAgent(arguments[index], out agentId))
                    {
                        return Invalid(
                            "Option '--agent' needs a valid local agent ID.",
                            out options,
                            out error);
                    }

                    hasAgent = true;
                    break;

                case "--format":
                    if (hasFormat)
                    {
                        return Invalid("Option '--format' can be set only once.", out options, out error);
                    }

                    if (++index >= arguments.Count
                        || !TryParseFormat(arguments[index], out format))
                    {
                        return Invalid(
                            "Option '--format' must be 'human' or 'json'.",
                            out options,
                            out error);
                    }

                    hasFormat = true;
                    break;

                default:
                    return Invalid("Unknown report argument.", out options, out error);
            }
        }

        if (hasDays && (hasFrom || hasTo))
        {
            return Invalid(
                "Option '--days' cannot be combined with '--from' or '--to'.",
                out options,
                out error);
        }

        if (hasFrom != hasTo)
        {
            return Invalid(
                "Options '--from' and '--to' must be used together.",
                out options,
                out error);
        }

        if (from is DateOnly fromDate && to is DateOnly toDate)
        {
            int rangeDays = toDate.DayNumber - fromDate.DayNumber + 1;
            if (rangeDays is < 1 or > MaximumDays)
            {
                return Invalid(
                    "The report range must contain from 1 through 3650 days.",
                    out options,
                    out error);
            }
        }

        options = new ReportOptions(days, from, to, agentId, format);
        error = string.Empty;
        return true;
    }

    private static bool TryParseDate(string value, out DateOnly? date)
    {
        bool parsed = DateOnly.TryParseExact(
            value,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out DateOnly result);
        date = parsed ? result : null;
        return parsed;
    }

    private static bool TryParseAgent(string value, out AgentId? agentId)
    {
        try
        {
            agentId = new AgentId(value);
            return true;
        }
        catch (ArgumentException)
        {
            agentId = null;
            return false;
        }
    }

    private static bool TryParseFormat(string value, out OutputFormat format)
    {
        if (string.Equals(value, "human", StringComparison.Ordinal))
        {
            format = OutputFormat.Human;
            return true;
        }

        if (string.Equals(value, "json", StringComparison.Ordinal))
        {
            format = OutputFormat.Json;
            return true;
        }

        format = default;
        return false;
    }

    private static bool Invalid(
        string message,
        out ReportOptions options,
        out string error)
    {
        options = default;
        error = message;
        return false;
    }

    private static void ValidateReport(UsageReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        ValidateMetrics(report.Totals);
        foreach (UsageAgentReport item in report.Agents)
        {
            ArgumentNullException.ThrowIfNull(item);
            ValidateMetrics(item.Metrics);
        }

        foreach (UsageModelReport item in report.Models)
        {
            ArgumentNullException.ThrowIfNull(item);
            ValidateMetrics(item.Metrics);
        }

        foreach (UsageDayReport item in report.Days)
        {
            ArgumentNullException.ThrowIfNull(item);
            ValidateMetrics(item.Metrics);
        }
    }

    private static void ValidateMetrics(UsageReportMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(metrics.Tokens);
        if (metrics.EventCount < 0
            || metrics.UnpricedTokens < 0
            || metrics.UnavailableCostEventCount < 0
            || metrics.ReportedCostUsd < 0
            || metrics.EstimatedCostUsd < 0
            || metrics.UnpricedTokens > metrics.Tokens.Total
            || metrics.UnavailableCostEventCount > metrics.EventCount
            || !Enum.IsDefined(metrics.Coverage))
        {
            throw new InvalidDataException("The local usage report contains invalid values.");
        }
    }

    private static bool HasUsefulData(UsageReportMetrics metrics) =>
        metrics.EventCount != 0
        || metrics.Tokens.Total != 0
        || metrics.ReportedCostUsd is not null
        || metrics.EstimatedCostUsd is not null;

    private static ReportDocument CreateDocument(
        DateTimeOffset generatedAt,
        DateOnly fromInclusive,
        DateOnly toInclusive,
        int days,
        AgentId? agentId,
        UsageReport report) =>
        new(
            SchemaVersion,
            generatedAt,
            new ReportRange(fromInclusive, toInclusive, days),
            new ReportFilter(agentId?.Value),
            CreateMetrics(report.Totals),
            report.Agents.Select(item => new AgentDocument(
                item.AgentId.Value,
                CreateMetrics(item.Metrics))).ToArray(),
            report.Models.Select(item => new ModelDocument(
                item.AgentId.Value,
                item.ModelProviderId?.Value,
                item.ModelId.Value,
                CreateMetrics(item.Metrics))).ToArray(),
            report.Days
                .OrderByDescending(item => item.Metrics.TotalCostUsd)
                .ThenByDescending(item => item.Metrics.Tokens.Total)
                .ThenBy(item => item.Date)
                .Take(TopDayCount)
                .Select(CreateDay)
                .ToArray(),
            report.Days.Select(CreateDay).ToArray());

    private static MetricsDocument CreateMetrics(UsageReportMetrics metrics) =>
        new(
            metrics.EventCount,
            new TokenDocument(
                metrics.Tokens.Input,
                metrics.Tokens.Output,
                metrics.Tokens.Reasoning,
                metrics.Tokens.CacheRead,
                metrics.Tokens.CacheWrite,
                metrics.Tokens.Total,
                metrics.UnpricedTokens),
            new CostDocument(
                metrics.TotalCostUsd,
                metrics.ReportedCostUsd,
                metrics.EstimatedCostUsd),
            metrics.UnavailableCostEventCount,
            CoverageName(metrics.Coverage),
            metrics.PriceCoveragePercent);

    private static DayDocument CreateDay(UsageDayReport item) =>
        new(item.Date, CreateMetrics(item.Metrics));

    private static async Task WriteHumanAsync(
        TextWriter output,
        DateOnly fromInclusive,
        DateOnly toInclusive,
        AgentId? agentId,
        UsageReport report)
    {
        await output.WriteLineAsync("TokenUsage report").ConfigureAwait(false);
        await output.WriteLineAsync(string.Create(
            CultureInfo.InvariantCulture,
            $"Range: {fromInclusive:yyyy-MM-dd} to {toInclusive:yyyy-MM-dd}"))
            .ConfigureAwait(false);
        if (agentId is not null)
        {
            await output.WriteLineAsync($"Agent: {agentId.Value}").ConfigureAwait(false);
        }

        await output.WriteLineAsync().ConfigureAwait(false);
        await output.WriteLineAsync("Totals").ConfigureAwait(false);
        await WriteMetricLinesAsync(output, report.Totals).ConfigureAwait(false);

        await output.WriteLineAsync().ConfigureAwait(false);
        await output.WriteLineAsync("Tokens").ConfigureAwait(false);
        await WriteTokenLineAsync(output, "Input", report.Totals.Tokens.Input, report.Totals.Tokens.Total)
            .ConfigureAwait(false);
        await WriteTokenLineAsync(output, "Output", report.Totals.Tokens.Output, report.Totals.Tokens.Total)
            .ConfigureAwait(false);
        await WriteTokenLineAsync(output, "Reasoning", report.Totals.Tokens.Reasoning, report.Totals.Tokens.Total)
            .ConfigureAwait(false);
        await WriteTokenLineAsync(output, "Cache read", report.Totals.Tokens.CacheRead, report.Totals.Tokens.Total)
            .ConfigureAwait(false);
        await WriteTokenLineAsync(output, "Cache write", report.Totals.Tokens.CacheWrite, report.Totals.Tokens.Total)
            .ConfigureAwait(false);

        if (report.Agents.Count > 0)
        {
            await output.WriteLineAsync().ConfigureAwait(false);
            await output.WriteLineAsync("By agent").ConfigureAwait(false);
            await output.WriteLineAsync("  Agent                 Tokens       Cost USD  Coverage")
                .ConfigureAwait(false);
            foreach (UsageAgentReport item in report.Agents)
            {
                await output.WriteLineAsync(FormatRow(
                    item.AgentId.Value,
                    item.Metrics.Tokens.Total,
                    item.Metrics.TotalCostUsd,
                    item.Metrics.PriceCoveragePercent)).ConfigureAwait(false);
            }
        }

        if (report.Models.Count > 0)
        {
            await output.WriteLineAsync().ConfigureAwait(false);
            await output.WriteLineAsync($"Top models (up to {TopModelCount})").ConfigureAwait(false);
            await output.WriteLineAsync("  Agent/model                        Tokens       Cost USD  Coverage")
                .ConfigureAwait(false);
            foreach (UsageModelReport item in report.Models.Take(TopModelCount))
            {
                await output.WriteLineAsync(FormatRow(
                    $"{item.AgentId.Value}/{item.ModelId.Value}",
                    item.Metrics.Tokens.Total,
                    item.Metrics.TotalCostUsd,
                    item.Metrics.PriceCoveragePercent,
                    nameWidth: 34)).ConfigureAwait(false);
            }
        }

        UsageDayReport[] topDays = report.Days
            .OrderByDescending(item => item.Metrics.TotalCostUsd)
            .ThenByDescending(item => item.Metrics.Tokens.Total)
            .ThenBy(item => item.Date)
            .Take(TopDayCount)
            .ToArray();
        if (topDays.Length > 0)
        {
            await output.WriteLineAsync().ConfigureAwait(false);
            await output.WriteLineAsync("Highest-cost days").ConfigureAwait(false);
            await output.WriteLineAsync("  Date                   Tokens       Cost USD  Coverage")
                .ConfigureAwait(false);
            foreach (UsageDayReport item in topDays)
            {
                await output.WriteLineAsync(FormatRow(
                    item.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    item.Metrics.Tokens.Total,
                    item.Metrics.TotalCostUsd,
                    item.Metrics.PriceCoveragePercent)).ConfigureAwait(false);
            }

            await output.WriteLineAsync().ConfigureAwait(false);
            await output.WriteLineAsync("Daily").ConfigureAwait(false);
            await output.WriteLineAsync("  Date                   Tokens       Cost USD  Coverage")
                .ConfigureAwait(false);
            foreach (UsageDayReport item in report.Days)
            {
                await output.WriteLineAsync(FormatRow(
                    item.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    item.Metrics.Tokens.Total,
                    item.Metrics.TotalCostUsd,
                    item.Metrics.PriceCoveragePercent)).ConfigureAwait(false);
            }
        }
    }

    private static async Task WriteMetricLinesAsync(
        TextWriter output,
        UsageReportMetrics metrics)
    {
        await output.WriteLineAsync($"  Cost USD: {FormatCost(metrics.TotalCostUsd)}")
            .ConfigureAwait(false);
        await output.WriteLineAsync($"  Reported USD: {FormatCost(metrics.ReportedCostUsd)}")
            .ConfigureAwait(false);
        await output.WriteLineAsync($"  Estimated USD: {FormatCost(metrics.EstimatedCostUsd)}")
            .ConfigureAwait(false);
        await output.WriteLineAsync(string.Create(
            CultureInfo.InvariantCulture,
            $"  Tokens: {metrics.Tokens.Total:N0}"))
            .ConfigureAwait(false);
        await output.WriteLineAsync(string.Create(
            CultureInfo.InvariantCulture,
            $"  Events: {metrics.EventCount:N0}"))
            .ConfigureAwait(false);
        await output.WriteLineAsync(string.Create(
            CultureInfo.InvariantCulture,
            $"  Price coverage: {metrics.PriceCoveragePercent:0.0}%"))
            .ConfigureAwait(false);
        await output.WriteLineAsync(string.Create(
            CultureInfo.InvariantCulture,
            $"  Unpriced tokens: {metrics.UnpricedTokens:N0}"))
            .ConfigureAwait(false);
        await output.WriteLineAsync($"  Source coverage: {CoverageName(metrics.Coverage)}")
            .ConfigureAwait(false);
    }

    private static async Task WriteTokenLineAsync(
        TextWriter output,
        string label,
        long tokens,
        long totalTokens)
    {
        decimal share = totalTokens == 0
            ? 0m
            : decimal.Round(tokens * 100m / totalTokens, 1, MidpointRounding.AwayFromZero);
        await output.WriteLineAsync(string.Create(
            CultureInfo.InvariantCulture,
            $"  {label,-12} {tokens,15:N0}  {share,5:0.0}%"))
            .ConfigureAwait(false);
    }

    private static string FormatRow(
        string name,
        long tokens,
        decimal cost,
        decimal coverage,
        int nameWidth = 20) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"  {name.PadRight(nameWidth)} {tokens,12:N0} {cost,14:0.######} {coverage,8:0.0}%");

    private static string FormatCost(decimal? value) => CliCostText.Format(value);

    private static string CoverageName(CoverageKind coverage) => coverage switch
    {
        CoverageKind.Complete => "complete",
        CoverageKind.Partial => "partial",
        CoverageKind.SummaryOnly => "summary-only",
        CoverageKind.Unpriced => "unpriced",
        _ => throw new ArgumentOutOfRangeException(nameof(coverage)),
    };

    private readonly record struct ReportOptions(
        int Days,
        DateOnly? From,
        DateOnly? To,
        AgentId? AgentId,
        OutputFormat Format);

    private enum OutputFormat
    {
        Human,
        Json,
    }

    private sealed record ReportDocument(
        string SchemaVersion,
        DateTimeOffset GeneratedAt,
        ReportRange Range,
        ReportFilter Filter,
        MetricsDocument Totals,
        IReadOnlyList<AgentDocument> ByAgent,
        IReadOnlyList<ModelDocument> Models,
        IReadOnlyList<DayDocument> HighestCostDays,
        IReadOnlyList<DayDocument> Daily);

    private sealed record ReportRange(DateOnly From, DateOnly To, int Days);

    private sealed record ReportFilter(string? Agent);

    private sealed record AgentDocument(string Agent, MetricsDocument Metrics);

    private sealed record ModelDocument(
        string Agent,
        string? Provider,
        string Model,
        MetricsDocument Metrics);

    private sealed record DayDocument(DateOnly Date, MetricsDocument Metrics);

    private sealed record MetricsDocument(
        int Events,
        TokenDocument Tokens,
        CostDocument CostUsd,
        int UnavailableCostEvents,
        string Coverage,
        decimal PriceCoveragePercent);

    private sealed record TokenDocument(
        long Input,
        long Output,
        long Reasoning,
        long CacheRead,
        long CacheWrite,
        long Total,
        long Unpriced);

    private sealed record CostDocument(
        decimal Total,
        decimal? Reported,
        decimal? Estimated);
}

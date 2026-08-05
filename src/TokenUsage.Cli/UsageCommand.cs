using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;

namespace TokenUsage.Cli;

public delegate Task<UsageCliSummary> UsageSummaryReader(
    DateOnly fromInclusive,
    DateOnly toInclusive,
    CancellationToken cancellationToken);

public static class UsageCommand
{
    public const string SchemaVersion = "tokenusage.usage.v1";
    public const string UsageText =
        "Usage: tokenusage usage [--days 1-3650] [--format human|json]";

    public const int SuccessExitCode = 0;
    public const int InvalidUsageExitCode = 2;
    public const int NoDataExitCode = 4;

    private const int DefaultDays = 30;
    private const int MaximumDays = 3650;

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
        UsageSummaryReader readSummary,
        TimeProvider clock,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);
        ArgumentNullException.ThrowIfNull(readSummary);
        ArgumentNullException.ThrowIfNull(clock);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryParse(arguments, out UsageOptions options, out string error))
        {
            await standardError.WriteLineAsync(error).ConfigureAwait(false);
            await standardError.WriteLineAsync(UsageText).ConfigureAwait(false);
            return InvalidUsageExitCode;
        }

        DateTimeOffset generatedAt = clock.GetUtcNow().ToUniversalTime();
        generatedAt = generatedAt.AddTicks(-(generatedAt.Ticks % TimeSpan.TicksPerSecond));
        DateOnly toInclusive = DateOnly.FromDateTime(generatedAt.UtcDateTime);
        DateOnly fromInclusive = toInclusive.AddDays(-(options.Days - 1));

        UsageCliSummary summary;
        try
        {
            summary = await readSummary(
                fromInclusive,
                toInclusive,
                cancellationToken).ConfigureAwait(false);
            ValidateSummary(summary);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            await standardError.WriteLineAsync("Unable to read local usage data.")
                .ConfigureAwait(false);
            return NoDataExitCode;
        }

        if (options.Format == OutputFormat.Json)
        {
            UsageDocument document = CreateDocument(
                generatedAt,
                fromInclusive,
                toInclusive,
                options.Days,
                summary);
            await standardOutput.WriteLineAsync(JsonSerializer.Serialize(document, JsonOptions))
                .ConfigureAwait(false);
        }
        else
        {
            await WriteHumanAsync(
                standardOutput,
                fromInclusive,
                toInclusive,
                summary).ConfigureAwait(false);
        }

        return HasUsefulData(summary) ? SuccessExitCode : NoDataExitCode;
    }

    private static bool TryParse(
        IReadOnlyList<string> arguments,
        out UsageOptions options,
        out string error)
    {
        int days = DefaultDays;
        OutputFormat format = OutputFormat.Human;
        bool hasDays = false;
        bool hasFormat = false;

        for (int index = 0; index < arguments.Count; index++)
        {
            string argument = arguments[index];
            switch (argument)
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
                    return Invalid(
                        "Unknown usage argument.",
                        out options,
                        out error);
            }
        }

        options = new UsageOptions(days, format);
        error = string.Empty;
        return true;
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
        out UsageOptions options,
        out string error)
    {
        options = default;
        error = message;
        return false;
    }

    private static void ValidateSummary(UsageCliSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        if (summary.EventCount < 0
            || summary.TotalTokens < 0
            || summary.UnpricedTokens < 0
            || summary.ReportedCostUsd < 0
            || summary.EstimatedCostUsd < 0)
        {
            throw new InvalidDataException("The local usage summary contains negative values.");
        }
    }

    private static bool HasUsefulData(UsageCliSummary summary) =>
        summary.EventCount != 0
        || summary.TotalTokens != 0
        || summary.ReportedCostUsd is not null
        || summary.EstimatedCostUsd is not null;

    private static UsageDocument CreateDocument(
        DateTimeOffset generatedAt,
        DateOnly fromInclusive,
        DateOnly toInclusive,
        int days,
        UsageCliSummary summary) =>
        new(
            SchemaVersion,
            generatedAt,
            new UsageRange(fromInclusive, toInclusive, days),
            summary.EventCount,
            new UsageTokens(summary.TotalTokens, summary.UnpricedTokens),
            new UsageCost(summary.ReportedCostUsd, summary.EstimatedCostUsd));

    private static async Task WriteHumanAsync(
        TextWriter output,
        DateOnly fromInclusive,
        DateOnly toInclusive,
        UsageCliSummary summary)
    {
        await output.WriteLineAsync(string.Create(
            CultureInfo.InvariantCulture,
            $"Range: {fromInclusive:yyyy-MM-dd} to {toInclusive:yyyy-MM-dd}"))
            .ConfigureAwait(false);
        await output.WriteLineAsync(string.Create(
            CultureInfo.InvariantCulture,
            $"Events: {summary.EventCount:N0}"))
            .ConfigureAwait(false);
        await output.WriteLineAsync(string.Create(
            CultureInfo.InvariantCulture,
            $"Total tokens: {summary.TotalTokens:N0}"))
            .ConfigureAwait(false);
        await output.WriteLineAsync(string.Create(
            CultureInfo.InvariantCulture,
            $"Unpriced tokens: {summary.UnpricedTokens:N0}"))
            .ConfigureAwait(false);
        await output.WriteLineAsync(
            $"Reported USD: {FormatCost(summary.ReportedCostUsd)}")
            .ConfigureAwait(false);
        await output.WriteLineAsync(
            $"Estimated USD: {FormatCost(summary.EstimatedCostUsd)}")
            .ConfigureAwait(false);
    }

    private static string FormatCost(decimal? value) => value is null
        ? "unavailable"
        : value.Value.ToString("0.######", CultureInfo.InvariantCulture);

    private readonly record struct UsageOptions(int Days, OutputFormat Format);

    private enum OutputFormat
    {
        Human,
        Json,
    }

    private sealed record UsageDocument(
        string SchemaVersion,
        DateTimeOffset GeneratedAt,
        UsageRange Range,
        int Events,
        UsageTokens Tokens,
        UsageCost CostUsd);

    private sealed record UsageRange(DateOnly From, DateOnly To, int Days);

    private sealed record UsageTokens(long Total, long Unpriced);

    private sealed record UsageCost(decimal? Reported, decimal? Estimated);
}

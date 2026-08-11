using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using TokenUsage.Core.Usage;

namespace TokenUsage.Cli;

public delegate Task<LocalUsageRefreshResult> LocalUsageRefresher(
    CancellationToken cancellationToken);

public static class RefreshCommand
{
    public const string SchemaVersion = "tokenusage.refresh.v1";
    public const string UsageText =
        "Usage: tokenusage refresh [--format human|json]";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "The CLI boundary must redact all refresh failures before writing stderr.")]
    public static async Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        TextWriter standardOutput,
        TextWriter standardError,
        LocalUsageRefresher refresh,
        TimeProvider clock,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);
        ArgumentNullException.ThrowIfNull(refresh);
        ArgumentNullException.ThrowIfNull(clock);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryParse(arguments, out OutputFormat format, out string error))
        {
            await standardError.WriteLineAsync(error).ConfigureAwait(false);
            await standardError.WriteLineAsync(UsageText).ConfigureAwait(false);
            return UsageCommand.InvalidUsageExitCode;
        }

        LocalUsageRefreshResult result;
        try
        {
            result = await refresh(cancellationToken).ConfigureAwait(false);
            Validate(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            await standardError.WriteLineAsync("Unable to refresh local usage data.")
                .ConfigureAwait(false);
            return UsageCommand.NoDataExitCode;
        }

        DateTimeOffset generatedAt = clock.GetUtcNow().ToUniversalTime();
        generatedAt = generatedAt.AddTicks(-(generatedAt.Ticks % TimeSpan.TicksPerSecond));
        if (format == OutputFormat.Json)
        {
            RefreshDocument document = CreateDocument(generatedAt, result);
            await standardOutput.WriteLineAsync(JsonSerializer.Serialize(document, JsonOptions))
                .ConfigureAwait(false);
        }
        else
        {
            await WriteHumanAsync(standardOutput, result).ConfigureAwait(false);
        }

        return result.Rollups.Count > 0
            ? UsageCommand.SuccessExitCode
            : UsageCommand.NoDataExitCode;
    }

    private static bool TryParse(
        IReadOnlyList<string> arguments,
        out OutputFormat format,
        out string error)
    {
        format = OutputFormat.Human;
        if (arguments.Count == 0)
        {
            error = string.Empty;
            return true;
        }

        if (arguments.Count == 2
            && string.Equals(arguments[0], "--format", StringComparison.Ordinal)
            && TryParseFormat(arguments[1], out format))
        {
            error = string.Empty;
            return true;
        }

        error = arguments.Count > 0
            && string.Equals(arguments[0], "--format", StringComparison.Ordinal)
            ? "Option '--format' must be set once to 'human' or 'json'."
            : "Unknown refresh argument.";
        return false;
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

    private static void Validate(LocalUsageRefreshResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Rollups.Any(rollup => rollup is null)
            || result.SourceDiagnostics.Any(diagnostic => diagnostic is null))
        {
            throw new InvalidDataException("The refresh result contains null entries.");
        }
    }

    private static RefreshDocument CreateDocument(
        DateTimeOffset generatedAt,
        LocalUsageRefreshResult result) =>
        new(
            SchemaVersion,
            generatedAt,
            StatusName(result.OverallStatus),
            new RefreshRange(result.FromInclusive, result.ToInclusive),
            result.Rollups.Count,
            result.SourceDiagnostics.Select(diagnostic => new ProviderDocument(
                diagnostic.AgentId.Value,
                StatusName(diagnostic.Status),
                IssueName(diagnostic.Issue),
                diagnostic.RetainsLastReliableSnapshot)).ToArray());

    private static async Task WriteHumanAsync(
        TextWriter output,
        LocalUsageRefreshResult result)
    {
        await output.WriteLineAsync($"Local usage refresh: {StatusName(result.OverallStatus)}")
            .ConfigureAwait(false);
        await output.WriteLineAsync(string.Create(
            CultureInfo.InvariantCulture,
            $"Range: {result.FromInclusive:yyyy-MM-dd} to {result.ToInclusive:yyyy-MM-dd}"))
            .ConfigureAwait(false);
        await output.WriteLineAsync(string.Create(
            CultureInfo.InvariantCulture,
            $"Daily rollups: {result.Rollups.Count:N0}"))
            .ConfigureAwait(false);
        foreach (UsageSourceDiagnostic diagnostic in result.SourceDiagnostics)
        {
            await output.WriteLineAsync(
                $"  {diagnostic.AgentId.Value}: {StatusName(diagnostic.Status)} ({IssueName(diagnostic.Issue)})")
                .ConfigureAwait(false);
        }
    }

    private static string StatusName(UsageSourceReadStatus status) => status switch
    {
        UsageSourceReadStatus.Complete => "complete",
        UsageSourceReadStatus.Partial => "partial",
        UsageSourceReadStatus.NoData => "no-data",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    private static string IssueName(UsageSourceIssueKind issue) => issue switch
    {
        UsageSourceIssueKind.None => "none",
        UsageSourceIssueKind.RootUnavailable => "root-unavailable",
        UsageSourceIssueKind.Empty => "empty",
        UsageSourceIssueKind.PartialScan => "partial-scan",
        UsageSourceIssueKind.AccessBlocked => "access-blocked",
        UsageSourceIssueKind.UnsupportedSchema => "unsupported-schema",
        _ => throw new ArgumentOutOfRangeException(nameof(issue)),
    };

    private enum OutputFormat
    {
        Human,
        Json,
    }

    private sealed record RefreshDocument(
        string SchemaVersion,
        DateTimeOffset GeneratedAt,
        string Status,
        RefreshRange Range,
        int Rollups,
        IReadOnlyList<ProviderDocument> Providers);

    private sealed record RefreshRange(DateOnly From, DateOnly To);

    private sealed record ProviderDocument(
        string Agent,
        string Status,
        string Issue,
        bool RetainsLastReliableSnapshot);
}

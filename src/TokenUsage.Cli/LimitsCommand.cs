using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using WOpenUsage.Core.Providers;

namespace WOpenUsage.Cli;

public delegate Task<IReadOnlyList<ProviderSnapshot>> LimitsSnapshotReader(
    string? providerId,
    bool forceRefresh,
    CancellationToken cancellationToken);

public static class LimitsCommand
{
    public const string UsageText =
        "Usage: wusage limits [provider-id] [--force] [--format human|json]";

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "The CLI boundary must redact all cache and refresh failures.")]
    public static async Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        TextWriter standardOutput,
        TextWriter standardError,
        LimitsSnapshotReader readSnapshots,
        TimeProvider clock,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);
        ArgumentNullException.ThrowIfNull(readSnapshots);
        ArgumentNullException.ThrowIfNull(clock);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryParse(arguments, out LimitsOptions options, out string error))
        {
            await standardError.WriteLineAsync(error).ConfigureAwait(false);
            await standardError.WriteLineAsync(UsageText).ConfigureAwait(false);
            return UsageCommand.InvalidUsageExitCode;
        }

        IReadOnlyList<ProviderSnapshot> snapshots;
        try
        {
            snapshots = await readSnapshots(
                    options.ProviderId,
                    options.ForceRefresh,
                    cancellationToken)
                .ConfigureAwait(false);
            ArgumentNullException.ThrowIfNull(snapshots);
            if (snapshots.Any(snapshot => snapshot is null))
            {
                throw new InvalidDataException("The snapshot reader returned a null value.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            await standardError.WriteLineAsync("Unable to read provider limits.")
                .ConfigureAwait(false);
            return UsageCommand.NoDataExitCode;
        }

        ProviderSnapshot[] selected = snapshots
            .Where(snapshot => options.ProviderId is null
                || string.Equals(
                    snapshot.ProviderId.Value,
                    options.ProviderId,
                    StringComparison.Ordinal))
            .OrderBy(snapshot => snapshot.ProviderId.Value, StringComparer.Ordinal)
            .ToArray();
        DateTimeOffset generatedAt = TruncateToSecond(clock.GetUtcNow().ToUniversalTime());
        var outputClock = new FixedTimeProvider(generatedAt);

        if (options.Format == OutputFormat.Json)
        {
            await standardOutput.WriteLineAsync(
                LimitsDocument.Serialize(generatedAt, selected, outputClock)).ConfigureAwait(false);
        }
        else
        {
            await WriteHumanAsync(standardOutput, selected, outputClock).ConfigureAwait(false);
        }

        return !selected.Any(snapshot => snapshot.Metrics.Count > 0)
            ? UsageCommand.NoDataExitCode
            : UsageCommand.SuccessExitCode;
    }

    private static bool TryParse(
        IReadOnlyList<string> arguments,
        out LimitsOptions options,
        out string error)
    {
        string? providerId = null;
        bool forceRefresh = false;
        bool hasForce = false;
        OutputFormat format = OutputFormat.Human;
        bool hasFormat = false;

        for (int index = 0; index < arguments.Count; index++)
        {
            string argument = arguments[index];
            switch (argument)
            {
                case "--force":
                    if (hasForce)
                    {
                        return Invalid("Option '--force' can be set only once.", out options, out error);
                    }

                    hasForce = true;
                    forceRefresh = true;
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
                    if (argument.StartsWith('-'))
                    {
                        return Invalid("Unknown limits argument.", out options, out error);
                    }

                    if (providerId is not null || !IsValidProviderId(argument))
                    {
                        return Invalid("Provider ID is invalid or repeated.", out options, out error);
                    }

                    providerId = argument;
                    break;
            }
        }

        options = new LimitsOptions(providerId, forceRefresh, format);
        error = string.Empty;
        return true;
    }

    private static bool IsValidProviderId(string value)
    {
        try
        {
            _ = new ProviderId(value);
            return true;
        }
        catch (ArgumentException)
        {
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
        out LimitsOptions options,
        out string error)
    {
        options = default;
        error = message;
        return false;
    }

    private static DateTimeOffset TruncateToSecond(DateTimeOffset value) =>
        value.AddTicks(-(value.Ticks % TimeSpan.TicksPerSecond));

    private static async Task WriteHumanAsync(
        TextWriter output,
        ProviderSnapshot[] snapshots,
        TimeProvider clock)
    {
        if (snapshots.Length == 0)
        {
            await output.WriteLineAsync("No provider limits found.").ConfigureAwait(false);
            return;
        }

        foreach (ProviderSnapshot snapshot in snapshots)
        {
            string displayName = SanitizeTerminalText(snapshot.DisplayName);
            string title = snapshot.PlanLabel is null
                ? displayName
                : $"{displayName} ({SanitizeTerminalText(snapshot.PlanLabel)})";
            await output.WriteLineAsync(title).ConfigureAwait(false);
            await output.WriteLineAsync($"  Provider: {snapshot.ProviderId.Value}")
                .ConfigureAwait(false);
            await output.WriteLineAsync(
                $"  Status: {(SnapshotFreshness.IsStale(snapshot, clock) ? "stale" : "fresh")}")
                .ConfigureAwait(false);

            foreach (MetricSnapshot metric in snapshot.Metrics
                         .OrderBy(item => item.Id.Value, StringComparer.Ordinal))
            {
                string line = metric switch
                {
                    ProgressMetricSnapshot progress => string.Create(
                        CultureInfo.InvariantCulture,
                        $"  {progress.Id.Value}: {progress.RemainingPercent:0.##}% remaining{FormatReset(progress.ResetsAtUtc)}"),
                    ScalarMetricSnapshot scalar => string.Create(
                        CultureInfo.InvariantCulture,
                        $"  {scalar.Id.Value}: {scalar.Value:0.######} {SanitizeTerminalText(scalar.Unit)}"),
                    _ => throw new NotSupportedException(
                        "The metric type is not supported by this CLI contract."),
                };
                await output.WriteLineAsync(line).ConfigureAwait(false);
            }
        }
    }

    private static string FormatReset(DateTimeOffset? resetsAtUtc) => resetsAtUtc is null
        ? string.Empty
        : string.Create(
            CultureInfo.InvariantCulture,
            $"; resets {resetsAtUtc.Value:yyyy-MM-ddTHH:mm:ss'Z'}");

    private static string SanitizeTerminalText(string value)
    {
        char[]? sanitized = null;
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (!char.IsControl(character)
                && CharUnicodeInfo.GetUnicodeCategory(value, index)
                    != UnicodeCategory.Format)
            {
                continue;
            }

            sanitized ??= value.ToCharArray();
            sanitized[index] = '\uFFFD';
        }

        return sanitized is null ? value : new string(sanitized);
    }

    private readonly record struct LimitsOptions(
        string? ProviderId,
        bool ForceRefresh,
        OutputFormat Format);

    private enum OutputFormat
    {
        Human,
        Json,
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

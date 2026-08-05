using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace WOpenUsage.Cli;

public static class DoctorCommand
{
    public const string SchemaVersion = "wusage.doctor.v1";
    public const string UsageText = "Usage: wusage doctor [--format human|json]";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "The CLI boundary must redact all diagnostic failures.")]
    public static async Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        TextWriter standardOutput,
        TextWriter standardError,
        ProviderDiagnosticsReader readDiagnostics,
        TimeProvider clock,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);
        ArgumentNullException.ThrowIfNull(readDiagnostics);
        ArgumentNullException.ThrowIfNull(clock);
        cancellationToken.ThrowIfCancellationRequested();

        if (!FormatOnlyCommandParser.TryParse(arguments, out CliOutputFormat format, out string error))
        {
            await standardError.WriteLineAsync(error).ConfigureAwait(false);
            await standardError.WriteLineAsync(UsageText).ConfigureAwait(false);
            return UsageCommand.InvalidUsageExitCode;
        }

        DoctorCheck[] checks;
        try
        {
            ProviderDiagnosticsSnapshot snapshot = await readDiagnostics(cancellationToken)
                .ConfigureAwait(false);
            checks = ProviderDiagnosticsValidator.ValidateChecks(snapshot);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            await standardError.WriteLineAsync("Unable to run doctor checks.")
                .ConfigureAwait(false);
            return UsageCommand.NoDataExitCode;
        }

        DateTimeOffset generatedAt = clock.GetUtcNow().ToUniversalTime();
        generatedAt = generatedAt.AddTicks(-(generatedAt.Ticks % TimeSpan.TicksPerSecond));
        if (format == CliOutputFormat.Json)
        {
            var document = new DoctorDocument(
                SchemaVersion,
                generatedAt,
                checks.Select(check => new CheckDocument(
                    check.Id,
                    JsonNamingPolicy.CamelCase.ConvertName(check.Status.ToString())))
                    .ToArray());
            await standardOutput.WriteLineAsync(JsonSerializer.Serialize(document, JsonOptions))
                .ConfigureAwait(false);
        }
        else
        {
            foreach (DoctorCheck check in checks)
            {
                await standardOutput.WriteLineAsync(
                        $"{check.Id}: {JsonNamingPolicy.CamelCase.ConvertName(check.Status.ToString())}")
                    .ConfigureAwait(false);
            }
        }

        return UsageCommand.SuccessExitCode;
    }

    private sealed record DoctorDocument(
        string SchemaVersion,
        DateTimeOffset GeneratedAt,
        IReadOnlyList<CheckDocument> Checks);

    private sealed record CheckDocument(string Id, string Status);
}

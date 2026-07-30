namespace WOpenUsage.Cli;

public static class CliApplication
{
    public const string UsageText =
        "Usage: wusage <limits|usage|providers|doctor> [command options]";

    public static async Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        TextWriter standardOutput,
        TextWriter standardError,
        string dataDirectory,
        TimeProvider clock,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentNullException.ThrowIfNull(clock);
        cancellationToken.ThrowIfCancellationRequested();

        if (arguments.Count == 0)
        {
            await standardError.WriteLineAsync(UsageText).ConfigureAwait(false);
            return UsageCommand.InvalidUsageExitCode;
        }

        string command = arguments[0];
        string[] commandArguments = arguments.Skip(1).ToArray();
        string fullDataDirectory = Path.GetFullPath(dataDirectory);

        return command switch
        {
            "usage" => await UsageCommand.RunAsync(
                commandArguments,
                standardOutput,
                standardError,
                (from, to, token) => new UsageQuery(Path.Combine(
                        fullDataDirectory,
                        "scanner",
                        "usage.v1.db"))
                    .ReadAsync(from, to, token),
                clock,
                cancellationToken).ConfigureAwait(false),
            "limits" => await LimitsCommand.RunAsync(
                commandArguments,
                standardOutput,
                standardError,
                (providerId, force, token) => LocalLimitsCliAccess.ReadAsync(
                    fullDataDirectory,
                    providerId,
                    force,
                    clock,
                    token),
                clock,
                cancellationToken).ConfigureAwait(false),
            "providers" => await ProvidersCommand.RunAsync(
                commandArguments,
                standardOutput,
                standardError,
                token => LocalProviderDiagnosticsAccess.ReadAsync(
                    fullDataDirectory,
                    clock,
                    token),
                clock,
                cancellationToken).ConfigureAwait(false),
            "doctor" => await DoctorCommand.RunAsync(
                commandArguments,
                standardOutput,
                standardError,
                token => LocalProviderDiagnosticsAccess.ReadAsync(
                    fullDataDirectory,
                    clock,
                    token),
                clock,
                cancellationToken).ConfigureAwait(false),
            _ => await WriteUnknownCommandAsync(standardError).ConfigureAwait(false),
        };
    }

    private static async Task<int> WriteUnknownCommandAsync(TextWriter standardError)
    {
        await standardError.WriteLineAsync("Unknown command.").ConfigureAwait(false);
        await standardError.WriteLineAsync(UsageText).ConfigureAwait(false);
        return UsageCommand.InvalidUsageExitCode;
    }
}

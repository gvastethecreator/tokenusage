namespace WOpenUsage.Cli;

public static class CliApplication
{
    public const string UsageText = "Usage: wusage <limits|usage> [command options]";

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
                (from, to, token) => LocalUsageCliAccess.ReadAsync(
                    Path.Combine(fullDataDirectory, "scanner", "usage.v1.db"),
                    from,
                    to,
                    token),
                clock,
                cancellationToken).ConfigureAwait(false),
            "limits" => await LimitsCommand.RunAsync(
                commandArguments,
                standardOutput,
                standardError,
                (force, token) => LocalLimitsCliAccess.ReadAsync(
                    fullDataDirectory,
                    force,
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

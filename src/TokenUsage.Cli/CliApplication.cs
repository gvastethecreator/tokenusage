namespace TokenUsage.Cli;

public static class CliApplication
{
    public const string UsageText =
        "Usage: tokenusage <refresh|limits|usage|report|providers|doctor|pricing|cursor|zcode|grok|hook> [command options]";

    public static bool IsHelpRequest(IReadOnlyList<string> arguments) =>
        arguments.Count == 1
        && arguments[0] is "help" or "--help" or "-h";

    public static async Task<int> WriteHelpAsync(TextWriter standardOutput)
    {
        ArgumentNullException.ThrowIfNull(standardOutput);
        await standardOutput.WriteLineAsync(UsageText).ConfigureAwait(false);
        return UsageCommand.SuccessExitCode;
    }

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

        if (IsHelpRequest(arguments))
        {
            return await WriteHelpAsync(standardOutput).ConfigureAwait(false);
        }

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
            "refresh" => await RefreshCommand.RunAsync(
                commandArguments,
                standardOutput,
                standardError,
                token => LocalUsageCliAccess.RefreshAsync(
                    fullDataDirectory,
                    clock,
                    token),
                clock,
                cancellationToken).ConfigureAwait(false),
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
            "report" => await ReportCommand.RunAsync(
                commandArguments,
                standardOutput,
                standardError,
                (from, to, agentId, token) => new UsageReportQuery(Path.Combine(
                        fullDataDirectory,
                        "scanner",
                        "usage.v1.db"))
                    .ReadAsync(from, to, agentId, cancellationToken: token),
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
            "pricing" => await PricingCommand.RunAsync(
                commandArguments,
                standardOutput,
                standardError,
                clock,
                cancellationToken).ConfigureAwait(false),
            "cursor" => await CursorCommand.RunAsync(
                commandArguments,
                standardOutput,
                standardError).ConfigureAwait(false),
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

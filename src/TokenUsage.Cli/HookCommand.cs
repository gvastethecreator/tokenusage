namespace TokenUsage.Cli;

public static class HookCommand
{
    public const string UsageText = "Usage: tokenusage hook <stop>";

    public static async Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        TextWriter standardOutput,
        TextWriter standardError,
        Func<Task<int>>? runRefresh = null,
        TextReader? standardInput = null)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);

        if (arguments.Count != 1
            || !string.Equals(arguments[0], "stop", StringComparison.Ordinal))
        {
            await standardError.WriteLineAsync(UsageText).ConfigureAwait(false);
            return UsageCommand.InvalidUsageExitCode;
        }

        return await RunStopAsync(runRefresh, standardInput).ConfigureAwait(false);
    }

    /// <summary>
    /// The refresh trigger every provider hook calls. A redirected standard input can carry
    /// conversation content, so it is drained and discarded; a console standard input (a
    /// detached launch) never reaches end-of-stream, so callers pass <c>null</c> instead and
    /// nothing is drained. Nothing is printed, and the exit code stays 0 to keep the hook
    /// silent.
    /// </summary>
    internal static async Task<int> RunStopAsync(
        Func<Task<int>>? runRefresh,
        TextReader? standardInput)
    {
        try
        {
            if (standardInput is not null)
            {
                await standardInput.ReadToEndAsync().ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is IOException
                                           or InvalidOperationException)
        {
        }

        if (runRefresh is not null)
        {
            try
            {
                await runRefresh().ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException
                                               or UnauthorizedAccessException
                                               or InvalidOperationException)
            {
            }
        }

        return UsageCommand.SuccessExitCode;
    }
}

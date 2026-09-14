using System.Diagnostics.CodeAnalysis;
using TokenUsage.Core.Usage;

namespace TokenUsage.Cli;

public static class RecoverUsageCommand
{
    public const string UsageText = "Usage: tokenusage recover-usage --backup <file> --output <new-file>";

    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "The CLI boundary redacts recovery paths and storage exceptions.")]
    public static async Task<int> RunAsync(IReadOnlyList<string> arguments, TextWriter output,
        TextWriter error, CancellationToken cancellationToken = default)
    {
        if (arguments.Count != 4 || arguments[0] != "--backup" || arguments[2] != "--output"
            || string.IsNullOrWhiteSpace(arguments[1]) || string.IsNullOrWhiteSpace(arguments[3]))
        {
            await error.WriteLineAsync(UsageText).ConfigureAwait(false);
            return 2;
        }
        try
        {
            await UsageRepository.RecoverCopyAsync(arguments[1], arguments[3], cancellationToken).ConfigureAwait(false);
            await output.WriteLineAsync("Recovered usage copy created. Existing history was not replaced; the application data location was not changed.").ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            await error.WriteLineAsync("Recovery failed. Keep the original and any output files for inspection; retry with a new output path after resolving the cause.").ConfigureAwait(false);
            return 4;
        }
    }
}

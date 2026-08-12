using Windows.Storage;
using TokenUsage.Cli;
using TokenUsage.Platform.Windows.Storage;

if (CliApplication.IsHelpRequest(args))
{
    return await CliApplication.WriteHelpAsync(Console.Out);
}

// Cursor local integration management does not open TokenUsage application storage. Keep it
// usable from an unpackaged CLI build where ApplicationData is unavailable.
if (args.Length > 0 && string.Equals(args[0], "cursor", StringComparison.Ordinal))
{
    return await CursorCommand.RunAsync(args.Skip(1).ToArray(), Console.Out, Console.Error);
}

string dataDirectory;
try
{
    dataDirectory = TokenUsageDataDirectory.Resolve(
        () => ApplicationData.Current.LocalFolder.Path);
}
catch (Exception)
{
    await Console.Error.WriteLineAsync("Unable to resolve TokenUsage local data.");
    return UsageCommand.NoDataExitCode;
}

return await CliApplication.RunAsync(
    args,
    Console.Out,
    Console.Error,
    dataDirectory,
    TimeProvider.System);

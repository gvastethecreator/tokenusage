using Windows.Storage;
using TokenUsage.Cli;

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
    string? dataDirectoryOverride =
        Environment.GetEnvironmentVariable("TOKENUSAGE_DATA_DIR");
    if (dataDirectoryOverride is not null && string.IsNullOrWhiteSpace(dataDirectoryOverride))
    {
        throw new InvalidOperationException("The data directory override is empty.");
    }

    dataDirectory = dataDirectoryOverride ?? ApplicationData.Current.LocalFolder.Path;
    dataDirectory = Path.GetFullPath(dataDirectory);
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

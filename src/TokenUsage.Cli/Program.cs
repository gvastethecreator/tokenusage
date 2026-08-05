using Windows.Storage;
using TokenUsage.Cli;

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

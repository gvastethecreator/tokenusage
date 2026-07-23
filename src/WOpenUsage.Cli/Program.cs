using Windows.Storage;
using WOpenUsage.Cli;

if (args.Length == 0 || !string.Equals(args[0], "usage", StringComparison.Ordinal))
{
    await Console.Error.WriteLineAsync(
        "Usage: wusage <usage> [command options]");
    return UsageCommand.InvalidUsageExitCode;
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

string databasePath = Path.Combine(dataDirectory, "scanner", "usage.v1.db");
return await UsageCommand.RunAsync(
    args[1..],
    Console.Out,
    Console.Error,
    (from, to, cancellationToken) => LocalUsageCliAccess.ReadAsync(
        databasePath,
        from,
        to,
        cancellationToken),
    TimeProvider.System);

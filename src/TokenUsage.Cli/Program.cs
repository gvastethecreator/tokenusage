using Windows.Storage;
using TokenUsage.Cli;
using TokenUsage.Core.Usage;
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

// ZCode hook management and the Stop-hook trigger follow the same unpackaged-safe rule.
if (args.Length > 0 && string.Equals(args[0], "zcode", StringComparison.Ordinal))
{
    return await ZcodeCommand.RunAsync(
        args.Skip(1).ToArray(),
        Console.Out,
        Console.Error,
        runRefresh: RefreshLocalUsageForHook,
        standardInput: Console.In);
}

// The generic refresh trigger every provider hook calls.
if (args.Length > 0 && string.Equals(args[0], "hook", StringComparison.Ordinal))
{
    return await HookCommand.RunAsync(
        args.Skip(1).ToArray(),
        Console.Out,
        Console.Error,
        runRefresh: RefreshLocalUsageForHook,
        standardInput: Console.In);
}

// Grok hook management follows the same unpackaged-safe rule.
if (args.Length > 0 && string.Equals(args[0], "grok", StringComparison.Ordinal))
{
    return await GrokCommand.RunAsync(args.Skip(1).ToArray(), Console.Out, Console.Error);
}

async Task<int> RefreshLocalUsageForHook()
{
    string hookDataDirectory;
    try
    {
        hookDataDirectory = TokenUsageDataDirectory.Resolve(
            () => ApplicationData.Current.LocalFolder.Path);
    }
    catch (Exception)
    {
        return 0;
    }

    try
    {
        var collectionSettings = new DataCollectionSettingsStore(
            Path.Combine(hookDataDirectory, DataCollectionSettingsStore.DefaultFileName));
        DataCollectionSettings settings = await collectionSettings.LoadAsync();
        if (!settings.BackgroundCollection)
        {
            return 0;
        }

        await LocalUsageCliAccess.RefreshAsync(hookDataDirectory, TimeProvider.System);
        return 0;
    }
    catch (Exception)
    {
        return 0;
    }
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

using TokenUsage.Runtime.Windows.Cursor;

namespace TokenUsage.Cli;

public static class CursorCommand
{
    public const string UsageText =
        "Usage: tokenusage cursor <install-hook|status|uninstall-hook>";

    public static async Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        TextWriter standardOutput,
        TextWriter standardError,
        CursorHookInstaller? installer = null)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);

        if (arguments.Count != 1)
        {
            await standardError.WriteLineAsync(UsageText).ConfigureAwait(false);
            return UsageCommand.InvalidUsageExitCode;
        }

        installer ??= new CursorHookInstaller();
        try
        {
            return arguments[0] switch
            {
                "install-hook" => await InstallAsync(installer, standardOutput)
                    .ConfigureAwait(false),
                "status" => await WriteStatusAsync(installer, standardOutput)
                    .ConfigureAwait(false),
                "uninstall-hook" => await UninstallAsync(installer, standardOutput)
                    .ConfigureAwait(false),
                _ => await WriteInvalidUsageAsync(standardError).ConfigureAwait(false),
            };
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException
                                           or InvalidDataException
                                           or InvalidOperationException)
        {
            await standardError.WriteLineAsync("Unable to update the Cursor hook.")
                .ConfigureAwait(false);
            return UsageCommand.NoDataExitCode;
        }
    }

    private static async Task<int> InstallAsync(
        CursorHookInstaller installer,
        TextWriter standardOutput)
    {
        installer.Install();
        await standardOutput.WriteLineAsync(
            "Cursor usage hook installed. New local Agent turns will be recorded as partial, unpriced usage.")
            .ConfigureAwait(false);
        return UsageCommand.SuccessExitCode;
    }

    private static async Task<int> WriteStatusAsync(
        CursorHookInstaller installer,
        TextWriter standardOutput)
    {
        await standardOutput.WriteLineAsync(installer.GetStatus() switch
        {
            CursorHookInstallationStatus.Installed => "Cursor usage hook: installed",
            CursorHookInstallationStatus.Incomplete => "Cursor usage hook: incomplete",
            _ => "Cursor usage hook: not installed",
        }).ConfigureAwait(false);
        return UsageCommand.SuccessExitCode;
    }

    private static async Task<int> UninstallAsync(
        CursorHookInstaller installer,
        TextWriter standardOutput)
    {
        installer.Uninstall();
        await standardOutput.WriteLineAsync("Cursor usage hook uninstalled.")
            .ConfigureAwait(false);
        return UsageCommand.SuccessExitCode;
    }

    private static async Task<int> WriteInvalidUsageAsync(TextWriter standardError)
    {
        await standardError.WriteLineAsync(UsageText).ConfigureAwait(false);
        return UsageCommand.InvalidUsageExitCode;
    }
}

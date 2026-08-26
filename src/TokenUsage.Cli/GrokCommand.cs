using TokenUsage.Runtime.Windows.Grok;

namespace TokenUsage.Cli;

public static class GrokCommand
{
    public const string UsageText =
        "Usage: tokenusage grok <install-hook|status|uninstall-hook>";

    public static async Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        TextWriter standardOutput,
        TextWriter standardError,
        GrokHookInstaller? installer = null)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);

        if (arguments.Count != 1)
        {
            await standardError.WriteLineAsync(UsageText).ConfigureAwait(false);
            return UsageCommand.InvalidUsageExitCode;
        }

        installer ??= new GrokHookInstaller();
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
            await standardError.WriteLineAsync(
                    "Unable to inspect or update the Grok local integration.")
                .ConfigureAwait(false);
            return UsageCommand.NoDataExitCode;
        }
    }

    private static async Task<int> InstallAsync(
        GrokHookInstaller installer,
        TextWriter standardOutput)
    {
        installer.Install();
        await standardOutput.WriteLineAsync(
            "Grok Stop hook installed. TokenUsage now refreshes its local usage after each Grok task.")
            .ConfigureAwait(false);
        return UsageCommand.SuccessExitCode;
    }

    private static async Task<int> WriteStatusAsync(
        GrokHookInstaller installer,
        TextWriter standardOutput)
    {
        string status = installer.GetStatus() switch
        {
            GrokHookInstallationStatus.Installed => "Grok Stop hook: installed",
            _ => "Grok Stop hook: not installed; run 'tokenusage grok install-hook'",
        };
        await standardOutput.WriteLineAsync(status).ConfigureAwait(false);
        return UsageCommand.SuccessExitCode;
    }

    private static async Task<int> UninstallAsync(
        GrokHookInstaller installer,
        TextWriter standardOutput)
    {
        installer.Uninstall();
        await standardOutput.WriteLineAsync("Grok Stop hook removed.")
            .ConfigureAwait(false);
        return UsageCommand.SuccessExitCode;
    }

    private static async Task<int> WriteInvalidUsageAsync(TextWriter standardError)
    {
        await standardError.WriteLineAsync(UsageText).ConfigureAwait(false);
        return UsageCommand.InvalidUsageExitCode;
    }
}

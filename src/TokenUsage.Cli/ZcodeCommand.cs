using System.Text;
using TokenUsage.Runtime.Windows.Zcode;

namespace TokenUsage.Cli;

public static class ZcodeCommand
{
    public const string UsageText =
        "Usage: tokenusage zcode <install-hook|status|uninstall-hook|stop-hook>";

    public static async Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        TextWriter standardOutput,
        TextWriter standardError,
        ZcodeHookInstaller? installer = null,
        Func<Task<int>>? runRefresh = null,
        TextReader? standardInput = null)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);

        if (arguments.Count != 1)
        {
            await standardError.WriteLineAsync(UsageText).ConfigureAwait(false);
            return UsageCommand.InvalidUsageExitCode;
        }

        installer ??= new ZcodeHookInstaller();
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
                "stop-hook" => await RunStopHookAsync(runRefresh, standardInput)
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
                    "Unable to inspect or update the ZCode local integration.")
                .ConfigureAwait(false);
            return UsageCommand.NoDataExitCode;
        }
    }

    /// <summary>
    /// The ZCode Stop hook calls this. The event payload can carry conversation
    /// content, so it shares the silent drain-and-refresh trigger with every
    /// other provider hook.
    /// </summary>
    private static Task<int> RunStopHookAsync(
        Func<Task<int>>? runRefresh,
        TextReader? standardInput) =>
        HookCommand.RunStopAsync(runRefresh, standardInput);

    private static async Task<int> InstallAsync(
        ZcodeHookInstaller installer,
        TextWriter standardOutput)
    {
        installer.Install();
        await standardOutput.WriteLineAsync(
            "ZCode Stop hook installed. TokenUsage now refreshes its local usage after each ZCode task.")
            .ConfigureAwait(false);
        return UsageCommand.SuccessExitCode;
    }

    private static async Task<int> WriteStatusAsync(
        ZcodeHookInstaller installer,
        TextWriter standardOutput)
    {
        string status = installer.GetStatus() switch
        {
            ZcodeHookInstallationStatus.Installed =>
                "ZCode Stop hook: installed",
            ZcodeHookInstallationStatus.Incomplete =>
                "ZCode Stop hook: registered but ZCode hooks are disabled in config.json",
            _ => "ZCode Stop hook: not installed; run 'tokenusage zcode install-hook'",
        };
        await standardOutput.WriteLineAsync(status).ConfigureAwait(false);
        return UsageCommand.SuccessExitCode;
    }

    private static async Task<int> UninstallAsync(
        ZcodeHookInstaller installer,
        TextWriter standardOutput)
    {
        installer.Uninstall();
        await standardOutput.WriteLineAsync("ZCode Stop hook removed.")
            .ConfigureAwait(false);
        return UsageCommand.SuccessExitCode;
    }

    private static async Task<int> WriteInvalidUsageAsync(TextWriter standardError)
    {
        await standardError.WriteLineAsync(UsageText).ConfigureAwait(false);
        return UsageCommand.InvalidUsageExitCode;
    }
}

using TokenUsage.Runtime.Windows.Cursor;
using TokenUsage.Core.Usage;
using TokenUsage.Providers.Cursor;

namespace TokenUsage.Cli;

public static class CursorCommand
{
    public const string UsageText =
        "Usage: tokenusage cursor <install-hook|status|uninstall-hook>";

    public static async Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        TextWriter standardOutput,
        TextWriter standardError,
        CursorHookInstaller? installer = null,
        CursorUsageEventSource? source = null)
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
        source ??= new CursorUsageEventSource(TimeZoneInfo.Local.Id);
        try
        {
            return arguments[0] switch
            {
                "install-hook" => await WriteDirectReadNoticeAsync(standardOutput)
                    .ConfigureAwait(false),
                "status" => await WriteStatusAsync(installer, source, standardOutput)
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
            await standardError.WriteLineAsync("Unable to inspect or update the Cursor local integration.")
                .ConfigureAwait(false);
            return UsageCommand.NoDataExitCode;
        }
    }

    private static async Task<int> WriteDirectReadNoticeAsync(TextWriter standardOutput)
    {
        await standardOutput.WriteLineAsync(
            "Cursor no longer requires a hook. TokenUsage reads estimated Agent context totals directly from Cursor's local state.")
            .ConfigureAwait(false);
        return UsageCommand.SuccessExitCode;
    }

    private static async Task<int> WriteStatusAsync(
        CursorHookInstaller installer,
        CursorUsageEventSource source,
        TextWriter standardOutput)
    {
        UsageSourceReadResult result = await source.ReadAsync().ConfigureAwait(false);
        await standardOutput.WriteLineAsync(result.Events.Count > 0
            ? $"Cursor local usage: available ({result.Events.Count} estimated context records)"
            : result.Issue == UsageSourceIssueKind.RootUnavailable
                ? "Cursor local usage: Cursor profile not found"
                : "Cursor local usage: no estimated context records found")
            .ConfigureAwait(false);

        string? legacyHookStatus = installer.GetStatus() switch
        {
            CursorHookInstallationStatus.Installed =>
                "Legacy TokenUsage hook: installed; run 'tokenusage cursor uninstall-hook' to remove it",
            CursorHookInstallationStatus.Incomplete =>
                "Legacy TokenUsage hook: incomplete; run 'tokenusage cursor uninstall-hook' to remove it",
            _ => null,
        };
        if (legacyHookStatus is not null)
        {
            await standardOutput.WriteLineAsync(legacyHookStatus).ConfigureAwait(false);
        }

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

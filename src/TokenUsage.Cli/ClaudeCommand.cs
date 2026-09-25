using System.Globalization;
using TokenUsage.Providers.Claude;
using TokenUsage.Runtime.Windows.Claude;

namespace TokenUsage.Cli;

public static class ClaudeCommand
{
    public const string UsageText =
        "Usage: tokenusage claude <install-hook|uninstall-hook|install-statusline|uninstall-statusline|status|statusline>";

    public static async Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        TextWriter standardOutput,
        TextWriter standardError,
        Func<string?>? resolveDataDirectory = null,
        Stream? standardInput = null,
        TimeProvider? clock = null,
        ClaudeSettingsInstaller? installer = null,
        ClaudeStatusLineForwarder? forwarder = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);

        if (arguments.Count != 1)
        {
            await standardError.WriteLineAsync(UsageText).ConfigureAwait(false);
            return UsageCommand.InvalidUsageExitCode;
        }

        installer ??= new ClaudeSettingsInstaller();
        if (string.Equals(arguments[0], "statusline", StringComparison.Ordinal))
        {
            return await RunStatusLineAsync(
                    installer,
                    forwarder ?? new ClaudeStatusLineForwarder(),
                    resolveDataDirectory,
                    standardInput,
                    standardOutput,
                    clock ?? TimeProvider.System,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        try
        {
            switch (arguments[0])
            {
                case "install-hook":
                    installer.InstallHook();
                    await standardOutput.WriteLineAsync(
                            "Claude Code Stop hook installed. TokenUsage now refreshes its local usage after each Claude Code task.")
                        .ConfigureAwait(false);
                    return UsageCommand.SuccessExitCode;
                case "uninstall-hook":
                    installer.UninstallHook();
                    await standardOutput.WriteLineAsync("Claude Code Stop hook removed.")
                        .ConfigureAwait(false);
                    return UsageCommand.SuccessExitCode;
                case "install-statusline":
                    installer.InstallStatusLine();
                    await standardOutput.WriteLineAsync(
                            "Claude Code status line wrapper installed. TokenUsage keeps only the 5-hour and weekly limit readings; a previous status line keeps running.")
                        .ConfigureAwait(false);
                    return UsageCommand.SuccessExitCode;
                case "uninstall-statusline":
                    installer.UninstallStatusLine();
                    await standardOutput.WriteLineAsync(
                            "Claude Code status line wrapper removed; any previous status line was restored.")
                        .ConfigureAwait(false);
                    return UsageCommand.SuccessExitCode;
                case "status":
                    await WriteStatusAsync(installer, resolveDataDirectory, standardOutput, clock ?? TimeProvider.System)
                        .ConfigureAwait(false);
                    return UsageCommand.SuccessExitCode;
                default:
                    await standardError.WriteLineAsync(UsageText).ConfigureAwait(false);
                    return UsageCommand.InvalidUsageExitCode;
            }
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException
                                           or InvalidDataException
                                           or InvalidOperationException
                                           or System.Text.Json.JsonException)
        {
            await standardError.WriteLineAsync(
                    "Unable to inspect or update the Claude Code settings.")
                .ConfigureAwait(false);
            return UsageCommand.NoDataExitCode;
        }
    }

    private static async Task WriteStatusAsync(
        ClaudeSettingsInstaller installer,
        Func<string?>? resolveDataDirectory,
        TextWriter standardOutput,
        TimeProvider clock)
    {
        await standardOutput.WriteLineAsync(installer.GetHookStatus() == ClaudeIntegrationStatus.Installed
                ? "Claude Code Stop hook: installed"
                : "Claude Code Stop hook: not installed; run 'tokenusage claude install-hook'")
            .ConfigureAwait(false);
        await standardOutput.WriteLineAsync(installer.GetStatusLineStatus() == ClaudeIntegrationStatus.Installed
                ? "Claude Code status line wrapper: installed"
                : "Claude Code status line wrapper: not installed; run 'tokenusage claude install-statusline'")
            .ConfigureAwait(false);
        string? dataDirectory = TryResolve(resolveDataDirectory);
        ClaudeRateLimitSnapshot? reading = dataDirectory is null
            ? null
            : new ClaudeRateLimitStore(ClaudeRateLimitStore.DefaultPath(dataDirectory)).Load();
        await standardOutput.WriteLineAsync(reading is null
                ? "Claude Code limits: no reading yet"
                : string.Format(
                    CultureInfo.InvariantCulture,
                    "Claude Code limits: {0} (observed {1:u})",
                    FormatLimits(reading, clock.GetUtcNow()) is { Length: > 0 } text ? text : "no open window",
                    reading.ObservedAtUtc))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// The status line entry point. It must stay quiet and fast: every failure still exits 0,
    /// because Claude Code shows whatever the command prints on each update.
    /// </summary>
    private static async Task<int> RunStatusLineAsync(
        ClaudeSettingsInstaller installer,
        ClaudeStatusLineForwarder forwarder,
        Func<string?>? resolveDataDirectory,
        Stream? standardInput,
        TextWriter standardOutput,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        byte[] input = await ReadBoundedAsync(standardInput, cancellationToken).ConfigureAwait(false);
        DateTimeOffset nowUtc = clock.GetUtcNow().ToUniversalTime();
        ClaudeRateLimitSnapshot? reading = null;
        if (ClaudeStatusLineRateLimits.TryParse(input, nowUtc, out ClaudeRateLimitSnapshot? parsed))
        {
            reading = parsed;
            try
            {
                string? dataDirectory = TryResolve(resolveDataDirectory);
                if (dataDirectory is not null)
                {
                    new ClaudeRateLimitStore(ClaudeRateLimitStore.DefaultPath(dataDirectory)).Save(parsed!);
                }
            }
            catch (Exception exception) when (exception is IOException
                                               or UnauthorizedAccessException
                                               or System.Security.SecurityException)
            {
                // A locked sidecar must not blank the user's status line.
            }
        }

        string? previousCommand = installer.GetPreviousStatusLineCommand();
        if (previousCommand is not null)
        {
            string? forwarded = await forwarder
                .RunAsync(previousCommand, input, cancellationToken)
                .ConfigureAwait(false);
            if (forwarded is not null)
            {
                await standardOutput.WriteAsync(forwarded).ConfigureAwait(false);
                return UsageCommand.SuccessExitCode;
            }
        }

        if (reading is not null && FormatLimits(reading, nowUtc) is { Length: > 0 } limits)
        {
            await standardOutput.WriteLineAsync(limits).ConfigureAwait(false);
        }

        return UsageCommand.SuccessExitCode;
    }

    internal static string FormatLimits(ClaudeRateLimitSnapshot reading, DateTimeOffset nowUtc) =>
        string.Join(
            " · ",
            reading.Windows
                .Where(window => window.ResetsAtUtc > nowUtc)
                .Select(window => string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} {1:0}%",
                    window.Key switch
                    {
                        ClaudeStatusLineRateLimits.FiveHourKey => "5h",
                        ClaudeStatusLineRateLimits.SevenDayKey => "7d",
                        _ => "spend",
                    },
                    window.UsedPercent)));

    private static async Task<byte[]> ReadBoundedAsync(Stream? input, CancellationToken cancellationToken)
    {
        if (input is null)
        {
            return [];
        }

        try
        {
            using var buffer = new MemoryStream();
            byte[] chunk = new byte[16 * 1024];
            int read;
            while ((read = await input.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > ClaudeStatusLineRateLimits.MaximumPayloadBytes)
                {
                    // Keep draining so the writer does not block, but stop buffering.
                    while (await input.ReadAsync(chunk, cancellationToken).ConfigureAwait(false) > 0)
                    {
                    }

                    return [];
                }

                buffer.Write(chunk, 0, read);
            }

            return buffer.ToArray();
        }
        catch (Exception exception) when (exception is IOException
                                           or InvalidOperationException
                                           or NotSupportedException)
        {
            return [];
        }
    }

    private static string? TryResolve(Func<string?>? resolveDataDirectory)
    {
        try
        {
            return resolveDataDirectory?.Invoke();
        }
        catch (Exception)
        {
            return null;
        }
    }
}

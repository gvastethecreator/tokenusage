using System.Globalization;
using TokenUsage.Core.Credentials;
using TokenUsage.Core.Usage;
using TokenUsage.Providers.Claude;
using TokenUsage.Runtime.Windows.Claude;
using TokenUsage.Runtime.Windows.Providers;

namespace TokenUsage.Cli;

public static class ClaudeCommand
{
    public const string UsageText =
        "Usage: tokenusage claude <install-hook|uninstall-hook|enable-limits|disable-limits|install-statusline|uninstall-statusline|recover-history|status|statusline>";

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

        if (arguments.Count > 0 && arguments[0] == "recover-history")
            return await ClaudeHistoryCommand.ExecuteAsync(arguments.Skip(1).ToArray(),
                standardOutput, standardError, resolveDataDirectory, cancellationToken).ConfigureAwait(false);

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
                case "enable-limits":
                {
                    string? dataDirectory = TryResolve(resolveDataDirectory);
                    if (dataDirectory is null)
                    {
                        await standardError.WriteLineAsync("The TokenUsage data directory could not be resolved.").ConfigureAwait(false);
                        return UsageCommand.NoDataExitCode;
                    }
                    await new ClaudeLimitsConsentStore(ClaudeLimitsConsentStore.DefaultPath(dataDirectory))
                        .EnableAsync(cancellationToken).ConfigureAwait(false);
                    await standardOutput.WriteLineAsync("Claude limits enabled through your Claude Code sign-in.").ConfigureAwait(false);
                    return UsageCommand.SuccessExitCode;
                }
                case "disable-limits":
                {
                    string? dataDirectory = TryResolve(resolveDataDirectory);
                    if (dataDirectory is null)
                    {
                        await standardError.WriteLineAsync("The TokenUsage data directory could not be resolved.").ConfigureAwait(false);
                        return UsageCommand.NoDataExitCode;
                    }
                    if (!await DisableLimitsAsync(dataDirectory, clock ?? TimeProvider.System, cancellationToken).ConfigureAwait(false))
                    {
                        await standardError.WriteLineAsync("Claude limits are disabled, but the cached reading could not be removed.").ConfigureAwait(false);
                        return UsageCommand.NoDataExitCode;
                    }
                    await standardOutput.WriteLineAsync("Claude limits disabled.").ConfigureAwait(false);
                    return UsageCommand.SuccessExitCode;
                }
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
                    if (TryResolve(resolveDataDirectory) is { } enabledDataDirectory)
                        await new ClaudeLimitsConsentStore(ClaudeLimitsConsentStore.DefaultPath(enabledDataDirectory))
                            .EnableAsync(cancellationToken).ConfigureAwait(false);
                    await standardOutput.WriteLineAsync(
                            "Claude limits enabled through your Claude Code sign-in. The status line wrapper preserves your previous command.")
                        .ConfigureAwait(false);
                    return UsageCommand.SuccessExitCode;
                case "uninstall-statusline":
                {
                    installer.UninstallStatusLine();
                    string? dataDirectory = TryResolve(resolveDataDirectory);
                    if (dataDirectory is not null)
                    {
                        if (!await DisableLimitsAsync(dataDirectory, clock ?? TimeProvider.System, cancellationToken).ConfigureAwait(false))
                        {
                            await standardError.WriteLineAsync("Claude limits are disabled, but the cached reading could not be removed.").ConfigureAwait(false);
                            return UsageCommand.NoDataExitCode;
                        }
                    }
                    await standardOutput.WriteLineAsync(
                            "Claude Code status line wrapper removed; any previous status line was restored.")
                        .ConfigureAwait(false);
                    return UsageCommand.SuccessExitCode;
                }
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
                                           or TimeoutException
                                           or System.Text.Json.JsonException)
        {
            await standardError.WriteLineAsync(
                    "Unable to inspect or update the Claude limits preference or Claude Code settings.")
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
        ClaudeLimitsConsentStatus consent = dataDirectory is null ? ClaudeLimitsConsentStatus.NotEnabled
            : await new ClaudeLimitsConsentStore(ClaudeLimitsConsentStore.DefaultPath(dataDirectory))
                .ReadOrMigrateAsync(installer.GetStatusLineStatus() == ClaudeIntegrationStatus.Installed)
                .ConfigureAwait(false);
        await standardOutput.WriteLineAsync(consent == ClaudeLimitsConsentStatus.Enabled
                ? "Claude limits access: enabled"
                : consent == ClaudeLimitsConsentStatus.Disabled ? "Claude limits access: disabled"
                : "Claude limits access: not enabled; run 'tokenusage claude enable-limits'")
            .ConfigureAwait(false);
        ClaudeRateLimitSnapshot? reading = dataDirectory is null || consent != ClaudeLimitsConsentStatus.Enabled
            ? null
            : new ClaudeRateLimitStore(ClaudeRateLimitStore.DefaultPath(dataDirectory)).Load();
        await standardOutput.WriteLineAsync(reading is null
                ? "Claude Code status line limits: no reading yet"
                : string.Format(
                    CultureInfo.InvariantCulture,
                    "Claude Code limits: {0} (observed {1:u})",
                    FormatLimits(reading, clock.GetUtcNow()) is { Length: > 0 } text ? text : "no open window",
                    reading.ObservedAtUtc))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// The status line entry point forwards the previous command's output and exit code.
    /// A local cache failure must not prevent the user's command from running.
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
        ClaudeStatusLineRateLimits.TryParse(input, nowUtc, out ClaudeRateLimitSnapshot? reading);
        bool isForwarded = Environment.GetEnvironmentVariable(
            ClaudeStatusLineForwarder.ReentrancyEnvironmentVariable) == "1";
        try
        {
            string? dataDirectory = TryResolve(resolveDataDirectory);
            if (!isForwarded && reading is not null && dataDirectory is not null)
            {
                await new ClaudeLimitsConsentStore(ClaudeLimitsConsentStore.DefaultPath(dataDirectory))
                    .RunIfEnabledAsync(installer.GetStatusLineStatus() == ClaudeIntegrationStatus.Installed,
                        () => new ClaudeRateLimitStore(ClaudeRateLimitStore.DefaultPath(dataDirectory)).Save(reading),
                        cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is IOException
                                               or UnauthorizedAccessException
                                               or System.Text.Json.JsonException
                                               or TimeoutException
                                               or System.Security.SecurityException)
        {
            // A locked sidecar must not blank the user's status line.
        }

        string? previousCommand = isForwarded ? null : installer.GetPreviousStatusLineCommand();
        if (previousCommand is not null)
        {
            ClaudeStatusLineForwardResult forwarded = await forwarder
                .RunAsync(previousCommand, input, cancellationToken)
                .ConfigureAwait(false);
            await standardOutput.WriteAsync(forwarded.StandardOutput).ConfigureAwait(false);
            return forwarded.ExitCode;
        }

        if (reading is not null && FormatLimits(reading, nowUtc) is { Length: > 0 } limits)
        {
            await standardOutput.WriteLineAsync(limits).ConfigureAwait(false);
        }

        return UsageCommand.SuccessExitCode;
    }

    private static async Task<bool> DisableLimitsAsync(string dataDirectory, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        await new ClaudeLimitsConsentStore(ClaudeLimitsConsentStore.DefaultPath(dataDirectory))
            .DisableAsync(cancellationToken).ConfigureAwait(false);
        new ClaudeRateLimitStore(ClaudeRateLimitStore.DefaultPath(dataDirectory)).Delete();
        ManualCredentialChangeResult result = await WindowsProviderCatalog.CreateComposition(dataDirectory, clock)
            .ResetProviderCacheAsync("claude", cancellationToken).ConfigureAwait(false);
        return result != ManualCredentialChangeResult.CacheCleanupFailed;
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

    internal static string? TryResolve(Func<string?>? resolveDataDirectory)
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

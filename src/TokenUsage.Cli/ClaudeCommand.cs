using System.Globalization;
using TokenUsage.Core.Usage;
using TokenUsage.Providers.Claude;
using TokenUsage.Runtime.Windows.Claude;
using TokenUsage.Runtime.Windows.Providers;

namespace TokenUsage.Cli;

public static class ClaudeCommand
{
    public const string UsageText =
        "Usage: tokenusage claude <install-statusline|uninstall-statusline|statusline>";

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
            return CliApplication.InvalidUsageExitCode;
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
                case "install-statusline":
                    installer.InstallStatusLine();
                    if (TryResolve(resolveDataDirectory) is { } enabledDataDirectory)
                        await new ClaudeLimitsConsentStore(ClaudeLimitsConsentStore.DefaultPath(enabledDataDirectory))
                            .EnableAsync(cancellationToken).ConfigureAwait(false);
                    await standardOutput.WriteLineAsync(
                            "Claude limits enabled through your Claude Code sign-in. The status line wrapper preserves your previous command.")
                        .ConfigureAwait(false);
                    return CliApplication.SuccessExitCode;
                case "uninstall-statusline":
                {
                    installer.UninstallStatusLine();
                    string? dataDirectory = TryResolve(resolveDataDirectory);
                    if (dataDirectory is not null)
                    {
                        if (!await DisableLimitsAsync(dataDirectory, clock ?? TimeProvider.System, cancellationToken).ConfigureAwait(false))
                        {
                            await standardError.WriteLineAsync("Claude limits are disabled, but the cached reading could not be removed.").ConfigureAwait(false);
                            return CliApplication.NoDataExitCode;
                        }
                    }
                    await standardOutput.WriteLineAsync(
                            "Claude Code status line wrapper removed; any previous status line was restored.")
                        .ConfigureAwait(false);
                    return CliApplication.SuccessExitCode;
                }
                default:
                    await standardError.WriteLineAsync(UsageText).ConfigureAwait(false);
                    return CliApplication.InvalidUsageExitCode;
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
            return CliApplication.NoDataExitCode;
        }
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
        byte[] input = await StandardInputPayload.ReadAsync(standardInput,
            ClaudeStatusLineRateLimits.MaximumPayloadBytes, cancellationToken).ConfigureAwait(false);
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

        return CliApplication.SuccessExitCode;
    }

    private static async Task<bool> DisableLimitsAsync(string dataDirectory, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        await new ClaudeLimitsConsentStore(ClaudeLimitsConsentStore.DefaultPath(dataDirectory))
            .DisableAsync(cancellationToken).ConfigureAwait(false);
        new ClaudeRateLimitStore(ClaudeRateLimitStore.DefaultPath(dataDirectory)).Delete();
        return await WindowsProviderCatalog.CreateComposition(dataDirectory, clock)
            .ClearClaudeLimitsCacheAsync(cancellationToken).ConfigureAwait(false);
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

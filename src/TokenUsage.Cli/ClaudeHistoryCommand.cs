using System.Text.Json;
using TokenUsage.Core.Usage;
using TokenUsage.Providers.Claude;
using TokenUsage.Runtime.Windows.Claude;

namespace TokenUsage.Cli;

public static class ClaudeHistoryCommand
{
    public const string UsageText = "Usage: tokenusage claude recover-history [--dry-run] [--format json]";
    private static readonly JsonSerializerOptions JsonOptions = new()
        { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public static async Task<int> ExecuteAsync(IReadOnlyList<string> arguments, TextWriter output,
        TextWriter error, Func<string?>? resolveDataDirectory = null, CancellationToken cancellationToken = default)
    {
        bool dryRun = false, json = false;
        for (int index = 0; index < arguments.Count; index++)
        {
            if (arguments[index] == "--dry-run" && !dryRun) dryRun = true;
            else if (arguments[index] == "--format" && !json && index + 1 < arguments.Count && arguments[++index] == "json") json = true;
            else { await error.WriteLineAsync(UsageText).ConfigureAwait(false); return 2; }
        }
        string? dataDirectory = ClaudeCommand.TryResolve(resolveDataDirectory);
        if (string.IsNullOrWhiteSpace(dataDirectory))
        { await error.WriteLineAsync("The TokenUsage data directory could not be resolved.").ConfigureAwait(false); return 1; }
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ConsoleCancelEventHandler handler = (_, args) => { args.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += handler;
        try
        {
            string database = Path.Combine(dataDirectory, "scanner", "usage.v1.db");
            var repository = dryRun
                ? await UsageRepository.OpenReadOnlyAsync(database, cancellation.Token).ConfigureAwait(false)
                : await UsageRepository.OpenAsync(database, cancellation.Token).ConfigureAwait(false);
            var source = new ClaudeUsageEventSource(TimeZoneInfo.Local.Id,
                checkpointPath: Path.Combine(dataDirectory, "scanner", "claude-usage.v1.json"));
            var service = new ClaudeHistoryRecoveryService(repository, source);
            var progress = new RecoveryProgress(error);
            ClaudeHistoryRecoveryReport result = await service.RecoverAsync(dryRun, progress, cancellation.Token).ConfigureAwait(false);
            if (json) await output.WriteLineAsync(JsonSerializer.Serialize(result, JsonOptions)).ConfigureAwait(false);
            else
            {
                await output.WriteLineAsync($"{(dryRun ? "Preview" : "Recovery")}: {result.Added:N0} added, {result.Existing:N0} existing, {result.Excluded:N0} excluded. {result.BytesRead / (1024 * 1024):N0} MiB read. {result.Blocker}").ConfigureAwait(false);
                var reasons = result.Exclusions;
                await output.WriteLineAsync($"Exclusions: retention {reasons.OutsideRetention:N0}; retired detail {reasons.RetiredDetails:N0}; retired event keys {reasons.Tombstones:N0}; identity conflicts {reasons.ConflictingIdentity:N0}; price evidence conflicts {reasons.ConflictingPriceEvidence:N0}; future records {reasons.Future:N0}; unreadable records {reasons.UnreadableRecords:N0}.").ConfigureAwait(false);
            }
            return result.Completed ? 0 : 1;
        }
        catch (OperationCanceledException)
        { await error.WriteLineAsync("Recovery cancelled. Confirmed history and saved progress have been kept.").ConfigureAwait(false); return 130; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException)
        { await error.WriteLineAsync("Recovery could not finish. Confirmed history and saved progress have been kept.").ConfigureAwait(false); return 1; }
        finally { Console.CancelKeyPress -= handler; }
    }

    private sealed class RecoveryProgress(TextWriter output) : IProgress<ClaudeHistoryRecoveryProgress>
    {
        public void Report(ClaudeHistoryRecoveryProgress value) => output.WriteLine(
            $"{value.BytesRead / (1024 * 1024):N0} MiB · added {value.Added:N0} · existing {value.Existing:N0} · excluded {value.Excluded:N0} · pending files {value.PendingFiles:N0}");
    }
}

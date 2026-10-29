using TokenUsage.Core.Usage;
using TokenUsage.Providers.Claude;

namespace TokenUsage.Runtime.Windows.Claude;

public sealed record ClaudeHistoryRecoveryProgress(int Added, int Existing, int Excluded,
    long BytesRead, int Batches, int PendingFiles)
{
    public int UnpricedGrowth { get; init; }
    public UsageRecoveryExclusionSummary Exclusions { get; init; } = UsageRecoveryExclusionSummary.Empty;
}

public sealed record ClaudeHistoryRecoveryReport(int Added, int Existing, int Excluded,
    long BytesRead, int Batches, bool Completed, string? Blocker)
{
    public int UnpricedGrowth { get; init; }
    public UsageRecoveryExclusionSummary Exclusions { get; init; } = UsageRecoveryExclusionSummary.Empty;
}

public sealed class ClaudeHistoryRecoveryService(UsageRepository repository,
    ClaudeUsageEventSource source, TimeProvider? clock = null)
{
    public async Task<ClaudeHistoryRecoveryReport> RecoverAsync(bool dryRun = false,
        IProgress<ClaudeHistoryRecoveryProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        TimeProvider time = clock ?? TimeProvider.System;
        ClaudeUsageEventSource history = source.CreateHistorySource(dryRun, time);
        var added = new HashSet<string>(StringComparer.Ordinal);
        var existing = new HashSet<string>(StringComparer.Ordinal);
        var reasons = new Dictionary<string, UsageRecoveryExclusionKind>(StringComparer.Ordinal);
        int unreadableRecords = 0;
        int futureMessages = 0;
        int batches = 0;
        int unpricedGrowth = 0;
        int idleFiles = 0;
        bool readErrors = false;
        long bytes = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            UsageSourceReadResult read = await history.ReadAsync(cancellationToken).ConfigureAwait(false);
            ClaudeUsageScanProgress scan = history.LastScanProgress;
            UsageRecoveryResult? recovered = null;
            bool canAdvance = true;
            async Task<bool> PersistAsync()
            {
                recovered = await repository.RecoverAgentEventsAsync(history.AgentId, read.Events,
                    time.GetUtcNow().ToUniversalTime(), dryRun, cancellationToken).ConfigureAwait(false);
                canAdvance = recovered.Exclusions.ConflictingIdentity == 0
                    && recovered.Exclusions.ConflictingPriceEvidence == 0;
                return canAdvance;
            }
            if (read.Checkpoint is { } checkpoint)
                await checkpoint.PersistAsync(PersistAsync, cancellationToken).ConfigureAwait(false);
            else await PersistAsync().ConfigureAwait(false);
            batches++;
            bytes += scan.BytesRead;
            readErrors |= scan.HasReadErrors;
            unreadableRecords = scan.RetainedUnreadableRecords;
            futureMessages = scan.DeferredFutureMessages;
            foreach (UsageEventKey key in recovered!.AddedKeys) added.Add(key.Value);
            foreach (UsageEventKey key in recovered.ExistingKeys) existing.Add(key.Value);
            foreach (var pair in recovered.ExcludedReasons) reasons[pair.Key] = pair.Value;
            foreach (UsageEventKey key in recovered.AddedKeys.Concat(recovered.ExistingKeys)) reasons.Remove(key.Value);
            existing.ExceptWith(added);
            unpricedGrowth += recovered.UnpricedGrowth;
            UsageRecoveryExclusionSummary exclusions = Exclusions();
            progress?.Report(new(added.Count, existing.Count, exclusions.Total,
                bytes, batches, scan.PendingFiles) { UnpricedGrowth = unpricedGrowth, Exclusions = exclusions });
            if (!canAdvance)
                return Report(false, "Some Claude updates conflict with recorded history or pricing evidence; source progress was preserved.");
            if (read.Issue == UsageSourceIssueKind.RootUnavailable)
                return Report(false, "Claude source files are unavailable.");
            if (scan.PendingFiles == 0)
                return Report(!readErrors,
                    readErrors ? "Some Claude source records could not be read." : null);
            idleFiles = scan.BytesAdvanced == 0 ? idleFiles + scan.FilesRead : 0;
            if (scan.BytesAdvanced == 0 && (scan.FilesRead == 0
                || idleFiles >= scan.FilesRead + scan.PendingFiles))
                return Report(false, "Claude history scan cannot advance within the current read budget or source access.");
        }

        UsageRecoveryExclusionSummary Exclusions()
        {
            UsageRecoveryExclusionSummary summary = UsageRecoveryExclusionSummary.FromReasons(reasons.Values, unreadableRecords);
            return summary with { Future = Math.Max(summary.Future, futureMessages) };
        }
        ClaudeHistoryRecoveryReport Report(bool completed, string? blocker)
        {
            UsageRecoveryExclusionSummary exclusions = Exclusions();
            return new(added.Count, existing.Count, exclusions.Total, bytes, batches, completed, blocker)
            { UnpricedGrowth = unpricedGrowth, Exclusions = exclusions };
        }
    }
}

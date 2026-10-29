using System.Text;
using System.Text.Json;
using TokenUsage.Core.Usage;
using TokenUsage.Providers.Claude;
using TokenUsage.Runtime.Windows.Claude;

namespace TokenUsage.Platform.Windows.Tests.Claude;

public sealed class ClaudeHistoryRecoveryTests
{
    [Fact]
    public async Task RecoveryScansOldFilesInBatchesDryRunIsReadOnlyAndStreamingCostFinishesCorrectly()
    {
        string root = Path.Combine(Path.GetTempPath(), "tokenusage-claude-recovery", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "projects"));
        try
        {
            DateTimeOffset occurred = DateTimeOffset.UtcNow.AddMinutes(-3);
            string Line(int output) => JsonSerializer.Serialize(new
            {
                type = "assistant", timestamp = occurred.ToString("O"),
                message = new { id = "stream", model = "claude-sonnet-4-6",
                    usage = new { input_tokens = 10, output_tokens = output } },
            });
            string first = Line(2);
            string last = Line(8);
            string path = Path.Combine(root, "projects", "history.jsonl");
            await File.WriteAllTextAsync(path, first + Environment.NewLine + last + Environment.NewLine);
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-60));
            string checkpoint = Path.Combine(root, "checkpoint.json");
            long budget = Encoding.UTF8.GetByteCount(first + Environment.NewLine);
            var source = new ClaudeUsageEventSource("UTC", configDirectoryOverride: root,
                maximumFileBytes: budget, maximumReadBytes: budget, checkpointPath: checkpoint);
            Assert.Empty((await source.ReadAsync()).Events);
            UsageRepository repository = await UsageRepository.OpenAsync(Path.Combine(root, "usage.db"));
            var service = new ClaudeHistoryRecoveryService(repository, source);
            var readOnlyService = new ClaudeHistoryRecoveryService(
                await UsageRepository.OpenReadOnlyAsync(Path.Combine(root, "usage.db")), source);
            ClaudeHistoryRecoveryReport preview = await readOnlyService.RecoverAsync(dryRun: true);
            Assert.True(preview.Completed);
            Assert.Equal(1, preview.Added);
            Assert.Equal(2, preview.Batches);
            Assert.False(File.Exists(checkpoint));
            Assert.Empty(await repository.QueryUsageEventsAsync(occurred.AddSeconds(-1), occurred.AddSeconds(1)));

            using var cancellation = new CancellationTokenSource();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RecoverAsync(
                progress: new CancelAfterBatch(cancellation), cancellationToken: cancellation.Token));
            UsageEvent partial = Assert.Single(await repository.QueryUsageEventsAsync(occurred.AddSeconds(-1), occurred.AddSeconds(1)));
            Assert.Equal(2, partial.Tokens.Output);
            Assert.True(File.Exists(checkpoint));
            ClaudeHistoryRecoveryReport actual = await service.RecoverAsync();
            Assert.True(actual.Completed);
            Assert.Equal(0, actual.Added);
            Assert.Equal(1, actual.Existing);
            Assert.Equal(1, actual.Batches);
            UsageEvent final = Assert.Single(await repository.QueryUsageEventsAsync(occurred.AddSeconds(-1), occurred.AddSeconds(1)));
            Assert.Equal(8, final.Tokens.Output);
            CostObservation expected = ClaudePricingCatalog.Resolve("claude-sonnet-4-6", occurred,
                final.Tokens, 0, 0, null, false);
            Assert.Equal(expected.EstimatedCostUsd, final.Cost.EstimatedCostUsd);
            ClaudeHistoryRecoveryReport repeat = await service.RecoverAsync();
            Assert.True(repeat.Completed);
            Assert.Equal(0, repeat.Added);
            Assert.Equal(0, repeat.BytesRead);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
    [Fact]
    public async Task UnreadableRecordCountsSurviveAnExhaustedCheckpoint()
    {
        string root = Path.Combine(Path.GetTempPath(), "tokenusage-claude-unreadable", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "projects"));
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "projects", "invalid.jsonl"),
                "{\"usage\": malformed}" + Environment.NewLine);
            UsageRepository repository = await UsageRepository.OpenAsync(Path.Combine(root, "usage.db"));
            var source = new ClaudeUsageEventSource("UTC", configDirectoryOverride: root,
                checkpointPath: Path.Combine(root, "checkpoint.json"));
            var service = new ClaudeHistoryRecoveryService(repository, source);
            ClaudeHistoryRecoveryReport first = await service.RecoverAsync();
            Assert.False(first.Completed);
            Assert.Equal(1, first.Exclusions.UnreadableRecords);
            ClaudeHistoryRecoveryReport repeat = await service.RecoverAsync();
            Assert.False(repeat.Completed);
            Assert.Equal(0, repeat.BytesRead);
            Assert.Equal(1, repeat.Exclusions.UnreadableRecords);
            Assert.Equal(1, repeat.Excluded);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class CancelAfterBatch(CancellationTokenSource cancellation) : IProgress<ClaudeHistoryRecoveryProgress>
    {
        public void Report(ClaudeHistoryRecoveryProgress value) => cancellation.Cancel();
    }

}

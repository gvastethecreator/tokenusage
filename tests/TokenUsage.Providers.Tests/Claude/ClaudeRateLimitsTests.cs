using System.Text;
using TokenUsage.Core.Providers;
using TokenUsage.Providers.Claude;

namespace TokenUsage.Providers.Tests.Claude;

public sealed class ClaudeRateLimitsTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    // Shape from https://code.claude.com/docs/en/statusline (available data), trimmed.
    private const string Payload =
        """
        {
          "session_id": "abc123",
          "transcript_path": "C:/Users/me/.claude/projects/x/abc123.jsonl",
          "cwd": "C:/work/secret-project",
          "model": { "id": "claude-opus-5-5", "display_name": "Opus" },
          "cost": { "total_cost_usd": 0.01234 },
          "rate_limits": {
            "five_hour": { "used_percentage": 23.5, "resets_at": 1790265600 },
            "seven_day": { "used_percentage": 41.2, "resets_at": 1790784000 }
          }
        }
        """;

    [Fact]
    public void ParsesOnlyTheRateLimitWindows()
    {
        Assert.True(ClaudeStatusLineRateLimits.TryParse(Utf8(Payload), Now, out ClaudeRateLimitSnapshot? reading));

        Assert.NotNull(reading);
        Assert.Equal(Now, reading.ObservedAtUtc);
        Assert.Collection(
            reading.Windows,
            window =>
            {
                Assert.Equal("five_hour", window.Key);
                Assert.Equal(23.5m, window.UsedPercent);
                Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1790265600), window.ResetsAtUtc);
            },
            window =>
            {
                Assert.Equal("seven_day", window.Key);
                Assert.Equal(41.2m, window.UsedPercent);
            });
    }

    [Theory]
    [InlineData("""{ "model": { "id": "x" } }""")]
    [InlineData("""{ "rate_limits": null }""")]
    [InlineData("not json")]
    [InlineData("")]
    public void MissingRateLimitsAreNotAReading(string payload)
    {
        Assert.False(ClaudeStatusLineRateLimits.TryParse(Utf8(payload), Now, out ClaudeRateLimitSnapshot? reading));
        Assert.Null(reading);
    }

    [Fact]
    public void MalformedWindowsAreSkippedWithoutDroppingValidOnes()
    {
        const string payload =
            """
            { "rate_limits": {
                "five_hour": { "used_percentage": "23", "resets_at": 1790265600 },
                "seven_day": { "used_percentage": -1, "resets_at": 1790784000 },
                "spend_limit": { "used_percentage": 112.5, "resets_at": 1790784000 }
            } }
            """;

        Assert.True(ClaudeStatusLineRateLimits.TryParse(Utf8(payload), Now, out ClaudeRateLimitSnapshot? reading));

        ClaudeRateLimitWindow window = Assert.Single(reading!.Windows);
        Assert.Equal("spend_limit", window.Key);
        Assert.Equal(112.5m, window.UsedPercent);
    }

    [Fact]
    public void StoreKeepsOnlyNumbersAndThrottlesUnchangedReadings()
    {
        string root = Path.Combine(Path.GetTempPath(), "tokenusage-claude-limits-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ClaudeRateLimitStore(ClaudeRateLimitStore.DefaultPath(root));
            Assert.True(ClaudeStatusLineRateLimits.TryParse(Utf8(Payload), Now, out ClaudeRateLimitSnapshot? reading));

            Assert.True(store.Save(reading!));
            Assert.False(store.Save(reading! with { ObservedAtUtc = Now.AddSeconds(30) }));
            Assert.True(store.Save(reading! with { ObservedAtUtc = Now.AddMinutes(2) }));

            string stored = File.ReadAllText(store.DocumentPath);
            Assert.DoesNotContain("secret-project", stored, StringComparison.Ordinal);
            Assert.DoesNotContain("abc123", stored, StringComparison.Ordinal);
            Assert.DoesNotContain("Opus", stored, StringComparison.Ordinal);
            ClaudeRateLimitSnapshot loaded = store.Load()!;
            Assert.Equal(Now.AddMinutes(2), loaded.ObservedAtUtc);
            Assert.Equal(reading!.Windows, loaded.Windows);

            store.Delete();
            Assert.Null(store.Load());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void MapperPublishesOpenWindowsAsQuotaMetrics()
    {
        var reading = new ClaudeRateLimitSnapshot(
            Now,
            [
                new("five_hour", 23.5m, Now.AddHours(2)),
                new("seven_day", 41.2m, Now.AddDays(3)),
                new("spend_limit", 10m, Now.AddMinutes(-1)),
            ]);

        ProviderSnapshot snapshot = ClaudeRateLimitSnapshotMapper.Map(reading, Now, "UTC")!;

        Assert.Equal("claude", snapshot.ProviderId.Value);
        Assert.Equal(Now, snapshot.SourceObservedAtUtc);
        ProgressMetricSnapshot[] windows = snapshot.Metrics.OfType<ProgressMetricSnapshot>().ToArray();
        Assert.Equal(
            [ClaudeRateLimitSnapshotMapper.FiveHourMetricId, ClaudeRateLimitSnapshotMapper.SevenDayMetricId],
            windows.Select(metric => metric.Id.Value));
        Assert.Equal(76.5m, windows[0].RemainingPercent);
        Assert.Equal(
            300m,
            snapshot.Metrics.OfType<ScalarMetricSnapshot>()
                .Single(metric => metric.Id.Value == "quota.five-hour.window-minutes").Value);
    }

    [Fact]
    public void MapperReturnsNothingOnceEveryWindowHasReset()
    {
        var reading = new ClaudeRateLimitSnapshot(Now.AddHours(-6), [new("five_hour", 90m, Now.AddHours(-1))]);

        Assert.Null(ClaudeRateLimitSnapshotMapper.Map(reading, Now, "UTC"));
    }

    private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);
}

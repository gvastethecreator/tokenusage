using System.Text.Json;
using TokenUsage.Core.Cache;
using TokenUsage.Core.Providers;
using TokenUsage.Providers.Claude;
using TokenUsage.Runtime.Windows.Claude;

namespace TokenUsage.Platform.Windows.Tests;

public sealed class ClaudeSwapSourceTests
{
    private static readonly TokenUsage.Core.Usage.HmacOpaqueKeyDeriver Keys = new(new byte[32]);

    private static object Account(int number, string email = "owner@example.test", string status = "ok",
        object? usage = null, object? lastGood = null, bool active = false) => new
    {
        number, email, organizationName = "Org", organizationUuid = "org-1", isOrganization = true, active,
        usageStatus = status, usage, usageFetchedAt = usage is null ? null : "2026-10-30T03:41:32Z",
        lastGoodUsage = lastGood, lastGoodFetchedAt = lastGood is null ? null : "2026-10-29T20:00:00Z",
    };

    private static readonly object Usage = new
    {
        fiveHour = new { pct = 42.0, resetsAt = "2026-10-30T04:19:59.894959+00:00" },
        sevenDay = new { pct = 16.0, resetsAt = "2026-11-03T12:59:59.894975+00:00", expectedPct = 37.3 },
        scoped = new[] { new { pct = 3.0, resetsAt = "2026-11-03T13:00:00+00:00", name = "Fable" } },
    };

    private static byte[] Inventory(params object[] accounts) =>
        JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, activeAccountNumber = 1, accounts });

    [Fact]
    public async Task ParsesIdentityStatusAndLimitsWithoutCredentials()
    {
        ClaudeSwapAccount[] accounts = ClaudeSwapSource.Parse(Inventory(
            Account(1, usage: Usage, active: true),
            Account(2, "second@example.test", "relogin_required"),
            Account(3, "third@example.test", "unavailable", lastGood: Usage)), Keys).ToArray();

        Assert.Equal(ProviderAccountStatus.Available, accounts[0].Status);
        Assert.Equal(ProviderAccountStatus.LoginRequired, accounts[1].Status);
        Assert.Null(accounts[1].LimitsJson);
        Assert.Equal(new DateTimeOffset(2026, 10, 29, 20, 0, 0, TimeSpan.Zero), accounts[2].ObservedAtUtc);

        // The slot number is a label; the email and organization are the identity.
        ClaudeSwapAccount moved = Assert.Single(ClaudeSwapSource.Parse(Inventory(Account(5, usage: Usage, active: true)), Keys));
        Assert.Equal(accounts[0].InstanceKey, moved.InstanceKey);

        ProviderOutcome outcome = await new ClaudeSwapSource().RefreshAsync(accounts[0],
            _ => Task.FromResult<IReadOnlyList<TokenUsage.Runtime.Windows.Accounts.SwapAccount>>([]),
            new RefreshContext(TimeProvider.System), CancellationToken.None);
        ProviderSnapshot snapshot = Assert.IsType<ProviderOutcome.Success>(outcome).Snapshot;
        ProgressMetricSnapshot[] windows = snapshot.Metrics.OfType<ProgressMetricSnapshot>().ToArray();
        Assert.Equal(42m, windows.Single(metric => metric.Id.Value == ClaudeRateLimitSnapshotMapper.FiveHourMetricId).Used);
        Assert.Equal(16m, windows.Single(metric => metric.Id.Value == ClaudeRateLimitSnapshotMapper.SevenDayMetricId).Used);
        Assert.Single(windows, metric => metric.Id.Value.StartsWith(ClaudeUsageSnapshotMapper.ScopedWeeklyMetricPrefix, StringComparison.Ordinal));
        Assert.Equal(new DateTimeOffset(2026, 10, 30, 3, 41, 32, TimeSpan.Zero), snapshot.SourceObservedAtUtc);
    }

    [Fact]
    public void RejectsAmbiguousOrUnsupportedInventories()
    {
        Assert.Throws<IOException>(() => ClaudeSwapSource.Parse(Inventory(Account(1), Account(1, "other@example.test")), Keys));
        Assert.Throws<IOException>(() => ClaudeSwapSource.Parse(Inventory(Account(1), Account(2)), Keys));
        Assert.Throws<IOException>(() => ClaudeSwapSource.Parse(Inventory(Account(1, active: true), Account(2, "b@example.test", active: true)), Keys));
        Assert.Throws<IOException>(() => ClaudeSwapSource.Parse("{\"schemaVersion\":2,\"accounts\":[]}"u8.ToArray(), Keys));
    }
}

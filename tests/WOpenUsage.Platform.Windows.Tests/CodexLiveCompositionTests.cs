using WOpenUsage.App.Services;
using WOpenUsage.App.ViewModels;
using WOpenUsage.App.ViewModels.Sample;
using WOpenUsage.Core.Cache;
using WOpenUsage.Core.Providers;
using WOpenUsage.Platform.Windows.Processes;
using WOpenUsage.Providers.Codex;

namespace WOpenUsage.Platform.Windows.Tests;

public sealed class CodexLiveCompositionTests
{
    private const string FakeModeEnvironmentVariable = "WOPENUSAGE_FAKE_CODEX_MODE";

    [Fact]
    public async Task FakeProcessFlowsThroughProtocolCacheAndDashboardWithoutAccountData()
    {
        using var folder = new TemporaryFolder();
        string executable = GetFakeCodexPath();
        string? previousExecutable = Environment.GetEnvironmentVariable(
            CodexExecutableResolver.OverrideEnvironmentVariable);
        string? previousMode = Environment.GetEnvironmentVariable(
            FakeModeEnvironmentVariable);
        Environment.SetEnvironmentVariable(
            CodexExecutableResolver.OverrideEnvironmentVariable,
            executable);
        Environment.SetEnvironmentVariable(FakeModeEnvironmentVariable, "quota");

        try
        {
            TimeProvider clock = TimeProvider.System;
            var factory = new CodexAppServerQuotaClientFactory(
                clock,
                new CodexClientOptions(
                    "wopenusage-test",
                    "0.1.0",
                    requestTimeout: TimeSpan.FromSeconds(5)));
            Assert.Equal(
                CodexClientAvailability.Available,
                await factory.DetectAsync(CancellationToken.None));
            var coordinator = new CodexRefreshCoordinator(folder.Path, clock, factory);

            IReadOnlyList<CacheFirstEvent> events = await CollectAsync(
                coordinator.RunAsync(forceRefresh: true, CancellationToken.None));

            Assert.IsType<SnapshotCacheReadResult.Empty>(
                Assert.IsType<CacheFirstEvent.CachePublished>(events[0]).ReadResult);
            CacheFirstEvent.ProviderCompleted completed =
                Assert.IsType<CacheFirstEvent.ProviderCompleted>(events[1]);
            ProviderOutcome.Success success =
                Assert.IsType<ProviderOutcome.Success>(completed.Outcome);
            Assert.Equal(CacheUpdateStatus.Updated, completed.CacheStatus);

            SampleDashboardSnapshot dashboard = CodexDashboardProjector.Create(
                success.Snapshot,
                clock,
                GetString);
            SampleProviderCard card = Assert.Single(dashboard.Providers);
            Assert.False(dashboard.HasSpend);
            Assert.Equal("Plus", card.PlanLabel);
            Assert.Equal(2, card.Windows.Count);
            Assert.Equal(58d, card.Windows[0].RemainingPercent);
            Assert.True(card.Windows[0].HasPace);
            Assert.True(card.Windows[0].IsPaceBehind);
            Assert.True(card.Windows[1].HasPace);
            Assert.True(card.Windows[1].IsPaceWithinLimit);
            Assert.Collection(
                card.Metrics,
                metric => Assert.Equal("1200 tokens", metric.Value.Replace(",", string.Empty).Replace(".", string.Empty)),
                metric => Assert.Equal("300 tokens", metric.Value),
                metric => Assert.Equal("1500 tokens", metric.Value.Replace(",", string.Empty).Replace(".", string.Empty)),
                metric => Assert.Equal("1500 tokens", metric.Value.Replace(",", string.Empty).Replace(".", string.Empty)));

            string visibleText = string.Join(
                '\n',
                [
                    .. card.Windows.Select(window =>
                        $"{window.Title}|{window.RemainingText}|{window.ResetText}|{window.PaceText}|{window.AutomationName}"),
                    .. card.Metrics.Select(metric => $"{metric.Label}|{metric.Value}"),
                ]);
            Assert.DoesNotContain("private-live@example.invalid", visibleText, StringComparison.Ordinal);
            Assert.DoesNotContain("auth.json", visibleText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Bearer", visibleText, StringComparison.OrdinalIgnoreCase);

            var store = new SnapshotStore(
                Path.Combine(folder.Path, SnapshotStore.DefaultFileName),
                clock);
            SnapshotCacheReadResult.Loaded cached =
                Assert.IsType<SnapshotCacheReadResult.Loaded>(await store.LoadAsync());
            ProviderSnapshot cachedSnapshot = Assert.Single(cached.Snapshots);
            Assert.Equal("codex", cachedSnapshot.ProviderId.Value);
            ScalarMetricSnapshot todayUsage = Assert.Single(
                cachedSnapshot.Metrics.OfType<ScalarMetricSnapshot>(),
                metric => metric.Id.Value == "usage.tokens.today");
            Assert.Equal(1200m, todayUsage.Value);
            Assert.Equal("tokens", todayUsage.Unit);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                CodexExecutableResolver.OverrideEnvironmentVariable,
                previousExecutable);
            Environment.SetEnvironmentVariable(
                FakeModeEnvironmentVariable,
                previousMode);
        }
    }

    [Fact]
    public async Task InvalidExecutableOverrideFailsClosedWithoutStartingAClient()
    {
        string? previous = Environment.GetEnvironmentVariable(
            CodexExecutableResolver.OverrideEnvironmentVariable);
        Environment.SetEnvironmentVariable(
            CodexExecutableResolver.OverrideEnvironmentVariable,
            Path.Combine(Path.GetTempPath(), "private-account", "codex.exe"));

        try
        {
            var factory = new CodexAppServerQuotaClientFactory(TimeProvider.System);

            Assert.Equal(
                CodexClientAvailability.Unavailable,
                await factory.DetectAsync(CancellationToken.None));
            await Assert.ThrowsAsync<CodexClientUnavailableException>(() =>
                factory.CreateAsync(CancellationToken.None));
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                CodexExecutableResolver.OverrideEnvironmentVariable,
                previous);
        }
    }

    private static string GetFakeCodexPath()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "FakeCodex", "codex.exe");
        Assert.True(File.Exists(path), $"Fake Codex executable is missing: {path}");
        return path;
    }

    private static async Task<IReadOnlyList<CacheFirstEvent>> CollectAsync(
        IAsyncEnumerable<CacheFirstEvent> source)
    {
        var events = new List<CacheFirstEvent>();
        await foreach (CacheFirstEvent item in source)
        {
            events.Add(item);
        }

        return events;
    }

    private static string GetString(string key) => key switch
    {
        "CodexQuotaPeriod" => "Current Codex limits",
        "CodexPlanUnknown" => "Plan unavailable",
        "CodexUsageFormat" => "{0}% remaining · {1}% used",
        "CodexWindowPrimary" => "Primary limit",
        "CodexWindowSecondary" => "Secondary limit",
        "CodexWindowAdditionalPrimaryFormat" => "Additional limit {0}",
        "CodexWindowAdditionalSecondaryFormat" => "Additional limit {0}",
        "CodexResetUnknown" => "Reset time unavailable",
        "CodexResetDue" => "Reset due",
        "SampleWindowSession" => "Session",
        "SampleWindowWeekly" => "Weekly",
        "SampleResetHoursFormat" => "Resets in {0} h",
        "SampleResetDaysFormat" => "Resets in {0} d",
        "SampleResetDaysHoursFormat" => "Resets in {0} d {1} h",
        "CodexPartialUsageNotice" => "Daily token usage is incomplete.",
        "CodexCapabilityUsage" => "Quota and local usage",
        "CodexUsageToday" => "Today",
        "CodexUsageYesterday" => "Yesterday",
        "CodexUsageLast7Days" => "Last 7 days",
        "CodexUsageLast30Days" => "Last 30 days",
        "CodexUsageMissing" => "No data",
        "CodexTokenCountFormat" => "{0:N0} tokens",
        "CodexTokenCountSingular" => "{0:N0} token",
        "CodexPaceAheadFormat" => "{0}% projected · below pace",
        "CodexPaceOnTrackFormat" => "{0}% projected · on pace",
        "CodexPaceBehindFormat" => "{0}% projected · above pace",
        "CodexPaceBehindEtaFormat" => "{0}% projected · limit in {1}",
        "CodexDurationHoursFormat" => "{0} h",
        "CodexDurationMinutesFormat" => "{0} min",
        "CodexDurationHoursMinutesFormat" => "{0} h {1} min",
        _ => throw new InvalidOperationException($"Unexpected resource '{key}'."),
    };

    private sealed class TemporaryFolder : IDisposable
    {
        public TemporaryFolder()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "wopenusage-live-composition-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}

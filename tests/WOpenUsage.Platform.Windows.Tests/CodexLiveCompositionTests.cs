using WOpenUsage.App.ViewModels;
using WOpenUsage.App.ViewModels.Sample;
using WOpenUsage.Core.Cache;
using WOpenUsage.Core.Providers;
using WOpenUsage.Platform.Windows.Processes;
using WOpenUsage.Providers.Codex;
using WOpenUsage.Runtime.Windows.Codex;

namespace WOpenUsage.Platform.Windows.Tests;

public sealed class CodexLiveCompositionTests
{
    private const string FakeModeEnvironmentVariable = "WOPENUSAGE_FAKE_CODEX_MODE";
    private const string FakeNowEnvironmentVariable = "WOPENUSAGE_FAKE_NOW_UTC";
    private const string FakePathMarkerEnvironmentVariable = "WOPENUSAGE_FAKE_PATH_MARKER";
    private const string RealSmokeEnvironmentVariable = "WOPENUSAGE_RUN_REAL_CODEX_SMOKE";
    private static readonly DateTimeOffset FakeNow =
        new(2026, 7, 22, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task FakeProcessFlowsThroughProtocolCacheAndDashboardWithoutAccountData()
    {
        using var folder = new TemporaryFolder();
        string executable = GetFakeCodexPath();
        string? previousExecutable = Environment.GetEnvironmentVariable(
            CodexExecutableResolver.OverrideEnvironmentVariable);
        string? previousMode = Environment.GetEnvironmentVariable(
            FakeModeEnvironmentVariable);
        string? previousNow = Environment.GetEnvironmentVariable(
            FakeNowEnvironmentVariable);
        Environment.SetEnvironmentVariable(
            CodexExecutableResolver.OverrideEnvironmentVariable,
            executable);
        Environment.SetEnvironmentVariable(FakeModeEnvironmentVariable, "quota");
        Environment.SetEnvironmentVariable(
            FakeNowEnvironmentVariable,
            FakeNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));

        try
        {
            TimeProvider clock = new FixedTimeProvider(FakeNow);
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
            Assert.Empty(card.Metrics);
            Assert.Collection(
                card.SecondaryMetricItems,
                metric => Assert.Equal("1200 tokens", metric.Value.Replace(",", string.Empty).Replace(".", string.Empty)),
                metric => Assert.Equal("300 tokens", metric.Value),
                metric => Assert.Equal("1500 tokens", metric.Value.Replace(",", string.Empty).Replace(".", string.Empty)),
                metric => Assert.Equal("1500 tokens", metric.Value.Replace(",", string.Empty).Replace(".", string.Empty)));

            string visibleText = string.Join(
                '\n',
                [
                    .. card.Windows.Select(window =>
                        $"{window.Title}|{window.RemainingText}|{window.ResetText}|{window.PaceText}|{window.AutomationName}"),
                    .. card.SecondaryMetricItems.Select(metric => $"{metric.Label}|{metric.Value}"),
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
            Environment.SetEnvironmentVariable(
                FakeNowEnvironmentVariable,
                previousNow);
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

    [Fact]
    public async Task FactoryWaitsForTheActiveProcessOwnerBeforeStartingAnother()
    {
        string executable = GetFakeCodexPath();
        string? previousExecutable = Environment.GetEnvironmentVariable(
            CodexExecutableResolver.OverrideEnvironmentVariable);
        Environment.SetEnvironmentVariable(
            CodexExecutableResolver.OverrideEnvironmentVariable,
            executable);

        try
        {
            var factory = new CodexAppServerQuotaClientFactory(TimeProvider.System);
            await using ICodexQuotaClient first =
                await factory.CreateAsync(CancellationToken.None);
            using var secondCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            Task<ICodexQuotaClient> secondTask =
                factory.CreateAsync(secondCancellation.Token);

            Assert.False(secondTask.IsCompleted);

            await first.DisposeAsync();
            await using ICodexQuotaClient second =
                await secondTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                CodexExecutableResolver.OverrideEnvironmentVariable,
                previousExecutable);
        }
    }

    [Fact]
    public async Task FakeProcessFailuresKeepLastGoodAndRecoverAfterBinaryPathChanges()
    {
        using var folder = new TemporaryFolder();
        using var binaryFolder = new TemporaryFolder();
        string executable = GetFakeCodexPath();
        string? previousExecutable = Environment.GetEnvironmentVariable(
            CodexExecutableResolver.OverrideEnvironmentVariable);
        string? previousMode = Environment.GetEnvironmentVariable(
            FakeModeEnvironmentVariable);
        string? previousMarker = Environment.GetEnvironmentVariable(
            FakePathMarkerEnvironmentVariable);

        try
        {
            Environment.SetEnvironmentVariable(
                CodexExecutableResolver.OverrideEnvironmentVariable,
                executable);
            Environment.SetEnvironmentVariable(FakeModeEnvironmentVariable, "quota");
            TimeProvider clock = TimeProvider.System;
            var factory = new CodexAppServerQuotaClientFactory(
                clock,
                new CodexClientOptions(
                    "wopenusage-recovery-test",
                    "0.1.0",
                    requestTimeout: TimeSpan.FromSeconds(2)));
            var coordinator = new CodexRefreshCoordinator(folder.Path, clock, factory);

            ProviderOutcome.Success initial = Assert.IsType<ProviderOutcome.Success>(
                Assert.IsType<CacheFirstEvent.ProviderCompleted>(
                    (await CollectAsync(coordinator.RunAsync(true, CancellationToken.None)))[1]).Outcome);

            Environment.SetEnvironmentVariable(FakeModeEnvironmentVariable, "crash");
            ProviderOutcome crash = Assert.IsType<CacheFirstEvent.ProviderCompleted>(
                (await CollectAsync(coordinator.RunAsync(true, CancellationToken.None)))[1]).Outcome;
            Assert.True(crash is ProviderOutcome.TransientFailure or ProviderOutcome.ContractFailure);
            Assert.Equal(initial.Snapshot.SourceObservedAtUtc, LastGood(crash)?.SourceObservedAtUtc);

            Environment.SetEnvironmentVariable(FakeModeEnvironmentVariable, "timeout");
            ProviderOutcome.TransientFailure timeout = Assert.IsType<ProviderOutcome.TransientFailure>(
                Assert.IsType<CacheFirstEvent.ProviderCompleted>(
                    (await CollectAsync(coordinator.RunAsync(true, CancellationToken.None)))[1]).Outcome);
            Assert.Equal(initial.Snapshot.SourceObservedAtUtc, timeout.LastGood?.SourceObservedAtUtc);

            Environment.SetEnvironmentVariable(FakeModeEnvironmentVariable, "contract");
            ProviderOutcome.ContractFailure contract = Assert.IsType<ProviderOutcome.ContractFailure>(
                Assert.IsType<CacheFirstEvent.ProviderCompleted>(
                    (await CollectAsync(coordinator.RunAsync(true, CancellationToken.None)))[1]).Outcome);
            Assert.Equal(initial.Snapshot.SourceObservedAtUtc, contract.LastGood?.SourceObservedAtUtc);

            string replacementDirectory = Path.Combine(binaryFolder.Path, "replacement");
            CopyDirectory(Path.GetDirectoryName(executable)!, replacementDirectory);
            string replacementExecutable = Path.Combine(replacementDirectory, "codex.exe");
            string pathMarker = Path.Combine(binaryFolder.Path, "started-path.txt");
            Environment.SetEnvironmentVariable(
                CodexExecutableResolver.OverrideEnvironmentVariable,
                replacementExecutable);
            Environment.SetEnvironmentVariable(FakeModeEnvironmentVariable, "quota");
            Environment.SetEnvironmentVariable(FakePathMarkerEnvironmentVariable, pathMarker);

            ProviderOutcome.Success recovered = Assert.IsType<ProviderOutcome.Success>(
                Assert.IsType<CacheFirstEvent.ProviderCompleted>(
                    (await CollectAsync(coordinator.RunAsync(true, CancellationToken.None)))[1]).Outcome);

            Assert.True(recovered.Snapshot.SourceObservedAtUtc >= initial.Snapshot.SourceObservedAtUtc);
            Assert.Equal("codex", recovered.Snapshot.ProviderId.Value);
            Assert.Equal(
                replacementExecutable,
                await File.ReadAllTextAsync(pathMarker),
                ignoreCase: true);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                CodexExecutableResolver.OverrideEnvironmentVariable,
                previousExecutable);
            Environment.SetEnvironmentVariable(
                FakeModeEnvironmentVariable,
                previousMode);
            Environment.SetEnvironmentVariable(
                FakePathMarkerEnvironmentVariable,
                previousMarker);
        }
    }

    [Fact]
    [Trait("Category", "OptIn")]
    public async Task RealCodexRecoverySmokeRestartsAfterAControlledClose()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable(RealSmokeEnvironmentVariable),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        using var folder = new TemporaryFolder();
        TimeProvider clock = TimeProvider.System;
        var factory = new CodexAppServerQuotaClientFactory(
            clock,
            new CodexClientOptions(
                "wopenusage-real-smoke",
                "0.1.0",
                requestTimeout: TimeSpan.FromSeconds(10)));
        Assert.Equal(
            CodexClientAvailability.Available,
            await factory.DetectAsync(CancellationToken.None));

        await using (ICodexQuotaClient client =
            await factory.CreateAsync(CancellationToken.None))
        {
            await client.HandshakeAsync(CancellationToken.None);
        }

        var coordinator = new CodexRefreshCoordinator(folder.Path, clock, factory);
        ProviderOutcome outcome = Assert.IsType<CacheFirstEvent.ProviderCompleted>(
            (await CollectAsync(coordinator.RunAsync(true, CancellationToken.None)))[1]).Outcome;
        ProviderSnapshot snapshot = outcome switch
        {
            ProviderOutcome.Success success => success.Snapshot,
            ProviderOutcome.PartialSuccess partial => partial.Snapshot,
            _ => throw new InvalidOperationException("The real Codex recovery smoke did not return a snapshot."),
        };

        Assert.Equal("codex", snapshot.ProviderId.Value);
        Assert.All(snapshot.Metrics, metric =>
            Assert.True(
                metric.Id.Value.StartsWith("quota.", StringComparison.Ordinal)
                || metric.Id.Value.StartsWith("usage.", StringComparison.Ordinal)));
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

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }
    }

    private static ProviderSnapshot? LastGood(ProviderOutcome outcome) => outcome switch
    {
        ProviderOutcome.TransientFailure failure => failure.LastGood,
        ProviderOutcome.ContractFailure failure => failure.LastGood,
        ProviderOutcome.Throttled throttled => throttled.LastGood,
        _ => null,
    };

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
        "ProviderSourceLabel" => "Source",
        "ProviderSourceOfficialLocalApi" => "Official local API",
        "ProviderObservedLabel" => "Updated",
        "ProviderObservedValueFormat" => "{0}",
        "ProviderDetailsTooltipFormat" => "Source: {0}. Updated: {1}.",
        "ProviderDetailsAutomationNameFormat" => "Details for {0}",
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

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

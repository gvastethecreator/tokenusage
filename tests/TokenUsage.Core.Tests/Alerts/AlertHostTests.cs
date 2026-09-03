using TokenUsage.Core.Alerts;
using TokenUsage.Core.Providers;

namespace TokenUsage.Core.Tests.Alerts;

public sealed class AlertHostTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FactsBuilderMapsProgressMetricsAndStaleness()
    {
        var clock = new FixedTimeProvider(Now);
        ProviderSnapshot snapshot = CreateSnapshot(
            used: 90m,
            limit: 100m,
            observedAt: Now.AddHours(-6));
        ProviderAlertFacts facts = AlertFactsBuilder.FromSnapshot(snapshot, clock, staleAfter: TimeSpan.FromHours(1));

        Assert.Equal("codex", facts.ProviderId.Value);
        Assert.True(facts.IsStale);
        Assert.False(facts.HasCredentialFailure);
        QuotaAlertFacts quota = Assert.Single(facts.Quotas);
        Assert.Equal(10m, quota.RemainingPercent);
    }

    [Fact]
    public void FactsBuilderProjectsExhaustionOnlyWhenDurationEvidenceIsPresent()
    {
        var clock = new FixedTimeProvider(Now);
        DataProvenance provenance = CreateProvenance();
        var snapshot = new ProviderSnapshot(
            new ProviderId("codex"),
            "Codex",
            "Plus",
            Now,
            Now,
            "UTC",
            [
                new ProgressMetricSnapshot(
                    new MetricId("quota.primary"),
                    75m,
                    100m,
                    Now.AddHours(12),
                    provenance),
                new ScalarMetricSnapshot(
                    new MetricId("quota.primary.window-minutes"),
                    1440m,
                    "minutes",
                    provenance),
            ],
            CoverageKind.Complete,
            1);

        QuotaAlertFacts quota = Assert.Single(AlertFactsBuilder.FromSnapshot(snapshot, clock).Quotas);

        Assert.Equal(Now.AddHours(4), quota.ProjectedExhaustionAtUtc);
    }

    [Fact]
    public void AlertActivationArgumentsRoundTripAndRejectMismatchedRoutes()
    {
        var original = new AlertActivationTarget(
            AlertActivationArea.QuotaReport,
            "codex",
            "quota.primary");

        Assert.True(AlertActivationTarget.TryParse(original.ToArguments(), out AlertActivationTarget? parsed));
        Assert.Equal(original, parsed);
        Assert.False(AlertActivationTarget.TryParse(
            new Dictionary<string, string>
            {
                ["area"] = "status",
                ["provider"] = "codex",
                ["metric"] = "quota.primary",
            },
            out _));
    }

    [Fact]
    public void FactsBuilderMarksCredentialFailureFromNotConfiguredOutcome()
    {
        var clock = new FixedTimeProvider(Now);
        ProviderAlertFacts facts = AlertFactsBuilder.FromOutcome(
            new ProviderId("codex"),
            new ProviderOutcome.NotConfigured("missing session"),
            clock);

        Assert.True(facts.HasCredentialFailure);
        Assert.Empty(facts.Quotas);
    }

    [Fact]
    public async Task HostEmitsIntentOncePerConditionKey()
    {
        using var folder = new TemporaryFolder();
        var settingsStore = new AlertSettingsStore(Path.Combine(folder.Root, AlertSettingsStore.DefaultFileName));
        var decisionStore = new AlertDecisionStore(Path.Combine(folder.Root, AlertDecisionStore.DefaultFileName));
        await settingsStore.SaveAsync(new AlertSettings(
            enabled: true,
            quotaThresholdPercent: 20,
            quotaThresholdEnabled: true,
            exhaustionForecastEnabled: true,
            staleDataEnabled: false,
            credentialFailureEnabled: false));

        var host = new AlertHost(decisionStore, settingsStore);
        ProviderAlertFacts facts = new(
            new ProviderId("codex"),
            isStale: false,
            hasCredentialFailure: false,
            quotas:
            [
                new QuotaAlertFacts(
                    new MetricId("session"),
                    remainingPercent: 10m,
                    resetsAtUtc: Now.AddDays(1),
                    projectedExhaustionAtUtc: null),
            ]);

        IReadOnlyList<AlertNotificationIntent> first = await host.EvaluateAsync(Now, [facts]);
        Assert.Single(first);
        Assert.Equal(AlertKind.QuotaThreshold, first[0].Kind);

        IReadOnlyList<AlertNotificationIntent> second = await host.EvaluateAsync(Now.AddMinutes(1), [facts]);
        Assert.Empty(second);
    }

    [Fact]
    public async Task NewQuotaWindowCanNotifyAfterAlertsWereTemporarilyDisabled()
    {
        using var folder = new TemporaryFolder();
        var settingsStore = new AlertSettingsStore(Path.Combine(folder.Root, AlertSettingsStore.DefaultFileName));
        var decisionStore = new AlertDecisionStore(Path.Combine(folder.Root, AlertDecisionStore.DefaultFileName));
        var host = new AlertHost(decisionStore, settingsStore);
        AlertSettings enabled = CreateSettings(enabled: true);
        await settingsStore.SaveAsync(enabled);

        ProviderAlertFacts firstWindow = CreateFacts(10m);
        Assert.Single(await host.EvaluateAsync(Now, [firstWindow]));

        await settingsStore.SaveAsync(CreateSettings(enabled: false));
        ProviderAlertFacts nextWindow = CreateFacts(10m, Now.AddDays(8));
        Assert.Empty(await host.EvaluateAsync(Now.AddMinutes(1), [nextWindow]));
        Assert.Single((await decisionStore.LoadAsync()).NotifiedConditionKeys);

        await settingsStore.SaveAsync(enabled);
        Assert.Single(await host.EvaluateAsync(Now.AddMinutes(2), [nextWindow]));
        Assert.Equal(2, (await decisionStore.LoadAsync()).NotifiedConditionKeys.Count);
    }

    [Fact]
    public async Task HostEmitsOneExhaustionForecastPerWindow()
    {
        using var folder = new TemporaryFolder();
        var settingsStore = new AlertSettingsStore(Path.Combine(folder.Root, AlertSettingsStore.DefaultFileName));
        var decisionStore = new AlertDecisionStore(Path.Combine(folder.Root, AlertDecisionStore.DefaultFileName));
        await settingsStore.SaveAsync(new AlertSettings(
            enabled: true,
            quotaThresholdPercent: 20,
            quotaThresholdEnabled: false,
            exhaustionForecastEnabled: true,
            staleDataEnabled: false,
            credentialFailureEnabled: false));
        var host = new AlertHost(decisionStore, settingsStore);
        var facts = new ProviderAlertFacts(
            new ProviderId("codex"),
            isStale: false,
            hasCredentialFailure: false,
            [
                new QuotaAlertFacts(
                    new MetricId("quota.primary"),
                    remainingPercent: 25m,
                    resetsAtUtc: Now.AddHours(12),
                    projectedExhaustionAtUtc: Now.AddHours(4)),
            ]);

        AlertNotificationIntent intent = Assert.Single(await host.EvaluateAsync(Now, [facts]));
        Assert.Equal(AlertKind.ExhaustionForecast, intent.Kind);
        Assert.Equal(Now.AddHours(4), intent.Candidate.ProjectedExhaustionAtUtc);
        Assert.Empty(await host.EvaluateAsync(Now.AddMinutes(1), [facts]));
    }

    [Fact]
    public async Task HostLeavesTheDecisionRecordAloneWhenNothingCrossesAThreshold()
    {
        using var folder = new TemporaryFolder();
        string decisionPath = Path.Combine(folder.Root, AlertDecisionStore.DefaultFileName);
        var settingsStore = new AlertSettingsStore(
            Path.Combine(folder.Root, AlertSettingsStore.DefaultFileName));
        var decisionStore = new AlertDecisionStore(decisionPath);
        await settingsStore.SaveAsync(new AlertSettings(
            enabled: true,
            quotaThresholdPercent: 20,
            quotaThresholdEnabled: true,
            exhaustionForecastEnabled: true,
            staleDataEnabled: false,
            credentialFailureEnabled: false));

        // A file the store cannot parse is quarantined the moment it is read, so the file staying
        // in place is proof the read never happened.
        await File.WriteAllTextAsync(decisionPath, "{ not json");
        var host = new AlertHost(decisionStore, settingsStore);

        IReadOnlyList<AlertNotificationIntent> quiet = await host.EvaluateAsync(
            Now,
            [CreateFacts(remainingPercent: 90m)]);

        Assert.Empty(quiet);
        Assert.True(File.Exists(decisionPath));

        IReadOnlyList<AlertNotificationIntent> crossing = await host.EvaluateAsync(
            Now,
            [CreateFacts(remainingPercent: 5m)]);

        Assert.Single(crossing);
    }

    [Fact]
    public void EvaluatorStillProducesThresholdCandidates()
    {
        var settings = new AlertSettings(
            enabled: true,
            quotaThresholdPercent: 25,
            quotaThresholdEnabled: true,
            exhaustionForecastEnabled: false,
            staleDataEnabled: false,
            credentialFailureEnabled: false);
        var facts = new ProviderAlertFacts(
            new ProviderId("codex"),
            isStale: false,
            hasCredentialFailure: false,
            [
                new QuotaAlertFacts(
                    new MetricId("session"),
                    remainingPercent: 15m,
                    resetsAtUtc: Now.AddHours(2),
                    projectedExhaustionAtUtc: null),
            ]);

        IReadOnlyList<AlertCandidate> candidates = AlertEvaluator.Evaluate(settings, Now, [facts]);
        Assert.Single(candidates);
        Assert.Equal(AlertKind.QuotaThreshold, candidates[0].ConditionKey.Kind);
    }

    [Fact]
    public async Task ProviderHealthRecoveryAllowsOneLaterRecurrence()
    {
        using var folder = new TemporaryFolder();
        var settingsStore = new AlertSettingsStore(Path.Combine(folder.Root, AlertSettingsStore.DefaultFileName));
        var decisionStore = new AlertDecisionStore(Path.Combine(folder.Root, AlertDecisionStore.DefaultFileName));
        await settingsStore.SaveAsync(new AlertSettings(
            enabled: true,
            quotaThresholdPercent: 20,
            quotaThresholdEnabled: false,
            exhaustionForecastEnabled: false,
            staleDataEnabled: true,
            credentialFailureEnabled: true));
        var host = new AlertHost(decisionStore, settingsStore);
        var active = new ProviderAlertFacts(
            new ProviderId("codex"),
            isStale: true,
            hasCredentialFailure: true,
            []);
        var recovered = new ProviderAlertFacts(
            new ProviderId("codex"),
            isStale: false,
            hasCredentialFailure: false,
            []);

        Assert.Equal(2, (await host.EvaluateAsync(Now, [active])).Count);
        Assert.Empty(await host.EvaluateAsync(Now.AddMinutes(1), [active]));
        Assert.Empty(await host.EvaluateAsync(Now.AddMinutes(2), [recovered]));
        IReadOnlyList<AlertNotificationIntent> recurrence =
            await host.EvaluateAsync(Now.AddMinutes(3), [active]);

        Assert.Equal(2, recurrence.Count);
        Assert.Contains(recurrence, intent => intent.Kind == AlertKind.StaleData);
        Assert.Contains(recurrence, intent => intent.Kind == AlertKind.CredentialFailure);
    }

    [Fact]
    public async Task DisabledHealthKindDoesNotClearItsDecisionHistory()
    {
        using var folder = new TemporaryFolder();
        var settingsStore = new AlertSettingsStore(Path.Combine(folder.Root, AlertSettingsStore.DefaultFileName));
        var decisionStore = new AlertDecisionStore(Path.Combine(folder.Root, AlertDecisionStore.DefaultFileName));
        var host = new AlertHost(decisionStore, settingsStore);
        AlertSettings enabled = new(
            enabled: true,
            quotaThresholdPercent: 20,
            quotaThresholdEnabled: false,
            exhaustionForecastEnabled: false,
            staleDataEnabled: true,
            credentialFailureEnabled: false);
        await settingsStore.SaveAsync(enabled);
        var stale = new ProviderAlertFacts(new ProviderId("codex"), true, false, []);
        var recovered = new ProviderAlertFacts(new ProviderId("codex"), false, false, []);
        Assert.Single(await host.EvaluateAsync(Now, [stale]));

        await settingsStore.SaveAsync(new AlertSettings(
            enabled: false,
            quotaThresholdPercent: 20,
            quotaThresholdEnabled: false,
            exhaustionForecastEnabled: false,
            staleDataEnabled: true,
            credentialFailureEnabled: false));
        Assert.Empty(await host.EvaluateAsync(Now.AddMinutes(1), [recovered]));
        await settingsStore.SaveAsync(enabled);

        Assert.Empty(await host.EvaluateAsync(Now.AddMinutes(2), [stale]));
        Assert.Single((await decisionStore.LoadAsync()).NotifiedConditionKeys);
    }

    private static ProviderAlertFacts CreateFacts(
        decimal remainingPercent,
        DateTimeOffset? resetsAtUtc = null) => new(
        new ProviderId("codex"),
        isStale: false,
        hasCredentialFailure: false,
        [
            new QuotaAlertFacts(
                new MetricId("session"),
                remainingPercent,
                resetsAtUtc: resetsAtUtc ?? Now.AddDays(1),
                projectedExhaustionAtUtc: null),
        ]);

    private static AlertSettings CreateSettings(bool enabled) => new(
        enabled,
        quotaThresholdPercent: 20,
        quotaThresholdEnabled: true,
        exhaustionForecastEnabled: true,
        staleDataEnabled: true,
        credentialFailureEnabled: true);

    private static ProviderSnapshot CreateSnapshot(decimal used, decimal limit, DateTimeOffset observedAt) =>
        new(
            new ProviderId("codex"),
            "Codex",
            "Plus",
            Now,
            observedAt,
            "UTC",
            [
                new ProgressMetricSnapshot(
                    new MetricId("session"),
                    used,
                    limit,
                    Now.AddDays(1),
                    CreateProvenance()),
            ],
            CoverageKind.Complete,
            1);

    private static DataProvenance CreateProvenance() => new(
        SourceKind.OfficialLocalApi,
        MeasurementKind.ProviderReported,
        "test/1");

    private sealed class TemporaryFolder : IDisposable
    {
        public TemporaryFolder()
        {
            Root = Path.Combine(Path.GetTempPath(), "wou-alerts-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
                }
            }
            catch
            {
            }
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow.ToUniversalTime();

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}

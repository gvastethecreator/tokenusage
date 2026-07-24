using WOpenUsage.Core.Alerts;
using WOpenUsage.Core.Providers;

namespace WOpenUsage.Core.Tests.Alerts;

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
            staleDataEnabled: true,
            credentialFailureEnabled: true));

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
                    new DataProvenance(
                        SourceKind.OfficialLocalApi,
                        MeasurementKind.ProviderReported,
                        "test/1")),
            ],
            CoverageKind.Complete,
            1);

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

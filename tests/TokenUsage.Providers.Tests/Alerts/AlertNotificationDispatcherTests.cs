using System.Globalization;
using TokenUsage.App.Services;
using TokenUsage.Core.Alerts;
using TokenUsage.Core.Cache;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Session;

namespace TokenUsage.Providers.Tests.Alerts;

public sealed class AlertNotificationDispatcherTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SessionUpdateDeliversOneSanitizedQuotaNotificationThroughSink()
    {
        using var folder = new TemporaryFolder();
        var clock = new FixedTimeProvider(Now);
        string settingsPath = Path.Combine(folder.Root, AlertSettingsStore.DefaultFileName);
        await new AlertSettingsStore(settingsPath, clock).SaveAsync(new AlertSettings(
            enabled: true,
            quotaThresholdPercent: 20,
            quotaThresholdEnabled: true,
            exhaustionForecastEnabled: false,
            staleDataEnabled: false,
            credentialFailureEnabled: false));
        var provider = new LowQuotaProvider(clock);
        var refreshHost = new ProviderRefreshHost(
            [
                new ProviderRefreshRegistration(
                    provider,
                    new SnapshotStore(Path.Combine(folder.Root, "cache.json"), clock)),
            ],
            clock);
        await using var session = new AppSessionHost(
            refreshHost,
            new AlertHost(
                new AlertDecisionStore(
                    Path.Combine(folder.Root, AlertDecisionStore.DefaultFileName),
                    clock),
                new AlertSettingsStore(settingsPath, clock)),
            clock);
        var sink = new RecordingSink();
        var dispatcher = new AlertNotificationDispatcher(sink, GetString);
        session.Updated += (_, update) =>
        {
            if (update.Alerts.Count > 0)
            {
                _ = dispatcher.DeliverAsync(update.Alerts);
            }
        };

        await session.StartAsync();
        AlertNotificationMessage first = await sink.FirstNotification.WaitAsync(TimeSpan.FromSeconds(2));
        await session.RefreshAsync(AppSessionRefreshReason.Manual, forceRefresh: true);

        Assert.Single(sink.Notifications);
        Assert.Equal("Codex quota is low", first.Title);
        Assert.Equal("10% remains. Your alert threshold is 20%.", first.Body);
        Assert.DoesNotContain("secret", first.Title + first.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(AlertActivationArea.QuotaReport, first.ActivationTarget.Area);
        Assert.Equal("codex", first.ActivationTarget.ProviderId);
        Assert.Equal("quota.primary", first.ActivationTarget.MetricId);
    }

    [Fact]
    public void ExhaustionNotificationIncludesProjectedTimeAndNoProviderOutcomeText()
    {
        var dispatcher = new AlertNotificationDispatcher(new RecordingSink(), GetString);
        AlertCandidate candidate = Assert.Single(AlertEvaluator.Evaluate(
            new AlertSettings(
                enabled: true,
                quotaThresholdPercent: 20,
                quotaThresholdEnabled: false,
                exhaustionForecastEnabled: true,
                staleDataEnabled: false,
                credentialFailureEnabled: false),
            Now,
            [
                new ProviderAlertFacts(
                    new ProviderId("codex"),
                    isStale: false,
                    hasCredentialFailure: false,
                    [
                        new QuotaAlertFacts(
                            new MetricId("quota.primary"),
                            remainingPercent: 25m,
                            resetsAtUtc: Now.AddHours(5),
                            projectedExhaustionAtUtc: Now.AddHours(2)),
                    ]),
            ]));

        AlertNotificationMessage message = dispatcher.CreateMessage(new AlertNotificationIntent(candidate));

        Assert.Contains(
            Now.AddHours(2).ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
            message.Body,
            StringComparison.Ordinal);
        Assert.DoesNotContain("outcome", message.Title + message.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HealthIntentsDeliverSanitizedProviderStatusMessagesOnce()
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
        var facts = new ProviderAlertFacts(
            new ProviderId("codex"),
            isStale: true,
            hasCredentialFailure: true,
            []);
        var sink = new RecordingSink();
        var dispatcher = new AlertNotificationDispatcher(sink, GetString);

        await dispatcher.DeliverAsync(await host.EvaluateAsync(Now, [facts]));
        await dispatcher.DeliverAsync(await host.EvaluateAsync(Now.AddMinutes(1), [facts]));

        Assert.Equal(2, sink.Notifications.Count);
        Assert.All(sink.Notifications, notification =>
        {
            Assert.Equal(AlertActivationArea.ProviderStatus, notification.ActivationTarget.Area);
            Assert.Equal("codex", notification.ActivationTarget.ProviderId);
            Assert.Null(notification.ActivationTarget.MetricId);
            Assert.DoesNotContain("secret-token-account@example.com", notification.Title + notification.Body, StringComparison.OrdinalIgnoreCase);
        });
        Assert.Contains(sink.Notifications, notification => notification.Title == "Codex data is stale");
        Assert.Contains(sink.Notifications, notification => notification.Title == "Codex needs attention");
    }

    private static string GetString(string key) => key switch
    {
        "AlertQuotaTitleFormat" => "{0} quota is low",
        "AlertQuotaBodyFormat" => "{0}% remains. Your alert threshold is {1}%.",
        "AlertExhaustionTitleFormat" => "{0} may run out before reset",
        "AlertExhaustionBodyFormat" => "At the current pace, quota may run out around {0}.",
        "AlertTimeUnavailable" => "an unknown time",
        "AlertStaleTitleFormat" => "{0} data is stale",
        "AlertStaleBody" => "TokenUsage has not received recent data. Open provider status to review the connection.",
        "AlertCredentialTitleFormat" => "{0} needs attention",
        "AlertCredentialBody" => "TokenUsage could not read this provider. Open provider status to review its setup.",
        "LocalUsageAgentCodex" => "Codex",
        _ => key,
    };

    private sealed class RecordingSink : IAlertNotificationSink
    {
        private readonly TaskCompletionSource<AlertNotificationMessage> _first =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<AlertNotificationMessage> Notifications { get; } = [];

        public Task<AlertNotificationMessage> FirstNotification => _first.Task;

        public Task ShowAsync(
            AlertNotificationMessage notification,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Notifications.Add(notification);
            _first.TrySetResult(notification);
            return Task.CompletedTask;
        }
    }

    private sealed class LowQuotaProvider(TimeProvider clock) : IProviderRuntime
    {
        public ProviderDescriptor Descriptor { get; } =
            new(new ProviderId("codex"), "Codex");

        public ValueTask<ProviderDetection> DetectAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<ProviderDetection>(new ProviderDetection.Available());

        public Task<ProviderOutcome> RefreshAsync(
            RefreshContext context,
            CancellationToken cancellationToken)
        {
            DateTimeOffset now = clock.GetUtcNow();
            return Task.FromResult<ProviderOutcome>(new ProviderOutcome.Success(new ProviderSnapshot(
                Descriptor.Id,
                "Codex",
                "Plus",
                now,
                now,
                "UTC",
                [
                    new ProgressMetricSnapshot(
                        new MetricId("quota.primary"),
                        90m,
                        100m,
                        now.AddHours(5),
                        new DataProvenance(
                            SourceKind.OfficialLocalApi,
                            MeasurementKind.ProviderReported,
                            "test/1")),
                ],
                CoverageKind.Complete,
                1)));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class TemporaryFolder : IDisposable
    {
        public TemporaryFolder()
        {
            Root = Path.Combine(Path.GetTempPath(), "tokenusage-alert-delivery-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch
            {
            }
        }
    }
}

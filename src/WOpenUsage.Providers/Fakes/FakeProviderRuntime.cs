using WOpenUsage.Core.Providers;

namespace WOpenUsage.Providers.Fakes;

public enum FakeProviderScenario
{
    Success,
    Partial,
    Stale,
    Error,
}

public sealed class FakeProviderRuntime : IProviderRuntime
{
    private const string AdapterVersion = "fake/1";

    public FakeProviderRuntime(FakeProviderScenario scenario)
    {
        if (!Enum.IsDefined(scenario))
        {
            throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        Scenario = scenario;
    }

    public FakeProviderScenario Scenario { get; }

    public ProviderDescriptor Descriptor { get; } =
        new(new ProviderId("fake"), "Fake provider", isExperimental: true);

    public ValueTask<ProviderDetection> DetectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<ProviderDetection>(new ProviderDetection.Available());
    }

    public Task<ProviderOutcome> RefreshAsync(
        RefreshContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        DateTimeOffset fetchedAtUtc = context.Clock.GetUtcNow().ToUniversalTime();
        ProviderOutcome outcome = Scenario switch
        {
            FakeProviderScenario.Success => new ProviderOutcome.Success(
                CreateSnapshot(
                    fetchedAtUtc,
                    fetchedAtUtc.AddSeconds(-30),
                    CoverageKind.Complete)),
            FakeProviderScenario.Partial => CreatePartialOutcome(fetchedAtUtc),
            FakeProviderScenario.Stale => new ProviderOutcome.Success(
                CreateSnapshot(
                    fetchedAtUtc,
                    fetchedAtUtc.Subtract(context.StaleAfter).AddTicks(-1),
                    CoverageKind.Complete)),
            FakeProviderScenario.Error => new ProviderOutcome.TransientFailure(
                new ProviderError(
                    ProviderErrorCode.TransientSourceFailure,
                    "Synthetic source unavailable."),
                context.LastGood),
            _ => throw new InvalidOperationException("Unknown fake provider scenario."),
        };

        return Task.FromResult(outcome);
    }

    public static ProviderSnapshot CreateSnapshot(
        DateTimeOffset fetchedAtUtc,
        DateTimeOffset sourceObservedAtUtc,
        CoverageKind coverage)
    {
        var provenance = new DataProvenance(
            SourceKind.Synthetic,
            MeasurementKind.ProviderReported,
            AdapterVersion);

        return new ProviderSnapshot(
            new ProviderId("fake"),
            "Fake provider",
            "Sample",
            fetchedAtUtc,
            sourceObservedAtUtc,
            "UTC",
            [
                new ProgressMetricSnapshot(
                    new MetricId("session"),
                    42m,
                    100m,
                    fetchedAtUtc.AddHours(4),
                    provenance),
                new ScalarMetricSnapshot(
                    new MetricId("spend-usd"),
                    12.34m,
                    "USD",
                    provenance),
            ],
            coverage,
            adapterContractVersion: 1);
    }

    private static ProviderOutcome.PartialSuccess CreatePartialOutcome(DateTimeOffset fetchedAtUtc)
    {
        ProviderWarning[] warnings =
        [
            new ProviderWarning(
                ProviderWarningCode.PartialCoverage,
                "One synthetic metric is unavailable."),
        ];

        return new ProviderOutcome.PartialSuccess(
            CreateSnapshot(
                fetchedAtUtc,
                fetchedAtUtc.AddMinutes(-2),
                CoverageKind.Partial),
            warnings);
    }
}

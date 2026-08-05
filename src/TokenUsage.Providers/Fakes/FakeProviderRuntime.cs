using WOpenUsage.Core.Providers;

namespace WOpenUsage.Providers.Fakes;

public enum FakeProviderScenario
{
    Success,
    NearLimit,
    Partial,
    Stale,
    Error,
}

public sealed class FakeProviderRuntime : IProviderRuntime
{
    private const string AdapterVersion = "fake/1";
    private static readonly ProviderDescriptor DefaultDescriptor =
        new(new ProviderId("fake"), "Fake provider", isExperimental: true);

    public FakeProviderRuntime(
        FakeProviderScenario scenario,
        TimeSpan? delay = null,
        ProviderDescriptor? descriptor = null)
    {
        if (!Enum.IsDefined(scenario))
        {
            throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        TimeSpan effectiveDelay = delay ?? TimeSpan.Zero;
        if (effectiveDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delay), "Delay cannot be negative.");
        }

        Scenario = scenario;
        Delay = effectiveDelay;
        Descriptor = descriptor ?? DefaultDescriptor;
    }

    public FakeProviderScenario Scenario { get; }

    public TimeSpan Delay { get; }

    public ProviderDescriptor Descriptor { get; }

    public ValueTask<ProviderDetection> DetectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<ProviderDetection>(new ProviderDetection.Available());
    }

    public async Task<ProviderOutcome> RefreshAsync(
        RefreshContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay, context.Clock, cancellationToken).ConfigureAwait(false);
        }

        DateTimeOffset fetchedAtUtc = context.Clock.GetUtcNow().ToUniversalTime();
        ProviderOutcome outcome = Scenario switch
        {
            FakeProviderScenario.Success => new ProviderOutcome.Success(
                CreateSnapshot(
                    Descriptor,
                    fetchedAtUtc,
                    fetchedAtUtc.AddSeconds(-30),
                    CoverageKind.Complete,
                    used: 42m,
                    spendUsd: 12.30m)),
            FakeProviderScenario.NearLimit => new ProviderOutcome.Success(
                CreateSnapshot(
                    Descriptor,
                    fetchedAtUtc,
                    fetchedAtUtc.AddSeconds(-30),
                    CoverageKind.Complete,
                    used: 92m,
                    spendUsd: 23.80m)),
            FakeProviderScenario.Partial => CreatePartialOutcome(fetchedAtUtc),
            FakeProviderScenario.Stale => new ProviderOutcome.Success(
                CreateSnapshot(
                    Descriptor,
                    fetchedAtUtc,
                    fetchedAtUtc.Subtract(context.StaleAfter).AddTicks(-1),
                    CoverageKind.Complete,
                    used: 42m,
                    spendUsd: 12.30m)),
            FakeProviderScenario.Error => new ProviderOutcome.TransientFailure(
                new ProviderError(
                    ProviderErrorCode.TransientSourceFailure,
                    "Synthetic source unavailable."),
                context.LastGood),
            _ => throw new InvalidOperationException("Unknown fake provider scenario."),
        };

        return outcome;
    }

    public static ProviderSnapshot CreateSnapshot(
        DateTimeOffset fetchedAtUtc,
        DateTimeOffset sourceObservedAtUtc,
        CoverageKind coverage) =>
        CreateSnapshot(
            DefaultDescriptor,
            fetchedAtUtc,
            sourceObservedAtUtc,
            coverage,
            used: 42m,
            spendUsd: 12.34m);

    private static ProviderSnapshot CreateSnapshot(
        ProviderDescriptor descriptor,
        DateTimeOffset fetchedAtUtc,
        DateTimeOffset sourceObservedAtUtc,
        CoverageKind coverage,
        decimal used,
        decimal spendUsd)
    {
        var provenance = new DataProvenance(
            SourceKind.Synthetic,
            MeasurementKind.ProviderReported,
            AdapterVersion);

        return new ProviderSnapshot(
            descriptor.Id,
            descriptor.DisplayName,
            "Sample",
            fetchedAtUtc,
            sourceObservedAtUtc,
            "UTC",
            [
                new ProgressMetricSnapshot(
                    new MetricId("session"),
                    used,
                    100m,
                    fetchedAtUtc.AddHours(4),
                    provenance),
                new ScalarMetricSnapshot(
                    new MetricId("spend-usd"),
                    spendUsd,
                    "USD",
                    provenance),
            ],
            coverage,
            adapterContractVersion: 1);
    }

    private ProviderOutcome.PartialSuccess CreatePartialOutcome(DateTimeOffset fetchedAtUtc)
    {
        ProviderWarning[] warnings =
        [
            new ProviderWarning(
                ProviderWarningCode.PartialCoverage,
                "One synthetic metric is unavailable."),
        ];

        return new ProviderOutcome.PartialSuccess(
            CreateSnapshot(
                Descriptor,
                fetchedAtUtc,
                fetchedAtUtc.AddMinutes(-2),
                CoverageKind.Partial,
                used: 42m,
                spendUsd: 9.40m),
            warnings);
    }
}

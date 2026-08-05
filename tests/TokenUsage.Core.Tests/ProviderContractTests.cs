using TokenUsage.Core.Providers;

namespace TokenUsage.Core.Tests;

public sealed class ProviderContractTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 22, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("")]
    [InlineData("Codex")]
    [InlineData("bad id")]
    [InlineData("-codex")]
    [InlineData("codex-")]
    [InlineData("codex..usage")]
    public void ProviderIdRejectsUnstableValues(string value)
    {
        Assert.Throws<ArgumentException>(() => new ProviderId(value));
    }

    [Fact]
    public void SnapshotCopiesMetricsAndRejectsDuplicateMetricIds()
    {
        var metrics = new List<MetricSnapshot>
        {
            CreateProgressMetric("session"),
        };
        ProviderSnapshot snapshot = CreateSnapshot(metrics);

        metrics.Clear();

        Assert.Single(snapshot.Metrics);
        Assert.Throws<ArgumentException>(() => CreateSnapshot(
            [CreateProgressMetric("session"), CreateProgressMetric("session")]));
    }

    [Fact]
    public void SnapshotCopiesCapabilitiesAndRejectsDuplicateCapabilityIds()
    {
        var provenance = new DataProvenance(
            SourceKind.ManualKey,
            MeasurementKind.ProviderReported,
            "fake/1");
        var capabilities = new List<ProviderCapabilitySnapshot>
        {
            new(new CapabilityId("quota.key"), ProviderCapabilityState.Available, provenance),
        };
        ProviderSnapshot snapshot = new(
            new ProviderId("fake"),
            "Fake provider",
            "Sample",
            Now,
            Now,
            "UTC",
            [CreateProgressMetric("session")],
            CoverageKind.Complete,
            1,
            capabilities);

        capabilities.Clear();

        Assert.Single(snapshot.Capabilities);
        Assert.Throws<ArgumentException>(() => new ProviderSnapshot(
            new ProviderId("fake"),
            "Fake provider",
            "Sample",
            Now,
            Now,
            "UTC",
            [CreateProgressMetric("session")],
            CoverageKind.Complete,
            1,
            [
                new(new CapabilityId("quota.key"), ProviderCapabilityState.Available, provenance),
                new(new CapabilityId("quota.key"), ProviderCapabilityState.Degraded, provenance),
            ]));
    }

    [Fact]
    public void ProgressMetadataIsOptionalAndValidated()
    {
        var metric = new ProgressMetricSnapshot(
            new MetricId("budget"),
            1m,
            10m,
            resetsAtUtc: null,
            new DataProvenance(
                SourceKind.ManualKey,
                MeasurementKind.ProviderReported,
                "fake/1"),
            "usd",
            ProgressResetCadence.Monthly,
            isActive: false);

        Assert.Equal("usd", metric.Unit);
        Assert.Equal(ProgressResetCadence.Monthly, metric.ResetCadence);
        Assert.False(metric.IsActive);
        Assert.Throws<ArgumentException>(() => new ProgressMetricSnapshot(
            new MetricId("bad-unit"),
            1m,
            10m,
            resetsAtUtc: null,
            metric.Provenance,
            " "));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProgressMetricSnapshot(
            new MetricId("bad-cadence"),
            1m,
            10m,
            resetsAtUtc: null,
            metric.Provenance,
            resetCadence: (ProgressResetCadence)999));
    }

    [Fact]
    public void SnapshotRejectsNonUtcAndFutureSourceTimestamps()
    {
        DateTimeOffset nonUtc = Now.ToOffset(TimeSpan.FromHours(2));

        Assert.Throws<ArgumentException>(() => new ProviderSnapshot(
            new ProviderId("fake"),
            "Fake provider",
            "Sample",
            nonUtc,
            Now,
            "UTC",
            [CreateProgressMetric("session")],
            CoverageKind.Complete,
            1));

        Assert.Throws<ArgumentOutOfRangeException>(() => new ProviderSnapshot(
            new ProviderId("fake"),
            "Fake provider",
            "Sample",
            Now,
            Now.AddSeconds(1),
            "UTC",
            [CreateProgressMetric("session")],
            CoverageKind.Complete,
            1));
    }

    [Fact]
    public void FreshnessUsesSourceObservationAndInjectedTimeProvider()
    {
        var clock = new AdjustableTimeProvider(Now);
        ProviderSnapshot snapshot = CreateSnapshot(
            [CreateProgressMetric("session")],
            sourceObservedAtUtc: Now.Subtract(SnapshotFreshness.DefaultMaxAge));

        Assert.False(SnapshotFreshness.IsStale(snapshot, clock));

        clock.Advance(TimeSpan.FromTicks(1));

        Assert.True(SnapshotFreshness.IsStale(snapshot, clock));
    }

    [Fact]
    public void DefaultStaleAgeMatchesTheProductPolicy()
    {
        var clock = new AdjustableTimeProvider(Now);

        Assert.Equal(TimeSpan.FromMinutes(10), SnapshotFreshness.DefaultMaxAge);
        Assert.Equal(
            TimeSpan.FromMinutes(10),
            new RefreshContext(clock).StaleAfter);
    }

    [Fact]
    public void NonPositiveStaleAgesAreRejected()
    {
        var clock = new AdjustableTimeProvider(Now);
        ProviderSnapshot snapshot = CreateSnapshot([CreateProgressMetric("session")]);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RefreshContext(clock, staleAfter: TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SnapshotFreshness.IsStale(snapshot, clock, TimeSpan.Zero));
    }

    [Fact]
    public void PartialOutcomeRequiresAWarningAndCopiesTheInput()
    {
        ProviderSnapshot snapshot = CreateSnapshot([CreateProgressMetric("session")]);
        var warnings = new List<ProviderWarning>
        {
            new(ProviderWarningCode.PartialCoverage, "One synthetic metric is unavailable."),
        };
        var outcome = new ProviderOutcome.PartialSuccess(snapshot, warnings);

        warnings.Clear();

        Assert.Single(outcome.Warnings);
        Assert.Throws<ArgumentException>(() => new ProviderOutcome.PartialSuccess(snapshot, []));
    }

    [Fact]
    public void FailureOutcomeCarriesTypedErrorAndOptionalLastGood()
    {
        ProviderSnapshot snapshot = CreateSnapshot([CreateProgressMetric("session")]);
        var error = new ProviderError(
            ProviderErrorCode.TransientSourceFailure,
            "Synthetic source unavailable.");

        var outcome = new ProviderOutcome.TransientFailure(error, snapshot);

        Assert.Equal(ProviderErrorCode.TransientSourceFailure, outcome.Error.Code);
        Assert.Same(snapshot, outcome.LastGood);
    }

    private static ProviderSnapshot CreateSnapshot(
        IEnumerable<MetricSnapshot> metrics,
        DateTimeOffset? sourceObservedAtUtc = null) =>
        new(
            new ProviderId("fake"),
            "Fake provider",
            "Sample",
            Now,
            sourceObservedAtUtc ?? Now,
            "UTC",
            metrics,
            CoverageKind.Complete,
            1);

    private static ProgressMetricSnapshot CreateProgressMetric(string id) =>
        new(
            new MetricId(id),
            42m,
            100m,
            Now.AddHours(4),
            new DataProvenance(
                SourceKind.Synthetic,
                MeasurementKind.ProviderReported,
                "fake/1"));

    private sealed class AdjustableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }
}

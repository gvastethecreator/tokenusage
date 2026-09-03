using TokenUsage.Core.Providers;
using TokenUsage.Providers.Codex;

namespace TokenUsage.Providers.Tests.Codex;

public sealed class CodexRateLimitsSnapshotMapperTests
{
    private static readonly DateTimeOffset ObservedAt =
        new(2026, 7, 22, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PrimaryAndSecondaryMapToPercentProgressWithOfficialProvenance()
    {
        var source = new CodexRateLimitsSnapshot(
            new CodexRateLimitBucket(
                "plus",
                Window(42, ObservedAt.AddHours(4), 300),
                Window(18, ObservedAt.AddDays(7), 10080)),
            new Dictionary<string, CodexRateLimitBucket>());

        CodexSnapshotMappingResult result =
            CodexRateLimitsSnapshotMapper.Map(source, ObservedAt, "UTC");

        ProviderSnapshot snapshot =
            Assert.IsType<CodexSnapshotMappingResult.Available>(result).Snapshot;
        Assert.Equal("codex", snapshot.ProviderId.Value);
        Assert.Equal("Codex", snapshot.DisplayName);
        Assert.Equal("Plus", snapshot.PlanLabel);
        Assert.Equal(ObservedAt, snapshot.FetchedAtUtc);
        Assert.Equal(ObservedAt, snapshot.SourceObservedAtUtc);
        Assert.Equal("UTC", snapshot.TimeZoneId);
        Assert.Equal(CoverageKind.Complete, snapshot.Coverage);
        Assert.Equal(1, snapshot.AdapterContractVersion);
        Assert.Collection(
            snapshot.Metrics,
            metric => AssertMetric(metric, "quota.primary", 42m, ObservedAt.AddHours(4)),
            metric => AssertDuration(metric, "quota.primary.window-minutes", 300m),
            metric => AssertMetric(metric, "quota.secondary", 18m, ObservedAt.AddDays(7)),
            metric => AssertDuration(metric, "quota.secondary.window-minutes", 10080m));
    }

    [Fact]
    public void StableDefaultMetricsRemainAndMirroredAdditionalBucketIsSkipped()
    {
        CodexRateLimitWindow sharedPrimary = Window(25, ObservedAt.AddHours(5), 300);
        var source = new CodexRateLimitsSnapshot(
            new CodexRateLimitBucket("pro", sharedPrimary, null),
            new Dictionary<string, CodexRateLimitBucket>(StringComparer.Ordinal)
            {
                ["Z_Model"] = new("pro", Window(75, ObservedAt.AddHours(2), 60), null),
                ["codex"] = new("pro", sharedPrimary, null),
                ["empty"] = new("pro", null, null),
            });

        CodexSnapshotMappingResult result =
            CodexRateLimitsSnapshotMapper.Map(source, ObservedAt, "America/Argentina/Buenos_Aires");

        ProviderSnapshot snapshot =
            Assert.IsType<CodexSnapshotMappingResult.Available>(result).Snapshot;
        Assert.Equal(
            [
                "quota.primary",
                "quota.primary.window-minutes",
                "quota.z-model.primary",
                "quota.z-model.primary.window-minutes",
            ],
            snapshot.Metrics.Select(metric => metric.Id.Value));
        Assert.Equal(4, snapshot.Metrics.Count);
        ProgressMetricSnapshot additional = Assert.IsType<ProgressMetricSnapshot>(snapshot.Metrics[2]);
        Assert.Null(additional.DisplayName);
        Assert.Equal("Z_Model", additional.LabelEvidence?.ProviderMetricKey);
        Assert.Equal(MetricLabelSource.Duration, additional.LabelEvidence?.Source);
        Assert.Equal(MetricLabelConfidence.Derived, additional.LabelEvidence?.Confidence);
    }

    [Fact]
    public void NamedAdditionalBucketPreservesProviderIdentityAndExactLabel()
    {
        var source = new CodexRateLimitsSnapshot(
            new CodexRateLimitBucket("pro", null, null),
            new Dictionary<string, CodexRateLimitBucket>(StringComparer.Ordinal)
            {
                ["codex-model"] = new(
                    "pro",
                    Window(75, ObservedAt.AddHours(2), 300),
                    null)
                {
                    LimitId = "base_model_inference",
                    LimitName = "gpt-reserve",
                },
            });

        ProviderSnapshot snapshot = Assert.IsType<CodexSnapshotMappingResult.Available>(
            CodexRateLimitsSnapshotMapper.Map(source, ObservedAt, "UTC")).Snapshot;

        ProgressMetricSnapshot metric = Assert.IsType<ProgressMetricSnapshot>(snapshot.Metrics[0]);
        Assert.Equal("quota.codex-model.primary", metric.Id.Value);
        Assert.Equal("gpt-reserve", metric.DisplayName);
        Assert.Equal("codex-model", metric.LabelEvidence?.ProviderMetricKey);
        Assert.Equal("base_model_inference", metric.LabelEvidence?.ProviderMetricId);
        Assert.Equal("gpt-reserve", metric.LabelEvidence?.ProviderMetricName);
        Assert.Equal(MetricLabelSource.Provider, metric.LabelEvidence?.Source);
        Assert.Equal(MetricLabelConfidence.Exact, metric.LabelEvidence?.Confidence);
    }

    [Fact]
    public void DefaultViewIsKeptWhenAdditionalBucketHasNoWindows()
    {
        var source = new CodexRateLimitsSnapshot(
            new CodexRateLimitBucket("team", Window(60, null, null), null),
            new Dictionary<string, CodexRateLimitBucket>
            {
                ["empty"] = new("team", null, null),
            });

        CodexSnapshotMappingResult result =
            CodexRateLimitsSnapshotMapper.Map(source, ObservedAt, "UTC");

        ProviderSnapshot snapshot =
            Assert.IsType<CodexSnapshotMappingResult.Available>(result).Snapshot;
        Assert.Single(snapshot.Metrics);
        Assert.Equal("quota.primary", snapshot.Metrics[0].Id.Value);
    }

    [Fact]
    public void MissingWindowsProduceAnExplicitNoRateLimitsResult()
    {
        var source = new CodexRateLimitsSnapshot(
            new CodexRateLimitBucket("unknown", null, null),
            new Dictionary<string, CodexRateLimitBucket>());

        CodexSnapshotMappingResult result =
            CodexRateLimitsSnapshotMapper.Map(source, ObservedAt, "UTC");

        Assert.IsType<CodexSnapshotMappingResult.NoRateLimits>(result);
    }

    [Fact]
    public void AUniqueAdditionalPlanSuppliesThePlanLabel()
    {
        var source = new CodexRateLimitsSnapshot(
            new CodexRateLimitBucket(null, null, null),
            new Dictionary<string, CodexRateLimitBucket>
            {
                ["codex"] = new("enterprise", Window(10, null, null), null),
            });

        CodexSnapshotMappingResult result =
            CodexRateLimitsSnapshotMapper.Map(source, ObservedAt, "UTC");

        ProviderSnapshot snapshot =
            Assert.IsType<CodexSnapshotMappingResult.Available>(result).Snapshot;
        Assert.Equal("Enterprise", snapshot.PlanLabel);
    }

    [Fact]
    public void ConflictingNormalizedAdditionalIdsFailWithoutEchoingEitherId()
    {
        const string firstId = "A_B";
        const string secondId = "a-b";
        var source = new CodexRateLimitsSnapshot(
            new CodexRateLimitBucket(null, null, null),
            new Dictionary<string, CodexRateLimitBucket>(StringComparer.Ordinal)
            {
                [firstId] = new(null, Window(10, null, null), null),
                [secondId] = new(null, Window(20, null, null), null),
            });

        CodexProtocolException error = Assert.Throws<CodexProtocolException>(() =>
            CodexRateLimitsSnapshotMapper.Map(source, ObservedAt, "UTC"));

        Assert.Equal("Codex rate limits could not be mapped to stable metrics.", error.Message);
        Assert.DoesNotContain(firstId, error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(secondId, error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ObservationTimeMustBeUtc()
    {
        var source = new CodexRateLimitsSnapshot(
            new CodexRateLimitBucket("plus", Window(42, null, null), null),
            new Dictionary<string, CodexRateLimitBucket>());
        DateTimeOffset localTime = ObservedAt.ToOffset(TimeSpan.FromHours(-3));

        Assert.Throws<ArgumentException>(() =>
            CodexRateLimitsSnapshotMapper.Map(source, localTime, "UTC"));
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    public void PercentBoundariesPreserveRemainingSemantics(
        int usedPercent,
        int expectedRemainingPercent)
    {
        var source = new CodexRateLimitsSnapshot(
            new CodexRateLimitBucket("plus", Window(usedPercent, null, null), null),
            new Dictionary<string, CodexRateLimitBucket>());

        CodexSnapshotMappingResult result =
            CodexRateLimitsSnapshotMapper.Map(source, ObservedAt, "UTC");

        ProviderSnapshot snapshot =
            Assert.IsType<CodexSnapshotMappingResult.Available>(result).Snapshot;
        ProgressMetricSnapshot metric = Assert.IsType<ProgressMetricSnapshot>(snapshot.Metrics[0]);
        Assert.Equal(expectedRemainingPercent, metric.RemainingPercent);
    }

    private static CodexRateLimitWindow Window(
        int usedPercent,
        DateTimeOffset? resetsAtUtc,
        long? durationMinutes) =>
        new(usedPercent, resetsAtUtc, durationMinutes);

    private static void AssertMetric(
        MetricSnapshot metric,
        string expectedId,
        decimal expectedUsed,
        DateTimeOffset? expectedReset)
    {
        ProgressMetricSnapshot progress = Assert.IsType<ProgressMetricSnapshot>(metric);
        Assert.Equal(expectedId, progress.Id.Value);
        Assert.Equal(expectedUsed, progress.Used);
        Assert.Equal(100m, progress.Limit);
        Assert.Equal(100m - expectedUsed, progress.RemainingPercent);
        Assert.Equal(expectedReset, progress.ResetsAtUtc);
        Assert.Equal(SourceKind.OfficialLocalApi, progress.Provenance.SourceKind);
        Assert.Equal(MeasurementKind.ProviderReported, progress.Provenance.MeasurementKind);
        Assert.Equal("codex-app-server/1", progress.Provenance.AdapterVersion);
    }

    private static void AssertDuration(
        MetricSnapshot metric,
        string expectedId,
        decimal expectedMinutes)
    {
        ScalarMetricSnapshot duration = Assert.IsType<ScalarMetricSnapshot>(metric);
        Assert.Equal(expectedId, duration.Id.Value);
        Assert.Equal(expectedMinutes, duration.Value);
        Assert.Equal("minutes", duration.Unit);
        Assert.Equal(SourceKind.OfficialLocalApi, duration.Provenance.SourceKind);
        Assert.Equal(MeasurementKind.ProviderReported, duration.Provenance.MeasurementKind);
        Assert.Equal("codex-app-server/1", duration.Provenance.AdapterVersion);
    }
}

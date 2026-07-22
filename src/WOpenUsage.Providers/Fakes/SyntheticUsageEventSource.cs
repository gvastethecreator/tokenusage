using System.Security.Cryptography;
using System.Text;
using System.Globalization;
using WOpenUsage.Core.Providers;
using WOpenUsage.Core.Usage;

namespace WOpenUsage.Providers.Fakes;

public sealed class SyntheticUsageEventSource : IUsageEventSource
{
    private readonly TimeProvider _clock;
    private readonly string _groupingTimeZoneId;

    public SyntheticUsageEventSource(TimeProvider clock, string groupingTimeZoneId)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        ArgumentException.ThrowIfNullOrWhiteSpace(groupingTimeZoneId);
        _ = TimeZoneInfo.FindSystemTimeZoneById(groupingTimeZoneId);
        _groupingTimeZoneId = groupingTimeZoneId;
    }

    public SourceKind SourceKind => SourceKind.Synthetic;

    public Task<UsageSourceReadResult> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DateTimeOffset now = _clock.GetUtcNow().ToUniversalTime();
        TimeZoneInfo groupingTimeZone = TimeZoneInfo.FindSystemTimeZoneById(
            _groupingTimeZoneId);
        string fixtureDay = TimeZoneInfo.ConvertTime(now, groupingTimeZone)
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        UsageEvent reported = CreateEvent(
            $"grok-build:fixture:{fixtureDay}:reported-1",
            "grok",
            "xai",
            "grok-4.5",
            now.AddMinutes(-20),
            new TokenBreakdown(14_200, 3_100, 720, 8_400, 0),
            CostObservation.ProviderReported(1.84m),
            CoverageKind.Complete);

        IReadOnlyList<UsageEvent> events =
        [
            reported,
            reported,
            CreateEvent(
                $"opencode:fixture:{fixtureDay}:estimated-1",
                "opencode",
                "openai",
                "gpt-5.4",
                now.AddHours(-2),
                new TokenBreakdown(9_600, 2_400, 0, 5_200, 0),
                CostObservation.CatalogEstimated(
                    0.62m,
                    "fixture-catalog-2026-07",
                    "gpt-5.4"),
                CoverageKind.Partial),
            CreateEvent(
                $"antigravity:fixture:{fixtureDay}:unpriced-1",
                "antigravity",
                "google",
                "gemini-2.5-pro",
                now.AddHours(-26),
                new TokenBreakdown(7_300, 1_900, 260, 0, 0),
                CostObservation.Unavailable(),
                CoverageKind.Unpriced),
        ];

        return Task.FromResult(new UsageSourceReadResult(
            events,
            UsageSourceReadStatus.Complete));
    }

    private UsageEvent CreateEvent(
        string localIdentity,
        string agentId,
        string modelProviderId,
        string modelId,
        DateTimeOffset occurredAtUtc,
        TokenBreakdown tokens,
        CostObservation cost,
        CoverageKind coverage) =>
        new(
            CreateKey(localIdentity),
            new AgentId(agentId),
            new ModelProviderId(modelProviderId),
            new ModelId(modelId),
            occurredAtUtc,
            _groupingTimeZoneId,
            tokens,
            cost,
            "fixture/1",
            coverage);

    private static UsageEventKey CreateKey(string localIdentity) =>
        new(Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(localIdentity))).ToLowerInvariant());
}

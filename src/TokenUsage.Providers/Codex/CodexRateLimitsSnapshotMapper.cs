using System.Text;
using TokenUsage.Core.Providers;

namespace TokenUsage.Providers.Codex;

public abstract class CodexSnapshotMappingResult
{
    private CodexSnapshotMappingResult()
    {
    }

    public sealed class Available : CodexSnapshotMappingResult
    {
        public Available(ProviderSnapshot snapshot)
        {
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        }

        public ProviderSnapshot Snapshot { get; }
    }

    public sealed class NoRateLimits : CodexSnapshotMappingResult;
}

public static class CodexRateLimitsSnapshotMapper
{
    private const string AdapterVersion = "codex-app-server/1";
    private const int AdapterContractVersion = 1;
    private const int MaximumAdditionalLimits = 64;
    public const string ResetCreditsReportedAvailableMetricId = "quota.reset-credits.reported-available";
    public const string ResetCreditsAvailableMetricId = "quota.reset-credits.available";
    public const string ResetCreditsHasDetailsMetricId = "quota.reset-credits.has-details";
    public const string ResetCreditsDetailCountMetricId = "quota.reset-credits.detail-count";
    public const string ResetCreditsExpiredMetricId = "quota.reset-credits.expired";
    public const string ResetCreditsNextExpiryMetricId = "quota.reset-credits.next-expiry-unix";
    private static readonly ProviderId CodexProviderId = new("codex");

    public static CodexSnapshotMappingResult Map(
        CodexRateLimitsSnapshot source,
        DateTimeOffset observedAtUtc,
        string timeZoneId)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (observedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Codex observation time must use the UTC offset.",
                nameof(observedAtUtc));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);
        if (source.RateLimitsByLimitId.Count > MaximumAdditionalLimits)
        {
            throw MappingFailure();
        }

        var provenance = new DataProvenance(
            SourceKind.OfficialLocalApi,
            MeasurementKind.ProviderReported,
            AdapterVersion);
        var metrics = new List<MetricSnapshot>();

        AddBucketMetrics(metrics, "quota", source.RateLimits, provenance, providerMetricKey: null);

        var usedPrefixes = new HashSet<string>(StringComparer.Ordinal);
        foreach ((string limitId, CodexRateLimitBucket bucket) in
            source.RateLimitsByLimitId.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (bucket.Primary is null && bucket.Secondary is null)
            {
                continue;
            }

            if (HasSameWindows(source.RateLimits, bucket))
            {
                continue;
            }

            string prefix = $"quota.{NormalizeLimitId(limitId)}";
            if (!usedPrefixes.Add(prefix))
            {
                throw MappingFailure();
            }

            AddBucketMetrics(metrics, prefix, bucket, provenance, limitId);
        }

        if (!metrics.OfType<ProgressMetricSnapshot>().Any())
        {
            return new CodexSnapshotMappingResult.NoRateLimits();
        }

        AddResetCreditMetrics(metrics, source.ResetCredits, observedAtUtc, provenance);

        return new CodexSnapshotMappingResult.Available(
            new ProviderSnapshot(
                CodexProviderId,
                "Codex",
                ResolvePlanLabel(source),
                observedAtUtc,
                observedAtUtc,
                timeZoneId,
                metrics,
                CoverageKind.Complete,
                AdapterContractVersion));
    }

    private static void AddResetCreditMetrics(
        List<MetricSnapshot> metrics,
        CodexResetCreditInventory? inventory,
        DateTimeOffset observedAtUtc,
        DataProvenance provenance)
    {
        if (inventory is null)
        {
            return;
        }

        IReadOnlyList<CodexResetCredit>? details = inventory.Credits;
        int effectiveAvailable = inventory.AvailableCount;
        int expired = 0;
        DateTimeOffset? nextExpiry = null;
        if (details is not null)
        {
            CodexResetCredit[] available = details
                .Where(credit =>
                    string.Equals(credit.Status, "available", StringComparison.OrdinalIgnoreCase)
                    && (credit.ExpiresAtUtc is null || credit.ExpiresAtUtc > observedAtUtc))
                .ToArray();
            effectiveAvailable = Math.Min(inventory.AvailableCount, available.Length);
            expired = details.Count(credit =>
                string.Equals(credit.Status, "expired", StringComparison.OrdinalIgnoreCase)
                || credit.ExpiresAtUtc is not null && credit.ExpiresAtUtc <= observedAtUtc);
            nextExpiry = available
                .Where(credit => credit.ExpiresAtUtc is not null)
                .Select(credit => credit.ExpiresAtUtc)
                .Min();
        }

        AddScalar(metrics, ResetCreditsReportedAvailableMetricId, inventory.AvailableCount, "credits", provenance);
        AddScalar(metrics, ResetCreditsAvailableMetricId, effectiveAvailable, "credits", provenance);
        AddScalar(metrics, ResetCreditsHasDetailsMetricId, details is null ? 0 : 1, "boolean", provenance);
        if (details is not null)
        {
            AddScalar(metrics, ResetCreditsDetailCountMetricId, details.Count, "credits", provenance);
            AddScalar(metrics, ResetCreditsExpiredMetricId, expired, "credits", provenance);
        }

        if (nextExpiry is not null)
        {
            AddScalar(
                metrics,
                ResetCreditsNextExpiryMetricId,
                nextExpiry.Value.ToUnixTimeSeconds(),
                "unix-seconds",
                provenance);
        }
    }

    private static void AddScalar(
        List<MetricSnapshot> metrics,
        string metricId,
        decimal value,
        string unit,
        DataProvenance provenance) =>
        metrics.Add(new ScalarMetricSnapshot(new MetricId(metricId), value, unit, provenance));

    private static void AddBucketMetrics(
        List<MetricSnapshot> metrics,
        string prefix,
        CodexRateLimitBucket bucket,
        DataProvenance provenance,
        string? providerMetricKey)
    {
        if (bucket.Primary is not null)
        {
            metrics.Add(CreateProgressMetric(
                $"{prefix}.primary",
                bucket.Primary,
                provenance,
                bucket.LimitName,
                CreateLabelEvidence(providerMetricKey, bucket, bucket.Primary)));
            AddWindowDuration(metrics, $"{prefix}.primary", bucket.Primary, provenance);
        }

        if (bucket.Secondary is not null)
        {
            metrics.Add(CreateProgressMetric(
                $"{prefix}.secondary",
                bucket.Secondary,
                provenance,
                bucket.LimitName,
                CreateLabelEvidence(providerMetricKey, bucket, bucket.Secondary)));
            AddWindowDuration(metrics, $"{prefix}.secondary", bucket.Secondary, provenance);
        }
    }

    private static void AddWindowDuration(
        List<MetricSnapshot> metrics,
        string prefix,
        CodexRateLimitWindow window,
        DataProvenance provenance)
    {
        if (window.WindowDurationMinutes is null)
        {
            return;
        }

        metrics.Add(
            new ScalarMetricSnapshot(
                new MetricId($"{prefix}.window-minutes"),
                window.WindowDurationMinutes.Value,
                "minutes",
                provenance));
    }

    private static ProgressMetricSnapshot CreateProgressMetric(
        string metricId,
        CodexRateLimitWindow window,
        DataProvenance provenance,
        string? displayName,
        MetricLabelEvidence? labelEvidence) =>
        new(
            new MetricId(metricId),
            window.UsedPercent,
            limit: 100m,
            window.ResetsAtUtc,
            provenance,
            displayName: displayName,
            labelEvidence: labelEvidence);

    private static MetricLabelEvidence? CreateLabelEvidence(
        string? providerMetricKey,
        CodexRateLimitBucket bucket,
        CodexRateLimitWindow window)
    {
        if (providerMetricKey is null)
        {
            return null;
        }

        (MetricLabelSource source, MetricLabelConfidence confidence) = bucket.LimitName switch
        {
            { Length: > 0 } => (MetricLabelSource.Provider, MetricLabelConfidence.Exact),
            _ when window.WindowDurationMinutes is not null =>
                (MetricLabelSource.Duration, MetricLabelConfidence.Derived),
            _ => (MetricLabelSource.Unknown, MetricLabelConfidence.Unknown),
        };
        return new MetricLabelEvidence(
            providerMetricKey,
            bucket.LimitId,
            bucket.LimitName,
            source,
            confidence);
    }

    private static bool HasSameWindows(
        CodexRateLimitBucket left,
        CodexRateLimitBucket right) =>
        left.Primary == right.Primary && left.Secondary == right.Secondary;

    private static string NormalizeLimitId(string value)
    {
        var result = new StringBuilder(value.Length);
        bool previousWasSeparator = false;

        foreach (char character in value)
        {
            char normalized = char.ToLowerInvariant(character);
            if (normalized is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                result.Append(normalized);
                previousWasSeparator = false;
                continue;
            }

            if (!previousWasSeparator && result.Length > 0)
            {
                result.Append('-');
                previousWasSeparator = true;
            }
        }

        string normalizedId = result.ToString().TrimEnd('-');
        if (normalizedId.Length == 0)
        {
            throw MappingFailure();
        }

        return normalizedId;
    }

    private static string? ResolvePlanLabel(CodexRateLimitsSnapshot source)
    {
        string? planType = source.RateLimits.PlanType;
        if (planType is null)
        {
            string[] additionalPlans = source.RateLimitsByLimitId.Values
                .Select(bucket => bucket.PlanType)
                .Where(candidate => candidate is not null)
                .Cast<string>()
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            planType = additionalPlans.Length == 1 ? additionalPlans[0] : null;
        }

        return planType switch
        {
            "free" => "Free",
            "go" => "Go",
            "plus" => "Plus",
            "pro" => "Pro",
            "prolite" => "Pro Lite",
            "team" => "Team",
            "self_serve_business_usage_based" => "Business (usage based)",
            "business" => "Business",
            "enterprise_cbp_usage_based" => "Enterprise (usage based)",
            "enterprise" => "Enterprise",
            "edu" => "Education",
            "unknown" => "Unknown",
            _ => null,
        };
    }

    private static CodexProtocolException MappingFailure() =>
        new("Codex rate limits could not be mapped to stable metrics.");
}

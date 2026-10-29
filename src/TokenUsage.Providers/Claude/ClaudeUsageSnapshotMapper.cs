using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TokenUsage.Core.Providers;

namespace TokenUsage.Providers.Claude;

/// <summary>Reads the named limits returned by the account usage endpoint used by Claude Code.</summary>
public static class ClaudeUsageSnapshotMapper
{
    public const string AdapterVersion = "claude-account-usage/1";
    public const string ScopedWeeklyMetricPrefix = "quota.seven-day.scoped.";

    public static ProviderSnapshot Map(
        JsonElement document,
        DateTimeOffset observedAtUtc,
        string timeZoneId,
        string? planLabel = null)
    {
        if (document.ValueKind != JsonValueKind.Object
            || !document.TryGetProperty("limits", out JsonElement limits)
            || limits.ValueKind != JsonValueKind.Array)
            throw new JsonException("Claude did not return its named usage limits.");

        var provenance = new DataProvenance(
            SourceKind.PrivateRemoteApi, MeasurementKind.ProviderReported, AdapterVersion);
        var metrics = new List<MetricSnapshot>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement limit in limits.EnumerateArray())
        {
            if (limit.ValueKind != JsonValueKind.Object
                || !limit.TryGetProperty("kind", out JsonElement value)
                || value.ValueKind != JsonValueKind.String)
                throw new JsonException("Claude returned an invalid usage limit kind.");
            string? kind = value.GetString();
            string? scopeLabel = null;
            string metricId;
            decimal minutes;
            switch (kind)
            {
                case "session":
                    metricId = ClaudeRateLimitSnapshotMapper.FiveHourMetricId;
                    minutes = 300m;
                    break;
                case "weekly_all":
                    metricId = ClaudeRateLimitSnapshotMapper.SevenDayMetricId;
                    minutes = 10080m;
                    break;
                case "weekly_scoped":
                    if (!limit.TryGetProperty("scope", out JsonElement scope)
                        || scope.ValueKind != JsonValueKind.Object)
                        throw new JsonException("A scoped Claude limit has no scope.");
                    scopeLabel = ReadScopeLabel(scope);
                    metricId = ScopedWeeklyMetricPrefix + Convert.ToHexStringLower(
                        SHA256.HashData(Encoding.UTF8.GetBytes(scopeLabel)))[..16];
                    minutes = 10080m;
                    break;
                default:
                    continue;
            }

            if (!seen.Add(metricId)
                || !limit.TryGetProperty("percent", out JsonElement percent)
                || percent.ValueKind != JsonValueKind.Number
                || !percent.TryGetDecimal(out decimal used)
                || used < 0m)
                throw new JsonException("Claude returned an invalid or duplicate usage limit.");

            DateTimeOffset? reset = null;
            if (limit.TryGetProperty("resets_at", out JsonElement resetsAt)
                && resetsAt.ValueKind != JsonValueKind.Null)
            {
                if (resetsAt.ValueKind != JsonValueKind.String
                    || !resetsAt.TryGetDateTimeOffset(out DateTimeOffset parsed))
                    throw new JsonException("Claude returned an invalid reset time.");
                reset = parsed.ToUniversalTime();
            }

            // is_active identifies the currently binding limit. Inactive limits still have
            // meaningful usage, including the zero-percent session and the Fable pool.
            metrics.Add(new ProgressMetricSnapshot(
                new MetricId(metricId), used, 100m, reset, provenance, unit: "percent",
                labelEvidence: new MetricLabelEvidence(kind, null, scopeLabel,
                    MetricLabelSource.Provider, MetricLabelConfidence.Exact)));
            metrics.Add(new ScalarMetricSnapshot(
                new MetricId(metricId + ".window-minutes"), minutes, "minutes", provenance));
        }

        if (metrics.Count == 0)
            throw new JsonException("Claude returned no supported account limits.");

        return new ProviderSnapshot(new ProviderId("claude"), "Claude", planLabel,
            observedAtUtc, observedAtUtc, timeZoneId, metrics, CoverageKind.Complete, 1);
    }

    private static string ReadScopeLabel(JsonElement scope)
    {
        var labels = new List<string>();
        foreach (string name in new[] { "model", "surface" })
        {
            if (scope.TryGetProperty(name, out JsonElement part)
                && part.ValueKind == JsonValueKind.Object
                && part.TryGetProperty("display_name", out JsonElement display)
                && display.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(display.GetString()))
                labels.Add(display.GetString()!.Trim());
        }

        string label = string.Join(" · ", labels);
        if (label.Length is 0 or > 160 || label.Any(char.IsControl))
            throw new JsonException("Claude returned an unnamed usage scope.");
        return label;
    }
}

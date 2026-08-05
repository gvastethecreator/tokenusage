using System.Text.Json;
using System.Text.Json.Serialization;
using TokenUsage.Core.Providers;

namespace TokenUsage.Cli;

internal static class LimitsDocument
{
    internal const string SchemaVersion = "tokenusage.limits.v1";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    internal static string Serialize(
        DateTimeOffset generatedAt,
        IReadOnlyList<ProviderSnapshot> snapshots,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        ArgumentNullException.ThrowIfNull(clock);
        if (generatedAt.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The timestamp must use the UTC offset.", nameof(generatedAt));
        }

        if (snapshots.Any(snapshot => snapshot is null))
        {
            throw new ArgumentException("Snapshots cannot contain null values.", nameof(snapshots));
        }

        ProviderDocument[] providers = snapshots
            .OrderBy(snapshot => snapshot.ProviderId.Value, StringComparer.Ordinal)
            .Select(snapshot => CreateProvider(snapshot, clock))
            .ToArray();
        var document = new RootDocument(
            SchemaVersion,
            generatedAt,
            providers,
            providers.Any(provider => provider.Stale));

        return JsonSerializer.Serialize(document, JsonOptions);
    }

    private static ProviderDocument CreateProvider(
        ProviderSnapshot snapshot,
        TimeProvider clock) =>
        new(
            snapshot.ProviderId.Value,
            snapshot.DisplayName,
            snapshot.PlanLabel,
            snapshot.SourceObservedAtUtc,
            SnapshotFreshness.IsStale(snapshot, clock),
            ToLowerCamelCase(snapshot.Coverage),
            snapshot.Metrics
                .OrderBy(metric => metric.Id.Value, StringComparer.Ordinal)
                .Select(CreateMetric)
                .ToArray());

    private static MetricDocument CreateMetric(MetricSnapshot metric) => metric switch
    {
        ProgressMetricSnapshot progress => new MetricDocument(
            "progress",
            progress.Id.Value,
            progress.Used,
            progress.Limit,
            progress.RemainingPercent,
            progress.ResetsAtUtc,
            null,
            null,
            ToLowerCamelCase(progress.Provenance.SourceKind),
            ToLowerCamelCase(progress.Provenance.MeasurementKind)),
        ScalarMetricSnapshot scalar => new MetricDocument(
            "scalar",
            scalar.Id.Value,
            null,
            null,
            null,
            null,
            scalar.Value,
            scalar.Unit,
            ToLowerCamelCase(scalar.Provenance.SourceKind),
            ToLowerCamelCase(scalar.Provenance.MeasurementKind)),
        _ => throw new NotSupportedException("The metric type is not supported by this CLI contract."),
    };

    private static string ToLowerCamelCase<TEnum>(TEnum value)
        where TEnum : struct, Enum =>
        JsonNamingPolicy.CamelCase.ConvertName(value.ToString());

    private sealed record RootDocument(
        string SchemaVersion,
        DateTimeOffset GeneratedAt,
        IReadOnlyList<ProviderDocument> Providers,
        bool Stale);

    private sealed record ProviderDocument(
        string Id,
        string Name,
        string? Plan,
        DateTimeOffset ObservedAt,
        bool Stale,
        string Coverage,
        IReadOnlyList<MetricDocument> Metrics);

    private sealed record MetricDocument(
        string Kind,
        string Id,
        decimal? Used,
        decimal? Limit,
        decimal? RemainingPercent,
        DateTimeOffset? ResetsAt,
        decimal? Value,
        string? Unit,
        string SourceKind,
        string MeasurementKind);
}

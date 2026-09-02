using TokenUsage.Core.Providers;

namespace TokenUsage.Core.Cache.Internal;

internal static class SnapshotCacheMapper
{
    public const int MaximumSnapshots = 256;
    public const int MaximumMetricsPerSnapshot = 512;
    public const int MaximumCapabilitiesPerSnapshot = 128;

    public static SnapshotCacheDocumentV1 ToDocument(
        IEnumerable<ProviderSnapshot> snapshots,
        DateTimeOffset writtenAtUtc)
    {
        ProviderSnapshot[] snapshotArray = snapshots.ToArray();
        if (snapshotArray.Length > MaximumSnapshots)
        {
            throw new InvalidOperationException($"A cache document cannot contain more than {MaximumSnapshots} providers.");
        }

        return new SnapshotCacheDocumentV1
        {
            SchemaVersion = SnapshotStore.CurrentSchemaVersion,
            WrittenAtUtc = writtenAtUtc,
            Snapshots = snapshotArray.Select(ToProvider).ToList(),
        };
    }

    public static IReadOnlyList<ProviderSnapshot> FromDocument(SnapshotCacheDocumentV1 document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.SchemaVersion != SnapshotStore.CurrentSchemaVersion)
        {
            throw new SnapshotCacheFormatException("The cache schema does not match the v1 mapper.");
        }

        RequireUtc(document.WrittenAtUtc, nameof(document.WrittenAtUtc));
        if (document.Snapshots is null)
        {
            throw new SnapshotCacheFormatException("The cache snapshot list is missing.");
        }

        if (document.Snapshots.Count > MaximumSnapshots)
        {
            throw new SnapshotCacheFormatException($"The cache contains more than {MaximumSnapshots} providers.");
        }

        if (document.Snapshots.Any(snapshot => snapshot is null))
        {
            throw new SnapshotCacheFormatException("The cache snapshot list contains a null entry.");
        }

        ProviderSnapshot[] snapshots = document.Snapshots.Select(FromProvider).ToArray();
        string? duplicateProvider = snapshots
            .GroupBy(snapshot => snapshot.ProviderId.Value, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)
            ?.Key;
        if (duplicateProvider is not null)
        {
            throw new SnapshotCacheFormatException($"Provider '{duplicateProvider}' appears more than once.");
        }

        return Array.AsReadOnly(snapshots);
    }

    private static SnapshotCacheProviderV1 ToProvider(ProviderSnapshot snapshot)
    {
        if (snapshot.Metrics.Count > MaximumMetricsPerSnapshot)
        {
            throw new InvalidOperationException(
                $"Provider '{snapshot.ProviderId.Value}' cannot cache more than {MaximumMetricsPerSnapshot} metrics.");
        }

        if (snapshot.Capabilities.Count > MaximumCapabilitiesPerSnapshot)
        {
            throw new InvalidOperationException(
                $"Provider '{snapshot.ProviderId.Value}' cannot cache more than {MaximumCapabilitiesPerSnapshot} capabilities.");
        }

        return new SnapshotCacheProviderV1
        {
            ProviderId = snapshot.ProviderId.Value,
            DisplayName = snapshot.DisplayName,
            PlanLabel = snapshot.PlanLabel,
            FetchedAtUtc = snapshot.FetchedAtUtc,
            SourceObservedAtUtc = snapshot.SourceObservedAtUtc,
            TimeZoneId = snapshot.TimeZoneId,
            Coverage = snapshot.Coverage.ToString(),
            AdapterContractVersion = snapshot.AdapterContractVersion,
            Metrics = snapshot.Metrics.Select(ToMetric).ToList(),
            Capabilities = snapshot.Capabilities.Select(ToCapability).ToList(),
        };
    }

    private static SnapshotCacheCapabilityV1 ToCapability(
        ProviderCapabilitySnapshot capability) => new()
        {
            Id = capability.Id.Value,
            State = capability.State.ToString(),
            Provenance = ToProvenance(capability.Provenance),
        };

    private static SnapshotCacheMetricV1 ToMetric(MetricSnapshot metric)
    {
        var dto = new SnapshotCacheMetricV1
        {
            Id = metric.Id.Value,
            Provenance = ToProvenance(metric.Provenance),
        };

        switch (metric)
        {
            case ProgressMetricSnapshot progress:
                dto.Kind = "progress";
                dto.Used = progress.Used;
                dto.Limit = progress.Limit;
                dto.ResetsAtUtc = progress.ResetsAtUtc;
                dto.Unit = progress.Unit;
                dto.ResetCadence = progress.ResetCadence?.ToString();
                dto.IsActive = progress.IsActive;
                dto.DisplayName = progress.DisplayName;
                break;
            case ScalarMetricSnapshot scalar:
                dto.Kind = "scalar";
                dto.Value = scalar.Value;
                dto.Unit = scalar.Unit;
                break;
            default:
                throw new InvalidOperationException(
                    $"Metric type '{metric.GetType().Name}' is not supported by cache schema v1.");
        }

        return dto;
    }

    private static SnapshotCacheProvenanceV1 ToProvenance(DataProvenance provenance) =>
        new()
        {
            SourceKind = provenance.SourceKind.ToString(),
            MeasurementKind = provenance.MeasurementKind.ToString(),
            AdapterVersion = provenance.AdapterVersion,
        };

    private static ProviderSnapshot FromProvider(SnapshotCacheProviderV1 dto)
    {
        if (dto.Metrics is null)
        {
            throw new SnapshotCacheFormatException("A provider metric list is missing.");
        }

        if (dto.Metrics.Count > MaximumMetricsPerSnapshot)
        {
            throw new SnapshotCacheFormatException(
                $"A provider contains more than {MaximumMetricsPerSnapshot} metrics.");
        }

        if (dto.Metrics.Any(metric => metric is null))
        {
            throw new SnapshotCacheFormatException("A provider metric list contains a null entry.");
        }

        if (dto.Capabilities is { Count: > MaximumCapabilitiesPerSnapshot })
        {
            throw new SnapshotCacheFormatException(
                $"A provider contains more than {MaximumCapabilitiesPerSnapshot} capabilities.");
        }

        if (dto.Capabilities?.Any(capability => capability is null) == true)
        {
            throw new SnapshotCacheFormatException(
                "A provider capability list contains a null entry.");
        }

        return new ProviderSnapshot(
            new ProviderId(RequireText(dto.ProviderId, nameof(dto.ProviderId))),
            RequireText(dto.DisplayName, nameof(dto.DisplayName)),
            dto.PlanLabel,
            RequireUtc(dto.FetchedAtUtc, nameof(dto.FetchedAtUtc)),
            RequireUtc(dto.SourceObservedAtUtc, nameof(dto.SourceObservedAtUtc)),
            RequireText(dto.TimeZoneId, nameof(dto.TimeZoneId)),
            dto.Metrics.Select(FromMetric),
            ParseEnum<CoverageKind>(dto.Coverage, nameof(dto.Coverage)),
            dto.AdapterContractVersion
                ?? throw new SnapshotCacheFormatException("The adapter contract version is missing."),
            dto.Capabilities?.Select(FromCapability));
    }

    private static ProviderCapabilitySnapshot FromCapability(
        SnapshotCacheCapabilityV1 dto) => new(
        new CapabilityId(RequireText(dto.Id, nameof(dto.Id))),
        ParseEnum<ProviderCapabilityState>(dto.State, nameof(dto.State)),
        FromProvenance(
            dto.Provenance
            ?? throw new SnapshotCacheFormatException("Capability provenance is missing.")));

    private static MetricSnapshot FromMetric(SnapshotCacheMetricV1 dto)
    {
        var id = new MetricId(RequireText(dto.Id, nameof(dto.Id)));
        DataProvenance provenance = FromProvenance(
            dto.Provenance ?? throw new SnapshotCacheFormatException("Metric provenance is missing."));

        return dto.Kind switch
        {
            "progress" => new ProgressMetricSnapshot(
                id,
                dto.Used ?? throw new SnapshotCacheFormatException("Progress used value is missing."),
                dto.Limit ?? throw new SnapshotCacheFormatException("Progress limit is missing."),
                OptionalUtc(dto.ResetsAtUtc, nameof(dto.ResetsAtUtc)),
                provenance,
                OptionalText(dto.Unit, nameof(dto.Unit)),
                ParseOptionalEnum<ProgressResetCadence>(
                    dto.ResetCadence,
                    nameof(dto.ResetCadence)),
                dto.IsActive,
                OptionalText(dto.DisplayName, nameof(dto.DisplayName))),
            "scalar" => new ScalarMetricSnapshot(
                id,
                dto.Value ?? throw new SnapshotCacheFormatException("Scalar value is missing."),
                RequireText(dto.Unit, nameof(dto.Unit)),
                provenance),
            _ => throw new SnapshotCacheFormatException("The metric kind is missing or unsupported."),
        };
    }

    private static DataProvenance FromProvenance(SnapshotCacheProvenanceV1 dto) =>
        new(
            ParseEnum<SourceKind>(dto.SourceKind, nameof(dto.SourceKind)),
            ParseEnum<MeasurementKind>(dto.MeasurementKind, nameof(dto.MeasurementKind)),
            RequireText(dto.AdapterVersion, nameof(dto.AdapterVersion)));

    private static TEnum ParseEnum<TEnum>(string? value, string fieldName)
        where TEnum : struct, Enum
    {
        if (value is null
            || !Enum.TryParse(value, ignoreCase: false, out TEnum parsed)
            || !Enum.IsDefined(parsed))
        {
            throw new SnapshotCacheFormatException($"Field '{fieldName}' has an unsupported value.");
        }

        return parsed;
    }

    private static TEnum? ParseOptionalEnum<TEnum>(string? value, string fieldName)
        where TEnum : struct, Enum =>
        value is null ? null : ParseEnum<TEnum>(value, fieldName);

    private static string? OptionalText(string? value, string fieldName)
    {
        if (value is null)
        {
            return null;
        }

        return RequireText(value, fieldName);
    }

    private static string RequireText(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new SnapshotCacheFormatException($"Field '{fieldName}' is missing.");
        }

        return value;
    }

    private static DateTimeOffset RequireUtc(DateTimeOffset? value, string fieldName)
    {
        if (value is null || value.Value.Offset != TimeSpan.Zero)
        {
            throw new SnapshotCacheFormatException($"Field '{fieldName}' must contain a UTC timestamp.");
        }

        return value.Value;
    }

    private static DateTimeOffset? OptionalUtc(DateTimeOffset? value, string fieldName)
    {
        if (value is not null && value.Value.Offset != TimeSpan.Zero)
        {
            throw new SnapshotCacheFormatException($"Field '{fieldName}' must contain a UTC timestamp.");
        }

        return value;
    }
}

internal sealed class SnapshotCacheFormatException(string message) : Exception(message);

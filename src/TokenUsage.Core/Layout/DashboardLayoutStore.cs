using System.Text.Json;
using System.Text.Json.Serialization;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Storage;

namespace TokenUsage.Core.Layout;

public sealed class DashboardLayoutStore
{
    public const int SchemaVersion = 2;
    public const string DefaultFileName = "dashboard-layout.v1.json";
    public const int MaxDocumentBytes = 64 * 1024;
    public const int MaxJsonDepth = 16;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = true,
        MaxDepth = MaxJsonDepth,
    };

    private readonly VersionedDocumentFile _document;

    public DashboardLayoutStore(string documentPath, TimeProvider? clock = null)
    {
        _document = new VersionedDocumentFile(
            documentPath,
            mutexNamePrefix: "TokenUsage.DashboardLayoutStore",
            clock ?? TimeProvider.System,
            lockTimeoutMessage: "Timed out while waiting for the dashboard layout lock.");
    }

    public string DocumentPath => _document.DocumentPath;

    public Task<DashboardLayoutLoadResult> LoadAsync(
        CancellationToken cancellationToken = default) =>
        _document.RunLockedAsync(LoadCore, cancellationToken);

    public Task<DashboardLayoutSaveResult> SaveAsync(
        DashboardLayout layout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var validated = new DashboardLayout(layout.Providers);
        return _document.RunLockedAsync(() => SaveCore(validated), cancellationToken);
    }

    private DashboardLayoutLoadResult LoadCore()
    {
        if (!_document.Exists)
        {
            return DashboardLayoutLoadResult.Empty.Instance;
        }

        try
        {
            byte[] bytes = _document.ReadBoundedBytes(MaxDocumentBytes);
            ReadOnlyMemory<byte> json = VersionedDocumentFile.RemoveUtf8Preamble(bytes);
            using JsonDocument parsed = JsonDocument.Parse(
                json,
                new JsonDocumentOptions { MaxDepth = MaxJsonDepth });
            int version = ReadSchemaVersion(parsed.RootElement);
            if (version > SchemaVersion)
            {
                return new DashboardLayoutLoadResult.UnsupportedVersion(version);
            }

            if (version is not 1 and not SchemaVersion)
            {
                return QuarantineCorrupt();
            }

            LayoutDocumentV1? document = JsonSerializer.Deserialize<LayoutDocumentV1>(
                json.Span,
                JsonOptions);
            return document is null
                ? QuarantineCorrupt()
                : new DashboardLayoutLoadResult.Loaded(FromDocument(document));
        }
        catch (Exception exception) when (IsInvalidDocument(exception))
        {
            return QuarantineCorrupt();
        }
    }

    private DashboardLayoutSaveResult SaveCore(DashboardLayout layout)
    {
        int? existingVersion = ProbeExistingSchemaVersion();
        if (existingVersion > SchemaVersion)
        {
            return new DashboardLayoutSaveResult.RefusedUnsupportedVersion(
                existingVersion.Value);
        }

        LayoutDocumentV1 document = ToDocument(layout);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
        _document.WriteAtomically(bytes, MaxDocumentBytes);
        return DashboardLayoutSaveResult.Saved.Instance;
    }

    private int? ProbeExistingSchemaVersion()
    {
        if (!_document.Exists)
        {
            return null;
        }

        byte[] bytes;
        try
        {
            bytes = _document.ReadBoundedBytes(MaxDocumentBytes);
        }
        catch (Exception exception) when (exception is VersionedDocumentFormatException
            or IOException
            or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                "The existing dashboard layout cannot be replaced safely.",
                exception);
        }

        try
        {
            ReadOnlyMemory<byte> json = VersionedDocumentFile.RemoveUtf8Preamble(bytes);
            using JsonDocument parsed = JsonDocument.Parse(
                json,
                new JsonDocumentOptions { MaxDepth = MaxJsonDepth });
            int version = ReadSchemaVersion(parsed.RootElement);
            if (version is 1 or SchemaVersion)
            {
                LayoutDocumentV1? document = JsonSerializer.Deserialize<LayoutDocumentV1>(
                    json.Span,
                    JsonOptions);
                if (document is null)
                {
                    throw new LayoutDocumentFormatException();
                }

                _ = FromDocument(document);
            }

            return version;
        }
        catch (Exception exception) when (IsInvalidDocument(exception))
        {
            throw new InvalidOperationException(
                "The existing dashboard layout cannot be replaced safely.",
                exception);
        }
    }

    private static int ReadSchemaVersion(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("schemaVersion", out JsonElement versionElement)
            || !versionElement.TryGetInt32(out int version)
            || version < 1)
        {
            throw new LayoutDocumentFormatException();
        }

        return version;
    }

    private static DashboardLayout FromDocument(LayoutDocumentV1 document)
    {
        if (document.Providers is null)
        {
            throw new LayoutDocumentFormatException();
        }

        var providers = new List<ProviderLayoutPreference>(document.Providers.Count);
        foreach (ProviderPreferenceV1? provider in document.Providers)
        {
            if (provider is null
                || string.IsNullOrWhiteSpace(provider.ProviderId)
                || provider.Metrics is null)
            {
                throw new LayoutDocumentFormatException();
            }

            var metrics = new List<MetricLayoutPreference>(provider.Metrics.Count);
            foreach (MetricPreferenceV1? metric in provider.Metrics)
            {
                if (metric is null || string.IsNullOrWhiteSpace(metric.MetricId))
                {
                    throw new LayoutDocumentFormatException();
                }

                metrics.Add(new MetricLayoutPreference(
                    new MetricId(metric.MetricId),
                    metric.IsVisible,
                    metric.IsHighlighted,
                    metric.IsOnDemand));
            }

            providers.Add(new ProviderLayoutPreference(
                new ProviderId(provider.ProviderId),
                provider.IsVisible,
                provider.IsHighlighted,
                metrics,
                provider.ColorHex));
        }

        return new DashboardLayout(providers);
    }

    private static LayoutDocumentV1 ToDocument(DashboardLayout layout) => new()
    {
        SchemaVersion = SchemaVersion,
        Providers = layout.Providers.Select(provider => new ProviderPreferenceV1
        {
            ProviderId = provider.ProviderId.Value,
            IsVisible = provider.IsVisible,
            IsHighlighted = provider.IsHighlighted,
            ColorHex = provider.ColorHex,
            Metrics = provider.Metrics.Select(metric => new MetricPreferenceV1
            {
                MetricId = metric.MetricId.Value,
                IsVisible = metric.IsVisible,
                IsHighlighted = metric.IsHighlighted,
                IsOnDemand = metric.IsOnDemand,
            }).ToList(),
        }).ToList(),
    };

    private DashboardLayoutLoadResult.Corrupt QuarantineCorrupt() =>
        new(_document.QuarantineCorrupt());

    private static bool IsInvalidDocument(Exception exception) => exception is
        JsonException
        or LayoutDocumentFormatException
        or VersionedDocumentFormatException
        or ArgumentException
        or InvalidOperationException
        or NotSupportedException
        or OverflowException;

    private sealed class LayoutDocumentV1
    {
        public int SchemaVersion { get; set; }

        public List<ProviderPreferenceV1>? Providers { get; set; }
    }

    private sealed class ProviderPreferenceV1
    {
        public string? ProviderId { get; set; }

        public bool IsVisible { get; set; }

        public bool IsHighlighted { get; set; }

        public string? ColorHex { get; set; }

        public List<MetricPreferenceV1>? Metrics { get; set; }
    }

    private sealed class MetricPreferenceV1
    {
        public string? MetricId { get; set; }

        public bool IsVisible { get; set; }

        public bool IsHighlighted { get; set; }

        public bool IsOnDemand { get; set; }
    }

    private sealed class LayoutDocumentFormatException : Exception;
}

public abstract class DashboardLayoutLoadResult
{
    private DashboardLayoutLoadResult()
    {
    }

    public sealed class Empty : DashboardLayoutLoadResult
    {
        public static Empty Instance { get; } = new();

        private Empty()
        {
        }
    }

    public sealed class Loaded : DashboardLayoutLoadResult
    {
        public Loaded(DashboardLayout layout)
        {
            Layout = layout ?? throw new ArgumentNullException(nameof(layout));
        }

        public DashboardLayout Layout { get; }
    }

    public sealed class Corrupt : DashboardLayoutLoadResult
    {
        public Corrupt(string quarantineFileName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(quarantineFileName);
            if (quarantineFileName.IndexOfAny(['/', '\\', ':']) >= 0)
            {
                throw new ArgumentException(
                    "The quarantine value must be a file name.",
                    nameof(quarantineFileName));
            }

            QuarantineFileName = quarantineFileName;
        }

        public string QuarantineFileName { get; }
    }

    public sealed class UnsupportedVersion : DashboardLayoutLoadResult
    {
        public UnsupportedVersion(int schemaVersion)
        {
            SchemaVersion = schemaVersion;
        }

        public int SchemaVersion { get; }
    }
}

public abstract class DashboardLayoutSaveResult
{
    private DashboardLayoutSaveResult()
    {
    }

    public sealed class Saved : DashboardLayoutSaveResult
    {
        public static Saved Instance { get; } = new();

        private Saved()
        {
        }
    }

    public sealed class RefusedUnsupportedVersion : DashboardLayoutSaveResult
    {
        public RefusedUnsupportedVersion(int schemaVersion)
        {
            SchemaVersion = schemaVersion;
        }

        public int SchemaVersion { get; }
    }
}

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WOpenUsage.Core.Providers;

namespace WOpenUsage.Core.Layout;

public sealed class DashboardLayoutStore
{
    public const int SchemaVersion = 1;
    public const string DefaultFileName = "dashboard-layout.v1.json";
    public const int MaxDocumentBytes = 64 * 1024;
    public const int MaxJsonDepth = 16;

    private static readonly TimeSpan MutexTimeout = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = true,
        MaxDepth = MaxJsonDepth,
    };

    private readonly TimeProvider _clock;
    private readonly string _mutexName;

    public DashboardLayoutStore(string documentPath, TimeProvider? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentPath);
        if (Path.EndsInDirectorySeparator(documentPath))
        {
            throw new ArgumentException(
                "The dashboard layout path must include a file name.",
                nameof(documentPath));
        }

        DocumentPath = Path.GetFullPath(documentPath);
        if (Directory.Exists(DocumentPath)
            || string.IsNullOrWhiteSpace(Path.GetFileName(DocumentPath)))
        {
            throw new ArgumentException(
                "The dashboard layout path must include a file name.",
                nameof(documentPath));
        }

        _clock = clock ?? TimeProvider.System;
        _mutexName = CreateMutexName(DocumentPath);
    }

    public string DocumentPath { get; }

    public Task<DashboardLayoutLoadResult> LoadAsync(
        CancellationToken cancellationToken = default) =>
        RunLocked(LoadCore, cancellationToken);

    public Task<DashboardLayoutSaveResult> SaveAsync(
        DashboardLayout layout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var validated = new DashboardLayout(layout.Providers);
        return RunLocked(() => SaveCore(validated), cancellationToken);
    }

    private DashboardLayoutLoadResult LoadCore()
    {
        if (!File.Exists(DocumentPath))
        {
            return DashboardLayoutLoadResult.Empty.Instance;
        }

        try
        {
            byte[] bytes = ReadBoundedDocument();
            ReadOnlyMemory<byte> json = RemoveUtf8Preamble(bytes);
            using JsonDocument parsed = JsonDocument.Parse(
                json,
                new JsonDocumentOptions { MaxDepth = MaxJsonDepth });
            int version = ReadSchemaVersion(parsed.RootElement);
            if (version > SchemaVersion)
            {
                return new DashboardLayoutLoadResult.UnsupportedVersion(version);
            }

            if (version != SchemaVersion)
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
        if (bytes.Length is <= 0 or > MaxDocumentBytes)
        {
            throw new InvalidOperationException(
                $"The dashboard layout exceeds the {MaxDocumentBytes}-byte limit.");
        }

        WriteAtomically(bytes);
        return DashboardLayoutSaveResult.Saved.Instance;
    }

    private int? ProbeExistingSchemaVersion()
    {
        if (!File.Exists(DocumentPath))
        {
            return null;
        }

        var file = new FileInfo(DocumentPath);
        if (file.Length is <= 0 or > MaxDocumentBytes)
        {
            throw new InvalidOperationException(
                "The existing dashboard layout cannot be replaced safely.");
        }

        byte[] bytes = File.ReadAllBytes(DocumentPath);
        if (bytes.Length is <= 0 or > MaxDocumentBytes)
        {
            throw new InvalidOperationException(
                "The existing dashboard layout cannot be replaced safely.");
        }

        try
        {
            using JsonDocument parsed = JsonDocument.Parse(
                RemoveUtf8Preamble(bytes),
                new JsonDocumentOptions { MaxDepth = MaxJsonDepth });
            int version = ReadSchemaVersion(parsed.RootElement);
            if (version == SchemaVersion)
            {
                LayoutDocumentV1? document = JsonSerializer.Deserialize<LayoutDocumentV1>(
                    RemoveUtf8Preamble(bytes).Span,
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

    private byte[] ReadBoundedDocument()
    {
        var file = new FileInfo(DocumentPath);
        if (file.Length is <= 0 or > MaxDocumentBytes)
        {
            throw new LayoutDocumentFormatException();
        }

        byte[] bytes = File.ReadAllBytes(DocumentPath);
        if (bytes.Length is <= 0 or > MaxDocumentBytes)
        {
            throw new LayoutDocumentFormatException();
        }

        return bytes;
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
                    metric.IsHighlighted));
            }

            providers.Add(new ProviderLayoutPreference(
                new ProviderId(provider.ProviderId),
                provider.IsVisible,
                provider.IsHighlighted,
                metrics));
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
            Metrics = provider.Metrics.Select(metric => new MetricPreferenceV1
            {
                MetricId = metric.MetricId.Value,
                IsVisible = metric.IsVisible,
                IsHighlighted = metric.IsHighlighted,
            }).ToList(),
        }).ToList(),
    };

    private DashboardLayoutLoadResult.Corrupt QuarantineCorrupt()
    {
        string directory = GetDocumentDirectory();
        string fileName = Path.GetFileName(DocumentPath);
        string stamp = _clock.GetUtcNow()
            .ToUniversalTime()
            .ToString("yyyyMMddTHHmmssfffZ", CultureInfo.InvariantCulture);
        string quarantineFileName =
            $"{fileName}.corrupt-{stamp}-{Guid.NewGuid():N}";
        string quarantinePath = Path.Combine(directory, quarantineFileName);

        File.Move(DocumentPath, quarantinePath);
        return new DashboardLayoutLoadResult.Corrupt(quarantineFileName);
    }

    private void WriteAtomically(byte[] bytes)
    {
        string directory = GetDocumentDirectory();
        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(
            directory,
            $"{Path.GetFileName(DocumentPath)}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var stream = new FileStream(
                temporaryPath,
                new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    BufferSize = 4096,
                    Options = FileOptions.WriteThrough,
                }))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, DocumentPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private Task<TResult> RunLocked<TResult>(
        Func<TResult> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var mutex = new Mutex(initiallyOwned: false, _mutexName);
            bool ownsMutex = false;

            try
            {
                try
                {
                    if (cancellationToken.CanBeCanceled)
                    {
                        int signaled = WaitHandle.WaitAny(
                            [mutex, cancellationToken.WaitHandle],
                            MutexTimeout);
                        if (signaled == 1)
                        {
                            throw new OperationCanceledException(cancellationToken);
                        }

                        if (signaled == WaitHandle.WaitTimeout)
                        {
                            throw new TimeoutException(
                                "Timed out while waiting for the dashboard layout lock.");
                        }

                        ownsMutex = true;
                    }
                    else
                    {
                        ownsMutex = mutex.WaitOne(MutexTimeout);
                        if (!ownsMutex)
                        {
                            throw new TimeoutException(
                                "Timed out while waiting for the dashboard layout lock.");
                        }
                    }
                }
                catch (AbandonedMutexException)
                {
                    ownsMutex = true;
                }

                cancellationToken.ThrowIfCancellationRequested();
                return operation();
            }
            finally
            {
                if (ownsMutex)
                {
                    mutex.ReleaseMutex();
                }
            }
        });
    }

    private string GetDocumentDirectory() =>
        Path.GetDirectoryName(DocumentPath)
        ?? throw new InvalidOperationException(
            "The dashboard layout path has no parent directory.");

    private static string CreateMutexName(string documentPath)
    {
        string normalized = Path.GetFullPath(documentPath).ToUpperInvariant();
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return $"Local\\WOpenUsage.DashboardLayoutStore.{Convert.ToHexString(hash.AsSpan(0, 16))}";
    }

    private static ReadOnlyMemory<byte> RemoveUtf8Preamble(byte[] bytes) =>
        bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble)
            ? bytes.AsMemory(Encoding.UTF8.Preamble.Length)
            : bytes;

    private static bool IsInvalidDocument(Exception exception) => exception is
        JsonException
        or LayoutDocumentFormatException
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

        public List<MetricPreferenceV1>? Metrics { get; set; }
    }

    private sealed class MetricPreferenceV1
    {
        public string? MetricId { get; set; }

        public bool IsVisible { get; set; }

        public bool IsHighlighted { get; set; }
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

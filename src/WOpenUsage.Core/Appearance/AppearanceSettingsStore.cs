using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WOpenUsage.Core.Appearance;

public sealed class AppearanceSettingsStore
{
    public const int SchemaVersion = 1;
    public const string DefaultFileName = "appearance.v1.json";
    public const int MaxDocumentBytes = 16 * 1024;
    public const int MaxJsonDepth = 8;

    private static readonly TimeSpan MutexTimeout = TimeSpan.FromSeconds(30);
    private readonly TimeProvider _clock;
    private readonly string _mutexName;

    public AppearanceSettingsStore(string documentPath, TimeProvider? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentPath);
        if (Path.EndsInDirectorySeparator(documentPath))
        {
            throw new ArgumentException(
                "The appearance settings path must include a file name.",
                nameof(documentPath));
        }

        DocumentPath = Path.GetFullPath(documentPath);
        if (Directory.Exists(DocumentPath)
            || string.IsNullOrWhiteSpace(Path.GetFileName(DocumentPath)))
        {
            throw new ArgumentException(
                "The appearance settings path must include a file name.",
                nameof(documentPath));
        }

        _clock = clock ?? TimeProvider.System;
        _mutexName = CreateMutexName(DocumentPath);
    }

    public string DocumentPath { get; }

    public Task<AppearanceSettingsLoadResult> LoadAsync(
        CancellationToken cancellationToken = default) =>
        RunLocked(LoadCore, cancellationToken);

    public Task<AppearanceSettingsSaveResult> SaveAsync(
        AppearanceSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return RunLocked(() => SaveCore(settings), cancellationToken);
    }

    private AppearanceSettingsLoadResult LoadCore()
    {
        if (!File.Exists(DocumentPath))
        {
            return AppearanceSettingsLoadResult.Defaults.Instance;
        }

        try
        {
            byte[] bytes = ReadBoundedDocument();
            using JsonDocument parsed = Parse(bytes);
            int version = ReadSchemaVersion(parsed.RootElement);
            if (version > SchemaVersion)
            {
                return new AppearanceSettingsLoadResult.UnsupportedVersion(version);
            }

            AppearanceSettings settings = version switch
            {
                0 => ReadLegacyDocument(parsed.RootElement),
                SchemaVersion => ReadVersionOneDocument(parsed.RootElement),
                _ => throw new AppearanceDocumentFormatException(),
            };
            return new AppearanceSettingsLoadResult.Loaded(
                settings,
                requiresMigration: version == 0);
        }
        catch (Exception exception) when (IsInvalidDocument(exception))
        {
            return QuarantineCorrupt();
        }
    }

    private AppearanceSettingsSaveResult SaveCore(AppearanceSettings settings)
    {
        int? existingVersion = ProbeExistingSchemaVersion();
        if (existingVersion > SchemaVersion)
        {
            return new AppearanceSettingsSaveResult.RefusedUnsupportedVersion(
                existingVersion.Value);
        }

        byte[] bytes = Serialize(settings);
        if (bytes.Length is <= 0 or > MaxDocumentBytes)
        {
            throw new InvalidOperationException(
                $"The appearance settings exceed the {MaxDocumentBytes}-byte limit.");
        }

        WriteAtomically(bytes);
        return AppearanceSettingsSaveResult.Saved.Instance;
    }

    private int? ProbeExistingSchemaVersion()
    {
        if (!File.Exists(DocumentPath))
        {
            return null;
        }

        try
        {
            byte[] bytes = ReadBoundedDocument();
            using JsonDocument parsed = Parse(bytes);
            int version = ReadSchemaVersion(parsed.RootElement);
            _ = version switch
            {
                0 => ReadLegacyDocument(parsed.RootElement),
                SchemaVersion => ReadVersionOneDocument(parsed.RootElement),
                > SchemaVersion => null,
                _ => throw new AppearanceDocumentFormatException(),
            };
            return version;
        }
        catch (Exception exception) when (IsInvalidDocument(exception))
        {
            throw new InvalidOperationException(
                "The existing appearance settings cannot be replaced safely.",
                exception);
        }
    }

    private static AppearanceSettings ReadVersionOneDocument(JsonElement root) => new(
        ReadRequiredEnum<AppThemeMode>(root, "theme"),
        ReadRequiredEnum<AppDensityMode>(root, "density"),
        ReadRequiredBoolean(root, "increaseTransparency"),
        ReadRequiredEnum<UsageDisplayMode>(root, "usageDisplay"),
        ReadRequiredEnum<ResetTimeDisplayMode>(root, "resetTimeDisplay"));

    private static AppearanceSettings ReadLegacyDocument(JsonElement root)
    {
        EnsureObject(root);
        AppearanceSettings defaults = AppearanceSettings.Default;
        return new AppearanceSettings(
            ReadOptionalEnum(root, "appearance", defaults.Theme),
            ReadOptionalEnum(root, "density", defaults.Density),
            ReadOptionalBoolean(root, "increaseTransparency", defaults.IncreaseTransparency),
            ReadOptionalEnum(root, "meterStyle", defaults.UsageDisplay),
            ReadOptionalEnum(root, "resetDisplayMode", defaults.ResetTimeDisplay));
    }

    private static TEnum ReadRequiredEnum<TEnum>(JsonElement root, string propertyName)
        where TEnum : struct, Enum
    {
        EnsureObject(root);
        if (!root.TryGetProperty(propertyName, out JsonElement value)
            || value.ValueKind != JsonValueKind.String)
        {
            throw new AppearanceDocumentFormatException();
        }

        return ParseEnum<TEnum>(value.GetString());
    }

    private static TEnum ReadOptionalEnum<TEnum>(
        JsonElement root,
        string propertyName,
        TEnum defaultValue)
        where TEnum : struct, Enum
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value))
        {
            return defaultValue;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new AppearanceDocumentFormatException();
        }

        return ParseEnum<TEnum>(value.GetString());
    }

    private static TEnum ParseEnum<TEnum>(string? value)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value)
            || !Enum.TryParse(value, ignoreCase: true, out TEnum parsed)
            || !Enum.IsDefined(parsed))
        {
            throw new AppearanceDocumentFormatException();
        }

        return parsed;
    }

    private static bool ReadRequiredBoolean(JsonElement root, string propertyName)
    {
        EnsureObject(root);
        if (!root.TryGetProperty(propertyName, out JsonElement value)
            || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new AppearanceDocumentFormatException();
        }

        return value.GetBoolean();
    }

    private static bool ReadOptionalBoolean(
        JsonElement root,
        string propertyName,
        bool defaultValue)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value))
        {
            return defaultValue;
        }

        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new AppearanceDocumentFormatException();
        }

        return value.GetBoolean();
    }

    private static int ReadSchemaVersion(JsonElement root)
    {
        EnsureObject(root);
        if (!root.TryGetProperty("schemaVersion", out JsonElement value))
        {
            return 0;
        }

        if (!value.TryGetInt32(out int version) || version < 1)
        {
            throw new AppearanceDocumentFormatException();
        }

        return version;
    }

    private static void EnsureObject(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new AppearanceDocumentFormatException();
        }
    }

    private static byte[] Serialize(AppearanceSettings settings)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(
            stream,
            new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", SchemaVersion);
            writer.WriteString("theme", ToStorageValue(settings.Theme));
            writer.WriteString("density", ToStorageValue(settings.Density));
            writer.WriteBoolean("increaseTransparency", settings.IncreaseTransparency);
            writer.WriteString("usageDisplay", ToStorageValue(settings.UsageDisplay));
            writer.WriteString("resetTimeDisplay", ToStorageValue(settings.ResetTimeDisplay));
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static string ToStorageValue<TEnum>(TEnum value)
        where TEnum : struct, Enum =>
        value.ToString().ToLowerInvariant();

    private byte[] ReadBoundedDocument()
    {
        var file = new FileInfo(DocumentPath);
        if (file.Length is <= 0 or > MaxDocumentBytes)
        {
            throw new AppearanceDocumentFormatException();
        }

        byte[] bytes = File.ReadAllBytes(DocumentPath);
        if (bytes.Length is <= 0 or > MaxDocumentBytes)
        {
            throw new AppearanceDocumentFormatException();
        }

        return bytes;
    }

    private static JsonDocument Parse(byte[] bytes) => JsonDocument.Parse(
        RemoveUtf8Preamble(bytes),
        new JsonDocumentOptions { MaxDepth = MaxJsonDepth });

    private AppearanceSettingsLoadResult.Corrupt QuarantineCorrupt()
    {
        string directory = GetDocumentDirectory();
        string fileName = Path.GetFileName(DocumentPath);
        string stamp = _clock.GetUtcNow()
            .ToUniversalTime()
            .ToString("yyyyMMddTHHmmssfffZ", CultureInfo.InvariantCulture);
        string quarantineFileName = $"{fileName}.corrupt-{stamp}-{Guid.NewGuid():N}";
        File.Move(DocumentPath, Path.Combine(directory, quarantineFileName));
        return new AppearanceSettingsLoadResult.Corrupt(quarantineFileName);
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
                                "Timed out while waiting for the appearance settings lock.");
                        }

                        ownsMutex = true;
                    }
                    else
                    {
                        ownsMutex = mutex.WaitOne(MutexTimeout);
                        if (!ownsMutex)
                        {
                            throw new TimeoutException(
                                "Timed out while waiting for the appearance settings lock.");
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
            "The appearance settings path has no parent directory.");

    private static string CreateMutexName(string documentPath)
    {
        string normalized = Path.GetFullPath(documentPath).ToUpperInvariant();
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return $"Local\\WOpenUsage.AppearanceSettingsStore.{Convert.ToHexString(hash.AsSpan(0, 16))}";
    }

    private static ReadOnlyMemory<byte> RemoveUtf8Preamble(byte[] bytes) =>
        bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble)
            ? bytes.AsMemory(Encoding.UTF8.Preamble.Length)
            : bytes;

    private static bool IsInvalidDocument(Exception exception) => exception is
        JsonException
        or AppearanceDocumentFormatException
        or ArgumentException
        or InvalidOperationException
        or NotSupportedException
        or OverflowException;

    private sealed class AppearanceDocumentFormatException : Exception;
}

public abstract class AppearanceSettingsLoadResult
{
    private AppearanceSettingsLoadResult()
    {
    }

    public sealed class Defaults : AppearanceSettingsLoadResult
    {
        public static Defaults Instance { get; } = new();

        private Defaults()
        {
        }
    }

    public sealed class Loaded : AppearanceSettingsLoadResult
    {
        public Loaded(AppearanceSettings settings, bool requiresMigration)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            RequiresMigration = requiresMigration;
        }

        public AppearanceSettings Settings { get; }

        public bool RequiresMigration { get; }
    }

    public sealed class Corrupt : AppearanceSettingsLoadResult
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

    public sealed class UnsupportedVersion : AppearanceSettingsLoadResult
    {
        public UnsupportedVersion(int schemaVersion)
        {
            SchemaVersion = schemaVersion;
        }

        public int SchemaVersion { get; }
    }
}

public abstract class AppearanceSettingsSaveResult
{
    private AppearanceSettingsSaveResult()
    {
    }

    public sealed class Saved : AppearanceSettingsSaveResult
    {
        public static Saved Instance { get; } = new();

        private Saved()
        {
        }
    }

    public sealed class RefusedUnsupportedVersion : AppearanceSettingsSaveResult
    {
        public RefusedUnsupportedVersion(int schemaVersion)
        {
            SchemaVersion = schemaVersion;
        }

        public int SchemaVersion { get; }
    }
}

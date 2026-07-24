using System.Text.Json;
using WOpenUsage.Core.Storage;

namespace WOpenUsage.Core.Appearance;

public sealed class AppearanceSettingsStore
{
    public const int SchemaVersion = 1;
    public const string DefaultFileName = "appearance.v1.json";
    public const int MaxDocumentBytes = 16 * 1024;
    public const int MaxJsonDepth = 8;

    private readonly VersionedDocumentFile _document;

    public AppearanceSettingsStore(string documentPath, TimeProvider? clock = null)
    {
        _document = new VersionedDocumentFile(
            documentPath,
            mutexNamePrefix: "WOpenUsage.AppearanceSettingsStore",
            clock ?? TimeProvider.System,
            lockTimeoutMessage: "Timed out while waiting for the appearance settings lock.");
    }

    public string DocumentPath => _document.DocumentPath;

    public Task<AppearanceSettingsLoadResult> LoadAsync(
        CancellationToken cancellationToken = default) =>
        _document.RunLockedAsync(LoadCore, cancellationToken);

    public Task<AppearanceSettingsSaveResult> SaveAsync(
        AppearanceSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return _document.RunLockedAsync(() => SaveCore(settings), cancellationToken);
    }

    private AppearanceSettingsLoadResult LoadCore()
    {
        if (!_document.Exists)
        {
            return AppearanceSettingsLoadResult.Defaults.Instance;
        }

        try
        {
            byte[] bytes = _document.ReadBoundedBytes(MaxDocumentBytes);
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
        _document.WriteAtomically(bytes, MaxDocumentBytes);
        return AppearanceSettingsSaveResult.Saved.Instance;
    }

    private int? ProbeExistingSchemaVersion()
    {
        if (!_document.Exists)
        {
            return null;
        }

        try
        {
            byte[] bytes = _document.ReadBoundedBytes(MaxDocumentBytes);
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

    private static JsonDocument Parse(byte[] bytes) => JsonDocument.Parse(
        VersionedDocumentFile.RemoveUtf8Preamble(bytes),
        new JsonDocumentOptions { MaxDepth = MaxJsonDepth });

    private AppearanceSettingsLoadResult.Corrupt QuarantineCorrupt() =>
        new(_document.QuarantineCorrupt());

    private static bool IsInvalidDocument(Exception exception) => exception is
        JsonException
        or AppearanceDocumentFormatException
        or VersionedDocumentFormatException
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

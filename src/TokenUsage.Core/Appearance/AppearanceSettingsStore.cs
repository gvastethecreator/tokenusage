using System.Text.Json;
using TokenUsage.Core.Storage;

namespace TokenUsage.Core.Appearance;

public sealed class AppearanceSettingsStore
{
    public const int SchemaVersion = 5;
    public const string DefaultFileName = "appearance.v1.json";
    public const int MaxDocumentBytes = 16 * 1024;
    public const int MaxJsonDepth = 8;

    private readonly VersionedDocumentFile _document;

    public AppearanceSettingsStore(string documentPath, TimeProvider? clock = null)
    {
        _document = new VersionedDocumentFile(
            documentPath,
            mutexNamePrefix: "TokenUsage.AppearanceSettingsStore",
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

            // Only schema 5 was ever shipped; an older document is set aside like any other
            // unreadable file, byte for byte, and the defaults apply.
            if (version != SchemaVersion)
            {
                throw new AppearanceDocumentFormatException();
            }

            return new AppearanceSettingsLoadResult.Loaded(ReadDocument(parsed.RootElement));
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
                SchemaVersion => ReadDocument(parsed.RootElement),
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

    private static AppearanceSettings ReadDocument(JsonElement root) => new(
        ReadRequiredEnum<AppThemeMode>(root, "theme"),
        ReadRequiredEnum<AppDensityMode>(root, "density"),
        ReadRequiredBoolean(root, "increaseTransparency"),
        ReadRequiredEnum<UsageDisplayMode>(root, "usageDisplay"),
        ReadRequiredEnum<ResetTimeDisplayMode>(root, "resetTimeDisplay"),
        ReadRequiredEnum<DashboardVisualizationMode>(root, "dashboardVisualization"),
        ReadTrayPopover(root),
        ReadRequiredEnum<ReportChartStyle>(root, "reportChartStyle"),
        ReadRequiredEnum<ReportChartGrouping>(root, "reportChartGrouping"),
        ReadOptionalBoolean(root, "markReportBestValues", true));

    private static TrayPopoverSettings ReadTrayPopover(JsonElement root)
    {
        EnsureObject(root);
        if (!root.TryGetProperty("trayPopover", out JsonElement popover))
        {
            throw new AppearanceDocumentFormatException();
        }

        EnsureObject(popover);
        if (!popover.TryGetProperty("providerCount", out JsonElement count)
            || !count.TryGetInt32(out int providerCount))
        {
            throw new AppearanceDocumentFormatException();
        }

        try
        {
            return new TrayPopoverSettings(
                ReadRequiredEnum<TrayPopoverMetric>(popover, "primaryMetric"),
                ReadRequiredEnum<TrayPopoverMetric>(popover, "secondaryMetric"),
                providerCount,
                ReadRequiredBoolean(popover, "showProviderName"),
                isEnabled: ReadRequiredBoolean(popover, "isEnabled"));
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new AppearanceDocumentFormatException();
        }
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
            writer.WriteString("reportChartStyle", ToStorageValue(settings.ReportChartStyle));
            writer.WriteString("reportChartGrouping", ToStorageValue(settings.ReportChartGrouping));
            writer.WriteBoolean("markReportBestValues", settings.MarkReportBestValues);
            writer.WriteString(
                "dashboardVisualization",
                ToStorageValue(settings.DashboardVisualization));
            writer.WriteStartObject("trayPopover");
            writer.WriteString(
                "primaryMetric",
                ToStorageValue(settings.TrayPopover.PrimaryMetric));
            writer.WriteString(
                "secondaryMetric",
                ToStorageValue(settings.TrayPopover.SecondaryMetric));
            writer.WriteNumber("providerCount", settings.TrayPopover.ProviderCount);
            writer.WriteBoolean("showProviderName", settings.TrayPopover.ShowProviderName);
            writer.WriteBoolean("isEnabled", settings.TrayPopover.IsEnabled);
            writer.WriteEndObject();
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
        public Loaded(AppearanceSettings settings)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public AppearanceSettings Settings { get; }
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

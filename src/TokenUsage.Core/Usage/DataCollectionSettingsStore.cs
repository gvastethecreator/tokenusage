using System.Text.Json;
using System.Text.Json.Serialization;
using TokenUsage.Core.Storage;

namespace TokenUsage.Core.Usage;

public sealed record DataCollectionSettings(
    bool BackgroundCollection = true,
    int OpenRefreshMinutes = 0);

/// <summary>
/// Persists how TokenUsage collects local usage data: whether provider hooks
/// refresh it in the background without the app open, and how often the app
/// refreshes while it stays open. Zero minutes means manual refresh only.
/// </summary>
public sealed class DataCollectionSettingsStore
{
    public const int SchemaVersion = 1;
    public const string DefaultFileName = "datacollection.v1.json";
    public const int MaxDocumentBytes = 4 * 1024;
    public static readonly int[] SupportedOpenRefreshMinutes = [0, 15, 30, 60];

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Disallow,
    };

    private readonly VersionedDocumentFile _document;

    public DataCollectionSettingsStore(string documentPath, TimeProvider? clock = null)
    {
        _document = new VersionedDocumentFile(
            documentPath,
            mutexNamePrefix: "TokenUsage.DataCollectionSettingsStore",
            clock ?? TimeProvider.System,
            lockTimeoutMessage: "Timed out while waiting for the data collection settings lock.");
    }

    public string DocumentPath => _document.DocumentPath;

    public Task<DataCollectionSettings> LoadAsync(
        CancellationToken cancellationToken = default) =>
        _document.RunLockedAsync(LoadCore, cancellationToken);

    public Task<bool> SaveAsync(
        DataCollectionSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return _document.RunLockedAsync(() => SaveCore(settings), cancellationToken);
    }

    private DataCollectionSettings LoadCore()
    {
        if (!_document.Exists)
        {
            return new DataCollectionSettings();
        }

        try
        {
            byte[] bytes = _document.ReadBoundedBytes(MaxDocumentBytes);
            using JsonDocument parsed = JsonDocument.Parse(bytes);
            int version = parsed.RootElement.TryGetProperty(
                    "schemaVersion",
                    out JsonElement versionElement)
                && versionElement.TryGetInt32(out int parsedVersion)
                ? parsedVersion
                : 0;
            if (version > SchemaVersion)
            {
                return new DataCollectionSettings();
            }

            bool background = parsed.RootElement.TryGetProperty(
                    "backgroundCollection",
                    out JsonElement backgroundElement)
                && backgroundElement.ValueKind == JsonValueKind.True;
            int minutes = parsed.RootElement.TryGetProperty(
                    "openRefreshMinutes",
                    out JsonElement minutesElement)
                && minutesElement.TryGetInt32(out int parsedMinutes)
                && SupportedOpenRefreshMinutes.Contains(parsedMinutes)
                ? parsedMinutes
                : 0;
            return new DataCollectionSettings(background, minutes);
        }
        catch (Exception exception) when (exception is JsonException
                                           or FormatException
                                           or IOException
                                           or ArgumentOutOfRangeException)
        {
            _document.QuarantineCorrupt();
            return new DataCollectionSettings();
        }
    }

    private bool SaveCore(DataCollectionSettings settings)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(
            new Document(SchemaVersion, settings.BackgroundCollection, settings.OpenRefreshMinutes),
            SerializerOptions);
        _document.WriteAtomically(bytes, MaxDocumentBytes);
        return true;
    }

    private sealed record Document(
        int SchemaVersion,
        bool BackgroundCollection,
        int OpenRefreshMinutes);
}

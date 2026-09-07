using System.Text.Json;
using TokenUsage.Core.Storage;

namespace TokenUsage.Core.Updates;

public sealed record UpdateSettings(
    bool AutomaticUpdatesEnabled = false,
    DateTimeOffset? LastCheckUtc = null);

public sealed class UpdateSettingsStore
{
    public const int SchemaVersion = 1;
    public const string DefaultFileName = "updates.v1.json";
    private const int MaxDocumentBytes = 4 * 1024;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly VersionedDocumentFile _document;

    public UpdateSettingsStore(string path)
    {
        _document = new VersionedDocumentFile(
            path,
            "TokenUsage.UpdateSettingsStore",
            TimeProvider.System,
            "Timed out while waiting for the update settings lock.");
    }

    public Task<UpdateSettings> LoadAsync(CancellationToken cancellationToken = default) =>
        _document.RunLockedAsync(LoadCore, cancellationToken);

    public Task SaveAsync(UpdateSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return _document.RunLockedAsync(() =>
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(
                new Document(SchemaVersion, settings.AutomaticUpdatesEnabled, settings.LastCheckUtc?.ToUniversalTime()),
                SerializerOptions);
            _document.WriteAtomically(bytes, MaxDocumentBytes);
            return true;
        }, cancellationToken);
    }

    private UpdateSettings LoadCore()
    {
        if (!_document.Exists)
        {
            return new UpdateSettings();
        }

        try
        {
            byte[] bytes = _document.ReadBoundedBytes(MaxDocumentBytes);
            Document? document = JsonSerializer.Deserialize<Document>(
                VersionedDocumentFile.RemoveUtf8Preamble(bytes).Span,
                SerializerOptions);
            if (document?.SchemaVersion != SchemaVersion)
            {
                return new UpdateSettings();
            }

            return new UpdateSettings(document.AutomaticUpdatesEnabled, document.LastCheckUtc?.ToUniversalTime());
        }
        catch (Exception exception) when (exception is JsonException or VersionedDocumentFormatException)
        {
            _document.QuarantineCorrupt();
            return new UpdateSettings();
        }
    }

    private sealed record Document(
        int SchemaVersion,
        bool AutomaticUpdatesEnabled,
        DateTimeOffset? LastCheckUtc);
}

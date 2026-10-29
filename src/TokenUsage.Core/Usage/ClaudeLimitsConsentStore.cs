using System.Text.Json;
using System.Text.Json.Serialization;
using TokenUsage.Core.Storage;

namespace TokenUsage.Core.Usage;

public enum ClaudeLimitsConsentStatus
{
    NotEnabled,
    Enabled,
    Disabled,
}

public sealed class ClaudeLimitsConsentStore
{
    public const string DefaultFileName = "claude-limits-consent.v1.json";
    private const int MaximumDocumentBytes = 4096;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter<ClaudeLimitsConsentStatus>(allowIntegerValues: false) },
    };
    private readonly VersionedDocumentFile _document;

    public ClaudeLimitsConsentStore(string documentPath)
    {
        _document = new VersionedDocumentFile(documentPath,
            "TokenUsage.ClaudeLimitsConsent", TimeProvider.System);
    }

    public string DocumentPath => _document.DocumentPath;

    public static string DefaultPath(string dataDirectory) =>
        Path.Combine(dataDirectory, DefaultFileName);

    public Task<ClaudeLimitsConsentStatus> ReadOrMigrateAsync(bool hasOwnDirectStatusLine,
        CancellationToken cancellationToken = default) =>
        _document.RunLockedAsync(() => ReadOrMigrate(hasOwnDirectStatusLine), cancellationToken);

    public Task<bool> RunIfEnabledAsync(bool hasOwnDirectStatusLine, Action operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return _document.RunLockedAsync(() =>
        {
            if (ReadOrMigrate(hasOwnDirectStatusLine) != ClaudeLimitsConsentStatus.Enabled)
                return false;
            operation();
            return true;
        }, cancellationToken);
    }

    private ClaudeLimitsConsentStatus ReadOrMigrate(bool hasOwnDirectStatusLine)
    {
        if (!_document.ExistsStrict)
            return Write(hasOwnDirectStatusLine
                ? ClaudeLimitsConsentStatus.Enabled : ClaudeLimitsConsentStatus.NotEnabled);

        byte[] bytes;
        try
        {
            bytes = _document.ReadBoundedBytes(MaximumDocumentBytes);
        }
        catch (VersionedDocumentFormatException exception)
        {
            throw new InvalidDataException("The Claude limits consent could not be read.", exception);
        }
        ConsentDocument? document = JsonSerializer.Deserialize<ConsentDocument>(bytes, SerializerOptions);
        if (document is null || document.SchemaVersion != 1 || !Enum.IsDefined(document.Status))
            throw new InvalidDataException("The Claude limits consent could not be read.");
        return document.Status;
    }

    public Task<ClaudeLimitsConsentStatus> EnableAsync(CancellationToken cancellationToken = default) =>
        _document.RunLockedAsync(() => Write(ClaudeLimitsConsentStatus.Enabled), cancellationToken);

    public Task<ClaudeLimitsConsentStatus> DisableAsync(CancellationToken cancellationToken = default) =>
        _document.RunLockedAsync(() => Write(ClaudeLimitsConsentStatus.Disabled), cancellationToken);

    private ClaudeLimitsConsentStatus Write(ClaudeLimitsConsentStatus status)
    {
        _document.WriteAtomically(JsonSerializer.SerializeToUtf8Bytes(new ConsentDocument(1, status),
            SerializerOptions), MaximumDocumentBytes);
        return status;
    }

    private sealed record ConsentDocument(int SchemaVersion, ClaudeLimitsConsentStatus Status);
}

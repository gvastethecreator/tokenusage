using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using TokenUsage.Core.Storage;

namespace TokenUsage.Core.Usage;

public sealed class AttributionConsentStore : IAttributionConsentSource
{
    public const int SchemaVersion = 1;
    public const string DefaultFileName = "attribution-consent.v1.json";
    public const int MaxDocumentBytes = 8 * 1024;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Disallow,
    };

    private readonly VersionedDocumentFile _document;
    private readonly TimeProvider _clock;

    public AttributionConsentStore(string documentPath, TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
        _document = new VersionedDocumentFile(
            documentPath,
            mutexNamePrefix: "TokenUsage.AttributionConsentStore",
            _clock,
            lockTimeoutMessage: "Timed out while waiting for the attribution consent lock.");
    }

    public string DocumentPath => _document.DocumentPath;

    public Task<AttributionConsent> LoadAsync(
        AttributionCapability capability,
        CancellationToken cancellationToken = default) =>
        _document.RunLockedAsync(() => ReadCapability(LoadDocument(), capability), cancellationToken);

    public Task<AttributionConsent> EnableAsync(
        AttributionCapability capability,
        CancellationToken cancellationToken = default) =>
        _document.RunLockedAsync(() => Mutate(capability, current =>
        {
            if (current.State == AttributionConsentState.PurgePending)
            {
                throw new InvalidOperationException(
                    "Attribution cannot be enabled until the pending purge finishes.");
            }

            if (current.State == AttributionConsentState.Enabled)
            {
                return current;
            }

            DateTimeOffset now = _clock.GetUtcNow().ToUniversalTime();
            return current with
            {
                State = AttributionConsentState.Enabled,
                Epoch = checked(current.Epoch + 1),
                UpdatedAtUtc = now,
                EnabledAtUtc = now,
            };
        }), cancellationToken);

    public Task<AttributionConsent> RevokeAsync(
        AttributionCapability capability,
        CancellationToken cancellationToken = default) =>
        _document.RunLockedAsync(() => Mutate(capability, current =>
        {
            if (current.State is AttributionConsentState.Disabled
                or AttributionConsentState.DisabledAfterPurge
                or AttributionConsentState.PurgePending)
            {
                return current.State == AttributionConsentState.PurgePending
                    ? current
                    : current with
                    {
                        State = AttributionConsentState.Disabled,
                        UpdatedAtUtc = _clock.GetUtcNow().ToUniversalTime(),
                        EnabledAtUtc = null,
                    };
            }

            return current with
            {
                State = AttributionConsentState.PurgePending,
                Epoch = checked(current.Epoch + 1),
                UpdatedAtUtc = _clock.GetUtcNow().ToUniversalTime(),
                EnabledAtUtc = null,
            };
        }), cancellationToken);

    public Task<AttributionConsent> CompletePurgeAsync(
        AttributionCapability capability,
        CancellationToken cancellationToken = default) =>
        _document.RunLockedAsync(() => Mutate(capability, current =>
        {
            if (current.State is AttributionConsentState.Disabled
                or AttributionConsentState.DisabledAfterPurge)
            {
                return current with
                {
                    State = AttributionConsentState.DisabledAfterPurge,
                    UpdatedAtUtc = _clock.GetUtcNow().ToUniversalTime(),
                    EnabledAtUtc = null,
                };
            }

            if (current.State != AttributionConsentState.PurgePending)
            {
                throw new InvalidOperationException(
                    "Attribution purge can finish only after revocation.");
            }

            return current with
            {
                State = AttributionConsentState.DisabledAfterPurge,
                UpdatedAtUtc = _clock.GetUtcNow().ToUniversalTime(),
                EnabledAtUtc = null,
            };
        }), cancellationToken);

    private Document LoadDocument()
    {
        if (!_document.Exists)
        {
            return new Document(SchemaVersion, []);
        }

        try
        {
            byte[] bytes = _document.ReadBoundedBytes(MaxDocumentBytes);
            using JsonDocument parsed = JsonDocument.Parse(bytes);
            int version = parsed.RootElement.TryGetProperty("schemaVersion", out JsonElement versionElement)
                && versionElement.TryGetInt32(out int parsedVersion)
                ? parsedVersion
                : 0;
            if (version > SchemaVersion)
            {
                return new Document(SchemaVersion, []);
            }

            var capabilities = new Dictionary<string, CapabilityDocument>(StringComparer.Ordinal);
            if (parsed.RootElement.TryGetProperty("capabilities", out JsonElement capabilitiesElement)
                && capabilitiesElement.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in capabilitiesElement.EnumerateObject())
                {
                    if (!TryReadCapability(property.Value, out CapabilityDocument? row) || row is null)
                    {
                        continue;
                    }

                    capabilities[property.Name] = row;
                }
            }

            return new Document(SchemaVersion, capabilities);
        }
        catch (Exception exception) when (exception is JsonException
                                           or FormatException
                                           or IOException
                                           or ArgumentOutOfRangeException)
        {
            _document.QuarantineCorrupt();
            return new Document(SchemaVersion, []);
        }
    }

    private AttributionConsent Mutate(
        AttributionCapability capability,
        Func<AttributionConsent, AttributionConsent> update)
    {
        Document document = LoadDocument();
        AttributionConsent current = ReadCapability(document, capability);
        AttributionConsent next = update(current);
        document.Capabilities[capability.Value] = new CapabilityDocument(
            ToWireState(next.State),
            next.Epoch,
            next.UpdatedAtUtc.ToString("O", CultureInfo.InvariantCulture),
            next.EnabledAtUtc?.ToString("O", CultureInfo.InvariantCulture));
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(document, SerializerOptions);
        _document.WriteAtomically(bytes, MaxDocumentBytes);
        return next;
    }

    private static AttributionConsent ReadCapability(Document document, AttributionCapability capability)
    {
        if (!document.Capabilities.TryGetValue(capability.Value, out CapabilityDocument? row))
        {
            return new AttributionConsent(
                capability,
                AttributionConsentState.Disabled,
                Epoch: 0,
                UpdatedAtUtc: DateTimeOffset.UnixEpoch);
        }

        return new AttributionConsent(
            capability,
            ParseState(row.State),
            row.Epoch,
            DateTimeOffset.TryParse(
                row.UpdatedAtUtc,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTimeOffset updated)
                ? updated.ToUniversalTime()
                : DateTimeOffset.UnixEpoch,
            DateTimeOffset.TryParse(
                row.EnabledAtUtc,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTimeOffset enabled)
                ? enabled.ToUniversalTime()
                : null);
    }

    private static bool TryReadCapability(JsonElement element, out CapabilityDocument? document)
    {
        document = null;
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty("state", out JsonElement stateElement)
            || stateElement.ValueKind != JsonValueKind.String
            || !element.TryGetProperty("epoch", out JsonElement epochElement)
            || !epochElement.TryGetInt64(out long epoch)
            || epoch < 0)
        {
            return false;
        }

        string? state = stateElement.GetString();
        if (state is not ("disabled" or "enabled" or "purge-pending" or "disabled-after-purge"))
        {
            return false;
        }

        string updated = element.TryGetProperty("updatedAtUtc", out JsonElement updatedElement)
            && updatedElement.ValueKind == JsonValueKind.String
            ? updatedElement.GetString() ?? string.Empty
            : string.Empty;
        string? enabled = element.TryGetProperty("enabledAtUtc", out JsonElement enabledElement)
            && enabledElement.ValueKind == JsonValueKind.String
            ? enabledElement.GetString()
            : null;
        document = new CapabilityDocument(state, epoch, updated, enabled);
        return true;
    }

    private static AttributionConsentState ParseState(string state) => state switch
    {
        "enabled" => AttributionConsentState.Enabled,
        "purge-pending" => AttributionConsentState.PurgePending,
        "disabled-after-purge" => AttributionConsentState.DisabledAfterPurge,
        _ => AttributionConsentState.Disabled,
    };

    private static string ToWireState(AttributionConsentState state) => state switch
    {
        AttributionConsentState.Enabled => "enabled",
        AttributionConsentState.PurgePending => "purge-pending",
        AttributionConsentState.DisabledAfterPurge => "disabled-after-purge",
        _ => "disabled",
    };

    private sealed class Document
    {
        public Document(int schemaVersion, Dictionary<string, CapabilityDocument> capabilities)
        {
            SchemaVersion = schemaVersion;
            Capabilities = capabilities;
        }

        public int SchemaVersion { get; }

        public Dictionary<string, CapabilityDocument> Capabilities { get; }
    }

    private sealed record CapabilityDocument(
        string State,
        long Epoch,
        string UpdatedAtUtc,
        string? EnabledAtUtc);
}

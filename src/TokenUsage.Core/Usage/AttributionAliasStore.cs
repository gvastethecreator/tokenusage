using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using TokenUsage.Core.Storage;

namespace TokenUsage.Core.Usage;

public sealed class AttributionAliasStore
{
    public const int SchemaVersion = 1;
    public const string DefaultFileName = "attribution-aliases.v1.json";
    public const int MaxDocumentBytes = 64 * 1024;
    public const int MaximumLabelLength = 64;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Disallow,
    };

    private readonly VersionedDocumentFile _document;
    private readonly TimeProvider _clock;

    public AttributionAliasStore(string documentPath, TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
        _document = new VersionedDocumentFile(
            documentPath,
            mutexNamePrefix: "TokenUsage.AttributionAliasStore",
            _clock,
            lockTimeoutMessage: "Timed out while waiting for the attribution alias lock.");
    }

    public string DocumentPath => _document.DocumentPath;

    public Task<string?> LoadAsync(
        OpaqueAttributionKey key,
        CancellationToken cancellationToken = default) =>
        _document.RunLockedAsync(() =>
        {
            Document document = LoadDocument();
            return document.Aliases.TryGetValue(key.Value, out AliasRow? row)
                ? row.Label
                : null;
        }, cancellationToken);

    public Task SetAsync(
        OpaqueAttributionKey key,
        string label,
        CancellationToken cancellationToken = default) =>
        _document.RunLockedAsync(() =>
        {
            string normalized = NormalizeLabel(label);
            Document document = LoadDocument();
            document.Aliases[key.Value] = new AliasRow(
                normalized,
                _clock.GetUtcNow().ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            Write(document);
            return true;
        }, cancellationToken);

    public Task RemoveAsync(
        OpaqueAttributionKey key,
        CancellationToken cancellationToken = default) =>
        _document.RunLockedAsync(() =>
        {
            Document document = LoadDocument();
            document.Aliases.Remove(key.Value);
            Write(document);
            return true;
        }, cancellationToken);

    public Task ClearAsync(CancellationToken cancellationToken = default) =>
        _document.RunLockedAsync(() =>
        {
            Write(new Document(SchemaVersion, []));
            return true;
        }, cancellationToken);

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

            var aliases = new Dictionary<string, AliasRow>(StringComparer.Ordinal);
            if (parsed.RootElement.TryGetProperty("aliases", out JsonElement aliasesElement)
                && aliasesElement.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in aliasesElement.EnumerateObject())
                {
                    if (!OpaqueAttributionKey.IsHexSha256(property.Name)
                        || property.Value.ValueKind != JsonValueKind.Object
                        || !property.Value.TryGetProperty("label", out JsonElement labelElement)
                        || labelElement.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    string label = labelElement.GetString() ?? string.Empty;
                    if (!TryAcceptLabel(label, out string accepted))
                    {
                        continue;
                    }

                    aliases[property.Name] = new AliasRow(accepted, string.Empty);
                }
            }

            return new Document(SchemaVersion, aliases);
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

    private void Write(Document document)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(document, SerializerOptions);
        _document.WriteAtomically(bytes, MaxDocumentBytes);
    }

    private static string NormalizeLabel(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        if (!TryAcceptLabel(label.Trim(), out string accepted))
        {
            throw new ArgumentException(
                "Project aliases must be short display names without path characters.",
                nameof(label));
        }

        return accepted;
    }

    private static bool TryAcceptLabel(string label, out string accepted)
    {
        accepted = string.Empty;
        if (label.Length is 0 or > MaximumLabelLength)
        {
            return false;
        }

        foreach (char character in label)
        {
            if (char.IsControl(character)
                || character is '/' or '\\' or ':' or '<' or '>' or '"' or '|' or '?' or '*')
            {
                return false;
            }
        }

        accepted = label;
        return true;
    }

    private sealed class Document
    {
        public Document(int schemaVersion, Dictionary<string, AliasRow> aliases)
        {
            SchemaVersion = schemaVersion;
            Aliases = aliases;
        }

        public int SchemaVersion { get; }

        public Dictionary<string, AliasRow> Aliases { get; }
    }

    private sealed record AliasRow(string Label, string UpdatedAtUtc);
}

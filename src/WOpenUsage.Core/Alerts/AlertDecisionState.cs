using System.Text.Json;
using WOpenUsage.Core.Providers;
using WOpenUsage.Core.Storage;

namespace WOpenUsage.Core.Alerts;

public sealed class AlertDecisionState
{
    private readonly HashSet<string> _notifiedKeys;

    public AlertDecisionState(IEnumerable<string>? notifiedConditionKeys = null)
    {
        _notifiedKeys = new HashSet<string>(
            notifiedConditionKeys ?? [],
            StringComparer.Ordinal);
    }

    public IReadOnlyCollection<string> NotifiedConditionKeys => _notifiedKeys;

    public bool HasNotified(AlertConditionKey key) =>
        _notifiedKeys.Contains(SerializeKey(key));

    public void MarkNotified(AlertConditionKey key) =>
        _notifiedKeys.Add(SerializeKey(key));

    public static string SerializeKey(AlertConditionKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        string metric = key.MetricId?.Value ?? "-";
        string reset = key.QuotaWindowResetsAtUtc?.ToUniversalTime().ToString("O") ?? "-";
        return string.Join('|', key.ProviderId.Value, metric, key.Kind.ToString(), reset);
    }
}

public sealed class AlertDecisionStore
{
    public const int SchemaVersion = 1;
    public const string DefaultFileName = "alert-decisions.v1.json";
    public const int MaxDocumentBytes = 64 * 1024;

    private readonly VersionedDocumentFile _document;

    public AlertDecisionStore(string documentPath, TimeProvider? clock = null)
    {
        _document = new VersionedDocumentFile(
            documentPath,
            mutexNamePrefix: "WOpenUsage.AlertDecisionStore",
            clock ?? TimeProvider.System,
            lockTimeoutMessage: "Timed out while waiting for the alert decision lock.");
    }

    public string DocumentPath => _document.DocumentPath;

    public Task<AlertDecisionState> LoadAsync(CancellationToken cancellationToken = default) =>
        _document.RunLockedAsync(LoadCore, cancellationToken);

    public Task SaveAsync(AlertDecisionState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        return _document.RunLockedAsync(() =>
        {
            SaveCore(state);
            return true;
        }, cancellationToken);
    }

    private AlertDecisionState LoadCore()
    {
        if (!_document.Exists)
        {
            return new AlertDecisionState();
        }

        try
        {
            byte[] bytes = _document.ReadBoundedBytes(MaxDocumentBytes);
            using JsonDocument parsed = JsonDocument.Parse(
                VersionedDocumentFile.RemoveUtf8Preamble(bytes),
                new JsonDocumentOptions { MaxDepth = 8 });
            if (parsed.RootElement.ValueKind != JsonValueKind.Object
                || !parsed.RootElement.TryGetProperty("schemaVersion", out JsonElement versionElement)
                || !versionElement.TryGetInt32(out int version)
                || version != SchemaVersion
                || !parsed.RootElement.TryGetProperty("notifiedKeys", out JsonElement keysElement)
                || keysElement.ValueKind != JsonValueKind.Array)
            {
                _ = _document.QuarantineCorrupt();
                return new AlertDecisionState();
            }

            var keys = new List<string>();
            foreach (JsonElement item in keysElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(item.GetString()))
                {
                    keys.Add(item.GetString()!);
                }
            }

            return new AlertDecisionState(keys);
        }
        catch (Exception exception) when (exception is JsonException
            or VersionedDocumentFormatException
            or InvalidOperationException)
        {
            if (_document.Exists)
            {
                _ = _document.QuarantineCorrupt();
            }

            return new AlertDecisionState();
        }
    }

    private void SaveCore(AlertDecisionState state)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", SchemaVersion);
            writer.WritePropertyName("notifiedKeys");
            writer.WriteStartArray();
            foreach (string key in state.NotifiedConditionKeys.OrderBy(value => value, StringComparer.Ordinal))
            {
                writer.WriteStringValue(key);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        _document.WriteAtomically(stream.ToArray(), MaxDocumentBytes);
    }
}

public sealed class AlertSettingsStore
{
    public const int SchemaVersion = 1;
    public const string DefaultFileName = "alert-settings.v1.json";
    public const int MaxDocumentBytes = 16 * 1024;

    private readonly VersionedDocumentFile _document;

    public AlertSettingsStore(string documentPath, TimeProvider? clock = null)
    {
        _document = new VersionedDocumentFile(
            documentPath,
            mutexNamePrefix: "WOpenUsage.AlertSettingsStore",
            clock ?? TimeProvider.System,
            lockTimeoutMessage: "Timed out while waiting for the alert settings lock.");
    }

    public string DocumentPath => _document.DocumentPath;

    public Task<AlertSettings> LoadAsync(CancellationToken cancellationToken = default) =>
        _document.RunLockedAsync(LoadCore, cancellationToken);

    public Task SaveAsync(AlertSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return _document.RunLockedAsync(() =>
        {
            SaveCore(settings);
            return true;
        }, cancellationToken);
    }

    private AlertSettings LoadCore()
    {
        if (!_document.Exists)
        {
            return AlertSettings.Default;
        }

        try
        {
            byte[] bytes = _document.ReadBoundedBytes(MaxDocumentBytes);
            using JsonDocument parsed = JsonDocument.Parse(
                VersionedDocumentFile.RemoveUtf8Preamble(bytes),
                new JsonDocumentOptions { MaxDepth = 8 });
            JsonElement root = parsed.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("schemaVersion", out JsonElement versionElement)
                || !versionElement.TryGetInt32(out int version)
                || version != SchemaVersion)
            {
                _ = _document.QuarantineCorrupt();
                return AlertSettings.Default;
            }

            return new AlertSettings(
                ReadBool(root, "enabled", AlertSettings.Default.Enabled),
                ReadInt(root, "quotaThresholdPercent", AlertSettings.Default.QuotaThresholdPercent),
                ReadBool(root, "quotaThresholdEnabled", AlertSettings.Default.QuotaThresholdEnabled),
                ReadBool(root, "exhaustionForecastEnabled", AlertSettings.Default.ExhaustionForecastEnabled),
                ReadBool(root, "staleDataEnabled", AlertSettings.Default.StaleDataEnabled),
                ReadBool(root, "credentialFailureEnabled", AlertSettings.Default.CredentialFailureEnabled));
        }
        catch (Exception exception) when (exception is JsonException
            or VersionedDocumentFormatException
            or ArgumentException
            or InvalidOperationException)
        {
            if (_document.Exists)
            {
                _ = _document.QuarantineCorrupt();
            }

            return AlertSettings.Default;
        }
    }

    private void SaveCore(AlertSettings settings)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", SchemaVersion);
            writer.WriteBoolean("enabled", settings.Enabled);
            writer.WriteNumber("quotaThresholdPercent", settings.QuotaThresholdPercent);
            writer.WriteBoolean("quotaThresholdEnabled", settings.QuotaThresholdEnabled);
            writer.WriteBoolean("exhaustionForecastEnabled", settings.ExhaustionForecastEnabled);
            writer.WriteBoolean("staleDataEnabled", settings.StaleDataEnabled);
            writer.WriteBoolean("credentialFailureEnabled", settings.CredentialFailureEnabled);
            writer.WriteEndObject();
        }

        _document.WriteAtomically(stream.ToArray(), MaxDocumentBytes);
    }

    private static bool ReadBool(JsonElement root, string name, bool defaultValue) =>
        root.TryGetProperty(name, out JsonElement value)
        && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : defaultValue;

    private static int ReadInt(JsonElement root, string name, int defaultValue) =>
        root.TryGetProperty(name, out JsonElement value) && value.TryGetInt32(out int number)
            ? number
            : defaultValue;
}

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TokenUsage.Runtime.Windows.Grok;

public enum GrokHookInstallationStatus
{
    NotInstalled,
    Installed,
}

/// <summary>
/// Registers TokenUsage in Grok's documented global hook folder. The Stop hook
/// is an async trigger only: TokenUsage discards the event payload and
/// refreshes its own local usage stores. No prompt, response, or transcript
/// data is read or stored.
/// </summary>
public sealed class GrokHookInstaller
{
    public const string HookFileName = "tokenusage.json";
    public const int HookTimeoutSeconds = 30;

    private readonly string _hookFilePath;
    private readonly string _grokHome;

    public GrokHookInstaller(
        string? homeDirectory = null,
        string? grokHomeOverride = null)
    {
        _grokHome = ResolveGrokHome(
            homeDirectory,
            grokHomeOverride ?? Environment.GetEnvironmentVariable("GROK_HOME"));
        _hookFilePath = Path.Combine(_grokHome, "hooks", HookFileName);
    }

    public string HookFilePath => _hookFilePath;

    /// <summary>True when a Grok installation exists for this user.</summary>
    public bool IsProviderDetected => Directory.Exists(_grokHome);

    public GrokHookInstallationStatus GetStatus() =>
        TryReadDocument(out JsonObject? document)
        && document is not null
        && FindRegistration(document) is not null
            ? GrokHookInstallationStatus.Installed
            : GrokHookInstallationStatus.NotInstalled;

    public void Install()
    {
        JsonObject document = ReadDocumentForUpdate();
        JsonObject hooks = GetOrCreateObject(document, "hooks");
        JsonArray stop = GetOrCreateArray(hooks, "Stop");
        if (FindRegistration(document) is null)
        {
            stop.Add(new JsonObject
            {
                ["hooks"] = new JsonArray(CreateRegistration()),
            });
        }

        WriteAtomically(Serialize(document));
    }

    public void Uninstall()
    {
        if (!File.Exists(_hookFilePath))
        {
            return;
        }

        JsonObject document = ReadDocumentForUpdate();
        bool changed = false;
        while (FindRegistration(document) is { } registration)
        {
            if (registration.Parent is JsonArray entries)
            {
                entries.Remove(registration);
                changed = true;
                if (entries.Count == 0
                    && entries.Parent is JsonObject group
                    && group.Parent is JsonArray stop)
                {
                    stop.Remove(group);
                }
            }
            else
            {
                break;
            }
        }

        if (document["hooks"] is JsonObject hooks)
        {
            if (hooks["Stop"] is JsonArray stopEvents && stopEvents.Count == 0)
            {
                hooks.Remove("Stop");
                changed = true;
            }

            if (hooks.Count == 0)
            {
                document.Remove("hooks");
            }
        }

        if (!changed)
        {
            return;
        }

        if (document.Count == 0
            || (document.Count == 1
                && document.ContainsKey("version")
                && document["version"]?.GetValue<int>() == 1))
        {
            File.Delete(_hookFilePath);
            return;
        }

        WriteAtomically(Serialize(document));
    }

    private static JsonObject CreateRegistration() => new()
    {
        ["type"] = "command",
        ["command"] = HookTriggerCommand.DetachedRefresh,
        ["timeout"] = HookTimeoutSeconds,
    };

    private static JsonNode? FindRegistration(JsonObject document)
    {
        if (document["hooks"] is not JsonObject hooks
            || hooks["Stop"] is not JsonArray stop)
        {
            return null;
        }

        foreach (JsonNode? group in stop)
        {
            if (group is not JsonObject groupObject
                || groupObject["hooks"] is not JsonArray entries)
            {
                continue;
            }

            foreach (JsonNode? entry in entries)
            {
                if (entry is not JsonObject entryObject
                    || entryObject["command"] is not JsonValue command)
                {
                    continue;
                }

                if (command.TryGetValue(out string? configuredCommand)
                    && configuredCommand.Contains("tokenusage", StringComparison.OrdinalIgnoreCase)
                    && configuredCommand.Contains("'hook','stop'", StringComparison.Ordinal))
                {
                    return entry;
                }
            }
        }

        return null;
    }

    private static string ResolveGrokHome(string? homeDirectory, string? configuredHome)
    {
        string home = homeDirectory
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string defaultHome = Path.Combine(Path.GetFullPath(home), ".grok");
        if (string.IsNullOrWhiteSpace(configuredHome))
        {
            return defaultHome;
        }

        string raw = configuredHome.Trim();
        if (raw == "~")
        {
            raw = Path.GetFullPath(home);
        }
        else if (raw.StartsWith($"~{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                 || raw.StartsWith($"~{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
        {
            raw = Path.Combine(Path.GetFullPath(home), raw[2..]);
        }

        try
        {
            return Path.GetFullPath(raw);
        }
        catch (Exception exception) when (exception is ArgumentException
                                           or NotSupportedException
                                           or PathTooLongException)
        {
            return defaultHome;
        }
    }

    private JsonObject ReadDocumentForUpdate()
    {
        if (!File.Exists(_hookFilePath))
        {
            return new JsonObject();
        }

        string json = File.ReadAllText(_hookFilePath, Encoding.UTF8);
        return JsonNode.Parse(json) as JsonObject
            ?? throw new InvalidDataException("The Grok hook file must contain a JSON object.");
    }

    private bool TryReadDocument(out JsonObject? document)
    {
        document = null;
        try
        {
            document = ReadDocumentForUpdate();
            return true;
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException
                                           or JsonException
                                           or InvalidDataException)
        {
            return false;
        }
    }

    private static string Serialize(JsonObject document) =>
        document.ToJsonString(new JsonSerializerOptions { WriteIndented = true })
        + Environment.NewLine;

    private static JsonObject GetOrCreateObject(JsonObject owner, string name)
    {
        if (owner[name] is JsonObject existing)
        {
            return existing;
        }

        if (owner[name] is not null)
        {
            throw new InvalidDataException($"The Grok hook file '{name}' must be an object.");
        }

        var created = new JsonObject();
        owner[name] = created;
        return created;
    }

    private static JsonArray GetOrCreateArray(JsonObject owner, string name)
    {
        if (owner[name] is JsonArray existing)
        {
            return existing;
        }

        if (owner[name] is not null)
        {
            throw new InvalidDataException($"The Grok hook file '{name}' must be an array.");
        }

        var created = new JsonArray();
        owner[name] = created;
        return created;
    }

    private void WriteAtomically(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_hookFilePath)!);
        string temporaryPath = _hookFilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, content, new UTF8Encoding(false));
            File.Move(temporaryPath, _hookFilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}

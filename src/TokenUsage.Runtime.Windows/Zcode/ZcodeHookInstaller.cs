using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TokenUsage.Providers.Zcode;

namespace TokenUsage.Runtime.Windows.Zcode;

public enum ZcodeHookInstallationStatus
{
    NotInstalled,
    Installed,
    Incomplete,
}

/// <summary>
/// Registers TokenUsage in ZCode's documented user hook configuration. The
/// Stop hook is an async command trigger only: TokenUsage discards the event
/// payload and refreshes its own local usage stores. No prompt, response, or
/// transcript data is read or stored.
/// </summary>
public sealed class ZcodeHookInstaller
{
    public const string HookCommand = "tokenusage";
    public const int HookTimeoutMilliseconds = 60_000;

    private readonly string _configPath;
    private readonly string _zcodeHome;

    public ZcodeHookInstaller(
        string? homeDirectory = null,
        string? zcodeHomeOverride = null)
    {
        _zcodeHome = ZcodeUsagePaths.ResolveConfiguredHome(
            homeDirectory,
            zcodeHomeOverride ?? Environment.GetEnvironmentVariable("ZCODE_HOME"));
        _configPath = Path.Combine(_zcodeHome, "cli", "config.json");
    }

    public string ConfigPath => _configPath;

    /// <summary>True when a ZCode installation exists for this user.</summary>
    public bool IsProviderDetected => Directory.Exists(_zcodeHome);

    public ZcodeHookInstallationStatus GetStatus()
    {
        if (!TryReadDocument(out JsonObject? document) || document is null)
        {
            return ZcodeHookInstallationStatus.NotInstalled;
        }

        if (FindRegistration(document!) is null)
        {
            return ZcodeHookInstallationStatus.NotInstalled;
        }

        return document!["hooks"] is JsonObject hooks
            && hooks["enabled"] is JsonValue enabled
            && enabled.TryGetValue(out bool enabledValue)
            && enabledValue
            ? ZcodeHookInstallationStatus.Installed
            : ZcodeHookInstallationStatus.Incomplete;
    }

    public void Install()
    {
        JsonObject document = ReadDocumentForUpdate();
        JsonObject hooks = GetOrCreateObject(document, "hooks");
        hooks["enabled"] = true;
        JsonObject events = GetOrCreateObject(hooks, "events");
        JsonArray stop = GetOrCreateArray(events, "Stop");
        if (FindRegistration(document) is null)
        {
            stop.Add(new JsonObject
            {
                ["matcher"] = "",
                ["hooks"] = new JsonArray(CreateRegistration()),
            });
        }

        WriteAtomically(Serialize(document));
    }

    public void Uninstall()
    {
        if (!File.Exists(_configPath))
        {
            return;
        }

        JsonObject document = ReadDocumentForUpdate();
        bool changed = false;
        while (FindRegistration(document) is { } registration)
        {
            if (registration.Parent is JsonArray entries
                && entries.Parent is JsonObject group)
            {
                entries.Remove(registration);
                changed = true;
                if (entries.Count == 0
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

        if (document["hooks"] is JsonObject hooks
            && hooks["events"] is JsonObject events
            && events["Stop"] is JsonArray stopEvents
            && stopEvents.Count == 0)
        {
            events.Remove("Stop");
            changed = true;
        }

        if (changed)
        {
            WriteAtomically(Serialize(document));
        }
    }

    private static JsonObject CreateRegistration() => new()
    {
        ["type"] = "command",
        ["command"] = HookCommand,
        ["args"] = new JsonArray("zcode", "stop-hook"),
        ["async"] = true,
        ["enabled"] = true,
        ["timeoutMs"] = HookTimeoutMilliseconds,
    };

    private static JsonNode? FindRegistration(JsonObject document)
    {
        if (document["hooks"] is not JsonObject hooks
            || hooks["events"] is not JsonObject events
            || events["Stop"] is not JsonArray stop)
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
                    || entryObject["command"] is not JsonValue command
                    || !command.TryGetValue(out string? configuredCommand)
                    || !string.Equals(
                        configuredCommand,
                        HookCommand,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (entryObject["args"] is JsonArray args
                    && args.Count > 0
                    && args[0] is JsonValue firstArgument
                    && firstArgument.TryGetValue(out string? argument)
                    && string.Equals(argument, "zcode", StringComparison.Ordinal))
                {
                    return entry;
                }
            }
        }

        return null;
    }

    private JsonObject ReadDocumentForUpdate()
    {
        if (!File.Exists(_configPath))
        {
            return new JsonObject();
        }

        string json = File.ReadAllText(_configPath, Encoding.UTF8);
        return JsonNode.Parse(json) as JsonObject
            ?? throw new InvalidDataException("ZCode config.json must contain a JSON object.");
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
            throw new InvalidDataException($"ZCode config.json '{name}' must be an object.");
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
            throw new InvalidDataException($"ZCode config.json '{name}' must be an array.");
        }

        var created = new JsonArray();
        owner[name] = created;
        return created;
    }

    private void WriteAtomically(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
        string temporaryPath = _configPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, content, new UTF8Encoding(false));
            File.Move(temporaryPath, _configPath, overwrite: true);
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

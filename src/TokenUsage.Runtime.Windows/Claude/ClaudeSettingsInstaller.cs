using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TokenUsage.Runtime.Windows.Claude;

public enum ClaudeIntegrationStatus
{
    NotInstalled,
    Installed,
}

/// <summary>
/// Registers TokenUsage in Claude Code's user settings file. Two independent entries exist:
/// <list type="bullet">
/// <item>a <c>Stop</c> hook that only triggers a TokenUsage refresh; the hook payload is
/// discarded, and</item>
/// <item>an opt-in status line wrapper. Claude Code pipes documented session JSON into the
/// status line command; TokenUsage keeps only the numeric <c>rate_limits</c> object and then
/// forwards the same input to the status line the user had before, so it keeps working.</item>
/// </list>
/// The rest of the settings file is preserved. The previous status line is kept in a
/// TokenUsage-owned sidecar next to the settings so uninstall can restore it.
/// See https://code.claude.com/docs/en/hooks and https://code.claude.com/docs/en/statusline.
/// </summary>
public sealed class ClaudeSettingsInstaller
{
    public const string SettingsFileName = "settings.json";
    public const string PreviousStatusLineFileName = "tokenusage-statusline.json";
    public const string StatusLineCommand = "tokenusage claude statusline";
    public const int HookTimeoutSeconds = 30;

    private readonly string _configDirectory;

    public ClaudeSettingsInstaller(
        string? homeDirectory = null,
        string? configDirectoryOverride = null)
    {
        _configDirectory = ResolveConfigDirectory(
            homeDirectory,
            configDirectoryOverride ?? Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR"));
        SettingsPath = Path.Combine(_configDirectory, SettingsFileName);
        PreviousStatusLinePath = Path.Combine(_configDirectory, PreviousStatusLineFileName);
    }

    public string SettingsPath { get; }

    public string PreviousStatusLinePath { get; }

    /// <summary>True when a Claude Code configuration folder exists for this user.</summary>
    public bool IsProviderDetected => Directory.Exists(_configDirectory);

    public ClaudeIntegrationStatus GetHookStatus() =>
        TryReadDocument(out JsonObject? document)
        && document is not null
        && FindHookRegistration(document) is not null
            ? ClaudeIntegrationStatus.Installed
            : ClaudeIntegrationStatus.NotInstalled;

    public ClaudeIntegrationStatus GetStatusLineStatus() =>
        TryReadDocument(out JsonObject? document)
        && document is not null
        && IsOwnStatusLine(document["statusLine"])
            ? ClaudeIntegrationStatus.Installed
            : ClaudeIntegrationStatus.NotInstalled;

    public void InstallHook()
    {
        JsonObject document = ReadDocumentForUpdate();
        if (FindHookRegistration(document) is not null)
        {
            return;
        }

        JsonObject hooks = GetOrCreateObject(document, "hooks");
        JsonArray stop = GetOrCreateArray(hooks, "Stop");
        stop.Add(new JsonObject
        {
            ["hooks"] = new JsonArray(new JsonObject
            {
                ["type"] = "command",
                ["command"] = HookTriggerCommand.DetachedRefresh,
                ["timeout"] = HookTimeoutSeconds,
            }),
        });
        WriteAtomically(SettingsPath, Serialize(document));
    }

    public void UninstallHook()
    {
        if (!File.Exists(SettingsPath))
        {
            return;
        }

        JsonObject document = ReadDocumentForUpdate();
        bool changed = false;
        while (FindHookRegistration(document) is { Parent: JsonArray entries } registration)
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

        if (!changed)
        {
            return;
        }

        if (document["hooks"] is JsonObject hooks)
        {
            if (hooks["Stop"] is JsonArray { Count: 0 })
            {
                hooks.Remove("Stop");
            }

            if (hooks.Count == 0)
            {
                document.Remove("hooks");
            }
        }

        WriteAtomically(SettingsPath, Serialize(document));
    }

    /// <summary>
    /// Points Claude Code's status line at TokenUsage. A status line the user already had is
    /// saved first, and its own options (padding, refresh interval) stay on the new entry.
    /// </summary>
    public void InstallStatusLine()
    {
        JsonObject document = ReadDocumentForUpdate();
        JsonNode? current = document["statusLine"];
        if (IsOwnStatusLine(current))
        {
            return;
        }

        JsonObject statusLine;
        if (current is JsonObject existing)
        {
            WriteAtomically(
                PreviousStatusLinePath,
                Serialize(new JsonObject { ["statusLine"] = existing.DeepClone() }));
            statusLine = (JsonObject)existing.DeepClone();
        }
        else
        {
            if (current is not null)
            {
                throw new InvalidDataException("The Claude Code 'statusLine' setting must be an object.");
            }

            DeletePreviousStatusLine();
            statusLine = new JsonObject();
        }

        statusLine["type"] = "command";
        statusLine["command"] = StatusLineCommand;
        document["statusLine"] = statusLine;
        WriteAtomically(SettingsPath, Serialize(document));
    }

    public void UninstallStatusLine()
    {
        if (!File.Exists(SettingsPath))
        {
            DeletePreviousStatusLine();
            return;
        }

        JsonObject document = ReadDocumentForUpdate();
        if (!IsOwnStatusLine(document["statusLine"]))
        {
            // The user replaced it; their current setting wins and the saved copy is stale.
            DeletePreviousStatusLine();
            return;
        }

        JsonObject? previous = ReadPreviousStatusLine();
        if (previous is null)
        {
            document.Remove("statusLine");
        }
        else
        {
            document["statusLine"] = previous;
        }

        WriteAtomically(SettingsPath, Serialize(document));
        DeletePreviousStatusLine();
    }

    /// <summary>
    /// The status line command that ran before TokenUsage took the slot, or null. The wrapper
    /// forwards Claude Code's input to it.
    /// </summary>
    public string? GetPreviousStatusLineCommand()
    {
        JsonObject? previous;
        try
        {
            previous = ReadPreviousStatusLine();
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException
                                           or JsonException
                                           or InvalidDataException)
        {
            return null;
        }

        return previous?["command"] is JsonValue value
            && value.TryGetValue(out string? command)
            && !string.IsNullOrWhiteSpace(command)
            && !IsOwnCommand(command)
                ? command
                : null;
    }

    private JsonObject? ReadPreviousStatusLine()
    {
        if (!File.Exists(PreviousStatusLinePath))
        {
            return null;
        }

        JsonNode? node = JsonNode.Parse(File.ReadAllText(PreviousStatusLinePath, Encoding.UTF8));
        return node?["statusLine"] is JsonObject statusLine
            ? (JsonObject)statusLine.DeepClone()
            : null;
    }

    private void DeletePreviousStatusLine()
    {
        if (File.Exists(PreviousStatusLinePath))
        {
            File.Delete(PreviousStatusLinePath);
        }
    }

    private static bool IsOwnStatusLine(JsonNode? statusLine) =>
        statusLine is JsonObject value
        && value["command"] is JsonValue command
        && command.TryGetValue(out string? text)
        && IsOwnCommand(text);

    private static bool IsOwnCommand(string command) =>
        command.Contains("tokenusage", StringComparison.OrdinalIgnoreCase)
        && command.Contains("claude statusline", StringComparison.Ordinal);

    private static JsonNode? FindHookRegistration(JsonObject document)
    {
        if (document["hooks"] is not JsonObject hooks
            || hooks["Stop"] is not JsonArray stop)
        {
            return null;
        }

        foreach (JsonNode? group in stop)
        {
            if (group is not JsonObject { } groupObject
                || groupObject["hooks"] is not JsonArray entries)
            {
                continue;
            }

            foreach (JsonNode? entry in entries)
            {
                if (entry is JsonObject entryObject
                    && entryObject["command"] is JsonValue command
                    && command.TryGetValue(out string? configuredCommand)
                    && configuredCommand.Contains("tokenusage", StringComparison.OrdinalIgnoreCase)
                    && configuredCommand.Contains("'hook','stop'", StringComparison.Ordinal))
                {
                    return entry;
                }
            }
        }

        return null;
    }

    private static string ResolveConfigDirectory(string? homeDirectory, string? configuredDirectory)
    {
        string home = Path.GetFullPath(homeDirectory
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        string defaultDirectory = Path.Combine(home, ".claude");
        // The usage reader accepts a comma-separated list; settings live in the first entry.
        string? raw = configuredDirectory?
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return defaultDirectory;
        }

        if (raw == "~")
        {
            raw = home;
        }
        else if (raw.StartsWith($"~{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                 || raw.StartsWith($"~{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
        {
            raw = Path.Combine(home, raw[2..]);
        }

        try
        {
            return Path.GetFullPath(raw);
        }
        catch (Exception exception) when (exception is ArgumentException
                                           or NotSupportedException
                                           or PathTooLongException)
        {
            return defaultDirectory;
        }
    }

    private JsonObject ReadDocumentForUpdate()
    {
        if (!File.Exists(SettingsPath))
        {
            return new JsonObject();
        }

        string json = File.ReadAllText(SettingsPath, Encoding.UTF8);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new JsonObject();
        }

        return JsonNode.Parse(
                json,
                documentOptions: new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                }) as JsonObject
            ?? throw new InvalidDataException("The Claude Code settings file must contain a JSON object.");
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
            throw new InvalidDataException($"The Claude Code setting '{name}' must be an object.");
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
            throw new InvalidDataException($"The Claude Code setting '{name}' must be an array.");
        }

        var created = new JsonArray();
        owner[name] = created;
        return created;
    }

    private static void WriteAtomically(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, content, new UTF8Encoding(false));
            File.Move(temporaryPath, path, overwrite: true);
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

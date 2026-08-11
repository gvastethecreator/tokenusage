using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TokenUsage.Providers.Cursor;

namespace TokenUsage.Runtime.Windows.Cursor;

public enum CursorHookInstallationStatus
{
    NotInstalled,
    Installed,
    Incomplete,
}

public sealed class CursorHookInstaller
{
    private const string ScriptFileName = "tokenusage-stop.ps1";
    private const string ResourceName =
        "TokenUsage.Runtime.Windows.Cursor.CursorStopHook.ps1";
    private readonly string _cursorHome;
    private readonly string _hooksPath;
    private readonly string _scriptPath;

    public CursorHookInstaller(string? homeDirectory = null)
    {
        _cursorHome = CursorUsagePaths.ResolveCursorHome(homeDirectory);
        _hooksPath = Path.Combine(_cursorHome, "hooks.json");
        _scriptPath = Path.Combine(_cursorHome, "hooks", ScriptFileName);
    }

    public string HooksPath => _hooksPath;

    public string ScriptPath => _scriptPath;

    public CursorHookInstallationStatus GetStatus()
    {
        bool hasScript = File.Exists(_scriptPath);
        bool hasRegistration = TryReadDocument(out JsonObject? document)
            && FindRegistration(document!) is not null;
        return (hasScript, hasRegistration) switch
        {
            (true, true) => CursorHookInstallationStatus.Installed,
            (false, false) => CursorHookInstallationStatus.NotInstalled,
            _ => CursorHookInstallationStatus.Incomplete,
        };
    }

    public void Install()
    {
        JsonObject document = ReadDocumentForUpdate();
        JsonObject hooks = GetOrCreateObject(document, "hooks");
        JsonArray stop = GetOrCreateArray(hooks, "stop");
        if (FindRegistration(document) is null)
        {
            stop.Add(new JsonObject
            {
                ["command"] = CreateCommand(),
                ["timeout"] = 5,
            });
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_scriptPath)!);
        using Stream resource = typeof(CursorHookInstaller).Assembly
            .GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("The Cursor hook resource is missing.");
        using var reader = new StreamReader(resource, Encoding.UTF8, true);
        WriteAtomically(_scriptPath, reader.ReadToEnd());
        WriteAtomically(
            _hooksPath,
            document.ToJsonString(new JsonSerializerOptions { WriteIndented = true })
            + Environment.NewLine);
    }

    public void Uninstall()
    {
        JsonObject document = ReadDocumentForUpdate();
        JsonNode? registration = FindRegistration(document);
        if (registration?.Parent is JsonArray stop)
        {
            stop.Remove(registration);
            WriteAtomically(
                _hooksPath,
                document.ToJsonString(new JsonSerializerOptions { WriteIndented = true })
                + Environment.NewLine);
        }

        if (File.Exists(_scriptPath))
        {
            File.Delete(_scriptPath);
        }
    }

    private JsonObject ReadDocumentForUpdate()
    {
        if (!File.Exists(_hooksPath))
        {
            return new JsonObject { ["version"] = 1 };
        }

        string json = File.ReadAllText(_hooksPath, Encoding.UTF8);
        return JsonNode.Parse(json) as JsonObject
            ?? throw new InvalidDataException("Cursor hooks.json must contain a JSON object.");
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

    private JsonNode? FindRegistration(JsonObject document)
    {
        if (document["hooks"] is not JsonObject hooks
            || hooks["stop"] is not JsonArray stop)
        {
            return null;
        }

        string command = CreateCommand();
        return stop.FirstOrDefault(node =>
            node is JsonObject item
            && item["command"] is JsonValue value
            && value.TryGetValue(out string? configuredCommand)
            && string.Equals(
                configuredCommand,
                command,
                StringComparison.OrdinalIgnoreCase));
    }

    private string CreateCommand() =>
        $"powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{_scriptPath}\"";

    private static JsonObject GetOrCreateObject(JsonObject owner, string name)
    {
        if (owner[name] is JsonObject existing)
        {
            return existing;
        }

        if (owner[name] is not null)
        {
            throw new InvalidDataException($"Cursor hooks.json '{name}' must be an object.");
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
            throw new InvalidDataException($"Cursor hooks.json '{name}' must be an array.");
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

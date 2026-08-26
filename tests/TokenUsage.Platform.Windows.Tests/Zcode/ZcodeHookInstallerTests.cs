using System.Text;
using System.Text.Json.Nodes;
using TokenUsage.Runtime.Windows.Zcode;

namespace TokenUsage.Platform.Windows.Tests.Zcode;

public sealed class ZcodeHookInstallerTests
{
    [Fact]
    public void InstallCreatesTheDocumentedAsyncStopEntry()
    {
        using var home = new TemporaryHome();
        var installer = new ZcodeHookInstaller(zcodeHomeOverride: home.ZcodeHome);

        installer.Install();

        JsonObject document = ReadDocument(home);
        Assert.True(document["hooks"]!["enabled"]!.GetValue<bool>());
        JsonArray stop = (JsonArray)document["hooks"]!["events"]!["Stop"]!;
        JsonObject entry = (JsonObject)((JsonObject)stop[0]!)["hooks"]![0]!;
        Assert.Equal("command", (string?)entry["type"]);
        Assert.Equal("tokenusage", (string?)entry["command"]);
        Assert.Equal(["zcode", "stop-hook"], ((JsonArray)entry["args"]!)
            .Select(argument => (string?)argument));
        Assert.True(entry["async"]!.GetValue<bool>());
        Assert.True(entry["enabled"]!.GetValue<bool>());
        Assert.Equal(60_000, entry["timeoutMs"]!.GetValue<int>());
        Assert.Equal(ZcodeHookInstallationStatus.Installed, installer.GetStatus());
    }

    [Fact]
    public void InstallPreservesExistingUserConfiguration()
    {
        using var home = new TemporaryHome();
        home.WriteConfig(
            """
            {
              "unknownTopLevel": { "keep": true },
              "hooks": {
                "enabled": true,
                "events": {
                  "SessionStart": [
                    { "matcher": "", "hooks": [ { "type": "command", "command": "user-tool" } ] }
                  ],
                  "Stop": [
                    { "matcher": "", "hooks": [ { "type": "command", "command": "user-tool" } ] }
                  ]
                }
              }
            }
            """);
        var installer = new ZcodeHookInstaller(zcodeHomeOverride: home.ZcodeHome);

        installer.Install();

        JsonObject document = ReadDocument(home);
        Assert.True(document["unknownTopLevel"]!["keep"]!.GetValue<bool>());
        JsonArray sessionStart = (JsonArray)document["hooks"]!["events"]!["SessionStart"]!;
        Assert.Single(sessionStart);
        JsonArray stop = (JsonArray)document["hooks"]!["events"]!["Stop"]!;
        Assert.Equal(2, stop.Count);
        Assert.Contains("user-tool", stop
            .SelectMany(group => (JsonArray)((JsonObject)group!)["hooks"]!)
            .Select(entry => (string?)((JsonObject?)entry)?["command"]));
        Assert.Contains("tokenusage", stop
            .SelectMany(group => (JsonArray)((JsonObject)group!)["hooks"]!)
            .Select(entry => (string?)((JsonObject?)entry)?["command"]));
    }

    [Fact]
    public void InstallIsIdempotent()
    {
        using var home = new TemporaryHome();
        var installer = new ZcodeHookInstaller(zcodeHomeOverride: home.ZcodeHome);

        installer.Install();
        installer.Install();

        JsonArray stop = (JsonArray)ReadDocument(home)["hooks"]!["events"]!["Stop"]!;
        Assert.Single(stop);
        JsonArray entries = (JsonArray)((JsonObject)stop[0]!)["hooks"]!;
        Assert.Single(entries);
    }

    [Fact]
    public void UninstallRemovesOnlyTheTokenUsageEntry()
    {
        using var home = new TemporaryHome();
        var installer = new ZcodeHookInstaller(zcodeHomeOverride: home.ZcodeHome);
        installer.Install();
        home.WriteConfig(
            """
            {
              "hooks": {
                "enabled": true,
                "events": {
                  "Stop": [
                    { "matcher": "", "hooks": [
                      { "type": "command", "command": "user-tool" },
                      { "type": "command", "command": "tokenusage", "args": ["zcode", "stop-hook"], "async": true }
                    ] }
                  ]
                }
              }
            }
            """);

        installer.Uninstall();

        JsonObject document = ReadDocument(home);
        JsonArray stop = (JsonArray)document["hooks"]!["events"]!["Stop"]!;
        JsonObject group = (JsonObject)stop[0]!;
        JsonArray entries = (JsonArray)group["hooks"]!;
        Assert.Single(entries);
        Assert.Equal("user-tool", (string?)((JsonObject)entries[0]!)["command"]);
        Assert.Equal(ZcodeHookInstallationStatus.NotInstalled, installer.GetStatus());
    }

    [Fact]
    public void UninstallWithoutConfigChangesNothing()
    {
        using var home = new TemporaryHome();
        var installer = new ZcodeHookInstaller(zcodeHomeOverride: home.ZcodeHome);

        installer.Uninstall();

        Assert.False(File.Exists(home.ConfigPath));
    }

    [Fact]
    public void EntryWithoutEnabledHooksReportsIncomplete()
    {
        using var home = new TemporaryHome();
        var installer = new ZcodeHookInstaller(zcodeHomeOverride: home.ZcodeHome);
        installer.Install();
        JsonObject document = ReadDocument(home);
        ((JsonObject)document["hooks"]!)["enabled"] = false;
        home.WriteConfig(document.ToJsonString());

        Assert.Equal(ZcodeHookInstallationStatus.Incomplete, installer.GetStatus());
    }

    [Fact]
    public void MissingConfigReportsNotInstalled()
    {
        using var home = new TemporaryHome();
        var installer = new ZcodeHookInstaller(zcodeHomeOverride: home.ZcodeHome);

        Assert.Equal(ZcodeHookInstallationStatus.NotInstalled, installer.GetStatus());
    }

    private static JsonObject ReadDocument(TemporaryHome home) =>
        JsonNode.Parse(File.ReadAllText(home.ConfigPath, Encoding.UTF8)) as JsonObject
        ?? throw new InvalidOperationException("The test config must be an object.");

    private sealed class TemporaryHome : IDisposable
    {
        public TemporaryHome()
        {
            ZcodeHome = Path.Combine(
                Path.GetTempPath(),
                "tokenusage-zcode-hook-tests",
                Guid.NewGuid().ToString("N"),
                ".zcode");
        }

        public string ZcodeHome { get; }

        public string ConfigPath => Path.Combine(ZcodeHome, "cli", "config.json");

        public void WriteConfig(string json)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            File.WriteAllText(ConfigPath, json, new UTF8Encoding(false));
        }

        public void Dispose()
        {
            var root = new DirectoryInfo(ZcodeHome);
            while (root is not null && root.Name != ".zcode")
            {
                root = root.Parent;
            }

            try
            {
                if (root is not null && Directory.Exists(root.Parent!.FullName))
                {
                    Directory.Delete(root.Parent.FullName, recursive: true);
                }
            }
            catch
            {
                // best effort
            }
        }
    }
}

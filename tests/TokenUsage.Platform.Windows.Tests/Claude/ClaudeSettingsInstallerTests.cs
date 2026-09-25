using System.Text;
using System.Text.Json.Nodes;
using TokenUsage.Runtime.Windows.Claude;

namespace TokenUsage.Platform.Windows.Tests.Claude;

public sealed class ClaudeSettingsInstallerTests
{
    [Fact]
    public void InstallHookAddsTheRefreshTriggerAndKeepsOtherSettings()
    {
        using var home = new TemporaryClaudeHome();
        home.WriteSettings(
            """
            {
              // Claude Code accepts comments in its settings file.
              "theme": "dark",
              "hooks": {
                "SessionStart": [ { "hooks": [ { "type": "command", "command": "user-tool" } ] } ],
                "Stop": [ { "hooks": [ { "type": "command", "command": "user-stop" } ] } ]
              }
            }
            """);
        var installer = home.CreateInstaller();

        installer.InstallHook();
        installer.InstallHook();

        JsonObject document = home.ReadSettings();
        Assert.Equal("dark", (string?)document["theme"]);
        Assert.Single((JsonArray)document["hooks"]!["SessionStart"]!);
        JsonArray stop = (JsonArray)document["hooks"]!["Stop"]!;
        Assert.Equal(2, stop.Count);
        JsonObject entry = (JsonObject)((JsonObject)stop[1]!)["hooks"]![0]!;
        Assert.Equal("command", (string?)entry["type"]);
        Assert.Contains("'hook','stop'", (string?)entry["command"], StringComparison.Ordinal);
        Assert.Equal(ClaudeIntegrationStatus.Installed, installer.GetHookStatus());
    }

    [Fact]
    public void UninstallHookRemovesOnlyTheTokenUsageEntry()
    {
        using var home = new TemporaryClaudeHome();
        home.WriteSettings(
            """
            { "hooks": { "Stop": [ { "hooks": [ { "type": "command", "command": "user-stop" } ] } ] } }
            """);
        var installer = home.CreateInstaller();
        installer.InstallHook();

        installer.UninstallHook();

        JsonObject document = home.ReadSettings();
        JsonArray stop = (JsonArray)document["hooks"]!["Stop"]!;
        Assert.Equal("user-stop", (string?)((JsonObject)stop.Single()!)["hooks"]![0]!["command"]);
        Assert.Equal(ClaudeIntegrationStatus.NotInstalled, installer.GetHookStatus());
    }

    [Fact]
    public void UninstallHookDropsEmptyHookSections()
    {
        using var home = new TemporaryClaudeHome();
        var installer = home.CreateInstaller();
        installer.InstallHook();

        installer.UninstallHook();

        Assert.False(home.ReadSettings().ContainsKey("hooks"));
    }

    [Fact]
    public void StatusLineWithoutAPreviousOneIsAddedAndRemoved()
    {
        using var home = new TemporaryClaudeHome();
        home.WriteSettings("""{ "theme": "dark" }""");
        var installer = home.CreateInstaller();

        installer.InstallStatusLine();

        JsonObject statusLine = (JsonObject)home.ReadSettings()["statusLine"]!;
        Assert.Equal("command", (string?)statusLine["type"]);
        Assert.Equal(ClaudeSettingsInstaller.StatusLineCommand, (string?)statusLine["command"]);
        Assert.Equal(ClaudeIntegrationStatus.Installed, installer.GetStatusLineStatus());
        Assert.Null(installer.GetPreviousStatusLineCommand());

        installer.UninstallStatusLine();

        JsonObject document = home.ReadSettings();
        Assert.False(document.ContainsKey("statusLine"));
        Assert.Equal("dark", (string?)document["theme"]);
        Assert.False(File.Exists(installer.PreviousStatusLinePath));
    }

    [Fact]
    public void StatusLineWrapsAndRestoresTheUsersOwnStatusLine()
    {
        using var home = new TemporaryClaudeHome();
        home.WriteSettings(
            """
            { "statusLine": { "type": "command", "command": "~/.claude/statusline.sh", "padding": 2, "refreshInterval": 10 } }
            """);
        var installer = home.CreateInstaller();

        installer.InstallStatusLine();
        installer.InstallStatusLine();

        JsonObject wrapped = (JsonObject)home.ReadSettings()["statusLine"]!;
        Assert.Equal(ClaudeSettingsInstaller.StatusLineCommand, (string?)wrapped["command"]);
        Assert.Equal(2, wrapped["padding"]!.GetValue<int>());
        Assert.Equal(10, wrapped["refreshInterval"]!.GetValue<int>());
        Assert.Equal("~/.claude/statusline.sh", installer.GetPreviousStatusLineCommand());

        installer.UninstallStatusLine();

        JsonObject restored = (JsonObject)home.ReadSettings()["statusLine"]!;
        Assert.Equal("~/.claude/statusline.sh", (string?)restored["command"]);
        Assert.Equal(2, restored["padding"]!.GetValue<int>());
        Assert.False(File.Exists(installer.PreviousStatusLinePath));
    }

    [Fact]
    public void UninstallKeepsAStatusLineTheUserSetAfterwards()
    {
        using var home = new TemporaryClaudeHome();
        var installer = home.CreateInstaller();
        installer.InstallStatusLine();
        home.WriteSettings("""{ "statusLine": { "type": "command", "command": "my-new-line" } }""");

        installer.UninstallStatusLine();

        Assert.Equal("my-new-line", (string?)home.ReadSettings()["statusLine"]!["command"]);
    }

    [Fact]
    public void InvalidSettingsAreNeverOverwritten()
    {
        using var home = new TemporaryClaudeHome();
        home.WriteSettings("{ not json");
        var installer = home.CreateInstaller();

        Assert.ThrowsAny<Exception>(installer.InstallStatusLine);
        Assert.Equal("{ not json", File.ReadAllText(installer.SettingsPath));
        Assert.Equal(ClaudeIntegrationStatus.NotInstalled, installer.GetStatusLineStatus());
    }

    [Fact]
    public void ConfigDirectoryOverrideUsesTheFirstListedFolder()
    {
        using var home = new TemporaryClaudeHome();
        string second = Path.Combine(home.Root, "second");
        var installer = new ClaudeSettingsInstaller(
            homeDirectory: home.Root,
            configDirectoryOverride: $"{home.ConfigDirectory},{second}");

        Assert.Equal(Path.Combine(home.ConfigDirectory, "settings.json"), installer.SettingsPath);
    }

    private sealed class TemporaryClaudeHome : IDisposable
    {
        public TemporaryClaudeHome()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "tokenusage-claude-settings-tests",
                Guid.NewGuid().ToString("N"));
            ConfigDirectory = Path.Combine(Root, ".claude");
            Directory.CreateDirectory(ConfigDirectory);
        }

        public string Root { get; }

        public string ConfigDirectory { get; }

        public ClaudeSettingsInstaller CreateInstaller() =>
            new(homeDirectory: Root, configDirectoryOverride: ConfigDirectory);

        public void WriteSettings(string json) => File.WriteAllText(
            Path.Combine(ConfigDirectory, ClaudeSettingsInstaller.SettingsFileName),
            json,
            new UTF8Encoding(false));

        public JsonObject ReadSettings() => (JsonObject)JsonNode.Parse(File.ReadAllText(
            Path.Combine(ConfigDirectory, ClaudeSettingsInstaller.SettingsFileName)))!;

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}

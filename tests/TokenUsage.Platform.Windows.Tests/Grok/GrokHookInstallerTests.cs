using System.Text;
using System.Text.Json.Nodes;
using TokenUsage.Runtime.Windows.Grok;

namespace TokenUsage.Platform.Windows.Tests.Grok;

public sealed class GrokHookInstallerTests
{
    [Fact]
    public void InstallCreatesTheDocumentedStopEntry()
    {
        using var home = new TemporaryHome();
        var installer = new GrokHookInstaller(grokHomeOverride: home.GrokHome);

        installer.Install();

        JsonObject document = ReadDocument(home);
        JsonArray stop = (JsonArray)document["hooks"]!["Stop"]!;
        JsonObject entry = (JsonObject)((JsonObject)stop[0]!)["hooks"]![0]!;
        Assert.Equal("command", (string?)entry["type"]);
        string command = entry["command"]!.GetValue<string>();
        Assert.Contains("tokenusage", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("'hook','stop'", command, StringComparison.Ordinal);
        Assert.Equal(30, entry["timeout"]!.GetValue<int>());
        Assert.Equal(GrokHookInstallationStatus.Installed, installer.GetStatus());
    }

    [Fact]
    public void InstallPreservesOtherHooksInTheSharedFolderFile()
    {
        using var home = new TemporaryHome();
        home.WriteHookFile(
            """
            {
              "hooks": {
                "SessionStart": [
                  { "hooks": [ { "type": "command", "command": "user-tool" } ] }
                ],
                "Stop": [
                  { "hooks": [ { "type": "command", "command": "user-tool" } ] }
                ]
              }
            }
            """);
        var installer = new GrokHookInstaller(grokHomeOverride: home.GrokHome);

        installer.Install();

        JsonObject document = ReadDocument(home);
        JsonArray sessionStart = (JsonArray)document["hooks"]!["SessionStart"]!;
        Assert.Single(sessionStart);
        JsonArray stop = (JsonArray)document["hooks"]!["Stop"]!;
        Assert.Equal(2, stop.Count);
    }

    [Fact]
    public void InstallIsIdempotent()
    {
        using var home = new TemporaryHome();
        var installer = new GrokHookInstaller(grokHomeOverride: home.GrokHome);

        installer.Install();
        installer.Install();

        JsonArray stop = (JsonArray)ReadDocument(home)["hooks"]!["Stop"]!;
        Assert.Single(stop);
        JsonArray entries = (JsonArray)((JsonObject)stop[0]!)["hooks"]!;
        Assert.Single(entries);
    }

    [Fact]
    public void UninstallRemovesOnlyTheTokenUsageEntryAndKeepsUserHooks()
    {
        using var home = new TemporaryHome();
        var installer = new GrokHookInstaller(grokHomeOverride: home.GrokHome);
        installer.Install();
        home.WriteHookFile(
            """
            {
              "hooks": {
                "Stop": [
                  { "hooks": [
                    { "type": "command", "command": "user-tool" },
                    { "type": "command", "command": "powershell.exe -NoProfile -Command Start-Process tokenusage -ArgumentList 'hook','stop'", "timeout": 30 }
                  ] }
                ]
              }
            }
            """);

        installer.Uninstall();

        JsonObject document = ReadDocument(home);
        JsonArray stop = (JsonArray)document["hooks"]!["Stop"]!;
        JsonObject group = (JsonObject)stop[0]!;
        JsonArray entries = (JsonArray)group["hooks"]!;
        Assert.Single(entries);
        Assert.Equal("user-tool", (string?)((JsonObject)entries[0]!)["command"]);
        Assert.Equal(GrokHookInstallationStatus.NotInstalled, installer.GetStatus());
    }

    [Fact]
    public void UninstallDeletesTheFileWhenOnlyTokenUsageRemains()
    {
        using var home = new TemporaryHome();
        var installer = new GrokHookInstaller(grokHomeOverride: home.GrokHome);
        installer.Install();

        installer.Uninstall();

        Assert.False(File.Exists(home.HookFilePath));
        Assert.Equal(GrokHookInstallationStatus.NotInstalled, installer.GetStatus());
    }

    [Fact]
    public void UninstallWithoutFileChangesNothing()
    {
        using var home = new TemporaryHome();
        var installer = new GrokHookInstaller(grokHomeOverride: home.GrokHome);

        installer.Uninstall();

        Assert.False(File.Exists(home.HookFilePath));
    }

    private static JsonObject ReadDocument(TemporaryHome home) =>
        JsonNode.Parse(File.ReadAllText(home.HookFilePath, Encoding.UTF8)) as JsonObject
        ?? throw new InvalidOperationException("The test hook file must be an object.");

    private sealed class TemporaryHome : IDisposable
    {
        public TemporaryHome()
        {
            GrokHome = Path.Combine(
                Path.GetTempPath(),
                "tokenusage-grok-hook-tests",
                Guid.NewGuid().ToString("N"),
                ".grok");
        }

        public string GrokHome { get; }

        public string HookFilePath => Path.Combine(GrokHome, "hooks", GrokHookInstaller.HookFileName);

        public void WriteHookFile(string json)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(HookFilePath)!);
            File.WriteAllText(HookFilePath, json, new UTF8Encoding(false));
        }

        public void Dispose()
        {
            var root = new DirectoryInfo(GrokHome);
            while (root is not null && root.Name != ".grok")
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

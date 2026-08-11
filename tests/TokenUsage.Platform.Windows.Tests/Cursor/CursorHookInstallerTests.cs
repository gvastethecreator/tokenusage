using System.Text.Json.Nodes;
using TokenUsage.Runtime.Windows.Cursor;

namespace TokenUsage.Platform.Windows.Tests.Cursor;

public sealed class CursorHookInstallerTests
{
    [Fact]
    public void InstallPreservesExistingHooksAndWritesContentDroppingCollector()
    {
        using var folder = new TemporaryFolder();
        string cursorHome = Path.Combine(folder.Path, ".cursor");
        Directory.CreateDirectory(cursorHome);
        File.WriteAllText(
            Path.Combine(cursorHome, "hooks.json"),
            """
            {
              "version": 1,
              "hooks": {
                "afterFileEdit": [{ "command": "existing-tool" }]
              }
            }
            """);
        var installer = new CursorHookInstaller(folder.Path);

        installer.Install();
        installer.Install();

        JsonObject document = JsonNode.Parse(File.ReadAllText(installer.HooksPath))!.AsObject();
        Assert.Equal("existing-tool", document["hooks"]!["afterFileEdit"]![0]!["command"]!.GetValue<string>());
        Assert.Single(document["hooks"]!["stop"]!.AsArray());
        Assert.Equal(CursorHookInstallationStatus.Installed, installer.GetStatus());

        string script = File.ReadAllText(installer.ScriptPath);
        Assert.Contains("input_tokens", script, StringComparison.Ordinal);
        Assert.Contains("cache_read_tokens", script, StringComparison.Ordinal);
        Assert.DoesNotContain("workspace_roots", script, StringComparison.Ordinal);
        Assert.DoesNotContain("transcript_path", script, StringComparison.Ordinal);
        Assert.DoesNotContain("user_email", script, StringComparison.Ordinal);
    }

    [Fact]
    public void UninstallRemovesOnlyTokenUsageRegistration()
    {
        using var folder = new TemporaryFolder();
        var installer = new CursorHookInstaller(folder.Path);
        installer.Install();
        JsonObject document = JsonNode.Parse(File.ReadAllText(installer.HooksPath))!.AsObject();
        document["hooks"]!["stop"]!.AsArray().Add(new JsonObject { ["command"] = "other-hook" });
        File.WriteAllText(installer.HooksPath, document.ToJsonString());

        installer.Uninstall();

        JsonObject updated = JsonNode.Parse(File.ReadAllText(installer.HooksPath))!.AsObject();
        Assert.Equal("other-hook", Assert.Single(updated["hooks"]!["stop"]!.AsArray())!["command"]!.GetValue<string>());
        Assert.False(File.Exists(installer.ScriptPath));
        Assert.Equal(CursorHookInstallationStatus.NotInstalled, installer.GetStatus());
    }

    private sealed class TemporaryFolder : IDisposable
    {
        public TemporaryFolder()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "tokenusage-cursor-hook-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}

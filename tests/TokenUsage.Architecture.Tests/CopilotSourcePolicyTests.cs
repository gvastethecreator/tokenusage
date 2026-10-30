namespace TokenUsage.Architecture.Tests;

public sealed class CopilotSourcePolicyTests
{
    [Fact]
    public void CopilotLocalUsageComesOnlyFromTheApprovedVsCodeChatSessionGate()
    {
        string repoRoot = ProjectReferenceGraph.FindRepoRoot();
        string gate = File.ReadAllText(Path.Combine(
            repoRoot,
            "docs",
            "source-gates",
            "COPILOT-VSCODE.md"));
        Assert.Contains("Status: `approved`", gate, StringComparison.Ordinal);

        string localRoot = Path.Combine(repoRoot, "src", "TokenUsage.Providers", "CopilotVsCode");
        string combined = string.Join('\n', Directory
            .GetFiles(localRoot, "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText));
        Assert.Contains("chatSessions", combined, StringComparison.Ordinal);
        Assert.Contains("emptyWindowChatSessions", combined, StringComparison.Ordinal);

        // Copilot CLI state, extension storage, logs, workspace descriptors, and
        // network or credential access stay out of the local source.
        string[] forbidden =
        [
            "\".copilot\"",
            ".copilot\\",
            ".copilot/",
            "session-state",
            "session-store",
            "OTEL",
            "copilot_internal",
            "hosts.yml",
            "workspace.json",
            "GitHub.copilot-chat",
            "github.copilot-chat",
            "exthost",
            "HttpClient",
            "CredentialManager",
            "PasswordVault",
        ];
        foreach (string token in forbidden)
        {
            Assert.DoesNotContain(token, combined, StringComparison.OrdinalIgnoreCase);
        }
    }
}

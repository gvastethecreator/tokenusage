namespace TokenUsage.Architecture.Tests;

public sealed class CopilotSourcePolicyTests
{
    [Fact]
    public void CopilotClientUsesOnlyPublicGitHubRest()
    {
        string repoRoot = ProjectReferenceGraph.FindRepoRoot();
        string copilotRoot = Path.Combine(repoRoot, "src", "TokenUsage.Providers", "Copilot");
        string[] sources = Directory.GetFiles(copilotRoot, "*.cs", SearchOption.AllDirectories);
        Assert.NotEmpty(sources);

        string combined = string.Join('\n', sources.Select(File.ReadAllText));

        Assert.Contains("https://api.github.com", combined, StringComparison.Ordinal);
        Assert.Contains("2026-03-10", combined, StringComparison.Ordinal);
        Assert.Contains("application/vnd.github+json", combined, StringComparison.Ordinal);
        Assert.Contains("/settings/billing/ai_credit/usage", combined, StringComparison.Ordinal);
        Assert.Contains("/copilot/billing", combined, StringComparison.Ordinal);
        Assert.Contains("https://api.github.com/user\"", combined, StringComparison.Ordinal);

        string[] forbidden =
        [
            "copilot_internal",
            "Editor-Version",
            "Copilot-Session",
            "Openai-Organization",
            "hosts.yml",
            "vscode",
            "Visual Studio",
            "gh auth",
            "github.com/login",
        ];
        foreach (string token in forbidden)
        {
            Assert.DoesNotContain(token, combined, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void CopilotCliTelemetryRemainsSeparateAndPolicyBlocked()
    {
        string repoRoot = ProjectReferenceGraph.FindRepoRoot();
        string gate = File.ReadAllText(Path.Combine(
            repoRoot,
            "docs",
            "source-gates",
            "COPILOT-CLI.md"));

        Assert.Contains("Status: `policy-blocked`", gate, StringComparison.Ordinal);
        Assert.Contains("1.0.82", gate, StringComparison.Ordinal);
        Assert.Contains("be82101e70f0253b57519bebb9cc9d0f6dfb2ed2", gate, StringComparison.Ordinal);
        Assert.Contains("gen_ai.client.token.usage", gate, StringComparison.Ordinal);
        Assert.Contains("would not replace the existing opt-in GitHub Billing", gate, StringComparison.Ordinal);
        Assert.Contains("Content-bearing field", gate, StringComparison.Ordinal);

        Assert.False(Directory.Exists(Path.Combine(
            repoRoot,
            "src",
            "TokenUsage.Providers",
            "CopilotCli")));
    }

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

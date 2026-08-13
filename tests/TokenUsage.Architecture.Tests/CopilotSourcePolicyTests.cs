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
}

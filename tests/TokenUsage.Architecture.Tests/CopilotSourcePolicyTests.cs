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

        string catalog = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "TokenUsage.Providers",
            "Catalog",
            "ProviderModuleCatalog.cs"));
        string copilotLine = catalog.Split('\n').Single(line =>
            line.Contains("Module(\"copilot\"", StringComparison.Ordinal));
        Assert.DoesNotContain("ProviderCapability.LocalUsage", copilotLine, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(
            repoRoot,
            "src",
            "TokenUsage.Providers",
            "CopilotCli")));
    }
}

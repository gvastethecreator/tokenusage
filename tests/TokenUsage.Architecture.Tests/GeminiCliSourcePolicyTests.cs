using TokenUsage.Core.Providers;
using TokenUsage.Providers.Catalog;

namespace TokenUsage.Architecture.Tests;

public sealed class GeminiCliSourcePolicyTests
{
    [Fact]
    public void GeminiCliRemainsBlockedUntilMetricsAreSeparatedFromContentSignals()
    {
        ProviderModuleDefinition module = ProviderModuleCatalog.Get("gemini-cli");

        Assert.Equal(ProviderModuleStage.PolicyBlocked, module.Stage);
        Assert.Contains(ProviderCapability.LocalUsage, module.Capabilities);

        string root = ProjectReferenceGraph.FindRepoRoot();
        string gate = File.ReadAllText(Path.Combine(
            root,
            "docs",
            "source-gates",
            "GEMINI-CLI.md"));
        Assert.Contains("Status: `policy-blocked`", gate, StringComparison.Ordinal);
        Assert.Contains("55b495d6db1794bf5b7f37a9bc03ebcab5103673", gate, StringComparison.Ordinal);
        Assert.Contains("gemini_cli.token.usage", gate, StringComparison.Ordinal);
        Assert.Contains("Local usage would remain separate from Google", gate, StringComparison.Ordinal);
        Assert.Contains("Content-bearing field", gate, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(
            root,
            "src",
            "TokenUsage.Providers",
            "Gemini")));
    }
}

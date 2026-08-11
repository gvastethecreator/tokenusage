using TokenUsage.App.ViewModels.Reports;

namespace TokenUsage.Architecture.Tests;

public sealed class UsageReportProviderOptionReconcilerTests
{
    [Fact]
    public void ReconcileReusesOptionsAndPreservesTheSelectedProvider()
    {
        UsageReportProviderOption codex = new("codex", "Codex");
        UsageReportProviderOption openCode = new("opencode", "OpenCode");
        UsageReportProviderOption[] current = [codex, openCode];

        UsageReportProviderSelectionState result = UsageReportProviderOptionReconciler.Reconcile(
            current,
            "opencode",
            ["codex", "opencode"],
            id => id == "codex" ? "Codex" : "OpenCode");

        Assert.False(result.OptionsChanged);
        Assert.Same(codex, result.Options[0]);
        Assert.Same(openCode, result.Options[1]);
        Assert.Same(openCode, result.Selected);
    }
}

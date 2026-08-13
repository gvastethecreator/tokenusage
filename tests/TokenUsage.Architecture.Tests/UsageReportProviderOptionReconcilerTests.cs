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

    [Fact]
    public void ReconcileWithNoUsedProvidersClearsTheSelection()
    {
        UsageReportProviderOption[] current =
        [
            new("codex", "Codex"),
            new("amp", "Amp"),
        ];

        UsageReportProviderSelectionState result = UsageReportProviderOptionReconciler.Reconcile(
            current,
            "amp",
            [],
            id => id);

        Assert.True(result.OptionsChanged);
        Assert.Empty(result.Options);
        Assert.Null(result.Selected);
    }

    [Fact]
    public void SelectUsedProviderIdsKeepsAgentsWithEventsOrTokens()
    {
        IReadOnlyList<string> ids = UsageReportProviderOptionReconciler.SelectUsedProviderIds(
        [
            ("codex", 12, 100),
            ("amp", 0, 0),
            ("cursor", 0, 40),
            ("", 8, 0),
            ("codex", 3, 10),
        ]);

        Assert.Equal(["codex", "cursor"], ids);
    }
}

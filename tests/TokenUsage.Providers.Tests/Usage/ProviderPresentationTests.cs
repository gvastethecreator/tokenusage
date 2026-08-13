using System.Globalization;
using TokenUsage.App.Localization;
using TokenUsage.App.ViewModels;
using TokenUsage.App.ViewModels.Dashboard;
using TokenUsage.Core.Usage;
using TokenUsage.Providers.Catalog;

namespace TokenUsage.Providers.Tests.Usage;

public sealed class ProviderPresentationTests
{
    private static readonly string[] MixedProviderIds =
        ["hermes", "brand-new", "goose", "codex"];

    [Fact]
    public void CuratedOrderPutsAnUnknownProviderLastInsteadOfOnTopOfAnother()
    {
        string[] ordered = MixedProviderIds
            .ByCuratedRank(providerId => providerId)
            .ToArray();

        Assert.Equal(["codex", "goose", "hermes", "brand-new"], ordered);
        Assert.Equal(int.MaxValue, ProviderDisplayOrder.CuratedRank("brand-new"));
        Assert.NotEqual(
            ProviderDisplayOrder.CuratedRank("goose"),
            ProviderDisplayOrder.CuratedRank("brand-new"));
    }

    [Fact]
    public void SpendOrderBreaksATieByVolumeAndThenByIdSoTheListNeverShuffles()
    {
        (string Id, decimal Spend, long Tokens)[] rows =
        [
            ("opencode", 2m, 10),
            ("claude", 2m, 10),
            ("codex", 2m, 99),
            ("amp", 5m, 1),
        ];

        string[] ordered = rows
            .BySpend(row => row.Spend, row => row.Tokens, row => row.Id)
            .Select(row => row.Id)
            .ToArray();

        Assert.Equal(["amp", "codex", "claude", "opencode"], ordered);
    }

    [Fact]
    public void ProviderNameUsesTheTranslationAndFallsBackToTheCatalog()
    {
        string Translate(string key) => key switch
        {
            "LocalUsageAgentCodex" => "Códex traducido",
            _ => throw new InvalidOperationException($"The resource '{key}' is missing."),
        };

        Assert.Equal("Códex traducido", ProviderDisplayName.Resolve("codex", Translate));
        Assert.Equal("Claude", ProviderDisplayName.Resolve("claude", Translate));
        Assert.Equal("Amp", ProviderDisplayName.Resolve("amp", Translate));
        Assert.Equal("Gemini CLI", ProviderDisplayName.Resolve("gemini-cli", Translate));
        Assert.Equal("brand-new", ProviderDisplayName.Resolve("brand-new", Translate));
        Assert.Equal(string.Empty, ProviderDisplayName.Resolve("  ", Translate));
        Assert.Equal(
            ProviderPresentationCatalog.CuratedRank("codex"),
            ProviderDisplayOrder.CuratedRank("codex"));
        Assert.Equal("codex.svg", ProviderPresentationCatalog.MarkFileName("codex"));
        Assert.Equal(
            "LocalUsageAgentCodex",
            ProviderPresentationCatalog.DisplayNameResourceKey("codex"));
    }

    /// <summary>
    /// The bucket and the suffix are the rule under test. The digits themselves follow the
    /// current culture, so the expected text is built with the same culture.
    /// </summary>
    [Fact]
    public void CompactTokenTextShortensLargeCountsAndKeepsSmallOnesWhole()
    {
        Assert.Equal(Local("{0:N0}", 999), UsageValueFormatter.CompactTokens(999));
        Assert.Equal(Local("{0:0.#}K", 1.5), UsageValueFormatter.CompactTokens(1_500));
        Assert.Equal(Local("{0:0.#}M", 2.25), UsageValueFormatter.CompactTokens(2_250_000));
        Assert.Equal(Local("{0:0.##}B", 1.25), UsageValueFormatter.CompactTokens(1_250_000_000));
        Assert.Equal(Local("${0:0.##}", 12.34), UsageValueFormatter.CompactUsd(12.34));
        Assert.Equal(Local("${0:N0}", 1_200), UsageValueFormatter.CompactUsd(1_200));
    }

    [Theory]
    [InlineData(UsageSourceReadStatus.Complete, UsageSourceIssueKind.None, ProviderStatusKind.Available)]
    [InlineData(UsageSourceReadStatus.Partial, UsageSourceIssueKind.None, ProviderStatusKind.Partial)]
    [InlineData(UsageSourceReadStatus.NoData, UsageSourceIssueKind.Empty, ProviderStatusKind.Pending)]
    [InlineData(UsageSourceReadStatus.NoData, UsageSourceIssueKind.RootUnavailable, ProviderStatusKind.Missing)]
    [InlineData(UsageSourceReadStatus.Complete, UsageSourceIssueKind.RootUnavailable, ProviderStatusKind.Missing)]
    [InlineData(UsageSourceReadStatus.NoData, UsageSourceIssueKind.AccessBlocked, ProviderStatusKind.Partial)]
    [InlineData(UsageSourceReadStatus.NoData, UsageSourceIssueKind.UnsupportedSchema, ProviderStatusKind.Partial)]
    public void LocalDiagnosticMapsToOneStatusForEverySurface(
        UsageSourceReadStatus status,
        UsageSourceIssueKind issue,
        ProviderStatusKind expected) =>
        Assert.Equal(expected, ProviderStatusPolicy.FromLocalDiagnostic(status, issue));

    /// <summary>
    /// Every status has its own line of text. A status without one would silently borrow another
    /// status's words, which is how a prepared provider could have read as "pending".
    /// </summary>
    [Fact]
    public void EveryStatusHasItsOwnCompactStateText()
    {
        ProviderStatusKind[] kinds = Enum.GetValues<ProviderStatusKind>()
            .Where(kind => kind != ProviderStatusKind.Neutral)
            .ToArray();

        string[] keys = kinds.Select(ProviderStatusPolicy.CompactStateKey).ToArray();

        Assert.Equal(kinds.Length, keys.Distinct(StringComparer.Ordinal).Count());
        Assert.All(keys, key => Assert.StartsWith(
            "ProviderStatusSummary",
            key,
            StringComparison.Ordinal));
    }

    private static string Local(string format, double value) =>
        string.Format(CultureInfo.CurrentCulture, format, value);
}

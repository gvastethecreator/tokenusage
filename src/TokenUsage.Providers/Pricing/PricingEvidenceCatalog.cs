using TokenUsage.Providers.Claude;
using TokenUsage.Providers.Codex;
using TokenUsage.Providers.Cursor;
using TokenUsage.Providers.Grok;
using TokenUsage.Providers.Zcode;

namespace TokenUsage.Providers.Pricing;

public static class PricingEvidenceCatalog
{
    public static IReadOnlyList<PricingRateEvidence> AllRates { get; } =
    [
        .. ClaudePricingCatalog.EvidenceEntries,
        .. CodexPricingCatalog.EvidenceEntries,
        .. CursorPricingCatalog.EvidenceEntries,
        .. GooglePricingCatalog.EvidenceEntries,
        .. GrokPricingCatalog.EvidenceEntries,
        .. MoonshotPricingCatalog.EvidenceEntries,
        .. ZcodePricingCatalog.EvidenceEntries,
    ];

    public static IReadOnlyList<PricingSourceEvidence> AllSources { get; } =
        AllRates
            .Select(item => item.Source)
            .Distinct()
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();
}

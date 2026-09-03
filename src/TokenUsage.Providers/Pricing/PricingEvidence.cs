using TokenUsage.Core.Usage;

namespace TokenUsage.Providers.Pricing;

public enum PricingBillingScope
{
    DirectProviderApi,
    HostSpecific,
}

public enum PricingValidityEndRule
{
    None,
    Exclusive,
    Inclusive,
}

public sealed record PricingSourceEvidence
{
    public PricingSourceEvidence(
        string id,
        Uri officialUri,
        DateOnly reviewedOn,
        PricingBillingScope billingScope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(officialUri);
        if (!officialUri.IsAbsoluteUri || officialUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("The pricing source must use an absolute HTTPS URI.", nameof(officialUri));
        }

        Id = id;
        OfficialUri = officialUri;
        ReviewedOn = reviewedOn;
        BillingScope = billingScope;
    }

    public string Id { get; }

    public Uri OfficialUri { get; }

    public DateOnly ReviewedOn { get; }

    public PricingBillingScope BillingScope { get; }
}

public sealed record PricingRateEvidence
{
    public PricingRateEvidence(
        string catalogVersion,
        string exactPriceMatch,
        PricingSourceEvidence source,
        DateTimeOffset effectiveFromUtc,
        DateTimeOffset? effectiveUntilUtc = null,
        PricingValidityEndRule endRule = PricingValidityEndRule.None,
        bool isPromotional = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(exactPriceMatch);
        ArgumentNullException.ThrowIfNull(source);
        EnsureUtc(effectiveFromUtc, nameof(effectiveFromUtc));
        if (effectiveUntilUtc is DateTimeOffset until)
        {
            EnsureUtc(until, nameof(effectiveUntilUtc));
            if (until <= effectiveFromUtc)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(effectiveUntilUtc),
                    "The pricing validity end must be after its start.");
            }

            if (endRule == PricingValidityEndRule.None)
            {
                throw new ArgumentException(
                    "A bounded price must declare whether its end is inclusive or exclusive.",
                    nameof(endRule));
            }
        }
        else if (endRule != PricingValidityEndRule.None)
        {
            throw new ArgumentException(
                "An unbounded price cannot declare an end rule.",
                nameof(endRule));
        }

        if (isPromotional && effectiveUntilUtc is null)
        {
            throw new ArgumentException("A promotional price must have an explicit end.", nameof(isPromotional));
        }

        CatalogVersion = catalogVersion;
        ExactPriceMatch = exactPriceMatch;
        Source = source;
        EffectiveFromUtc = effectiveFromUtc;
        EffectiveUntilUtc = effectiveUntilUtc;
        EndRule = endRule;
        IsPromotional = isPromotional;
    }

    public string CatalogVersion { get; }

    public string ExactPriceMatch { get; }

    public PricingSourceEvidence Source { get; }

    public DateTimeOffset EffectiveFromUtc { get; }

    public DateTimeOffset? EffectiveUntilUtc { get; }

    public PricingValidityEndRule EndRule { get; }

    public bool IsPromotional { get; }

    public bool IsEffectiveAt(DateTimeOffset occurredAtUtc)
    {
        EnsureUtc(occurredAtUtc, nameof(occurredAtUtc));
        if (occurredAtUtc < EffectiveFromUtc)
        {
            return false;
        }

        return EffectiveUntilUtc switch
        {
            null => true,
            DateTimeOffset until when EndRule == PricingValidityEndRule.Exclusive =>
                occurredAtUtc < until,
            DateTimeOffset until when EndRule == PricingValidityEndRule.Inclusive =>
                occurredAtUtc <= until,
            _ => false,
        };
    }

    private static void EnsureUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The timestamp must use the UTC offset.", parameterName);
        }
    }
}

public enum PricingDiagnosticKind
{
    StaleSource,
    PromotionNearExpiry,
    ExpiredPromotionWithoutSuccessor,
}

public sealed record PricingDiagnostic(
    PricingDiagnosticKind Kind,
    string SourceId,
    string? ExactPriceMatch,
    DateTimeOffset? EffectiveUntilUtc);

public static class PricingEvidence
{
    public static DateTimeOffset HistoricalStartUtc { get; } = DateTimeOffset.MinValue;

    public static PricingRateEvidence Ongoing(
        string catalogVersion,
        string exactPriceMatch,
        PricingSourceEvidence source,
        DateTimeOffset? effectiveFromUtc = null) =>
        new(
            catalogVersion,
            exactPriceMatch,
            source,
            effectiveFromUtc ?? HistoricalStartUtc);

    public static PricingRateEvidence Promotion(
        string catalogVersion,
        string exactPriceMatch,
        PricingSourceEvidence source,
        DateTimeOffset effectiveUntilUtc) =>
        new(
            catalogVersion,
            exactPriceMatch,
            source,
            HistoricalStartUtc,
            effectiveUntilUtc,
            PricingValidityEndRule.Exclusive,
            isPromotional: true);

    public static PricingRateEvidence FollowOn(
        string catalogVersion,
        string exactPriceMatch,
        PricingSourceEvidence source,
        DateTimeOffset effectiveFromUtc) =>
        Ongoing(catalogVersion, exactPriceMatch, source, effectiveFromUtc);
}

public static class PricingOfficialSources
{
    private static readonly DateOnly ReviewDate = new(2026, 9, 3);

    public static PricingSourceEvidence OpenAi { get; } = new(
        "openai-model-pricing",
        new Uri("https://developers.openai.com/api/docs/models/gpt-5.6-sol"),
        ReviewDate,
        PricingBillingScope.DirectProviderApi);

    public static PricingSourceEvidence Anthropic { get; } = new(
        "anthropic-model-pricing",
        new Uri("https://platform.claude.com/docs/en/about-claude/pricing"),
        ReviewDate,
        PricingBillingScope.DirectProviderApi);

    public static PricingSourceEvidence Google { get; } = new(
        "google-gemini-api-pricing",
        new Uri("https://ai.google.dev/gemini-api/docs/pricing"),
        ReviewDate,
        PricingBillingScope.DirectProviderApi);

    public static PricingSourceEvidence Xai { get; } = new(
        "xai-model-pricing",
        new Uri("https://docs.x.ai/developers/models/grok-4.6"),
        ReviewDate,
        PricingBillingScope.DirectProviderApi);

    public static PricingSourceEvidence Cursor { get; } = new(
        "cursor-model-pricing",
        new Uri("https://cursor.com/docs/models-and-pricing"),
        ReviewDate,
        PricingBillingScope.HostSpecific);

    public static PricingSourceEvidence Zai { get; } = new(
        "zai-model-pricing",
        new Uri("https://docs.z.ai/guides/overview/pricing"),
        ReviewDate,
        PricingBillingScope.DirectProviderApi);

    public static PricingSourceEvidence Moonshot { get; } = new(
        "moonshot-model-pricing",
        new Uri("https://platform.moonshot.ai/docs/pricing/chat"),
        ReviewDate,
        PricingBillingScope.DirectProviderApi);
}

public static class PricingCatalogAudit
{
    public static void ValidateCoverage(
        string catalogVersion,
        IEnumerable<string> exactPriceMatches,
        IReadOnlyList<PricingRateEvidence> evidence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogVersion);
        ArgumentNullException.ThrowIfNull(exactPriceMatches);
        ArgumentNullException.ThrowIfNull(evidence);

        string[] expected = exactPriceMatches
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] covered = evidence
            .Where(item => item.CatalogVersion == catalogVersion)
            .Select(item => item.ExactPriceMatch)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (!expected.SequenceEqual(covered, StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                $"Pricing evidence coverage does not match catalog '{catalogVersion}'.");
        }
    }

    public static IReadOnlyList<PricingDiagnostic> Evaluate(
        IReadOnlyList<PricingRateEvidence> evidence,
        DateTimeOffset nowUtc,
        TimeSpan staleAfter,
        TimeSpan promotionWarningWindow)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        EnsureNonNegative(staleAfter, nameof(staleAfter));
        EnsureNonNegative(promotionWarningWindow, nameof(promotionWarningWindow));
        if (nowUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The timestamp must use the UTC offset.", nameof(nowUtc));
        }

        var diagnostics = new List<PricingDiagnostic>();
        DateOnly today = DateOnly.FromDateTime(nowUtc.UtcDateTime);
        foreach (PricingSourceEvidence source in evidence.Select(item => item.Source).Distinct())
        {
            if (today.DayNumber - source.ReviewedOn.DayNumber > staleAfter.TotalDays)
            {
                diagnostics.Add(new(
                    PricingDiagnosticKind.StaleSource,
                    source.Id,
                    null,
                    null));
            }
        }

        foreach (PricingRateEvidence promotion in evidence.Where(item => item.IsPromotional))
        {
            DateTimeOffset end = promotion.EffectiveUntilUtc!.Value;
            if (end > nowUtc && end - nowUtc <= promotionWarningWindow)
            {
                diagnostics.Add(new(
                    PricingDiagnosticKind.PromotionNearExpiry,
                    promotion.Source.Id,
                    promotion.ExactPriceMatch,
                    end));
                continue;
            }

            if (end <= nowUtc && !evidence.Any(candidate =>
                    candidate.CatalogVersion == promotion.CatalogVersion
                    && candidate.ExactPriceMatch == promotion.ExactPriceMatch
                    && !candidate.IsPromotional
                    && candidate.IsEffectiveAt(nowUtc)))
            {
                diagnostics.Add(new(
                    PricingDiagnosticKind.ExpiredPromotionWithoutSuccessor,
                    promotion.Source.Id,
                    promotion.ExactPriceMatch,
                    end));
            }
        }

        return diagnostics
            .OrderBy(item => item.Kind)
            .ThenBy(item => item.SourceId, StringComparer.Ordinal)
            .ThenBy(item => item.ExactPriceMatch, StringComparer.Ordinal)
            .ToArray();
    }

    public static bool CanReproduce(
        CostObservation observation,
        DateTimeOffset occurredAtUtc,
        IReadOnlyList<PricingRateEvidence> evidence)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(evidence);
        if (observation.Kind != CostKind.CatalogEstimated
            || observation.CatalogVersion is null
            || observation.ExactPriceMatch is null)
        {
            return false;
        }

        return evidence.Any(item =>
            item.CatalogVersion == observation.CatalogVersion
            && item.ExactPriceMatch == observation.ExactPriceMatch
            && item.IsEffectiveAt(occurredAtUtc));
    }

    private static void EnsureNonNegative(TimeSpan value, string parameterName)
    {
        if (value < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}

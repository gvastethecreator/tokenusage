namespace TokenUsage.Core.Usage;

public static class AttributionAdmission
{
    public static bool AdmitsAutomaticLink(
        AttributionConsent? consent,
        DateTimeOffset occurredAtUtc,
        string eventKey,
        IReadOnlySet<string> historicalEventKeys,
        DateOnly? backfillFromInclusive = null,
        DateOnly? backfillToInclusive = null,
        IReadOnlySet<string>? committedEventKeys = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventKey);
        ArgumentNullException.ThrowIfNull(historicalEventKeys);
        if (consent is null || !consent.AllowsLinks)
        {
            return false;
        }

        if (committedEventKeys is not null && committedEventKeys.Contains(eventKey))
        {
            return true;
        }

        DateOnly occurred = DateOnly.FromDateTime(occurredAtUtc.UtcDateTime);
        if (backfillFromInclusive is { } from && backfillToInclusive is { } to)
        {
            return occurred >= from && occurred <= to;
        }

        if (historicalEventKeys.Contains(eventKey))
        {
            return false;
        }

        DateTimeOffset enabledAt = consent.EnabledAtUtc ?? consent.UpdatedAtUtc;
        return occurredAtUtc >= enabledAt;
    }

    public static IReadOnlyList<UsageSessionLink> FilterByLiveConsent(
        IReadOnlyList<UsageSessionLink> links,
        IReadOnlyList<AttributionConsent> consents)
    {
        ArgumentNullException.ThrowIfNull(links);
        ArgumentNullException.ThrowIfNull(consents);
        if (links.Count == 0)
        {
            return [];
        }

        return links
            .Where(link => consents.Any(consent =>
                consent.Capability.Value == link.Capability.Value && consent.AcceptsEpoch(link.ConsentEpoch)))
            .ToArray();
    }

    public static IReadOnlyList<UsageProjectLink> FilterProjectByLiveConsent(
        IReadOnlyList<UsageProjectLink> links,
        AttributionConsent consent)
    {
        ArgumentNullException.ThrowIfNull(links);
        if (links.Count == 0 || !consent.AllowsLinks)
        {
            return [];
        }

        return links.Where(link => consent.AcceptsEpoch(link.ConsentEpoch)).ToArray();
    }

    public static IReadOnlyList<UsageOperationFact> FilterOperationsByLiveConsent(
        IReadOnlyList<UsageOperationFact> facts,
        AttributionConsent consent)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (facts.Count == 0 || !consent.AllowsLinks)
        {
            return [];
        }

        return facts.Where(fact =>
                fact.Capability.Value == consent.Capability.Value
                && consent.AcceptsEpoch(fact.ConsentEpoch))
            .ToArray();
    }

    public static HashSet<string> SnapshotEventKeys(IEnumerable<string> eventKeys)
    {
        ArgumentNullException.ThrowIfNull(eventKeys);
        return eventKeys.ToHashSet(StringComparer.Ordinal);
    }
}

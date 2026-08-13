using TokenUsage.Providers.Catalog;

namespace TokenUsage.App.ViewModels;

/// <summary>
/// The order providers take when a surface lists them. Two rules cover every list: a curated
/// rank where the order is fixed, and "highest spend first" where the data ranks itself. Both
/// end on the provider ID, so two providers that tie never swap places between refreshes.
/// </summary>
public static class ProviderDisplayOrder
{
    /// <summary>
    /// Rank in the curated list. A provider outside the list sorts last instead of landing on
    /// another provider's rank.
    /// </summary>
    public static int CuratedRank(string providerId) =>
        ProviderPresentationCatalog.CuratedRank(providerId);

    public static IOrderedEnumerable<T> ByCuratedRank<T>(
        this IEnumerable<T> items,
        Func<T, string> providerId)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(providerId);
        return items
            .OrderBy(item => CuratedRank(providerId(item)))
            .ThenBy(providerId, StringComparer.Ordinal);
    }

    /// <summary>
    /// Money first, then volume, then the ID. Volume breaks a tie because a provider with no
    /// priced model still has usage worth ranking.
    /// </summary>
    public static IOrderedEnumerable<T> BySpend<T>(
        this IEnumerable<T> items,
        Func<T, decimal> spend,
        Func<T, long> tokens,
        Func<T, string> providerId)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(spend);
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(providerId);
        return items
            .OrderByDescending(spend)
            .ThenByDescending(tokens)
            .ThenBy(providerId, StringComparer.Ordinal);
    }
}

using TokenUsage.Providers.Catalog;

namespace TokenUsage.App.Localization;

/// <summary>
/// One place that turns a provider ID into the name a person reads. A translated resource
/// wins when the provider has one, the catalog name covers every other catalog entry, and the
/// raw ID is the last resort. Three surfaces used to keep their own list, so a provider added
/// to the catalog showed its ID on some screens and its name on others.
/// </summary>
public static class ProviderDisplayName
{
    public static string Resolve(string providerId, Func<string, string> getString)
    {
        ArgumentNullException.ThrowIfNull(getString);
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return string.Empty;
        }

        if (ProviderPresentationCatalog.DisplayNameResourceKey(providerId) is string key
            && TryGetString(key, getString) is string translated)
        {
            return translated;
        }

        return CatalogName(providerId) ?? providerId;
    }

    public static string? CatalogName(string providerId) => ProviderModuleCatalog.Entries
        .FirstOrDefault(entry => string.Equals(entry.Id.Value, providerId, StringComparison.Ordinal))
        ?.DisplayName;

    /// <summary>
    /// A missing translation must not take down a report, so the caller falls back to the
    /// catalog name instead.
    /// </summary>
    private static string? TryGetString(string key, Func<string, string> getString)
    {
        try
        {
            string value = getString(key);
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or KeyNotFoundException
            or ArgumentException)
        {
            return null;
        }
    }
}

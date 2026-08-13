using System.Collections.ObjectModel;
using System.Globalization;

namespace TokenUsage.App.Localization;

public static class AppLanguageCatalog
{
    public const string EnglishUnitedStates = "en-US";
    public const string SpanishSpain = "es-ES";

    public static ReadOnlyCollection<string> SupportedLanguageTags { get; } =
        Array.AsReadOnly([EnglishUnitedStates, SpanishSpain]);

    public static string ResolveLanguageTag(string? languageTag)
    {
        if (string.IsNullOrWhiteSpace(languageTag)
            || languageTag.Contains('_', StringComparison.Ordinal))
        {
            return EnglishUnitedStates;
        }

        try
        {
            CultureInfo culture = CultureInfo.GetCultureInfo(languageTag.Trim());
            return culture.TwoLetterISOLanguageName switch
            {
                "es" => SpanishSpain,
                "en" => EnglishUnitedStates,
                _ => EnglishUnitedStates,
            };
        }
        catch (CultureNotFoundException)
        {
            return EnglishUnitedStates;
        }
    }

    public static string ResolveStartupLanguageTag(
        string? primaryLanguageOverride,
        IReadOnlyList<string> preferredLanguages)
    {
        string? requestedLanguage = primaryLanguageOverride;
        if (string.IsNullOrWhiteSpace(requestedLanguage) && preferredLanguages.Count > 0)
        {
            requestedLanguage = preferredLanguages[0];
        }

        return ResolveLanguageTag(requestedLanguage);
    }

    public static CultureInfo GetCulture(string? languageTag) =>
        CultureInfo.GetCultureInfo(ResolveLanguageTag(languageTag));
}

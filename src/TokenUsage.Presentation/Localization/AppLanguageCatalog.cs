using System.Globalization;

namespace TokenUsage.App.Localization;

public static class AppLanguageCatalog
{
    public const string EnglishUnitedStates = "en-US";

    public static IReadOnlyList<string> SupportedLanguageTags { get; } =
        Array.AsReadOnly([EnglishUnitedStates]);

    public static string ResolveLanguageTag(string? languageTag) => EnglishUnitedStates;

    public static string ResolveStartupLanguageTag(
        string? primaryLanguageOverride,
        IReadOnlyList<string> preferredLanguages)
        => EnglishUnitedStates;

    public static CultureInfo GetCulture(string? languageTag) =>
        CultureInfo.GetCultureInfo(ResolveLanguageTag(languageTag));
}

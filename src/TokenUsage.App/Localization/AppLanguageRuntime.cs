using System.Globalization;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.Globalization;

namespace WOpenUsage.App.Localization;

internal static class AppLanguageRuntime
{
    public static string ActiveLanguageTag { get; private set; } =
        AppLanguageCatalog.EnglishUnitedStates;

    public static void Initialize()
    {
        ActiveLanguageTag = AppLanguageCatalog.ResolveStartupLanguageTag(
            ApplicationLanguages.PrimaryLanguageOverride,
            ApplicationLanguages.Languages);
        ApplicationLanguages.PrimaryLanguageOverride = ActiveLanguageTag;
        CultureInfo culture = AppLanguageCatalog.GetCulture(ActiveLanguageTag);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }

    public static bool RequiresRestart(string languageTag) =>
        !string.Equals(
            AppLanguageCatalog.ResolveLanguageTag(languageTag),
            ActiveLanguageTag,
            StringComparison.OrdinalIgnoreCase);

    public static void RestartWithLanguage(
        string languageTag,
        string arguments)
    {
        string previousOverride = ApplicationLanguages.PrimaryLanguageOverride;
        try
        {
            ApplicationLanguages.PrimaryLanguageOverride =
                AppLanguageCatalog.ResolveLanguageTag(languageTag);
            _ = AppInstance.Restart(arguments);
        }
        finally
        {
            ApplicationLanguages.PrimaryLanguageOverride = previousOverride;
        }
    }
}

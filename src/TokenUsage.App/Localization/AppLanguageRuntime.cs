using System.Globalization;
using Microsoft.Windows.Globalization;

namespace TokenUsage.App.Localization;

internal static class AppLanguageRuntime
{
    public static string ActiveLanguageTag { get; private set; } =
        AppLanguageCatalog.EnglishUnitedStates;

    public static void Initialize()
    {
        ActiveLanguageTag = AppLanguageCatalog.EnglishUnitedStates;
        ApplicationLanguages.PrimaryLanguageOverride = ActiveLanguageTag;
        CultureInfo culture = AppLanguageCatalog.GetCulture(ActiveLanguageTag);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }
}

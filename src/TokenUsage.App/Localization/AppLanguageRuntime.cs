using System.Globalization;
using Microsoft.Windows.Globalization;

namespace TokenUsage.App.Localization;

// The app ships one language. Resources and number formats follow en-US whatever the
// Windows display language is, so layouts and formatted values stay the ones that were tested.
internal static class AppLanguageRuntime
{
    public const string ActiveLanguageTag = "en-US";

    public static void Initialize()
    {
        ApplicationLanguages.PrimaryLanguageOverride = ActiveLanguageTag;
        CultureInfo culture = CultureInfo.GetCultureInfo(ActiveLanguageTag);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }
}

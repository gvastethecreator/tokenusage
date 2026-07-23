using System.Globalization;
using WOpenUsage.App.Localization;

namespace WOpenUsage.Architecture.Tests;

public sealed class AppLanguageCatalogTests
{
    [Fact]
    public void SupportedLanguageTagsExposeOnlyEnglishAndSpanish()
    {
        Assert.Equal(
            [AppLanguageCatalog.EnglishUnitedStates, AppLanguageCatalog.SpanishSpain],
            AppLanguageCatalog.SupportedLanguageTags);
        Assert.IsType<System.Collections.ObjectModel.ReadOnlyCollection<string>>(
            AppLanguageCatalog.SupportedLanguageTags);
    }

    [Theory]
    [InlineData("en-US", "en-US")]
    [InlineData("en", "en-US")]
    [InlineData("EN", "en-US")]
    [InlineData("en-GB", "en-US")]
    [InlineData("es-ES", "es-ES")]
    [InlineData("es", "es-ES")]
    [InlineData("ES", "es-ES")]
    [InlineData("es-MX", "es-ES")]
    public void ResolveLanguageTagMapsSupportedLanguageFamilies(
        string input,
        string expected)
    {
        Assert.Equal(expected, AppLanguageCatalog.ResolveLanguageTag(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("en_US")]
    [InlineData("es_ES")]
    [InlineData("fr-FR")]
    [InlineData("!!!")]
    [InlineData("-es")]
    [InlineData("es-")]
    [InlineData("es ES")]
    public void ResolveLanguageTagFallsBackForInvalidOrUnsupportedTags(string? input)
    {
        Assert.Equal(
            AppLanguageCatalog.EnglishUnitedStates,
            AppLanguageCatalog.ResolveLanguageTag(input));
    }

    [Theory]
    [InlineData("en-GB", "en-US", ".")]
    [InlineData("es-MX", "es-ES", ",")]
    [InlineData("fr-FR", "en-US", ".")]
    public void GetCultureReturnsSafeSupportedCulture(
        string input,
        string expectedName,
        string expectedDecimalSeparator)
    {
        CultureInfo culture = AppLanguageCatalog.GetCulture(input);

        Assert.Equal(expectedName, culture.Name);
        Assert.Equal(expectedDecimalSeparator, culture.NumberFormat.NumberDecimalSeparator);
        Assert.True(culture.IsReadOnly);
    }

    [Theory]
    [InlineData("es-ES", new[] { "en-US" }, "es-ES")]
    [InlineData(null, new[] { "es-MX", "en-US" }, "es-ES")]
    [InlineData(null, new[] { "fr-FR", "es-ES" }, "en-US")]
    [InlineData(null, new string[0], "en-US")]
    public void ResolveStartupLanguageTagPrefersTheSavedOverrideThenMapsTheSystemLanguage(
        string? primaryLanguageOverride,
        IReadOnlyList<string> preferredLanguages,
        string expected)
    {
        Assert.Equal(
            expected,
            AppLanguageCatalog.ResolveStartupLanguageTag(primaryLanguageOverride, preferredLanguages));
    }
}

using System.Globalization;
using TokenUsage.App.Localization;

namespace TokenUsage.Architecture.Tests;

public sealed class AppLanguageCatalogTests
{
    [Fact]
    public void SupportedLanguageTagsExposeOnlyEnglish()
    {
        Assert.Equal(
            [AppLanguageCatalog.EnglishUnitedStates],
            AppLanguageCatalog.SupportedLanguageTags);
    }

    [Theory]
    [InlineData("en-US", "en-US")]
    [InlineData("en", "en-US")]
    [InlineData("EN", "en-US")]
    [InlineData("en-GB", "en-US")]
    [InlineData("es-ES", "en-US")]
    [InlineData("fr-FR", "en-US")]
    public void ResolveLanguageTagAlwaysUsesEnglish(
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
    [InlineData("es-MX", "en-US", ".")]
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
    [InlineData("es-ES", new[] { "en-US" }, "en-US")]
    [InlineData(null, new[] { "es-MX", "en-US" }, "en-US")]
    [InlineData(null, new[] { "fr-FR", "es-ES" }, "en-US")]
    [InlineData(null, new string[0], "en-US")]
    public void ResolveStartupLanguageTagAlwaysUsesEnglish(
        string? primaryLanguageOverride,
        IReadOnlyList<string> preferredLanguages,
        string expected)
    {
        Assert.Equal(
            expected,
            AppLanguageCatalog.ResolveStartupLanguageTag(primaryLanguageOverride, preferredLanguages));
    }
}

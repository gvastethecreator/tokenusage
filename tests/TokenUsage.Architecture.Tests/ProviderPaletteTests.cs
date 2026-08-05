using System.Xml.Linq;

namespace TokenUsage.Architecture.Tests;

public sealed class ProviderPaletteTests
{
    private static readonly string[] ProviderKeys =
    [
        "Antigravity",
        "Claude",
        "Codex",
        "Grok",
        "OpenCode",
    ];

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void ProviderBrushesUseDarkToBaseDiagonalGradients(string theme)
    {
        string repoRoot = ProjectReferenceGraph.FindRepoRoot();
        XDocument resources = XDocument.Load(
            Path.Combine(repoRoot, "src", "TokenUsage.App", "App.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        XElement dictionary = FindThemeDictionary(resources, theme, xaml, x);

        foreach (string provider in ProviderKeys)
        {
            XElement? brush = dictionary
                .Elements(xaml + "LinearGradientBrush")
                .SingleOrDefault(element =>
                    string.Equals(
                        (string?)element.Attribute(x + "Key"),
                        $"Provider{provider}Brush",
                        StringComparison.Ordinal));

            Assert.NotNull(brush);
            Assert.Equal("RelativeToBoundingBox", (string?)brush.Attribute("MappingMode"));
            Assert.Equal("0,0", (string?)brush.Attribute("StartPoint"));
            Assert.Equal("1,1", (string?)brush.Attribute("EndPoint"));

            XElement[] stops = brush.Elements(xaml + "GradientStop").ToArray();
            Assert.Equal(2, stops.Length);
            Assert.Equal("0", (string?)stops[0].Attribute("Offset"));
            Assert.Equal("1", (string?)stops[1].Attribute("Offset"));
            Assert.Equal(
                $"{{StaticResource Provider{provider}ColorDark}}",
                (string?)stops[0].Attribute("Color"));
            Assert.Equal(
                $"{{StaticResource Provider{provider}Color}}",
                (string?)stops[1].Attribute("Color"));
        }
    }

    [Fact]
    public void HighContrastUsesSystemBrushesWithoutGradients()
    {
        string repoRoot = ProjectReferenceGraph.FindRepoRoot();
        XDocument resources = XDocument.Load(
            Path.Combine(repoRoot, "src", "TokenUsage.App", "App.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement dictionary = FindThemeDictionary(resources, "HighContrast", xaml, x);
        Assert.Empty(dictionary.Elements(xaml + "LinearGradientBrush"));

        foreach (string provider in ProviderKeys)
        {
            AssertResourceRedirect(
                dictionary,
                $"Provider{provider}Brush",
                "SystemColorHighlightColorBrush",
                xaml,
                x);
        }

        AssertResourceRedirect(
            dictionary,
            "ProviderFallbackBrush",
            "SystemColorWindowTextColorBrush",
            xaml,
            x);
        AssertResourceRedirect(
            dictionary,
            "ProviderDonutShadowBrush",
            "SystemColorWindowTextColorBrush",
            xaml,
            x);
    }

    [Fact]
    public void DonutShadowIsThemeSpecificAndSubtle()
    {
        string repoRoot = ProjectReferenceGraph.FindRepoRoot();
        XDocument resources = XDocument.Load(
            Path.Combine(repoRoot, "src", "TokenUsage.App", "App.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        Assert.Equal(
            "#24000000",
            FindBrushColor(resources, "Light", "ProviderDonutShadowBrush", xaml, x));
        Assert.Equal(
            "#30000000",
            FindBrushColor(resources, "Dark", "ProviderDonutShadowBrush", xaml, x));
    }

    [Theory]
    [InlineData("Light", "1", "Visible", "Collapsed")]
    [InlineData("Dark", "1", "Visible", "Collapsed")]
    [InlineData("HighContrast", "0", "Collapsed", "Visible")]
    public void ThemeControlsShadowAndProviderMarkVisibility(
        string theme,
        string shadowOpacity,
        string brandVisibility,
        string highContrastVisibility)
    {
        string repoRoot = ProjectReferenceGraph.FindRepoRoot();
        XDocument resources = XDocument.Load(
            Path.Combine(repoRoot, "src", "TokenUsage.App", "App.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement dictionary = FindThemeDictionary(resources, theme, xaml, x);

        XElement opacity = dictionary
            .Elements(x + "Double")
            .Single(element =>
                string.Equals(
                    (string?)element.Attribute(x + "Key"),
                    "ProviderDonutShadowOpacity",
                    StringComparison.Ordinal));
        Assert.Equal(shadowOpacity, opacity.Value);
        AssertVisibility(dictionary, "ProviderBrandMarkVisibility", brandVisibility, xaml, x);
        AssertVisibility(
            dictionary,
            "ProviderHighContrastMarkVisibility",
            highContrastVisibility,
            xaml,
            x);
    }

    [Fact]
    public void ProviderMarksMatchTheirBaseColors()
    {
        string repoRoot = ProjectReferenceGraph.FindRepoRoot();
        XDocument resources = XDocument.Load(
            Path.Combine(repoRoot, "src", "TokenUsage.App", "App.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        IReadOnlyDictionary<string, string> assetNames = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Antigravity"] = "antigravity.svg",
            ["Claude"] = "claude.svg",
            ["Codex"] = "codex.svg",
            ["Grok"] = "grok.svg",
            ["OpenCode"] = "opencode.svg",
        };

        foreach ((string provider, string assetName) in assetNames)
        {
            XElement color = resources
                .Descendants(xaml + "Color")
                .Single(element =>
                    string.Equals(
                        (string?)element.Attribute(x + "Key"),
                        $"Provider{provider}Color",
                        StringComparison.Ordinal));
            string expectedFill = $"#{color.Value[3..]}";
            XDocument mark = XDocument.Load(
                Path.Combine(
                    repoRoot,
                    "src",
                    "TokenUsage.App",
                    "Assets",
                    "ProviderMarks",
                    assetName));
            string[] fills = mark
                .Descendants()
                .Attributes("fill")
                .Select(attribute => attribute.Value)
                .Where(value => !string.Equals(value, "none", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            Assert.Equal([expectedFill], fills);
        }
    }

    private static XElement FindThemeDictionary(
        XDocument resources,
        string theme,
        XNamespace xaml,
        XNamespace x) =>
        resources
            .Descendants(xaml + "ResourceDictionary")
            .Single(element =>
                string.Equals((string?)element.Attribute(x + "Key"), theme, StringComparison.Ordinal));

    private static string? FindBrushColor(
        XDocument resources,
        string theme,
        string resourceKey,
        XNamespace xaml,
        XNamespace x) =>
        (string?)FindThemeDictionary(resources, theme, xaml, x)
            .Elements(xaml + "SolidColorBrush")
            .Single(element =>
                string.Equals((string?)element.Attribute(x + "Key"), resourceKey, StringComparison.Ordinal))
            .Attribute("Color");

    private static void AssertResourceRedirect(
        XElement dictionary,
        string resourceKey,
        string expectedTarget,
        XNamespace xaml,
        XNamespace x)
    {
        XElement redirect = dictionary
            .Elements(xaml + "StaticResource")
            .Single(element =>
                string.Equals((string?)element.Attribute(x + "Key"), resourceKey, StringComparison.Ordinal));

        Assert.Equal(expectedTarget, (string?)redirect.Attribute("ResourceKey"));
    }

    private static void AssertVisibility(
        XElement dictionary,
        string resourceKey,
        string expectedValue,
        XNamespace xaml,
        XNamespace x)
    {
        XElement visibility = dictionary
            .Elements(xaml + "Visibility")
            .Single(element =>
                string.Equals((string?)element.Attribute(x + "Key"), resourceKey, StringComparison.Ordinal));

        Assert.Equal(expectedValue, visibility.Value);
    }
}

using System.Xml.Linq;

namespace WOpenUsage.Architecture.Tests;

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

    [Fact]
    public void ProviderBrushesUseDarkToBaseDiagonalGradients()
    {
        string repoRoot = ProjectReferenceGraph.FindRepoRoot();
        XDocument resources = XDocument.Load(
            Path.Combine(repoRoot, "src", "WOpenUsage.App", "App.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        foreach (string provider in ProviderKeys)
        {
            XElement? brush = resources
                .Descendants(xaml + "LinearGradientBrush")
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

    [Theory]
    [InlineData("ProviderFallbackBrush", "{ThemeResource TextFillColorSecondary}")]
    [InlineData("ProviderHighContrastBrush", "{ThemeResource SystemColorHighlightColor}")]
    public void SystemFallbackBrushesRemainSolid(string resourceKey, string expectedColor)
    {
        string repoRoot = ProjectReferenceGraph.FindRepoRoot();
        XDocument resources = XDocument.Load(
            Path.Combine(repoRoot, "src", "WOpenUsage.App", "App.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement? brush = resources
            .Descendants(xaml + "SolidColorBrush")
            .SingleOrDefault(element =>
                string.Equals(
                    (string?)element.Attribute(x + "Key"),
                    resourceKey,
                    StringComparison.Ordinal));

        Assert.NotNull(brush);
        Assert.Equal(expectedColor, (string?)brush.Attribute("Color"));
    }

    [Fact]
    public void DonutShadowRemainsSubtleAndFixed()
    {
        string repoRoot = ProjectReferenceGraph.FindRepoRoot();
        XDocument resources = XDocument.Load(
            Path.Combine(repoRoot, "src", "WOpenUsage.App", "App.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement brush = resources
            .Descendants(xaml + "SolidColorBrush")
            .Single(element =>
                string.Equals(
                    (string?)element.Attribute(x + "Key"),
                    "ProviderDonutShadowBrush",
                    StringComparison.Ordinal));

        Assert.Equal("#30000000", (string?)brush.Attribute("Color"));
    }

    [Fact]
    public void ProviderMarksMatchTheirBaseColors()
    {
        string repoRoot = ProjectReferenceGraph.FindRepoRoot();
        XDocument resources = XDocument.Load(
            Path.Combine(repoRoot, "src", "WOpenUsage.App", "App.xaml"));
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
                    "WOpenUsage.App",
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
}

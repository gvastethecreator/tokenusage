using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace TokenUsage.Architecture.Tests;

public sealed partial class LocalizationContractTests
{
    [Fact]
    public void EnglishAndSpanishResourcesHaveExactNonBlankKeyParity()
    {
        ResourceSet english = LoadResources("en-US");
        ResourceSet spanish = LoadResources("es-ES");

        Assert.Equal(english.Values.Keys.Order(), spanish.Values.Keys.Order());
        Assert.DoesNotContain(english.Values, pair => string.IsNullOrWhiteSpace(pair.Value));
        Assert.DoesNotContain(spanish.Values, pair => string.IsNullOrWhiteSpace(pair.Value));
        Assert.DoesNotContain(english.Values.Values, value => value.Contains("WOpenUsage", StringComparison.Ordinal));
        Assert.DoesNotContain(spanish.Values.Values, value => value.Contains("WOpenUsage", StringComparison.Ordinal));
    }

    [Fact]
    public void EnglishAndSpanishFormatPlaceholdersMatch()
    {
        ResourceSet english = LoadResources("en-US");
        ResourceSet spanish = LoadResources("es-ES");

        foreach (string key in english.Values.Keys)
        {
            Assert.Equal(
                PlaceholderIndexes(english.Values[key]),
                PlaceholderIndexes(spanish.Values[key]));
        }
    }

    [Fact]
    public void EveryXamlUidHasResourcesInBothLanguages()
    {
        string appRoot = AppRoot();
        ResourceSet english = LoadResources("en-US");
        ResourceSet spanish = LoadResources("es-ES");
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        string[] uids = Directory
            .EnumerateFiles(appRoot, "*.xaml", SearchOption.AllDirectories)
            .Where(path => !IsGeneratedPath(path))
            .SelectMany(path => XDocument.Load(path)
                .Descendants()
                .Attributes(x + "Uid")
                .Select(attribute => attribute.Value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        foreach (string uid in uids)
        {
            Assert.Contains(english.Values.Keys, key => key.StartsWith($"{uid}.", StringComparison.Ordinal));
            Assert.Contains(spanish.Values.Keys, key => key.StartsWith($"{uid}.", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void LiteralResourceLookupsExistInBothLanguages()
    {
        string appRoot = AppRoot();
        ResourceSet english = LoadResources("en-US");
        ResourceSet spanish = LoadResources("es-ES");
        HashSet<string> referencedKeys = [];

        foreach (string path in Directory.EnumerateFiles(appRoot, "*.cs", SearchOption.AllDirectories)
                     .Where(path => !IsGeneratedPath(path)))
        {
            string source = File.ReadAllText(path);
            foreach (Match match in DirectResourceCallPattern().Matches(source))
            {
                referencedKeys.Add(match.Groups["key"].Value);
            }

            foreach (Match call in GetStringExpressionPattern().Matches(source))
            {
                foreach (Match literal in StringLiteralPattern().Matches(call.Groups["body"].Value))
                {
                    referencedKeys.Add(literal.Groups["key"].Value);
                }
            }
        }

        Assert.NotEmpty(referencedKeys);
        foreach (string key in referencedKeys)
        {
            Assert.True(english.Values.ContainsKey(key), $"Missing en-US resource: {key}");
            Assert.True(spanish.Values.ContainsKey(key), $"Missing es-ES resource: {key}");
        }
    }

    [Fact]
    public void PackageManifestDeclaresOnlyTheSupportedLanguages()
    {
        XNamespace package = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
        XDocument manifest = XDocument.Load(Path.Combine(PackageRoot(), "Package.appxmanifest"));
        string[] languages = manifest
            .Descendants(package + "Resource")
            .Select(element => (string?)element.Attribute("Language"))
            .OfType<string>()
            .ToArray();

        Assert.Equal(["en-US", "es-ES"], languages);
    }

    private static ResourceSet LoadResources(string languageTag)
    {
        string path = Path.Combine(AppRoot(), "Strings", languageTag, "Resources.resw");
        Dictionary<string, string> values = XDocument.Load(path)
            .Root!
            .Elements("data")
            .ToDictionary(
                element => (string)element.Attribute("name")!,
                element => element.Element("value")?.Value ?? string.Empty,
                StringComparer.Ordinal);
        return new ResourceSet(values);
    }

    private static int[] PlaceholderIndexes(string value) => PlaceholderPattern()
        .Matches(value)
        .Select(match => int.Parse(match.Groups["index"].Value, System.Globalization.CultureInfo.InvariantCulture))
        .Distinct()
        .Order()
        .ToArray();

    private static string AppRoot() => Path.Combine(
        ProjectReferenceGraph.FindRepoRoot(),
        "src",
        "TokenUsage.App");

    private static string PackageRoot() => Path.Combine(
        ProjectReferenceGraph.FindRepoRoot(),
        "src",
        "TokenUsage.Package");

    private static bool IsGeneratedPath(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
        || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

    private sealed record ResourceSet(IReadOnlyDictionary<string, string> Values);

    [GeneratedRegex("\\{(?<index>[0-9]+)(?:[^}]*)\\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderPattern();

    [GeneratedRegex("(?:GetString|text)\\s*\\(\\s*\\\"(?<key>[A-Za-z][A-Za-z0-9.]*)\\\"|Format\\s*\\(\\s*(?:text|getString)\\s*,\\s*\\\"(?<key>[A-Za-z][A-Za-z0-9.]*)\\\"", RegexOptions.CultureInvariant)]
    private static partial Regex DirectResourceCallPattern();

    [GeneratedRegex("GetString\\s*\\((?<body>[^;]{1,4000}?)\\)", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex GetStringExpressionPattern();

    [GeneratedRegex("\\\"(?<key>[A-Za-z][A-Za-z0-9.]*)\\\"", RegexOptions.CultureInvariant)]
    private static partial Regex StringLiteralPattern();
}

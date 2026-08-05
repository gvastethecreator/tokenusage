using System.Xml.Linq;

namespace WOpenUsage.Architecture.Tests;

public sealed class PackagingContractTests
{
    private static readonly XNamespace MsBuild = "http://schemas.microsoft.com/developer/msbuild/2003";
    private static readonly XNamespace Package = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
    private static readonly XNamespace Uap5 = "http://schemas.microsoft.com/appx/manifest/uap/windows10/5";

    [Fact]
    public void PackagingProjectIncludesOnlySupportedPlatformsAndBothExecutables()
    {
        XDocument project = XDocument.Load(PackagePath("WOpenUsage.Package.wapproj"));
        string[] configurations = project
            .Descendants(MsBuild + "ProjectConfiguration")
            .Select(element => (string?)element.Attribute("Include"))
            .OfType<string>()
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        string[] references = project
            .Descendants(MsBuild + "ProjectReference")
            .Select(element => ((string?)element.Attribute("Include"))?.Replace('/', '\\'))
            .OfType<string>()
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            ["Debug|ARM64", "Debug|x64", "Release|ARM64", "Release|x64"],
            configurations);
        Assert.Equal(
            [
                "..\\WOpenUsage.App\\WOpenUsage.App.csproj",
                "..\\WOpenUsage.Cli\\WOpenUsage.Cli.csproj",
            ],
            references);
        Assert.Equal(
            "..\\WOpenUsage.App\\WOpenUsage.App.csproj",
            project.Descendants(MsBuild + "EntryPointProjectUniqueName").Single().Value);
    }

    [Fact]
    public void ManifestRegistersConsoleAliasWithoutChangingPackageIdentity()
    {
        XDocument manifest = XDocument.Load(PackagePath("Package.appxmanifest"));
        XElement identity = manifest.Root!.Element(Package + "Identity")!;
        XElement application = manifest
            .Descendants(Package + "Application")
            .Single(element => (string?)element.Attribute("Id") == "App");
        XElement extension = application
            .Descendants(Uap5 + "Extension")
            .Single(element => (string?)element.Attribute("Category") == "windows.appExecutionAlias");
        XElement alias = extension.Descendants(Uap5 + "ExecutionAlias").Single();

        Assert.Equal("D6C94EDD-3747-465C-9A81-05DF5A4108C5", (string?)identity.Attribute("Name"));
        Assert.Equal("CN=AppPublisher", (string?)identity.Attribute("Publisher"));
        Assert.Equal("1.0.0.0", (string?)identity.Attribute("Version"));
        Assert.Equal("WOpenUsage.App.exe", (string?)application.Attribute("Executable"));
        Assert.Equal("Windows.FullTrustApplication", (string?)application.Attribute("EntryPoint"));
        Assert.Equal("WOpenUsage.Cli\\wusage.exe", (string?)extension.Attribute("Executable"));
        Assert.Equal("Windows.FullTrustApplication", (string?)extension.Attribute("EntryPoint"));
        Assert.Equal("wusage.exe", (string?)alias.Attribute("Alias"));
    }

    [Fact]
    public void AppDoesNotOwnMsixAndCliEmitsWusage()
    {
        string repoRoot = ProjectReferenceGraph.FindRepoRoot();
        XDocument app = XDocument.Load(Path.Combine(
            repoRoot,
            "src",
            "WOpenUsage.App",
            "WOpenUsage.App.csproj"));
        XDocument cli = XDocument.Load(Path.Combine(
            repoRoot,
            "src",
            "WOpenUsage.Cli",
            "WOpenUsage.Cli.csproj"));

        Assert.Empty(app.Descendants("EnableMsixTooling"));
        Assert.Empty(app.Descendants("AppxManifest"));
        Assert.Equal("WinExe", app.Descendants("OutputType").Single().Value);
        Assert.Equal("wusage", cli.Descendants("AssemblyName").Single().Value);
    }

    private static string PackagePath(string fileName) => Path.Combine(
        ProjectReferenceGraph.FindRepoRoot(),
        "src",
        "WOpenUsage.Package",
        fileName);
}

using System.Xml.Linq;

namespace TokenUsage.Architecture.Tests;

public sealed class PackagingContractTests
{
    private static readonly XNamespace MsBuild = "http://schemas.microsoft.com/developer/msbuild/2003";
    private static readonly XNamespace Package = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
    private static readonly XNamespace Uap5 = "http://schemas.microsoft.com/appx/manifest/uap/windows10/5";
    private static readonly XNamespace Assembly = "urn:schemas-microsoft-com:asm.v1";

    [Fact]
    public void PackagingProjectIncludesOnlySupportedPlatformsAndBothExecutables()
    {
        XDocument project = XDocument.Load(PackagePath("TokenUsage.Package.wapproj"));
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
                "..\\TokenUsage.App\\TokenUsage.App.csproj",
                "..\\TokenUsage.Cli\\TokenUsage.Cli.csproj",
            ],
            references);
        Assert.Equal(
            "..\\TokenUsage.App\\TokenUsage.App.csproj",
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

        Assert.Equal("GVASTETHECREATOR.TokenUsage", (string?)identity.Attribute("Name"));
        Assert.Equal("CN=DB97CC4C-CCCD-41DF-8D43-C67641CBBC92", (string?)identity.Attribute("Publisher"));
        Assert.Equal("0.0.1.0", (string?)identity.Attribute("Version"));
        Assert.Equal("TokenUsage.App.exe", (string?)application.Attribute("Executable"));
        Assert.Equal("Windows.FullTrustApplication", (string?)application.Attribute("EntryPoint"));
        Assert.Equal("TokenUsage.Cli\\tokenusage.exe", (string?)extension.Attribute("Executable"));
        Assert.Equal("Windows.FullTrustApplication", (string?)extension.Attribute("EntryPoint"));
        Assert.Equal("tokenusage.exe", (string?)alias.Attribute("Alias"));
    }

    [Fact]
    public void AppUsesTargetRuntimesWithoutOwningMsixAndCliEmitsTokenUsage()
    {
        string repoRoot = ProjectReferenceGraph.FindRepoRoot();
        XDocument app = XDocument.Load(Path.Combine(
            repoRoot,
            "src",
            "TokenUsage.App",
            "TokenUsage.App.csproj"));
        XDocument cli = XDocument.Load(Path.Combine(
            repoRoot,
            "src",
            "TokenUsage.Cli",
            "TokenUsage.Cli.csproj"));

        Assert.Empty(app.Descendants("EnableMsixTooling"));
        Assert.Empty(app.Descendants("AppxManifest"));
        Assert.Equal("WinExe", app.Descendants("OutputType").Single().Value);
        foreach ((string platform, string runtime) in new[] { ("x64", "win-x64"), ("ARM64", "win-arm64") })
        {
            XElement targetRuntime = Assert.Single(app.Descendants("RuntimeIdentifier"),
                element => ((string?)element.Attribute("Condition"))?.Contains(
                    $"'$(Platform)' == '{platform}'", StringComparison.Ordinal) == true);
            Assert.Equal(runtime, targetRuntime.Value);
        }
        Assert.Equal("tokenusage", cli.Descendants("AssemblyName").Single().Value);
    }

    [Fact]
    public void ProductAndWindowsManifestsUseTheSameReleaseVersion()
    {
        string repoRoot = ProjectReferenceGraph.FindRepoRoot();
        XDocument buildProperties = XDocument.Load(Path.Combine(repoRoot, "Directory.Build.props"));
        XDocument appManifest = XDocument.Load(Path.Combine(
            repoRoot,
            "src",
            "TokenUsage.App",
            "app.manifest"));
        XDocument packageManifest = XDocument.Load(PackagePath("Package.appxmanifest"));

        Assert.Equal("0.0.1", buildProperties.Descendants("Version").Single().Value);
        Assert.Equal("0.0.1.0", buildProperties.Descendants("AssemblyVersion").Single().Value);
        Assert.Equal("0.0.1.0", buildProperties.Descendants("FileVersion").Single().Value);
        Assert.Equal("0.0.1", buildProperties.Descendants("InformationalVersion").Single().Value);
        Assert.Equal(
            "false",
            buildProperties.Descendants("IncludeSourceRevisionInInformationalVersion").Single().Value);
        Assert.Equal(
            "0.0.1.0",
            (string?)appManifest.Root!.Element(Assembly + "assemblyIdentity")!.Attribute("version"));
        Assert.Equal(
            "0.0.1.0",
            (string?)packageManifest.Root!.Element(Package + "Identity")!.Attribute("Version"));
    }

    [Theory]
    [InlineData("portable-x64.pubxml", "x64", "win-x64")]
    [InlineData("portable-arm64.pubxml", "ARM64", "win-arm64")]
    public void PortableProfilesAreUnpackagedAndSelfContained(
        string profileName,
        string platform,
        string runtimeIdentifier)
    {
        string repoRoot = ProjectReferenceGraph.FindRepoRoot();
        XDocument profile = XDocument.Load(Path.Combine(
            repoRoot,
            "src",
            "TokenUsage.App",
            "Properties",
            "PublishProfiles",
            profileName));

        Assert.Equal(platform, profile.Descendants(MsBuild + "Platform").Single().Value);
        Assert.Equal(
            runtimeIdentifier,
            profile.Descendants(MsBuild + "RuntimeIdentifier").Single().Value);
        Assert.Equal("None", profile.Descendants(MsBuild + "WindowsPackageType").Single().Value);
        Assert.Equal("true", profile.Descendants(MsBuild + "EnableMsixTooling").Single().Value);
        Assert.Equal(
            "true",
            profile.Descendants(MsBuild + "WindowsAppSDKSelfContained").Single().Value);
        Assert.Equal("true", profile.Descendants(MsBuild + "SelfContained").Single().Value);
        Assert.Equal("false", profile.Descendants(MsBuild + "PublishSingleFile").Single().Value);
        Assert.Equal("false", profile.Descendants(MsBuild + "PublishTrimmed").Single().Value);
    }

    [Fact]
    public void PortableApplicationPublishesRuntimeAssets()
    {
        string repoRoot = ProjectReferenceGraph.FindRepoRoot();
        XDocument appProject = XDocument.Load(Path.Combine(
            repoRoot,
            "src",
            "TokenUsage.App",
            "TokenUsage.App.csproj"));
        XElement assets = appProject
            .Descendants("Content")
            .Single(element => (string?)element.Attribute("Update") == @"Assets\**\*");

        Assert.Equal("PreserveNewest", (string?)assets.Attribute("CopyToOutputDirectory"));
        Assert.Equal("PreserveNewest", (string?)assets.Attribute("CopyToPublishDirectory"));
    }

    private static string PackagePath(string fileName) => Path.Combine(
        ProjectReferenceGraph.FindRepoRoot(),
        "src",
        "TokenUsage.Package",
        fileName);
}

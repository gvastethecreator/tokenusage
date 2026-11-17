using System.Xml.Linq;

namespace TokenUsage.Architecture.Tests;

public sealed class PackagingContractTests
{
    private static readonly XNamespace MsBuild = "http://schemas.microsoft.com/developer/msbuild/2003";
    private static readonly XNamespace Package = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
    private static readonly XNamespace Uap5 = "http://schemas.microsoft.com/appx/manifest/uap/windows10/5";
    private static readonly XNamespace Com = "http://schemas.microsoft.com/appx/manifest/com/windows10";
    private static readonly XNamespace Desktop = "http://schemas.microsoft.com/appx/manifest/desktop/windows10";
    private static readonly XNamespace Assembly = "urn:schemas-microsoft-com:asm.v1";

    [Fact]
    public void SolutionListsX64BeforeArm64ForLocalDotnetBuild()
    {
        string repoRoot = ProjectReferenceGraph.FindRepoRoot();
        string slnx = File.ReadAllText(Path.Combine(repoRoot, "TokenUsage.slnx"));
        int namedX64 = slnx.IndexOf("<Platform Name=\"x64\"", StringComparison.Ordinal);
        int namedArm64 = slnx.IndexOf("<Platform Name=\"ARM64\"", StringComparison.Ordinal);
        int mappedX64 = slnx.IndexOf("Solution=\"*|x64\"", StringComparison.Ordinal);
        int mappedArm64 = slnx.IndexOf("Solution=\"*|ARM64\"", StringComparison.Ordinal);
        Assert.True(namedX64 >= 0 && namedArm64 >= 0, "TokenUsage.slnx must declare x64 and ARM64.");
        Assert.True(namedX64 < namedArm64, "x64 must be the first solution platform.");
        Assert.True(mappedX64 >= 0 && mappedArm64 >= 0, "TokenUsage.slnx must map x64 and ARM64.");
        Assert.True(
            mappedX64 < mappedArm64,
            "x64 project mappings must come first so `dotnet build TokenUsage.slnx` does not pick ARM64.");

        XDocument solutionProps = XDocument.Load(Path.Combine(repoRoot, "Directory.Solution.props"));
        Assert.Contains(
            solutionProps.Descendants("Platform"),
            element => element.Value == "x64"
                && (element.Attribute("Condition")?.Value.Contains("$(Platform)", StringComparison.Ordinal) ?? false));
    }

    [Fact]
    public void ManifestRegistersBothExecutablesAndToastActivationWithoutChangingIdentity()
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
        Assert.Equal("TokenUsage.App\\TokenUsage.App.exe", (string?)application.Attribute("Executable"));
        Assert.Equal("Windows.FullTrustApplication", (string?)application.Attribute("EntryPoint"));
        Assert.Equal("TokenUsage.Cli\\tokenusage.exe", (string?)extension.Attribute("Executable"));
        Assert.Equal("Windows.FullTrustApplication", (string?)extension.Attribute("EntryPoint"));
        Assert.Equal("tokenusage.exe", (string?)alias.Attribute("Alias"));

        XElement server = application.Descendants(Com + "ExeServer").Single();
        XElement toast = application.Descendants(Desktop + "ToastNotificationActivation").Single();
        Assert.Equal((string?)application.Attribute("Executable"), (string?)server.Attribute("Executable"));
        Assert.Equal("----AppNotificationActivated:", (string?)server.Attribute("Arguments"));
        Assert.Equal("7B30C8A5-0C11-4FB5-94FA-59FD9154B798", (string?)toast.Attribute("ToastActivatorCLSID"));
        Assert.Equal((string?)toast.Attribute("ToastActivatorCLSID"),
            (string?)server.Element(Com + "Class")!.Attribute("Id"));
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

        string version = buildProperties.Descendants("Version").Single().Value;
        string windowsVersion = $"{version}.0";
        Assert.Matches(@"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$", version);
        Assert.All(version.Split('.'), component => Assert.InRange(int.Parse(component,
            System.Globalization.CultureInfo.InvariantCulture), 0, ushort.MaxValue));
        Assert.Equal(windowsVersion, buildProperties.Descendants("AssemblyVersion").Single().Value);
        Assert.Equal(windowsVersion, buildProperties.Descendants("FileVersion").Single().Value);
        Assert.Equal(version, buildProperties.Descendants("InformationalVersion").Single().Value);
        Assert.Equal(
            "false",
            buildProperties.Descendants("IncludeSourceRevisionInInformationalVersion").Single().Value);
        Assert.Equal(
            windowsVersion,
            (string?)appManifest.Root!.Element(Assembly + "assemblyIdentity")!.Attribute("version"));
        Assert.Equal(
            windowsVersion,
            (string?)packageManifest.Root!.Element(Package + "Identity")!.Attribute("Version"));
    }

    [Theory]
    [InlineData("portable-x64.pubxml", "x64", "win-x64")]
    [InlineData("portable-arm64.pubxml", "ARM64", "win-arm64")]
    public void PortableProfilesUseUnpackagedWinUiAndOnePrivateDotNetRuntime(
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
        Assert.Equal("false", profile.Descendants(MsBuild + "SelfContained").Single().Value);
        Assert.Equal("AppRelative", profile.Descendants(MsBuild + "AppHostDotNetSearch").Single().Value);
        Assert.Equal(".dotnet", profile.Descendants(MsBuild + "AppHostRelativeDotNet").Single().Value);
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

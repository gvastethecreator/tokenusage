using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using TokenUsage.Runtime.Windows.Updates;

namespace TokenUsage.Platform.Windows.Tests;

public sealed class MsixUpdateInstallerTests : IDisposable
{
    private const string PackageName = "GVASTETHECREATOR.TokenUsage";
    private const string Publisher = "CN=TokenUsage updater test";
    private static readonly Version ReleaseVersion = new(1, 2, 3);
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "TokenUsage.MsixUpdate.Tests", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(".msix", "x64")]
    [InlineData(".appx", "arm64")]
    [InlineData(".msixbundle", "x64")]
    [InlineData(".appxbundle", "arm64")]
    public void AcceptsMatchingPackageMetadataWithoutDeploying(string extension, string architecture)
    {
        XElement manifest = extension.EndsWith("bundle", StringComparison.Ordinal)
            ? BundleManifest(architecture, "application") : PackageManifest(architecture);
        string path = CreatePackage(manifest, extension);

        MsixUpdateInstaller.ValidatePackage(path, PackageName, Publisher, ReleaseVersion, architecture);
    }

    [Theory]
    [InlineData("Name", "Another.Package")]
    [InlineData("Publisher", "CN=Another publisher")]
    [InlineData("Version", "1.2.4.0")]
    [InlineData("ProcessorArchitecture", "arm64")]
    public void RejectsPackageIdentityThatDoesNotMatchTheSelectedUpdate(string attribute, string value)
    {
        XElement manifest = PackageManifest("x64");
        manifest.Elements().Single().SetAttributeValue(attribute, value);
        string path = CreatePackage(manifest);

        Assert.Throws<InvalidDataException>(() =>
            MsixUpdateInstaller.ValidatePackage(path, PackageName, Publisher, ReleaseVersion, "x64"));
    }

    [Theory]
    [InlineData("arm64", "application")]
    [InlineData("x64", "resource")]
    public void RejectsBundleWithoutAnApplicationForTheInstalledArchitecture(string architecture, string type)
    {
        string path = CreatePackage(BundleManifest(architecture, type), ".msixbundle");

        Assert.Throws<InvalidDataException>(() =>
            MsixUpdateInstaller.ValidatePackage(path, PackageName, Publisher, ReleaseVersion, "x64"));
    }

    [Fact]
    public void RejectsPackageWithoutASignatureEntry()
    {
        string path = CreatePackage(PackageManifest("x64"), withSignature: false);

        Assert.Throws<InvalidDataException>(() =>
            MsixUpdateInstaller.ValidatePackage(path, PackageName, Publisher, ReleaseVersion, "x64"));
    }

    [Fact]
    public void RejectsXmlWithADocumentTypeDeclaration()
    {
        string path = CreatePackage(PackageManifest("x64"),
            prefix: "<!DOCTYPE Package [<!ENTITY marker 'unsafe'>]>");

        Assert.Throws<XmlException>(() =>
            MsixUpdateInstaller.ValidatePackage(path, PackageName, Publisher, ReleaseVersion, "x64"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private static XElement PackageManifest(string architecture)
    {
        XNamespace ns = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
        return new XElement(ns + "Package", Identity(ns,
            new XAttribute("ProcessorArchitecture", architecture)));
    }

    private static XElement BundleManifest(string architecture, string type)
    {
        XNamespace ns = "http://schemas.microsoft.com/appx/2013/bundle";
        return new XElement(ns + "Bundle", Identity(ns),
            new XElement(ns + "Packages", new XElement(ns + "Package",
                new XAttribute("Type", type), new XAttribute("Architecture", architecture))));
    }

    private static XElement Identity(XNamespace ns, params XAttribute[] extraAttributes) =>
        new(ns + "Identity", new XAttribute("Name", PackageName), new XAttribute("Publisher", Publisher),
            new XAttribute("Version", "1.2.3.0"), extraAttributes);

    private string CreatePackage(XElement manifest, string extension = ".msix", bool withSignature = true, string prefix = "")
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "candidate" + extension);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        string manifestName = extension.EndsWith("bundle", StringComparison.Ordinal)
            ? "AppxMetadata/AppxBundleManifest.xml" : "AppxManifest.xml";
        using (var writer = new StreamWriter(archive.CreateEntry(manifestName).Open()))
            writer.Write(prefix + manifest);
        if (withSignature)
        {
            // This placeholder exercises metadata validation, not Windows signature trust or deployment.
            using Stream signature = archive.CreateEntry("AppxSignature.p7x").Open();
            signature.WriteByte(0);
        }
        return path;
    }
}

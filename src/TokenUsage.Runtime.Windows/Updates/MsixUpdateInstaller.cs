using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using Windows.Management.Deployment;

namespace TokenUsage.Runtime.Windows.Updates;

public static class MsixUpdateInstaller
{
    public static void ValidatePackage(string path, string packageName, string publisher, Version version, string architecture)
    {
        using var archive = ZipFile.OpenRead(path);
        bool bundle = path.EndsWith("bundle", StringComparison.OrdinalIgnoreCase);
        string manifestName = bundle ? "AppxMetadata/AppxBundleManifest.xml" : "AppxManifest.xml";
        ZipArchiveEntry manifest = archive.GetEntry(manifestName)
            ?? throw new InvalidDataException("The update has no package manifest.");
        if (manifest.Length is <= 0 or > 1024 * 1024 || archive.GetEntry("AppxSignature.p7x") is null)
            throw new InvalidDataException("The update is not a signed package.");
        using Stream stream = manifest.Open();
        using XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1024 * 1024,
        });
        XElement root = XDocument.Load(reader).Root ?? throw new InvalidDataException("Missing package root.");
        XElement identity = root.Elements().SingleOrDefault(element => element.Name.LocalName == "Identity")
            ?? throw new InvalidDataException("Missing package identity.");
        Version expected = new(version.Major, version.Minor, version.Build, Math.Max(0, version.Revision));
        if ((string?)identity.Attribute("Name") != packageName || (string?)identity.Attribute("Publisher") != publisher
            || !Version.TryParse((string?)identity.Attribute("Version"), out Version? actual) || actual != expected)
            throw new InvalidDataException("The update identity does not match this app and release.");
        bool matchesArchitecture = bundle
            ? root.Descendants().Any(element => element.Name.LocalName == "Package"
                && (string?)element.Attribute("Type") == "application"
                && string.Equals((string?)element.Attribute("Architecture"), architecture, StringComparison.OrdinalIgnoreCase))
            : string.Equals((string?)identity.Attribute("ProcessorArchitecture"), architecture, StringComparison.OrdinalIgnoreCase);
        if (!matchesArchitecture) throw new InvalidDataException("The update architecture does not match this app.");
    }

    public static async Task StageAsync(string path, UpdateEnvironment environment, Version version, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(version);
        if (version <= environment.Version)
            throw new InvalidOperationException("An update must be newer than the installed app.");
        if (environment.Kind != TokenUsage.Core.Updates.UpdatePackageKind.Msix
            || environment.PackageName is null || environment.Publisher is null)
            throw new InvalidOperationException("This installation cannot receive MSIX updates.");
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            throw new PlatformNotSupportedException("MSIX automatic updates require Windows 10 version 2004.");
        // Keep the validated file immutable while Windows verifies its trusted signature.
        using var fileLock = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        ValidatePackage(path, environment.PackageName, environment.Publisher, version, environment.Architecture);
        DeploymentResult result = await new PackageManager().AddPackageByUriAsync(
            new Uri(Path.GetFullPath(path)), new AddPackageOptions
            {
                DeferRegistrationWhenPackagesAreInUse = true,
                ForceAppShutdown = false,
                ForceTargetAppShutdown = false,
                ForceUpdateFromAnyVersion = false,
                AllowUnsigned = false,
            }).AsTask(cancellationToken).ConfigureAwait(false);
        if (result.ExtendedErrorCode is { HResult: < 0 } error)
            throw new IOException($"Windows rejected the update (0x{error.HResult:X8}).", error);
    }
}

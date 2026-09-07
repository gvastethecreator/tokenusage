using System.Runtime.InteropServices;
using TokenUsage.Core.Updates;
using Windows.ApplicationModel;

namespace TokenUsage.Runtime.Windows.Updates;

public sealed record UpdateEnvironment(
    Version Version, string Architecture, UpdatePackageKind? Kind, string StatusKey,
    string InstallDirectory, string? PackageName = null, string? Publisher = null)
{
    public bool IsSupported => Kind is not null;

    public static UpdateEnvironment Detect(Version applicationVersion)
    {
        string architecture = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
        string directory = Path.GetFullPath(AppContext.BaseDirectory);
        UpdateEnvironment Unsupported(string key) => new(applicationVersion, architecture, null, key, directory);
        if (architecture is not ("x64" or "arm64")) return Unsupported("UpdateUnsupportedArchitecture");
        try
        {
            Package package = Package.Current;
            if (package.SignatureKind == PackageSignatureKind.Store) return Unsupported("UpdateStoreManaged");
            if (package.IsDevelopmentMode || package.Id.Name != "GVASTETHECREATOR.TokenUsage"
                || package.SignatureKind is not (PackageSignatureKind.Developer or PackageSignatureKind.Enterprise))
                return Unsupported("UpdateDevelopmentBuild");
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
                return Unsupported("UpdateUnsupportedWindows");
            PackageVersion version = package.Id.Version;
            return new(new Version(version.Major, version.Minor, version.Build, version.Revision),
                architecture, UpdatePackageKind.Msix, "UpdateIdle", directory, package.Id.Name, package.Id.Publisher);
        }
        catch (Exception exception) when (exception is InvalidOperationException or COMException)
        {
            return File.Exists(Path.Combine(directory, "TokenUsage.portable"))
                && !Directory.Exists(Path.Combine(directory, ".git"))
                ? new(applicationVersion, architecture, UpdatePackageKind.Portable, "UpdateIdle", directory)
                : Unsupported("UpdateDevelopmentBuild");
        }
    }
}

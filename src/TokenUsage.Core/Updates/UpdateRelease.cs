namespace TokenUsage.Core.Updates;

public enum UpdatePackageKind
{
    Portable,
    Msix,
}

public sealed record UpdateRelease(
    Version Version,
    string Tag,
    string AssetName,
    Uri DownloadUri,
    long Size,
    string Sha256,
    UpdatePackageKind Kind);

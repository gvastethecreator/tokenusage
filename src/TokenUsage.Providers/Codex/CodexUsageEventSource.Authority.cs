using System.Security.Cryptography;
using System.Text;
using TokenUsage.Core.Usage;

namespace TokenUsage.Providers.Codex;

public sealed partial class CodexUsageEventSource
{
    public UsageSourceInstanceId SourceAuthority { get; }

    private static UsageSourceInstanceId ResolveSourceAuthority(string home)
    {
        string fullPath = Path.GetFullPath(home);
        string current = Path.GetPathRoot(fullPath)!;
        foreach (string segment in fullPath[current.Length..].Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            var directory = new DirectoryInfo(current);
            if (directory.LinkTarget is not null)
                current = directory.ResolveLinkTarget(returnFinalTarget: true)?.FullName
                    ?? throw new IOException("The Codex profile alias could not be resolved.");
        }
        current = Path.TrimEndingDirectorySeparator(Path.GetFullPath(current));
        if (OperatingSystem.IsWindows()) current = current.ToUpperInvariant();
        return new UsageSourceInstanceId(Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes("codex-profile/v1\0" + current))));
    }
}

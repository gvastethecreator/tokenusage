using System.Runtime.InteropServices;

namespace TokenUsage.Platform.Windows.Processes;

public static class CodexExecutableResolver
{
    public const string OverrideEnvironmentVariable = "TOKENUSAGE_CODEX_EXECUTABLE";

    public static CodexExecutableResolution Resolve() =>
        Resolve(CodexExecutableSearchContext.Capture());

    internal static CodexExecutableResolution Resolve(CodexExecutableSearchContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.ExplicitOverride is not null)
        {
            return TryNormalizeExecutable(context.ExplicitOverride, out string? explicitPath)
                ? new CodexExecutableResolution.Resolved(explicitPath)
                : new CodexExecutableResolution.InvalidOverride();
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string candidate in EnumeratePreferredNativeCandidates(context))
        {
            if (TryNormalizeExecutable(candidate, out string? executablePath)
                && seen.Add(executablePath))
            {
                return new CodexExecutableResolution.Resolved(executablePath);
            }
        }

        foreach (string candidate in EnumerateSearchCandidates(context))
        {
            if (!TryNormalizeExecutable(candidate, out string? executablePath)
                || !seen.Add(executablePath))
            {
                continue;
            }

            return new CodexExecutableResolution.Resolved(executablePath);
        }

        return new CodexExecutableResolution.Missing();
    }

    private static IEnumerable<string> EnumerateSearchCandidates(
        CodexExecutableSearchContext context)
    {
        string? deferredBunCandidate = null;
        if (!string.IsNullOrEmpty(context.PathValue))
        {
            foreach (string rawEntry in context.PathValue.Split(Path.PathSeparator))
            {
                string entry = rawEntry.Trim();
                if (entry.Length >= 2 && entry[0] == '"' && entry[^1] == '"')
                {
                    entry = entry[1..^1];
                }

                if (entry.Length == 0)
                {
                    continue;
                }

                string expanded;
                try
                {
                    expanded = Environment.ExpandEnvironmentVariables(entry);
                }
                catch (ArgumentException)
                {
                    continue;
                }

                if (!IsAbsoluteLocalPath(expanded))
                {
                    continue;
                }

                string candidate = Path.Combine(expanded, "codex.exe");
                if (IsBunGlobalBin(expanded, context.UserProfile))
                {
                    deferredBunCandidate = candidate;
                    continue;
                }

                yield return candidate;
            }
        }

        foreach (string candidate in EnumerateKnownCandidates(context))
        {
            yield return candidate;
        }

        if (deferredBunCandidate is not null)
        {
            yield return deferredBunCandidate;
        }
    }

    private static IEnumerable<string> EnumerateKnownCandidates(
        CodexExecutableSearchContext context)
    {
        if (!string.IsNullOrWhiteSpace(context.UserProfile))
        {
            yield return Path.Combine(context.UserProfile, ".bun", "bin", "codex.exe");
        }

        if (!string.IsNullOrWhiteSpace(context.LocalAppData))
        {
            yield return Path.Combine(
                context.LocalAppData,
                "Microsoft",
                "WinGet",
                "Links",
                "codex.exe");
            yield return Path.Combine(
                context.LocalAppData,
                "Microsoft",
                "WindowsApps",
                "codex.exe");
            yield return Path.Combine(
                context.LocalAppData,
                "Programs",
                "Codex",
                "codex.exe");
        }

        if (!string.IsNullOrWhiteSpace(context.ProgramFiles))
        {
            yield return Path.Combine(context.ProgramFiles, "Codex", "codex.exe");
        }
    }

    private static IEnumerable<string> EnumeratePreferredNativeCandidates(
        CodexExecutableSearchContext context)
    {
        if (string.IsNullOrWhiteSpace(context.AppData))
        {
            yield break;
        }

        string? package = context.Architecture switch
        {
            Architecture.X64 => "codex-win32-x64",
            Architecture.Arm64 => "codex-win32-arm64",
            _ => null,
        };
        string? target = context.Architecture switch
        {
            Architecture.X64 => "x86_64-pc-windows-msvc",
            Architecture.Arm64 => "aarch64-pc-windows-msvc",
            _ => null,
        };
        if (package is not null && target is not null)
        {
            yield return Path.Combine(
                context.AppData,
                "npm",
                "node_modules",
                "@openai",
                "codex",
                "node_modules",
                "@openai",
                package,
                "vendor",
                target,
                "bin",
                "codex.exe");
        }
    }

    internal static bool TryNormalizeExecutable(string candidate, out string executablePath)
    {
        executablePath = string.Empty;
        if (string.IsNullOrWhiteSpace(candidate)
            || candidate.IndexOfAny(['\r', '\n', '\0', '"']) >= 0
            || !IsAbsoluteLocalPath(candidate)
            || !string.Equals(Path.GetExtension(candidate), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(candidate);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        if (!IsAbsoluteLocalPath(fullPath))
        {
            return false;
        }

        try
        {
            var file = new FileInfo(fullPath);
            if (!file.Exists || file.Length <= 0)
            {
                return false;
            }
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or IOException or NotSupportedException)
        {
            return false;
        }

        executablePath = fullPath;
        return true;
    }

    private static bool IsAbsoluteLocalPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || path.StartsWith("\\\\", StringComparison.Ordinal)
            || !Path.IsPathFullyQualified(path))
        {
            return false;
        }

        string? root = Path.GetPathRoot(path);
        return root is { Length: >= 3 }
            && char.IsAsciiLetter(root[0])
            && root[1] == ':'
            && Path.EndsInDirectorySeparator(root);
    }

    private static bool IsBunGlobalBin(string path, string? userProfile)
    {
        if (string.IsNullOrWhiteSpace(userProfile))
        {
            return false;
        }

        try
        {
            string expected = Path.GetFullPath(Path.Combine(userProfile, ".bun", "bin"));
            string actual = Path.GetFullPath(path);
            return string.Equals(
                Path.TrimEndingDirectorySeparator(actual),
                Path.TrimEndingDirectorySeparator(expected),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}

internal sealed record CodexExecutableSearchContext(
    string? ExplicitOverride,
    string? PathValue,
    string? UserProfile,
    string? AppData,
    string? LocalAppData,
    string? ProgramFiles,
    Architecture Architecture)
{
    internal static CodexExecutableSearchContext Capture() =>
        new(
            Environment.GetEnvironmentVariable(CodexExecutableResolver.OverrideEnvironmentVariable),
            Environment.GetEnvironmentVariable("PATH"),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            RuntimeInformation.ProcessArchitecture);
}

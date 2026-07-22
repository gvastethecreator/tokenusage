using System.Runtime.InteropServices;
using WOpenUsage.Platform.Windows.Processes;

namespace WOpenUsage.Platform.Windows.Tests;

public sealed class CodexExecutableResolverTests
{
    [Fact]
    public void ValidAbsoluteOverrideWins()
    {
        using var folder = new TemporaryFolder();
        string executable = folder.CreateExecutable("override", "codex.exe");
        string pathExecutable = folder.CreateExecutable("path", "codex.exe");
        CodexExecutableSearchContext context = CreateContext(
            explicitOverride: executable,
            pathValue: Path.GetDirectoryName(pathExecutable));

        CodexExecutableResolution.Resolved result = Assert.IsType<CodexExecutableResolution.Resolved>(
            CodexExecutableResolver.Resolve(context));

        Assert.Equal(executable, result.ExecutablePath, ignoreCase: true);
    }

    [Fact]
    public void InvalidOverrideFailsClosedWithoutPathFallback()
    {
        using var folder = new TemporaryFolder();
        string pathExecutable = folder.CreateExecutable("path", "codex.exe");
        string missingOverride = Path.Combine(folder.Root, "missing", "codex.exe");
        CodexExecutableSearchContext context = CreateContext(
            explicitOverride: missingOverride,
            pathValue: Path.GetDirectoryName(pathExecutable));

        Assert.IsType<CodexExecutableResolution.InvalidOverride>(
            CodexExecutableResolver.Resolve(context));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("codex.exe")]
    [InlineData(".\\codex.exe")]
    [InlineData("C:\\tools\\codex.cmd")]
    [InlineData("C:\\tools\\codex.exe --stdio")]
    [InlineData("\"C:\\tools\\codex.exe\"")]
    [InlineData("\\\\server\\tools\\codex.exe")]
    [InlineData("\\\\?\\C:\\tools\\codex.exe")]
    public void UnsafeOverrideShapesAreRejected(string explicitOverride)
    {
        CodexExecutableSearchContext context = CreateContext(explicitOverride);

        Assert.IsType<CodexExecutableResolution.InvalidOverride>(
            CodexExecutableResolver.Resolve(context));
    }

    [Fact]
    public void AbsolutePathEntryResolvesExistingNonEmptyExe()
    {
        using var folder = new TemporaryFolder();
        string executable = folder.CreateExecutable("path", "codex.exe");
        string quotedDirectory = $"\"{Path.GetDirectoryName(executable)}\"";
        CodexExecutableSearchContext context = CreateContext(
            explicitOverride: null,
            pathValue: quotedDirectory);

        CodexExecutableResolution.Resolved result = Assert.IsType<CodexExecutableResolution.Resolved>(
            CodexExecutableResolver.Resolve(context));

        Assert.Equal(executable, result.ExecutablePath, ignoreCase: true);
    }

    [Fact]
    public void RelativeAndEmptyPathEntriesNeverUseCurrentDirectory()
    {
        string pathValue = string.Join(Path.PathSeparator, "", ".", "relative-tools");
        CodexExecutableSearchContext context = CreateContext(
            explicitOverride: null,
            pathValue: pathValue);

        Assert.IsType<CodexExecutableResolution.Missing>(
            CodexExecutableResolver.Resolve(context));
    }

    [Fact]
    public void EmptyPathCandidateIsSkippedForKnownCandidate()
    {
        using var folder = new TemporaryFolder();
        string emptyDirectory = folder.CreateDirectory("empty-path");
        File.WriteAllBytes(Path.Combine(emptyDirectory, "codex.exe"), []);
        string userProfile = folder.CreateDirectory("profile");
        string knownExecutable = folder.CreateExecutable(
            Path.Combine("profile", ".bun", "bin"),
            "codex.exe");
        CodexExecutableSearchContext context = CreateContext(
            explicitOverride: null,
            pathValue: emptyDirectory,
            userProfile: userProfile);

        CodexExecutableResolution.Resolved result = Assert.IsType<CodexExecutableResolution.Resolved>(
            CodexExecutableResolver.Resolve(context));

        Assert.Equal(knownExecutable, result.ExecutablePath, ignoreCase: true);
    }

    [Theory]
    [InlineData(Architecture.X64, "codex-win32-x64", "x86_64-pc-windows-msvc")]
    [InlineData(Architecture.Arm64, "codex-win32-arm64", "aarch64-pc-windows-msvc")]
    public void NpmNativeCandidateMatchesProcessArchitecture(
        Architecture architecture,
        string package,
        string target)
    {
        using var folder = new TemporaryFolder();
        string appData = folder.CreateDirectory("appdata");
        string executable = folder.CreateExecutable(
            Path.Combine(
                "appdata",
                "npm",
                "node_modules",
                "@openai",
                "codex",
                "node_modules",
                "@openai",
                package,
                "vendor",
                target,
                "bin"),
            "codex.exe");
        CodexExecutableSearchContext context = CreateContext(
            explicitOverride: null,
            appData: appData,
            architecture: architecture);

        CodexExecutableResolution.Resolved result = Assert.IsType<CodexExecutableResolution.Resolved>(
            CodexExecutableResolver.Resolve(context));

        Assert.Equal(executable, result.ExecutablePath, ignoreCase: true);
    }

    private static CodexExecutableSearchContext CreateContext(
        string? explicitOverride,
        string? pathValue = null,
        string? userProfile = null,
        string? appData = null,
        Architecture architecture = Architecture.X64) =>
        new(
            explicitOverride,
            pathValue,
            userProfile,
            appData,
            LocalAppData: null,
            ProgramFiles: null,
            architecture);

    private sealed class TemporaryFolder : IDisposable
    {
        internal TemporaryFolder()
        {
            Root = Path.Combine(Path.GetTempPath(), $"wopenusage-resolver-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
        }

        internal string Root { get; }

        internal string CreateDirectory(string relativePath)
        {
            string path = Path.Combine(Root, relativePath);
            Directory.CreateDirectory(path);
            return path;
        }

        internal string CreateExecutable(string relativeDirectory, string fileName)
        {
            string directory = CreateDirectory(relativeDirectory);
            string path = Path.Combine(directory, fileName);
            File.WriteAllBytes(path, [0x4D, 0x5A, 0x01]);
            return path;
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}

using TokenUsage.Providers.Claude;

namespace TokenUsage.Providers.Tests.Claude;

public sealed class ClaudeConfigLocatorTests
{
    [Fact]
    public void UsesDefaultClaudeProjectsDirectory()
    {
        using var folder = new TemporaryFolder();
        string expected = Directory.CreateDirectory(
            Path.Combine(folder.Path, ".claude", "projects")).FullName;

        Assert.Equal([expected], ClaudeConfigLocator.FindProjectDirectories(folder.Path));
    }

    [Fact]
    public void OverrideAcceptsConfigRootsAndProjectsDirectories()
    {
        using var folder = new TemporaryFolder();
        string first = Directory.CreateDirectory(
            Path.Combine(folder.Path, "first", "projects")).FullName;
        string second = Directory.CreateDirectory(
            Path.Combine(folder.Path, "second", "projects")).FullName;

        IReadOnlyList<string> actual = ClaudeConfigLocator.FindProjectDirectories(
            folder.Path,
            $"{Path.GetDirectoryName(first)}, {second}");

        Assert.Equal([first, second], actual);
    }

    [Fact]
    public void InvalidOverrideDoesNotFallBackToTheDefaultAccount()
    {
        using var folder = new TemporaryFolder();
        Directory.CreateDirectory(Path.Combine(folder.Path, ".claude", "projects"));

        IReadOnlyList<string> actual = ClaudeConfigLocator.FindProjectDirectories(
            folder.Path,
            Path.Combine(folder.Path, "missing"));

        Assert.Empty(actual);
    }

    private sealed class TemporaryFolder : IDisposable
    {
        public TemporaryFolder()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "tokenusage-claude-locator",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}

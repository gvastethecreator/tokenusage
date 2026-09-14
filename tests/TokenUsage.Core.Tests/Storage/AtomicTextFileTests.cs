using TokenUsage.Core.Storage;

namespace TokenUsage.Core.Tests.Storage;

public sealed class AtomicTextFileTests
{
    [Fact]
    public async Task SuccessfulWriteReplacesTheDestinationAndRemovesTheStagingFile()
    {
        using var folder = new TemporaryFolder();
        string path = Path.Combine(folder.Path, "report.json");
        await AtomicTextFile.WriteAsync(path, "{\"ok\":true}");
        Assert.Equal("{\"ok\":true}", await File.ReadAllTextAsync(path));
        Assert.False(File.Exists(path + ".part"));
    }

    [Fact]
    public async Task CancellationDeletesTheStagingFileAndLeavesNoCompletedResult()
    {
        using var folder = new TemporaryFolder();
        string path = Path.Combine(folder.Path, "report.json");
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => AtomicTextFile.WriteAsync(path, "partial", cancel.Token));
        Assert.False(File.Exists(path));
        Assert.False(File.Exists(path + ".part"));
    }

    [Fact]
    public async Task DirectoryDestinationThrowsAndLeavesNoStagingFile()
    {
        using var folder = new TemporaryFolder();
        string path = Path.Combine(folder.Path, "denied-target");
        Directory.CreateDirectory(path);
        Exception error = await Assert.ThrowsAnyAsync<Exception>(() => AtomicTextFile.WriteAsync(path, "{\"ok\":true}"));
        Assert.True(error is IOException or UnauthorizedAccessException);
        Assert.True(Directory.Exists(path));
        Assert.False(File.Exists(path + ".part"));
        Assert.Empty(Directory.GetFiles(path));
    }

    private sealed class TemporaryFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "tokenusage-atomic",
            Guid.NewGuid().ToString("N"));

        public TemporaryFolder() => Directory.CreateDirectory(Path);

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}

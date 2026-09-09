using TokenUsage.Core.Storage;

namespace TokenUsage.Core.Tests.Storage;

public sealed class VersionedDocumentFileTests
{
    [Fact]
    public void InterruptedStreamWritePreservesOriginalAndRemovesTemporaryFile()
    {
        string directory = Directory.CreateTempSubdirectory("tokenusage-stream-write-").FullName;
        string path = Path.Combine(directory, "checkpoint.json");
        try
        {
            var document = new VersionedDocumentFile(path, "TokenUsage.StreamWriteTest", TimeProvider.System);
            document.WriteAtomically("original"u8.ToArray(), 100);
            Assert.Throws<IOException>(() => document.WriteAtomically(stream =>
            {
                stream.Write("partial"u8);
                throw new IOException("Interrupted write");
            }));
            Assert.Equal("original", File.ReadAllText(path));
            Assert.Equal([path], Directory.GetFiles(directory));
        }
        finally
        {
            File.Delete(path);
            Directory.Delete(directory);
        }
    }
}

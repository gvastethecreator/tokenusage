using System.Globalization;
using TokenUsage.Core.Usage;

namespace TokenUsage.Cli.Tests;

public sealed class RecoverUsageCommandTests
{
    [Fact]
    public async Task RecoveryCreatesASeparateStoreAndRefusesAnExistingOutput()
    {
        string root = Path.Combine(Path.GetTempPath(), "tokenusage-recovery-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string originalPath = Path.Combine(root, "original.db"), copyPath = Path.Combine(root, "copy.db");
            UsageRepository original = await UsageRepository.OpenAsync(originalPath);
            UsageDataRevision revision = await original.ReadDataRevisionAsync();
            using var output = new StringWriter(CultureInfo.InvariantCulture);
            using var error = new StringWriter(CultureInfo.InvariantCulture);
            Assert.Equal(0, await RecoverUsageCommand.RunAsync(["--backup", originalPath, "--output", copyPath], output, error));
            UsageRepository copy = await UsageRepository.OpenReadOnlyAsync(copyPath);
            Assert.NotEqual(revision.DatabaseId, (await copy.ReadDataRevisionAsync()).DatabaseId);
            Assert.Equal(4, await RecoverUsageCommand.RunAsync(["--backup", originalPath, "--output", copyPath], output, error));
            Assert.Equal(revision, await original.ReadDataRevisionAsync());
            Assert.DoesNotContain(root, error.ToString(), StringComparison.Ordinal);
            Assert.Equal(2, await RecoverUsageCommand.RunAsync([], output, error));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}

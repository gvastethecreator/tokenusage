using WOpenUsage.Core.Automation;
using WOpenUsage.Core.Providers;
using WOpenUsage.Providers.Codex;
using WOpenUsage.Runtime.Windows.Automation;

namespace WOpenUsage.Platform.Windows.Tests.Automation;

public sealed class WindowsProviderDiagnosticsQueryTests
{
    [Fact]
    public async Task MissingLocalDataReturnsTheActiveCatalogWithoutCreatingFiles()
    {
        string dataDirectory = Path.Combine(
            Path.GetTempPath(),
            "wou-diagnostics-" + Guid.NewGuid().ToString("N"));
        var query = new WindowsProviderDiagnosticsQuery(
            dataDirectory,
            new MissingCodexFactory(),
            _ => false,
            _ => throw new InvalidOperationException("Deferred Vercel must not be detected."));

        ProviderDiagnosticsSnapshot result = await query.ExecuteAsync();

        Assert.Equal(4, result.Providers.Count);
        Assert.Equal(6, result.Checks.Count);
        Assert.DoesNotContain(
            result.Providers,
            provider => provider.Id == "vercel-ai-gateway");
        Assert.All(result.Providers, provider =>
            Assert.Equal(ProviderDetectionStatus.Missing, provider.Detection));
        Assert.False(Directory.Exists(dataDirectory));
    }

    private sealed class MissingCodexFactory : ICodexQuotaClientFactory
    {
        public ValueTask<CodexClientAvailability> DetectAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(CodexClientAvailability.MissingCli);
        }

        public Task<ICodexQuotaClient> CreateAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Diagnostics must not start Codex.");
    }
}

using TokenUsage.Core.Automation;
using TokenUsage.Core.Providers;
using TokenUsage.Providers.Codex;
using TokenUsage.Runtime.Windows.Automation;

namespace TokenUsage.Platform.Windows.Tests.Automation;

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

        Assert.Equal(5, result.Providers.Count);
        Assert.Equal(7, result.Checks.Count);
        Assert.Contains(result.Checks, check => check.Id == "local-usage-antigravity");
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

using System.Net;
using System.Text.Json;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;
using TokenUsage.Runtime.Windows.Claude;

namespace TokenUsage.Platform.Windows.Tests.Claude;

public sealed class ClaudeUsageRuntimeTests
{
    [Fact]
    public async Task OptInReadsAccountUsageAndRetainsLastReadingWhenSignInOrContractFails()
    {
        string root = Path.Combine(Path.GetTempPath(), "tokenusage-claude-usage-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var settings = new ClaudeSettingsInstaller(configDirectoryOverride: root);
            using var handler = new UsageHandler();
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(1) };
            var consent = new ClaudeLimitsConsentStore(ClaudeLimitsConsentStore.DefaultPath(root));
            var runtime = new ClaudeUsageRuntime(settings, consent, client, "UTC");
            var context = new RefreshContext(TimeProvider.System, forceRefresh: true);

            Assert.IsType<ProviderOutcome.TransientFailure>(await runtime.RefreshAsync(context, default));
            Assert.Equal(0, handler.Calls);
            settings.InstallStatusLine();
            // A later install cannot change a completed migration; explicit opt-in does.
            Assert.Equal(ClaudeLimitsConsentStatus.NotEnabled, await consent.ReadOrMigrateAsync(true));
            await consent.EnableAsync();
            Assert.IsType<ProviderOutcome.TransientFailure>(await runtime.RefreshAsync(context, default));
            Assert.Equal(0, handler.Calls);

            string path = Path.Combine(root, ".credentials.json");
            string credentials = JsonSerializer.Serialize(new
            {
                claudeAiOauth = new
                {
                    accessToken = "fixture-access-token",
                    refreshToken = "fixture-refresh-token",
                    expiresAt = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds(),
                    subscriptionType = "max",
                    rateLimitTier = "default_claude_max_5x",
                },
            });
            await File.WriteAllTextAsync(path, credentials);
            var success = Assert.IsType<ProviderOutcome.Success>(await runtime.RefreshAsync(context, default));
            Assert.Equal("Max (5x)", success.Snapshot.PlanLabel);
            Assert.Equal(100m, Assert.Single(success.Snapshot.Metrics.OfType<ProgressMetricSnapshot>()).Used);
            context = new RefreshContext(TimeProvider.System, success.Snapshot, forceRefresh: true);
            await File.WriteAllTextAsync(settings.SettingsPath,
                """{"statusLine":{"type":"command","command":"wrapper --command 'tokenusage claude statusline'"}}""");
            Assert.Equal(ClaudeIntegrationStatus.NotInstalled, settings.GetStatusLineStatus());

            File.Delete(path);
            var unavailable = Assert.IsType<ProviderOutcome.TransientFailure>(await runtime.RefreshAsync(context, default));
            Assert.Same(success.Snapshot, unavailable.LastGood);
            Assert.Contains("sign in", unavailable.Error.Message, StringComparison.Ordinal);
            await File.WriteAllTextAsync(path, credentials);

            handler.Status = HttpStatusCode.Unauthorized;
            var failure = Assert.IsType<ProviderOutcome.TransientFailure>(await runtime.RefreshAsync(context, default));
            Assert.Same(success.Snapshot, failure.LastGood);
            Assert.Contains("/usage", failure.Error.Message, StringComparison.Ordinal);
            Assert.Contains("/login", failure.Error.Message, StringComparison.Ordinal);
            Assert.Equal(credentials, await File.ReadAllTextAsync(path));

            handler.Status = HttpStatusCode.OK;
            handler.Payload = "{\"limits\":[{\"kind\":\"weekly_scoped\",\"scope\":null,\"percent\":26}]}";
            var contract = Assert.IsType<ProviderOutcome.ContractFailure>(await runtime.RefreshAsync(context, default));
            Assert.Same(success.Snapshot, contract.LastGood);

            handler.StallBody = true;
            failure = Assert.IsType<ProviderOutcome.TransientFailure>(
                await runtime.RefreshAsync(context, default).WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Same(success.Snapshot, failure.LastGood);
            handler.StallBody = false;

            await File.WriteAllTextAsync(path, "{\"claudeAiOauth\":{\"accessToken\":\"expired-token\",\"expiresAt\":1}}");
            failure = Assert.IsType<ProviderOutcome.TransientFailure>(await runtime.RefreshAsync(context, default));
            Assert.Same(success.Snapshot, failure.LastGood);
            Assert.Equal(4, handler.Calls);

            Assert.Contains("expired", failure.Error.Message, StringComparison.Ordinal);
            Assert.Contains("/usage", failure.Error.Message, StringComparison.Ordinal);
            await File.WriteAllTextAsync(path, credentials);
            handler.Payload = "{\"limits\":[{\"kind\":\"weekly_all\",\"percent\":100,\"resets_at\":null}]}";
            handler.BeforeResponse = async () => { await consent.DisableAsync(); };
            Assert.IsType<ProviderOutcome.NotConfigured>(await runtime.RefreshAsync(context, default));
            await File.WriteAllTextAsync(path, "not-readable-credentials");
            Assert.IsType<ProviderOutcome.NotConfigured>(await runtime.RefreshAsync(context, default));
            Assert.Equal(5, handler.Calls);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class StalledContent : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            SerializeToStreamAsync(stream, context, CancellationToken.None);
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context,
            CancellationToken cancellationToken) => Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    private sealed class UsageHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public bool StallBody { get; set; }
        public Func<Task>? BeforeResponse { get; set; }
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public string Payload { get; set; } = "{\"limits\":[{\"kind\":\"weekly_all\",\"percent\":100,\"resets_at\":null}]}";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("https://api.anthropic.com/api/oauth/usage", request.RequestUri?.AbsoluteUri);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("fixture-access-token", request.Headers.Authorization?.Parameter);
            Assert.Equal("oauth-2025-04-20", Assert.Single(request.Headers.GetValues("anthropic-beta")));
            if (BeforeResponse is not null)
                await BeforeResponse();
            return new HttpResponseMessage(Status)
            {
                RequestMessage = request,
                Content = StallBody ? new StalledContent() : new StringContent(Payload),
            };
        }
    }
}

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using TokenUsage.Core.Cache;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;
using TokenUsage.Providers.Claude;

namespace TokenUsage.Runtime.Windows.Claude;

/// <summary>
/// Reads the existing Claude Code sign-in in memory. Claude Code owns credential renewal;
/// TokenUsage never writes or copies its tokens. The existing Claude opt-in controls access.
/// </summary>
public sealed class ClaudeUsageRuntime : IProviderRuntime
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
    private static readonly Uri Endpoint = new("https://api.anthropic.com/api/oauth/usage");
    private static readonly HttpClient SharedClient = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    }) { Timeout = RequestTimeout };
    private static readonly TimeSpan ReadingLifetime = TimeSpan.FromMinutes(1);
    private readonly ClaudeSettingsInstaller _settings;
    private readonly ClaudeLimitsConsentStore _consent;
    private readonly HttpClient _client;
    private readonly string _timeZoneId;

    public ClaudeUsageRuntime(ClaudeSettingsInstaller settings, ClaudeLimitsConsentStore consent, HttpClient? client = null,
        string? timeZoneId = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _consent = consent ?? throw new ArgumentNullException(nameof(consent));
        _client = client ?? SharedClient;
        _timeZoneId = timeZoneId ?? TimeZoneInfo.Local.Id;
    }

    public ProviderDescriptor Descriptor { get; } = new(new ProviderId("claude"), "Claude");

    public static ProviderRefreshRegistration CreateRegistration(
        string dataDirectory, string cacheDirectory, TimeProvider clock, string timeZoneId) =>
        new(new ResilientProviderRuntime(new ClaudeUsageRuntime(new ClaudeSettingsInstaller(),
                new ClaudeLimitsConsentStore(ClaudeLimitsConsentStore.DefaultPath(dataDirectory)),
                timeZoneId: timeZoneId)),
            new SnapshotStore(Path.Combine(cacheDirectory, SnapshotStore.DefaultFileName), clock),
            new ProviderOperationGate());

    public async Task<ProviderOutcome> RefreshAsync(RefreshContext context,
        CancellationToken cancellationToken)
    {
        ClaudeLimitsConsentStatus consent;
        try
        {
            consent = await _consent.ReadOrMigrateAsync(
                _settings.GetStatusLineStatus() == ClaudeIntegrationStatus.Installed, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or JsonException or TimeoutException)
        {
            return Failure(context, "The Claude limits preference could not be read. The last reading is retained.");
        }
        if (consent == ClaudeLimitsConsentStatus.Disabled)
            return new ProviderOutcome.NotConfigured("Claude limits are turned off in Settings.");
        if (consent != ClaudeLimitsConsentStatus.Enabled)
            return Failure(context, "Enable Claude limits in Settings to read your Claude Code sign-in.");

        if (!context.ForceRefresh && context.LastGood is { } lastGood
            && !SnapshotFreshness.IsStale(lastGood, context.Clock, ReadingLifetime))
            return new ProviderOutcome.Success(lastGood);

        DateTimeOffset now = context.Clock.GetUtcNow();
        string credentialsPath = Path.Combine(Path.GetDirectoryName(_settings.SettingsPath)!, ".credentials.json");
        bool readingResponse = false;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_client.Timeout > TimeSpan.Zero && _client.Timeout < RequestTimeout
                ? _client.Timeout : RequestTimeout);
            CancellationToken readToken = timeout.Token;
            var file = new FileInfo(credentialsPath);
            if (!file.Exists)
                return Failure(context, "Open Claude Code and sign in to read its limits.");
            if (file.Length > 64 * 1024)
                return Failure(context, "The Claude Code sign-in could not be read.");

            using JsonDocument credentials = JsonDocument.Parse(
                await File.ReadAllTextAsync(credentialsPath, readToken).ConfigureAwait(false));
            if (credentials.RootElement.ValueKind != JsonValueKind.Object
                || !credentials.RootElement.TryGetProperty("claudeAiOauth", out JsonElement oauth)
                || oauth.ValueKind != JsonValueKind.Object
                || !oauth.TryGetProperty("accessToken", out JsonElement token)
                || token.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(token.GetString()))
                return Failure(context, "The Claude Code sign-in is unavailable. Open Claude Code and sign in to read its limits.");
            if (!oauth.TryGetProperty("expiresAt", out JsonElement expires)
                || expires.ValueKind != JsonValueKind.Number
                || !expires.TryGetInt64(out long expiry))
                return Failure(context, "The Claude Code sign-in could not be read. Open Claude Code to check its sign-in.");
            if (expiry <= now.ToUnixTimeMilliseconds())
                return Failure(context, "The Claude Code session expired. Run /usage in Claude Code to renew it, then refresh here.");

            using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.GetString());
            request.Headers.Add("anthropic-beta", "oauth-2025-04-20");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using HttpResponseMessage response = await _client.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, readToken).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return Failure(context, "Claude rejected the session. Run /usage in Claude Code; if it cannot renew the session, run /login. Then refresh here.");
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                return new ProviderOutcome.Throttled(now.AddMinutes(5), context.LastGood);
            if (!response.IsSuccessStatusCode)
                return Failure(context, "Claude could not refresh its limits.");
            readingResponse = true;
            if (response.RequestMessage?.RequestUri != Endpoint)
                throw new JsonException("Unexpected Claude usage response origin.");

            await response.Content.LoadIntoBufferAsync(64 * 1024, readToken).ConfigureAwait(false);
            using JsonDocument document = JsonDocument.Parse(
                await response.Content.ReadAsByteArrayAsync(readToken).ConfigureAwait(false));
            string? plan = oauth.TryGetProperty("subscriptionType", out JsonElement subscription)
                && subscription.ValueKind == JsonValueKind.String
                ? subscription.GetString() : null;
            if (oauth.TryGetProperty("rateLimitTier", out JsonElement tier)
                && tier.ValueKind == JsonValueKind.String
                && tier.GetString() is string rateTier && plan == "max")
                plan = rateTier.EndsWith("_5x", StringComparison.Ordinal) ? "Max (5x)"
                    : rateTier.EndsWith("_20x", StringComparison.Ordinal) ? "Max (20x)" : "Max";

            try
            {
                consent = await _consent.ReadOrMigrateAsync(false, readToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                or JsonException or TimeoutException)
            {
                return Failure(context, "The Claude limits preference could not be read. The last reading is retained.");
            }
            if (consent == ClaudeLimitsConsentStatus.Disabled)
                return new ProviderOutcome.NotConfigured("Claude limits are turned off in Settings.");
            if (consent != ClaudeLimitsConsentStatus.Enabled)
                return Failure(context, "Enable Claude limits in Settings to read your Claude Code sign-in.");

            return new ProviderOutcome.Success(ClaudeUsageSnapshotMapper.Map(
                document.RootElement, context.Clock.GetUtcNow(), _timeZoneId, plan));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (JsonException)
        {
            if (!readingResponse)
                return Failure(context, "The Claude Code sign-in could not be read. Open Claude Code to check its sign-in.");
            return new ProviderOutcome.ContractFailure(new ProviderError(
                ProviderErrorCode.ContractViolation, "Claude returned an unreadable usage response."), context.LastGood);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or HttpRequestException or OperationCanceledException or FormatException)
        {
            return Failure(context, "Claude could not refresh its limits. The last reading is retained.");
        }
    }

    private static ProviderOutcome.TransientFailure Failure(RefreshContext context, string reason) =>
        new ProviderOutcome.TransientFailure(
            new ProviderError(ProviderErrorCode.TransientSourceFailure, reason), context.LastGood);
}

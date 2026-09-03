using System.Text;
using System.Text.Json;
using TokenUsage.Providers.Codex;

namespace TokenUsage.Providers.Tests.Codex;

public sealed class CodexAppServerClientTests
{
    private const long PrimaryReset = 1_800_000_000;
    private const long SecondaryReset = 1_800_100_000;

    [Fact]
    public async Task HandshakeAndRateLimitReadUseTheCurrentJsonlContract()
    {
        using var peer = new ScriptedCodexJsonlPeer(
            """
            {"id":"1","result":{"codexHome":"SYNTHETIC_PATH_SENTINEL","platformFamily":"windows","platformOs":"windows","userAgent":"synthetic","future":true}}
            """,
            JsonSerializer.Serialize(new
            {
                id = "2",
                result = new
                {
                    rateLimits = new
                    {
                        planType = "plus",
                        primary = new
                        {
                            usedPercent = 42,
                            resetsAt = PrimaryReset,
                            windowDurationMins = 300,
                        },
                        secondary = new
                        {
                            usedPercent = 18,
                            resetsAt = SecondaryReset,
                            windowDurationMins = 10080,
                        },
                    },
                    rateLimitsByLimitId = (object?)null,
                    future = new { value = 1 },
                },
            }));
        await using CodexAppServerClient client = peer.CreateClient();

        await client.HandshakeAsync(CancellationToken.None);
        CodexRateLimitsSnapshot result = await client.ReadRateLimitsAsync(CancellationToken.None);

        Assert.Equal("plus", result.RateLimits.PlanType);
        Assert.Equal(42, result.RateLimits.Primary?.UsedPercent);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(PrimaryReset), result.RateLimits.Primary?.ResetsAtUtc);
        Assert.Equal(300, result.RateLimits.Primary?.WindowDurationMinutes);
        Assert.Equal(18, result.RateLimits.Secondary?.UsedPercent);
        Assert.Empty(result.RateLimitsByLimitId);
        Assert.Null(result.ResetCredits);

        IReadOnlyList<string> requestLines = peer.GetRequestLines();
        Assert.Equal(3, requestLines.Count);
        using JsonDocument initialize = JsonDocument.Parse(requestLines[0]);
        using JsonDocument initialized = JsonDocument.Parse(requestLines[1]);
        using JsonDocument rateLimits = JsonDocument.Parse(requestLines[2]);

        Assert.Equal(1, initialize.RootElement.GetProperty("id").GetInt64());
        Assert.Equal("initialize", initialize.RootElement.GetProperty("method").GetString());
        Assert.Equal(
            "tokenusage",
            initialize.RootElement.GetProperty("params").GetProperty("clientInfo").GetProperty("name").GetString());
        Assert.False(
            initialize.RootElement.GetProperty("params").GetProperty("capabilities").GetProperty("experimentalApi").GetBoolean());
        Assert.Equal("initialized", initialized.RootElement.GetProperty("method").GetString());
        Assert.False(initialized.RootElement.TryGetProperty("id", out _));
        Assert.Equal(2, rateLimits.RootElement.GetProperty("id").GetInt64());
        Assert.Equal("account/rateLimits/read", rateLimits.RootElement.GetProperty("method").GetString());
        Assert.Equal(JsonValueKind.Null, rateLimits.RootElement.GetProperty("params").ValueKind);
        Assert.DoesNotContain("SYNTHETIC_PATH_SENTINEL", string.Join('\n', requestLines), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResetCreditsPreserveAvailabilityAndSafeMetadataOnly()
    {
        using var peer = new ScriptedCodexJsonlPeer(
            """{"id":1,"result":{}}""",
            """
            {"id":2,"result":{"rateLimits":{"primary":{"usedPercent":10}},"rateLimitResetCredits":{"availableCount":1,"credits":[{"id":"PRIVATE_CREDIT_ID","resetType":"codexRateLimits","status":"available","grantedAt":1787353826,"expiresAt":1789945826,"title":"Full reset","description":"PRIVATE_DESCRIPTION"}]},"accountId":"PRIVATE_ACCOUNT_ID"}}
            """);
        await using CodexAppServerClient client = peer.CreateClient();

        await client.HandshakeAsync(CancellationToken.None);
        CodexRateLimitsSnapshot result = await client.ReadRateLimitsAsync(CancellationToken.None);

        CodexResetCreditInventory inventory = Assert.IsType<CodexResetCreditInventory>(
            result.ResetCredits);
        Assert.Equal(1, inventory.AvailableCount);
        CodexResetCredit credit = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<CodexResetCredit>>(
            inventory.Credits));
        Assert.Equal("codexRateLimits", credit.ResetType);
        Assert.Equal("available", credit.Status);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1787353826), credit.GrantedAtUtc);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1789945826), credit.ExpiresAtUtc);
        Assert.DoesNotContain("PRIVATE", JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ZeroCountWithoutDetailsRemainsAvailableAsCountOnlyData()
    {
        using var peer = new ScriptedCodexJsonlPeer(
            """{"id":1,"result":{}}""",
            """{"id":2,"result":{"rateLimits":{"primary":{"usedPercent":10}},"rateLimitResetCredits":{"availableCount":0}}}""");
        await using CodexAppServerClient client = peer.CreateClient();

        await client.HandshakeAsync(CancellationToken.None);
        CodexRateLimitsSnapshot result = await client.ReadRateLimitsAsync(CancellationToken.None);

        Assert.Equal(0, result.ResetCredits?.AvailableCount);
        Assert.Null(result.ResetCredits?.Credits);
    }

    [Theory]
    [InlineData("{\"availableCount\":-1}")]
    [InlineData("{\"availableCount\":1,\"credits\":[{\"resetType\":\"unsafe type\",\"status\":\"available\"}]}")]
    [InlineData("[]")]
    public async Task MalformedResetCreditsFailClosed(string resetCredits)
    {
        using var peer = new ScriptedCodexJsonlPeer(
            """{"id":1,"result":{}}""",
            """{"id":2,"result":{"rateLimits":{"primary":{"usedPercent":10}},"rateLimitResetCredits":$credits$}}"""
                .Replace("$credits$", resetCredits, StringComparison.Ordinal));
        await using CodexAppServerClient client = peer.CreateClient();

        await client.HandshakeAsync(CancellationToken.None);
        CodexProtocolException error = await Assert.ThrowsAsync<CodexProtocolException>(() =>
            client.ReadRateLimitsAsync(CancellationToken.None));

        Assert.Equal(
            "Codex app-server returned an unsupported rate-limit response.",
            error.Message);
        Assert.DoesNotContain(resetCredits, error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OversizedResetCreditCollectionFailsClosed()
    {
        string credit = """{"resetType":"codexRateLimits","status":"available"}""";
        string credits = string.Join(',', Enumerable.Repeat(credit, 65));
        using var peer = new ScriptedCodexJsonlPeer(
            """{"id":1,"result":{}}""",
            """{"id":2,"result":{"rateLimits":{"primary":{"usedPercent":10}},"rateLimitResetCredits":{"availableCount":65,"credits":[$credits$]}}}"""
                .Replace("$credits$", credits, StringComparison.Ordinal));
        await using CodexAppServerClient client = peer.CreateClient();

        await client.HandshakeAsync(CancellationToken.None);
        await Assert.ThrowsAsync<CodexProtocolException>(() =>
            client.ReadRateLimitsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task UnknownFieldsNotificationsAndNewPlanValuesAreTolerated()
    {
        using var peer = new ScriptedCodexJsonlPeer(
            """{"id":1,"result":{"unknown":{"nested":true}}}""",
            """{"method":"account/rateLimits/updated","params":{"ignored":"PRIVATE_SENTINEL"}}""",
            """{"id":2,"result":{"rateLimits":{"planType":"future-plan","primary":{"usedPercent":0,"future":true}},"newTopLevel":7}}""");
        await using CodexAppServerClient client = peer.CreateClient();

        await client.HandshakeAsync(CancellationToken.None);
        CodexRateLimitsSnapshot result = await client.ReadRateLimitsAsync(CancellationToken.None);

        Assert.Equal("unknown", result.RateLimits.PlanType);
        Assert.Equal(0, result.RateLimits.Primary?.UsedPercent);
    }

    [Fact]
    public async Task AdditionalLimitBucketsAreParsedWithoutAccountFields()
    {
        using var peer = new ScriptedCodexJsonlPeer(
            """{"id":1,"result":{}}""",
            """
            {"id":2,"result":{"rateLimits":{"planType":"pro","primary":null,"secondary":null},"rateLimitsByLimitId":{"codex-model":{"limitId":"base_model_inference","limitName":"gpt-reserve","planType":"pro","primary":{"usedPercent":75}},"codex-mini":{"planType":"free","secondary":{"usedPercent":100,"windowDurationMins":60}}}}}
            """);
        await using CodexAppServerClient client = peer.CreateClient();

        await client.HandshakeAsync(CancellationToken.None);
        CodexRateLimitsSnapshot result = await client.ReadRateLimitsAsync(CancellationToken.None);

        Assert.Null(result.RateLimits.Primary);
        Assert.Null(result.RateLimits.Secondary);
        Assert.Equal(2, result.RateLimitsByLimitId.Count);
        Assert.Equal("base_model_inference", result.RateLimitsByLimitId["codex-model"].LimitId);
        Assert.Equal("gpt-reserve", result.RateLimitsByLimitId["codex-model"].LimitName);
        Assert.Equal(75, result.RateLimitsByLimitId["codex-model"].Primary?.UsedPercent);
        Assert.Equal(100, result.RateLimitsByLimitId["codex-mini"].Secondary?.UsedPercent);
    }

    [Theory]
    [InlineData("unsafe id")]
    [InlineData("path/segment")]
    public async Task UnsafeProviderLimitIdsFailClosed(string limitId)
    {
        using var peer = new ScriptedCodexJsonlPeer(
            """{"id":1,"result":{}}""",
            """
            {"id":2,"result":{"rateLimits":{"primary":null,"secondary":null},"rateLimitsByLimitId":{"safe-key":{"limitId":"$limitId$","primary":{"usedPercent":25}}}}}
            """.Replace("$limitId$", limitId, StringComparison.Ordinal));
        await using CodexAppServerClient client = peer.CreateClient();

        await client.HandshakeAsync(CancellationToken.None);
        CodexProtocolException error = await Assert.ThrowsAsync<CodexProtocolException>(() =>
            client.ReadRateLimitsAsync(CancellationToken.None));

        Assert.Equal(
            "Codex app-server returned an unsupported rate-limit response.",
            error.Message);
        Assert.DoesNotContain(limitId, error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task NullWindowsRemainAbsent()
    {
        using var peer = new ScriptedCodexJsonlPeer(
            """{"id":1,"result":{}}""",
            """{"id":2,"result":{"rateLimits":{"planType":null,"primary":null,"secondary":null}}}""");
        await using CodexAppServerClient client = peer.CreateClient();

        await client.HandshakeAsync(CancellationToken.None);
        CodexRateLimitsSnapshot result = await client.ReadRateLimitsAsync(CancellationToken.None);

        Assert.Null(result.RateLimits.PlanType);
        Assert.Null(result.RateLimits.Primary);
        Assert.Null(result.RateLimits.Secondary);
    }

    [Fact]
    public async Task MismatchedResponseIdFailsClosed()
    {
        using var peer = new ScriptedCodexJsonlPeer("""{"id":99,"result":{}}""");
        await using CodexAppServerClient client = peer.CreateClient();

        CodexProtocolException error = await Assert.ThrowsAsync<CodexProtocolException>(() =>
            client.HandshakeAsync(CancellationToken.None));

        Assert.Equal("Codex app-server response used an unexpected request ID.", error.Message);

        CodexProtocolException reuseError = await Assert.ThrowsAsync<CodexProtocolException>(() =>
            client.HandshakeAsync(CancellationToken.None));
        Assert.Equal(
            "Codex app-server session cannot be reused after a protocol or transport failure.",
            reuseError.Message);
        Assert.Single(peer.GetRequestLines());
    }

    [Fact]
    public async Task RequestTimeoutHasAStableSanitizedError()
    {
        await using var input = new BlockingReadStream();
        await using var output = new MemoryStream();
        await using var client = new CodexAppServerClient(
            input,
            output,
            ScriptedCodexJsonlPeer.CreateDefaultOptions(TimeSpan.FromMilliseconds(50)));

        CodexRequestTimeoutException error =
            await Assert.ThrowsAsync<CodexRequestTimeoutException>(() =>
                client.HandshakeAsync(CancellationToken.None));

        Assert.Equal(
            "Codex app-server did not answer before the request timeout.",
            error.Message);
    }

    [Fact]
    public async Task CallerCancellationIsNotReportedAsTimeout()
    {
        await using var input = new BlockingReadStream();
        await using var output = new MemoryStream();
        await using var client = new CodexAppServerClient(
            input,
            output,
            ScriptedCodexJsonlPeer.CreateDefaultOptions(TimeSpan.FromSeconds(5)));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.HandshakeAsync(cancellation.Token));
    }

    [Fact]
    public async Task OversizedResponseFailsWithoutEchoingTheLine()
    {
        string privatePayload = new('x', 300);
        using var peer = new ScriptedCodexJsonlPeer(privatePayload);
        await using CodexAppServerClient client = peer.CreateClient(
            ScriptedCodexJsonlPeer.CreateDefaultOptions(maximumLineBytes: 256));

        CodexProtocolException error = await Assert.ThrowsAsync<CodexProtocolException>(() =>
            client.HandshakeAsync(CancellationToken.None));

        Assert.Equal("Codex app-server response exceeded the JSONL line limit.", error.Message);
        Assert.DoesNotContain(privatePayload, error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidJsonFailsWithoutEchoingTheLine()
    {
        const string privatePayload = "PRIVATE_RESPONSE_SENTINEL";
        using var peer = new ScriptedCodexJsonlPeer($"not-json-{privatePayload}");
        await using CodexAppServerClient client = peer.CreateClient();

        CodexProtocolException error = await Assert.ThrowsAsync<CodexProtocolException>(() =>
            client.HandshakeAsync(CancellationToken.None));

        Assert.Equal("Codex app-server returned invalid JSON.", error.Message);
        Assert.DoesNotContain(privatePayload, error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TruncatedJsonlMessageFailsBeforeParsing()
    {
        byte[] truncated = Encoding.UTF8.GetBytes("{\"id\":1,\"result\":{}");
        await using var input = new MemoryStream(truncated, writable: false);
        await using var output = new MemoryStream();
        await using var client = new CodexAppServerClient(
            input,
            output,
            ScriptedCodexJsonlPeer.CreateDefaultOptions(),
            leaveOpen: true);

        CodexProtocolException error = await Assert.ThrowsAsync<CodexProtocolException>(() =>
            client.HandshakeAsync(CancellationToken.None));

        Assert.Equal("Codex app-server closed a truncated JSONL message.", error.Message);
    }

    [Fact]
    public async Task RpcErrorExposesOnlyTheNumericCode()
    {
        const string privatePayload = "PRIVATE_SERVER_DETAIL_SENTINEL";
        using var peer = new ScriptedCodexJsonlPeer(JsonSerializer.Serialize(new
        {
            id = 1,
            error = new
            {
                code = -32001,
                message = privatePayload,
                data = new { detail = privatePayload },
            },
        }));
        await using CodexAppServerClient client = peer.CreateClient();

        CodexRpcException error = await Assert.ThrowsAsync<CodexRpcException>(() =>
            client.HandshakeAsync(CancellationToken.None));

        Assert.Equal(-32001, error.Code);
        Assert.Equal("Codex app-server rejected the request.", error.Message);
        Assert.DoesNotContain(privatePayload, error.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task UsedPercentOutsideTheContractFailsClosed(int usedPercent)
    {
        using var peer = new ScriptedCodexJsonlPeer(
            """{"id":1,"result":{}}""",
            JsonSerializer.Serialize(new
            {
                id = 2,
                result = new
                {
                    rateLimits = new
                    {
                        primary = new { usedPercent },
                    },
                },
            }));
        await using CodexAppServerClient client = peer.CreateClient();
        await client.HandshakeAsync(CancellationToken.None);

        CodexProtocolException error = await Assert.ThrowsAsync<CodexProtocolException>(() =>
            client.ReadRateLimitsAsync(CancellationToken.None));

        Assert.Equal(
            "Codex app-server returned an unsupported rate-limit response.",
            error.Message);
    }

    [Fact]
    public async Task RateLimitReadRequiresACompletedHandshake()
    {
        using var peer = new ScriptedCodexJsonlPeer();
        await using CodexAppServerClient client = peer.CreateClient();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.ReadRateLimitsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task HandshakeIsIdempotentAndDoesNotConsumeAnotherResponse()
    {
        using var peer = new ScriptedCodexJsonlPeer("""{"id":1,"result":{}}""");
        await using CodexAppServerClient client = peer.CreateClient();

        await client.HandshakeAsync(CancellationToken.None);
        await client.HandshakeAsync(CancellationToken.None);

        Assert.Equal(2, peer.GetRequestLines().Count);
    }
}

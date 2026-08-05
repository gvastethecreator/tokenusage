using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using WOpenUsage.Providers.Codex;

namespace WOpenUsage.Providers.Tests.Codex;

public sealed class CodexTokenUsageTests
{
    [Fact]
    public async Task UsageReadUsesNullParamsAndReturnsDailyBucketsWithoutIdentityFields()
    {
        using var peer = new ScriptedCodexJsonlPeer(
            """{"id":1,"result":{}}""",
            JsonSerializer.Serialize(new
            {
                id = 2,
                result = new
                {
                    summary = new
                    {
                        currentStreakDays = 3,
                        peakDailyTokens = 1200,
                        future = "PRIVATE_SUMMARY_SENTINEL",
                    },
                    dailyUsageBuckets = new[]
                    {
                        new
                        {
                            startDate = "2026-07-21",
                            tokens = 400,
                            future = "PRIVATE_BUCKET_SENTINEL",
                        },
                        new
                        {
                            startDate = "2026-07-22",
                            tokens = 800,
                            future = "PRIVATE_BUCKET_SENTINEL",
                        },
                    },
                    email = "private-account@example.invalid",
                },
            }));
        await using CodexAppServerClient client = peer.CreateClient();

        await client.HandshakeAsync(CancellationToken.None);
        CodexTokenUsageSnapshot result = await client.ReadTokenUsageAsync(
            CancellationToken.None);

        Assert.Equal(3, result.Summary.CurrentStreakDays);
        Assert.Equal(1200, result.Summary.PeakDailyTokens);
        Assert.Collection(
            result.DailyUsageBuckets,
            first =>
            {
                Assert.Equal(new DateOnly(2026, 7, 21), first.StartDate);
                Assert.Equal(400, first.Tokens);
            },
            second =>
            {
                Assert.Equal(new DateOnly(2026, 7, 22), second.StartDate);
                Assert.Equal(800, second.Tokens);
            });

        IReadOnlyList<string> requestLines = peer.GetRequestLines();
        using JsonDocument request = JsonDocument.Parse(requestLines[2]);
        Assert.Equal("account/usage/read", request.RootElement.GetProperty("method").GetString());
        Assert.Equal(JsonValueKind.Null, request.RootElement.GetProperty("params").ValueKind);

        string publicResult = result.ToString();
        Assert.DoesNotContain("private-account", publicResult, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PRIVATE_", publicResult, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{\"summary\":{}}")]
    [InlineData("{\"summary\":{},\"dailyUsageBuckets\":null}")]
    [InlineData("{\"summary\":{},\"dailyUsageBuckets\":[]}")]
    public async Task MissingNullOrEmptyBucketsReturnAnEmptyImmutableList(string resultJson)
    {
        using var peer = CreatePeer(resultJson);
        await using CodexAppServerClient client = peer.CreateClient();

        await client.HandshakeAsync(CancellationToken.None);
        CodexTokenUsageSnapshot result = await client.ReadTokenUsageAsync(
            CancellationToken.None);

        Assert.Empty(result.DailyUsageBuckets);
        Assert.Null(result.Summary.LifetimeTokens);
    }

    [Fact]
    public async Task ZeroMaximumAndDuplicateDailyBucketsRemainProviderReported()
    {
        string response = JsonSerializer.Serialize(new
        {
            summary = new
            {
                currentStreakDays = (long?)null,
                lifetimeTokens = long.MaxValue,
                longestRunningTurnSec = 0,
                longestStreakDays = 4,
                peakDailyTokens = 0,
            },
            dailyUsageBuckets = new[]
            {
                new { startDate = "2026-07-22", tokens = 0L },
                new { startDate = "2026-07-22", tokens = long.MaxValue },
            },
        });
        using var peer = CreatePeer(response);
        await using CodexAppServerClient client = peer.CreateClient();

        await client.HandshakeAsync(CancellationToken.None);
        CodexTokenUsageSnapshot result = await client.ReadTokenUsageAsync(
            CancellationToken.None);

        Assert.Equal(long.MaxValue, result.Summary.LifetimeTokens);
        Assert.Equal(0, result.Summary.LongestRunningTurnSeconds);
        Assert.Equal(2, result.DailyUsageBuckets.Count);
        Assert.Equal(0, result.DailyUsageBuckets[0].Tokens);
        Assert.Equal(long.MaxValue, result.DailyUsageBuckets[1].Tokens);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"summary\":null}")]
    [InlineData("{\"summary\":[]}")]
    [InlineData("{\"summary\":{},\"dailyUsageBuckets\":{}}")]
    [InlineData("{\"summary\":{},\"dailyUsageBuckets\":[null]}")]
    [InlineData("{\"summary\":{},\"dailyUsageBuckets\":[{\"tokens\":1}]}")]
    [InlineData("{\"summary\":{},\"dailyUsageBuckets\":[{\"startDate\":\"2026-07-22\"}]}")]
    [InlineData("{\"summary\":{},\"dailyUsageBuckets\":[{\"startDate\":null,\"tokens\":1}]}")]
    [InlineData("{\"summary\":{},\"dailyUsageBuckets\":[{\"startDate\":1,\"tokens\":1}]}")]
    [InlineData("{\"summary\":{},\"dailyUsageBuckets\":[{\"startDate\":\"2026-7-22\",\"tokens\":1}]}")]
    [InlineData("{\"summary\":{},\"dailyUsageBuckets\":[{\"startDate\":\"2026-02-30\",\"tokens\":1}]}")]
    [InlineData("{\"summary\":{},\"dailyUsageBuckets\":[{\"startDate\":\"2026-07-22\",\"tokens\":-1}]}")]
    [InlineData("{\"summary\":{},\"dailyUsageBuckets\":[{\"startDate\":\"2026-07-22\",\"tokens\":1.5}]}")]
    [InlineData("{\"summary\":{},\"dailyUsageBuckets\":[{\"startDate\":\"2026-07-22\",\"tokens\":\"1\"}]}")]
    [InlineData("{\"summary\":{\"lifetimeTokens\":-1}}")]
    [InlineData("{\"summary\":{\"peakDailyTokens\":\"1\"}}")]
    public async Task UnsupportedResponsesFailWithoutEchoingPrivateFields(string resultJson)
    {
        const string privateValue = "PRIVATE_USAGE_SENTINEL";
        JsonObject result = JsonNode.Parse(resultJson)!.AsObject();
        result["future"] = privateValue;
        using var peer = CreatePeer(result.ToJsonString());
        await using CodexAppServerClient client = peer.CreateClient();
        await client.HandshakeAsync(CancellationToken.None);

        CodexProtocolException error = await Assert.ThrowsAsync<CodexProtocolException>(() =>
            client.ReadTokenUsageAsync(CancellationToken.None));

        Assert.Equal(
            "Codex app-server returned an unsupported token-usage response.",
            error.Message);
        Assert.DoesNotContain(privateValue, error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(resultJson, error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task MoreThanFourHundredBucketsFailsClosed()
    {
        object[] buckets = CreateBuckets(401);
        using var peer = new ScriptedCodexJsonlPeer(
            """{"id":1,"result":{}}""",
            JsonSerializer.Serialize(new
            {
                id = 2,
                result = new { summary = new { }, dailyUsageBuckets = buckets },
            }));
        await using CodexAppServerClient client = peer.CreateClient(
            ScriptedCodexJsonlPeer.CreateDefaultOptions(maximumLineBytes: 256 * 1024));
        await client.HandshakeAsync(CancellationToken.None);

        CodexProtocolException error = await Assert.ThrowsAsync<CodexProtocolException>(() =>
            client.ReadTokenUsageAsync(CancellationToken.None));

        Assert.Equal(
            "Codex app-server returned an unsupported token-usage response.",
            error.Message);
    }

    [Fact]
    public async Task ExactlyFourHundredBucketsAreAcceptedAndImmutable()
    {
        using var peer = new ScriptedCodexJsonlPeer(
            """{"id":1,"result":{}}""",
            JsonSerializer.Serialize(new
            {
                id = 2,
                result = new { summary = new { }, dailyUsageBuckets = CreateBuckets(400) },
            }));
        await using CodexAppServerClient client = peer.CreateClient(
            ScriptedCodexJsonlPeer.CreateDefaultOptions(maximumLineBytes: 256 * 1024));
        await client.HandshakeAsync(CancellationToken.None);

        CodexTokenUsageSnapshot result = await client.ReadTokenUsageAsync(
            CancellationToken.None);

        Assert.Equal(400, result.DailyUsageBuckets.Count);
        IList<CodexUsageDailyBucket> mutableView = Assert.IsAssignableFrom<
            IList<CodexUsageDailyBucket>>(result.DailyUsageBuckets);
        Assert.True(mutableView.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => mutableView.Add(
            new CodexUsageDailyBucket(new DateOnly(2027, 2, 5), 1)));
    }

    [Fact]
    public async Task UsageReadRequiresACompletedHandshake()
    {
        using var peer = new ScriptedCodexJsonlPeer();
        await using CodexAppServerClient client = peer.CreateClient();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.ReadTokenUsageAsync(CancellationToken.None));

        Assert.Empty(peer.GetRequestLines());
    }

    [Fact]
    public async Task UsageReadSkipsNotificationsAndMatchesItsRequestId()
    {
        using var peer = new ScriptedCodexJsonlPeer(
            """{"id":1,"result":{}}""",
            """{"method":"account/usage/updated","params":{"private":"PRIVATE_NOTIFICATION"}}""",
            """{"id":"2","result":{"summary":{},"dailyUsageBuckets":[{"startDate":"2026-07-22","tokens":7}]}}""");
        await using CodexAppServerClient client = peer.CreateClient();

        await client.HandshakeAsync(CancellationToken.None);
        CodexTokenUsageSnapshot result = await client.ReadTokenUsageAsync(
            CancellationToken.None);

        Assert.Equal(7, Assert.Single(result.DailyUsageBuckets).Tokens);
    }

    private static ScriptedCodexJsonlPeer CreatePeer(string resultJson) =>
        new(
            """{"id":1,"result":{}}""",
            $$"""{"id":2,"result":{{resultJson}}}""");

    private static object[] CreateBuckets(int count) =>
        Enumerable.Range(0, count)
            .Select(index => (object)new
            {
                startDate = new DateOnly(2026, 1, 1).AddDays(index).ToString(
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture),
                tokens = index,
            })
            .ToArray();
}

using System.Text.Json;
using WOpenUsage.Providers.Codex;

namespace WOpenUsage.Providers.Tests.Codex;

public sealed class CodexAccountStatusTests
{
    [Fact]
    public async Task ChatGptAccountKeepsOnlyQuotaRelevantFields()
    {
        const string privateEmail = "private-account-sentinel@example.invalid";
        using var peer = new ScriptedCodexJsonlPeer(
            """{"id":1,"result":{}}""",
            JsonSerializer.Serialize(new
            {
                id = 2,
                result = new
                {
                    account = new
                    {
                        type = "chatgpt",
                        email = privateEmail,
                        planType = "pro",
                        future = "ignored",
                    },
                    requiresOpenaiAuth = true,
                },
            }));
        await using CodexAppServerClient client = peer.CreateClient();

        await client.HandshakeAsync(CancellationToken.None);
        CodexAccountStatus result =
            await client.ReadAccountStatusAsync(CancellationToken.None);

        Assert.Equal(CodexAccountKind.ChatGpt, result.Kind);
        Assert.True(result.RequiresOpenAiAuth);
        Assert.Equal("pro", result.PlanType);
        Assert.DoesNotContain(
            privateEmail,
            string.Join('\n', peer.GetRequestLines()),
            StringComparison.Ordinal);

        using JsonDocument request = JsonDocument.Parse(peer.GetRequestLines()[2]);
        Assert.Equal("account/read", request.RootElement.GetProperty("method").GetString());
        Assert.False(
            request.RootElement.GetProperty("params").GetProperty("refreshToken").GetBoolean());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MissingAccountPreservesRequiresAuth(bool requiresOpenAiAuth)
    {
        using var peer = new ScriptedCodexJsonlPeer(
            """{"id":1,"result":{}}""",
            JsonSerializer.Serialize(new
            {
                id = 2,
                result = new
                {
                    account = (object?)null,
                    requiresOpenaiAuth = requiresOpenAiAuth,
                },
            }));
        await using CodexAppServerClient client = peer.CreateClient();
        await client.HandshakeAsync(CancellationToken.None);

        CodexAccountStatus result =
            await client.ReadAccountStatusAsync(CancellationToken.None);

        Assert.Equal(CodexAccountKind.None, result.Kind);
        Assert.Equal(requiresOpenAiAuth, result.RequiresOpenAiAuth);
        Assert.Null(result.PlanType);
    }

    [Theory]
    [InlineData("apiKey", CodexAccountKind.ApiKey)]
    [InlineData("amazonBedrock", CodexAccountKind.AmazonBedrock)]
    [InlineData("futureAuth", CodexAccountKind.Other)]
    public async Task NonChatGptAccountKindsDoNotExposeAccountFields(
        string accountType,
        CodexAccountKind expectedKind)
    {
        using var peer = new ScriptedCodexJsonlPeer(
            """{"id":1,"result":{}}""",
            JsonSerializer.Serialize(new
            {
                id = 2,
                result = new
                {
                    account = new { type = accountType, privateField = "PRIVATE_SENTINEL" },
                    requiresOpenaiAuth = true,
                },
            }));
        await using CodexAppServerClient client = peer.CreateClient();
        await client.HandshakeAsync(CancellationToken.None);

        CodexAccountStatus result =
            await client.ReadAccountStatusAsync(CancellationToken.None);

        Assert.Equal(expectedKind, result.Kind);
        Assert.Null(result.PlanType);
    }

    [Fact]
    public async Task UnsupportedAccountShapeFailsWithASanitizedContractError()
    {
        const string privateValue = "PRIVATE_ACCOUNT_SENTINEL";
        using var peer = new ScriptedCodexJsonlPeer(
            """{"id":1,"result":{}}""",
            JsonSerializer.Serialize(new
            {
                id = 2,
                result = new
                {
                    account = new { privateField = privateValue },
                    requiresOpenaiAuth = true,
                },
            }));
        await using CodexAppServerClient client = peer.CreateClient();
        await client.HandshakeAsync(CancellationToken.None);

        CodexProtocolException error = await Assert.ThrowsAsync<CodexProtocolException>(() =>
            client.ReadAccountStatusAsync(CancellationToken.None));

        Assert.Equal("Codex app-server returned an unsupported account response.", error.Message);
        Assert.DoesNotContain(privateValue, error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AccountReadRequiresACompletedHandshake()
    {
        using var peer = new ScriptedCodexJsonlPeer();
        await using CodexAppServerClient client = peer.CreateClient();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.ReadAccountStatusAsync(CancellationToken.None));
    }
}

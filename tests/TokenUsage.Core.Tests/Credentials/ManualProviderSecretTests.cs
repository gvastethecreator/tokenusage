using TokenUsage.Core.Credentials;

namespace TokenUsage.Core.Tests.Credentials;

public sealed class ManualProviderSecretTests
{
    [Fact]
    public void RejectsBlankOrOversizedValuesWithoutEchoingTheSecret()
    {
        const string apiKey = "private-api-key";
        ArgumentException blank = Assert.Throws<ArgumentException>(() => new ManualProviderSecret(" "));
        ArgumentException oversized = Assert.Throws<ArgumentException>(() =>
            new ManualProviderSecret(new string('k', ManualProviderSecret.MaximumApiKeyLength + 1)));
        ArgumentException secondary = Assert.Throws<ArgumentException>(() =>
            new ManualProviderSecret(apiKey, " "));

        Assert.DoesNotContain(apiKey, blank.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(
            new string('k', ManualProviderSecret.MaximumApiKeyLength),
            oversized.Message,
            StringComparison.Ordinal);
        Assert.Equal(apiKey, new ManualProviderSecret(apiKey, "org_1").ApiKey);
        Assert.Equal("org_1", new ManualProviderSecret(apiKey, "org_1").SecondaryValue);
        Assert.Null(new ManualProviderSecret(apiKey).SecondaryValue);
        Assert.Contains("additional connection value", secondary.Message, StringComparison.Ordinal);
    }
}

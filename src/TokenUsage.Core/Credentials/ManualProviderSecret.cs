namespace TokenUsage.Core.Credentials;

public sealed record ManualProviderSecret
{
    public const int MaximumApiKeyLength = 8192;
    public const int MaximumSecondaryValueLength = 1024;

    public ManualProviderSecret(string apiKey, string? secondaryValue = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        if (apiKey.Length > MaximumApiKeyLength)
        {
            throw new ArgumentException("The API key is too long.", nameof(apiKey));
        }

        if (secondaryValue is not null)
        {
            if (string.IsNullOrWhiteSpace(secondaryValue)
                || secondaryValue.Length > MaximumSecondaryValueLength)
            {
                throw new ArgumentException(
                    "The additional connection value is invalid.",
                    nameof(secondaryValue));
            }
        }

        ApiKey = apiKey;
        SecondaryValue = secondaryValue;
    }

    public string ApiKey { get; }

    public string? SecondaryValue { get; }
}

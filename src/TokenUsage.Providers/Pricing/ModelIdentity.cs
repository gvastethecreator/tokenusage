using System.Text;
using TokenUsage.Core.Usage;

namespace TokenUsage.Providers.Pricing;

/// <summary>
/// Turns a provider's raw model string into a stable, readable id. Illegal
/// characters become a hyphen instead of a hashed <c>unknown-*</c> placeholder,
/// so a name the source already had still groups and prices.
/// </summary>
public static class ModelIdentity
{
    public const string Unknown = "unknown";

    public static string Sanitize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Unknown;
        }

        var builder = new StringBuilder(raw.Length);
        bool previousWasSeparator = false;
        foreach (char character in raw.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                builder.Append(character);
                previousWasSeparator = false;
                continue;
            }

            if ((character is '.' or '-' || !previousWasSeparator)
                && builder.Length > 0
                && !previousWasSeparator)
            {
                builder.Append(character is '.' ? '.' : '-');
                previousWasSeparator = true;
            }
        }

        string sanitized = builder.ToString().Trim('-', '.');
        return string.IsNullOrWhiteSpace(sanitized) ? Unknown : sanitized;
    }

    public static string ForStorage(string? raw)
    {
        string sanitized = Sanitize(raw);
        return sanitized == Unknown
            ? Unknown
            : KnownModelPricingCatalog.Canonicalize(sanitized);
    }

    public static ModelId ToModelId(string? raw) => new(ForStorage(raw));

    public static ModelProviderId? TryProviderId(string? raw)
    {
        string sanitized = Sanitize(raw);
        return sanitized == Unknown ? null : new ModelProviderId(sanitized);
    }
}

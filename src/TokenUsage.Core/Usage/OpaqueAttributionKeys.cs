using System.Security.Cryptography;
using System.Text;

namespace TokenUsage.Core.Usage;

public static class OpaqueKeyDomains
{
    public const string CodexSession = "codex-session/v1";
    public const string CodexParent = CodexSession;
    public const string CodexProject = "codex-project/v1";
    public const string CursorSession = "cursor-session/v1";
    public const string CodexMcp = "codex-mcp/v1";
    public const string CodexSkills = "codex-skills/v1";
    public const string CodexCommands = "codex-commands/v1";
    public const string CodexFiles = "codex-files/v1";
    public const string LegacyUnnamedSource = "codex";
}

public sealed record OpaqueAttributionKey
{
    public OpaqueAttributionKey(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!IsHexSha256(value))
        {
            throw new ArgumentException(
                "Opaque attribution keys must be lowercase SHA-256 hex values.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public string ShortLabel => Value[^4..];

    public override string ToString() => Value;

    public static bool IsHexSha256(string value) =>
        value.Length == 64
        && value.All(character => character is (>= '0' and <= '9') or (>= 'a' and <= 'f'));
}

public sealed record UsageSessionLink
{
    public UsageSessionLink(
        UsageEventKey eventKey,
        OpaqueAttributionKey sessionKey,
        OpaqueAttributionKey? parentSessionKey,
        long consentEpoch,
        AttributionCapability? capability = null)
    {
        ArgumentNullException.ThrowIfNull(eventKey);
        ArgumentNullException.ThrowIfNull(sessionKey);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(consentEpoch);
        EventKey = eventKey;
        SessionKey = sessionKey;
        ParentSessionKey = parentSessionKey;
        ConsentEpoch = consentEpoch;
        Capability = capability ?? AttributionCapability.CodexSession;
    }

    public UsageEventKey EventKey { get; }

    public OpaqueAttributionKey SessionKey { get; }

    public OpaqueAttributionKey? ParentSessionKey { get; }

    public long ConsentEpoch { get; }

    public AttributionCapability Capability { get; }
}

public interface IOpaqueKeyDeriver
{
    OpaqueAttributionKey Derive(string domain, string source, string identifier);
}

public sealed class HmacOpaqueKeyDeriver : IOpaqueKeyDeriver
{
    private readonly byte[] _key;

    public HmacOpaqueKeyDeriver(byte[] keyMaterial)
    {
        ArgumentNullException.ThrowIfNull(keyMaterial);
        if (keyMaterial.Length < 32)
        {
            throw new ArgumentException("Opaque key material must be at least 32 bytes.", nameof(keyMaterial));
        }

        _key = keyMaterial.ToArray();
    }

    public OpaqueAttributionKey Derive(string domain, string source, string identifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        int length = checked(Encoding.UTF8.GetByteCount(domain)
            + 1
            + Encoding.UTF8.GetByteCount(source)
            + 1
            + Encoding.UTF8.GetByteCount(identifier));
        Span<byte> payload = length <= 512 ? stackalloc byte[length] : new byte[length];
        int written = Encoding.UTF8.GetBytes(domain, payload);
        payload[written] = 0x1F;
        written++;
        written += Encoding.UTF8.GetBytes(source, payload[written..]);
        payload[written] = 0x1F;
        written++;
        Encoding.UTF8.GetBytes(identifier, payload[written..]);
        byte[] hash = HMACSHA256.HashData(_key, payload);
        return new OpaqueAttributionKey(Convert.ToHexString(hash).ToLowerInvariant());
    }
}

public static class OpaqueNativeId
{
    public static bool TryNormalize(string? value, out string identifier)
    {
        identifier = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string trimmed = value.Trim();
        if (trimmed.Length is 0 or > 128)
        {
            return false;
        }

        foreach (char character in trimmed)
        {
            if (char.IsControl(character)
                || character is '/' or '\\' or ':' or '<' or '>' or '"' or '|' or '?' or '*')
            {
                return false;
            }
        }

        identifier = trimmed;
        return true;
    }
}

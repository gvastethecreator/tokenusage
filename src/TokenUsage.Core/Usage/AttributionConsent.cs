namespace TokenUsage.Core.Usage;

public enum AttributionConsentState
{
    Disabled = 0,
    Enabled = 1,
    PurgePending = 2,
    DisabledAfterPurge = 3,
}

public readonly record struct AttributionCapability
{
    public AttributionCapability(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > 64
            || value.Any(character => character is not (>= 'a' and <= 'z')
                and not (>= '0' and <= '9')
                and not '-'))
        {
            throw new ArgumentException(
                "Attribution capabilities must be lowercase tokens.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public static AttributionCapability CodexSession { get; } = new("codex-session");

    public static AttributionCapability CodexProject { get; } = new("codex-project");

    public static AttributionCapability CursorSession { get; } = new("cursor-session");

    public static AttributionCapability CodexMcp { get; } = new("codex-mcp");

    public static AttributionCapability CodexSkills { get; } = new("codex-skills");

    public static AttributionCapability CodexCommands { get; } = new("codex-commands");

    public static AttributionCapability CodexFiles { get; } = new("codex-files");

    public override string ToString() => Value;
}

public sealed record AttributionConsent(
    AttributionCapability Capability,
    AttributionConsentState State,
    long Epoch,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? EnabledAtUtc = null)
{
    public bool AllowsLinks => State == AttributionConsentState.Enabled && Epoch > 0;

    public bool AcceptsEpoch(long epoch) => AllowsLinks && Epoch == epoch;

    public long ActiveLinkEpoch => AllowsLinks ? Epoch : 0;
}

public interface IAttributionConsentSource
{
    Task<AttributionConsent> LoadAsync(
        AttributionCapability capability,
        CancellationToken cancellationToken = default);
}

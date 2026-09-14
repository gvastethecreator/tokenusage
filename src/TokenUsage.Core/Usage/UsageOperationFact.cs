namespace TokenUsage.Core.Usage;

public enum UsageOperationKind
{
    Tool = 0,
    Mcp = 1,
    Skill = 2,
    Spawn = 3,
    Command = 4,
    File = 5,
}

public enum UsageOperationOutcome
{
    Unknown = 0,
    Success = 1,
    Error = 2,
}

public static class UsageOperationKindCodec
{
    public static string ToWire(UsageOperationKind kind) => kind switch
    {
        UsageOperationKind.Tool => "tool",
        UsageOperationKind.Mcp => "mcp",
        UsageOperationKind.Skill => "skill",
        UsageOperationKind.Spawn => "spawn",
        UsageOperationKind.Command => "command",
        UsageOperationKind.File => "file",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static bool TryParse(string? value, out UsageOperationKind kind)
    {
        kind = value switch
        {
            "tool" => UsageOperationKind.Tool,
            "mcp" => UsageOperationKind.Mcp,
            "skill" => UsageOperationKind.Skill,
            "spawn" => UsageOperationKind.Spawn,
            "command" => UsageOperationKind.Command,
            "file" => UsageOperationKind.File,
            _ => (UsageOperationKind)(-1),
        };
        return kind is >= UsageOperationKind.Tool and <= UsageOperationKind.File;
    }
}

public static class UsageOperationOutcomeCodec
{
    public static string ToWire(UsageOperationOutcome outcome) => outcome switch
    {
        UsageOperationOutcome.Unknown => "unknown",
        UsageOperationOutcome.Success => "success",
        UsageOperationOutcome.Error => "error",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
    };

    public static bool TryParse(string? value, out UsageOperationOutcome outcome)
    {
        outcome = value switch
        {
            "unknown" => UsageOperationOutcome.Unknown,
            "success" => UsageOperationOutcome.Success,
            "error" => UsageOperationOutcome.Error,
            _ => (UsageOperationOutcome)(-1),
        };
        return outcome is >= UsageOperationOutcome.Unknown and <= UsageOperationOutcome.Error;
    }
}

public static class BoundedOperationLabel
{
    public const int MaxLength = 64;

    public static bool TryNormalize(string? value, out string label)
    {
        label = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string trimmed = value.Trim();
        if (trimmed.Length is 0 or > MaxLength)
        {
            return false;
        }

        foreach (char character in trimmed)
        {
            if (char.IsAsciiLetterOrDigit(character)
                || character is '-' or '_' or '.')
            {
                continue;
            }

            return false;
        }

        label = trimmed;
        return true;
    }
}

public static class CodexAdmittedDynamicTools
{
    public static bool IsAllowlisted(string tool) => tool is "Read" or "Edit";
}

public static class CodexCommandFamily
{
    public static string Classify(IReadOnlyList<string>? command)
    {
        if (command is null || command.Count == 0)
        {
            return "unknown";
        }

        string first = PathToken(command[0]);
        if (first is "git" or "dotnet" or "pnpm" or "npm" or "cargo")
        {
            if (first is "dotnet" or "cargo" or "npm" or "pnpm"
                && command.Count > 1
                && PathToken(command[1]) is "test")
            {
                return "test";
            }

            return first;
        }

        if (first is "rg" or "grep" or "findstr" or "ag" or "ack")
        {
            return "search";
        }

        if (first is "pytest" or "vstest.console")
        {
            return "test";
        }

        return "unknown";
    }

    private static string PathToken(string value)
    {
        string trimmed = value.Trim().Trim('"');
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        int separator = Math.Max(trimmed.LastIndexOf('\\'), trimmed.LastIndexOf('/'));
        string name = separator >= 0 ? trimmed[(separator + 1)..] : trimmed;
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
        {
            name = Path.GetFileNameWithoutExtension(name);
        }

        return name.ToLowerInvariant();
    }
}

public sealed record UsageOperationFact
{
    public UsageOperationFact(
        OpaqueAttributionKey operationKey,
        AttributionCapability capability,
        long consentEpoch,
        UsageOperationKind kind,
        string tool,
        string? server,
        UsageOperationOutcome outcome,
        DateTimeOffset startedAtUtc,
        DateTimeOffset? endedAtUtc,
        OpaqueAttributionKey? sessionKey,
        int quantity = 1)
    {
        ArgumentNullException.ThrowIfNull(operationKey);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(consentEpoch);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        if (!BoundedOperationLabel.TryNormalize(tool, out string boundedTool))
        {
            throw new ArgumentException("Operation tool labels must be bounded.", nameof(tool));
        }

        string? boundedServer = null;
        if (server is not null)
        {
            if (!BoundedOperationLabel.TryNormalize(server, out string normalizedServer))
            {
                throw new ArgumentException("Operation server labels must be bounded.", nameof(server));
            }

            boundedServer = normalizedServer;
        }

        OperationKey = operationKey;
        Capability = capability;
        ConsentEpoch = consentEpoch;
        Kind = kind;
        Tool = boundedTool;
        Server = boundedServer;
        Outcome = outcome;
        StartedAtUtc = startedAtUtc;
        EndedAtUtc = endedAtUtc;
        SessionKey = sessionKey;
        Quantity = quantity;
    }

    public OpaqueAttributionKey OperationKey { get; init; }
    public AttributionCapability Capability { get; init; }
    public long ConsentEpoch { get; init; }
    public UsageOperationKind Kind { get; init; }
    public string Tool { get; init; }
    public string? Server { get; init; }
    public UsageOperationOutcome Outcome { get; init; }
    public DateTimeOffset StartedAtUtc { get; init; }
    public DateTimeOffset? EndedAtUtc { get; init; }
    public OpaqueAttributionKey? SessionKey { get; init; }
    public int Quantity { get; init; }
}

public sealed record UsageOperationRankedRow(
    string Id,
    UsageOperationKind Kind,
    string Tool,
    string? Server,
    int InvocationCount,
    int SuccessCount,
    int ErrorCount,
    int UnknownCount,
    bool OutcomesAvailable,
    OpaqueAttributionKey? SessionKey = null);

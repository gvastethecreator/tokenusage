namespace TokenUsage.Core.Usage;

public enum UsageDerivedActivityCategory
{
    Edit,
    Read,
    Test,
    Delegate,
    Unknown,
}

public sealed record UsageDerivedActivitySummary(
    string MethodVersion,
    int Eligible,
    int Edit,
    int Read,
    int Test,
    int Delegate,
    int Unknown,
    string CostAvailability)
{
    public static UsageDerivedActivitySummary Empty { get; } = new(
        UsageDerivedActivity.MethodVersion,
        0,
        0,
        0,
        0,
        0,
        0,
        UsageDerivedActivity.CostUnavailable);
}

public static class UsageDerivedActivity
{
    public const string MethodVersion = "derived-activity/v1";
    public const string CostUnavailable = "unavailable";
    public const string CountUnit = "invocations";

    public static UsageDerivedActivityCategory Classify(UsageOperationKind kind, string tool)
    {
        if (kind == UsageOperationKind.File
            || (kind == UsageOperationKind.Tool && string.Equals(tool, "Edit", StringComparison.Ordinal)))
        {
            return UsageDerivedActivityCategory.Edit;
        }

        if ((kind == UsageOperationKind.Tool && string.Equals(tool, "Read", StringComparison.Ordinal))
            || (kind == UsageOperationKind.Command && string.Equals(tool, "search", StringComparison.Ordinal)))
        {
            return UsageDerivedActivityCategory.Read;
        }

        if (kind == UsageOperationKind.Command && string.Equals(tool, "test", StringComparison.Ordinal))
        {
            return UsageDerivedActivityCategory.Test;
        }

        if (kind == UsageOperationKind.Spawn)
        {
            return UsageDerivedActivityCategory.Delegate;
        }

        return UsageDerivedActivityCategory.Unknown;
    }

    public static string ToWire(UsageDerivedActivityCategory category) => category switch
    {
        UsageDerivedActivityCategory.Edit => "edit",
        UsageDerivedActivityCategory.Read => "read",
        UsageDerivedActivityCategory.Test => "test",
        UsageDerivedActivityCategory.Delegate => "delegate",
        _ => "unknown",
    };

    public static bool TryParse(string? value, out UsageDerivedActivityCategory category)
    {
        category = value switch
        {
            "edit" => UsageDerivedActivityCategory.Edit,
            "read" => UsageDerivedActivityCategory.Read,
            "test" => UsageDerivedActivityCategory.Test,
            "delegate" => UsageDerivedActivityCategory.Delegate,
            "unknown" => UsageDerivedActivityCategory.Unknown,
            _ => (UsageDerivedActivityCategory)(-1),
        };
        return category is >= UsageDerivedActivityCategory.Edit and <= UsageDerivedActivityCategory.Unknown;
    }

    public static UsageDerivedActivitySummary Summarize(
        IEnumerable<(UsageOperationKind Kind, string Tool, int Count)> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        int edit = 0;
        int read = 0;
        int test = 0;
        int delegateCount = 0;
        int unknown = 0;
        foreach ((UsageOperationKind kind, string tool, int count) in rows)
        {
            if (count <= 0)
            {
                continue;
            }

            switch (Classify(kind, tool))
            {
                case UsageDerivedActivityCategory.Edit:
                    edit += count;
                    break;
                case UsageDerivedActivityCategory.Read:
                    read += count;
                    break;
                case UsageDerivedActivityCategory.Test:
                    test += count;
                    break;
                case UsageDerivedActivityCategory.Delegate:
                    delegateCount += count;
                    break;
                default:
                    unknown += count;
                    break;
            }
        }

        return new UsageDerivedActivitySummary(
            MethodVersion,
            edit + read + test + delegateCount + unknown,
            edit,
            read,
            test,
            delegateCount,
            unknown,
            CostUnavailable);
    }

    // Grain: ranking InvocationCount after GROUP BY kind, tool, server, not distinct operation_key.
    public static UsageDerivedActivitySummary Summarize(IEnumerable<UsageOperationRankedRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return Summarize(rows.Select(row => (row.Kind, row.Tool, row.InvocationCount)));
    }
}
